# Implementation Plan: SplitTrip

## Overview

**SplitTrip** is a collaborative group travel planner, AI-assisted expense splitter, and graph-based debt settlement platform built on `.NET 10.0` (`ASP.NET Core Razor Pages`, `EF Core 10 SQL Server`, `SignalR`, `BackgroundService`, `Google Gemini 2.5 Flash`, and `Napas VietQR`). Work is organized into **Wave 1 (`foundation`)** to establish the strict 5-layer architecture, complete SQL Server schema, authentication, trip workspace, and modular extension seams, followed by **Wave 2**, where three feature modules (`live-itinerary`, `smart-expenses`, `debt-settlement`) execute **in parallel** without file conflicts, and **Wave 3** for full-stack integration verification.

## Dependency Graph

```text
Wave 1: foundation (T01–T08)
  ├── 5-Layer Solution (BusinessObjects → DataAccessObjects → Repositories → Services → Web + Tests)
  ├── Complete EF Core Schema + InitialCreate Migration + database/SplitTrip_Schema.sql
  ├── Cookie Auth + appsettings System Admin + User VietQR Bank Profile
  ├── Trip Lifecycle + InviteCode + GetTripMemberBalancesAsync + NetBalance == 0 Leave Guard
  └── Partial TripHub + Pre-wired Module DI Stubs (Itinerary / Expenses / Settlement)
        │
        ├──► Wave 2A [Parallel]: live-itinerary (T09–T13)
        │      ├── ItineraryDao / Repository / Service (CRUD, Cross-Day Reorder, Vote Toggle)
        │      ├── Gemini 2.5 Flash Structured JSON Itinerary Generator
        │      ├── DepartureReminderWorker (30-min lookahead, ReminderSentAtUtc)
        │      └── TripHub.Itinerary.cs + /Trips/Itinerary UI + xUnit Suite
        │
        ├──► Wave 2B [Parallel]: smart-expenses (T14–T18)
        │      ├── ExpenseSplitCalculator (5 Modes, Whole-VND Largest-Remainder Reconciliation)
        │      ├── ExpenseDao / Repository / Service (CRUD, Draft Exclusion, Live Claiming)
        │      ├── Gemini 2.5 Flash Vision Receipt OCR + DraftReview Mismatch Guard
        │      └── TripHub.Expenses.cs + /Trips/Expenses UI + xUnit Suite
        │
        └──► Wave 2C [Parallel]: debt-settlement (T19–T24)
               ├── DebtSimplificationService (Min-Cash-Flow) + VietQrService (img.vietqr.io)
               ├── SettlementDao / Repository / Service (2-Step Handshake + Auto-Complete)
               ├── TripSettlementWorker (Ongoing → Settling + 24h Overdue Debt Nudges)
               ├── AdminAnalyticsDao / Repository / Service + /Admin Dashboard
               └── TripHub.Settlement.cs + /Trips/Settlement UI + xUnit Suite
                     │
                     ▼
Wave 3: Integration & Submission Verification (T25–T26)
```

## Architecture Decisions

1. **Strict 5-Layer PRN222 Boundary:**
   - `SplitTrip.Web` references **only** `SplitTrip.Services` and `SplitTrip.BusinessObjects` (never `SplitTrip.DataAccessObjects` or `SplitTripDbContext`). Service-layer DI helpers encapsulate repository, DAO, and `DbContext` registrations.
2. **Zero-Conflict Wave 2 Parallelism via Partial Hub & Module Extension Stubs:**
   - Wave 1 declares `public partial class TripHub : Hub` in `src/SplitTrip.Web/Hubs/TripHub.cs` and wires `AddFoundationModule`, `AddItineraryModule`, `AddExpensesModule`, and `AddSettlementModule` in `Program.cs`.
   - Wave 2 workers exclusively edit their own slice files (`TripHub.<Module>.cs`, `<Module>ModuleExtensions.cs`, `Pages/Trips/<Module>.cshtml*`, `Workers/*`, `Services/<Module>/*`, `Repositories/<Module>/*`, `Daos/<Module>Dao.cs`, `tests/SplitTrip.Tests/<Module>/*`), eliminating merge/index conflicts.
