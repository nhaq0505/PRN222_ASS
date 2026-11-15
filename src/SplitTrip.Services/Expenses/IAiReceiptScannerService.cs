using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public interface IAiReceiptScannerService
{
    void ValidateReceiptImage(string fileName, string? contentType, ReadOnlySpan<byte> imageBytes);

    Task<ReceiptDraftDto> ScanAndCreateDraftAsync(
        ScanReceiptRequest request,
        CancellationToken ct = default);
}
