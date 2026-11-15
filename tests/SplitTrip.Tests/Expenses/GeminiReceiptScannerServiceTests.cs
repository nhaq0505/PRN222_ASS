using System.Net;
using System.Text;
using System.Text.Json;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Tests.Expenses;

public sealed class GeminiReceiptScannerServiceTests
{
    [Fact]
    public async Task ScanWithoutApiKey_UsesSmartVietnameseHotpotFallback_AndExcludesDraftFromNetBalance()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, _, _) = await harness.SeedTripWithThreeMembersAsync();
        var scanner = harness.CreateScanner(apiKey: string.Empty);

        var draft = await scanner.ScanAndCreateDraftAsync(new ScanReceiptRequest(
            TripId: tripId,
            ActorUserId: alice,
            PaidByUserId: alice,
            ImageBytes: ExpensesTestHarness.ValidJpegBytes(),
            FileName: "hotpot.jpg",
            ContentType: "image/jpeg"));

        Assert.True(draft.IsDemoFallback);
        Assert.Equal(ReceiptScanStatus.DraftReview, draft.ScanStatus);
        Assert.False(draft.HasMismatch);
        Assert.Equal(820_000m, draft.SubtotalAmount);
        Assert.Equal(65_600m, draft.TaxOrTipAmount);
        Assert.Equal(50_000m, draft.DiscountAmount);
        Assert.Equal(835_600m, draft.TotalAmount);
        Assert.Equal(5, draft.Items.Count);

        // DraftReview MUST NOT alter trip NetBalance before confirmation
        var balances = await harness.TripService.GetTripMemberBalancesAsync(tripId);
        Assert.All(balances, b => Assert.Equal(0m, b.NetBalance));
    }

    [Fact]
    public async Task ScanWithGeminiMismatchResponse_FlagsMismatchWarning_AndBlocksPublishUntilReconciled()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, _, _) = await harness.SeedTripWithThreeMembersAsync();

        var mockedOcrJson = JsonSerializer.Serialize(new
        {
            merchantTitle = "Quán Nướng Đà Lạt",
            category = "Food",
            spentDate = "2026-11-15",
            items = new[]
            {
                new { itemName = "Bò nướng tảng", quantity = 1, unitPrice = 200_000, lineTotal = 200_000 },
                new { itemName = "Lẩu gà lá é", quantity = 1, unitPrice = 250_000, lineTotal = 250_000 }
            },
            // Intentional OCR discrepancy: items sum = 450,000, VAT = 36,000, Discount = 0 -> expected 486,000, but OCR read totalAmount = 500,000!
            subtotalAmount = 450_000,
            taxOrTipAmount = 36_000,
            discountAmount = 0,
            totalAmount = 500_000
        });

        var geminiEnvelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[] { new { text = mockedOcrJson } }
                    }
                }
            }
        });

        var scanner = harness.CreateScanner(
            apiKey: "fake-test-gemini-key",
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(geminiEnvelope, Encoding.UTF8, "application/json")
            });

        var draft = await scanner.ScanAndCreateDraftAsync(new ScanReceiptRequest(
            TripId: tripId,
            ActorUserId: alice,
            PaidByUserId: alice,
            ImageBytes: ExpensesTestHarness.ValidPngBytes(),
            FileName: "receipt.png",
            ContentType: "image/png"));

        Assert.False(draft.IsDemoFallback);
        Assert.True(draft.HasMismatch);
        Assert.NotNull(draft.MismatchWarningMessage);
        Assert.Equal(-14_000m, draft.TotalDelta);

        // Publishing while mismatched must be blocked
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ExpenseService.ConfirmDraftAndOpenClaimingAsync(draft.ExpenseId, alice));

        // Reconciling draft by adding missing 14,000 VND wet-tissue / drink line allows publishing
        var reconciledEdit = new UpdateReceiptDraftRequest(
            ExpenseId: draft.ExpenseId,
            Title: draft.Title,
            Category: ExpenseCategory.Food,
            PaidByUserId: alice,
            SubtotalAmount: 464_000m,
            TaxOrTipAmount: 36_000m,
            DiscountAmount: 0m,
            TotalAmount: 500_000m,
            Items:
            [
                new DraftReceiptItemInput("Bò nướng tảng", 1, 200_000m, 200_000m),
                new DraftReceiptItemInput("Lẩu gà lá é", 1, 250_000m, 250_000m),
                new DraftReceiptItemInput("Khăn lạnh & nước suối", 2, 7_000m, 14_000m)
            ]);

        var board = await harness.ExpenseService.ConfirmDraftAndOpenClaimingAsync(draft.ExpenseId, alice, reconciledEdit);
        Assert.Equal(ReceiptScanStatus.ClaimingOpen, board.ScanStatus);
        Assert.Equal(3, board.UnclaimedItemCount);
    }

    [Fact]
    public async Task ScanWithMalformedGeminiJson_GracefullyFallsBackToDemoExtraction()
    {
        await using var harness = new ExpensesTestHarness();
        var (tripId, alice, _, _) = await harness.SeedTripWithThreeMembersAsync();

        var invalidEnvelope = """{"candidates":[{"content":{"parts":[{"text":"NOT_VALID_JSON_AT_ALL"}]}}]}""";
        var scanner = harness.CreateScanner(
            apiKey: "fake-test-gemini-key",
            handlerFunc: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(invalidEnvelope, Encoding.UTF8, "application/json")
            });

        var draft = await scanner.ScanAndCreateDraftAsync(new ScanReceiptRequest(
            TripId: tripId,
            ActorUserId: alice,
            ImageBytes: ExpensesTestHarness.ValidWebpBytes(),
            FileName: "blurry.webp",
            ContentType: "image/webp"));

        Assert.True(draft.IsDemoFallback);
        Assert.Equal(ReceiptScanStatus.DraftReview, draft.ScanStatus);
        Assert.NotEmpty(draft.Items);
    }

    [Fact]
    public void ValidateReceiptImage_RejectsInvalidExtensionsAndSpoofedMagicBytes()
    {
        using var httpClient = new HttpClient();
        var scanner = new SplitTrip.Services.Expenses.GeminiReceiptScannerService(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new SplitTrip.BusinessObjects.Options.GeminiOptions()),
            null!,
            null!,
            null!);

        // Disallowed extension
        Assert.Throws<ArgumentException>(() =>
            scanner.ValidateReceiptImage("malware.exe", "image/jpeg", ExpensesTestHarness.ValidJpegBytes()));

        // Spoofed magic header bytes ("MZ" executable disguised as .jpg)
        byte[] spoofedBytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];
        Assert.Throws<ArgumentException>(() =>
            scanner.ValidateReceiptImage("receipt.jpg", "image/jpeg", spoofedBytes));
    }
}
