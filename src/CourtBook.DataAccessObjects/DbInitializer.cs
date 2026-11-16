using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.BusinessObjects.Security;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        CourtBookDbContext context,
        string? adminEmail = null,
        string? adminPassword = null,
        string? adminFullName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Database.IsRelational())
        {
            if (context.Database.GetMigrations().Any())
            {
                await context.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
        else
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }

        await SeedUsersAsync(context, adminEmail, adminPassword, adminFullName, cancellationToken);
        await SeedVenuesAndCourtsAsync(context, cancellationToken);
        await SeedSampleBookingsAsync(context, cancellationToken);
    }

    private static async Task SeedUsersAsync(
        CourtBookDbContext context,
        string? adminEmail,
        string? adminPassword,
        string? adminFullName,
        CancellationToken cancellationToken)
    {
        string resolvedAdminEmail = string.IsNullOrWhiteSpace(adminEmail)
            ? "admin@courtbook.vn"
            : adminEmail.Trim().ToLowerInvariant();
        string resolvedAdminPassword = string.IsNullOrWhiteSpace(adminPassword)
            ? "Admin@123"
            : adminPassword;
        string resolvedAdminName = string.IsNullOrWhiteSpace(adminFullName)
            ? "Quản trị sàn CourtBook"
            : adminFullName.Trim();

        var seedUsers = new (string FullName, string Email, string Phone, string Password, UserRole Role)[]
        {
            (resolvedAdminName, resolvedAdminEmail, "0901000001", resolvedAdminPassword, UserRole.Admin),
            ("Lê Minh Khôi", "khoi.owner@courtbook.vn", "0902000011", "Owner@123", UserRole.CourtOwner),
            ("Phạm Thu Hà", "ha.owner@courtbook.vn", "0902000022", "Owner@123", UserRole.CourtOwner),
            ("Nguyễn Văn An", "an@courtbook.vn", "0909123456", "User@123", UserRole.Member),
            ("Trần Thị Bình", "binh@courtbook.vn", "0909654321", "User@123", UserRole.Member)
        };

        List<string> existingEmails = await context.Users
            .Select(u => u.Email.ToLower())
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingEmails, StringComparer.OrdinalIgnoreCase);
        DateTime nowUtc = DateTime.UtcNow;
        bool added = false;

        foreach (var seed in seedUsers)
        {
            if (existingSet.Contains(seed.Email))
            {
                continue;
            }

            context.Users.Add(new AppUser
            {
                FullName = seed.FullName,
                Email = seed.Email,
                PhoneNumber = seed.Phone,
                PasswordHash = PasswordHelper.HashPassword(seed.Password),
                Role = seed.Role,
                IsActive = true,
                CreatedAtUtc = nowUtc
            });
            existingSet.Add(seed.Email);
            added = true;
        }

        if (added)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedVenuesAndCourtsAsync(
        CourtBookDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Venues.AnyAsync(cancellationToken))
        {
            return;
        }

        AppUser? khoi = await context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == "khoi.owner@courtbook.vn", cancellationToken);
        AppUser? ha = await context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == "ha.owner@courtbook.vn", cancellationToken);

        if (khoi is null || ha is null)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;

        var venues = new List<Venue>
        {
            new()
            {
                Slug = "thao-dien-racquet-club",
                Name = "Thảo Điền Racquet Club",
                District = "TP. Thủ Đức",
                Address = "28 Xuân Thủy, Thảo Điền, TP. Thủ Đức",
                PhoneNumber = "0902000011",
                Description = "Tổ hợp Pickleball & Cầu lông chuẩn thi đấu tại trung tâm Thảo Điền, thảm Acrylic Pro chống trơn, bãi xe rộng và căn-tin mát mẻ.",
                AccentColor = "emerald",
                OpenHour = 6,
                CloseHour = 22,
                OwnerId = khoi.Id,
                Status = VenueStatus.Active,
                CreatedAtUtc = nowUtc.AddDays(-20),
                Courts = new List<Court>
                {
                    new()
                    {
                        Code = "PB-01",
                        Name = "Sân Pickleball 1",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 140_000m,
                        PeakPricePerHour = 190_000m,
                        IsActive = true,
                        DisplayOrder = 1
                    },
                    new()
                    {
                        Code = "PB-02",
                        Name = "Sân Pickleball 2",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 140_000m,
                        PeakPricePerHour = 190_000m,
                        IsActive = true,
                        DisplayOrder = 2
                    },
                    new()
                    {
                        Code = "PB-03",
                        Name = "Sân Pickleball 3",
                        SportType = SportType.Pickleball,
                        IsIndoor = false,
                        Surface = "Mặt sân Hardcourt Mái che",
                        BasePricePerHour = 110_000m,
                        PeakPricePerHour = 150_000m,
                        IsActive = true,
                        DisplayOrder = 3
                    },
                    new()
                    {
                        Code = "PB-04",
                        Name = "Sân Pickleball 4",
                        SportType = SportType.Pickleball,
                        IsIndoor = false,
                        Surface = "Mặt sân Hardcourt Mái che",
                        BasePricePerHour = 110_000m,
                        PeakPricePerHour = 150_000m,
                        IsActive = true,
                        DisplayOrder = 4
                    },
                    new()
                    {
                        Code = "BD-01",
                        Name = "Sân Cầu lông 1",
                        SportType = SportType.Badminton,
                        IsIndoor = true,
                        Surface = "Thảm PVC Yonex",
                        BasePricePerHour = 100_000m,
                        PeakPricePerHour = 140_000m,
                        IsActive = true,
                        DisplayOrder = 5
                    },
                    new()
                    {
                        Code = "BD-02",
                        Name = "Sân Cầu lông 2",
                        SportType = SportType.Badminton,
                        IsIndoor = true,
                        Surface = "Thảm PVC Yonex",
                        BasePricePerHour = 100_000m,
                        PeakPricePerHour = 140_000m,
                        IsActive = true,
                        DisplayOrder = 6
                    }
                }
            },
            new()
            {
                Slug = "sala-riverside-pickleball",
                Name = "Sala Riverside Pickleball",
                District = "TP. Thủ Đức",
                Address = "10 Mai Chí Thọ, Khu đô thị Sala, TP. Thủ Đức",
                PhoneNumber = "0902000011",
                Description = "Cụm 4 sân Pickleball ven sông thoáng gió tại khu đô thị Sala, hệ thống đèn LED chống chói tiêu chuẩn giải đấu buổi tối.",
                AccentColor = "teal",
                OpenHour = 6,
                CloseHour = 22,
                OwnerId = khoi.Id,
                Status = VenueStatus.Active,
                CreatedAtUtc = nowUtc.AddDays(-14),
                Courts = new List<Court>
                {
                    new()
                    {
                        Code = "PB-01",
                        Name = "Sân Sala PB 1",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 180_000m,
                        IsActive = true,
                        DisplayOrder = 1
                    },
                    new()
                    {
                        Code = "PB-02",
                        Name = "Sân Sala PB 2",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 180_000m,
                        IsActive = true,
                        DisplayOrder = 2
                    },
                    new()
                    {
                        Code = "PB-03",
                        Name = "Sân Sala PB 3",
                        SportType = SportType.Pickleball,
                        IsIndoor = false,
                        Surface = "Mặt sân Hardcourt Mái che",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 180_000m,
                        IsActive = true,
                        DisplayOrder = 3
                    },
                    new()
                    {
                        Code = "PB-04",
                        Name = "Sân Sala PB 4",
                        SportType = SportType.Pickleball,
                        IsIndoor = false,
                        Surface = "Mặt sân Hardcourt Mái che",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 180_000m,
                        IsActive = true,
                        DisplayOrder = 4
                    }
                }
            },
            new()
            {
                Slug = "phu-my-hung-sport-center",
                Name = "Phú Mỹ Hưng Sport Center",
                District = "Quận 7",
                Address = "105 Nguyễn Đức Cảnh, Phú Mỹ Hưng, Quận 7",
                PhoneNumber = "0902000022",
                Description = "Trung tâm thể thao đa năng khu Nam Sài Gòn với sân Pickleball & Cầu lông trong nhà tiêu chuẩn, khu thay đồ và gửi xe miễn phí.",
                AccentColor = "blue",
                OpenHour = 6,
                CloseHour = 22,
                OwnerId = ha.Id,
                Status = VenueStatus.Active,
                CreatedAtUtc = nowUtc.AddDays(-10),
                Courts = new List<Court>
                {
                    new()
                    {
                        Code = "PB-01",
                        Name = "Sân PMH Pickleball 1",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 175_000m,
                        IsActive = true,
                        DisplayOrder = 1
                    },
                    new()
                    {
                        Code = "PB-02",
                        Name = "Sân PMH Pickleball 2",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Pro",
                        BasePricePerHour = 130_000m,
                        PeakPricePerHour = 175_000m,
                        IsActive = true,
                        DisplayOrder = 2
                    },
                    new()
                    {
                        Code = "BD-01",
                        Name = "Sân PMH Cầu lông 1",
                        SportType = SportType.Badminton,
                        IsIndoor = true,
                        Surface = "Thảm PVC Yonex",
                        BasePricePerHour = 95_000m,
                        PeakPricePerHour = 135_000m,
                        IsActive = true,
                        DisplayOrder = 3
                    },
                    new()
                    {
                        Code = "BD-02",
                        Name = "Sân PMH Cầu lông 2",
                        SportType = SportType.Badminton,
                        IsIndoor = true,
                        Surface = "Thảm PVC Yonex",
                        BasePricePerHour = 95_000m,
                        PeakPricePerHour = 135_000m,
                        IsActive = true,
                        DisplayOrder = 4
                    },
                    new()
                    {
                        Code = "BD-03",
                        Name = "Sân PMH Cầu lông 3",
                        SportType = SportType.Badminton,
                        IsIndoor = true,
                        Surface = "Thảm PVC Yonex",
                        BasePricePerHour = 95_000m,
                        PeakPricePerHour = 135_000m,
                        IsActive = true,
                        DisplayOrder = 5
                    }
                }
            },
            new()
            {
                Slug = "tan-binh-arena",
                Name = "Tân Bình Arena",
                District = "Quận Tân Bình",
                Address = "18 Cộng Hòa, Phường 4, Quận Tân Bình",
                PhoneNumber = "0902000022",
                Description = "Cụm sân Pickleball mới khai trương gần sân bay Tân Sơn Nhất, không gian mái vòm cao ráo, thuận tiện di chuyển.",
                AccentColor = "amber",
                OpenHour = 6,
                CloseHour = 22,
                OwnerId = ha.Id,
                Status = VenueStatus.PendingApproval,
                CreatedAtUtc = nowUtc.AddDays(-1),
                Courts = new List<Court>
                {
                    new()
                    {
                        Code = "PB-01",
                        Name = "Sân Tân Bình 1",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Cushion",
                        BasePricePerHour = 120_000m,
                        PeakPricePerHour = 160_000m,
                        IsActive = true,
                        DisplayOrder = 1
                    },
                    new()
                    {
                        Code = "PB-02",
                        Name = "Sân Tân Bình 2",
                        SportType = SportType.Pickleball,
                        IsIndoor = true,
                        Surface = "Thảm Acrylic Cushion",
                        BasePricePerHour = 120_000m,
                        PeakPricePerHour = 160_000m,
                        IsActive = true,
                        DisplayOrder = 2
                    },
                    new()
                    {
                        Code = "PB-03",
                        Name = "Sân Tân Bình 3",
                        SportType = SportType.Pickleball,
                        IsIndoor = false,
                        Surface = "Hardcourt Ngoài trời",
                        BasePricePerHour = 120_000m,
                        PeakPricePerHour = 160_000m,
                        IsActive = true,
                        DisplayOrder = 3
                    }
                }
            }
        };

        context.Venues.AddRange(venues);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSampleBookingsAsync(
        CourtBookDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Bookings.AnyAsync(cancellationToken))
        {
            return;
        }

        AppUser? an = await context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == "an@courtbook.vn", cancellationToken);
        AppUser? binh = await context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == "binh@courtbook.vn", cancellationToken);

        if (an is null || binh is null)
        {
            return;
        }

        List<Court> allCourts = await context.Courts
            .Include(c => c.Venue)
            .ToListAsync(cancellationToken);

        var courtMap = allCourts
            .Where(c => c.Venue is not null)
            .ToDictionary(
                c => (c.Venue!.Slug, c.Code),
                c => c);

        if (courtMap.Count == 0)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(GetVietnamNow());
        DateOnly tomorrow = today.AddDays(1);
        DateTime nowUtc = DateTime.UtcNow;

        var candidates = new (string VenueSlug, string CourtCode, AppUser User, DateOnly Date, int Hour, BookingStatus Status, string Notes)[]
        {
            ("thao-dien-racquet-club", "PB-01", an, today, 7, BookingStatus.CheckedIn, "Đánh giao hữu buổi sáng"),
            ("thao-dien-racquet-club", "PB-02", binh, today, 18, BookingStatus.Confirmed, "Thuê 2 vợt Pickleball tại quầy"),
            ("thao-dien-racquet-club", "BD-01", an, today, 19, BookingStatus.Confirmed, "CLB Cầu lông buổi tối"),
            ("sala-riverside-pickleball", "PB-01", binh, today, 17, BookingStatus.CheckedIn, "Tập đánh đôi cuối chiều"),
            ("sala-riverside-pickleball", "PB-02", an, tomorrow, 19, BookingStatus.Confirmed, "Giao hữu nhóm Sala"),
            ("phu-my-hung-sport-center", "PB-01", an, today, 20, BookingStatus.Confirmed, "Đánh đơn buổi tối"),
            ("phu-my-hung-sport-center", "BD-01", binh, tomorrow, 7, BookingStatus.Confirmed, "Tập cầu lông sáng"),
            ("thao-dien-racquet-club", "PB-03", binh, tomorrow, 18, BookingStatus.Confirmed, "Lịch tập định kỳ")
        };

        var bookings = new List<Booking>();
        int seq = 1;

        foreach (var item in candidates)
        {
            if (!courtMap.TryGetValue((item.VenueSlug, item.CourtCode), out Court? court))
            {
                continue;
            }

            bookings.Add(new Booking
            {
                BookingCode = $"CB-{item.Date:yyyyMMdd}-{seq:D4}",
                CourtId = court.Id,
                UserId = item.User.Id,
                PlayDate = item.Date,
                StartHour = item.Hour,
                PriceVnd = court.GetPriceForHour(item.Hour),
                Status = item.Status,
                Notes = item.Notes,
                CreatedAtUtc = nowUtc.AddHours(-3),
                ConfirmedAtUtc = nowUtc.AddHours(-3),
                CheckedInAtUtc = item.Status == BookingStatus.CheckedIn ? nowUtc.AddMinutes(-20) : null
            });
            seq++;
        }

        if (bookings.Count > 0)
        {
            context.Bookings.AddRange(bookings);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static DateTime GetVietnamNow()
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                tz = TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "Indochina Time", "ICT");
            }
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    }
}
