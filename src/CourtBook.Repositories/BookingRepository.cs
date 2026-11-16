using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;

namespace CourtBook.Repositories;

public class BookingRepository : IBookingRepository
{
    private readonly BookingDao _dao;

    public BookingRepository(BookingDao dao)
    {
        _dao = dao;
    }

    public Task<Booking?> GetByIdAsync(int id, bool includeDetails = false, CancellationToken ct = default)
        => _dao.GetByIdAsync(id, includeDetails, ct);

    public Task<Booking?> GetByCodeAsync(string bookingCode, bool includeDetails = false, CancellationToken ct = default)
        => _dao.GetByCodeAsync(bookingCode, includeDetails, ct);

    public Task<List<Booking>> GetBookingsForDateAsync(DateOnly playDate, bool activeOnly = false, CancellationToken ct = default)
        => _dao.GetBookingsForDateAsync(playDate, activeOnly, ct);

    public Task<List<Booking>> GetBookingsForVenueAndDateAsync(
        int venueId,
        DateOnly playDate,
        bool activeOnly = false,
        CancellationToken ct = default)
        => _dao.GetBookingsForVenueAndDateAsync(venueId, playDate, activeOnly, ct);

    public Task<Booking?> GetActiveSlotAsync(int courtId, DateOnly playDate, int startHour, CancellationToken ct = default)
        => _dao.GetActiveSlotAsync(courtId, playDate, startHour, ct);

    public Task<List<Booking>> GetActiveHoldsByUserAsync(int userId, int? venueId, DateTime utcNow, CancellationToken ct = default)
        => _dao.GetActiveHoldsByUserAsync(userId, venueId, utcNow, ct);

    public Task<int> CountActiveHoldsByUserAsync(int userId, DateTime utcNow, int? venueId = null, CancellationToken ct = default)
        => _dao.CountActiveHoldsByUserAsync(userId, utcNow, venueId, ct);

    public Task<List<Booking>> GetActiveHoldsByCourtAsync(int courtId, DateTime utcNow, CancellationToken ct = default)
        => _dao.GetActiveHoldsByCourtAsync(courtId, utcNow, ct);

    public Task<List<Booking>> GetActiveHoldsByVenueAsync(int venueId, DateTime utcNow, CancellationToken ct = default)
        => _dao.GetActiveHoldsByVenueAsync(venueId, utcNow, ct);

    public Task<int> CountUpcomingConfirmedByCourtAsync(int courtId, DateOnly todayVn, int currentHourVn, CancellationToken ct = default)
        => _dao.CountUpcomingConfirmedByCourtAsync(courtId, todayVn, currentHourVn, ct);

    public Task<List<Booking>> GetExpiredHoldsAsync(DateTime utcNow, CancellationToken ct = default)
        => _dao.GetExpiredHoldsAsync(utcNow, ct);

    public Task<List<Booking>> GetBookingsByUserAsync(int userId, CancellationToken ct = default)
        => _dao.GetBookingsByUserAsync(userId, ct);

    public Task<List<Booking>> GetBookingsByVenueAsync(int venueId, CancellationToken ct = default)
        => _dao.GetBookingsByVenueAsync(venueId, ct);

    public Task<Booking> AddAsync(Booking booking, CancellationToken ct = default)
        => _dao.AddAsync(booking, ct);

    public Task<bool> TryAddSlotAsync(Booking booking, CancellationToken ct = default)
        => _dao.TryAddSlotAsync(booking, ct);

    public Task UpdateAsync(Booking booking, CancellationToken ct = default)
        => _dao.UpdateAsync(booking, ct);

    public Task UpdateRangeAsync(IEnumerable<Booking> bookings, CancellationToken ct = default)
        => _dao.UpdateRangeAsync(bookings, ct);
}
