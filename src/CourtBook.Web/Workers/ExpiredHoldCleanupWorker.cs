using CourtBook.Services.Interfaces;

namespace CourtBook.Web.Workers;

/// <summary>
/// Periodically releases expired temporary slot holds and broadcasts realtime resets.
/// </summary>
public class ExpiredHoldCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredHoldCleanupWorker> _logger;

    public ExpiredHoldCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredHoldCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IBookingService bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                int releasedCount = await bookingService.ReleaseExpiredHoldsAsync(stoppingToken);

                if (releasedCount > 0)
                {
                    _logger.LogInformation("Released {Count} expired slot hold(s).", releasedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while cleaning up expired slot holds.");
            }
        }
    }
}
