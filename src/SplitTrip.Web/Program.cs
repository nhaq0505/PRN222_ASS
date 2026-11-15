using SplitTrip.Services;
using SplitTrip.Web.Extensions;
using SplitTrip.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddFoundationModule(builder.Configuration);
builder.Services.AddItineraryModule(builder.Configuration);
builder.Services.AddExpensesModule(builder.Configuration);
builder.Services.AddSettlementModule(builder.Configuration);

var app = builder.Build();

await app.Services.MigrateSplitTripDatabaseAsync();
await app.Services.SeedSplitTripDemoDataAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHub<TripHub>("/hubs/trip");
app.MapHub<TripHub>("/tripHub");

app.Run();

public partial class Program;
