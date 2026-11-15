using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.DataAccessObjects;
using SplitTrip.DataAccessObjects.Daos;
using SplitTrip.Repositories.Notifications;
using SplitTrip.Repositories.Trips;
using SplitTrip.Repositories.Users;
using SplitTrip.Services.Auth;
using SplitTrip.Services.Auth.Dtos;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Expenses.Dtos;
using SplitTrip.Services.Itinerary;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Seed;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Trips;
using SplitTrip.Services.Trips.Dtos;

namespace SplitTrip.Tests.Integration;

public sealed class EndToEndTripLifecycleTests
{
    [Fact]
    public async Task FullCrossModuleLifecycle_FromRegistrationAndReceiptClaiming_ToVietQrHandshakeAndCompletedAnalytics_ConservesZeroSumLedger()
    {
        await using var harness = new FullStackIntegrationHarness();

        // 1. Register 4 travelers + configure Napas VietQR bank profiles + verify System Admin login
        var adminLogin = await harness.AuthService.LoginAsync("admin@splittrip.vn", "Admin@123");
        Assert.NotNull(adminLogin);
        Assert.True(adminLogin.IsSystemAdmin);
        Assert.Equal(AuthService.AdminRole, adminLogin.Role);

        var an = await harness.RegisterMemberWithBankAsync("an@splittrip.vn", "Nguyễn Văn An", "VCB", "1029384756");
        var binh = await harness.RegisterMemberWithBankAsync("binh@splittrip.vn", "Trần Thị Bình", "TCB", "19038475621018");
        var chi = await harness.RegisterMemberWithBankAsync("chi@splittrip.vn", "Lê Hoàng Chi", "MB", "0987654321");
        var dung = await harness.RegisterMemberWithBankAsync("dung@splittrip.vn", "Phạm Quốc Dũng", "ACB", "23456789");

        // 2. Create 3-day trip, join via InviteCode, and transition Planning -> Ongoing
        var startDate = new DateOnly(2026, 12, 18);
        var endDate = startDate.AddDays(2);

        var createdTrip = await harness.TripService.CreateTripAsync(
            an.UserId,
            new CreateTripRequest(
                "Đà Lạt Săn Mây Cuối Năm",
                "Đà Lạt",
                "Kiểm thử tích hợp toàn trình 4 phân hệ SplitTrip",
                startDate,
                endDate,
                2_500_000m));

        Assert.Equal(TripStatus.Planning, createdTrip.Status);
        Assert.Equal(3, createdTrip.DayCount);

        await harness.TripService.JoinByInviteCodeAsync(binh.UserId, createdTrip.InviteCode);
        await harness.TripService.JoinByInviteCodeAsync(chi.UserId, createdTrip.InviteCode);
        await harness.TripService.JoinByInviteCodeAsync(dung.UserId, createdTrip.InviteCode);

        var ongoingStatus = await harness.TripService.TransitionStatusAsync(
            createdTrip.Id,
            an.UserId,
            TripStatus.Ongoing);
        Assert.Equal(TripStatus.Ongoing, ongoingStatus);

        // 3. Collaborative Itinerary: add manual stop, apply AI Trip Planner fallback, cast/flip votes
        var initialSchedule = await harness.ItineraryService.GetScheduleAsync(createdTrip.Id, an.UserId);
        var day1Id = initialSchedule.Days[0].Id;

        var cloudStop = await harness.ItineraryService.AddItemAsync(
            createdTrip.Id,
            an.UserId,
            new UpsertItineraryItemRequest(
                day1Id,
                "Săn mây Đồi chè Cầu Đất",
                "Đồi chè Cầu Đất",
                "Xã Trạm Hành, Đà Lạt",
                new TimeOnly(5, 0),
                new TimeOnly(7, 0),
                120_000m,
                "Đi sớm đón bình minh"));

        await harness.ItineraryService.ToggleVoteAsync(createdTrip.Id, cloudStop.Id, an.UserId, VoteType.Upvote);
        await harness.ItineraryService.ToggleVoteAsync(createdTrip.Id, cloudStop.Id, binh.UserId, VoteType.Upvote);
        var voteAfterChi = await harness.ItineraryService.ToggleVoteAsync(createdTrip.Id, cloudStop.Id, chi.UserId, VoteType.Downvote);
        Assert.Equal(2, voteAfterChi.Upvotes);
        Assert.Equal(1, voteAfterChi.Downvotes);
        Assert.Equal(1, voteAfterChi.NetScore);

        var scheduleAfterAi = await harness.AiTripPlannerService.GenerateAndApplyPlanAsync(
            createdTrip.Id,
            an.UserId,
            new AiTripPlanRequestDto("Cân bằng", 2_500_000m, "Ưu tiên ẩm thực địa phương"));

        Assert.True(scheduleAfterAi.AiSuggestedItemsCount > 0);
        Assert.True(scheduleAfterAi.TotalItemsCount > 1);

        // 4. Multi-mode shared expenses creating circular pairwise debts across Binh, Chi, Dung
        await harness.ExpenseService.CreateExpenseAsync(
            binh.UserId,
            new UpsertExpenseRequest(
                createdTrip.Id,
                PaidByUserId: binh.UserId,
                Title: "Vé xe Limousine khứ hồi",
                Category: ExpenseCategory.Transport,
                TotalAmount: 2_400_000m,
                SplitMode: ExpenseSplitMode.Equal,
                Participants:
                [
                    new ParticipantShareInput(an.UserId, 1m),
                    new ParticipantShareInput(binh.UserId, 1m),
                    new ParticipantShareInput(chi.UserId, 1m),
                    new ParticipantShareInput(dung.UserId, 1m)
                ]));

        await harness.ExpenseService.CreateExpenseAsync(
            chi.UserId,
            new UpsertExpenseRequest(
                createdTrip.Id,
                PaidByUserId: chi.UserId,
                Title: "Homestay rừng thông 2 đêm",
                Category: ExpenseCategory.Accommodation,
                TotalAmount: 2_000_000m,
                SplitMode: ExpenseSplitMode.Percentage,
                Participants:
                [
                    new ParticipantShareInput(an.UserId, 30m),
                    new ParticipantShareInput(binh.UserId, 30m),
                    new ParticipantShareInput(chi.UserId, 20m),
                    new ParticipantShareInput(dung.UserId, 20m)
                ]));

        await harness.ExpenseService.CreateExpenseAsync(
            dung.UserId,
            new UpsertExpenseRequest(
                createdTrip.Id,
                PaidByUserId: dung.UserId,
                Title: "Vé chèo SUP Hồ Tuyền Lâm",
                Category: ExpenseCategory.Ticket,
                TotalAmount: 1_000_000m,
                SplitMode: ExpenseSplitMode.Shares,
                Participants:
                [
                    new ParticipantShareInput(an.UserId, 1m),
                    new ParticipantShareInput(binh.UserId, 1m),
                    new ParticipantShareInput(chi.UserId, 1m),
                    new ParticipantShareInput(dung.UserId, 2m)
                ]));

        var balancesBeforeDraft = await harness.TripService.GetTripMemberBalancesAsync(createdTrip.Id);
        Assert.Equal(0m, balancesBeforeDraft.Sum(b => b.NetBalance));

        // 5. Scan AI Receipt -> DraftReview (must NOT affect NetBalance or Admin finalized spend yet!)
        var scannedDraft = await harness.ReceiptScannerService.ScanAndCreateDraftAsync(
            new ScanReceiptRequest(
                createdTrip.Id,
                ActorUserId: an.UserId,
                PaidByUserId: an.UserId,
                UseDemoSample: true));

        Assert.Equal(ReceiptScanStatus.DraftReview, scannedDraft.ScanStatus);

        var balancesDuringDraft = await harness.TripService.GetTripMemberBalancesAsync(createdTrip.Id);
        Assert.Equal(
            balancesBeforeDraft.Select(b => (b.UserId, b.NetBalance)),
            balancesDuringDraft.Select(b => (b.UserId, b.NetBalance)));

        var adminDuringDraft = await harness.AdminAnalyticsService.GetDashboardAsync();
        Assert.Equal(3, adminDuringDraft.Kpis.FinalizedExpenseCount);
        Assert.Equal(5_400_000m, adminDuringDraft.Kpis.TotalFinalizedVolumeVnd);

        // Confirm OCR draft with VAT + Discount (Subtotal = 600k, VAT = 60k, Discount = 30k -> Total = 630k)
        var claimingBoard = await harness.ExpenseService.ConfirmDraftAndOpenClaimingAsync(
            scannedDraft.ExpenseId,
            an.UserId,
            new UpdateReceiptDraftRequest(
                scannedDraft.ExpenseId,
                "Tiệc nướng BBQ Đà Lạt",
                ExpenseCategory.Food,
                an.UserId,
                SubtotalAmount: 600_000m,
                TaxOrTipAmount: 60_000m,
                DiscountAmount: 30_000m,
                TotalAmount: 630_000m,
                Items:
                [
                    new DraftReceiptItemInput("Bò Mỹ nướng tảng", 1, 300_000m, 300_000m),
                    new DraftReceiptItemInput("Lẩu gà lá é", 1, 200_000m, 200_000m),
                    new DraftReceiptItemInput("Rau củ & Trà đào", 1, 100_000m, 100_000m)
                ]));

        Assert.Equal(ReceiptScanStatus.ClaimingOpen, claimingBoard.ScanStatus);
        Assert.Equal(3, claimingBoard.Items.Count);

        // 6. Live multi-member dish claiming with pro-rata VAT/discount allocation + Locking
        var dish1Id = claimingBoard.Items[0].Id; // 300k shared by An & Binh (150k each)
        var dish2Id = claimingBoard.Items[1].Id; // 200k shared by Chi & Dung (100k each)
        var dish3Id = claimingBoard.Items[2].Id; // 100k claimed by Chi

        await harness.ExpenseService.ToggleItemClaimAsync(scannedDraft.ExpenseId, dish1Id, an.UserId);
        await harness.ExpenseService.ToggleItemClaimAsync(scannedDraft.ExpenseId, dish1Id, binh.UserId);
        await harness.ExpenseService.ToggleItemClaimAsync(scannedDraft.ExpenseId, dish2Id, chi.UserId);
        await harness.ExpenseService.ToggleItemClaimAsync(scannedDraft.ExpenseId, dish2Id, dung.UserId);
        claimingBoard = await harness.ExpenseService.ToggleItemClaimAsync(scannedDraft.ExpenseId, dish3Id, chi.UserId);

        Assert.True(claimingBoard.AllItemsClaimed);
        Assert.Equal(0, claimingBoard.UnclaimedItemCount);
        Assert.Equal(630_000m, claimingBoard.MemberPreviews.Sum(m => m.LedgerOwedAmount));

        // Verify pro-rata math (+5% net adjustment: 630k / 600k = 1.05x):
        // An: 150k * 1.05 = 157,500; Binh: 150k * 1.05 = 157,500; Chi: 200k * 1.05 = 210,000; Dung: 100k * 1.05 = 105,000
        Assert.Equal(157_500m, claimingBoard.MemberPreviews.Single(m => m.UserId == an.UserId).LedgerOwedAmount);
        Assert.Equal(157_500m, claimingBoard.MemberPreviews.Single(m => m.UserId == binh.UserId).LedgerOwedAmount);
        Assert.Equal(210_000m, claimingBoard.MemberPreviews.Single(m => m.UserId == chi.UserId).LedgerOwedAmount);
        Assert.Equal(105_000m, claimingBoard.MemberPreviews.Single(m => m.UserId == dung.UserId).LedgerOwedAmount);

        var lockedBoard = await harness.ExpenseService.FinalizeReceiptClaimsAsync(scannedDraft.ExpenseId, an.UserId);
        Assert.Equal(ReceiptScanStatus.Locked, lockedBoard.ScanStatus);

        // Guard check: member with non-zero NetBalance cannot leave the trip
        var balancesAfterExpenses = await harness.TripService.GetTripMemberBalancesAsync(createdTrip.Id);
        Assert.Equal(0m, balancesAfterExpenses.Sum(b => b.NetBalance));
        var nonZeroMember = balancesAfterExpenses.First(b => b.UserId != an.UserId && b.NetBalance != 0m);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.RemoveOrLeaveMemberAsync(createdTrip.Id, nonZeroMember.UserId, nonZeroMember.UserId));

