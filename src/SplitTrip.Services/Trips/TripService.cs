using System.Security.Cryptography;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Services.Trips;

public sealed class TripService(
    ITripRepository tripRepository,
    ITripMemberRepository memberRepository,
    IUserRepository userRepository,
    INotificationRepository notificationRepository) : ITripService
{
    private const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int InviteCodeLength = 8;

    public async Task<TripDetailsDto> CreateTripAsync(
        Guid ownerUserId,
        CreateTripRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("Tên chuyến đi phải từ 1 đến 200 ký tự.", nameof(request));
        }

        var destination = (request.Destination ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(destination) || destination.Length > 200)
        {
            throw new ArgumentException("Điểm đến phải từ 1 đến 200 ký tự.", nameof(request));
        }

        if (request.EndDate < request.StartDate)
        {
            throw new ArgumentException("Ngày kết thúc không được nhỏ hơn ngày khởi hành.", nameof(request));
        }

        var dayCount = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (dayCount is < 1 or > 60)
        {
            throw new ArgumentException("Thời lượng chuyến đi phải từ 1 đến 60 ngày.", nameof(request));
        }

        if (request.EstimatedBudgetPerPerson < 0m)
        {
            throw new ArgumentException("Ngân sách dự kiến mỗi người không được âm.", nameof(request));
        }

        var owner = await userRepository.GetByIdAsync(ownerUserId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy tài khoản người tạo chuyến đi.");

        var inviteCode = await GenerateUniqueInviteCodeAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var tripId = Guid.NewGuid();

        var trip = new Trip
        {
            Id = tripId,
            Name = name,
            Destination = destination,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            InviteCode = inviteCode,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            EstimatedBudgetPerPerson = decimal.Truncate(request.EstimatedBudgetPerPerson),
            Status = TripStatus.Planning,
            OwnerUserId = owner.Id,
            CreatedAtUtc = nowUtc
        };

        var ownerMember = new TripMember
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            UserId = owner.Id,
            Role = TripMemberRole.Owner,
            IsActive = true,
            JoinedAtUtc = nowUtc
        };

        var days = Enumerable.Range(0, dayCount)
            .Select(offset => new TripDay
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                DayNumber = offset + 1,
                Date = request.StartDate.AddDays(offset),
                Title = $"Ngày {offset + 1}"
            })
            .ToList();

        await tripRepository.AddTripWithOwnerAndDaysAsync(trip, ownerMember, days, ct);

        return await GetTripDetailsAsync(tripId, owner.Id, isAdmin: false, ct);
    }

    public async Task<JoinTripResultDto> JoinByInviteCodeAsync(
        Guid userId,
        string inviteCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            throw new ArgumentException("Vui lòng nhập mã mời chuyến đi.", nameof(inviteCode));
        }

        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy người dùng.");

        var normalizedCode = inviteCode.Trim().ToUpperInvariant();
        var trip = await tripRepository.GetByInviteCodeAsync(normalizedCode, ct)
            ?? throw new KeyNotFoundException("Mã mời chuyến đi không tồn tại hoặc không hợp lệ.");

        if (trip.Status is TripStatus.Settling or TripStatus.Completed)
        {
            throw new InvalidOperationException("Chuyến đi đang tất toán hoặc đã kết thúc, không thể tham gia mới.");
        }

        var existingMembership = await memberRepository.GetMembershipAsync(trip.Id, userId, ct);
        var wasReactivated = false;

        if (existingMembership is not null)
        {
            if (existingMembership.IsActive)
            {
                throw new InvalidOperationException("Bạn đã là thành viên đang hoạt động của chuyến đi này.");
            }

            existingMembership.IsActive = true;
            existingMembership.JoinedAtUtc = DateTime.UtcNow;
            await memberRepository.UpdateAsync(existingMembership, ct);
            wasReactivated = true;
        }
        else
        {
            var newMember = new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                UserId = userId,
                Role = TripMemberRole.Member,
                IsActive = true,
                JoinedAtUtc = DateTime.UtcNow
            };
            await memberRepository.AddAsync(newMember, ct);
        }

        if (trip.OwnerUserId != userId)
        {
            await notificationRepository.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = trip.OwnerUserId,
                TripId = trip.Id,
                Type = NotificationType.TripInvite,
                Title = "Thành viên mới tham gia chuyến đi",
                Message = $"{user.FullName} đã tham gia chuyến đi \"{trip.Name}\".",
                ActionUrl = $"/Trips/Details?id={trip.Id}",
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            }, ct);
        }

        return new JoinTripResultDto(trip.Id, trip.Name, trip.InviteCode, wasReactivated);
    }

    public async Task<IReadOnlyList<TripSummaryDto>> GetUserTripsAsync(Guid userId, CancellationToken ct = default)
    {
        var trips = await tripRepository.GetTripsForUserAsync(userId, ct);

        return trips
            .Select(t => new TripSummaryDto(
                t.Id,
                t.Name,
                t.Destination,
                t.InviteCode,
                t.StartDate,
                t.EndDate,
                t.EstimatedBudgetPerPerson,
                t.Status,
                t.OwnerUserId,
                t.OwnerUser?.FullName ?? "Trưởng đoàn",
                t.Members.Count(m => m.IsActive),
                t.CreatedAtUtc))
            .ToList();
    }

    public async Task<TripDetailsDto> GetTripDetailsAsync(
        Guid tripId,
        Guid viewerUserId,
        bool isAdmin = false,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdWithDetailsAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!isAdmin)
        {
            var isMember = trip.Members.Any(m => m.UserId == viewerUserId && m.IsActive);
            if (!isMember)
            {
                throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
            }
        }

        var balances = await GetTripMemberBalancesAsync(tripId, ct);
        var dayCount = trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1;
        var days = trip.Days
            .OrderBy(d => d.DayNumber)
            .Select(d => new TripDayOverviewDto(d.Id, d.DayNumber, d.Date, d.Title))
            .ToList();

        return new TripDetailsDto(
            trip.Id,
            trip.Name,
            trip.Destination,
            trip.Description,
            trip.InviteCode,
            trip.StartDate,
            trip.EndDate,
            dayCount,
            trip.EstimatedBudgetPerPerson,
            trip.Status,
            trip.OwnerUserId,
            trip.OwnerUser?.FullName ?? "Trưởng đoàn",
            trip.CreatedAtUtc,
            trip.ClosedAtUtc,
            days,
            balances);
    }

    public async Task<IReadOnlyList<MemberNetBalanceDto>> GetTripMemberBalancesAsync(
        Guid tripId,
        CancellationToken ct = default)
    {
        var (members, finalizedExpenses, confirmedSettlements) =
            await memberRepository.GetTripLedgerInputsAsync(tripId, ct);

        var paidByUser = finalizedExpenses
            .GroupBy(e => e.PaidByUserId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.TotalAmount));

        var owedByUser = finalizedExpenses
            .SelectMany(e => e.Splits)
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.OwedAmount));

        var settlementsPaidByUser = confirmedSettlements
            .GroupBy(s => s.FromUserId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var settlementsReceivedByUser = confirmedSettlements
            .GroupBy(s => s.ToUserId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

        var results = new List<MemberNetBalanceDto>(members.Count);

        foreach (var member in members)
        {
            var totalPaid = paidByUser.GetValueOrDefault(member.UserId, 0m);
            var totalOwed = owedByUser.GetValueOrDefault(member.UserId, 0m);
            var settlementsPaid = settlementsPaidByUser.GetValueOrDefault(member.UserId, 0m);
            var settlementsReceived = settlementsReceivedByUser.GetValueOrDefault(member.UserId, 0m);
            var netBalance = totalPaid - totalOwed + settlementsPaid - settlementsReceived;

            if (!member.IsActive && netBalance == 0m)
            {
                continue;
            }

            var user = member.User;
            results.Add(new MemberNetBalanceDto(
                member.UserId,
                user?.FullName ?? "Thành viên",
                user?.Email ?? string.Empty,
                user?.BankCode,
                user?.BankBin,
                user?.BankAccountNumber,
                user?.BankAccountHolder,
                totalPaid,
                totalOwed,
                settlementsPaid,
                settlementsReceived,
                netBalance,
                member.Role,
                member.IsActive));
        }

        return results;
    }

    public async Task<TripStatus> TransitionStatusAsync(
        Guid tripId,
        Guid actorUserId,
        TripStatus targetStatus,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.OwnerUserId != actorUserId)
        {
            throw new UnauthorizedAccessException("Chỉ trưởng đoàn mới có quyền chuyển trạng thái chuyến đi.");
        }

        var isAllowed = (trip.Status, targetStatus) switch
        {
            (TripStatus.Planning, TripStatus.Ongoing) => true,
            (TripStatus.Ongoing, TripStatus.Settling) => true,
            (TripStatus.Settling, TripStatus.Ongoing) => true,
            (TripStatus.Settling, TripStatus.Completed) => true,
            _ => false
        };

        if (!isAllowed)
        {
            throw new InvalidOperationException(
                $"Không thể chuyển trạng thái chuyến đi từ '{trip.Status}' sang '{targetStatus}'.");
        }

        if (targetStatus == TripStatus.Completed)
        {
            var balances = await GetTripMemberBalancesAsync(tripId, ct);
            if (balances.Any(b => b.NetBalance != 0m))
            {
                throw new InvalidOperationException(
                    "Không thể hoàn tất chuyến đi khi vẫn còn thành viên chưa tất toán hết công nợ (NetBalance != 0đ).");
            }

            if (await tripRepository.HasUnconfirmedSettlementsAsync(tripId, ct))
            {
                throw new InvalidOperationException(
                    "Không thể hoàn tất chuyến đi khi vẫn còn giao dịch thanh toán đang chờ xác nhận hoặc khiếu nại.");
            }

            trip.ClosedAtUtc = DateTime.UtcNow;
        }
        else if (targetStatus == TripStatus.Ongoing)
        {
            trip.ClosedAtUtc = null;
        }

        trip.Status = targetStatus;
        await tripRepository.UpdateAsync(trip, ct);

        return trip.Status;
    }

    public Task RemoveMemberAsync(Guid tripId, Guid actorUserId, Guid targetUserId, CancellationToken ct = default)
        => RemoveOrLeaveMemberAsync(tripId, actorUserId, targetUserId, ct);

    public async Task RemoveOrLeaveMemberAsync(
        Guid tripId,
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken ct = default)
    {
        var trip = await tripRepository.GetByIdAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.OwnerUserId != actorUserId && actorUserId != targetUserId)
        {
            throw new UnauthorizedAccessException(
                "Chỉ trưởng đoàn hoặc chính thành viên đó mới có quyền thực hiện thao tác này.");
        }

        if (targetUserId == trip.OwnerUserId)
        {
            throw new InvalidOperationException("Trưởng đoàn không thể rời hoặc bị xóa khỏi chuyến đi.");
        }

        if (trip.Status == TripStatus.Completed)
        {
            throw new InvalidOperationException("Không thể thay đổi thành viên của chuyến đi đã hoàn tất.");
        }

        var membership = await memberRepository.GetMembershipAsync(tripId, targetUserId, ct);
        if (membership is null || !membership.IsActive)
        {
            throw new KeyNotFoundException("Thành viên không tồn tại hoặc đã rời chuyến đi.");
        }

        var balances = await GetTripMemberBalancesAsync(tripId, ct);
        var targetBalance = balances.FirstOrDefault(b => b.UserId == targetUserId)?.NetBalance ?? 0m;
        if (targetBalance != 0m)
        {
            throw new InvalidOperationException(
                $"Không thể rời chuyến đi khi số dư công nợ chưa tất toán về 0đ (hiện tại: {targetBalance:N0}đ).");
        }

        var hasHistory = await memberRepository.HasFinancialHistoryAsync(tripId, targetUserId, ct);
        if (hasHistory)
        {
            membership.IsActive = false;
            await memberRepository.UpdateAsync(membership, ct);
        }
        else
        {
            await memberRepository.RemoveAsync(membership, ct);
        }
    }

    public async Task<bool> IsActiveMemberAsync(Guid tripId, Guid userId, CancellationToken ct = default)
    {
        var membership = await memberRepository.GetMembershipAsync(tripId, userId, ct);
        return membership is { IsActive: true };
    }

    private async Task<string> GenerateUniqueInviteCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[InviteCodeLength];
            for (var i = 0; i < InviteCodeLength; i++)
            {
                chars[i] = InviteAlphabet[RandomNumberGenerator.GetInt32(InviteAlphabet.Length)];
            }

            var candidate = new string(chars);
            if (!await tripRepository.InviteCodeExistsAsync(candidate, ct))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không thể sinh mã mời duy nhất, vui lòng thử lại.");
    }
}
