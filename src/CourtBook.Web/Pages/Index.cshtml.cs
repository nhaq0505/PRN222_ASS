using CourtBook.BusinessObjects.Enums;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IVenueService _venueService;

    public IndexModel(IVenueService venueService)
    {
        _venueService = venueService;
    }

    public MarketplaceBrowseDto Browse { get; set; } = new();

    public async Task OnGetAsync(string? keyword, string? district, SportType? sport)
    {
        DateOnly todayVn = GetVietnamToday();

        Browse = await _venueService.GetMarketplaceAsync(
            keyword,
            district,
            sport,
            todayVn,
            HttpContext.RequestAborted);
    }

    private static DateOnly GetVietnamToday()
    {
        foreach (string timeZoneId in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    }
}
