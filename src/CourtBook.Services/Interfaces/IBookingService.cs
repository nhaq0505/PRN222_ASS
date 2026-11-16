using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Models;

namespace CourtBook.Services.Interfaces;

public interface IBookingService
{
    Task<ScheduleMatrixDto?> GetScheduleMatrixAsync(
        int venueId,
        DateOnly playDate,
        SportType? sportFilter = null,
        int? currentUserId = null,
        CancellationToken ct = default);

    Task<HeldSummaryDto> GetMyHeldSummaryAsync(int userId, int venueId, CancellationToken ct = default);

    Task<ServiceResult<ToggleHoldResponseDto>> ToggleHoldSlotAsync(
        int userId,
        int courtId,
        DateOnly playDate,
        int startHour,
        CancellationToken ct = default);

    Task<ServiceResult<IReadOnlyList<BookingTicketDto>>> ConfirmMyHoldsAsync(
        int userId,
        int venueId,
        string? notes = null,
        CancellationToken ct = default);

    Task<ServiceResult<IReadOnlyList<BookingTicketDto>>> ConfirmMyHoldsAsync(
        int userId,
        int venueId,
        string? notes,
        string? paymentMethod,
        CancellationToken ct = default);

    Task<ServiceResult<bool>> TogglePaymentStatusAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default);

    Task<byte[]> ExportOwnerBookingsCsvAsync(
        int venueId,
        int actorUserId,
        UserRole actorRole,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default);

    Task<ServiceResult<BookingTicketDto>> QuickCheckInByCodeAsync(
        string bookingCode,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default);

    Task<UserBookingsPageDto> GetUserBookingsAsync(int userId, CancellationToken ct = default);

    Task<ServiceResult<BookingTicketDto>> CancelBookingAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default);

    Task<ServiceResult<BookingTicketDto>> CheckInBookingAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default);

    Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct = default);

    Task<OwnerPortalDto> GetOwnerPortalAsync(
        int ownerUserId,
        int? selectedVenueId,
        DateOnly selectedDate,
        CancellationToken ct = default);
}
