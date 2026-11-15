using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Tests.Expenses;

public sealed class LiveItemClaimingServiceTests
{
    [Fact]
    public async Task LiveClaiming_SharedDishesAndProRataVatDiscount_ReconcilesExactVndAndLocksCleanly()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, bob, charlie) = await harness.SeedTripWithThreeMembersAsync();
        var scanner = harness.CreateScanner(apiKey: string.Empty);

        var initialDraft = await scanner.ScanAndCreateDraftAsync(new ScanReceiptRequest(
            TripId: tripId,
            ActorUserId: alice,
            PaidByUserId: alice,
            UseDemoSample: true));

        // Customize draft to SPEC test case:
        // Subtotal = 450,000 VND (Item 1: 150,000 solo, Item 2: 200,000 solo, Item 3: 100,000 shared by all 3!)
        // VAT 8% = +36,000 VND, Discount = -50,000 VND -> Total = 436,000 VND
        var customDraft = new UpdateReceiptDraftRequest(
            ExpenseId: initialDraft.ExpenseId,
            Title: "Hải sản Biển Đông",
            Category: ExpenseCategory.Food,
            PaidByUserId: alice,
            SubtotalAmount: 450_000m,
            TaxOrTipAmount: 36_000m,
            DiscountAmount: 50_000m,
            TotalAmount: 436_000m,
            Items:
            [
                new DraftReceiptItemInput("Mực hấp gừng", 1, 150_000m, 150_000m),
                new DraftReceiptItemInput("Cua rang me", 1, 200_000m, 200_000m),
                new DraftReceiptItemInput("Lẩu hải sản dùng chung", 1, 100_000m, 100_000m)
            ]);

        var board = await harness.ExpenseService.ConfirmDraftAndOpenClaimingAsync(
            initialDraft.ExpenseId,
            alice,
            customDraft);

        Assert.Equal(ReceiptScanStatus.ClaimingOpen, board.ScanStatus);
        Assert.Equal(3, board.UnclaimedItemCount);
        Assert.Equal(436_000m, board.MemberPreviews.Sum(m => m.LedgerOwedAmount));

        // Locking before all items are claimed must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.FinalizeReceiptClaimsAsync(board.ExpenseId, alice));

        var item1 = board.Items[0].Id; // 150,000 -> Alice solo
        var item2 = board.Items[1].Id; // 200,000 -> Bob solo
        var item3 = board.Items[2].Id; // 100,000 -> Shared by Alice, Bob, Charlie

        await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item1, alice);
        await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item2, bob);

        // Toggle on then off then on to verify unclaim toggle works cleanly
        await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, charlie);
        var afterUnclaim = await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, charlie);
        Assert.Equal(1, afterUnclaim.UnclaimedItemCount);

        // All 3 co-claim the 100,000 VND shared hotpot in deterministic chronological order
        await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, alice);
        await Task.Delay(10);
        await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, bob);
        await Task.Delay(10);
        var allClaimedBoard = await harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, charlie);

        Assert.True(allClaimedBoard.AllItemsClaimed);
        Assert.Equal(0, allClaimedBoard.UnclaimedItemCount);
        Assert.Equal(436_000m, allClaimedBoard.MemberPreviews.Sum(m => m.LedgerOwedAmount));

        // Non-payer / non-owner (Bob) cannot lock claiming
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.ExpenseService.FinalizeReceiptClaimsAsync(board.ExpenseId, bob));

        // Payer (Alice) locks & finalizes
        var lockedBoard = await harness.ExpenseService.FinalizeReceiptClaimsAsync(board.ExpenseId, alice);
        Assert.Equal(ReceiptScanStatus.Locked, lockedBoard.ScanStatus);

        var summary = await harness.ExpenseService.GetExpenseByIdAsync(board.ExpenseId, alice);
        Assert.Equal(ReceiptScanStatus.Locked, summary.ScanStatus);
        Assert.Equal(436_000m, summary.Splits.Sum(s => s.OwedAmount));
        Assert.All(summary.Splits, s => Assert.Equal(decimal.Truncate(s.OwedAmount), s.OwedAmount));

        // Verify exact pro-rata math:
        // Shared hotpot 100,000 / 3 -> Alice 33,334, Bob 33,333, Charlie 33,333
        // Dish subtotals: Alice = 183,334 | Bob = 233,333 | Charlie = 33,333 (Sum = 450,000)
        // Pro-rata of 436,000:
        // Alice: 436,000 * 183,334 / 450,000 = 177,630.2755... -> 177,630
        // Bob:   436,000 * 233,333 / 450,000 = 226,073.7511... -> 226,074 (+1 largest remainder)
        // Charlie: 436,000 * 33,333 / 450,000 = 32,295.9733... -> 32,296 (+1 largest remainder)
        // Sum = 177,630 + 226,074 + 32,296 = 436,000 VND!
        Assert.Equal(177_630m, summary.Splits.Single(s => s.UserId == alice).OwedAmount);
        Assert.Equal(226_074m, summary.Splits.Single(s => s.UserId == bob).OwedAmount);
        Assert.Equal(32_296m, summary.Splits.Single(s => s.UserId == charlie).OwedAmount);

        // Mutations after Locked must be rejected
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.ToggleItemClaimAsync(board.ExpenseId, item3, charlie));

        // Trip NetBalance must remain strictly zero-sum
        var balances = await harness.TripService.GetTripMemberBalancesAsync(tripId);
        Assert.Equal(0m, balances.Sum(b => b.NetBalance));
    }
}
