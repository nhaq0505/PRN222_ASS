namespace SplitTrip.BusinessObjects.Entities;

public class ItineraryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripDayId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? LocationName { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public decimal EstimatedCost { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
    public bool IsAiSuggested { get; set; }
    public DateTime? ReminderSentAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public TripDay? TripDay { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<ItineraryVote> Votes { get; set; } = new List<ItineraryVote>();
}
