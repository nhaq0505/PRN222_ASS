using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class BookingDao
{
    private readonly CourtBookDbContext _context;

    public BookingDao(CourtBookDbContext context)
    {
        _context = context;
    }

    public Task<Booking?> GetByIdAsync(int id, bool includeDetails = false, CancellationToken ct = default)
    {
        IQueryable<Booking> query = _context.Bookings;
        if (includeDetails)
        {
            query = query
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Venue)
                .Include(b => b.User);
        }

        return query.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public Task<Booking?> GetByCodeAsync(string bookingCode, bool includeDetails = false, CancellationToken ct = default)
    {
        string normalized = bookingCode.Trim().ToUpperInvariant();
        IQueryable<Booking> query = _context.Bookings.AsNoTracking();
        if (includeDetails)
        {
            query = query
                .Include(b => b.Court)
                    .ThenInclude(c => c!.Venue)
                .Include(b => b.User);
        }

        return query.FirstOrDefaultAsync(b => b.BookingCode.ToUpper() == normalized, ct);
    }

    public Task<List<Booking>> GetBookingsForDateAsync(
        DateOnly playDate,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        IQueryable<Booking> query = _context.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Include(b => b.User)
            .Where(b => b.PlayDate == playDate);

        if (activeOnly)
        {
            query = query.Where(b =>
                b.Status == BookingStatus.Held ||
                b.Status == BookingStatus.Confirmed ||
                b.Status == BookingStatus.CheckedIn);
        }

        return query
            .OrderBy(b => b.StartHour)
            .ThenBy(b => b.CourtId)
            .ToListAsync(ct);
    }

    public Task<List<Booking>> GetBookingsForVenueAndDateAsync(
        int venueId,
        DateOnly playDate,
        bool activeOnly = false,
        CancellationToken ct = default)
    {
        IQueryable<Booking> query = _context.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Include(b => b.User)
            .Where(b => b.PlayDate == playDate && b.Court != null && b.Court.VenueId == venueId);

        if (activeOnly)
        {
            query = query.Where(b =>
                b.Status == BookingStatus.Held ||
                b.Status == BookingStatus.Confirmed ||
                b.Status == BookingStatus.CheckedIn);
        }

        return query
            .OrderBy(b => b.StartHour)
            .ThenBy(b => b.CourtId)
            .ToListAsync(ct);
    }

    public Task<Booking?> GetActiveSlotAsync(
        int courtId,
        DateOnly playDate,
        int startHour,
        CancellationToken ct = default)
        => _context.Bookings
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b =>
                b.CourtId == courtId &&
                b.PlayDate == playDate &&
                b.StartHour == startHour &&
                (b.Status == BookingStatus.Held ||
                 b.Status == BookingStatus.Confirmed ||
                 b.Status == BookingStatus.CheckedIn),
                ct);

    public Task<List<Booking>> GetActiveHoldsByUserAsync(
        int userId,
        int? venueId,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        IQueryable<Booking> query = _context.Bookings
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Where(b =>
                b.UserId == userId &&
                b.Status == BookingStatus.Held &&
                (b.HoldExpiresAtUtc == null || b.HoldExpiresAtUtc > utcNow));

        if (venueId.HasValue)
        {
            query = query.Where(b => b.Court != null && b.Court.VenueId == venueId.Value);
        }

        return query
            .OrderBy(b => b.PlayDate)
            .ThenBy(b => b.StartHour)
            .ThenBy(b => b.CourtId)
            .ToListAsync(ct);
    }

    public Task<int> CountActiveHoldsByUserAsync(
        int userId,
        DateTime utcNow,
        int? venueId = null,
        CancellationToken ct = default)
    {
        IQueryable<Booking> query = _context.Bookings.Where(b =>
            b.UserId == userId &&
            b.Status == BookingStatus.Held &&
            (b.HoldExpiresAtUtc == null || b.HoldExpiresAtUtc > utcNow));

        if (venueId.HasValue)
        {
            query = query.Where(b => b.Court != null && b.Court.VenueId == venueId.Value);
        }

        return query.CountAsync(ct);
    }

    public Task<List<Booking>> GetExpiredHoldsAsync(DateTime utcNow, CancellationToken ct = default)
        => _context.Bookings
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Where(b =>
                b.Status == BookingStatus.Held &&
                b.HoldExpiresAtUtc != null &&
                b.HoldExpiresAtUtc <= utcNow)
            .ToListAsync(ct);

    public Task<List<Booking>> GetBookingsByUserAsync(int userId, CancellationToken ct = default)
        => _context.Bookings
            .AsNoTracking()
            .Include(b => b.Court)
                .ThenInclude(c => c!.Venue)
            .Include(b => b.User)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.PlayDate)
            .ThenByDescending(b => b.StartHour)
            .ThenByDescending(b => b.Id)
            .ToListAsync(ct);

    public async Task<Booking> AddAsync(Booking booking, CancellationToken ct = default)
    {
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync(ct);
        return booking;
    }

    public async Task<bool> TryAddSlotAsync(Booking booking, CancellationToken ct = default)
    {
        _context.Bookings.Add(booking);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _context.Entry(booking).State = EntityState.Detached;
            return false;
        }
    }

    public async Task UpdateAsync(Booking booking, CancellationToken ct = default)
    {
        if (_context.Entry(booking).State == EntityState.Detached)
        {
            _context.Bookings.Update(booking);
        }
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(IEnumerable<Booking> bookings, CancellationToken ct = default)
    {
        foreach (Booking booking in bookings)
        {
            if (_context.Entry(booking).State == EntityState.Detached)
            {
                _context.Bookings.Update(booking);
            }
        }
        await _context.SaveChangesAsync(ct);
    }

    public static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx)
        {
            // 2601 = Cannot insert duplicate key row with unique index
            // 2627 = Violation of UNIQUE KEY / PRIMARY KEY constraint
            return sqlEx.Number == 2601 || sqlEx.Number == 2627;
        }

        return false;
    }
}
