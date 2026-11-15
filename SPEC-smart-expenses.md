# Spec: `smart-expenses` — Multi-Mode Expense Splitting, Gemini Receipt OCR & Live Item Claiming

## Assumptions

1. **Whole-VND Integer Arithmetic:** Every monetary value (`SubtotalAmount`, `TaxOrTipAmount`, `DiscountAmount`, `TotalAmount`, `OwedAmount`, `UnitPrice`, `LineTotal`) uses `decimal(18, 0)` representing whole `VND` (`₫`), while `ExpenseSplit.ShareRatioOrWeight` uses `decimal(18, 4)` for percentages/weights.
2. **Draft Exclusion & Zero-Sum Continuity:** Unconfirmed AI scans (`ScanStatus == ReceiptScanStatus.DraftReview`) are excluded from trip `NetBalance` calculations. Once confirmed (`None`, `ClaimingOpen`, or `Locked`), `Sum(ExpenseSplit.OwedAmount) == Expense.TotalAmount` holds at all times; during `ClaimingOpen`, any not-yet-claimed `ReceiptItem` temporarily allocates to `PaidByUserId` until claimed, and transitioning `ClaimingOpen → Locked` requires every `ReceiptItem` to have at least one claim.
3. **Local Receipt Uploads & Gemini Vision:** Receipt images (`JPG`, `PNG`, `WEBP`, ≤ 10 MB) are validated by MIME/header bytes, stored with randomized `Guid` filenames under `wwwroot/uploads/receipts/{tripId}/`, and parsed via `Gemini 2.5 Flash` multimodal REST API (`responseMimeType = "application/json"`).
4. **Parallel Module Isolation:** All DI hooks live in `src/SplitTrip.Web/Extensions/ExpensesModuleExtensions.cs` and SignalR hub handlers live in `src/SplitTrip.Web/Hubs/TripHub.Expenses.cs` (`public partial class TripHub`), without editing `Program.cs` or `TripHub.cs`.

## Objective

Provide an airtight group expense engine supporting five split modes (`Equal`, `ExactAmount`, `Percentage`, `Shares`, `Itemized`) with exact whole-VND penny-remainder reconciliation, **Gemini Vision receipt OCR** with subtotal/total mismatch draft review, and **SignalR live item claiming** with automatic pro-rata tax, tip, and discount distribution.

### Core Domain Rules & Invariants

1. **Strict 5-Layer Access:**
   - `SplitTrip.Web` (`Pages/Trips/Expenses*`, `Hubs/TripHub.Expenses.cs`, `Extensions/ExpensesModuleExtensions.cs`) calls **only** `SplitTrip.Services` interfaces and `SplitTrip.BusinessObjects`.
2. **Membership & Trip Lifecycle Gate:**
   - Caller, `PaidByUserId`, and every `ExpenseSplit.UserId` / `ReceiptItemClaim.UserId` must be active `TripMember`s (`IsActive == true`) of `TripId`.
   - Creating, editing, deleting, or claiming expenses is permitted only while `Trip.Status` is `TripStatus.Planning` or `TripStatus.Ongoing` (read-only in `Settling` and `Completed`, unless reopened by the Trip Owner).
3. **Exact Whole-VND Reconciliation (`Sum(OwedAmount) == TotalAmount`):**
   - `TotalAmount > 0` (whole VND). Whenever breakdown fields are populated, `TotalAmount == SubtotalAmount + TaxOrTipAmount - DiscountAmount` and `DiscountAmount <= SubtotalAmount + TaxOrTipAmount`.
   - **Largest-Remainder Algorithm (`ExpenseSplitCalculator`):**
     - Given integer `TotalAmount` and participant weights $w_i > 0$ ($W = \sum w_i$):
       1. Exact share $r_i = \text{TotalAmount} \times (w_i / W)$.
       2. Floor share $b_i = \lfloor r_i \rfloor$, integer remainder $R = \text{TotalAmount} - \sum b_i$ ($0 \le R < N$).
       3. Sort participants by descending fractional part $(r_i - b_i)$, breaking ties deterministically by `UserId == PaidByUserId ? 0 : 1` then `UserId` ascending, and award `+1 VND` to the first $R$ participants.
   - **Split Mode Specifications:**
     - `Equal (0)`: $N \ge 1$ selected members with `ShareRatioOrWeight = 1m`; splits `TotalAmount` via Largest-Remainder (`±1 VND` max variance).
     - `ExactAmount (1)`: Explicit integer `OwedAmount >= 0` per participant (`ShareRatioOrWeight = OwedAmount`); rejects save if `Sum(OwedAmount) != TotalAmount` and reports the exact VND delta.
     - `Percentage (2)`: Participant percentages `ShareRatioOrWeight` (`0 < p <= 100`, `decimal(18,4)`) must satisfy `Sum(p) == 100m` (tolerance `±0.0001m`); `OwedAmount` reconciled via Largest-Remainder.
     - `Shares (3)`: Positive relative weights `ShareRatioOrWeight > 0` (e.g., `1`, `2`, `0.5`); `OwedAmount` computed proportionally via Largest-Remainder.
     - `Itemized (4)`: Driven by `ReceiptItem`s and `ReceiptItemClaim`s with pro-rata `TaxOrTipAmount` and `DiscountAmount` allocation (see Rule 5).
