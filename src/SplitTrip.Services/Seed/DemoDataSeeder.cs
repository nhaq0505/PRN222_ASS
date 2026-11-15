using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.DataAccessObjects;
using SplitTrip.Services.Expenses;
using SplitTrip.Services.Expenses.Dtos;
using SplitTrip.Services.Settlements;
using SplitTrip.Services.Settlements.Dtos;

namespace SplitTrip.Services.Seed;

public static class DemoDataSeeder
{
    public const string DemoPassword = "User@123";
    public const string DemoInviteCode = "DALAT3N2";

    public static async Task<bool> SeedIfEmptyAsync(
        SplitTripDbContext dbContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (await dbContext.Users.AnyAsync(ct))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var todayVietnam = DateOnly.FromDateTime(nowUtc.AddHours(7));
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(DemoPassword);

        // 1. Four demo travelers with valid Napas VietQR bank profiles
        var an = new User
        {
            Id = Guid.NewGuid(),
            Email = "an@splittrip.vn",
            PasswordHash = passwordHash,
            FullName = "Nguyễn Văn An",
            BankCode = "VCB",
            BankBin = "970436",
            BankAccountNumber = "1029384756",
            BankAccountHolder = "NGUYEN VAN AN",
            CreatedAtUtc = nowUtc.AddDays(-5)
        };

        var binh = new User
        {
            Id = Guid.NewGuid(),
            Email = "binh@splittrip.vn",
            PasswordHash = passwordHash,
            FullName = "Trần Thị Bình",
            BankCode = "TCB",
            BankBin = "970407",
            BankAccountNumber = "19038475621018",
            BankAccountHolder = "TRAN THI BINH",
            CreatedAtUtc = nowUtc.AddDays(-5)
        };

        var chi = new User
        {
            Id = Guid.NewGuid(),
            Email = "chi@splittrip.vn",
            PasswordHash = passwordHash,
            FullName = "Lê Hoàng Chi",
            BankCode = "MB",
            BankBin = "970422",
            BankAccountNumber = "0987654321",
            BankAccountHolder = "LE HOANG CHI",
            CreatedAtUtc = nowUtc.AddDays(-4)
        };

        var dung = new User
        {
            Id = Guid.NewGuid(),
            Email = "dung@splittrip.vn",
            PasswordHash = passwordHash,
            FullName = "Phạm Quốc Dũng",
            BankCode = "ACB",
            BankBin = "970416",
            BankAccountNumber = "23456789",
            BankAccountHolder = "PHAM QUOC DUNG",
            CreatedAtUtc = nowUtc.AddDays(-4)
        };

        var demoUsers = new[] { an, binh, chi, dung };
        dbContext.Users.AddRange(demoUsers);

        // 2. Ongoing 3-day / 2-night Da Lat trip (spanning yesterday -> tomorrow so live editing & claiming stay open)
        var startDate = todayVietnam.AddDays(-1);
        var endDate = todayVietnam.AddDays(1);
        var tripId = Guid.NewGuid();

        var trip = new Trip
        {
            Id = tripId,
            Name = "Đà Lạt Săn Mây 3N2Đ",
            Destination = "Đà Lạt, Lâm Đồng",
            Description = "Hành trình săn mây đồi chè Cầu Đất, chèo SUP Hồ Tuyền Lâm và thưởng thức tiệc nướng BBQ phố núi cùng hội bạn thân.",
            InviteCode = DemoInviteCode,
            StartDate = startDate,
            EndDate = endDate,
            EstimatedBudgetPerPerson = 2_500_000m,
            Status = TripStatus.Ongoing,
            OwnerUserId = an.Id,
            CreatedAtUtc = nowUtc.AddDays(-3)
        };
        dbContext.Trips.Add(trip);

        var members = new[]
        {
            new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                UserId = an.Id,
                Role = TripMemberRole.Owner,
                IsActive = true,
                JoinedAtUtc = nowUtc.AddDays(-3)
            },
            new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                UserId = binh.Id,
                Role = TripMemberRole.Member,
                IsActive = true,
                JoinedAtUtc = nowUtc.AddDays(-3).AddMinutes(12)
            },
            new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                UserId = chi.Id,
                Role = TripMemberRole.Member,
                IsActive = true,
                JoinedAtUtc = nowUtc.AddDays(-3).AddMinutes(25)
            },
            new TripMember
            {
                Id = Guid.NewGuid(),
                TripId = tripId,
                UserId = dung.Id,
                Role = TripMemberRole.Member,
                IsActive = true,
                JoinedAtUtc = nowUtc.AddDays(-3).AddMinutes(40)
            }
        };
        dbContext.TripMembers.AddRange(members);

        // 3. Three TripDays with realistic Da Lat stops & member votes
        var day1 = new TripDay
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            DayNumber = 1,
            Date = startDate,
            Title = "Ngày 1: Đón bình minh Cầu Đất & dạo trung tâm phố núi",
            Notes = "Xuất phát sớm lúc 04:30 sáng để kịp biển mây bình minh."
        };

        var day2 = new TripDay
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            DayNumber = 2,
            Date = todayVietnam,
            Title = "Ngày 2: Chèo SUP Hồ Tuyền Lâm & Tiệc nướng BBQ tối",
            Notes = "Mang theo áo khoác giữ ấm buổi chiều tối."
        };

        var day3 = new TripDay
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            DayNumber = 3,
            Date = endDate,
            Title = "Ngày 3: Mua đặc sản Chợ Đà Lạt & khởi hành về TP.HCM",
            Notes = "Trả phòng homestay trước 11:00 trưa."
        };
        dbContext.TripDays.AddRange(day1, day2, day3);

        var stop1 = CreateStop(
            day1.Id,
            sortOrder: 0,
            title: "Săn mây & thưởng trà Đồi chè Cầu Đất",
            location: "Đồi chè Cầu Đất",
            address: "Thôn Trường Thọ, Xã Trạm Hành, TP. Đà Lạt",
            start: new TimeOnly(5, 0),
            end: new TimeOnly(7, 15),
            estimatedCost: 120_000m,
            notes: "Thuê cầu gỗ săn mây và uống cà phê Arabica nóng.",
            isAiSuggested: false,
            createdByUserId: an.Id,
            createdAtUtc: nowUtc.AddDays(-2),
            lat: 11.8749,
            lng: 108.5633);

        var stop2 = CreateStop(
            day1.Id,
            sortOrder: 1,
            title: "Ăn sáng Bánh căn Lệ & sữa đậu nành nóng",
            location: "Bánh căn Lệ Yersin",
            address: "27/44 Yersin, Phường 10, TP. Đà Lạt",
            start: new TimeOnly(8, 0),
            end: new TimeOnly(9, 30),
            estimatedCost: 65_000m,
            notes: "Bánh căn trứng cút lòng đào + xíu mại chén.",
            isAiSuggested: true,
            createdByUserId: binh.Id,
            createdAtUtc: nowUtc.AddDays(-2).AddMinutes(10),
            lat: 11.9426,
            lng: 108.4521);

        var stop3 = CreateStop(
            day1.Id,
            sortOrder: 2,
            title: "Nhận phòng Homestay Đồi Thông & dạo Quảng trường Lâm Viên",
            location: "Quảng trường Lâm Viên",
            address: "Đường Trần Quốc Toản, Phường 1, TP. Đà Lạt",
            start: new TimeOnly(14, 30),
            end: new TimeOnly(17, 0),
            estimatedCost: 50_000m,
            notes: "Check-in nụ hoa Atiso và dạo quanh Hồ Xuân Hương.",
            isAiSuggested: false,
            createdByUserId: chi.Id,
            createdAtUtc: nowUtc.AddDays(-2).AddMinutes(20),
            lat: 11.9404,
            lng: 108.4451);

        var stop4 = CreateStop(
            day2.Id,
            sortOrder: 0,
            title: "Chèo SUP sương sớm & ngắm rừng thông Hồ Tuyền Lâm",
            location: "Khu du lịch Hồ Tuyền Lâm",
            address: "Phường 4, TP. Đà Lạt",
            start: new TimeOnly(8, 30),
            end: new TimeOnly(11, 30),
            estimatedCost: 250_000m,
            notes: "Đã đặt trước 2 thuyền SUP đôi kèm áo phao.",
            isAiSuggested: true,
            createdByUserId: dung.Id,
            createdAtUtc: nowUtc.AddDays(-2).AddMinutes(35),
            lat: 11.9025,
            lng: 108.4316);

        var stop5 = CreateStop(
            day2.Id,
            sortOrder: 1,
            title: "Ngắm hoàng hôn nhà kính tại Tiệm cà phê Túi Mơ To",
            location: "Tiệm cà phê Túi Mơ To",
            address: "Hẻm 31 Sào Nam, Phường 11, TP. Đà Lạt",
            start: new TimeOnly(15, 30),
            end: new TimeOnly(17, 30),
            estimatedCost: 105_000m,
            notes: "Vườn cúc họa mi và góc view thung lũng đèn.",
            isAiSuggested: true,
            createdByUserId: chi.Id,
            createdAtUtc: nowUtc.AddDays(-2).AddMinutes(45),
            lat: 11.9512,
            lng: 108.4789);

        var stop6 = CreateStop(
            day2.Id,
            sortOrder: 2,
            title: "Tiệc nướng BBQ Tiệm Nướng Thương Thương",
            location: "Tiệm Nướng Thương Thương",
            address: "30 Đặng Thái Thân, Phường 3, TP. Đà Lạt",
            start: new TimeOnly(19, 0),
            end: new TimeOnly(21, 30),
            estimatedCost: 250_000m,
            notes: "Bàn ngoài trời ngắm rừng thông — quét hóa đơn AI chia món trực tiếp!",
            isAiSuggested: false,
            createdByUserId: an.Id,
            createdAtUtc: nowUtc.AddDays(-2).AddMinutes(55),
            lat: 11.9298,
            lng: 108.4488);

        var stop7 = CreateStop(
            day3.Id,
            sortOrder: 0,
            title: "Mua dâu tây & mứt đặc sản tại Chợ Đà Lạt",
            location: "Chợ Đà Lạt",
            address: "Đường Nguyễn Thị Minh Khai, Phường 1, TP. Đà Lạt",
            start: new TimeOnly(8, 0),
            end: new TimeOnly(10, 0),
            estimatedCost: 300_000m,
            notes: "Mua dâu giống Nhật, trà Atiso và khoai lang mật làm quà.",
            isAiSuggested: false,
            createdByUserId: binh.Id,
            createdAtUtc: nowUtc.AddDays(-1),
            lat: 11.9428,
            lng: 108.4368);

        var stop8 = CreateStop(
            day3.Id,
            sortOrder: 1,
            title: "Ăn trưa Lẩu gà lá é Tao Ngộ trước giờ lên xe",
            location: "Lẩu gà lá é Tao Ngộ",
            address: "5 Đường 3/4, Phường 3, TP. Đà Lạt",
            start: new TimeOnly(11, 15),
            end: new TimeOnly(12, 45),
            estimatedCost: 180_000m,
            notes: "Xe Limousine đón cả nhóm lúc 13:30.",
            isAiSuggested: true,
            createdByUserId: an.Id,
            createdAtUtc: nowUtc.AddDays(-1).AddMinutes(15),
            lat: 11.9332,
            lng: 108.4435);

        dbContext.ItineraryItems.AddRange(stop1, stop2, stop3, stop4, stop5, stop6, stop7, stop8);

        var votes = new List<ItineraryVote>
        {
            CreateVote(stop1.Id, an.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop1.Id, binh.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop1.Id, chi.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop1.Id, dung.Id, VoteType.Upvote, nowUtc),

            CreateVote(stop2.Id, an.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop2.Id, binh.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop2.Id, chi.Id, VoteType.Upvote, nowUtc),

            CreateVote(stop4.Id, an.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop4.Id, binh.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop4.Id, dung.Id, VoteType.Upvote, nowUtc),

            CreateVote(stop5.Id, binh.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop5.Id, chi.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop5.Id, dung.Id, VoteType.Downvote, nowUtc),

            CreateVote(stop6.Id, an.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop6.Id, binh.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop6.Id, chi.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop6.Id, dung.Id, VoteType.Upvote, nowUtc),

            CreateVote(stop8.Id, an.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop8.Id, chi.Id, VoteType.Upvote, nowUtc),
            CreateVote(stop8.Id, dung.Id, VoteType.Upvote, nowUtc)
        };
        dbContext.ItineraryVotes.AddRange(votes);

        // 4. Multi-mode & itemized shared expenses across all 4 payers (creating circular pairwise obligations)
        // Expense 1: An pays Accommodation (Shares: 1.5, 1.5, 1, 1) -> 3,200,000 VND
        var (expHomestay, splitsHomestay) = BuildManualExpense(
            tripId,
            paidByUserId: an.Id,
            title: "Homestay Đồi Thông Đà Lạt (nguyên căn 2 đêm)",
            category: ExpenseCategory.Accommodation,
            totalAmount: 3_200_000m,
            splitMode: ExpenseSplitMode.Shares,
            participants:
            [
                new ParticipantShareInput(an.Id, 1.5m),
                new ParticipantShareInput(binh.Id, 1.5m),
                new ParticipantShareInput(chi.Id, 1.0m),
                new ParticipantShareInput(dung.Id, 1.0m)
            ],
            spentAtUtc: nowUtc.AddHours(-30),
            notes: "An & Bình ở phòng Master view ban công (1.5 phần/người); Chi & Dũng phòng tiêu chuẩn.");

        // Expense 2: Binh pays Limousine Transport (Equal across all 4) -> 2,600,000 VND
        var (expTransport, splitsTransport) = BuildManualExpense(
            tripId,
            paidByUserId: binh.Id,
            title: "Vé xe Limousine phòng đôi khứ hồi TP.HCM - Đà Lạt",
            category: ExpenseCategory.Transport,
            totalAmount: 2_600_000m,
            splitMode: ExpenseSplitMode.Equal,
            participants:
            [
                new ParticipantShareInput(an.Id, 1m),
                new ParticipantShareInput(binh.Id, 1m),
                new ParticipantShareInput(chi.Id, 1m),
                new ParticipantShareInput(dung.Id, 1m)
            ],
            spentAtUtc: nowUtc.AddHours(-26),
            notes: "4 vé khứ hồi nhà xe Thành Bưởi / Phương Trang VIP (650.000đ/người).");

        // Expense 3: Chi pays Cloud Hunting & SUP Tickets (Percentage: 30%, 30%, 20%, 20%) -> 1,200,000 VND
        var (expTickets, splitsTickets) = BuildManualExpense(
            tripId,
            paidByUserId: chi.Id,
            title: "Combo săn mây Cầu Đất & chèo SUP Hồ Tuyền Lâm",
            category: ExpenseCategory.Ticket,
            totalAmount: 1_200_000m,
            splitMode: ExpenseSplitMode.Percentage,
            participants:
            [
                new ParticipantShareInput(an.Id, 30m),
                new ParticipantShareInput(binh.Id, 30m),
                new ParticipantShareInput(chi.Id, 20m),
                new ParticipantShareInput(dung.Id, 20m)
            ],
            spentAtUtc: nowUtc.AddHours(-14),
            notes: "An & Bình thuê thêm gói chụp ảnh flycam trên hồ.");

        // Expense 4: Dung pays Cafe & Pastries (ExactAmount) -> 420,000 VND
        var (expCafe, splitsCafe) = BuildManualExpense(
            tripId,
            paidByUserId: dung.Id,
            title: "Cà phê Túi Mơ To & bánh ngọt buổi chiều",
            category: ExpenseCategory.Food,
            totalAmount: 420_000m,
            splitMode: ExpenseSplitMode.ExactAmount,
            participants:
            [
                new ParticipantShareInput(an.Id, 110_000m),
                new ParticipantShareInput(binh.Id, 95_000m),
                new ParticipantShareInput(chi.Id, 105_000m),
                new ParticipantShareInput(dung.Id, 110_000m)
            ],
            spentAtUtc: nowUtc.AddHours(-6),
            notes: "Tính theo đúng đồ uống từng bạn gọi tại quầy.");

        // Expense 5: ClaimingOpen BBQ Receipt paid by An (ready for live 2-browser dish claiming in PRN222 defense!)
        // Subtotal = 950,000; VAT 8% = 76,000; Member Discount = 26,000 -> Total = 1,000,000 VND
        var bbqExpenseId = Guid.NewGuid();
        var bbqSpentAt = nowUtc.AddHours(-2);

        var bbqItem1 = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            ItemName = "Combo Bò Mỹ nướng tảng sốt tiêu đen",
            Quantity = 1,
            UnitPrice = 360_000m,
            LineTotal = 360_000m,
            SortOrder = 0
        };
        var bbqItem2 = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            ItemName = "Ba chỉ heo bản lớn nướng kim chi",
            Quantity = 2,
            UnitPrice = 135_000m,
            LineTotal = 270_000m,
            SortOrder = 1
        };
        var bbqItem3 = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            ItemName = "Lẩu gà lá é nấm rừng size vừa",
            Quantity = 1,
            UnitPrice = 220_000m,
            LineTotal = 220_000m,
            SortOrder = 2
        };
        var bbqItem4 = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            ItemName = "Rau củ nướng mật ong Đà Lạt",
            Quantity = 1,
            UnitPrice = 60_000m,
            LineTotal = 60_000m,
            SortOrder = 3
        };
        var bbqItem5 = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            ItemName = "Trà đào cam sả ấm lớn",
            Quantity = 1,
            UnitPrice = 40_000m,
            LineTotal = 40_000m,
            SortOrder = 4
        };

        var bbqItems = new[] { bbqItem1, bbqItem2, bbqItem3, bbqItem4, bbqItem5 };
        var bbqClaims = new List<ReceiptItemClaim>
        {
            // Item 1 claimed by An & Dung
            new() { Id = Guid.NewGuid(), ReceiptItemId = bbqItem1.Id, UserId = an.Id, ClaimedAtUtc = bbqSpentAt.AddMinutes(5) },
            new() { Id = Guid.NewGuid(), ReceiptItemId = bbqItem1.Id, UserId = dung.Id, ClaimedAtUtc = bbqSpentAt.AddMinutes(6) },
            // Item 2 claimed by Binh & Chi
            new() { Id = Guid.NewGuid(), ReceiptItemId = bbqItem2.Id, UserId = binh.Id, ClaimedAtUtc = bbqSpentAt.AddMinutes(7) },
            new() { Id = Guid.NewGuid(), ReceiptItemId = bbqItem2.Id, UserId = chi.Id, ClaimedAtUtc = bbqSpentAt.AddMinutes(8) },
            // Item 4 claimed by Chi
            new() { Id = Guid.NewGuid(), ReceiptItemId = bbqItem4.Id, UserId = chi.Id, ClaimedAtUtc = bbqSpentAt.AddMinutes(9) }
            // Item 3 (Lau ga la e - 220k) & Item 5 (Tra dao cam sa - 40k) intentionally left UNCLAIMED for live demo!
        };

        var calcItems = bbqItems
            .Select(item => (
                ItemId: item.Id,
                item.LineTotal,
                Claims: (IReadOnlyList<(Guid UserId, DateTime ClaimedAtUtc)>)bbqClaims
                    .Where(c => c.ReceiptItemId == item.Id)
                    .Select(c => (c.UserId, c.ClaimedAtUtc))
                    .ToList()))
            .ToList();

        var bbqAllocation = ExpenseSplitCalculator.CalculateItemizedProRataAllocation(
            subtotalAmount: 950_000m,
            totalAmount: 1_000_000m,
            paidByUserId: an.Id,
            items: calcItems,
            allowUnclaimedFallbackToPayer: true);

        var expBbq = new Expense
        {
            Id = bbqExpenseId,
            TripId = tripId,
            PaidByUserId = an.Id,
            Title = "Tiệc nướng BBQ Tiệm Nướng Thương Thương",
            Category = ExpenseCategory.Food,
            SubtotalAmount = 950_000m,
            TaxOrTipAmount = 76_000m,
            DiscountAmount = 26_000m,
            TotalAmount = 1_000_000m,
            SplitMode = ExpenseSplitMode.Itemized,
            ScanStatus = ReceiptScanStatus.ClaimingOpen,
            SpentAtUtc = bbqSpentAt,
            Notes = "Hóa đơn quét bằng Gemini AI Vision (VAT 8% + giảm giá thành viên 26.000đ). Còn 2 món đang mở để cả nhóm bấm nhận trực tiếp!",
            CreatedAtUtc = bbqSpentAt
        };

        var splitsBbq = bbqAllocation.ReconciledSplits.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = bbqExpenseId,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        var allExpenses = new[] { expHomestay, expTransport, expTickets, expCafe, expBbq };
        var allSplits = splitsHomestay
            .Concat(splitsTransport)
            .Concat(splitsTickets)
            .Concat(splitsCafe)
            .Concat(splitsBbq)
            .ToList();

        dbContext.Expenses.AddRange(allExpenses);
        dbContext.ExpenseSplits.AddRange(allSplits);
        dbContext.ReceiptItems.AddRange(bbqItems);
        dbContext.ReceiptItemClaims.AddRange(bbqClaims);

        // 5. Pre-materialize initial Min-Cash-Flow Pending settlements so /Trips/Settlement & /Admin show live stats immediately
        var netByUser = demoUsers.ToDictionary(u => u.Id, _ => 0m);
        foreach (var exp in allExpenses)
        {
            netByUser[exp.PaidByUserId] += exp.TotalAmount;
        }

        foreach (var split in allSplits)
        {
            netByUser[split.UserId] -= split.OwedAmount;
        }

        var simplifier = new MinCashFlowService();
        var vietQr = new VietQrService();
        var balanceInputs = demoUsers
            .Select(u => new MemberBalanceInput(u.Id, u.FullName, netByUser[u.Id]))
            .ToList();

        var simplifiedPlan = simplifier.Simplify(balanceInputs);
        var initialSettlements = simplifiedPlan.Transfers.Select(t => new Settlement
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            FromUserId = t.FromUserId,
            ToUserId = t.ToUserId,
            Amount = t.Amount,
            Status = SettlementStatus.Pending,
            TransferNote = vietQr.BuildTransferNote(DemoInviteCode, t.FromUserName),
            CreatedAtUtc = nowUtc.AddMinutes(-30)
        }).ToList();

        dbContext.Settlements.AddRange(initialSettlements);

        // 6. Helpful unread notifications pointing directly to the live BBQ claiming board
        var notifications = new[] { binh, chi, dung }
            .Select(member => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = member.Id,
                TripId = tripId,
                Type = NotificationType.ExpenseAdded,
                Title = "Hóa đơn BBQ Đà Lạt đang mở chọn món trực tiếp",
                Message = "Nguyễn Văn An vừa mở nhận món cho hóa đơn \"Tiệc nướng BBQ Tiệm Nướng Thương Thương\" (1.000.000đ). Bấm vào để chọn món bạn đã dùng!",
                ActionUrl = $"/Trips/ReceiptClaim?tripId={tripId}&expenseId={bbqExpenseId}",
                IsRead = false,
                CreatedAtUtc = bbqSpentAt.AddMinutes(2)
            })
            .ToList();

        dbContext.Notifications.AddRange(notifications);

        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    private static (Expense Expense, List<ExpenseSplit> Splits) BuildManualExpense(
        Guid tripId,
        Guid paidByUserId,
        string title,
        ExpenseCategory category,
        decimal totalAmount,
        ExpenseSplitMode splitMode,
        IReadOnlyList<ParticipantShareInput> participants,
        DateTime spentAtUtc,
        string notes)
    {
        var expenseId = Guid.NewGuid();
        var computed = ExpenseSplitCalculator.CalculateSplits(splitMode, totalAmount, paidByUserId, participants);

        var expense = new Expense
        {
            Id = expenseId,
            TripId = tripId,
            PaidByUserId = paidByUserId,
            Title = title,
            Category = category,
            SubtotalAmount = totalAmount,
            TaxOrTipAmount = 0m,
            DiscountAmount = 0m,
            TotalAmount = totalAmount,
            SplitMode = splitMode,
            ScanStatus = ReceiptScanStatus.None,
            SpentAtUtc = spentAtUtc,
            Notes = notes,
            CreatedAtUtc = spentAtUtc
        };

        var splits = computed.Select(s => new ExpenseSplit
        {
            Id = Guid.NewGuid(),
            ExpenseId = expenseId,
            UserId = s.UserId,
            ShareRatioOrWeight = s.ShareRatioOrWeight,
            OwedAmount = s.OwedAmount
        }).ToList();

        return (expense, splits);
    }

    private static ItineraryItem CreateStop(
        Guid tripDayId,
        int sortOrder,
        string title,
        string location,
        string address,
        TimeOnly start,
        TimeOnly end,
        decimal estimatedCost,
        string notes,
        bool isAiSuggested,
        Guid createdByUserId,
        DateTime createdAtUtc,
        double? lat = null,
        double? lng = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TripDayId = tripDayId,
            Title = title,
            LocationName = location,
            Address = address,
            Latitude = lat,
            Longitude = lng,
            StartTime = start,
            EndTime = end,
            EstimatedCost = estimatedCost,
            SortOrder = sortOrder,
            Notes = notes,
            IsAiSuggested = isAiSuggested,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc
        };

    private static ItineraryVote CreateVote(
        Guid itemId,
        Guid userId,
        VoteType voteType,
        DateTime createdAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            ItineraryItemId = itemId,
            UserId = userId,
            Vote = voteType,
            CreatedAtUtc = createdAtUtc
        };
}
