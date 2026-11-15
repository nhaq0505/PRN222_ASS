# Task Breakdown: SplitTrip

> Tracks atomic, dependency-ordered vertical slices across Wave 1 (`foundation`), Wave 2 parallel feature streams (`live-itinerary`, `smart-expenses`, `debt-settlement`), and Wave 3 integration verification.

---

## Wave 1: `foundation` (`SPEC-foundation.md`)

### Task T01: Scaffold 5-Layer `.NET 10.0` Solution, Domain Enums & Option Models
**Description:** Create `SplitTrip.sln` with strict 5-layer project references (`BusinessObjects`, `DataAccessObjects`, `Repositories`, `Services`, `Web`) plus `tests/SplitTrip.Tests` on `net10.0`, and define all domain enums and strongly-typed configuration option models.

**Acceptance criteria:**
- [x] `SplitTrip.Web` references only `SplitTrip.Services` and `SplitTrip.BusinessObjects` (no `DataAccessObjects` reference).
- [x] All 8 enums (`TripStatus`, `TripMemberRole`, `ExpenseCategory`, `ExpenseSplitMode`, `ReceiptScanStatus`, `SettlementStatus`, `VoteType`, `NotificationType`) and option models (`AdminAccountOptions`, `GeminiOptions`) exist in `SplitTrip.BusinessObjects`.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** None

**Files touched:**
- `SplitTrip.sln`
- `src/SplitTrip.BusinessObjects/Enums/*`
- `src/SplitTrip.BusinessObjects/Options/*`
- `src/*/*.csproj`, `tests/SplitTrip.Tests/SplitTrip.Tests.csproj`

**Estimated scope:** Medium (project files + enums/options)

---

### Task T02: Implement Complete Domain Entities (`BusinessObjects`)
**Description:** Define POCOs and navigation properties for `User`, `Trip`, `TripMember`, `TripDay`, `ItineraryItem`, `ItineraryVote`, `Expense`, `ExpenseSplit`, `ReceiptItem`, `ReceiptItemClaim`, `Settlement`, and `Notification` with zero EF/Web package dependencies.

**Acceptance criteria:**
- [x] All 12 entities match exact property names, nullability, and types (`DateOnly`, `TimeOnly`, `decimal`, `Guid`) required by all four module specs.
- [x] `SplitTrip.BusinessObjects.csproj` has zero NuGet package dependencies.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T01

**Files touched:**
- `src/SplitTrip.BusinessObjects/Entities/*.cs`

**Estimated scope:** Medium (entity classes)

---

### Task T03: Implement `SplitTripDbContext`, `InitialCreate` Migration & SQL Export
**Description:** Configure `SplitTripDbContext` and `SplitTripDbContextFactory` with `decimal(18,0)` money precision (`decimal(18,4)` on `ShareRatioOrWeight`), composite unique indexes, and `DeleteBehavior.Restrict` on all `User` FKs, apply `InitialCreate` on LocalDB, and export `database/SplitTrip_Schema.sql`.

**Acceptance criteria:**
- [x] Unique indexes configured on `User.Email`, `Trip.InviteCode`, `(TripId, UserId)`, `(TripId, DayNumber)`, `(ItineraryItemId, UserId)`, `(ExpenseId, UserId)`, and `(ReceiptItemId, UserId)`.
- [x] `dotnet ef database update` succeeds on `(localdb)\mssqllocaldb` with zero cascade-path errors and generates idempotent `database/SplitTrip_Schema.sql`.

**Verification:**
- [x] Migration update: `dotnet ef database update --project src/SplitTrip.DataAccessObjects --startup-project src/SplitTrip.Web`
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T02

**Files touched:**
- `src/SplitTrip.DataAccessObjects/SplitTripDbContext.cs`
- `src/SplitTrip.DataAccessObjects/SplitTripDbContextFactory.cs`
- `src/SplitTrip.DataAccessObjects/Migrations/*`
- `database/SplitTrip_Schema.sql`

**Estimated scope:** Medium (4 files/folders)

---

