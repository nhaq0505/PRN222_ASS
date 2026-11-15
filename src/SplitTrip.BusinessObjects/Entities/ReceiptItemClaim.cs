namespace SplitTrip.BusinessObjects.Entities;

public class ReceiptItemClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptItemId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;

    public ReceiptItem? ReceiptItem { get; set; }
    public User? User { get; set; }
}