3. **Complete Upfront Relational Schema with Safe Delete Behaviors:**
   - Defining all entities in Wave 1 (`InitialCreate`) avoids parallel EF Core migration snapshot collisions in Wave 2.
   - All foreign keys pointing to `User` use `DeleteBehavior.Restrict` (and `Notification.TripId` uses `DeleteBehavior.SetNull` / `Restrict`) so SQL Server `(localdb)\mssqllocaldb` applies cleanly with zero multiple-cascade-path errors.
4. **Whole-VND Financial Precision & Zero-Sum Ledger Contract:**
   - Money columns use `decimal(18, 0)` (integer `VND`) and weights use `decimal(18, 4)`.
   - `ITripService.GetTripMemberBalancesAsync(Guid tripId)` is exposed in Wave 1 (excluding `ReceiptScanStatus.DraftReview` unconfirmed OCR drafts and counting only `SettlementStatus.Confirmed` settlements), enabling `debt-settlement` and `smart-expenses` to build concurrently against a shared zero-sum contract (`∑ NetBalance == 0`).
5. **Idempotent Background Workers:**
   - `DepartureReminderWorker` stamps `ItineraryItem.ReminderSentAtUtc` atomically with `DepartureReminder` notification creation (resetting only if rescheduled to a new future slot).
   - `TripSettlementWorker` stamps `Settlement.LastNudgedAtUtc` to enforce a strict 24-hour cooldown on `DebtNudge` notifications and evaluates `EndDate` against Vietnam local date (`UTC+07:00`).

## Phase & Task Index (`tasks/todo.md`)

Detailed task definitions, acceptance criteria, file lists, and verification commands are tracked in `tasks/todo.md`:

### Wave 1: `foundation` (`SPEC-foundation.md`)
- [x] `T01`: Solution scaffolding (`.NET 10.0` 5-layer projects + `SplitTrip.Tests`), enums, and option models.
- [x] `T02`: Domain entities across all four capability areas (`BusinessObjects`).
- [x] `T03`: `SplitTripDbContext`, `InitialCreate` migration, LocalDB update, and `database/SplitTrip_Schema.sql` export.
- [x] `T04`: Foundation DAOs and Repositories (`User`, `Trip`, `TripMember`, `Notification`).
- [x] `T05`: `AuthService` (BCrypt, `appsettings` Admin, bank profile) and `TripService` (state machine, invite code, `NetBalance` ledger, leave guard).
- [x] `T06`: Foundation xUnit test suite (`AuthService`, `TripService`, leave guard).
- [x] `T07`: Cookie Auth, `partial class TripHub`, Wave 2 DI extension stubs, and responsive UI layout.
- [x] `T08`: Auth, Bank Profile (VietQR preview), and Trip Workspace Razor Pages with live SignalR sync.
- [x] **Checkpoint 1:** Wave 1 Foundation verified (`dotnet build`, `dotnet test`, LocalDB schema, clean module seams).

### Wave 2A (Parallel): `live-itinerary` (`SPEC-live-itinerary.md`)
- [x] `T09`: `ItineraryDao`, `ItineraryRepository`, and `ItineraryService` (schedule CRUD, cross-day reorder, vote toggle/flip).
- [x] `T10`: `GeminiItineraryService` (`gemini-2.5-flash` structured JSON itinerary generator).
- [x] `T11`: `DepartureReminderWorker` (60s `BackgroundService`, 30-min lookahead, `ReminderSentAtUtc`).
- [x] `T12`: `TripHub.Itinerary.cs`, `ItineraryModuleExtensions.cs`, and `/Trips/Itinerary` collaborative timeline UI.
- [x] `T13`: `live-itinerary` xUnit test suite (`ItineraryServiceTests`, `GeminiItineraryServiceTests`, `DepartureReminderWorkerTests`).
- [x] **Checkpoint 2A:** `live-itinerary` verified.

