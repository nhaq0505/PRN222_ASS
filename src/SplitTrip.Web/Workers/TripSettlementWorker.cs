using Microsoft.AspNetCore.SignalR;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Notifications;
using SplitTrip.Services.Settlements;
using SplitTrip.Web.Hubs;

namespace SplitTrip.Web.Workers;

public sealed class TripSettlementWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<TripHub> hubContext,
    ILogger<TripSettlementWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialStartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialStartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        await SafeRunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SafeRunCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful host shutdown
        }
    }

    public async Task ExecuteCycleOnceAsync(DateTime utcNow, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var result = await settlementService.RunAutomationCycleAsync(utcNow, ct);

        foreach (var settledTrip in result.AutoSettledTrips)
        {
            var tripGroup = TripHub.TripGroupName(settledTrip.TripId);

            await hubContext.Clients.Group(tripGroup).SendAsync(
                "TripStatusChanged",
                new
                {
                    tripId = settledTrip.TripId,
                    status = TripStatus.Settling.ToString(),
                    statusLabel = "Đang tất toán",
                    message = $"Chuyến đi \"{settledTrip.TripName}\" đã qua ngày kết thúc và tự động chuyển sang Tất toán VietQR."
                },
                ct);

            await hubContext.Clients.Group(tripGroup).SendAsync(
                "SettlementUpdated",
                new
                {
                    tripId = settledTrip.TripId,
                    status = "AutoSettling",
                    message = $"Đã tạo {settledTrip.GeneratedSettlementCount} giao dịch tất toán tối ưu Min-Cash-Flow."
                },
                ct);

            foreach (var (memberId, notification) in settledTrip.MemberNotifications)
            {
                await TripHub.DispatchUserNotificationAsync(
                    hubContext.Clients,
                    memberId,
                    notification,
                    notificationService,
                    ct);
            }

            logger.LogInformation(
                "Auto-transitioned Trip {TripId} ({TripName}) to Settling with {Count} simplified transfers.",
                settledTrip.TripId,
                settledTrip.TripName,
                settledTrip.GeneratedSettlementCount);
        }

        foreach (var nudge in result.OverdueNudges)
        {
            await TripHub.DispatchUserNotificationAsync(
                hubContext.Clients,
                nudge.TargetUserId,
                nudge.Notification,
                notificationService,
                ct);

            await hubContext.Clients.Group(TripHub.TripGroupName(nudge.TripId)).SendAsync(
                "SettlementUpdated",
                new
                {
                    tripId = nudge.TripId,
                    settlementId = nudge.SettlementId,
                    status = "OverdueNudged",
                    message = "Hệ thống đã gửi thông báo nhắc hẹn công nợ quá 24 giờ."
                },
                ct);

            logger.LogInformation(
                "Sent 24h DebtNudge for Settlement {SettlementId} to User {UserId}.",
                nudge.SettlementId,
                nudge.TargetUserId);
        }
    }

    private async Task SafeRunCycleAsync(CancellationToken ct)
    {
        try
        {
            await ExecuteCycleOnceAsync(DateTime.UtcNow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while running TripSettlementWorker background automation cycle.");
        }
    }
}