        // 7. Transition Ongoing -> Settling, verify Min-Cash-Flow simplification & Napas VietQR URLs
        var settlingStatus = await harness.TripService.TransitionStatusAsync(
            createdTrip.Id,
            an.UserId,
            TripStatus.Settling);
        Assert.Equal(TripStatus.Settling, settlingStatus);

        var settlementWorkspace = await harness.SettlementService.GetWorkspaceAsync(createdTrip.Id, an.UserId);
        Assert.True(settlementWorkspace.IsZeroSumBalanced);
        Assert.Equal(0m, settlementWorkspace.LedgerNetSum);
        Assert.True(settlementWorkspace.RawExpenseObligationCount >= 12);
        Assert.InRange(settlementWorkspace.SimplifiedTransferCount, 1, 3);
        Assert.True(settlementWorkspace.EliminatedTransfersCount > 0);
        Assert.All(settlementWorkspace.ActiveSettlements, card =>
        {
            Assert.True(card.VietQr.IsBankConfigured);
            Assert.NotNull(card.VietQr.QrImageUrl);
            Assert.StartsWith("https://img.vietqr.io/image/", card.VietQr.QrImageUrl);
            Assert.Contains("-compact2.png", card.VietQr.QrImageUrl);
            Assert.Contains($"amount={card.Amount:0}", card.VietQr.QrImageUrl);
        });

