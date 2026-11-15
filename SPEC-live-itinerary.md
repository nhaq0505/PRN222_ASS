# Spec: `live-itinerary` — Collaborative Schedule, AI Trip Planner & Departure Reminders

## Assumptions

1. **Local Trip Timezone:** `TripDay.Date` (`DateOnly`) and `ItineraryItem.StartTime` (`TimeOnly?`) represent Vietnam local wall-clock time (`UTC+07:00`, Windows `"SE Asia Standard Time"` / IANA `"Asia/Ho_Chi_Minh"`). `DepartureReminderWorker` converts `DateTime.UtcNow` to `UTC+07:00` when evaluating the 30-minute lookahead window, while persisting `ReminderSentAtUtc` in UTC.
2. **Gemini Integration:** `Gemini:ApiKey` and `Gemini:Model` (`"gemini-2.5-flash"`) are read via `IOptions<GeminiOptions>`. Calls use `IHttpClientFactory` against the Gemini REST `generateContent` endpoint with structured JSON schema output (`responseMimeType = "application/json"`). When `ApiKey` is unconfigured, the service fails gracefully with an actionable Vietnamese validation error rather than throwing unhandled exceptions.
3. **Parallel Module Isolation:** All DI registrations for this module live in `ItineraryModuleExtensions.cs` (and a dedicated service-layer registration method) and SignalR hub methods live in `partial class TripHub` inside `src/SplitTrip.Web/Hubs/TripHub.Itinerary.cs`, avoiding edits to `Program.cs` or `TripHub.cs`.

## Objective

Enable active `TripMember`s to collaboratively plan, reorder, and vote on multi-day trip schedules (`TripDay`, `ItineraryItem`, `ItineraryVote`) in real time via `SignalR`, generate structured multi-day itinerary drafts with **Gemini 2.5 Flash**, and receive automated **30-minute departure reminders** via a `BackgroundService` worker (`DepartureReminderWorker`).

### Core Domain Rules & Invariants

1. **Strict 5-Layer Access:**
   - `SplitTrip.Web` (`Pages/Trips/Itinerary.cshtml*`, `Hubs/TripHub.Itinerary.cs`, `Workers/DepartureReminderWorker.cs`) depends **only** on `SplitTrip.Services` and `SplitTrip.BusinessObjects`.
   - `Services` → `Repositories` → `DataAccessObjects` → `BusinessObjects`.
2. **Membership & Trip Lifecycle Gate:**
   - Only active `TripMember`s (`IsActive == true`) of `TripId` may view or interact with the itinerary.
   - Schedule mutations (add/edit/delete/reorder/vote/AI-apply) are permitted only when `Trip.Status` is `TripStatus.Planning` or `TripStatus.Ongoing`. In `Settling` or `Completed`, the itinerary renders in read-only mode.
3. **Day & Stop Validation (`TripDay` & `ItineraryItem`):**
   - `TripDay` rows (`DayNumber = 1..N`, unique `(TripId, DayNumber)`) are seeded from `Trip.StartDate..Trip.EndDate`; day `Title` and `Notes` are editable.
   - `ItineraryItem.Title` is required (`1..200` chars). `EstimatedCost` uses `decimal(18, 0)` whole `VND` and must satisfy `EstimatedCost >= 0`.
   - When both `StartTime` and `EndTime` are specified, `EndTime > StartTime` is enforced.
   - Drag-and-drop reordering (`ReorderItemsAsync`) supports both within-day reordering and moving an item across `TripDay`s within the same `Trip`, re-sequencing `SortOrder` contiguously (`0, 1, 2, ...`).
   - Updating an item's `TripDayId` or `StartTime` to a new future time resets `ReminderSentAtUtc = null` so the new departure window can trigger a fresh reminder.
4. **Stop Voting (`ItineraryVote`):**
   - Unique constraint `(ItineraryItemId, UserId)` with `VoteType` (`Upvote = 1`, `Downvote = -1`).
   - **Toggle / Flip behavior:** Submitting the identical `VoteType` removes the user's existing vote; submitting the opposite `VoteType` updates the existing row in place.
   - Item score summary exposes `Upvotes`, `Downvotes`, `NetScore = Upvotes - Downvotes`, and `CurrentUserVote`.
5. **Gemini 2.5 Flash Structured JSON Planner (`IAiItineraryService`):**
   - Sends `Destination`, `StartDate..EndDate` (`DayCount`), `EstimatedBudgetPerPerson`, active member count, and user travel preferences to Gemini `2.5-flash` with `responseMimeType: "application/json"` and an explicit JSON schema:
     - `days[]`: `{ dayNumber, title, notes, items: [{ title, locationName, address, startTime, endTime, estimatedCost, notes }] }`.
   - Parses and validates every generated item (`dayNumber` within `1..DayCount`, valid `"HH:mm"` `TimeOnly`, rounded non-negative integer `VND` `EstimatedCost`), stamps `IsAiSuggested = true`, appends after existing `SortOrder` on each `TripDay`, and broadcasts updates to `trip:{tripId}`.