### Wave 2B (Parallel): `smart-expenses` (`SPEC-smart-expenses.md`)
- [x] `T14`: `ExpenseSplitCalculator` (5 split modes + pro-rata adjustments with Largest-Remainder integer VND math) & calculator unit tests.
- [x] `T15`: `ExpenseDao`, `ExpenseRepository`, and `ExpenseService` (CRUD, `DraftReview` gate, live item claiming, claiming lock).
- [x] `T16`: `GeminiReceiptScannerService` (`gemini-2.5-flash` Vision OCR + subtotal/total mismatch warning).
- [x] `T17`: `TripHub.Expenses.cs`, `ExpensesModuleExtensions.cs`, and `/Trips/Expenses` UI (5-mode split form, `DraftReview` editor, live claiming board).
- [x] `T18`: `smart-expenses` xUnit tests for Vision OCR mismatch blocking and live multi-user item claiming.
- [x] **Checkpoint 2B:** `smart-expenses` verified.

### Wave 2C (Parallel): `debt-settlement` (`SPEC-debt-settlement.md`)
- [x] `T19`: `DebtSimplificationService` (`Min-Cash-Flow` greedy simplifier) and `VietQrService` (`img.vietqr.io` QuickLink builder) + unit tests.
- [x] `T20`: `SettlementDao`, `SettlementRepository`, and `SettlementService` (plan sync, 2-step handshake, auto-complete trip).
- [x] `T21`: `TripSettlementWorker` (`Ongoing → Settling` past `EndDate` + 24h `LastNudgedAtUtc` overdue debt nudges).
- [x] `T22`: `AdminAnalyticsDao`, `AdminAnalyticsRepository`, `AdminAnalyticsService`, and `/Admin` analytics dashboard.
- [x] `T23`: `TripHub.Settlement.cs`, `SettlementModuleExtensions.cs`, and `/Trips/Settlement` UI (ledger, VietQR modal, live handshake).
- [x] `T24`: `debt-settlement` xUnit tests for 2-step handshake authorization, auto-complete, and worker idempotency.
- [x] **Checkpoint 2C:** `debt-settlement` verified.

### Wave 3: Final Integration & Submission Gate
- [x] `T25`: Cross-module end-to-end verification (ledger conservation across OCR, claiming, partial/disputed/confirmed settlements, and Admin KPIs) + `DemoDataSeeder`.
- [x] `T26`: Documentation, configuration sanitation, SQL script check, and final build/test pass.
- [x] **Checkpoint 3:** Release-ready verification complete.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| SQL Server `multiple cascade paths` error on `User` foreign keys | High | Enforce `DeleteBehavior.Restrict` on every FK targeting `Users` and `SetNull`/`Restrict` on `Notification.TripId`; test `dotnet ef database update` on SQL Server LocalDB in Wave 1 (`T03`). |
| Rounding drift (`±1–2 VND`) breaking `∑ NetBalance == 0` | High | Centralize integer `decimal(18, 0)` Largest-Remainder allocation in `ExpenseSplitCalculator`, enforce `Sum(OwedAmount) == TotalAmount` on write, and assert `Sum(NetBalance) == 0` before `Min-Cash-Flow`. |
| Wave 2 parallel agents colliding on `Program.cs`, `TripHub.cs`, or EF migrations | High | Pre-create the full EF schema, `partial class TripHub`, and `Add*Module` extension files in Wave 1 so Wave 2 workers touch strictly disjoint files. |
| Missing or rate-limited `Gemini:ApiKey` crashing UI flows | Medium | Read `GeminiOptions` via `IOptions<GeminiOptions>`, wrap HTTP calls with structured error handling, and return actionable Vietnamese warning alerts without corrupting trip data. |
| Duplicate reminder or debt-nudge spam across worker restarts | Medium | Persist `ItineraryItem.ReminderSentAtUtc` and `Settlement.LastNudgedAtUtc` in SQL Server within the same transaction as `Notification` creation. |
| Timezone mismatch between UTC server clock and local Vietnam schedule (`DateOnly`/`TimeOnly`) | Medium | Standardize local schedule comparisons on Vietnam timezone (`SE Asia Standard Time` / `Asia/Ho_Chi_Minh`, `UTC+07:00`) via an explicit helper injectable with `utcNow` in unit tests. |

## Open Questions

- None blocking Wave 1 or Wave 2 implementation; live bank webhook reconciliation (Casso/SePay) is intentionally out of scope in favor of the deterministic 2-step member handshake (`Pending → MarkedPaid → Confirmed / Disputed`).
