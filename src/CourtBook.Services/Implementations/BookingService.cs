using System.Globalization;
using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Helpers;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class BookingService : IBookingService
{
    public const int MaxActiveHoldsPerUser = 6;
    public const int MemberCancelLeadHours = 2;
    public const int MaxAdvanceBookingDays = 30;
    private const string SelfDealingBookingMessage =
        "Bạn là chủ sở hữu cụm sân này, không thể tự đặt lịch trên sân của chính mình.";
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(2);
    private static readonly SemaphoreSlim SlotLock = new(1, 1);

    private readonly IBookingRepository _bookingRepository;
    private readonly ICourtRepository _courtRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISlotRealtimeNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public BookingService(
        IBookingRepository bookingRepository,
        ICourtRepository courtRepository,
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        ISlotRealtimeNotifier notifier,
        TimeProvider? timeProvider = null)
    {
        _bookingRepository = bookingRepository;
        _courtRepository = courtRepository;
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _notifier = notifier;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ScheduleMatrixDto?> GetScheduleMatrixAsync(
        int venueId,
        DateOnly playDate,
        SportType? sportFilter = null,
        int? currentUserId = null,
        CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);

        Venue? venue = await _venueRepository.GetByIdWithDetailsAsync(venueId, ct);
        if (venue is null || venue.Status != VenueStatus.Active)
        {
            return null;
        }

        int[] operatingHours = venue.GetOperatingHours();
        List<Court> courts = await _courtRepository.GetByVenueAsync(venueId, activeOnly: false, sportFilter: sportFilter, ct);
        List<Booking> bookings = await _bookingRepository.GetBookingsForVenueAndDateAsync(venueId, playDate, activeOnly: true, ct);

        DateTime utcNow = GetUtcNow();
        DateTime nowVn = GetVietnamNow(utcNow);
        DateOnly todayVn = DateOnly.FromDateTime(nowVn);

        var bookingLookup = bookings
            .GroupBy(b => (b.CourtId, b.StartHour))
            .ToDictionary(g => g.Key, g => g.First());

        var courtRows = new List<CourtRowDto>(courts.Count);

        foreach (Court court in courts)
        {
            var slots = new List<SlotCellDto>(operatingHours.Length);

            foreach (int hour in operatingHours)
            {
                bookingLookup.TryGetValue((court.Id, hour), out Booking? active);
                decimal priceVnd = court.GetPriceForHour(hour);
                SlotVisualState state = ResolveVisualState(court, playDate, hour, active, currentUserId, utcNow, todayVn, nowVn);

                bool isHold = active is not null && active.Status == BookingStatus.Held;
                slots.Add(new SlotCellDto
                {
                    CourtId = court.Id,
                    StartHour = hour,
                    TimeLabel = $"{hour:00}:00",
                    TimeRangeLabel = FormatTimeRange(hour),
                    PriceVnd = active?.PriceVnd ?? priceVnd,
                    IsPeakHour = Court.IsPeakHour(hour),
                    State = state,
                    BookingId = active?.Id,
                    HoldExpiresAtUtc = isHold ? AsUtc(active!.HoldExpiresAtUtc) : null,
                    HolderDisplayName = active?.User?.FullName
                });
            }

            courtRows.Add(new CourtRowDto
            {
                CourtId = court.Id,
                Code = court.Code,
                Name = court.Name,
                SportType = court.SportType,
                IsIndoor = court.IsIndoor,
                Surface = court.Surface,
                ImageUrl = court.ImageUrl,
                BasePricePerHour = court.BasePricePerHour,
                PeakPricePerHour = court.PeakPricePerHour,
                IsActive = court.IsActive,
                Slots = slots
            });
        }

        HeldSummaryDto myHolds = currentUserId.HasValue
            ? await BuildHeldSummaryAsync(currentUserId.Value, venueId, utcNow, ct)
            : new HeldSummaryDto();

        return new ScheduleMatrixDto
        {
            VenueId = venue.Id,
            VenueName = venue.Name,
            VenueDistrict = venue.District,
            VenueAddress = venue.Address,
            VenuePhone = venue.PhoneNumber,
            VenueDescription = venue.Description,
            VenueCoverImageUrl = venue.CoverImageUrl,
            VenuePhotoUrls = venue.GetAllPhotos(),
            OwnerId = venue.OwnerId,
            OwnerName = venue.Owner?.FullName ?? string.Empty,
            OpenHour = venue.OpenHour,
            CloseHour = venue.CloseHour,
            PlayDate = playDate,
            SportFilter = sportFilter,
            Hours = operatingHours,
            Courts = courtRows,
            MyHolds = myHolds
        };
    }

    public async Task<HeldSummaryDto> GetMyHeldSummaryAsync(int userId, int venueId, CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);
        return await BuildHeldSummaryAsync(userId, venueId, GetUtcNow(), ct);
    }

    public async Task<ServiceResult<ToggleHoldResponseDto>> ToggleHoldSlotAsync(
        int userId,
        int courtId,
        DateOnly playDate,
        int startHour,
        CancellationToken ct = default)
    {
        await SlotLock.WaitAsync(ct);
        try
        {
            string? actorError = await AuthorizeBookingCustomerAsync(userId, ct);
            if (actorError is not null)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail(actorError);
            }

            await ReleaseExpiredHoldsInternalAsync(ct);

            Court? court = await _courtRepository.GetByIdAsync(courtId, ct);
            if (court is null)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail("Không tìm thấy sân.");
            }

            Venue? venue = court.Venue ?? await _venueRepository.GetByIdAsync(court.VenueId, ct);
            if (venue is null || venue.Status != VenueStatus.Active)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail("Cụm sân này hiện không mở nhận đặt lịch.");
            }

            if (venue.OwnerId == userId)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail(SelfDealingBookingMessage);
            }

            if (!court.IsActive)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail("Sân hiện đang bảo trì.");
            }

            if (startHour < venue.OpenHour || startHour >= venue.CloseHour)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail(
                    $"Khung giờ không hợp lệ (cụm sân mở cửa {venue.OpenHour:00}:00 - {venue.CloseHour:00}:00).");
            }

            DateTime utcNow = GetUtcNow();
            DateTime nowVn = GetVietnamNow(utcNow);
            Booking? existing = await _bookingRepository.GetActiveSlotAsync(courtId, playDate, startHour, ct);

            if (existing is not null)
            {
                if (existing.Status == BookingStatus.Held && existing.UserId == userId)
                {
                    existing.Status = BookingStatus.Cancelled;
                    existing.CancelledAtUtc = utcNow;
                    existing.HoldExpiresAtUtc = null;
                    await _bookingRepository.UpdateAsync(existing, ct);

                    SlotUpdateBroadcastDto releasedDto = BuildBroadcastDto(
                        playDate,
                        court,
                        startHour,
                        BookingStatus.Cancelled,
                        existing.Id,
                        userId,
                        court.GetPriceForHour(startHour),
                        null);

                    await _notifier.NotifySlotUpdatedAsync(releasedDto);
                    HeldSummaryDto summaryAfterRelease = await BuildHeldSummaryAsync(userId, court.VenueId, utcNow, ct);

                    return ServiceResult<ToggleHoldResponseDto>.Ok(new ToggleHoldResponseDto
                    {
                        UpdatedSlot = releasedDto,
                        MyHolds = summaryAfterRelease,
                        Action = "released"
                    });
                }

                string conflictMsg = existing.Status == BookingStatus.Held
                    ? "Khung giờ này đang được người khác giữ chỗ."
                    : "Khung giờ này đã có người đặt.";
                return ServiceResult<ToggleHoldResponseDto>.Fail(conflictMsg);
            }

            if (IsSlotPastOrStarted(playDate, startHour, nowVn))
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail("Không thể đặt khung giờ đã qua.");
            }

            DateOnly maxAdvanceDate = DateOnly.FromDateTime(nowVn).AddDays(MaxAdvanceBookingDays);
            if (playDate > maxAdvanceDate)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail(
                    $"Chỉ mở đặt lịch trước tối đa {MaxAdvanceBookingDays} ngày.");
            }

            int currentHoldCount = await _bookingRepository.CountActiveHoldsByUserAsync(userId, utcNow, court.VenueId, ct);
            if (currentHoldCount >= MaxActiveHoldsPerUser)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail(
                    $"Bạn chỉ có thể giữ tối đa {MaxActiveHoldsPerUser} khung giờ cùng lúc tại cụm sân này.");
            }

            decimal priceVnd = court.GetPriceForHour(startHour);
            DateTime expiresUtc = utcNow.Add(HoldDuration);

            var hold = new Booking
            {
                BookingCode = GenerateBookingCode(playDate),
                CourtId = court.Id,
                UserId = userId,
                PlayDate = playDate,
                StartHour = startHour,
                PriceVnd = priceVnd,
                Status = BookingStatus.Held,
                HoldExpiresAtUtc = expiresUtc,
                CreatedAtUtc = utcNow
            };

            bool added = await _bookingRepository.TryAddSlotAsync(hold, ct);
            if (!added)
            {
                return ServiceResult<ToggleHoldResponseDto>.Fail("Khung giờ này vừa có người khác giữ chỗ.");
            }

            SlotUpdateBroadcastDto heldDto = BuildBroadcastDto(
                playDate,
                court,
                startHour,
                BookingStatus.Held,
                hold.Id,
                userId,
                priceVnd,
                expiresUtc);

            await _notifier.NotifySlotUpdatedAsync(heldDto);
            HeldSummaryDto summaryAfterHold = await BuildHeldSummaryAsync(userId, court.VenueId, utcNow, ct);

            return ServiceResult<ToggleHoldResponseDto>.Ok(new ToggleHoldResponseDto
            {
                UpdatedSlot = heldDto,
                MyHolds = summaryAfterHold,
                Action = "held"
            });
        }
        finally
        {
            SlotLock.Release();
        }
    }

    public Task<ServiceResult<IReadOnlyList<BookingTicketDto>>> ConfirmMyHoldsAsync(
        int userId,
        int venueId,
        string? notes = null,
        CancellationToken ct = default)
        => ConfirmMyHoldsAsync(userId, venueId, notes, "Tại sân", ct);

    public async Task<ServiceResult<IReadOnlyList<BookingTicketDto>>> ConfirmMyHoldsAsync(
        int userId,
        int venueId,
        string? notes,
        string? paymentMethod,
        CancellationToken ct = default)
    {
        await SlotLock.WaitAsync(ct);
        try
        {
            string? actorError = await AuthorizeBookingCustomerAsync(userId, ct);
            if (actorError is not null)
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(actorError);
            }

            await ReleaseExpiredHoldsInternalAsync(ct);

            Venue? venue = await _venueRepository.GetByIdAsync(venueId, ct);
            if (venue is null || venue.Status != VenueStatus.Active)
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(
                    "Cụm sân này hiện không mở nhận đặt lịch.");
            }

            if (venue.OwnerId == userId)
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(SelfDealingBookingMessage);
            }

            DateTime utcNow = GetUtcNow();
            DateTime nowVn = GetVietnamNow(utcNow);
            List<Booking> activeHolds = await _bookingRepository.GetActiveHoldsByUserAsync(userId, venueId, utcNow, ct);

            if (activeHolds.Count == 0)
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(
                    "Bạn chưa chọn khung giờ nào hoặc thời gian giữ chỗ đã hết hạn.");
            }

            if (activeHolds.Any(h => h.Court is not null && !h.Court.IsActive))
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(
                    "Một số sân trong danh sách giữ chỗ hiện đang bảo trì.");
            }

            if (activeHolds.Any(h => IsSlotPastOrStarted(h.PlayDate, h.StartHour, nowVn)))
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(
                    "Một hoặc nhiều khung giờ giữ chỗ đã bắt đầu hoặc trôi qua. Vui lòng chọn lại.");
            }

            if (!TryNormalizePaymentMethod(paymentMethod, out string selectedPayment))
            {
                return ServiceResult<IReadOnlyList<BookingTicketDto>>.Fail(
                    "Phương thức thanh toán không hợp lệ. Chỉ hỗ trợ 'Tại sân' hoặc 'Chuyển khoản VietQR'.");
            }

            string? cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            var broadcasts = new List<SlotUpdateBroadcastDto>(activeHolds.Count);

            foreach (Booking hold in activeHolds)
            {
                hold.Status = BookingStatus.Confirmed;
                hold.ConfirmedAtUtc = utcNow;
                hold.HoldExpiresAtUtc = null;
                hold.PaymentMethod = selectedPayment;
                hold.IsPaid = false; // Initial state pending owner payment verification or quick check-in
                if (cleanNotes is not null)
                {
                    hold.Notes = cleanNotes;
                }

                broadcasts.Add(new SlotUpdateBroadcastDto
                {
                    VenueId = hold.Court?.VenueId ?? venueId,
                    PlayDate = hold.PlayDate.ToString("yyyy-MM-dd"),
                    CourtId = hold.CourtId,
                    StartHour = hold.StartHour,
                    Status = BookingStatus.Confirmed,
                    BookingId = hold.Id,
                    UserId = hold.UserId,
                    PriceVnd = hold.PriceVnd,
                    HoldExpiresAtUtc = null,
                    IsCourtActive = hold.Court?.IsActive ?? true
                });
            }

            await _bookingRepository.UpdateRangeAsync(activeHolds, ct);
            await _notifier.NotifySlotsUpdatedAsync(broadcasts);

            IReadOnlyList<BookingTicketDto> tickets = activeHolds
                .Select(b => MapTicket(
                    b,
                    canCancel: CanMemberSelfCancel(b.PlayDate, b.StartHour, nowVn),
                    canCheckIn: false))
                .ToList();

            return ServiceResult<IReadOnlyList<BookingTicketDto>>.Ok(tickets);
        }
        finally
        {
            SlotLock.Release();
        }
    }

    public async Task<UserBookingsPageDto> GetUserBookingsAsync(int userId, CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);

        DateTime utcNow = GetUtcNow();
        DateTime nowVn = GetVietnamNow(utcNow);
        DateOnly todayVn = DateOnly.FromDateTime(nowVn);

        List<Booking> bookings = await _bookingRepository.GetBookingsByUserAsync(userId, ct);

        var upcoming = new List<BookingTicketDto>();
        var history = new List<BookingTicketDto>();

        foreach (Booking booking in bookings)
        {
            if (booking.Status == BookingStatus.Held)
            {
                continue;
            }

            bool isUpcomingSlot = booking.PlayDate > todayVn ||
                                  (booking.PlayDate == todayVn && booking.StartHour >= nowVn.Hour);

            if (booking.Status == BookingStatus.Confirmed && isUpcomingSlot)
            {
                bool canCancel = CanMemberSelfCancel(booking.PlayDate, booking.StartHour, nowVn);
                upcoming.Add(MapTicket(booking, canCancel: canCancel, canCheckIn: false));
            }
            else
            {
                history.Add(MapTicket(booking, canCancel: false, canCheckIn: false));
            }
        }

        IReadOnlyList<BookingTicketDto> orderedUpcoming = upcoming
            .OrderBy(t => t.PlayDate)
            .ThenBy(t => t.StartHour)
            .ToList();

        return new UserBookingsPageDto
        {
            Upcoming = orderedUpcoming,
            History = history
        };
    }

    public async Task<ServiceResult<BookingTicketDto>> CancelBookingAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null)
        {
            return ServiceResult<BookingTicketDto>.Fail(ActorGuard.InactiveOrMissingMessage);
        }

        if (actorRole == UserRole.Admin || actor.Role == UserRole.Admin)
        {
            return ServiceResult<BookingTicketDto>.Fail(
                "Quản trị sàn không trực tiếp hủy đơn lẻ của đối tác; vui lòng dùng chức năng Tạm khóa cụm sân nếu vi phạm.");
        }

        Booking? booking = await _bookingRepository.GetByIdAsync(bookingId, includeDetails: true, ct);
        if (booking is null)
        {
            return ServiceResult<BookingTicketDto>.Fail("Không tìm thấy đơn đặt sân.");
        }

        bool isVenueOwner = actorRole == UserRole.CourtOwner
                            && actor.Role == UserRole.CourtOwner
                            && booking.Court?.Venue?.OwnerId == actorUserId;
        bool isSelfBooking = booking.UserId == actorUserId;

        if (!isVenueOwner && !isSelfBooking)
        {
            string denyMsg = actorRole == UserRole.CourtOwner && actor.Role == UserRole.CourtOwner
                ? "Bạn không thể thao tác đơn đặt của cụm sân thuộc chủ khác."
                : "Bạn không có quyền hủy đơn đặt sân này.";
            return ServiceResult<BookingTicketDto>.Fail(denyMsg);
        }

        if (booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Expired)
        {
            return ServiceResult<BookingTicketDto>.Fail("Đơn đặt sân này đã bị hủy hoặc hết hạn trước đó.");
        }

        if (booking.Status == BookingStatus.CheckedIn)
        {
            return ServiceResult<BookingTicketDto>.Fail("Đơn đã check-in, không thể hủy.");
        }

        DateTime utcNow = GetUtcNow();
        DateTime nowVn = GetVietnamNow(utcNow);

        if (!isVenueOwner)
        {
            if (IsSlotPastOrStarted(booking.PlayDate, booking.StartHour, nowVn))
            {
                return ServiceResult<BookingTicketDto>.Fail("Đã đến hoặc quá giờ chơi, không thể tự hủy lịch.");
            }

            if (booking.Status == BookingStatus.Confirmed && !CanMemberSelfCancel(booking.PlayDate, booking.StartHour, nowVn))
            {
                return ServiceResult<BookingTicketDto>.Fail(
                    "Khách chỉ có thể tự hủy lịch trước giờ chơi ít nhất 2 tiếng. Vui lòng liên hệ trực tiếp Chủ sân để được hỗ trợ.");
            }
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAtUtc = utcNow;
        booking.HoldExpiresAtUtc = null;
        await _bookingRepository.UpdateAsync(booking, ct);

        await _notifier.NotifySlotUpdatedAsync(new SlotUpdateBroadcastDto
        {
            VenueId = booking.Court?.VenueId ?? 0,
            PlayDate = booking.PlayDate.ToString("yyyy-MM-dd"),
            CourtId = booking.CourtId,
            StartHour = booking.StartHour,
            Status = BookingStatus.Cancelled,
            BookingId = booking.Id,
            UserId = booking.UserId,
            PriceVnd = booking.Court?.GetPriceForHour(booking.StartHour) ?? booking.PriceVnd,
            HoldExpiresAtUtc = null,
            IsCourtActive = booking.Court?.IsActive ?? true
        });

        return ServiceResult<BookingTicketDto>.Ok(MapTicket(booking, canCancel: false, canCheckIn: false));
    }

    public async Task<ServiceResult<BookingTicketDto>> CheckInBookingAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null)
        {
            return ServiceResult<BookingTicketDto>.Fail(ActorGuard.InactiveOrMissingMessage);
        }

        if (actorRole == UserRole.Admin || actor.Role == UserRole.Admin)
        {
            return ServiceResult<BookingTicketDto>.Fail(
                "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp check-in thu tiền tại sân của đối tác.");
        }

        Booking? booking = await _bookingRepository.GetByIdAsync(bookingId, includeDetails: true, ct);
        if (booking is null)
        {
            return ServiceResult<BookingTicketDto>.Fail("Không tìm thấy đơn đặt sân.");
        }

        bool isAuthorized = actorRole == UserRole.CourtOwner
                            && actor.Role == UserRole.CourtOwner
                            && booking.Court?.Venue?.OwnerId == actorUserId;

        if (!isAuthorized)
        {
            string denyMsg = actorRole == UserRole.CourtOwner && actor.Role == UserRole.CourtOwner
                ? "Bạn không thể thao tác đơn đặt của cụm sân thuộc chủ khác."
                : "Bạn không có quyền check-in đơn đặt sân này.";
            return ServiceResult<BookingTicketDto>.Fail(denyMsg);
        }

        if (booking.Status == BookingStatus.CheckedIn)
        {
            return ServiceResult<BookingTicketDto>.Fail("Đơn này đã được check-in trước đó.");
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return ServiceResult<BookingTicketDto>.Fail("Chỉ có thể check-in cho đơn đã xác nhận.");
        }

        DateTime utcNow = GetUtcNow();
        DateOnly todayVn = DateOnly.FromDateTime(GetVietnamNow(utcNow));

        if (booking.PlayDate > todayVn)
        {
            return ServiceResult<BookingTicketDto>.Fail(
                $"Chỉ có thể check-in đúng ngày chơi trên vé ({booking.PlayDate:dd/MM/yyyy}).");
        }

        if (booking.PlayDate < todayVn)
        {
            return ServiceResult<BookingTicketDto>.Fail(
                $"Đơn đặt ngày {booking.PlayDate:dd/MM/yyyy} đã qua ngày chơi, không thể check-in.");
        }

        booking.Status = BookingStatus.CheckedIn;
        booking.CheckedInAtUtc = utcNow;
        booking.IsPaid = true;
        await _bookingRepository.UpdateAsync(booking, ct);

        await _notifier.NotifySlotUpdatedAsync(new SlotUpdateBroadcastDto
        {
            VenueId = booking.Court?.VenueId ?? 0,
            PlayDate = booking.PlayDate.ToString("yyyy-MM-dd"),
            CourtId = booking.CourtId,
            StartHour = booking.StartHour,
            Status = BookingStatus.CheckedIn,
            BookingId = booking.Id,
            UserId = booking.UserId,
            PriceVnd = booking.PriceVnd,
            HoldExpiresAtUtc = null,
            IsCourtActive = booking.Court?.IsActive ?? true
        });

        return ServiceResult<BookingTicketDto>.Ok(MapTicket(booking, canCancel: false, canCheckIn: false));
    }

    public async Task<ServiceResult<bool>> TogglePaymentStatusAsync(
        int bookingId,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null)
        {
            return ServiceResult<bool>.Fail(ActorGuard.InactiveOrMissingMessage);
        }

        if (actorRole == UserRole.Admin || actor.Role == UserRole.Admin)
        {
            return ServiceResult<bool>.Fail(
                "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp thu tiền tại sân của đối tác.");
        }

        Booking? booking = await _bookingRepository.GetByIdAsync(bookingId, includeDetails: true, ct);
        if (booking is null)
        {
            return ServiceResult<bool>.Fail("Không tìm thấy đơn đặt sân.");
        }

        bool isAuthorized = actorRole == UserRole.CourtOwner
                            && actor.Role == UserRole.CourtOwner
                            && booking.Court?.Venue?.OwnerId == actorUserId;

        if (!isAuthorized)
        {
            string denyMsg = actorRole == UserRole.CourtOwner && actor.Role == UserRole.CourtOwner
                ? "Bạn không thể thao tác đơn đặt của cụm sân thuộc chủ khác."
                : "Bạn không có quyền thao tác trên đơn này.";
            return ServiceResult<bool>.Fail(denyMsg);
        }

        if (booking.Status == BookingStatus.CheckedIn)
        {
            return ServiceResult<bool>.Fail("Đơn đã check-in được tính là đã thanh toán.");
        }

        if (booking.Status != BookingStatus.Confirmed)
        {
            return ServiceResult<bool>.Fail("Chỉ cập nhật thanh toán cho đơn đã xác nhận.");
        }

        booking.IsPaid = !booking.IsPaid;
        await _bookingRepository.UpdateAsync(booking, ct);
        return ServiceResult<bool>.Ok(booking.IsPaid);
    }

    public async Task<ServiceResult<BookingTicketDto>> QuickCheckInByCodeAsync(
        string bookingCode,
        int actorUserId,
        UserRole actorRole,
        CancellationToken ct = default)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null)
        {
            return ServiceResult<BookingTicketDto>.Fail(ActorGuard.InactiveOrMissingMessage);
        }

        if (actorRole == UserRole.Admin || actor.Role == UserRole.Admin)
        {
            return ServiceResult<BookingTicketDto>.Fail(
                "Quản trị sàn chỉ giám sát hệ thống, không trực tiếp check-in thu tiền tại sân của đối tác.");
        }

        if (actorRole != UserRole.CourtOwner || actor.Role != UserRole.CourtOwner)
        {
            return ServiceResult<BookingTicketDto>.Fail("Bạn không có quyền check-in đơn đặt sân này.");
        }

        if (string.IsNullOrWhiteSpace(bookingCode))
        {
            return ServiceResult<BookingTicketDto>.Fail("Vui lòng nhập mã đặt sân.");
        }

        string cleanCode = bookingCode.Trim().ToUpperInvariant();
        Booking? booking = await _bookingRepository.GetByCodeAsync(cleanCode, includeDetails: true, ct);
        if (booking is null)
        {
            return ServiceResult<BookingTicketDto>.Fail($"Không tìm thấy đơn đặt sân với mã {cleanCode}.");
        }

        return await CheckInBookingAsync(booking.Id, actorUserId, actorRole, ct);
    }

    public async Task<byte[]> ExportOwnerBookingsCsvAsync(
        int venueId,
        int actorUserId,
        UserRole actorRole,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, actorUserId, ct);
        if (actor is null || actorRole != UserRole.CourtOwner || actor.Role != UserRole.CourtOwner)
        {
            throw new UnauthorizedAccessException("Chỉ chủ sở hữu cụm sân đang hoạt động mới có quyền xuất dữ liệu.");
        }

        Venue? venue = await _venueRepository.GetByIdAsync(venueId, ct);
        if (venue is null)
        {
            throw new InvalidOperationException("Không tìm thấy cụm sân.");
        }

        if (venue.OwnerId != actorUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền xuất dữ liệu cụm sân này.");
        }

        List<Booking> bookings = await _bookingRepository.GetBookingsByVenueAsync(venueId, ct);
        if (fromDate.HasValue)
        {
            bookings = bookings.Where(b => b.PlayDate >= fromDate.Value).ToList();
        }
        if (toDate.HasValue)
        {
            bookings = bookings.Where(b => b.PlayDate <= toDate.Value).ToList();
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Mã Đặt Sân,Tên Sân,Môn Thể Thao,Khách Hàng,Số Điện Thoại,Ngày Chơi,Khung Giờ,Giá Tiền (VND),Trạng Thái,Phương Thức TT,Thanh Toán,Ghi Chú,Thời Gian Đặt");

        foreach (Booking b in bookings.OrderByDescending(b => b.PlayDate).ThenByDescending(b => b.StartHour))
        {
            string statusName = b.Status switch
            {
                BookingStatus.Confirmed => "Đã xác nhận",
                BookingStatus.CheckedIn => "Đã check-in",
                BookingStatus.Cancelled => "Đã hủy",
                BookingStatus.Held => "Đang giữ chỗ",
                _ => "Hết hạn"
            };

            string courtName = EscapeCsvField(b.Court?.Name ?? $"Sân #{b.CourtId}");
            string sport = b.Court?.SportType == SportType.Pickleball ? "Pickleball" : "Cầu lông";
            string customer = EscapeCsvField(b.User?.FullName ?? "N/A");
            string phone = EscapeCsvField(b.User?.PhoneNumber ?? "N/A");
            string timeRange = $"{b.StartHour:D2}:00 - {b.StartHour + 1:D2}:00";
            string priceStr = b.PriceVnd.ToString("0", CultureInfo.InvariantCulture);
            string paymentMethod = EscapeCsvField(b.PaymentMethod);
            string isPaid = b.IsPaid ? "Đã thanh toán" : "Chưa thanh toán";
            string notes = EscapeCsvField(b.Notes);

            sb.AppendLine($"{EscapeCsvField(b.BookingCode)},{courtName},{sport},{customer},{phone},{b.PlayDate:yyyy-MM-dd},{timeRange},{priceStr},{statusName},{paymentMethod},{isPaid},{notes},{b.CreatedAtUtc:yyyy-MM-dd HH:mm}");
        }

        byte[] preamble = System.Text.Encoding.UTF8.GetPreamble();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        byte[] result = new byte[preamble.Length + bytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(bytes, 0, result, preamble.Length, bytes.Length);
        return result;
    }

    public async Task<int> ReleaseExpiredHoldsAsync(CancellationToken ct = default)
    {
        await SlotLock.WaitAsync(ct);
        try
        {
            return await ReleaseExpiredHoldsInternalAsync(ct);
        }
        finally
        {
            SlotLock.Release();
        }
    }

    public async Task<OwnerPortalDto> GetOwnerPortalAsync(
        int ownerUserId,
        int? selectedVenueId,
        DateOnly selectedDate,
        CancellationToken ct = default)
    {
        await ReleaseExpiredHoldsAsync(ct);

        List<Venue> accessibleVenues = await _venueRepository.GetByOwnerAsync(ownerUserId, ct);

        List<OwnerVenueSummaryDto> myVenues = accessibleVenues
            .Select(v => new OwnerVenueSummaryDto
            {
                Id = v.Id,
                Name = v.Name,
                District = v.District,
                CoverImageUrl = v.CoverImageUrl,
                Status = v.Status,
                CourtCount = v.Courts.Count
            })
            .ToList();

        Venue? selectedVenue = selectedVenueId.HasValue
            ? accessibleVenues.FirstOrDefault(v => v.Id == selectedVenueId.Value) ?? accessibleVenues.FirstOrDefault()
            : accessibleVenues.FirstOrDefault();

        if (selectedVenue is null)
        {
            return new OwnerPortalDto
            {
                MyVenues = myVenues,
                SelectedVenue = null,
                SelectedDate = selectedDate
            };
        }

        DateTime utcNow = GetUtcNow();
        DateOnly todayVn = DateOnly.FromDateTime(GetVietnamNow(utcNow));
        List<Court> venueCourts = await _courtRepository.GetByVenueAsync(selectedVenue.Id, activeOnly: false, sportFilter: null, ct);
        List<Booking> dateBookings = await _bookingRepository.GetBookingsForVenueAndDateAsync(selectedVenue.Id, selectedDate, activeOnly: false, ct);

        List<Booking> confirmedOrCheckedIn = dateBookings
            .Where(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn)
            .ToList();

        int checkedInCount = dateBookings.Count(b => b.Status == BookingStatus.CheckedIn);
        decimal realizedRevenue = dateBookings
            .Where(b => b.Status == BookingStatus.CheckedIn)
            .Sum(b => b.PriceVnd);
        decimal expectedRevenue = dateBookings
            .Where(b => b.Status == BookingStatus.Confirmed)
            .Sum(b => b.PriceVnd);
        int activeCourtCount = venueCourts.Count(c => c.IsActive);
        int operatingHoursCount = selectedVenue.GetOperatingHours().Length;
        double occupancy = activeCourtCount > 0 && operatingHoursCount > 0
            ? Math.Round(confirmedOrCheckedIn.Count / (activeCourtCount * (double)operatingHoursCount) * 100.0, 1)
            : 0.0;

        List<Booking> allVenueBookings = await _bookingRepository.GetBookingsByVenueAsync(selectedVenue.Id, ct);
        DateOnly monthStart = new DateOnly(selectedDate.Year, selectedDate.Month, 1);
        DateOnly monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var monthBookings = allVenueBookings
            .Where(b => b.PlayDate >= monthStart && b.PlayDate <= monthEnd && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.CheckedIn))
            .ToList();
        decimal monthRevenue = monthBookings.Sum(b => b.PriceVnd);

        int activeHolds = dateBookings.Count(b =>
            b.Status == BookingStatus.Held &&
            (b.HoldExpiresAtUtc == null || b.HoldExpiresAtUtc > utcNow));

        List<BookingTicketDto> tickets = dateBookings
            .Where(b => b.Status != BookingStatus.Expired)
            .OrderBy(b => b.StartHour)
            .ThenBy(b => b.Court?.DisplayOrder ?? int.MaxValue)
            .Select(b => MapTicket(
                b,
                canCancel: b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Held,
                canCheckIn: b.Status == BookingStatus.Confirmed && b.PlayDate == todayVn))
            .ToList();

        return new OwnerPortalDto
        {
            MyVenues = myVenues,
            SelectedVenue = selectedVenue,
            SelectedDate = selectedDate,
            TodayConfirmedCount = confirmedOrCheckedIn.Count,
            TodayCheckedInCount = checkedInCount,
            TodayRevenueVnd = realizedRevenue,
            TodayExpectedRevenueVnd = expectedRevenue,
            MonthRevenueVnd = monthRevenue,
            MonthBookingsCount = monthBookings.Count,
            OccupancyPercent = occupancy,
            ActiveHoldsCount = activeHolds,
            DateBookings = tickets,
            VenueCourts = venueCourts
        };
    }

    private async Task<int> ReleaseExpiredHoldsInternalAsync(CancellationToken ct)
    {
        DateTime utcNow = GetUtcNow();
        List<Booking> expired = await _bookingRepository.GetExpiredHoldsAsync(utcNow, ct);
        return await HoldExpirationHelper.ExpireHoldsAndBroadcastAsync(
            expired,
            _bookingRepository,
            _notifier,
            ct: ct);
    }

    private async Task<HeldSummaryDto> BuildHeldSummaryAsync(int userId, int venueId, DateTime utcNow, CancellationToken ct)
    {
        List<Booking> holds = await _bookingRepository.GetActiveHoldsByUserAsync(userId, venueId, utcNow, ct);
        if (holds.Count == 0)
        {
            return new HeldSummaryDto();
        }

        List<HeldItemDto> items = holds.Select(h => new HeldItemDto
        {
            BookingId = h.Id,
            VenueId = h.Court?.VenueId ?? venueId,
            CourtId = h.CourtId,
            CourtName = h.Court?.Name ?? $"Sân #{h.CourtId}",
            PlayDate = h.PlayDate,
            StartHour = h.StartHour,
            TimeRangeLabel = FormatTimeRange(h.StartHour),
            PriceVnd = h.PriceVnd,
            HoldExpiresAtUtc = AsUtc(h.HoldExpiresAtUtc)
        }).ToList();

        string firstDate = items[0].PlayDate.ToString("dd/MM", CultureInfo.InvariantCulture);
        string summaryText = items.Count == 1
            ? $"{items[0].CourtName} · {firstDate} ({items[0].TimeRangeLabel})"
            : $"{items[0].CourtName} · {firstDate} ({items[0].TimeRangeLabel}) và {items.Count - 1} khung giờ khác";

        DateTime? earliest = items
            .Where(i => i.HoldExpiresAtUtc.HasValue)
            .Select(i => i.HoldExpiresAtUtc)
            .Min();

        return new HeldSummaryDto
        {
            Count = items.Count,
            TotalPriceVnd = items.Sum(i => i.PriceVnd),
            EarliestExpiresAtUtc = AsUtc(earliest),
            SummaryText = summaryText,
            Items = items
        };
    }

    private static SlotVisualState ResolveVisualState(
        Court court,
        DateOnly playDate,
        int hour,
        Booking? active,
        int? currentUserId,
        DateTime utcNow,
        DateOnly todayVn,
        DateTime nowVn)
    {
        if (active is not null)
        {
            if (active.Status == BookingStatus.Confirmed || active.Status == BookingStatus.CheckedIn)
            {
                return currentUserId.HasValue && active.UserId == currentUserId.Value
                    ? SlotVisualState.BookedByMe
                    : SlotVisualState.BookedByOther;
            }

            if (active.Status == BookingStatus.Held &&
                (active.HoldExpiresAtUtc is null || active.HoldExpiresAtUtc > utcNow))
            {
                if (!court.IsActive)
                {
                    return SlotVisualState.Maintenance;
                }

                return currentUserId.HasValue && active.UserId == currentUserId.Value
                    ? SlotVisualState.HeldByMe
                    : SlotVisualState.HeldByOther;
            }
        }

        if (!court.IsActive)
        {
            return SlotVisualState.Maintenance;
        }

        if (playDate < todayVn || (playDate == todayVn && hour <= nowVn.Hour))
        {
            return SlotVisualState.Past;
        }

        return SlotVisualState.Available;
    }

    private static SlotUpdateBroadcastDto BuildBroadcastDto(
        DateOnly playDate,
        Court court,
        int startHour,
        BookingStatus status,
        int? bookingId,
        int? userId,
        decimal priceVnd,
        DateTime? holdExpiresAtUtc) => new()
    {
        VenueId = court.VenueId,
        PlayDate = playDate.ToString("yyyy-MM-dd"),
        CourtId = court.Id,
        StartHour = startHour,
        Status = status,
        BookingId = bookingId,
        UserId = userId,
        PriceVnd = priceVnd,
        HoldExpiresAtUtc = AsUtc(holdExpiresAtUtc),
        IsCourtActive = court.IsActive
    };

    private static BookingTicketDto MapTicket(Booking b, bool canCancel, bool canCheckIn) => new()
    {
        Id = b.Id,
        BookingCode = b.BookingCode,
        VenueId = b.Court?.VenueId ?? 0,
        VenueName = b.Court?.Venue?.Name ?? string.Empty,
        VenueDistrict = b.Court?.Venue?.District ?? string.Empty,
        VenueAddress = b.Court?.Venue?.Address ?? string.Empty,
        CourtId = b.CourtId,
        CourtCode = b.Court?.Code ?? string.Empty,
        CourtName = b.Court?.Name ?? $"Sân #{b.CourtId}",
        SportType = b.Court?.SportType ?? SportType.Pickleball,
        IsIndoor = b.Court?.IsIndoor ?? true,
        UserId = b.UserId,
        CustomerName = b.User?.FullName ?? string.Empty,
        CustomerPhone = b.User?.PhoneNumber ?? string.Empty,
        PlayDate = b.PlayDate,
        StartHour = b.StartHour,
        TimeRangeLabel = FormatTimeRange(b.StartHour),
        PriceVnd = b.PriceVnd,
        Status = b.Status,
        Notes = b.Notes,
        PaymentMethod = b.PaymentMethod ?? "Tại sân",
        IsPaid = b.IsPaid,
        CreatedAtUtc = AsUtc(b.CreatedAtUtc),
        CanCancel = canCancel,
        CanCheckIn = canCheckIn
    };

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value)
        => value.HasValue ? AsUtc(value.Value) : null;

    public static bool CanMemberSelfCancel(DateOnly playDate, int startHour, DateTime nowVn)
    {
        DateTime slotStartVn = playDate.ToDateTime(new TimeOnly(startHour, 0));
        return (slotStartVn - nowVn) >= TimeSpan.FromHours(MemberCancelLeadHours);
    }

    private static bool IsSlotPastOrStarted(DateOnly playDate, int startHour, DateTime nowVn)
    {
        DateOnly todayVn = DateOnly.FromDateTime(nowVn);
        return playDate < todayVn || (playDate == todayVn && startHour <= nowVn.Hour);
    }

    private static string FormatTimeRange(int startHour) => $"{startHour:00}:00 - {startHour + 1:00}:00";

    private static string GenerateBookingCode(DateOnly playDate)
        => $"CB-{playDate:yyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private DateTime GetUtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime GetVietnamNow(DateTime utcNow)
        => VietnamTimeHelper.FromUtc(utcNow);

    private async Task<string?> AuthorizeBookingCustomerAsync(int userId, CancellationToken ct)
    {
        AppUser? actor = await ActorGuard.GetActiveActorAsync(_userRepository, userId, ct);
        if (actor is null)
        {
            return ActorGuard.InactiveOrMissingMessage;
        }

        if (actor.Role == UserRole.Admin)
        {
            return "Tài khoản Quản trị sàn dùng để điều hành hệ thống, không tham gia đặt lịch sân.";
        }

        return null;
    }

    private static bool TryNormalizePaymentMethod(string? raw, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            normalized = "Tại sân";
            return true;
        }

        string trimmed = raw.Trim();
        if (string.Equals(trimmed, "Tại sân", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Tại sân";
            return true;
        }

        if (string.Equals(trimmed, "Chuyển khoản VietQR", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Chuyển khoản VietQR";
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static string EscapeCsvField(string? value)
    {
        string clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        if (clean.Contains(',') || clean.Contains('"'))
        {
            return $"\"{clean.Replace("\"", "\"\"")}\"";
        }
        return clean;
    }
}