6. **Real-Time Collaboration (`TripHub.Itinerary.cs`):**
   - `public partial class TripHub` adds itinerary hub operations / server broadcasts scoped to SignalR group `trip:{tripId}`:
     - `ItineraryItemAdded`, `ItineraryItemUpdated`, `ItineraryItemDeleted`, `ItineraryReordered`, `ItineraryVoteUpdated`, `ItineraryDayUpdated`, and `AiItineraryGenerated`.
   - Every hub invocation verifies caller authentication and active `TripMember` membership before persisting or broadcasting.
7. **30-Minute Departure Reminder (`DepartureReminderWorker`):**
   - Runs as a `BackgroundService` on a 60-second `PeriodicTimer` creating a fresh DI scope per tick.
   - Queries `ItineraryItem`s where:
     - `Trip.Status == TripStatus.Ongoing` (or `Planning` on the active travel date),
     - `StartTime != null` and `ReminderSentAtUtc == null`,
     - Local scheduled timestamp `(TripDay.Date + StartTime)` falls within `[nowLocal, nowLocal + 30 minutes]`.
   - Stamps `ReminderSentAtUtc = DateTime.UtcNow` and inserts `Notification` rows (`Type = NotificationType.DepartureReminder`, `ActionUrl = $"/Trips/Itinerary?tripId={tripId}#item-{item.Id}"`) for all active `TripMember`s in the same unit of work, then pushes `ReceiveNotification` to `user:{userId}` and `DepartureReminderTriggered` to `trip:{tripId}`.

## Tech Stack

- **Runtime & Web:** `.NET 10.0` (`net10.0`), `ASP.NET Core Razor Pages`, `ASP.NET Core SignalR`
- **Data Access:** `EF Core 10 (SQL Server)` via `ItineraryDao` (`SplitTrip.DataAccessObjects`) and `IItineraryRepository` (`SplitTrip.Repositories`)
- **AI Provider:** Google Gemini `gemini-2.5-flash` REST API (`IHttpClientFactory` + `System.Text.Json`)
- **Background Processing:** `Microsoft.Extensions.Hosting.BackgroundService` (`PeriodicTimer`)
- **Frontend Drag & Drop:** Native HTML5 Drag-and-Drop / SortableJS + SignalR JS client

## Commands

```powershell
# Build entire solution
dotnet build SplitTrip.sln --configuration Debug

# Run itinerary unit tests
dotnet test SplitTrip.sln --no-build --filter "FullyQualifiedName~Itinerary"

# Run full test suite
dotnet test SplitTrip.sln --no-build --verbosity normal

# Run Web app (with Gemini key via environment or user-secrets)
dotnet user-secrets set "Gemini:ApiKey" "<YOUR_API_KEY>" --project src/SplitTrip.Web
dotnet run --project src/SplitTrip.Web
```

## Project Structure

```text
src/
├── SplitTrip.DataAccessObjects/
│   └── Daos/
│       └── ItineraryDao.cs                       → EF Core queries for TripDay, ItineraryItem, ItineraryVote, upcoming reminders
├── SplitTrip.Repositories/
│   └── Itinerary/
│       ├── IItineraryRepository.cs               → Repository contract for schedule CRUD, reorder, voting, reminder batching
│       └── ItineraryRepository.cs                → Repository implementation wrapping ItineraryDao
├── SplitTrip.Services/
│   └── Itinerary/
│       ├── Dtos/ItineraryDtos.cs                 → TripScheduleDto, TripDayDto, ItineraryItemDto, VoteResultDto, AiItineraryRequestDto
│       ├── IItineraryService.cs                  → Schedule CRUD, drag-and-drop reorder, voting, 30-min reminder processing
│       ├── ItineraryService.cs                   → Domain validation & invariant enforcement
│       ├── IAiItineraryService.cs                → Gemini 2.5 Flash structured JSON itinerary generator contract
│       └── GeminiItineraryService.cs             → HttpClient Gemini integration + JSON schema validation
└── SplitTrip.Web/
    ├── Extensions/
    │   └── ItineraryModuleExtensions.cs          → Registers Itinerary DAOs/Repos/Services, HttpClient, DepartureReminderWorker
    ├── Hubs/
    │   └── TripHub.Itinerary.cs                  → partial class TripHub real-time itinerary & vote methods
    ├── Workers/
    │   └── DepartureReminderWorker.cs            → 60s BackgroundService with 30-min ReminderSentAtUtc lookahead
    └── Pages/Trips/
        ├── Itinerary.cshtml                      → Multi-day timeline UI, drag-and-drop cards, vote buttons, AI planner modal
        └── Itinerary.cshtml.cs                   → Razor PageModel + JSON/AJAX handlers calling IItineraryService & IAiItineraryService
tests/
└── SplitTrip.Tests/
    └── Itinerary/
        ├── ItineraryServiceTests.cs              → Validation, cross-day reorder, vote toggle/flip, lifecycle lock tests
        ├── GeminiItineraryServiceTests.cs        → Structured JSON parsing, invalid schema rejection, missing API key handling
        └── DepartureReminderWorkerTests.cs       → 30-min window matching, ReminderSentAtUtc idempotency, reschedule reset tests
```

