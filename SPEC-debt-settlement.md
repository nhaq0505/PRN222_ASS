# Spec: `debt-settlement` — Zero-Sum Ledger, Min-Cash-Flow, VietQR, Workers & Admin Analytics

## Assumptions

1. **Zero-Sum Ledger Source of Truth:** Member balances consume `ITripService.GetTripMemberBalancesAsync(tripId)` (whole `VND`, `decimal(18, 0)`), which includes finalized expenses (`ScanStatus != ReceiptScanStatus.DraftReview`) and `SettlementStatus.Confirmed` transfers while ignoring `Pending`, `MarkedPaid`, and `Disputed` rows until confirmed.
2. **Napas VietQR QuickLink:** QR codes use the public VietQR QuickLink image format (`https://img.vietqr.io/image/{bankBinOrCode}-{accountNumber}-compact2.png?...`) populated from the creditor's saved profile (`BankBin` / `BankCode`, `BankAccountNumber`, `BankAccountHolder`). No paid VietQR API token is required.
3. **Vietnam Date Boundary for Worker:** `TripSettlementWorker` evaluates `Trip.EndDate < todayLocal` in Vietnam time (`UTC+07:00`, `"SE Asia Standard Time"` / `"Asia/Ho_Chi_Minh"`) and stores `LastNudgedAtUtc`, `MarkedPaidAtUtc`, and `ConfirmedAtUtc` in UTC.
4. **Parallel Module Isolation:** All DI hooks reside in `src/SplitTrip.Web/Extensions/SettlementModuleExtensions.cs` and SignalR hub operations reside in `src/SplitTrip.Web/Hubs/TripHub.Settlement.cs` (`public partial class TripHub`), never editing `Program.cs` or `TripHub.cs`.

## Objective

Close the financial loop of group travel with a mathematically zero-sum `NetBalance` ledger, a greedy **Min-Cash-Flow** graph debt simplification engine, dynamic **Napas VietQR** payment links, a **2-step SignalR settlement handshake** (`Pending → MarkedPaid → Confirmed / Disputed`), an automated **`TripSettlementWorker`** (`Ongoing → Settling` + 24h overdue debt nudges), and a **System Admin Analytics Dashboard**.

### Core Domain Rules & Invariants

1. **Strict 5-Layer Access:**
   - `SplitTrip.Web` (`Pages/Trips/Settlement*`, `Pages/Admin/*`, `Hubs/TripHub.Settlement.cs`, `Workers/TripSettlementWorker.cs`) depends **only** on `SplitTrip.Services` and `SplitTrip.BusinessObjects`.
2. **Zero-Sum `NetBalance` Ledger Invariant (`∑ NetBalance == 0`):**
   - For each trip member $u$:
     $$\text{NetBalance}_u = \text{TotalPaidFinalizedExpenses}_u - \text{TotalOwedSplits}_u + \text{ConfirmedSettlementsPaid}_u - \text{ConfirmedSettlementsReceived}_u$$
   - Across all members of any trip, $\sum_u \text{NetBalance}_u == 0\text{ VND}$ holds identically before, during, and after partial or complete settlements.
   - `NetBalance > 0`: Creditor (owed money); `NetBalance < 0`: Debtor (owes `|NetBalance|`); `NetBalance == 0`: Settled.
3. **`Min-Cash-Flow` Greedy Debt Simplification (`IDebtSimplificationService`):**
   - Validates `balances.Sum(b => b.NetBalance) == 0m` (throws `InvalidOperationException` on non-zero sum).
   - Partitions non-zero members into `Creditors` (`Credit = NetBalance > 0`) and `Debtors` (`Debt = -NetBalance > 0`).
   - Repeatedly pairs `maxCreditor` (`OrderByDescending(Credit).ThenBy(UserId)`) with `maxDebtor` (`OrderByDescending(Debt).ThenBy(UserId)`), emitting transfer `Amount = Math.Min(maxCreditor.Credit, maxDebtor.Debt)` from `maxDebtor` to `maxCreditor`, and decrements both until all balances hit `0`.
   - Guarantees at most $M - 1$ transfers for $M$ non-zero participants, eliminates circular debts ($A \to B \to C \to A \implies 0$ transfers), and minimizes total cash transferred ($\sum \text{Amount} == \sum_{\text{NetBalance} > 0} \text{NetBalance}$).
