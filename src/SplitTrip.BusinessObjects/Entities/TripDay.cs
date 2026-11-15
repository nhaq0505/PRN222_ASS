namespace SplitTrip.BusinessObjects.Entities;

public class TripDay
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TripId { get; set; }
    public int DayNumber { get; set; }
    public DateOnly Date { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }

    public Trip? Trip { get; set; }
    public ICollection<ItineraryItem> Items { get; set; } = new List<ItineraryItem>();
}

public partial class Trip
{
    public ICollection<TripDay> Days { get; set; } = new List<TripDay>();
}
