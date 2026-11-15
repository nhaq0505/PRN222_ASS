using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Web.Hubs;

namespace SplitTrip.Tests.Itinerary;

public sealed class DepartureReminderWorkerTests
{
    [Fact]
    public async Task ExecuteSingleTickAsync_TriggersOnlyForOngoingStopWithin30MinWindow_AndIsIdempotent()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var member = await harness.CreateUserAsync("member@splittrip.vn", "Thành Viên 2");

        // Simulated Vietnam local time: 2026-12-15 09:00 (which is 2026-12-15 02:00 UTC)
        var simulatedUtcNow = new DateTime(2026, 12, 15, 2, 0, 0, DateTimeKind.Utc);
        var travelDate = new DateOnly(2026, 12, 15);

        var trip = await harness.CreateSampleTripAsync(owner.Id, "Vũng Tàu", startDate: travelDate, days: 2);
        await harness.TripService.JoinByInviteCodeAsync(member.Id, trip.InviteCode);
        await harness.TripService.TransitionStatusAsync(trip.Id, owner.Id, TripStatus.Ongoing);

        var day1Id = trip.Days[0].Id;

        // +25 min (09:25 local) -> SHOULD trigger
        var dueStop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "Ăn sáng Bánh khọt Gốc Vú Sữa",
                "14 Nguyễn Trường Tộ",
                null,
                new TimeOnly(9, 25),
                new TimeOnly(10, 15),
                200_000m,
                null));

        // +45 min (09:45 local) -> outside 30-min window -> MUST NOT trigger
        var laterStop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "Tượng Chúa Kitô",
                null,
                null,
                new TimeOnly(9, 45),
                new TimeOnly(11, 0),
                0m,
                null));

        // -5 min (08:55 local) -> already in past -> MUST NOT trigger
        var pastStop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "Tập trung sảnh khách sạn",
                null,
                null,
                new TimeOnly(8, 55),
                new TimeOnly(9, 10),
                0m,
                null));

        var worker = harness.CreateReminderWorker();

        // First tick at 09:00 local
        var triggeredCount = await worker.ExecuteSingleTickAsync(simulatedUtcNow);
        Assert.Equal(1, triggeredCount);

        var ownerNotifications = await harness.NotificationService.GetRecentAsync(owner.Id);
        var memberNotifications = await harness.NotificationService.GetRecentAsync(member.Id);

        Assert.Contains(ownerNotifications, n =>
            n.Type == NotificationType.DepartureReminder
            && n.Title.Contains("Bánh khọt")
            && n.ActionUrl == $"/Trips/Itinerary?tripId={trip.Id}#item-{dueStop.Id}");

        Assert.Contains(memberNotifications, n =>
            n.Type == NotificationType.DepartureReminder
            && n.Title.Contains("Bánh khọt"));

        Assert.Contains(harness.FakeHubContext.FakeClients.GroupMessages, m =>
            m.GroupName == TripHub.UserGroupName(owner.Id) && m.Method == "ReceiveNotification");
        Assert.Contains(harness.FakeHubContext.FakeClients.GroupMessages, m =>
            m.GroupName == TripHub.UserGroupName(member.Id) && m.Method == "ReceiveNotification");
        Assert.Contains(harness.FakeHubContext.FakeClients.GroupMessages, m =>
            m.GroupName == TripHub.TripGroupName(trip.Id) && m.Method == "DepartureReminderTriggered");

        await using var verifyScope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(harness.ServiceProvider);
        var freshItineraryService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SplitTrip.Services.Itinerary.IItineraryService>(verifyScope.ServiceProvider);

        var scheduleAfterFirstTick = await freshItineraryService.GetScheduleAsync(trip.Id, owner.Id);
        var dayItems = scheduleAfterFirstTick.Days[0].Items;
        Assert.NotNull(dayItems.Single(i => i.Id == dueStop.Id).ReminderSentAtUtc);
        Assert.Null(dayItems.Single(i => i.Id == laterStop.Id).ReminderSentAtUtc);
        Assert.Null(dayItems.Single(i => i.Id == pastStop.Id).ReminderSentAtUtc);

        // Second tick 1 minute later -> idempotent (0 duplicates!)
        var repeatCount = await worker.ExecuteSingleTickAsync(simulatedUtcNow.AddMinutes(1));
        Assert.Equal(0, repeatCount);
    }

    [Fact]
    public async Task UpdateItemStartTime_ResetsReminderSentAtUtc_AllowingFreshReminderInNewWindow()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");

        var travelDate = new DateOnly(2026, 12, 20);
        var trip = await harness.CreateSampleTripAsync(owner.Id, "Đà Lạt", startDate: travelDate, days: 1);
        await harness.TripService.TransitionStatusAsync(trip.Id, owner.Id, TripStatus.Ongoing);

        var day1Id = trip.Days[0].Id;
        var stop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "Ga Đà Lạt",
                null,
                null,
                new TimeOnly(10, 20),
                new TimeOnly(11, 30),
                150_000m,
                null));

        var worker = harness.CreateReminderWorker();

        // 10:00 Vietnam local = 03:00 UTC -> triggers first reminder
        var firstTick = await worker.ExecuteSingleTickAsync(new DateTime(2026, 12, 20, 3, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, firstTick);

        await using var rescheduleScope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(harness.ServiceProvider);
        var scopedItinerary = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<SplitTrip.Services.Itinerary.IItineraryService>(rescheduleScope.ServiceProvider);

        var beforeReschedule = await scopedItinerary.GetScheduleAsync(trip.Id, owner.Id);
        Assert.NotNull(beforeReschedule.Days[0].Items.Single().ReminderSentAtUtc);

        // Reschedule stop to 14:20 local -> resets ReminderSentAtUtc
        var updated = await scopedItinerary.UpdateItemAsync(
            trip.Id,
            stop.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "Ga Đà Lạt (đổi giờ chiều)",
                null,
                null,
                new TimeOnly(14, 20),
                new TimeOnly(15, 30),
                150_000m,
                null));

        Assert.Null(updated.ReminderSentAtUtc);

        // 14:00 Vietnam local = 07:00 UTC -> triggers fresh reminder!
        var secondTick = await worker.ExecuteSingleTickAsync(new DateTime(2026, 12, 20, 7, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, secondTick);
    }
}
