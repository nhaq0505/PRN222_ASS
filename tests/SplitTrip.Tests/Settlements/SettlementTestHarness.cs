using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.DataAccessObjects.Daos.Settlements;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Settlements;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Tests.Settlements;

internal sealed class SettlementTestHarness : IAsyncDisposable
{
    public SplitTripDbContext DbContext { get; }
    public IAuthService AuthService { get; }
    public ITripService TripService { get; }
    public INotificationService NotificationService { get; }
    public IDebtSimplificationService DebtSimplificationService { get; }
    public IVietQrService VietQrService { get; }
    public ISettlementService SettlementService { get; }
    public IAdminAnalyticsService AdminAnalyticsService { get; }
    public ISettlementRepository SettlementRepository { get; }

    public SettlementTestHarness()
    {
        var options = new DbContextOptionsBuilder<SplitTripDbContext>()
            .UseInMemoryDatabase($"SplitTrip_Settlements_{Guid.NewGuid():N}")
            .Options;

        DbContext = new SplitTripDbContext(options);

        var userDao = new UserDao(DbContext);
        var tripDao = new TripDao(DbContext);
        var tripMemberDao = new TripMemberDao(DbContext);
        var notificationDao = new NotificationDao(DbContext);
        var settlementDao = new SettlementDao(DbContext);
        var adminAnalyticsDao = new AdminAnalyticsDao(DbContext);

        var userRepo = new UserRepository(userDao);
        var tripRepo = new TripRepository(tripDao);
        var tripMemberRepo = new TripMemberRepository(tripMemberDao);
        var notificationRepo = new NotificationRepository(notificationDao);
        SettlementRepository = new SettlementRepository(settlementDao);
        var adminAnalyticsRepo = new AdminAnalyticsRepository(adminAnalyticsDao);

        var adminOptions = Options.Create(new AdminAccountOptions
        {
            Email = "admin@splittrip.vn",
            Password = "Admin@123",
            FullName = "SplitTrip System Admin"
        });

        AuthService = new AuthService(userRepo, adminOptions);
        TripService = new TripService(tripRepo, tripMemberRepo, userRepo, notificationRepo);
        NotificationService = new NotificationService(notificationRepo);
        DebtSimplificationService = new MinCashFlowService();
        VietQrService = new VietQrService();
        SettlementService = new SettlementService(
            SettlementRepository,
            tripRepo,
            TripService,
            DebtSimplificationService,
            VietQrService,
            NotificationService);
        AdminAnalyticsService = new AdminAnalyticsService(adminAnalyticsRepo, DebtSimplificationService);
    }

    public async Task<AuthenticatedUserDto> CreateUserAsync(
        string email,
        string fullName,
        string? bankCode = "VCB",
        string? bankAccountNumber = "0123456789")
    {
        var user = await AuthService.RegisterAsync(new RegisterUserRequest(email, "Secret@123", fullName));
        if (!string.IsNullOrWhiteSpace(bankCode) && !string.IsNullOrWhiteSpace(bankAccountNumber))
        {
            await AuthService.UpdateBankProfileAsync(
                user.UserId,
                new UpdateBankProfileRequest(
                    fullName,
                    null,
                    bankCode,
                    null,
                    bankAccountNumber,
                    fullName));
        }

        return user;
    }

    public async Task<TripDetailsDto> CreateTripWithMembersAsync(
        Guid ownerId,
        IEnumerable<Guid> additionalMemberIds,
        string name = "Đà Lạt Săn Mây",
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        TripStatus initialStatus = TripStatus.Ongoing)
    {
        var start = startDate ?? new DateOnly(2026, 11, 1);
        var end = endDate ?? new DateOnly(2026, 11, 4);

        var created = await TripService.CreateTripAsync(
            ownerId,
            new CreateTripRequest(name, "Đà Lạt", "Chuyến đi nhóm", start, end, 2_000_000m));

        foreach (var memberId in additionalMemberIds)
        {
            await TripService.JoinByInviteCodeAsync(memberId, created.InviteCode);
        }

        if (initialStatus is TripStatus.Ongoing or TripStatus.Settling)
        {
            await TripService.TransitionStatusAsync(created.Id, ownerId, TripStatus.Ongoing);
        }

        if (initialStatus == TripStatus.Settling)
        {
            await TripService.TransitionStatusAsync(created.Id, ownerId, TripStatus.Settling);
        }

        return await TripService.GetTripDetailsAsync(created.Id, ownerId);
    }

    public async Task<Expense> AddExpenseWithSplitsAsync(
        Guid tripId,
        Guid paidByUserId,
        string title,
        decimal totalAmount,
        IReadOnlyDictionary<Guid, decimal> splitsByUser,
        ExpenseCategory category = ExpenseCategory.Food,
        ReceiptScanStatus scanStatus = ReceiptScanStatus.None)
    {
        var expenseId = Guid.NewGuid();
        var expense = new Expense
        {
            Id = expenseId,
            TripId = tripId,
            PaidByUserId = paidByUserId,
            Title = title,
            Category = category,
            SubtotalAmount = totalAmount,
            TotalAmount = totalAmount,
            SplitMode = ExpenseSplitMode.ExactAmount,
            ScanStatus = scanStatus,
            SpentAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            Splits = splitsByUser.Select(kvp => new ExpenseSplit
            {
                Id = Guid.NewGuid(),
                ExpenseId = expenseId,
                UserId = kvp.Key,
                ShareRatioOrWeight = 1m,
                OwedAmount = kvp.Value
            }).ToList()
        };

        DbContext.Expenses.Add(expense);
        await DbContext.SaveChangesAsync();
        return expense;
    }

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}
