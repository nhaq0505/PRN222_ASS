using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SplitTrip.Web.Pages.Account;

public sealed class AccessDeniedModel : PageModel
{
    public void OnGet()
    {
        HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}