4. **Gemini Vision Receipt Parsing & `DraftReview` Mismatch Guard (`IAiReceiptScannerService`):**
   - Sends receipt image bytes + MIME type to `Gemini 2.5 Flash` requesting structured JSON: `{ merchantTitle, category, spentDate, items: [{ itemName, quantity, unitPrice, lineTotal }], subtotalAmount, taxOrTipAmount, discountAmount, totalAmount }`.
   - Saves initial `Expense` with `ScanStatus = ReceiptScanStatus.DraftReview`, `SplitMode = ExpenseSplitMode.Itemized`, and ordered `ReceiptItem` rows (`SortOrder = 0..n-1`).
   - **Mismatch Detection & Reconciliation Gate:**
     - Computes `ItemsSum = Sum(ReceiptItem.LineTotal)` and `ExpectedTotal = SubtotalAmount + TaxOrTipAmount - DiscountAmount`.
     - If `ItemsSum != SubtotalAmount` or `ExpectedTotal != TotalAmount`, `ReceiptDraftDto.HasMismatch = true` surfaces a prominent warning banner showing the exact VND discrepancy (`ItemsSum - SubtotalAmount` and `ExpectedTotal - TotalAmount`).
     - Confirming the draft (`ConfirmDraftAndOpenClaimingAsync`: `DraftReview → ClaimingOpen`) is **blocked** until `SubtotalAmount > 0`, `ItemsSum == SubtotalAmount`, and `SubtotalAmount + TaxOrTipAmount - DiscountAmount == TotalAmount > 0`.
5. **Live SignalR Item Claiming & Pro-Rata Tax/Tip/Discount Allocation:**
   - When `ScanStatus == ReceiptScanStatus.ClaimingOpen`, any active `TripMember` can toggle claims on individual `ReceiptItem`s (`Unique(ReceiptItemId, UserId)`). Multiple members claiming the same shared dish split that item's `LineTotal` equally (integer floor + deterministic `+1 VND` remainder ordered by `ClaimedAtUtc`, then `UserId`).
   - Any unclaimed `ReceiptItem` during `ClaimingOpen` temporarily assigns its `LineTotal` to `PaidByUserId` so `Sum(OwedAmount) == TotalAmount` remains invariant on the trip ledger; `ClaimingBoardDto` clearly highlights unclaimed items (`UnclaimedItemCount`).
   - **Pro-Rata Adjustment Formula:**
     - Let $S_u$ be member $u$'s summed item subtotals ($\sum_u S_u = \text{SubtotalAmount}$).
     - Each member with $S_u > 0$ has exact adjusted obligation $r_u = \text{TotalAmount} \times (S_u / \text{SubtotalAmount})$ — equivalently allocating `TaxOrTipAmount - DiscountAmount` strictly pro-rata to $S_u$ — reconciled to whole `VND` via Largest-Remainder so `Sum(ExpenseSplit.OwedAmount) == TotalAmount`.
   - **Locking (`ClaimingOpen → Locked`):** `PaidByUserId` or Trip `Owner` may lock claiming once `UnclaimedItemCount == 0`, freezing `ReceiptItemClaim`s and finalizing splits.

## Tech Stack

