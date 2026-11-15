using Microsoft.AspNetCore.SignalR;
using SplitTrip.Services.Itinerary;
using SplitTrip.Services.Notifications;
using SplitTrip.Web.Hubs;

namespace SplitTrip.Web.Workers;

public sealed class DepartureReminderWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<TripHub> hubContext,
    ILogger<DepartureReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialStartupDelay = TimeSpan.FromSeconds(5);

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

        await SafeRunTickAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeRunTickAsync(stoppingToken);
        }
    }

    public async Task<int> ExecuteSingleTickAsync(
        DateTime? utcNowOverride = null,
        CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var itineraryService = scope.ServiceProvider.GetRequiredService<IItineraryService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var dispatches = await itineraryService.ProcessUpcomingDepartureRemindersAsync(utcNowOverride, ct);
        if (dispatches.Count == 0)
        {
            return 0;
        }

        foreach (var dispatch in dispatches)
        {
            await hubContext.Clients.Group(TripHub.TripGroupName(dispatch.TripId))
                .SendAsync(
                    "DepartureReminderTriggered",
                    new
                    {
                        tripId = dispatch.TripId,
                        tripName = dispatch.TripName,
                        itemId = dispatch.ItineraryItemId,
                        title = dispatch.ItemTitle,
                        locationName = dispatch.LocationName,
                        startTime = dispatch.StartTimeFormatted,
                        minutesUntilStart = dispatch.MinutesUntilStart,
                        sentAtUtc = dispatch.SentAtUtc
                    },
                    ct);

            foreach (var notif in dispatch.UserNotifications)
            {
                await hubContext.Clients.Group(TripHub.UserGroupName(notif.UserId))
                    .SendAsync(
                        "ReceiveNotification",
                        new
                        {
                            id = notif.NotificationId,
                            tripId = notif.TripId,
                            type = notif.Type.ToString(),
                            title = notif.Title,
                            message = notif.Message,
                            actionUrl = notif.ActionUrl,
                            createdAtUtc = notif.CreatedAtUtc
                        },
                        ct);

                var unreadCount = await notificationService.GetUnreadCountAsync(notif.UserId, ct);
                await hubContext.Clients.Group(TripHub.UserGroupName(notif.UserId))
                    .SendAsync("NotificationBadgeUpdated", unreadCount, ct);
            }
        }

        return dispatches.Count;
    }

    private async Task SafeRunTickAsync(CancellationToken ct)
    {
        try
        {
            var count = await ExecuteSingleTickAsync(utcNowOverride: null, ct);
            if (count > 0)
            {
                logger.LogInformation(
                    "DepartureReminderWorker đã gửi nhắc nhở khởi hành cho {Count} điểm dừng sắp tới.",
                    count);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lỗi tạm thời khi kiểm tra nhắc nhở khởi hành lịch trình.");
        }
    }
}
