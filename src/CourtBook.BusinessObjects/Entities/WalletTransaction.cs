namespace CourtBook.BusinessObjects.Entities;

public class WalletTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public decimal AmountVnd { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