        // 8. Execute 2-step settlement handshake (including 1 Dispute -> Re-mark Paid cycle) until auto-completed!
        var activeTransfers = settlementWorkspace.ActiveSettlements.ToList();
        var firstTransfer = activeTransfers[0];

        var markedFirst = await harness.SettlementService.MarkAsPaidAsync(
            firstTransfer.Id,
            firstTransfer.FromUserId,
            "Đã chuyển khoản qua VietQR lần 1");
        Assert.Equal(SettlementStatus.MarkedPaid, markedFirst.Settlement.Status);

        var disputedFirst = await harness.SettlementService.DisputeAsync(
            firstTransfer.Id,
            firstTransfer.ToUserId,
            "Chưa thấy biến động số dư, kiểm tra lại giúp mình nhé");
        Assert.Equal(SettlementStatus.Disputed, disputedFirst.Settlement.Status);

        await harness.SettlementService.MarkAsPaidAsync(
            firstTransfer.Id,
            firstTransfer.FromUserId,
            "Đã chuyển lại thành công mã GD FT26320999");

        for (var i = 0; i < activeTransfers.Count; i++)
        {
            var transfer = activeTransfers[i];
            if (i > 0)
            {
                var marked = await harness.SettlementService.MarkAsPaidAsync(
                    transfer.Id,
                    transfer.FromUserId,
                    $"Chuyển khoản VietQR #{i + 1}");
                Assert.Equal(SettlementStatus.MarkedPaid, marked.Settlement.Status);
            }

            var confirmed = await harness.SettlementService.ConfirmReceivedAsync(
                transfer.Id,
                transfer.ToUserId);

            Assert.Equal(SettlementStatus.Confirmed, confirmed.Settlement.Status);

            var isLastTransfer = i == activeTransfers.Count - 1;
            Assert.Equal(isLastTransfer, confirmed.TripCompleted);
            Assert.Equal(isLastTransfer ? TripStatus.Completed : TripStatus.Settling, confirmed.NewTripStatus);
        }

