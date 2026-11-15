using Microsoft.AspNetCore.Authentication.Cookies;
using SplitTrip.Services;

namespace SplitTrip.Web.Extensions;

public static class FoundationModuleExtensions
{
    public static IServiceCollection AddFoundationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSplitTripFoundation(configuration);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "SplitTrip.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            });

        services.AddAuthorization();
        services.AddSignalR();
        services.AddHttpContextAccessor();

        return services;
    }
}
