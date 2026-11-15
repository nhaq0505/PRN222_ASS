# Capability Map: SplitTrip

**SplitTrip** is a real-time collaborative group travel planner, AI-assisted expense splitter, and graph-based debt simplification platform built on `.NET 10.0`, `ASP.NET Core Razor Pages`, `EF Core (SQL Server)`, `SignalR`, and `BackgroundService` workers.

## Modules

| Module id | Responsibility | Depends on | Suggested Owner |
|---|---|---|---|
| `foundation` | 5-layer `.NET 10.0` solution (`BusinessObjects`, `DataAccessObjects`, `Repositories`, `Services`, `Web` + `Tests`), `SplitTripDbContext` (EF Core Code-First), Cookie Auth + `appsettings.json` System Admin, User profile & bank info (for VietQR), Trip lifecycle (`Planning → Ongoing → Settling → Completed`), Invite code join/leave guards, base `TripHub` (`SignalR` groups). | — | Shared foundation |
| `live-itinerary` | Collaborative multi-day schedule (`TripDays`, `ItineraryItems`), real-time drag-and-drop reordering & updates via `SignalR`, stop voting (`ItineraryVotes`), **AI Trip Planner** (`Gemini` structured itinerary draft generation), and **Departure Reminder Worker** (`BackgroundService` notifying 30 minutes before upcoming stops). | `foundation` | Member 1 |
| `smart-expenses` | Multi-mode group expenses (`Equal`, `ExactAmount`, `Percentage`, `Shares`, `Itemized`) with strict penny-reconciliation (`Sum(Shares) == TotalAmount`), **AI Receipt Scanner** (`Gemini Vision` item extraction + subtotal/total mismatch draft review), and **Live Item Claiming** (`SignalR` real-time dish selection with pro-rata tax/discount allocation). | `foundation` | Member 2 |
| `debt-settlement` | Net-balance ledger (`∑ NetBalance == 0`), **Min-Cash-Flow** graph debt simplification engine, dynamic **VietQR** QuickLink generation (`img.vietqr.io`), 2-step real-time settlement handshake (`Pending → MarkedPaid → Confirmed / Disputed`) via `SignalR`, **Trip Auto-Close & Overdue Debt Nudge Worker** (`BackgroundService`), and System Admin analytics dashboard. | `foundation`, `smart-expenses` | Member 3 |

## Build Order

```text
foundation ──→ live-itinerary
           └──→ smart-expenses ──→ debt-settlement
```

> Note: Core domain contracts (`MemberNetBalanceDto`, `IDebtSimplificationService`, `IVietQrService`) are established early so `debt-settlement` algorithmic, VietQR, and Admin dashboard slices can be developed in parallel right after `foundation`.
