using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;
using CourtBook.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.Web.Pages.Profile;

public class FavoritesModel : PageModel
{
    private readonly CourtBookDbContext _db;

    public FavoritesModel(CourtBookDbContext db)
    {
        _db = db;
    }

    public List<FavoriteVenue> Favorites { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Favorites" });
        }

        Favorites = await _db.FavoriteVenues
            .Where(f => f.UserId == userId.Value)
            .Include(f => f.Venue)
            .OrderByDescending(f => f.CreatedAtUtc)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int favoriteId)
    {
        int? userId = User.GetUserId();
        if (userId is null)
        {
            return RedirectToPage("/Account/Login", new { ReturnUrl = "/Profile/Favorites" });
        }

        FavoriteVenue? favorite = await _db.FavoriteVenues
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId.Value);

        if (favorite is not null)
        {
            _db.FavoriteVenues.Remove(favorite);
            await _db.SaveChangesAsync();
            TempData["ToastSuccess"] = "Đã xóa sân yêu thích.";
        }

        return RedirectToPage();
    }
}
