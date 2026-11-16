using CourtBook.BusinessObjects.Entities;

namespace CourtBook.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(int id, bool includeDetails = false, CancellationToken ct = default);
    Task<Booking?> GetByCodeAsync(string bookingCode, bool includeDetails = false, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsForDateAsync(DateOnly playDate, bool activeOnly = false, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsForVenueAndDateAsync(int venueId, DateOnly playDate, bool activeOnly = false, CancellationToken ct = default);
    Task<Booking?> GetActiveSlotAsync(int courtId, DateOnly playDate, int startHour, CancellationToken ct = default);
    Task<List<Booking>> GetActiveHoldsByUserAsync(int userId, int? venueId, DateTime utcNow, CancellationToken ct = default);
    Task<int> CountActiveHoldsByUserAsync(int userId, DateTime utcNow, int? venueId = null, CancellationToken ct = default);
    Task<List<Booking>> GetExpiredHoldsAsync(DateTime utcNow, CancellationToken ct = default);
    Task<List<Booking>> GetBookingsByUserAsync(int userId, CancellationToken ct = default);
    Task<Booking> AddAsync(Booking booking, CancellationToken ct = default);
    Task<bool> TryAddSlotAsync(Booking booking, CancellationToken ct = default);
    Task UpdateAsync(Booking booking, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<Booking> bookings, CancellationToken ct = default);
}
