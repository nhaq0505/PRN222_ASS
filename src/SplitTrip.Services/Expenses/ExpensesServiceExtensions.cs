using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SplitTrip.DataAccessObjects.Daos.Expenses;
using SplitTrip.Repositories.Expenses;

namespace SplitTrip.Services.Expenses;

public static class ExpensesServiceExtensions
{
    public static IServiceCollection AddSplitTripExpenses(this IServiceCollection services)
    {
        services.AddScoped<ExpenseDao>();
        services.AddScoped<ReceiptDao>();

        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IReceiptRepository, ReceiptRepository>();

        services.AddScoped<IExpenseService, ExpenseService>();

        services.TryAddSingleton<HttpClient>();
        services.AddScoped<IAiReceiptScannerService, GeminiReceiptScannerService>();

        return services;
    }
}