### Task T04: Implement Foundation DAOs and Repositories
**Description:** Implement async EF Core DAOs (`UserDao`, `TripDao`, `TripMemberDao`, `NotificationDao`) and corresponding repository contracts and implementations (`IUserRepository`, `ITripRepository`, `ITripMemberRepository`, `INotificationRepository`).

**Acceptance criteria:**
- [x] DAOs encapsulate all `SplitTripDbContext` queries for users, trips, members, ledger aggregates, and notifications.
- [x] Repositories expose clean async interfaces with `CancellationToken` support without leaking `DbContext`.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T03

**Files touched:**
- `src/SplitTrip.DataAccessObjects/Daos/UserDao.cs`
- `src/SplitTrip.DataAccessObjects/Daos/TripDao.cs`
- `src/SplitTrip.DataAccessObjects/Daos/TripMemberDao.cs`
- `src/SplitTrip.DataAccessObjects/Daos/NotificationDao.cs`
- `src/SplitTrip.Repositories/*`

**Estimated scope:** Medium (DAOs + repository pairs)

---

### Task T05: Implement `AuthService`, `TripService` & Foundation DI Registration
**Description:** Implement `AuthService` (BCrypt registration/login, `appsettings.json` System Admin login with `Role = "Admin"`, VietQR bank profile update) and `TripService` (trip creation with `TripDay` seeding, 8-char `InviteCode` join/reactivate, lifecycle transitions, zero-sum `GetTripMemberBalancesAsync` excluding `DraftReview`, and `NetBalance == 0` leave/kick guard).

**Acceptance criteria:**
- [x] `GetTripMemberBalancesAsync` computes `NetBalance = TotalPaidFinalizedExpenses - TotalOwedSplits + ConfirmedSettlementsPaid - ConfirmedSettlementsReceived` and ignores `ReceiptScanStatus.DraftReview`.
- [x] `RemoveOrLeaveMemberAsync` rejects leaving/removal when `NetBalance != 0m` and soft-deactivates (`IsActive = false`) settled members with financial history.
- [x] `ServiceCollectionExtensions.AddSplitTripFoundation` registers `SplitTripDbContext`, foundation DAOs, repositories, and services inside `SplitTrip.Services`.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T04

**Files touched:**
- `src/SplitTrip.Services/Auth/*`
- `src/SplitTrip.Services/Trips/*`
- `src/SplitTrip.Services/Notifications/*`
- `src/SplitTrip.Services/ServiceCollectionExtensions.cs`

**Estimated scope:** Medium (4 service areas)

---

### Task T06: Add Foundation Unit Tests (`SplitTrip.Tests/Foundation`)
**Description:** Write xUnit unit tests covering user registration/BCrypt login, `appsettings.json` System Admin authentication, trip lifecycle transitions (`Planning → Ongoing → Settling → Completed` + Owner reopen), invite joining, and the `NetBalance != 0` member leave guard.

**Acceptance criteria:**
- [x] Valid and invalid `TripStatus` transitions are verified.
- [x] Leaving with non-zero `NetBalance` throws `InvalidOperationException`; leaving with `0` balance succeeds.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Foundation"`

**Dependencies:** T05

**Files touched:**
- `tests/SplitTrip.Tests/Foundation/*.cs`

**Estimated scope:** Small (2–3 test files)

---

### Task T07: Configure Cookie Auth, Partial `TripHub`, Wave 2 DI Hooks & Base Layout
**Description:** Implement `public partial class TripHub : Hub` (`trip:{tripId}` and `user:{userId}` groups), create `FoundationModuleExtensions.cs` plus Wave 2 stub files (`ItineraryModuleExtensions.cs`, `ExpensesModuleExtensions.cs`, `SettlementModuleExtensions.cs`) wired in `Program.cs`, configure `appsettings.json`, and build the shared travel UI layout (`_Layout.cshtml`, `site.css`).

**Acceptance criteria:**
- [x] `Program.cs` calls `AddFoundationModule`, `AddItineraryModule`, `AddExpensesModule`, and `AddSettlementModule` so Wave 2 workers never edit `Program.cs`.
- [x] `TripHub.cs` is declared `public partial class TripHub : Hub` and authenticates `JoinTripRoom` / `LeaveTripRoom`.
- [x] `_Layout.cshtml` includes notification badge support and conditional Admin navigation (`User.IsInRole("Admin")`).

