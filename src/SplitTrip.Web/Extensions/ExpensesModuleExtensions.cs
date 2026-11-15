using SplitTrip.Services.Expenses;

namespace SplitTrip.Web.Extensions;

public static class ExpensesModuleExtensions
{
    public static IServiceCollection AddExpensesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSplitTripExpenses();

        services.AddHttpClient<IAiReceiptScannerService, GeminiReceiptScannerService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });

        return services;
    }
}