4. **Napas VietQR QuickLink Builder (`IVietQrService`):**
   - Builds `https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={wholeVnd}&addInfo={encodedNote}&accountName={encodedHolder}` where:
     - `bankId` prefers 6-digit Napas `BankBin` (falling back to `BankCode`).
     - `wholeVnd` is formatted as an ungrouped integer string (`> 0`).
     - `TransferNote` is ASCII-normalized uppercase (`"SPLITTRIP {InviteCode} {DebtorShortName}"`, ≤ 50 chars) and URL-encoded via `Uri.EscapeDataString`.
   - When creditor `BankBin`/`BankCode` or `BankAccountNumber` is missing, returns `IsBankConfigured = false` and `QrImageUrl = null` so the UI displays a clear bank-setup callout instead of a broken image.
5. **2-Step SignalR Settlement Handshake (`ISettlementService` & `TripHub.Settlement.cs`):**
   - **Plan Materialization (`SyncSettlementPlanAsync`):** Recomputes simplified transfers on residual balances (subtracting in-flight `MarkedPaid` obligations so already-sent transfers are never duplicated), replacing stale `Pending` rows while preserving `MarkedPaid`, `Confirmed`, and `Disputed` audit history.
   - **`Pending / Disputed → MarkedPaid`:** Only `FromUserId` (debtor) may mark a transfer paid (`MarkedPaidAtUtc = DateTime.UtcNow`, optional `ProofNote`, clears `DisputeReason`). Dispatches `NotificationType.SettlementUpdate` to `ToUserId` and broadcasts `SettlementUpdated` to `trip:{tripId}`.
   - **`MarkedPaid → Confirmed`:** Only `ToUserId` (creditor) may confirm receipt (`ConfirmedAtUtc = DateTime.UtcNow`). Updates ledger `NetBalance` immediately; when `Trip.Status == TripStatus.Settling` and all member `NetBalance == 0m` with no open `Pending`/`MarkedPaid`/`Disputed` settlements, automatically transitions `Trip.Status = TripStatus.Completed` (`ClosedAtUtc = DateTime.UtcNow`) and broadcasts `TripCompleted`.
   - **`MarkedPaid → Disputed`:** Only `ToUserId` (creditor) may dispute with a required `DisputeReason` (`1..500` chars). Ledger `NetBalance` remains unchanged; debtor (`FromUserId`) receives `NotificationType.SettlementUpdate` + live SignalR alert to re-verify and re-submit.
6. **`TripSettlementWorker` (`BackgroundService`):**
   - Runs periodically (`PeriodicTimer`, 5-minute interval) via `IServiceScopeFactory` delegating testable logic to `ISettlementAutomationService.RunCycleAsync(DateTime utcNow, CancellationToken ct)`:
     1. **Auto-Transition Past `EndDate`:** Transitions trips with `Status == TripStatus.Ongoing` and `EndDate < todayLocal` (`UTC+07:00`) to `TripStatus.Settling`, materializes simplified `Settlement` rows (or `Completed` if zero balances exist), notifies active members, and broadcasts `TripStatusChanged` to `trip:{tripId}`.
     2. **24h Overdue Debt Nudges:** Selects `Settlement` rows on `Settling` trips where `Status` is `Pending` or `Disputed`, `CreatedAtUtc <= utcNow.AddHours(-24)`, and `LastNudgedAtUtc == null || LastNudgedAtUtc <= utcNow.AddHours(-24)`. Stamps `LastNudgedAtUtc = utcNow`, creates `NotificationType.DebtNudge` for `FromUserId`, and pushes `ReceiveNotification` to `user:{FromUserId}`.