**Verification:**
- [x] Build & tests pass: `dotnet build SplitTrip.sln && dotnet test SplitTrip.sln --no-build`

**Dependencies:** T05, T06

**Files touched:**
- `src/SplitTrip.Web/Program.cs`
- `src/SplitTrip.Web/Hubs/TripHub.cs`
- `src/SplitTrip.Web/Extensions/*ModuleExtensions.cs`
- `src/SplitTrip.Web/Pages/Shared/_Layout.cshtml`
- `src/SplitTrip.Web/wwwroot/css/site.css`

**Estimated scope:** Medium (5 files)

---

### Task T08: Build Auth, VietQR Bank Profile & Trip Workspace Razor Pages
**Description:** Build `/Index`, `/Account/Login`, `/Account/Register`, `/Account/Logout`, `/Account/Profile` (VN Napas bank selector + account fields + live VietQR preview), `/Trips/Create`, `/Trips/Join`, and `/Trips/Details` (header, copyable `InviteCode`, Owner lifecycle controls, `NetBalance` member table, leave/kick actions, module navigation tabs, and live SignalR updates).

**Acceptance criteria:**
- [x] Users can register, sign in, configure VietQR bank details, create/join trips via `InviteCode`, transition trip status, and observe live member/status updates over `SignalR`.
- [x] Sub-navigation links cleanly route to `/Trips/Itinerary`, `/Trips/Expenses`, and `/Trips/Settlement` with `tripId`.

**Verification:**
- [x] Build & tests pass: `dotnet build SplitTrip.sln && dotnet test SplitTrip.sln --no-build`

**Dependencies:** T07

**Files touched:**
- `src/SplitTrip.Web/Pages/Index.cshtml*`
- `src/SplitTrip.Web/Pages/Account/*.cshtml*`
- `src/SplitTrip.Web/Pages/Trips/Create.cshtml*`
- `src/SplitTrip.Web/Pages/Trips/Join.cshtml*`
- `src/SplitTrip.Web/Pages/Trips/Details.cshtml*`

**Estimated scope:** Medium (Auth & Trip Razor Pages)

---

## Checkpoint 1: Wave 1 Foundation Gate
- [x] `dotnet build SplitTrip.sln` succeeds with 0 errors.
- [x] `dotnet test SplitTrip.sln --no-build` passes 100%.
- [x] `SplitTrip.Web.csproj` has no reference to `SplitTrip.DataAccessObjects`.
- [x] `TripHub` is `partial` and all three Wave 2 `*ModuleExtensions.cs` stubs are wired in `Program.cs`.
- [x] `database/SplitTrip_Schema.sql` is exported and synced with `InitialCreate`.

---

## Wave 2A (Parallel Stream 1): `live-itinerary` (`SPEC-live-itinerary.md`)

### Task T09: Implement `ItineraryDao`, `ItineraryRepository` & `ItineraryService`
**Description:** Build data access, repository, and domain service logic for multi-day schedules (`TripDay`), itinerary stops (`ItineraryItem`), within-day and cross-day drag-and-drop `SortOrder` normalization, and `ItineraryVote` toggle/flip rules.

**Acceptance criteria:**
- [x] Enforces active `TripMember` authorization, `Planning`/`Ongoing` edit lock, `EstimatedCost >= 0` (whole VND), and `EndTime > StartTime`.
- [x] Cross-day or time reschedule resets `ReminderSentAtUtc = null`; voting toggles off identical votes and flips opposite votes under unique `(ItineraryItemId, UserId)`.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** Checkpoint 1

**Files touched:**
- `src/SplitTrip.DataAccessObjects/Daos/ItineraryDao.cs`
- `src/SplitTrip.Repositories/Itinerary/IItineraryRepository.cs`
- `src/SplitTrip.Repositories/Itinerary/ItineraryRepository.cs`
- `src/SplitTrip.Services/Itinerary/Dtos/ItineraryDtos.cs`
- `src/SplitTrip.Services/Itinerary/IItineraryService.cs`, `ItineraryService.cs`