- **Runtime & Web:** `.NET 10.0` (`net10.0`), `ASP.NET Core Razor Pages`, `ASP.NET Core SignalR`
- **Data Access:** `EF Core 10 (SQL Server)` via `ExpenseDao` (`SplitTrip.DataAccessObjects`) and `IExpenseRepository` (`SplitTrip.Repositories`)
- **AI Multimodal OCR:** Google Gemini `gemini-2.5-flash` Vision REST API (`IHttpClientFactory` + `System.Text.Json`)
- **Testing:** `xUnit` + `EF Core InMemory` (`tests/SplitTrip.Tests/Expenses/`)

## Commands

```powershell
# Build entire solution
dotnet build SplitTrip.sln --configuration Debug

# Run smart-expenses unit & invariant tests
dotnet test SplitTrip.sln --no-build --filter "FullyQualifiedName~Expenses"

# Run full test suite
dotnet test SplitTrip.sln --no-build --verbosity normal
```

## Project Structure

```text
src/
├── SplitTrip.DataAccessObjects/
│   └── Daos/
│       └── ExpenseDao.cs                         → EF Core persistence for Expense, ExpenseSplit, ReceiptItem, ReceiptItemClaim
├── SplitTrip.Repositories/
│   └── Expenses/
│       ├── IExpenseRepository.cs                 → Repository contract for expense CRUD, draft review, and item claims
│       └── ExpenseRepository.cs                  → Repository implementation wrapping ExpenseDao
├── SplitTrip.Services/
│   └── Expenses/
│       ├── Dtos/ExpenseDtos.cs                   → ExpenseDto, SplitInputDto, ReceiptDraftDto, ClaimBoardDto
│       ├── ExpenseSplitCalculator.cs             → Pure deterministic whole-VND calculator for all 5 split modes + pro-rata adjustments
│       ├── IExpenseService.cs                    → Expense CRUD, draft confirmation, live claim toggle, claiming lock
│       ├── ExpenseService.cs                     → Domain validation, notifications, and ledger-safe split persistence
│       ├── IAiReceiptScannerService.cs           → Multimodal Gemini Vision receipt parser contract
│       └── GeminiReceiptScannerService.cs        → Image base64 encoding, Gemini JSON schema parsing, mismatch calculation
└── SplitTrip.Web/
    ├── Extensions/
    │   └── ExpensesModuleExtensions.cs           → Registers Expense DAOs/Repos/Services and Gemini Vision HttpClient
    ├── Hubs/
    │   └── TripHub.Expenses.cs                   → partial class TripHub for live item claiming & split broadcasts
    └── Pages/Trips/
        ├── Expenses.cshtml                       → Expense feed, 5-mode split form, receipt upload, DraftReview editor, live claiming board
        └── Expenses.cshtml.cs                    → PageModel handlers for expense creation, OCR upload, draft approval, and claim locking
tests/
└── SplitTrip.Tests/
    └── Expenses/
        ├── ExpenseSplitCalculatorTests.cs        → Property/edge tests proving Sum(OwedAmount) == TotalAmount across all 5 modes
        ├── GeminiReceiptScannerServiceTests.cs   → Vision JSON parsing, subtotal/total mismatch warning & confirmation block tests
        └── LiveItemClaimingServiceTests.cs       → Shared dish claiming, pro-rata tax/discount math, unclaimed fallback & lock tests
```

## Code Style

```csharp
namespace SplitTrip.Services.Expenses;

public static class ExpenseSplitCalculator
{
    public static IReadOnlyDictionary<Guid, decimal> AllocateByWeights(
        decimal totalAmount,
        Guid paidByUserId,
        IReadOnlyDictionary<Guid, decimal> weights)
    {
        if (totalAmount <= 0m || decimal.Truncate(totalAmount) != totalAmount)
            throw new ArgumentOutOfRangeException(nameof(totalAmount), "Tổng chi phí phải là số nguyên dương (VNĐ).");

        var positive = weights.Where(kv => kv.Value > 0m).ToList();
        if (positive.Count == 0)
            throw new InvalidOperationException("Phải có ít nhất một thành viên tham gia chia tiền.");

        var totalWeight = positive.Sum(kv => kv.Value);
        var provisional = positive.Select(kv =>
        {
            var exact = totalAmount * kv.Value / totalWeight;
            var floored = decimal.Floor(exact);
            return (UserId: kv.Key, Floored: floored, Remainder: exact - floored);
        }).ToList();

        var remainderVnd = (int)(totalAmount - provisional.Sum(x => x.Floored));
        var winners = provisional
            .OrderByDescending(x => x.Remainder)
            .ThenBy(x => x.UserId == paidByUserId ? 0 : 1)
            .ThenBy(x => x.UserId)
            .Take(remainderVnd)
            .Select(x => x.UserId)
            .ToHashSet();

        return provisional.ToDictionary(
            x => x.UserId,
            x => x.Floored + (winners.Contains(x.UserId) ? 1m : 0m));
    }
}
```