7. **System Admin Analytics Dashboard (`/Admin/Index`):**
   - Restricted via `[Authorize(Roles = "Admin")]` (configured in `appsettings.json` `AdminAccount`).
   - Aggregates read-only platform metrics via `IAdminAnalyticsService`: total users, trips by `TripStatus`, finalized spending volume (`VND`), category breakdown (`ExpenseCategory`), AI adoption (`IsAiSuggested` stops + OCR receipts), debt simplification efficiency (raw split obligations vs simplified settlements `%` saved), settlement status breakdown (`Confirmed` / `MarkedPaid` / `Pending` / `Disputed`), and recent trips table.

## Tech Stack

- **Runtime & Web:** `.NET 10.0` (`net10.0`), `ASP.NET Core Razor Pages`, `ASP.NET Core SignalR`
- **Data Access:** `EF Core 10 (SQL Server)` via `SettlementDao` & `AdminAnalyticsDao` (`SplitTrip.DataAccessObjects`)
- **Payment QR Standard:** Napas 247 VietQR QuickLink (`img.vietqr.io`)
- **Background Processing:** `Microsoft.Extensions.Hosting.BackgroundService` (`TripSettlementWorker`)
- **Testing:** `xUnit` + `EF Core InMemory` (`tests/SplitTrip.Tests/Settlement/`)

## Commands

```powershell
# Build entire solution
dotnet build SplitTrip.sln --configuration Debug

# Run settlement, VietQR, worker, and admin analytics tests
dotnet test SplitTrip.sln --no-build --filter "FullyQualifiedName~Settlement"

# Run full test suite
dotnet test SplitTrip.sln --no-build --verbosity normal
```

## Project Structure

```text
src/
├── SplitTrip.DataAccessObjects/
│   └── Daos/
│       ├── SettlementDao.cs                      → EF Core persistence for Settlement, overdue queries, auto-settling trips
│       └── AdminAnalyticsDao.cs                  → Read-only aggregate projections for System Admin dashboard
├── SplitTrip.Repositories/
│   ├── Settlements/
│   │   ├── ISettlementRepository.cs              → Repository contract for settlements & worker queries
│   │   └── SettlementRepository.cs               → Repository implementation wrapping SettlementDao
│   └── Admin/
│       ├── IAdminAnalyticsRepository.cs          → Repository contract for platform KPIs
│       └── AdminAnalyticsRepository.cs           → Repository implementation wrapping AdminAnalyticsDao
├── SplitTrip.Services/
│   ├── Settlements/
│   │   ├── Dtos/SettlementDtos.cs                → SimplifiedTransferDto, SettlementCardDto, VietQrDto, LedgerSummaryDto
│   │   ├── IDebtSimplificationService.cs         → Pure Min-Cash-Flow simplification contract
│   │   ├── DebtSimplificationService.cs          → Greedy max-creditor / max-debtor graph simplifier
│   │   ├── IVietQrService.cs                     → Napas VietQR QuickLink builder contract
│   │   ├── VietQrService.cs                      → URL-safe img.vietqr.io generator & note normalizer
│   │   ├── ISettlementService.cs                 → Plan sync, 2-step handshake, manual nudge, worker automation
│   │   └── SettlementService.cs                  → State machine, zero-sum checks, auto-complete trip logic
│   └── Admin/
│       ├── Dtos/AdminDashboardDto.cs             → KPI cards, category breakdown, settlement efficiency DTOs
│       ├── IAdminAnalyticsService.cs             → Admin analytics service contract
│       └── AdminAnalyticsService.cs              → Platform analytics orchestration
└── SplitTrip.Web/
    ├── Extensions/
    │   └── SettlementModuleExtensions.cs         → Registers Settlement & Admin DAOs/Repos/Services + TripSettlementWorker
    ├── Hubs/
    │   └── TripHub.Settlement.cs                 → partial class TripHub for real-time 2-step settlement events
    ├── Workers/
    │   └── TripSettlementWorker.cs               → Periodic BackgroundService for EndDate auto-close & 24h debt nudges
    └── Pages/
        ├── Trips/
        │   ├── Settlement.cshtml                 → NetBalance bars, simplified transfers, VietQR modal, 2-step action cards
        │   └── Settlement.cshtml.cs              → PageModel handlers for sync, mark paid, confirm, dispute, manual nudge
        └── Admin/
            ├── Index.cshtml                      → System Admin KPIs, category & status charts, settlement efficiency table
            └── Index.cshtml.cs                   → [Authorize(Roles = "Admin")] PageModel
tests/
└── SplitTrip.Tests/
    └── Settlement/
        ├── DebtSimplificationServiceTests.cs     → Zero-sum validation, circular debt collapse, M-1 bound, cash conservation
        ├── VietQrServiceTests.cs                 → QuickLink URL formatting, Vietnamese diacritic stripping, missing bank guard
        ├── SettlementHandshakeTests.cs           → Pending -> MarkedPaid -> Confirmed/Disputed permissions & auto-complete
        └── TripSettlementWorkerTests.cs          → EndDate Ongoing -> Settling transition & 24h LastNudgedAtUtc idempotency
```