**Estimated scope:** Medium (5 files)

---

### Task T10: Implement `GeminiItineraryService` (`gemini-2.5-flash` Structured JSON Planner)
**Description:** Implement `IAiItineraryService` calling Gemini `2.5-flash` via `HttpClient` with `responseMimeType = "application/json"` and strict JSON schema to generate multi-day Vietnamese travel stops tagged with `IsAiSuggested = true`.

**Acceptance criteria:**
- [x] Parses structured `days[].items[]` JSON, validates `dayNumber`, `TimeOnly`, and non-negative whole-VND `EstimatedCost`, and appends items cleanly to existing `TripDay`s.
- [x] Returns a clear, non-crashing Vietnamese validation error when `Gemini:ApiKey` is unconfigured or Gemini returns non-200 status.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T09

**Files touched:**
- `src/SplitTrip.Services/Itinerary/IAiItineraryService.cs`
- `src/SplitTrip.Services/Itinerary/GeminiItineraryService.cs`

**Estimated scope:** Small (2 files)

---

### Task T11: Implement `DepartureReminderWorker` (30-Minute Lookahead `BackgroundService`)
**Description:** Create `DepartureReminderWorker` polling every 60s to find `ItineraryItem`s starting within `[nowLocal, nowLocal + 30 minutes]` (`UTC+07:00`) where `ReminderSentAtUtc == null`, stamping `ReminderSentAtUtc`, persisting `NotificationType.DepartureReminder` rows, and broadcasting live SignalR notifications.

**Acceptance criteria:**
- [x] Stops departing in `<= 30` minutes emit exactly one `DepartureReminder` per active member and stamp `ReminderSentAtUtc = utcNow`.
- [x] Subsequent ticks skip already-reminded stops (`ReminderSentAtUtc != null`) until rescheduled.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T09

**Files touched:**
- `src/SplitTrip.Web/Workers/DepartureReminderWorker.cs`
- `src/SplitTrip.Services/Itinerary/ItineraryService.cs`

**Estimated scope:** Small (2 files)

---

### Task T12: Build `TripHub.Itinerary.cs`, `ItineraryModuleExtensions.cs` & `/Trips/Itinerary` UI
**Description:** Wire DI in `ItineraryModuleExtensions.cs`, add real-time SignalR hub operations in `TripHub.Itinerary.cs`, and build `/Trips/Itinerary` with multi-day tabs, stop CRUD modals, HTML5/Sortable drag-and-drop reordering, live upvote/downvote badges, and the AI Trip Planner modal.

**Acceptance criteria:**
- [x] Adding, editing, deleting, reordering, voting, and importing AI stops synchronize live across connected browsers in `trip:{tripId}` without page reloads.
- [x] Read-only banner and disabled controls render when `Trip.Status` is `Settling` or `Completed`.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T10, T11

**Files touched:**
- `src/SplitTrip.Web/Extensions/ItineraryModuleExtensions.cs`
- `src/SplitTrip.Web/Hubs/TripHub.Itinerary.cs`
- `src/SplitTrip.Web/Pages/Trips/Itinerary.cshtml`
- `src/SplitTrip.Web/Pages/Trips/Itinerary.cshtml.cs`

**Estimated scope:** Medium (4 files)

---

### Task T13: Add `live-itinerary` xUnit Test Suite
**Description:** Add comprehensive unit tests for itinerary validation, cross-day `SortOrder` normalization, vote toggle/flip, mocked Gemini JSON itinerary generation, and 30-minute `DepartureReminderWorker` lookahead idempotency.

**Acceptance criteria:**
- [x] All unit tests in `tests/SplitTrip.Tests/Itinerary/` pass with deterministic clock and mocked `HttpMessageHandler`.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Itinerary"`

**Dependencies:** T12

**Files touched:**
- `tests/SplitTrip.Tests/Itinerary/ItineraryServiceTests.cs`
- `tests/SplitTrip.Tests/Itinerary/GeminiItineraryServiceTests.cs`
- `tests/SplitTrip.Tests/Itinerary/DepartureReminderWorkerTests.cs`

**Estimated scope:** Medium (3 test files)

---

