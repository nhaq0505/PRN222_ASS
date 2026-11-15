using SplitTrip.BusinessObjects.Enums;

namespace SplitTrip.BusinessObjects.Entities;

public class Settlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public decimal Amount { get; set; }
    public SettlementStatus Status { get; set; } = SettlementStatus.Pending;
    public string TransferNote { get; set; } = string.Empty;
    public string? ProofNote { get; set; }
    public DateTime? MarkedPaidAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public string? DisputeReason { get; set; }
    public DateTime? LastNudgedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Trip? Trip { get; set; }
    public User? FromUser { get; set; }
    public User? ToUser { get; set; }
}

public partial class Trip
{
    public ICollection<Settlement> Settlements { get; set; } = new List<Settlement>();
}