## Code Style

```csharp
namespace SplitTrip.Services.Settlements;

public sealed class DebtSimplificationService : IDebtSimplificationService
{
    public IReadOnlyList<SimplifiedTransferDto> Simplify(IReadOnlyCollection<MemberBalanceInput> balances)
    {
        if (balances.Count == 0)
            return [];

        var netSum = balances.Sum(b => b.NetBalance);
        if (netSum != 0m)
            throw new InvalidOperationException($"Sổ cái không cân bằng tổng bằng 0 (lệch {netSum:N0}đ).");

        var creditors = balances
            .Where(b => b.NetBalance > 0m)
            .Select(b => new BalanceNode(b.UserId, b.FullName, b.NetBalance))
            .ToList();

        var debtors = balances
            .Where(b => b.NetBalance < 0m)
            .Select(b => new BalanceNode(b.UserId, b.FullName, -b.NetBalance))
            .ToList();

        var transfers = new List<SimplifiedTransferDto>();

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            var creditor = creditors.OrderByDescending(c => c.Remaining).ThenBy(c => c.UserId).First();
            var debtor = debtors.OrderByDescending(d => d.Remaining).ThenBy(d => d.UserId).First();

            var amount = Math.Min(creditor.Remaining, debtor.Remaining);
            transfers.Add(new SimplifiedTransferDto(debtor.UserId, debtor.FullName, creditor.UserId, creditor.FullName, amount));

            creditor.Remaining -= amount;
            debtor.Remaining -= amount;

            if (creditor.Remaining == 0m) creditors.Remove(creditor);
            if (debtor.Remaining == 0m) debtors.Remove(debtor);
        }

        return transfers;
    }

    private sealed class BalanceNode(Guid userId, string fullName, decimal remaining)
    {
        public Guid UserId { get; } = userId;
        public string FullName { get; } = fullName;
        public decimal Remaining { get; set; } = remaining;
    }
}
```

## Testing Strategy

- **Min-Cash-Flow Unit Tests (`DebtSimplificationServiceTests.cs`):**
  - Circular debt ($A=-100k, B=+100k$ vs $A \to B \to C \to A$ nets to $0$ transfers), chain debt ($A \xrightarrow{200k} B \xrightarrow{200k} C \implies A \xrightarrow{200k} C$), 6-person skewed trip verifying `transfers.Count <= nonZeroMembers - 1`, cash conservation (`transfers.Sum(t => t.Amount) == totalPositiveBalance`), and non-zero ledger rejection.