## Checkpoint 2A: `live-itinerary` Module Gate
- [x] `dotnet build SplitTrip.sln` and `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Itinerary"` pass with 0 errors.
- [x] No files outside `live-itinerary` ownership were modified.

---

## Wave 2B (Parallel Stream 2): `smart-expenses` (`SPEC-smart-expenses.md`)

### Task T14: Implement `ExpenseSplitCalculator` & Whole-VND Reconciliation Tests
**Description:** Build `ExpenseSplitCalculator` supporting `Equal`, `ExactAmount`, `Percentage`, `Shares`, and itemized pro-rata tax/tip/discount distribution using Largest-Remainder deterministic integer VND allocation (`Sum(OwedAmount) == TotalAmount`), paired with unit tests.

**Acceptance criteria:**
- [x] `Sum(OwedAmount) == TotalAmount` holds with 0 fractional VND across uneven divisions (`100_000 / 3`), percentages (`100%`), weights, exact-amount validation, and itemized `TaxOrTipAmount - DiscountAmount` pro-rata allocation.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~ExpenseSplitCalculator"`

**Dependencies:** Checkpoint 1

**Files touched:**
- `src/SplitTrip.Services/Expenses/Dtos/ExpenseDtos.cs`
- `src/SplitTrip.Services/Expenses/ExpenseSplitCalculator.cs`
- `tests/SplitTrip.Tests/Expenses/ExpenseSplitCalculatorTests.cs`

**Estimated scope:** Small (3 files)

---

### Task T15: Implement `ExpenseDao`, `ExpenseRepository` & `ExpenseService`
**Description:** Implement persistence and domain logic for creating/updating/deleting expenses, confirming `DraftReview → ClaimingOpen`, toggling `ReceiptItemClaim`s (shared dish splits + temporary `PaidByUserId` fallback for unclaimed items), locking claiming (`ClaimingOpen → Locked`), and emitting `ExpenseAdded` notifications.

**Acceptance criteria:**
- [x] `DraftReview` expenses stay excluded from `NetBalance`; confirmed expenses (`None`, `ClaimingOpen`, `Locked`) preserve `Sum(OwedAmount) == TotalAmount` at every state transition.
- [x] `LockClaimingAsync` blocks `ClaimingOpen → Locked` while any `ReceiptItem` has zero claims.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T14

**Files touched:**
- `src/SplitTrip.DataAccessObjects/Daos/ExpenseDao.cs`
- `src/SplitTrip.Repositories/Expenses/IExpenseRepository.cs`, `ExpenseRepository.cs`
- `src/SplitTrip.Services/Expenses/IExpenseService.cs`, `ExpenseService.cs`

**Estimated scope:** Medium (5 files)

---

### Task T16: Implement `GeminiReceiptScannerService` (Vision OCR & Mismatch Detection)
**Description:** Implement multimodal receipt scanning (`JPG`/`PNG`/`WEBP` validation + `gemini-2.5-flash` Vision JSON extraction) creating `ReceiptScanStatus.DraftReview` expenses and flagging `Sum(LineTotal) != SubtotalAmount` or `Subtotal + TaxOrTip - Discount != TotalAmount` discrepancies.

**Acceptance criteria:**
- [x] Detects and reports exact VND mismatch deltas in `ReceiptDraftDto` and blocks transitioning `DraftReview → ClaimingOpen` until item lines and totals reconcile cleanly.
- [x] Rejects invalid file types or files `> 10 MB` safely and handles missing `Gemini:ApiKey` gracefully.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T15

**Files touched:**
- `src/SplitTrip.Services/Expenses/IAiReceiptScannerService.cs`
- `src/SplitTrip.Services/Expenses/GeminiReceiptScannerService.cs`

**Estimated scope:** Small (2 files)

---

### Task T17: Build `TripHub.Expenses.cs`, `ExpensesModuleExtensions.cs` & `/Trips/Expenses` UI
**Description:** Register expense services in `ExpensesModuleExtensions.cs`, implement real-time dish claim/lock broadcasts in `TripHub.Expenses.cs`, and build `/Trips/Expenses` featuring the 5-mode split creator, receipt OCR uploader, `DraftReview` mismatch editor, and live SignalR dish-claiming board.

