using CourtBook.DataAccessObjects;
using CourtBook.Repositories;
using CourtBook.Services.Implementations;
using CourtBook.Services.Interfaces;
using CourtBook.Web.Hubs;
using CourtBook.Web.Realtime;
using CourtBook.Web.Services;
using CourtBook.Web.Workers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

string? configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

string connectionString = configuredConnectionString ??
    $"Data Source={Path.Combine(builder.Environment.ContentRootPath, "Data", "courtbook.db")}";

var useSqlite = builder.Configuration.GetValue<bool>("UseSqlite")
    || configuredConnectionString?.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) == true
    || configuredConnectionString is null;

if (useSqlite)
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "courtbook.db");
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
    connectionString = $"Data Source={dbPath}";
    builder.Services.AddDbContext<CourtBookDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    builder.Services.AddDbContext<CourtBookDbContext>(options =>
        options.UseSqlServer(connectionString));
}

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
