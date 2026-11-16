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

string provider = builder.Configuration["DatabaseProvider"] ?? "Auto";
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=127.0.0.1,1433;Database=CourtBookDb;User Id=sa;Password=CourtBook@2026!;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
string sqliteConnectionString = builder.Configuration.GetConnectionString("SqliteConnection")
    ?? "Data Source=CourtBook.db";

bool useSqlServer = false;
if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    useSqlServer = true;
}
else if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    useSqlServer = false;
}
else
{
    useSqlServer = CanConnectToSqlServer(connectionString);
    if (!useSqlServer)
    {
        Console.WriteLine("[CourtBook] SQL Server 1433 không phản hồi. Tự động chuyển sang SQLite (CourtBook.db) để chạy ngay.");
    }
}

if (useSqlServer)
{
    Console.WriteLine("[CourtBook] Sử dụng cơ sở dữ liệu: Microsoft SQL Server");
    builder.Services.AddDbContext<CourtBookDbContext>(options =>
        options.UseSqlServer(connectionString));
}
else
{
    Console.WriteLine("[CourtBook] Sử dụng cơ sở dữ liệu: SQLite (" + sqliteConnectionString + ")");
    builder.Services.AddDbContext<CourtBookDbContext>(options =>
        options.UseSqlite(sqliteConnectionString));
}

// Layer 2: DAOs
builder.Services.AddScoped<UserDao>();
builder.Services.AddScoped<VenueDao>();
builder.Services.AddScoped<CourtDao>();
builder.Services.AddScoped<BookingDao>();
builder.Services.AddScoped<VenueReviewDao>();

// Layer 3: Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IVenueRepository, VenueRepository>();
builder.Services.AddScoped<ICourtRepository, CourtRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IVenueReviewRepository, VenueReviewRepository>();

// Layer 4: Services & Realtime
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IVenueService, VenueService>();
builder.Services.AddScoped<ICourtService, CourtService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IVenueReviewService, VenueReviewService>();
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

static bool CanConnectToSqlServer(string connectionString)
{
    try
    {
        var scsb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        string host = scsb.DataSource;
        int port = 1433;
        if (host.Contains(','))
        {
            var parts = host.Split(',');
            host = parts[0].Trim();
            _ = int.TryParse(parts[1].Trim(), out port);
        }
        else if (host.Contains(':'))
        {
            var parts = host.Split(':');
            host = parts[0].Trim();
            _ = int.TryParse(parts[1].Trim(), out port);
        }

        using var client = new System.Net.Sockets.TcpClient();
        var task = client.ConnectAsync(host, port);
        if (!task.Wait(TimeSpan.FromMilliseconds(500)))
        {
            return false;
        }
        return client.Connected;
    }
    catch
    {
        return false;
    }
}