**Acceptance criteria:**
- [x] Claiming or unclaiming a dish updates claimer avatars and live pro-rata `OwedAmount` breakdowns across all connected browsers via `SignalR`.
- [x] Category filters, payer badges, and receipt status pills (`DraftReview`, `ClaimingOpen`, `Locked`) render clearly.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T16

**Files touched:**
- `src/SplitTrip.Web/Extensions/ExpensesModuleExtensions.cs`
- `src/SplitTrip.Web/Hubs/TripHub.Expenses.cs`
- `src/SplitTrip.Web/Pages/Trips/Expenses.cshtml`
- `src/SplitTrip.Web/Pages/Trips/Expenses.cshtml.cs`

**Estimated scope:** Medium (4 files)

---

### Task T18: Add Receipt OCR & Live Item Claiming xUnit Tests
**Description:** Write unit tests verifying mocked Gemini Vision receipt parsing, subtotal/total mismatch confirmation blocking, `DraftReview` ledger exclusion, multi-member shared dish claiming, pro-rata tax/discount calculation, and claim locking.

**Acceptance criteria:**
- [x] All unit tests in `tests/SplitTrip.Tests/Expenses/` pass with 0 rounding drift.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Expenses"`

**Dependencies:** T17

**Files touched:**
- `tests/SplitTrip.Tests/Expenses/GeminiReceiptScannerServiceTests.cs`
- `tests/SplitTrip.Tests/Expenses/LiveItemClaimingServiceTests.cs`

**Estimated scope:** Small (2 test files)

---

## Checkpoint 2B: `smart-expenses` Module Gate
- [x] `dotnet build SplitTrip.sln` and `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Expenses"` pass with 0 errors.
- [x] Zero-sum split invariant (`Sum(OwedAmount) == TotalAmount`) verified across all 5 split modes.

---

## Wave 2C (Parallel Stream 3): `debt-settlement` (`SPEC-debt-settlement.md`)

### Task T19: Implement `DebtSimplificationService` (`Min-Cash-Flow`) & `VietQrService` with Tests
**Description:** Implement the greedy max-creditor/max-debtor `Min-Cash-Flow` graph simplification service (`IDebtSimplificationService`) and Napas VietQR QuickLink URL generator (`IVietQrService` for `img.vietqr.io` with Vietnamese diacritic stripping), paired with unit tests.

**Acceptance criteria:**
- [x] `DebtSimplificationService` enforces `∑ NetBalance == 0`, collapses circular/chain debts to `<= M - 1` transfers, and conserves total positive cash volume.
- [x] `VietQrService` generates valid `compact2` URLs with integer VND amounts and sanitized notes, and flags missing bank details cleanly.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~DebtSimplification|FullyQualifiedName~VietQr"`

**Dependencies:** Checkpoint 1

**Files touched:**
- `src/SplitTrip.Services/Settlements/Dtos/SettlementDtos.cs`
- `src/SplitTrip.Services/Settlements/IDebtSimplificationService.cs`, `DebtSimplificationService.cs`
- `src/SplitTrip.Services/Settlements/IVietQrService.cs`, `VietQrService.cs`
- `tests/SplitTrip.Tests/Settlement/DebtSimplificationServiceTests.cs`, `VietQrServiceTests.cs`

**Estimated scope:** Medium (5 service files + 2 test files)

---

### Task T20: Implement `SettlementDao`, `SettlementRepository` & `SettlementService`
**Description:** Implement persistence and domain state machine for `SyncSettlementPlanAsync`, 2-step settlement handshake (`Pending / Disputed → MarkedPaid` by `FromUserId`; `MarkedPaid → Confirmed / Disputed` by `ToUserId`), manual debt nudge, and automatic `Settling → Completed` transition once all balances reach `0đ`.

**Acceptance criteria:**
- [x] Only `Confirmed` transfers modify `NetBalance`; `SyncSettlementPlanAsync` never deletes `MarkedPaid` or `Confirmed` records.
- [x] Final `Confirmed` transfer bringing all trip member `NetBalance == 0m` on a `Settling` trip transitions `Trip.Status` to `Completed` (`ClosedAtUtc` set).

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T19