## Code Style

```csharp
namespace SplitTrip.Web.Hubs;

[Authorize]
public partial class TripHub
{
    public async Task ToggleItineraryVote(
        Guid tripId,
        Guid itemId,
        VoteType voteType,
        [FromServices] IItineraryService itineraryService)
    {
        var userId = GetAuthenticatedUserId();
        var result = await itineraryService.ToggleVoteAsync(tripId, itemId, userId, voteType, Context.ConnectionAborted);

        await Clients.Group($"trip:{tripId}").SendAsync(
            "ItineraryVoteUpdated",
            result,
            Context.ConnectionAborted);
    }
}
```

## Testing Strategy

- **Service & Domain Unit Tests (`tests/SplitTrip.Tests/Itinerary/`):**
  - Adding/updating items validates `EndTime > StartTime`, `EstimatedCost >= 0`, active `TripMember` authorization, and blocks mutations when `Trip.Status` is `Settling` or `Completed`.
  - Drag-and-drop reordering across days normalizes `SortOrder` (`0..n-1`) and resets `ReminderSentAtUtc` when moved to a future slot.
  - Voting verifies insert, toggle-off on duplicate vote, and flip (`Upvote` ↔ `Downvote`) while maintaining unique `(ItineraryItemId, UserId)`.
  - Gemini service tests use a mocked `HttpMessageHandler` to verify structured JSON parsing, `IsAiSuggested = true` tagging, whole-VND rounding, and graceful error handling on HTTP 429/500 or missing `ApiKey`.
  - Reminder lookahead tests inject a deterministic clock (`TimeProvider` or explicit `utcNow` parameter) to prove stops at `+25 min` trigger once (`ReminderSentAtUtc != null`), stops at `+45 min` or `-5 min` do not trigger, and subsequent worker ticks never duplicate notifications.

## Boundaries

- **Always:**
  - Keep `TripHub` extensions strictly inside `src/SplitTrip.Web/Hubs/TripHub.Itinerary.cs` (`public partial class TripHub`) and module DI wiring inside `src/SplitTrip.Web/Extensions/ItineraryModuleExtensions.cs`.
  - Broadcast schedule mutations via `IHubContext<TripHub>` / `TripHub` to `trip:{tripId}` so all open tabs stay synchronized without page reloads.
  - Stamp `ReminderSentAtUtc` atomically when persisting `DepartureReminder` notifications.
- **Ask first:**
  - Changing `TripDay`, `ItineraryItem`, or `ItineraryVote` columns in `SplitTrip.BusinessObjects` after `InitialCreate` migration.
  - Switching AI model defaults away from `gemini-2.5-flash`.
- **Never:**
  - Reference `SplitTrip.DataAccessObjects` from `SplitTrip.Web`.
  - Edit `Program.cs` or `TripHub.cs` directly during Wave 2 parallel execution.
  - Hardcode Gemini API keys in source files or tracked `appsettings.json`.

## Success Criteria

- [ ] Active trip members can view multi-day tabs (`Day 1..N`), create/edit/delete stops with time ranges, locations, and whole-VND estimated costs, and see updates live across two browser sessions via `SignalR`.
- [ ] Drag-and-drop reordering within a day and across days persists normalized `SortOrder` values and updates connected members in real time.
- [ ] Upvote/downvote toggle & flip logic enforces one vote per member per item and broadcasts live score badges (`Upvotes`, `Downvotes`, `NetScore`).
- [ ] AI Trip Planner modal generates structured multi-day suggestions from `Gemini 2.5 Flash`, marks imported stops with `IsAiSuggested = true`, and degrades gracefully when `Gemini:ApiKey` is absent.
- [ ] `DepartureReminderWorker` dispatches `NotificationType.DepartureReminder` notifications + live SignalR alerts for stops departing within 30 minutes and stamps `ReminderSentAtUtc` to guarantee exactly-once delivery per scheduled slot.
- [ ] All unit tests in `tests/SplitTrip.Tests/Itinerary/` pass (`dotnet test SplitTrip.sln`).