- **VietQR Unit Tests (`VietQrServiceTests.cs`):**
  - Verifies exact `https://img.vietqr.io/image/{bin}-{stk}-compact2.png` URL structure, integer `amount`, diacritic stripping (`"Nguyễn Văn Ánh"` → `"NGUYEN VAN ANH"`), and `IsBankConfigured == false` when creditor lacks bank details.
- **Handshake & Lifecycle Tests (`SettlementHandshakeTests.cs`):**
  - Enforces actor authorization (only `FromUserId` marks paid; only `ToUserId` confirms or disputes; `Disputed` requires `DisputeReason`), verifies `Confirmed` shifts `NetBalance` toward `0`, and confirms final confirmation on a `Settling` trip transitions `Trip.Status` to `Completed`.
- **Worker & Admin Analytics Tests (`TripSettlementWorkerTests.cs`):**
  - Verifies `Ongoing` trips with `EndDate < todayLocal` transition to `Settling` while current/future trips stay `Ongoing`; verifies overdue `Pending`/`Disputed` settlements older than 24h emit one `DebtNudge` and respect the 24h `LastNudgedAtUtc` cooldown; verifies Admin analytics totals exclude `DraftReview` expenses.

## Boundaries

- **Always:**
  - Verify `∑ NetBalance == 0` before running `Min-Cash-Flow` simplification.
  - Enforce strict actor checks on settlement transitions (`FromUserId` for `MarkedPaid`; `ToUserId` for `Confirmed` and `Disputed`).
  - Keep DI registrations in `SettlementModuleExtensions.cs` and SignalR hub methods in `TripHub.Settlement.cs`.
  - Protect all `/Admin/*` pages with `[Authorize(Roles = "Admin")]`.
- **Ask first:**
  - Integrating live bank webhook callbacks (Casso / SePay / VietQR Pro API) beyond the 2-step member handshake.
  - Modifying `Settlement` or `Notification` entity schemas after `InitialCreate`.
- **Never:**
  - Mutate member `NetBalance` on `Pending`, `MarkedPaid`, or `Disputed` status (only `Confirmed` settles debt).
  - Overwrite or delete `MarkedPaid` or `Confirmed` rows during `SyncSettlementPlanAsync`.
  - Reference `SplitTrip.DataAccessObjects` from `SplitTrip.Web` or edit `Program.cs` / `TripHub.cs`.

## Success Criteria

- [ ] `/Trips/Settlement` displays each member's zero-sum `NetBalance` breakdown (`Paid`, `Owed`, `Settled`, `Net`) and renders simplified `Min-Cash-Flow` transfers (`<= M - 1` transactions).
- [ ] Clicking "Thanh toán VietQR" renders a valid `img.vietqr.io` QR code pre-filled with creditor bank BIN/account, exact integer VND amount, and sanitized transfer note (or prompts bank profile setup if missing).
- [ ] Debtor (`FromUserId`) marking paid (`Pending → MarkedPaid`) and Creditor (`ToUserId`) confirming (`MarkedPaid → Confirmed`) or disputing (`MarkedPaid → Disputed`) sync live across browsers via `SignalR`, automatically completing `Settling → Completed` when all balances hit `0đ`.
- [ ] `TripSettlementWorker` automatically transitions `Ongoing → Settling` once `EndDate` passes and dispatches at-most-once-per-24h `DebtNudge` notifications using `LastNudgedAtUtc`.
- [ ] System Admin signing in via `appsettings.json` credentials accesses `/Admin` with accurate trip, category spending, AI usage, and debt-simplification efficiency analytics, while non-admin users receive `403 Access Denied`.
- [ ] All unit tests in `tests/SplitTrip.Tests/Settlement/` pass (`dotnet test SplitTrip.sln`).
