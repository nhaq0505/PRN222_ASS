using SplitTrip.BusinessObjects.Enums;
using SplitTrip.Services.Itinerary.Dtos;

namespace SplitTrip.Tests.Itinerary;

public sealed class ItineraryServiceTests
{
    [Fact]
    public async Task AddAndUpdateItem_ValidatesTimeRangeNegativeCostAndWholeVndRounding()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Nguyễn Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id);
        var day1Id = trip.Days[0].Id;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.ItineraryService.AddItemAsync(
                trip.Id,
                owner.Id,
                new UpsertItineraryItemRequest(
                    day1Id,
                    "Ăn sáng",
                    "Quán Phở",
                    null,
                    new TimeOnly(10, 0),
                    new TimeOnly(9, 0),
                    50_000m,
                    null)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.ItineraryService.AddItemAsync(
                trip.Id,
                owner.Id,
                new UpsertItineraryItemRequest(
                    day1Id,
                    "Điểm dừng âm",
                    null,
                    null,
                    null,
                    null,
                    -1_000m,
                    null)));

        var created = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(
                day1Id,
                "  Cà phê Túi Mơ To  ",
                "Đà Lạt",
                "Hẻm 31 Sào Nam",
                new TimeOnly(16, 0),
                new TimeOnly(18, 0),
                125_499.6m,
                "Ngắm hoàng hôn"));

        Assert.Equal("Cà phê Túi Mơ To", created.Title);
        Assert.Equal(125_500m, created.EstimatedCost);
        Assert.Equal(0, created.SortOrder);
    }

    [Fact]
    public async Task Mutations_BlockNonMembersAndSettlingOrCompletedTrips()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var outsider = await harness.CreateUserAsync("outsider@splittrip.vn", "Người Ngoài");
        var trip = await harness.CreateSampleTripAsync(owner.Id);
        var day1Id = trip.Days[0].Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.ItineraryService.AddItemAsync(
                trip.Id,
                outsider.Id,
                new UpsertItineraryItemRequest(day1Id, "Lén thêm", null, null, null, null, 0m, null)));

        var stop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(day1Id, "Dạo chợ đêm", null, null, null, null, 100_000m, null));

        await harness.TripService.TransitionStatusAsync(trip.Id, owner.Id, TripStatus.Ongoing);
        await harness.TripService.TransitionStatusAsync(trip.Id, owner.Id, TripStatus.Settling);

        var settlingSchedule = await harness.ItineraryService.GetScheduleAsync(trip.Id, owner.Id);
        Assert.False(settlingSchedule.CanEdit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ItineraryService.AddItemAsync(
                trip.Id,
                owner.Id,
                new UpsertItineraryItemRequest(day1Id, "Không được thêm", null, null, null, null, 50_000m, null)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ItineraryService.ToggleVoteAsync(trip.Id, stop.Id, owner.Id, VoteType.Upvote));

        await harness.TripService.TransitionStatusAsync(trip.Id, owner.Id, TripStatus.Completed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ItineraryService.DeleteItemAsync(trip.Id, stop.Id, owner.Id));
    }

    [Fact]
    public async Task ReorderItemsAsync_WithinDayAndAcrossDays_NormalizesSortOrderAndResetsReminderTimestamp()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id, days: 2);
        var day1Id = trip.Days[0].Id;
        var day2Id = trip.Days[1].Id;

        var itemA = await harness.ItineraryService.AddItemAsync(
            trip.Id, owner.Id, new UpsertItineraryItemRequest(day1Id, "Điểm A", null, null, new TimeOnly(8, 0), new TimeOnly(9, 0), 0m, null));
        var itemB = await harness.ItineraryService.AddItemAsync(
            trip.Id, owner.Id, new UpsertItineraryItemRequest(day1Id, "Điểm B", null, null, new TimeOnly(9, 30), new TimeOnly(11, 0), 0m, null));
        var itemC = await harness.ItineraryService.AddItemAsync(
            trip.Id, owner.Id, new UpsertItineraryItemRequest(day1Id, "Điểm C", null, null, new TimeOnly(14, 0), new TimeOnly(15, 0), 0m, null));
        var itemD = await harness.ItineraryService.AddItemAsync(
            trip.Id, owner.Id, new UpsertItineraryItemRequest(day2Id, "Điểm D", null, null, new TimeOnly(8, 30), new TimeOnly(10, 0), 0m, null));

        // Simulate itemB having already sent a reminder on Day 1
        var dbItemB = await harness.DbContext.ItineraryItems.FindAsync(itemB.Id);
        Assert.NotNull(dbItemB);
        dbItemB!.ReminderSentAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await harness.DbContext.SaveChangesAsync();

        // Within-day reorder on Day 1: [C, A, B]
        var withinDay = await harness.ItineraryService.ReorderItemsAsync(
            trip.Id,
            day1Id,
            owner.Id,
            [itemC.Id, itemA.Id, itemB.Id]);

        var reorderedDay1 = withinDay.Days.Single(d => d.Id == day1Id);
        Assert.Equal([itemC.Id, itemA.Id, itemB.Id], reorderedDay1.Items.Select(i => i.Id).ToArray());
        Assert.Equal([0, 1, 2], reorderedDay1.Items.Select(i => i.SortOrder).ToArray());

        // Cross-day drag: Move itemB from Day 1 into Day 2 before itemD -> Day 2 becomes [B, D], Day 1 becomes [C, A] with SortOrder [0, 1]
        var acrossDays = await harness.ItineraryService.ReorderItemsAsync(
            trip.Id,
            day2Id,
            owner.Id,
            [itemB.Id, itemD.Id]);

        var updatedDay1 = acrossDays.Days.Single(d => d.Id == day1Id);
        var updatedDay2 = acrossDays.Days.Single(d => d.Id == day2Id);

        Assert.Equal([itemC.Id, itemA.Id], updatedDay1.Items.Select(i => i.Id).ToArray());
        Assert.Equal([0, 1], updatedDay1.Items.Select(i => i.SortOrder).ToArray());

        Assert.Equal([itemB.Id, itemD.Id], updatedDay2.Items.Select(i => i.Id).ToArray());
        Assert.Equal([0, 1], updatedDay2.Items.Select(i => i.SortOrder).ToArray());

        var movedItemB = updatedDay2.Items.Single(i => i.Id == itemB.Id);
        Assert.Null(movedItemB.ReminderSentAtUtc);
    }

    [Fact]
    public async Task ToggleVoteAsync_SupportsInsertToggleOffAndFlipBetweenUpvoteAndDownvote()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var member2 = await harness.CreateUserAsync("m2@splittrip.vn", "Thành Viên 2");
        var trip = await harness.CreateSampleTripAsync(owner.Id);
        await harness.TripService.JoinByInviteCodeAsync(member2.Id, trip.InviteCode);

        var stop = await harness.ItineraryService.AddItemAsync(
            trip.Id,
            owner.Id,
            new UpsertItineraryItemRequest(trip.Days[0].Id, "Lẩu gà lá é", null, null, null, null, 350_000m, null));

        // 1. Owner upvotes -> +1
        var v1 = await harness.ItineraryService.ToggleVoteAsync(trip.Id, stop.Id, owner.Id, VoteType.Upvote);
        Assert.Equal(1, v1.Upvotes);
        Assert.Equal(0, v1.Downvotes);
        Assert.Equal(1, v1.NetScore);
        Assert.Equal(VoteType.Upvote, v1.CurrentUserVote);

        // 2. Member 2 upvotes -> +2
        var v2 = await harness.ItineraryService.ToggleVoteAsync(trip.Id, stop.Id, member2.Id, VoteType.Upvote);
        Assert.Equal(2, v2.Upvotes);
        Assert.Equal(2, v2.NetScore);

        // 3. Member 2 flips to Downvote -> 1 up, 1 down, net 0
        var v3 = await harness.ItineraryService.ToggleVoteAsync(trip.Id, stop.Id, member2.Id, VoteType.Downvote);
        Assert.Equal(1, v3.Upvotes);
        Assert.Equal(1, v3.Downvotes);
        Assert.Equal(0, v3.NetScore);
        Assert.Equal(VoteType.Downvote, v3.CurrentUserVote);

        // 4. Member 2 clicks Downvote again -> toggles off -> 1 up, 0 down, net +1
        var v4 = await harness.ItineraryService.ToggleVoteAsync(trip.Id, stop.Id, member2.Id, VoteType.Downvote);
        Assert.Equal(1, v4.Upvotes);
        Assert.Equal(0, v4.Downvotes);
        Assert.Equal(1, v4.NetScore);
        Assert.Null(v4.CurrentUserVote);
    }

    [Fact]
    public async Task DeleteItemAsync_RemovesStopAndRenumbersRemainingItems()
    {
        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id, days: 1);
        var dayId = trip.Days[0].Id;

        var s1 = await harness.ItineraryService.AddItemAsync(trip.Id, owner.Id, new UpsertItineraryItemRequest(dayId, "Stop 1", null, null, null, null, 0m, null));
        var s2 = await harness.ItineraryService.AddItemAsync(trip.Id, owner.Id, new UpsertItineraryItemRequest(dayId, "Stop 2", null, null, null, null, 0m, null));
        var s3 = await harness.ItineraryService.AddItemAsync(trip.Id, owner.Id, new UpsertItineraryItemRequest(dayId, "Stop 3", null, null, null, null, 0m, null));

        await harness.ItineraryService.DeleteItemAsync(trip.Id, s2.Id, owner.Id);

        var schedule = await harness.ItineraryService.GetScheduleAsync(trip.Id, owner.Id);
        var items = schedule.Days[0].Items;
        Assert.Equal([s1.Id, s3.Id], items.Select(i => i.Id).ToArray());
        Assert.Equal([0, 1], items.Select(i => i.SortOrder).ToArray());
    }
}