        // 9. Verify auto-transition to Completed, zero net balances for all members, and frozen Completed roster guard
        var finalTripDetails = await harness.TripService.GetTripDetailsAsync(createdTrip.Id, an.UserId);
        Assert.Equal(TripStatus.Completed, finalTripDetails.Status);
        Assert.NotNull(finalTripDetails.ClosedAtUtc);
        Assert.All(finalTripDetails.MemberBalances, b => Assert.Equal(0m, b.NetBalance));

        // Completed trips lock membership modifications to preserve immutable financial history
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TripService.RemoveOrLeaveMemberAsync(createdTrip.Id, dung.UserId, dung.UserId));

        // 10. Verify System Admin Analytics KPIs reflect completed trip, AI adoption, and Min-Cash-Flow savings
        var finalDashboard = await harness.AdminAnalyticsService.GetDashboardAsync();
        Assert.Equal(4, finalDashboard.Kpis.TotalRegisteredUsers);
        Assert.Equal(1, finalDashboard.Kpis.TotalTrips);
        Assert.Equal(1, finalDashboard.Kpis.CompletedTrips);
        Assert.Equal(4, finalDashboard.Kpis.FinalizedExpenseCount);
        Assert.Equal(6_030_000m, finalDashboard.Kpis.TotalFinalizedVolumeVnd);
        Assert.Equal(1, finalDashboard.Kpis.AiScannedReceiptsCount);
        Assert.True(finalDashboard.Kpis.AiSuggestedStopsCount > 0);
        Assert.True(finalDashboard.Kpis.ConfirmedSettledVnd > 0m);
        Assert.Equal(0m, finalDashboard.Kpis.PendingSettlementVnd);
        Assert.True(finalDashboard.Kpis.TotalEliminatedTransfers > 0);
    }

    [Fact]
    public async Task DemoDataSeeder_SeedIfEmptyAsync_IsIdempotentAndBuildsZeroSumReadyDaLatTrip()
    {
        await using var harness = new FullStackIntegrationHarness();

        var seededFirst = await DemoDataSeeder.SeedIfEmptyAsync(harness.DbContext);
        var seededSecond = await DemoDataSeeder.SeedIfEmptyAsync(harness.DbContext);

        Assert.True(seededFirst);
        Assert.False(seededSecond);

        var demoEmails = new[] { "an@splittrip.vn", "binh@splittrip.vn", "chi@splittrip.vn", "dung@splittrip.vn" };
        Guid ownerId = Guid.Empty;

        foreach (var email in demoEmails)
        {
            var auth = await harness.AuthService.LoginAsync(email, DemoDataSeeder.DemoPassword);
            Assert.NotNull(auth);
            var profile = await harness.AuthService.GetProfileAsync(auth.UserId);
            Assert.NotNull(profile);
            Assert.False(string.IsNullOrWhiteSpace(profile.BankBin));
            Assert.False(string.IsNullOrWhiteSpace(profile.BankAccountNumber));

            if (email == "an@splittrip.vn")
            {
                ownerId = auth.UserId;
            }
        }

        var userTrips = await harness.TripService.GetUserTripsAsync(ownerId);
        var daLatTrip = Assert.Single(userTrips);
        Assert.Equal("Đà Lạt Săn Mây 3N2Đ", daLatTrip.Name);
        Assert.Equal(DemoDataSeeder.DemoInviteCode, daLatTrip.InviteCode);
        Assert.Equal(TripStatus.Ongoing, daLatTrip.Status);
        Assert.Equal(4, daLatTrip.ActiveMemberCount);

        var schedule = await harness.ItineraryService.GetScheduleAsync(daLatTrip.Id, ownerId);
        Assert.Equal(3, schedule.DayCount);
        Assert.Equal(8, schedule.TotalItemsCount);

        var expensesOverview = await harness.ExpenseService.GetTripExpensesOverviewAsync(daLatTrip.Id, ownerId);
        Assert.Equal(5, expensesOverview.ConfirmedExpenseCount);
        Assert.Equal(1, expensesOverview.OpenClaimingCount);

        var openBbq = Assert.Single(expensesOverview.Expenses, e => e.ScanStatus == ReceiptScanStatus.ClaimingOpen);
        var claimingBoard = await harness.ExpenseService.GetClaimingBoardAsync(openBbq.Id, ownerId);
        Assert.Equal(5, claimingBoard.TotalItemCount);
        Assert.Equal(2, claimingBoard.UnclaimedItemCount);

        var balances = await harness.TripService.GetTripMemberBalancesAsync(daLatTrip.Id);
        Assert.Equal(0m, balances.Sum(b => b.NetBalance));
    }

    private sealed class FullStackIntegrationHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AsyncServiceScope _scope;

        public SplitTripDbContext DbContext { get; }
        public IAuthService AuthService { get; }
        public ITripService TripService { get; }
        public IItineraryService ItineraryService { get; }
        public IAiTripPlannerService AiTripPlannerService { get; }
        public IExpenseService ExpenseService { get; }
        public IAiReceiptScannerService ReceiptScannerService { get; }
        public ISettlementService SettlementService { get; }
        public IAdminAnalyticsService AdminAnalyticsService { get; }

        public FullStackIntegrationHarness()
        {
            var dbName = $"SplitTrip_E2E_{Guid.NewGuid():N}";
            var services = new ServiceCollection();

            services.AddDbContext<SplitTripDbContext>(opts => opts.UseInMemoryDatabase(dbName));
            services.AddSingleton<IOptions<AdminAccountOptions>>(Options.Create(new AdminAccountOptions
            {
                Email = "admin@splittrip.vn",
                Password = "Admin@123",
                FullName = "Quản trị viên SplitTrip"
            }));
            services.AddSingleton<IOptions<GeminiOptions>>(Options.Create(new GeminiOptions
            {
                ApiKey = string.Empty,
                Model = "gemini-2.5-flash"
            }));

            services.AddScoped<UserDao>();
            services.AddScoped<TripDao>();
            services.AddScoped<TripMemberDao>();
            services.AddScoped<NotificationDao>();

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITripRepository, TripRepository>();
            services.AddScoped<ITripMemberRepository, TripMemberRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITripService, TripService>();
            services.AddScoped<INotificationService, NotificationService>();

            services.AddSplitTripItineraryServices();
            services.AddSplitTripExpenses();
            services.AddSplitTripSettlements();

            _serviceProvider = services.BuildServiceProvider();
            _scope = _serviceProvider.CreateAsyncScope();

            var sp = _scope.ServiceProvider;
            DbContext = sp.GetRequiredService<SplitTripDbContext>();
            AuthService = sp.GetRequiredService<IAuthService>();
            TripService = sp.GetRequiredService<ITripService>();
            ItineraryService = sp.GetRequiredService<IItineraryService>();
            AiTripPlannerService = sp.GetRequiredService<IAiTripPlannerService>();
            ExpenseService = sp.GetRequiredService<IExpenseService>();
            ReceiptScannerService = sp.GetRequiredService<IAiReceiptScannerService>();
            SettlementService = sp.GetRequiredService<ISettlementService>();
            AdminAnalyticsService = sp.GetRequiredService<IAdminAnalyticsService>();
        }

        public async Task<AuthenticatedUserDto> RegisterMemberWithBankAsync(
            string email,
            string fullName,
            string bankCode,
            string accountNumber)
        {
            var registered = await AuthService.RegisterAsync(new RegisterUserRequest(email, "User@123", fullName));
            await AuthService.UpdateBankProfileAsync(
                registered.UserId,
                new UpdateBankProfileRequest(fullName, null, bankCode, null, accountNumber, fullName));
            return registered;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }
    }
}
