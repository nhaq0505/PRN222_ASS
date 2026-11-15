using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.DataAccessObjects.Daos.Expenses;
using SplitTrip.Repositories.Expenses;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Tests.Expenses;

internal sealed class ExpensesTestHarness : IAsyncDisposable
{
    public SplitTripDbContext DbContext { get; }
    public IAuthService AuthService { get; }
    public ITripService TripService { get; }
    public INotificationService NotificationService { get; }
    public IExpenseService ExpenseService { get; }

    private readonly ITripRepository _tripRepo;
    private readonly ITripMemberRepository _tripMemberRepo;
    private readonly IReceiptRepository _receiptRepo;

    public ExpensesTestHarness()
    {
        var options = new DbContextOptionsBuilder<SplitTripDbContext>()
            .UseInMemoryDatabase($"SplitTrip_Expenses_{Guid.NewGuid():N}")
            .Options;

        DbContext = new SplitTripDbContext(options);

        var userDao = new UserDao(DbContext);
        var tripDao = new TripDao(DbContext);
        var tripMemberDao = new TripMemberDao(DbContext);
        var notificationDao = new NotificationDao(DbContext);
        var expenseDao = new ExpenseDao(DbContext);
        var receiptDao = new ReceiptDao(DbContext);

        var userRepo = new UserRepository(userDao);
        _tripRepo = new TripRepository(tripDao);
        _tripMemberRepo = new TripMemberRepository(tripMemberDao);
        var notificationRepo = new NotificationRepository(notificationDao);
        var expenseRepo = new ExpenseRepository(expenseDao);
        _receiptRepo = new ReceiptRepository(receiptDao);

        var adminOptions = Options.Create(new AdminAccountOptions
        {
            Email = "admin@splittrip.vn",
            Password = "Admin@123",
            FullName = "SplitTrip System Admin"
        });

        AuthService = new AuthService(userRepo, adminOptions);
        TripService = new TripService(_tripRepo, _tripMemberRepo, userRepo, notificationRepo);
        NotificationService = new NotificationService(notificationRepo);
        ExpenseService = new ExpenseService(expenseRepo, _receiptRepo, _tripRepo, _tripMemberRepo, notificationRepo);
    }

    public IAiReceiptScannerService CreateScanner(
        string apiKey = "",
        Func<HttpRequestMessage, HttpResponseMessage>? handlerFunc = null)
    {
        var handler = new StubHttpMessageHandler(handlerFunc ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        }));

        var httpClient = new HttpClient(handler);
        var geminiOptions = Options.Create(new GeminiOptions
        {
            ApiKey = apiKey,
            Model = "gemini-2.5-flash"
        });

        return new GeminiReceiptScannerService(
            httpClient,
            geminiOptions,
            _tripRepo,
            _tripMemberRepo,
            _receiptRepo);
    }

    public async Task<(Guid TripId, Guid AliceId, Guid BobId, Guid CharlieId)> SeedTripWithThreeMembersAsync()
    {
        var alice = await AuthService.RegisterAsync(new RegisterUserRequest("alice@splittrip.vn", "Pass@123", "An Nguyễn"));
        var bob = await AuthService.RegisterAsync(new RegisterUserRequest("bob@splittrip.vn", "Pass@123", "Bình Trần"));
        var charlie = await AuthService.RegisterAsync(new RegisterUserRequest("charlie@splittrip.vn", "Pass@123", "Chi Lê"));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = await TripService.CreateTripAsync(
            alice.UserId,
            new CreateTripRequest("Đà Lạt Săn Mây", "Đà Lạt", "Chuyến đi nhóm 3 người", today, today.AddDays(3), 2_000_000m));

        await TripService.JoinByInviteCodeAsync(bob.UserId, trip.InviteCode);
        await TripService.JoinByInviteCodeAsync(charlie.UserId, trip.InviteCode);

        return (trip.Id, alice.UserId, bob.UserId, charlie.UserId);
    }

    public static byte[] ValidJpegBytes() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    public static byte[] ValidPngBytes() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    public static byte[] ValidWebpBytes() =>
        [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20];

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
