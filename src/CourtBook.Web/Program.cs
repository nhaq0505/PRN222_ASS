using CourtBook.BusinessObjects.Enums;
using CourtBook.DataAccessObjects;
using CourtBook.Repositories;
using CourtBook.Services.Implementations;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;
using CourtBook.Web.Extensions;
using CourtBook.Web.Hubs;
using CourtBook.Web.Realtime;
using CourtBook.Web.Services;
using CourtBook.Web.Workers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=127.0.0.1,1433;Database=CourtBookDb;User Id=sa;Password=CourtBook@2026!;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";

builder.Services.AddDbContext<CourtBookDbContext>(options =>
    options.UseSqlServer(connectionString));

// Layer 2: DAOs
builder.Services.AddScoped<UserDao>();
builder.Services.AddScoped<VenueDao>();
builder.Services.AddScoped<CourtDao>();
builder.Services.AddScoped<BookingDao>();

// Layer 3: Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IVenueRepository, VenueRepository>();
builder.Services.AddScoped<ICourtRepository, CourtRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();

// Layer 4: Services & Realtime
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IVenueService, VenueService>();
builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IPlatformAdminService, PlatformAdminService>();
builder.Services.AddScoped<IPhotoStorageService, LocalPhotoStorageService>();
builder.Services.AddSingleton<ISlotRealtimeNotifier, SignalRSlotRealtimeNotifier>();

// Background cleanup for 2-minute slot holds
builder.Services.AddHostedService<ExpiredHoldCleanupWorker>();

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CourtBook.Auth";
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                int? userId = context.Principal?.GetUserId();
                UserRole? claimRole = context.Principal?.GetUserRole();
                IAuthService authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                AuthUserDto? activeUser = userId.HasValue
                    ? await authService.GetByIdAsync(userId.Value, context.HttpContext.RequestAborted)
                    : null;

                if (activeUser is null || activeUser.Role != claimRole)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            },
            OnRedirectToLogin = context =>
            {
                if (IsJsonOrAjaxRequest(context.Request))
                {
                    context.Response.Headers.Location = context.RedirectUri;
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (IsJsonOrAjaxRequest(context.Request))
                {
                    context.Response.Headers.Location = context.RedirectUri;
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

var app = builder.Build();

// Initialize database schema and seed demo users, courts, and sample bookings
using (IServiceScope scope = app.Services.CreateScope())
{
    CourtBookDbContext db = scope.ServiceProvider.GetRequiredService<CourtBookDbContext>();
    string? adminEmail = builder.Configuration["DefaultAdmin:Email"];
    string? adminPassword = builder.Configuration["DefaultAdmin:Password"];
    string? adminFullName = builder.Configuration["DefaultAdmin:FullName"];

    await DbInitializer.InitializeAsync(db, adminEmail, adminPassword, adminFullName);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapHub<CourtHub>("/hubs/court");

app.Run();

static bool IsJsonOrAjaxRequest(HttpRequest request)
    => string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
       || request.Headers.Accept.Any(a =>
           a is not null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));