**Files touched:**
- `src/SplitTrip.DataAccessObjects/Daos/SettlementDao.cs`
- `src/SplitTrip.Repositories/Settlements/ISettlementRepository.cs`, `SettlementRepository.cs`
- `src/SplitTrip.Services/Settlements/ISettlementService.cs`, `SettlementService.cs`

**Estimated scope:** Medium (5 files)

---

### Task T21: Implement `TripSettlementWorker` (`EndDate` Auto-Settling & 24h Debt Nudges)
**Description:** Implement `TripSettlementWorker` (`BackgroundService`) to auto-transition `Ongoing` trips past `EndDate` (`UTC+07:00`) to `Settling` (generating simplified settlements) and dispatch `NotificationType.DebtNudge` alerts for `Pending`/`Disputed` settlements older than 24 hours using `LastNudgedAtUtc`.

**Acceptance criteria:**
- [x] Trips with `Status == Ongoing` and `EndDate < todayLocal` transition to `Settling` and notify members.
- [x] Overdue settlements emit at most one `DebtNudge` per 24-hour window (`LastNudgedAtUtc` stamped).

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T20

**Files touched:**
- `src/SplitTrip.Web/Workers/TripSettlementWorker.cs`
- `src/SplitTrip.Services/Settlements/SettlementService.cs`

**Estimated scope:** Small (2 files)

---

### Task T22: Implement System Admin Analytics (`AdminAnalyticsDao`, Repo, Service & `/Admin` Page)
**Description:** Build read-only platform analytics across `AdminAnalyticsDao`, `AdminAnalyticsRepository`, `AdminAnalyticsService`, and `[Authorize(Roles = "Admin")]` `/Admin/Index` showing user/trip counts by `TripStatus`, finalized spending by `ExpenseCategory`, AI planner/OCR adoption, and `Min-Cash-Flow` transfer reduction metrics.

**Acceptance criteria:**
- [x] `/Admin` enforces `Roles = "Admin"` (accessible via `appsettings.json` `AdminAccount` login, blocked for standard members).
- [x] Dashboard KPIs exclude `DraftReview` expenses and accurately summarize settlement completion rates.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T20

**Files touched:**
- `src/SplitTrip.DataAccessObjects/Daos/AdminAnalyticsDao.cs`
- `src/SplitTrip.Repositories/Admin/IAdminAnalyticsRepository.cs`, `AdminAnalyticsRepository.cs`
- `src/SplitTrip.Services/Admin/*`
- `src/SplitTrip.Web/Pages/Admin/Index.cshtml*`

**Estimated scope:** Medium (DAO + Repo + Service + Admin Page)

---

### Task T23: Build `TripHub.Settlement.cs`, `SettlementModuleExtensions.cs` & `/Trips/Settlement` UI
**Description:** Wire Settlement & Admin services + `TripSettlementWorker` in `SettlementModuleExtensions.cs`, add real-time settlement handshake broadcasts in `TripHub.Settlement.cs`, and build `/Trips/Settlement` with zero-sum `NetBalance` visual bars, simplified transfer cards, VietQR payment modal, and live Mark Paid / Confirm / Dispute controls.

**Acceptance criteria:**
- [x] Debtor marking paid and Creditor confirming or disputing update both screens in real time via `SignalR`, refreshing `NetBalance` bars and `TripStatus` badges live.
- [x] VietQR modal renders `img.vietqr.io` QR image with one-click copy buttons for account number, exact VND amount, and transfer note.

**Verification:**
- [x] Build succeeds: `dotnet build SplitTrip.sln --configuration Debug`

**Dependencies:** T21, T22

**Files touched:**
- `src/SplitTrip.Web/Extensions/SettlementModuleExtensions.cs`
- `src/SplitTrip.Web/Hubs/TripHub.Settlement.cs`
- `src/SplitTrip.Web/Pages/Trips/Settlement.cshtml`
- `src/SplitTrip.Web/Pages/Trips/Settlement.cshtml.cs`

**Estimated scope:** Medium (4 files)

---

