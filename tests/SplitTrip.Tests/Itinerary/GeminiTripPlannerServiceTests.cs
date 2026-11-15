using System.Net;
using System.Text;
using SplitTrip.Services.Itinerary.Dtos;

namespace SplitTrip.Tests.Itinerary;

public sealed class GeminiTripPlannerServiceTests
{
    [Theory]
    [InlineData("Đà Lạt")]
    [InlineData("Da Nang")]
    [InlineData("Phú Quốc")]
    [InlineData("Hà Giang")]
    [InlineData("Sa Pa")]
    [InlineData("Quy Nhơn")]
    public async Task PreviewAndApplyPlan_WhenApiKeyEmpty_UsesCuratedDestinationFallbackAndMarksIsAiSuggested(string destination)
    {
        await using var harness = new ItineraryTestHarness(apiKey: string.Empty);
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id, destination: destination, days: 3, budgetPerPerson: 3_000_000m);

        var planner = harness.CreateAiPlanner(apiKey: string.Empty);
        var preview = await planner.PreviewPlanAsync(
            trip.Id,
            owner.Id,
            new AiTripPlanRequestDto("Cân bằng", 3_000_000m, "Thích ăn đặc sản địa phương"));

        Assert.True(preview.UsedFallback);
        Assert.Equal(3, preview.Days.Count);
        Assert.All(preview.Days, day =>
        {
            Assert.NotEmpty(day.Items);
            Assert.All(day.Items, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Title));
                Assert.True(item.EstimatedCost >= 0m);
                Assert.Equal(decimal.Truncate(item.EstimatedCost), item.EstimatedCost);
            });
        });

        var schedule = await planner.ApplyPlanAsync(
            trip.Id,
            owner.Id,
            new ApplyAiTripPlanRequestDto(preview.Days, ClearExistingAiItems: false));

        Assert.True(schedule.TotalItemsCount >= 6);
        Assert.Equal(schedule.TotalItemsCount, schedule.AiSuggestedItemsCount);
        Assert.All(schedule.Days.SelectMany(d => d.Items), item => Assert.True(item.IsAiSuggested));
    }

    [Fact]
    public async Task PreviewAndApplyPlan_WithMockGeminiRestResponse_ParsesStructuredJsonRoundsVndAndAppliesToTripDays()
    {
        const string geminiResponseJson = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      {
                        "text": "{\"days\":[{\"dayNumber\":1,\"title\":\"Ngày 1: Phố núi sương mù\",\"notes\":\"Áo ấm\",\"items\":[{\"title\":\"Phở thố Chu Gia\",\"locationName\":\"Trung tâm Đà Lạt\",\"address\":\"1/29 Phan Đình Phùng\",\"startTime\":\"07:30\",\"endTime\":\"08:45\",\"estimatedCost\":185000.4,\"notes\":\"Đi sớm\"}]},{\"dayNumber\":2,\"title\":\"Ngày 2: Săn mây\",\"notes\":\"\",\"items\":[{\"title\":\"Đồi chè Cầu Đất\",\"locationName\":\"Xuân Trường\",\"address\":\"Cầu Đất\",\"startTime\":\"05:30\",\"endTime\":\"08:00\",\"estimatedCost\":250000,\"notes\":\"Đón bình minh\"}]}]}"
                      }
                    ]
                  }
                }
              ]
            }
            """;

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiResponseJson, Encoding.UTF8, "application/json")
        });

        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id, destination: "Đà Lạt", days: 2);

        var planner = harness.CreateAiPlanner(apiKey: "test-gemini-key", httpClient: new HttpClient(handler));
        var preview = await planner.PreviewPlanAsync(
            trip.Id,
            owner.Id,
            new AiTripPlanRequestDto("Cân bằng", 2_000_000m, null, AllowFallbackOnApiError: false));

        Assert.False(preview.UsedFallback);
        Assert.Equal(2, preview.Days.Count);
        Assert.Equal(185_000m, preview.Days[0].Items[0].EstimatedCost);

        var applied = await planner.ApplyPlanAsync(
            trip.Id,
            owner.Id,
            new ApplyAiTripPlanRequestDto(preview.Days));

        Assert.Equal(2, applied.TotalItemsCount);
        Assert.All(applied.Days.SelectMany(d => d.Items), i => Assert.True(i.IsAiSuggested));
    }

    [Fact]
    public async Task PreviewPlan_OnHttp429Error_FallsBackGracefullyOrThrowsWhenStrict()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await using var harness = new ItineraryTestHarness();
        var owner = await harness.CreateUserAsync("owner@splittrip.vn", "Trưởng Đoàn");
        var trip = await harness.CreateSampleTripAsync(owner.Id, destination: "Nha Trang", days: 2);

        var planner = harness.CreateAiPlanner(apiKey: "test-gemini-key", httpClient: new HttpClient(handler));

        // Strict mode throws actionable Vietnamese error
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            planner.PreviewPlanAsync(
                trip.Id,
                owner.Id,
                new AiTripPlanRequestDto("Cân bằng", 2_000_000m, null, AllowFallbackOnApiError: false)));

        // Default demo mode seamlessly falls back to curated Nha Trang template
        var fallbackPreview = await planner.PreviewPlanAsync(
            trip.Id,
            owner.Id,
            new AiTripPlanRequestDto("Cân bằng", 2_000_000m, null, AllowFallbackOnApiError: true));

        Assert.True(fallbackPreview.UsedFallback);
        Assert.Equal(2, fallbackPreview.Days.Count);
        Assert.Contains(fallbackPreview.Days.SelectMany(d => d.Items), i => i.Title.Contains("Ponagar") || i.Title.Contains("Nha Trang"));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
