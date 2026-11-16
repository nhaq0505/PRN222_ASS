using System.Security.Claims;
using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;
using CourtBook.Web.Pages.Profile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Tests;

public class ProfilePageTests
{
    [Theory]
    [InlineData(49_999)]
    [InlineData(0)]
    [InlineData(75_000)]
    public async Task DepositAsync_RejectsAmountsOutsideAllowedIncrements(decimal amount)
    {
        await using var db = CreateContext();
        AppUser user = await AddUserAsync(db);
        IndexModel page = CreatePage(db, user.Id);

        await page.OnPostDepositAsync(amount);

        Assert.Equal(0m, (await db.Users.SingleAsync()).WalletBalanceVnd);
        Assert.Empty(await db.WalletTransactions.ToListAsync());
        Assert.Equal(
            "Số tiền nạp phải là bội số 50.000đ trong khoảng 50.000đ đến 100.000.000đ.",
            page.TempData["ToastError"]);
    }

    [Fact]
    public async Task DepositAsync_AcceptsMinimumAmount()
    {
        await using var db = CreateContext();
        AppUser user = await AddUserAsync(db);
        IndexModel page = CreatePage(db, user.Id);

        await page.OnPostDepositAsync(IndexModel.MinimumDepositAmountVnd);

        Assert.Equal(IndexModel.MinimumDepositAmountVnd, (await db.Users.SingleAsync()).WalletBalanceVnd);
        WalletTransaction transaction = await db.WalletTransactions.SingleAsync();
        Assert.Equal(IndexModel.MinimumDepositAmountVnd, transaction.AmountVnd);
    }

    [Fact]
    public async Task UpdateProfileAsync_RejectsValuesExceedingDatabaseLimits()
    {
        await using var db = CreateContext();
        AppUser user = await AddUserAsync(db);
        IndexModel page = CreatePage(db, user.Id);

        await page.OnPostUpdateProfileAsync(
            new string('A', 101),
            "0901234567",
            string.Empty,
            string.Empty);

        Assert.Equal("Nguyễn An", (await db.Users.SingleAsync()).FullName);
        Assert.Equal("Họ và tên phải từ 2 đến 100 ký tự.", page.TempData["ToastError"]);
    }

    private static CourtBookDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CourtBookDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CourtBookDbContext(options);
    }

    private static async Task<AppUser> AddUserAsync(CourtBookDbContext db)
    {
        var user = new AppUser
        {
            FullName = "Nguyễn An",
            Email = "an@example.com",
            PhoneNumber = "0901234567",
            PasswordHash = "test-hash"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IndexModel CreatePage(CourtBookDbContext db, int userId)
    {
        var page = new IndexModel(db);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "test"))
        };
        page.PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext
        {
            HttpContext = httpContext
        };
        page.TempData = new TempDataDictionary(httpContext, new TestTempDataProvider());
        return page;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
