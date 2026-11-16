using CourtBook.BusinessObjects.Enums;

namespace CourtBook.Services.Models;

public enum SlotVisualState
{
    Available = 0,
    HeldByMe = 1,
    HeldByOther = 2,
    BookedByMe = 3,
    BookedByOther = 4,
    Maintenance = 5,
    Past = 6
}

public class SlotCellDto
{
    public int CourtId { get; set; }
    public int StartHour { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
    public string TimeRangeLabel { get; set; } = string.Empty;
    public decimal PriceVnd { get; set; }
    public bool IsPeakHour { get; set; }
    public SlotVisualState State { get; set; }
    public int? BookingId { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public string? HolderDisplayName { get; set; }
}

public class CourtRowDto
{
    public int CourtId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SportType SportType { get; set; }
    public bool IsIndoor { get; set; }
    public string Surface { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal BasePricePerHour { get; set; }
    public decimal PeakPricePerHour { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<SlotCellDto> Slots { get; set; } = Array.Empty<SlotCellDto>();
}

public class HeldItemDto
{
    public int BookingId { get; set; }
    public int VenueId { get; set; }
    public int CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public DateOnly PlayDate { get; set; }
    public int StartHour { get; set; }
    public string TimeRangeLabel { get; set; } = string.Empty;
    public decimal PriceVnd { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
}

public class HeldSummaryDto
{
    public int Count { get; set; }
    public decimal TotalPriceVnd { get; set; }
    public DateTime? EarliestExpiresAtUtc { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public IReadOnlyList<HeldItemDto> Items { get; set; } = Array.Empty<HeldItemDto>();
}

public class ScheduleMatrixDto
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string VenueDistrict { get; set; } = string.Empty;
    public string VenueAddress { get; set; } = string.Empty;
    public string VenuePhone { get; set; } = string.Empty;
    public string VenueDescription { get; set; } = string.Empty;
    public string VenueCoverImageUrl { get; set; } = string.Empty;
    public IReadOnlyList<string> VenuePhotoUrls { get; set; } = Array.Empty<string>();
    public int OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int OpenHour { get; set; } = 6;
    public int CloseHour { get; set; } = 22;
    public DateOnly PlayDate { get; set; }
    public SportType? SportFilter { get; set; }
    public IReadOnlyList<int> Hours { get; set; } = Array.Empty<int>();
    public IReadOnlyList<CourtRowDto> Courts { get; set; } = Array.Empty<CourtRowDto>();
    public HeldSummaryDto MyHolds { get; set; } = new();
}

public class SlotUpdateBroadcastDto
{
    public int VenueId { get; set; }
    public string PlayDate { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public int StartHour { get; set; }
    public BookingStatus Status { get; set; }
    public int? BookingId { get; set; }
    public int? UserId { get; set; }
    public decimal PriceVnd { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public bool IsCourtActive { get; set; }
}

public class ToggleHoldResponseDto
{
    public SlotUpdateBroadcastDto UpdatedSlot { get; set; } = new();
    public HeldSummaryDto MyHolds { get; set; } = new();
    public string Action { get; set; } = string.Empty;
}