### Task T24: Add Handshake, Worker & Admin Analytics xUnit Tests
**Description:** Add unit tests covering 2-step settlement authorization (`FromUserId` vs `ToUserId`), dispute reason enforcement, `Settling → Completed` auto-transition, `TripSettlementWorker` `EndDate` + 24h `LastNudgedAtUtc` cooldown rules, and Admin analytics aggregation.

**Acceptance criteria:**
- [x] All unit tests in `tests/SplitTrip.Tests/Settlement/` pass 100%.

**Verification:**
- [x] Tests pass: `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Settlement"`

**Dependencies:** T23

**Files touched:**
- `tests/SplitTrip.Tests/Settlement/SettlementHandshakeTests.cs`
- `tests/SplitTrip.Tests/Settlement/TripSettlementWorkerTests.cs`

**Estimated scope:** Small (2 test files)

---

## Checkpoint 2C: `debt-settlement` Module Gate
- [x] `dotnet build SplitTrip.sln` and `dotnet test SplitTrip.sln --filter "FullyQualifiedName~Settlement"` pass with 0 errors.
- [x] Zero-sum ledger (`∑ NetBalance == 0`), VietQR QuickLink generation, 2-step SignalR handshake, worker automation, and `/Admin` role guard verified.

---

## Wave 3: End-to-End Integration & Submission Gate

### Task T25: Cross-Module End-to-End Verification & Schema Parity Check
**Description:** Verify end-to-end integration across `foundation`, `live-itinerary`, `smart-expenses`, and `debt-settlement` (including OCR `DraftReview` → live dish claiming → `Min-Cash-Flow` simplification → VietQR → 2-step confirmation → `TripStatus.Completed` → Admin analytics) and confirm `database/SplitTrip_Schema.sql` matches the current EF Core model.

**Acceptance criteria:**
- [x] Full test suite across Foundation, Itinerary, Expenses, and Settlement passes simultaneously.
- [x] `SplitTrip.Web` maintains strict 5-layer isolation (no `DataAccessObjects` reference) and zero unreferenced/broken navigation links across `/Trips/Details`, `/Trips/Itinerary`, `/Trips/Expenses`, `/Trips/Settlement`, and `/Admin`.

**Verification:**
- [x] Full build & test: `dotnet build SplitTrip.sln --configuration Release && dotnet test SplitTrip.sln --configuration Release --no-build`

**Dependencies:** Checkpoint 2A, Checkpoint 2B, Checkpoint 2C

**Files touched:**
- `src/SplitTrip.Services/Seed/DemoDataSeeder.cs`
- `tests/SplitTrip.Tests/Integration/EndToEndTripLifecycleTests.cs`
- `database/SplitTrip_Schema.sql`

**Estimated scope:** Small (1–2 files)

---

### Task T26: Documentation & Submission Readiness (`README.md`)
**Description:** Document architecture, setup commands, LocalDB connection string, `appsettings.json` System Admin credentials, optional `Gemini:ApiKey` user-secrets setup, VietQR flow, and test commands in `README.md`, ensuring zero secrets are tracked in git.

**Acceptance criteria:**
- [x] `appsettings.json` contains empty `Gemini:ApiKey` (`""`) and documented local development defaults.
- [x] `README.md` enables a fresh clone to build, migrate/run against SQL Server LocalDB, and execute all tests in under 3 commands.

**Verification:**
- [x] Clean status & test pass: `dotnet test SplitTrip.sln`

**Dependencies:** T25

**Files touched:**
- `README.md`

**Estimated scope:** Small (1 file)

---

## Checkpoint 3: Final Release Gate
- [x] All 4 specs (`SPEC-foundation.md`, `SPEC-live-itinerary.md`, `SPEC-smart-expenses.md`, `SPEC-debt-settlement.md`), `CAPABILITY-MAP.md`, `tasks/plan.md`, `tasks/todo.md`, and `database/SplitTrip_Schema.sql` are committed and consistent.
- [x] `dotnet build SplitTrip.sln` succeeds with 0 errors and `dotnet test SplitTrip.sln` passes 100% of unit and integration tests.
- [x] Git commit history reflects clean, atomic vertical slices pushed incrementally to `origin/main`.