## Testing Strategy

- **Split Calculator Unit Tests (`ExpenseSplitCalculatorTests.cs`):**
  - Prime/unfriendly totals (e.g., `100_000 VND` / `3` members → `33_334`, `33_333`, `33_333`), percentages (`33.3333%`, `33.3333%`, `33.3334%`), unequal weights (`1 : 2 : 4`), exact amount mismatch rejection, and itemized pro-rata tax + discount combinations (`Subtotal = 450_000`, `VAT 8% = 36_000`, `Discount = 50_000`, `Total = 436_000`).
  - Assert `splits.Values.Sum() == totalAmount` and `splits.Values.All(v => v == decimal.Truncate(v) && v >= 0)` on every test case.
- **Receipt OCR & Draft Review Tests (`GeminiReceiptScannerServiceTests.cs`):**
  - Mocked Gemini HTTP responses for clean receipts, receipts where `Sum(LineTotal) != SubtotalAmount`, and missing API key / malformed JSON fallback.
  - Verify `DraftReview` expenses do **not** alter `TripService.GetTripMemberBalancesAsync` until confirmed.
- **Live Item Claiming Tests (`LiveItemClaimingServiceTests.cs`):**
  - Single-member claim, multi-member shared dish split, unclaim toggle, pro-rata tax/tip/discount updates, blocking `LockClaiming` while unclaimed items remain, and rejecting claim mutations once `ScanStatus == Locked`.

## Boundaries

- **Always:**
  - Guarantee `Sum(ExpenseSplit.OwedAmount) == Expense.TotalAmount` in whole `VND` for every persisted non-draft expense.
  - Validate uploaded receipt files (allowed extensions `.jpg`, `.jpeg`, `.png`, `.webp`, magic header bytes, ≤ 10 MB size limit) before saving or forwarding to Gemini.
  - Keep SignalR methods in `src/SplitTrip.Web/Hubs/TripHub.Expenses.cs` and DI registrations in `src/SplitTrip.Web/Extensions/ExpensesModuleExtensions.cs`.
- **Ask first:**
  - Modifying `Expense`, `ExpenseSplit`, `ReceiptItem`, or `ReceiptItemClaim` schema columns after `InitialCreate`.
  - Supporting external cloud blob storage instead of `wwwroot/uploads/receipts`.
- **Never:**
  - Include `ReceiptScanStatus.DraftReview` expenses in member `NetBalance` totals.
  - Use `double` or `float` anywhere in split, percentage, or pro-rata tax/discount math.
  - Reference `SplitTrip.DataAccessObjects` from `SplitTrip.Web` or edit `Program.cs` / `TripHub.cs`.

## Success Criteria

- [ ] Members can create/edit/delete expenses in all 5 modes (`Equal`, `ExactAmount`, `Percentage`, `Shares`, `Itemized`), and `Sum(OwedAmount) == TotalAmount` holds with 0 VND rounding drift.
- [ ] Uploading a receipt image invokes `Gemini 2.5 Flash` Vision into `DraftReview`, highlights any `Sum(LineTotal) != SubtotalAmount` or `Subtotal + TaxOrTip - Discount != TotalAmount` discrepancy, and requires reconciliation before opening claiming.
- [ ] During `ClaimingOpen`, two or more connected browsers can claim/unclaim individual or shared dishes via `SignalR` and immediately see live recalculated `OwedAmount` shares including pro-rata tax/tip/discount.
- [ ] Payer or Trip Owner can lock claiming (`ClaimingOpen → Locked`) once all items are claimed, and `NetBalance` across the trip stays zero-sum (`∑ NetBalance == 0`) throughout.
- [ ] All unit tests in `tests/SplitTrip.Tests/Expenses/` pass (`dotnet test SplitTrip.sln`).
