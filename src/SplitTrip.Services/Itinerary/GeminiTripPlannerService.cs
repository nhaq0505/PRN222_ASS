using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.Repositories.Itinerary;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Itinerary.Dtos;
using SplitTrip.Services.Trips;

namespace SplitTrip.Services.Itinerary;

public sealed class GeminiTripPlannerService : IAiTripPlannerService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ITripRepository _tripRepository;
    private readonly ITripDayRepository _tripDayRepository;
    private readonly IItineraryItemRepository _itineraryItemRepository;
    private readonly ITripService _tripService;
    private readonly IOptions<GeminiOptions> _geminiOptions;
    private readonly HttpClient _httpClient;

    public GeminiTripPlannerService(
        ITripRepository tripRepository,
        ITripDayRepository tripDayRepository,
        IItineraryItemRepository itineraryItemRepository,
        ITripService tripService,
        IOptions<GeminiOptions> geminiOptions,
        HttpClient? httpClient = null)
    {
        _tripRepository = tripRepository;
        _tripDayRepository = tripDayRepository;
        _itineraryItemRepository = itineraryItemRepository;
        _tripService = tripService;
        _geminiOptions = geminiOptions;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<AiTripPlanPreviewDto> PreviewPlanAsync(
        Guid tripId,
        Guid actorUserId,
        AiTripPlanRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        var dayCount = Math.Max(1, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        var travelStyle = string.IsNullOrWhiteSpace(request.TravelStyle) ? "Cân bằng" : request.TravelStyle.Trim();
        var budgetPerPerson = request.BudgetPerPersonOverride is > 0m
            ? decimal.Round(request.BudgetPerPersonOverride.Value, 0, MidpointRounding.AwayFromZero)
            : (trip.EstimatedBudgetPerPerson > 0m ? trip.EstimatedBudgetPerPerson : 2_000_000m);

        var activeMemberCount = Math.Max(1, trip.Members.Count(m => m.IsActive));
        var options = _geminiOptions.Value;
        var apiKey = (options.ApiKey ?? string.Empty).Trim();
        var model = string.IsNullOrWhiteSpace(options.Model) ? "gemini-2.5-flash" : options.Model.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var fallbackDays = BuildSmartFallbackDays(
                trip.Destination,
                trip.StartDate,
                dayCount,
                budgetPerPerson,
                travelStyle,
                request.PreferencesPrompt);

            return new AiTripPlanPreviewDto(
                trip.Id,
                trip.Destination,
                dayCount,
                budgetPerPerson,
                travelStyle,
                UsedFallback: true,
                SourceLabel: "Gợi ý thông minh theo điểm đến (Smart Fallback)",
                FallbackReason: "Chưa cấu hình Gemini:ApiKey — hệ thống tự động tạo lịch trình địa phương thông minh.",
                fallbackDays.Sum(d => d.Items.Sum(i => i.EstimatedCost)),
                fallbackDays);
        }

        try
        {
            var aiDays = await GenerateViaGeminiRestAsync(
                apiKey,
                model,
                trip,
                dayCount,
                budgetPerPerson,
                activeMemberCount,
                travelStyle,
                request.PreferencesPrompt,
                ct);

            return new AiTripPlanPreviewDto(
                trip.Id,
                trip.Destination,
                dayCount,
                budgetPerPerson,
                travelStyle,
                UsedFallback: false,
                SourceLabel: $"Gemini AI ({model})",
                FallbackReason: null,
                aiDays.Sum(d => d.Items.Sum(i => i.EstimatedCost)),
                aiDays);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!request.AllowFallbackOnApiError)
            {
                throw new InvalidOperationException(
                    $"Không thể tạo lịch trình từ Gemini AI: {ex.Message}",
                    ex);
            }

            var fallbackDays = BuildSmartFallbackDays(
                trip.Destination,
                trip.StartDate,
                dayCount,
                budgetPerPerson,
                travelStyle,
                request.PreferencesPrompt);

            return new AiTripPlanPreviewDto(
                trip.Id,
                trip.Destination,
                dayCount,
                budgetPerPerson,
                travelStyle,
                UsedFallback: true,
                SourceLabel: "Gợi ý thông minh theo điểm đến (Dự phòng tự động)",
                FallbackReason: "Dịch vụ Gemini tạm thời bận hoặc quá hạn mức — đã tự động chuyển sang bộ gợi ý thông minh.",
                fallbackDays.Sum(d => d.Items.Sum(i => i.EstimatedCost)),
                fallbackDays);
        }
    }

    public async Task<TripScheduleDto> ApplyPlanAsync(
        Guid tripId,
        Guid actorUserId,
        ApplyAiTripPlanRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await ValidateMutableTripAndMembershipAsync(tripId, actorUserId, ct);
        if (request.Days is null || request.Days.Count == 0)
        {
            throw new ArgumentException("Danh sách ngày gợi ý không được để trống.", nameof(request));
        }

        var existingDays = await EnsureDaysSeededAsync(trip, ct);

        if (request.ClearExistingAiItems)
        {
            var existingItems = await _itineraryItemRepository.GetByTripIdAsync(trip.Id, ct);
            foreach (var aiItem in existingItems.Where(i => i.IsAiSuggested))
            {
                await _itineraryItemRepository.DeleteAsync(aiItem, ct);
            }

            existingDays = await _tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
        }

        var daysByNumber = existingDays.ToDictionary(d => d.DayNumber);
        var updatedDays = new List<TripDay>();
        var itemsToInsert = new List<ItineraryItem>();
        var nowUtc = DateTime.UtcNow;

        foreach (var suggestedDay in request.Days.OrderBy(d => d.DayNumber))
        {
            if (!daysByNumber.TryGetValue(suggestedDay.DayNumber, out var targetDay))
            {
                continue;
            }

            var dayModified = false;
            if (!string.IsNullOrWhiteSpace(suggestedDay.Title))
            {
                var trimmedDayTitle = Truncate(suggestedDay.Title.Trim(), 200);
                if (string.IsNullOrWhiteSpace(targetDay.Title) || targetDay.Title == $"Ngày {targetDay.DayNumber}")
                {
                    targetDay.Title = trimmedDayTitle;
                    dayModified = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(suggestedDay.Notes) && string.IsNullOrWhiteSpace(targetDay.Notes))
            {
                targetDay.Notes = Truncate(suggestedDay.Notes.Trim(), 2000);
                dayModified = true;
            }

            if (dayModified)
            {
                updatedDays.Add(targetDay);
            }

            var nextSort = targetDay.Items.Count == 0
                ? 0
                : targetDay.Items.Max(i => i.SortOrder) + 1;

            foreach (var rawItem in suggestedDay.Items ?? [])
            {
                var title = Truncate((rawItem.Title ?? string.Empty).Trim(), 200);
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var (startTime, endTime) = ParseSafeTimeRange(rawItem.StartTime, rawItem.EndTime);
                var cost = Math.Max(0m, decimal.Round(rawItem.EstimatedCost, 0, MidpointRounding.AwayFromZero));

                itemsToInsert.Add(new ItineraryItem
                {
                    Id = Guid.NewGuid(),
                    TripDayId = targetDay.Id,
                    Title = title,
                    LocationName = NullIfWhiteSpace(rawItem.LocationName, 250),
                    Address = NullIfWhiteSpace(rawItem.Address, 500),
                    StartTime = startTime,
                    EndTime = endTime,
                    EstimatedCost = cost,
                    SortOrder = nextSort++,
                    Notes = NullIfWhiteSpace(rawItem.Notes, 2000),
                    IsAiSuggested = true,
                    ReminderSentAtUtc = null,
                    CreatedByUserId = actorUserId,
                    CreatedAtUtc = nowUtc
                });
            }
        }

        if (updatedDays.Count > 0)
        {
            await _tripDayRepository.UpdateRangeAsync(updatedDays, ct);
        }

        if (itemsToInsert.Count > 0)
        {
            await _itineraryItemRepository.AddRangeAsync(itemsToInsert, ct);
        }

        var reloadedDays = await _tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
        return ItineraryService.MapSchedule(trip, reloadedDays, actorUserId, isAdmin: false);
    }

    public async Task<TripScheduleDto> GenerateAndApplyPlanAsync(
        Guid tripId,
        Guid actorUserId,
        AiTripPlanRequestDto request,
        bool clearExistingAiItems = false,
        CancellationToken ct = default)
    {
        var preview = await PreviewPlanAsync(tripId, actorUserId, request, ct);
        return await ApplyPlanAsync(
            tripId,
            actorUserId,
            new ApplyAiTripPlanRequestDto(preview.Days, clearExistingAiItems),
            ct);
    }

    private async Task<IReadOnlyList<AiSuggestedDayDto>> GenerateViaGeminiRestAsync(
        string apiKey,
        string model,
        Trip trip,
        int dayCount,
        decimal budgetPerPerson,
        int activeMemberCount,
        string travelStyle,
        string? preferencesPrompt,
        CancellationToken ct)
    {
        var endpoint =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var prompt = BuildGeminiPrompt(trip, dayCount, budgetPerPerson, activeMemberCount, travelStyle, preferencesPrompt);
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.45,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    required = new[] { "days" },
                    properties = new
                    {
                        days = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                required = new[] { "dayNumber", "title", "items" },
                                properties = new
                                {
                                    dayNumber = new { type = "INTEGER" },
                                    title = new { type = "STRING" },
                                    notes = new { type = "STRING" },
                                    items = new
                                    {
                                        type = "ARRAY",
                                        items = new
                                        {
                                            type = "OBJECT",
                                            required = new[] { "title", "startTime", "endTime", "estimatedCost" },
                                            properties = new
                                            {
                                                title = new { type = "STRING" },
                                                locationName = new { type = "STRING" },
                                                address = new { type = "STRING" },
                                                startTime = new { type = "STRING" },
                                                endTime = new { type = "STRING" },
                                                estimatedCost = new { type = "NUMBER" },
                                                notes = new { type = "STRING" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini API trả về mã lỗi HTTP {(int)response.StatusCode}.");
        }

        var rawBody = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(rawBody);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Phản hồi Gemini không chứa candidates hợp lệ.");
        }

        var firstCandidate = candidates[0];
        if (!firstCandidate.TryGetProperty("content", out var contentEl)
            || !contentEl.TryGetProperty("parts", out var partsEl)
            || partsEl.ValueKind != JsonValueKind.Array
            || partsEl.GetArrayLength() == 0
            || !partsEl[0].TryGetProperty("text", out var textEl))
        {
            throw new InvalidOperationException("Phản hồi Gemini thiếu nội dung văn bản JSON.");
        }

        var jsonText = CleanMarkdownCodeFence(textEl.GetString() ?? string.Empty);
        var parsed = JsonSerializer.Deserialize<GeminiScheduleEnvelope>(jsonText, JsonOptions)
            ?? throw new InvalidOperationException("Không thể phân tích cấu trúc JSON lịch trình từ Gemini.");

        if (parsed.Days is null || parsed.Days.Count == 0)
        {
            throw new InvalidOperationException("Dữ liệu JSON từ Gemini không có ngày lịch trình nào.");
        }

        var resultDays = new List<AiSuggestedDayDto>(dayCount);
        for (var dayNum = 1; dayNum <= dayCount; dayNum++)
        {
            var date = trip.StartDate.AddDays(dayNum - 1);
            var matched = parsed.Days.FirstOrDefault(d => d.DayNumber == dayNum);
            if (matched is null || matched.Items is null || matched.Items.Count == 0)
            {
                var fallbackSingle = BuildSmartFallbackDays(trip.Destination, trip.StartDate, dayCount, budgetPerPerson, travelStyle, preferencesPrompt)
                    .First(d => d.DayNumber == dayNum);
                resultDays.Add(fallbackSingle);
                continue;
            }

            var sanitizedItems = new List<AiSuggestedItemDto>();
            foreach (var item in matched.Items)
            {
                var title = Truncate((item.Title ?? string.Empty).Trim(), 200);
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var (start, end) = ParseSafeTimeRange(item.StartTime, item.EndTime);
                var cost = Math.Max(0m, decimal.Round(item.EstimatedCost, 0, MidpointRounding.AwayFromZero));

                sanitizedItems.Add(new AiSuggestedItemDto(
                    title,
                    NullIfWhiteSpace(item.LocationName, 250),
                    NullIfWhiteSpace(item.Address, 500),
                    start?.ToString("HH:mm"),
                    end?.ToString("HH:mm"),
                    cost,
                    NullIfWhiteSpace(item.Notes, 2000)));
            }

            if (sanitizedItems.Count == 0)
            {
                throw new InvalidOperationException($"Ngày {dayNum} không có điểm dừng hợp lệ từ Gemini.");
            }

            var dayTitle = string.IsNullOrWhiteSpace(matched.Title)
                ? $"Ngày {dayNum}: Khám phá {trip.Destination}"
                : Truncate(matched.Title.Trim(), 200);

            resultDays.Add(new AiSuggestedDayDto(
                dayNum,
                date,
                dayTitle,
                NullIfWhiteSpace(matched.Notes, 2000),
                sanitizedItems));
        }

        return resultDays;
    }

    private static string BuildGeminiPrompt(
        Trip trip,
        int dayCount,
        decimal budgetPerPerson,
        int activeMemberCount,
        string travelStyle,
        string? preferencesPrompt)
    {
        var prefLine = string.IsNullOrWhiteSpace(preferencesPrompt)
            ? "Ưu tiên địa điểm nổi tiếng, ẩm thực địa phương đặc sắc, di chuyển hợp lý."
            : preferencesPrompt.Trim();

        return $$"""
            Bạn là chuyên gia lên lịch trình du lịch nhóm tại Việt Nam cho ứng dụng SplitTrip.
            Hãy tạo lịch trình chi tiết bằng tiếng Việt tự nhiên cho chuyến đi sau:
            - Điểm đến: {{trip.Destination}}
            - Tên chuyến đi: {{trip.Name}}
            - Thời gian: {{trip.StartDate:yyyy-MM-dd}} đến {{trip.EndDate:yyyy-MM-dd}} ({{dayCount}} ngày)
            - Số thành viên: {{activeMemberCount}} người
            - Ngân sách dự kiến mỗi người cho toàn chuyến: {{budgetPerPerson:0}} VND
            - Phong cách du lịch: {{travelStyle}}
            - Yêu cầu bổ sung của đoàn: {{prefLine}}

            Quy tắc bắt buộc:
            1. Trả về đúng {{dayCount}} ngày (dayNumber từ 1 đến {{dayCount}}), mỗi ngày từ 3 đến 4 điểm dừng hợp lý theo khung giờ sáng - trưa - chiều - tối.
            2. startTime và endTime dùng định dạng 24h "HH:mm" (ví dụ "08:00", "10:30"), và endTime luôn lớn hơn startTime.
            3. estimatedCost là chi phí ước tính cho CẢ ĐOÀN ({{activeMemberCount}} người) tại điểm dừng đó bằng số nguyên VND (>= 0, làm tròn hàng nghìn VND).
            4. Chỉ trả về JSON thuần đúng schema.
            """;
    }

    internal static IReadOnlyList<AiSuggestedDayDto> BuildSmartFallbackDays(
        string destination,
        DateOnly startDate,
        int dayCount,
        decimal budgetPerPerson,
        string travelStyle,
        string? preferencesPrompt)
    {
        var normalizedKey = NormalizeAsciiKey(destination);
        var template = ResolveDestinationTemplate(normalizedKey, destination);

        var styleMultiplier = NormalizeAsciiKey(travelStyle) switch
        {
            var s when s.Contains("tiet kiem") || s.Contains("budget") => 0.75m,
            var s when s.Contains("nghi duong") || s.Contains("thoai mai") || s.Contains("luxury") || s.Contains("cao cap") => 1.35m,
            _ => 1.0m
        };

        var dailyTarget = Math.Max(250_000m, budgetPerPerson / Math.Max(1, dayCount)) * styleMultiplier;
        var prefNote = string.IsNullOrWhiteSpace(preferencesPrompt)
            ? null
            : $"Lưu ý nhóm: {Truncate(preferencesPrompt.Trim(), 160)}";

        var result = new List<AiSuggestedDayDto>(dayCount);

        for (var dayIdx = 0; dayIdx < dayCount; dayIdx++)
        {
            var dayNum = dayIdx + 1;
            var date = startDate.AddDays(dayIdx);
            var dayBlueprint = template.Days[dayIdx % template.Days.Count];

            var rawSum = dayBlueprint.Stops.Sum(s => s.BaseCostVnd);
            var scale = rawSum > 0m ? dailyTarget / rawSum : 1m;
            scale = Math.Clamp(scale, 0.45m, 3.0m);

            var items = dayBlueprint.Stops
                .Select((stop, idx) =>
                {
                    var scaledCost = stop.BaseCostVnd <= 0m
                        ? 0m
                        : RoundToNearestThousand(stop.BaseCostVnd * scale);

                    var note = idx == 0 && prefNote is not null
                        ? $"{stop.Notes} ({prefNote})"
                        : stop.Notes;

                    return new AiSuggestedItemDto(
                        stop.Title,
                        stop.LocationName,
                        stop.Address,
                        stop.StartTime,
                        stop.EndTime,
                        scaledCost,
                        note);
                })
                .ToList();

            var dayTitle = dayIdx < template.Days.Count
                ? $"Ngày {dayNum}: {dayBlueprint.ThemeTitle}"
                : $"Ngày {dayNum}: Trải nghiệm mở rộng {template.DisplayName}";

            result.Add(new AiSuggestedDayDto(
                dayNum,
                date,
                dayTitle,
                dayBlueprint.DayNotes,
                items));
        }

        return result;
    }

    private static DestinationBlueprint ResolveDestinationTemplate(string key, string rawDestination)
    {
        if (key.Contains("da lat") || key.Contains("dalat") || key.Contains("lam dong"))
        {
            return new DestinationBlueprint("Đà Lạt", [
                new DayBlueprint("Săn sương phố núi & Quảng trường trung tâm", "Mang theo áo khoác mỏng buổi sáng và tối.", [
                    new("Ăn sáng Bánh căn Lệ & sữa đậu nành nóng", "Bánh căn Lệ Đà Lạt", "27/44 Yersin, Phường 10, Đà Lạt", "07:30", "08:45", 180_000m, "Đi sớm tránh xếp hàng đông cuối tuần."),
                    new("Check-in Quảng trường Lâm Viên & Hồ Xuân Hương", "Quảng trường Lâm Viên", "Đường Trần Quốc Toản, Phường 1, Đà Lạt", "09:00", "11:00", 60_000m, "Chụp ảnh nụ hoa Atiso và dạo bờ hồ."),
                    new("Ăn trưa Lẩu gà lá é Tao Ngộ", "Lẩu gà lá é Tao Ngộ", "5 Đường 3/4, Phường 3, Đà Lạt", "12:00", "13:30", 350_000m, "Đặc sản trứ danh hợp thời tiết se lạnh."),
                    new("Ngắm hoàng hôn tại Tiệm cà phê Túi Mơ To", "Tiệm cà phê Túi Mơ To", "Hẻm 31 Sào Nam, Phường 11, Đà Lạt", "16:00", "18:00", 220_000m, "View nhà lồng thung lũng lên đèn cực đẹp."),
                    new("Dạo Chợ đêm Đà Lạt & thưởng thức bánh tráng nướng", "Chợ đêm Đà Lạt", "Đường Nguyễn Thị Minh Khai, Phường 1, Đà Lạt", "19:30", "21:30", 200_000m, "Mua dâu tây tươi và quà len lưu niệm.")
                ]),
                new DayBlueprint("Săn mây Cầu Đất & Ga cổ Đông Dương", "Khởi hành sớm đón bình minh trên đồi chè.", [
                    new("Đón bình minh & săn mây Đồi chè Cầu Đất", "Đồi chè Cầu Đất Farm", "Thôn Trường Thọ, Xã Xuân Trường, Đà Lạt", "05:30", "08:30", 250_000m, "Thưởng thức cà phê Arabica giữa biển mây."),
                    new("Tham quan Ga Đà Lạt cổ & toa tàu gỗ", "Ga Đà Lạt", "1 Quang Trung, Phường 10, Đà Lạt", "09:30", "11:00", 150_000m, "Nhà ga cổ kính đẹp nhất Đông Dương."),
                    new("Thưởng thức Bánh ướt lòng gà Trang", "Quán Trang Đà Lạt", "15F Tăng Bạt Hổ, Phường 1, Đà Lạt", "12:00", "13:15", 240_000m, "Món ăn trưa nhẹ nhàng chuẩn vị Đà Lạt."),
                    new("Trải nghiệm xe trượt thác Datanla", "Khu du lịch Thác Datanla", "QL20 Đèo Prenn, Phường 3, Đà Lạt", "14:30", "17:00", 450_000m, "Máng trượt xuyên rừng thông dài nhất Đông Nam Á."),
                    new("Tiệc nướng BBQ Lẩu bò Ba Toa Nhà Gỗ", "Lẩu bò Quán Gỗ", "1/29 Hoàng Diệu, Phường 5, Đà Lạt", "18:30", "20:30", 480_000m, "Nên đặt bàn trước cho nhóm đông người.")
                ]),
                new DayBlueprint("Làng cổ tích thông reo & Mua đặc sản về phố", "Thư giãn nhẹ nhàng trước giờ trả phòng.", [
                    new("Phở thố Chu Gia & cà phê Tùng cổ điển", "Cà phê Tùng Khu Hòa Bình", "6 Khu Hòa Bình, Phường 1, Đà Lạt", "08:00", "09:45", 200_000m, "Không gian nhạc Trịnh hoài niệm giữa trung tâm."),
                    new("Dạo rừng thông Hồ Tuyền Lâm & Thiền viện Trúc Lâm", "Hồ Tuyền Lâm", "Phường 4, Đà Lạt", "10:15", "12:15", 150_000m, "Không khí trong lành, mặt hồ phẳng lặng."),
                    new("Mua đặc sản mứt, trà Atiso tại L'angfarm", "L'angfarm Chợ Đà Lạt", "6 Nguyễn Thị Minh Khai, Đà Lạt", "13:30", "15:00", 350_000m, "Đóng gói gọn gàng tiện chia quà cho nhóm.")
                ])
            ]);
        }

        if (key.Contains("da nang") || key.Contains("danang") || key.Contains("hoi an") || key.Contains("quang nam"))
        {
            return new DestinationBlueprint("Đà Nẵng - Hội An", [
                new DayBlueprint("Biển Mỹ Khê, Bán đảo Sơn Trà & Cầu Rồng", "Chuẩn bị kem chống nắng và đồ bơi biển.", [
                    new("Điểm tâm Mì Quảng Bà Mua đậm vị xứ Quảng", "Mì Quảng Bà Mua", "95A Nguyễn Tri Phương, Thanh Khê, Đà Nẵng", "08:00", "09:15", 180_000m, "Gọi mì quảng tôm thịt trứng kèm bánh tráng nướng."),
                    new("Viếng Chùa Linh Ứng - Bán đảo Sơn Trà", "Chùa Linh Ứng Bãi Bụt", "Hoàng Sa, Thọ Quang, Sơn Trà, Đà Nẵng", "09:45", "11:30", 50_000m, "Ngắm toàn cảnh vịnh biển Đà Nẵng từ trên cao."),
                    new("Tắm biển Mỹ Khê & dạo cát trắng", "Bãi biển Mỹ Khê", "Đường Võ Nguyên Giáp, Phước Mỹ, Đà Nẵng", "15:30", "17:30", 80_000m, "Một trong những bãi biển quyến rũ nhất hành tinh."),
                    new("Hải sản tươi sống Bé Mặn ven biển", "Hải sản Bé Mặn", "Lô 8 Võ Nguyên Giáp, Sơn Trà, Đà Nẵng", "18:30", "20:30", 650_000m, "Chọn hải sản tươi tại bể, minh bạch cân ký."),
                    new("Xem Cầu Rồng phun lửa & dạo bờ sông Hàn", "Cầu Rồng Đà Nẵng", "Đường Trần Hưng Đạo, Sơn Trà, Đà Nẵng", "20:45", "22:00", 120_000m, "Phun lửa & nước lúc 21:00 tối cuối tuần.")
                ]),
                new DayBlueprint("Bà Nà Hills Cầu Vàng & Phố cổ Hội An", "Ngày điểm nhấn kết hợp núi cao và phố đèn lồng.", [
                    new("Cáp treo Sun World Bà Nà Hills & Cầu Vàng", "Sun World Ba Na Hills", "Thôn An Sơn, Hòa Ninh, Hòa Vang, Đà Nẵng", "08:00", "13:30", 900_000m, "Check-in Cầu Vàng, Làng Pháp và buffet trưa."),
                    new("Tham quan danh thắng Ngũ Hành Sơn", "Ngũ Hành Sơn", "81 Huyền Trân Công Chúa, Ngũ Hành Sơn, Đà Nẵng", "14:30", "16:00", 120_000m, "Khám phá động Huyền Không và làng đá Non Nước."),
                    new("Dạo Phố cổ Hội An, thả hoa đăng sông Hoài & ăn Cao Lầu", "Phố cổ Hội An", "Phường Minh An, TP. Hội An", "17:00", "21:00", 420_000m, "Thưởng thức bánh mì Phượng, nước Mót và chụp ảnh đèn lồng.")
                ]),
                new DayBlueprint("Chợ Hàn đặc sản & Cà phê muối ven sông", "Mua sắm quà miền Trung trước giờ ra sân bay.", [
                    new("Bún chả cá Hờn & cà phê muối sông Hàn", "Bún chả cá 109 Nguyễn Chí Thanh", "109 Nguyễn Chí Thanh, Hải Châu, Đà Nẵng", "08:00", "09:30", 160_000m, "Nước dùng ngọt thanh từ cá tươi."),
                    new("Mua chả bò, mực rim me tại Chợ Hàn", "Chợ Hàn Đà Nẵng", "119 Trần Phú, Hải Châu, Đà Nẵng", "10:00", "11:45", 400_000m, "Đặc sản đóng gói sẵn mang lên máy bay.")
                ])
            ]);
        }

        if (key.Contains("phu quoc") || key.Contains("kien giang"))
        {
            return new DestinationBlueprint("Phú Quốc", [
                new DayBlueprint("Bắc Đảo sắc màu & Hoàng hôn biển ngọc", "Thời tiết nắng đẹp lý tưởng chụp ảnh biển.", [
                    new("Ăn sáng Bún quậy Kiến Xây trứ danh", "Bún quậy Kiến Xây", "28 Bạch Đằng, Dương Đông, Phú Quốc", "08:00", "09:15", 220_000m, "Tự pha chén nước chấm tắc ớt tươi đặc trưng."),
                    new("Khám phá Grand World - Thành phố không ngủ", "Grand World Phú Quốc", "Bãi Dài, Gành Dầu, Phú Quốc", "10:00", "13:30", 300_000m, "Check-in công trình Tre Huyền Thoại và dòng sông Venice."),
                    new("Ngắm hoàng hôn tại Sunset Sanato Beach Club", "Sunset Sanato", "Bãi Trường, Dương Tơ, Phú Quốc", "16:30", "18:30", 280_000m, "Khung giờ vàng chụp ảnh đẹp nhất đảo Ngọc."),
                    new("Khám phá ẩm thực Chợ đêm Phú Quốc", "Chợ đêm Phú Quốc", "54 Nguyễn Trãi, Dương Đông, Phú Quốc", "19:30", "21:30", 480_000m, "Thưởng thức nhum nướng mỡ hành và kem cuộn dừa.")
                ]),
                new DayBlueprint("Cáp treo Hòn Thơm vượt biển & Bãi Sao", "Mang kính bơi, nón rộng vành cho tour biển đảo.", [
                    new("Trải nghiệm Cáp treo 3 dây Hòn Thơm", "Ga An Thới Sun World", "Thị trấn An Thới, Phú Quốc", "08:30", "12:30", 750_000m, "Ngắm toàn cảnh quần đảo An Thới nước xanh ngọc bích."),
                    new("Tắm biển cát trắng mịn tại Bãi Sao", "Bãi Sao Phú Quốc", "Ấp Bãi Sao, An Thới, Phú Quốc", "13:30", "16:00", 200_000m, "Chụp ảnh hàng dừa nghiêng và chèo kayak."),
                    new("Gỏi cá trích & ghẹ hấp Làng chài Hàm Ninh", "Làng chài Hàm Ninh", "Xã Hàm Ninh, Phú Quốc", "17:30", "20:00", 600_000m, "Ghẹ Hàm Ninh thịt chắc ngọt tự nhiên.")
                ])
            ]);
        }

        if (key.Contains("ha giang") || key.Contains("dong van") || key.Contains("meo vac"))
        {
            return new DestinationBlueprint("Hà Giang", [
                new DayBlueprint("Cung đường Hạnh Phúc - Quản Bạ - Đồng Văn", "Kiểm tra kỹ phanh xe và áo ấm chống gió đèo.", [
                    new("Check-in Cột mốc Km0 & ăn phở chua Hà Giang", "Quảng trường 26/3 Hà Giang", "Đường Nguyễn Trãi, TP. Hà Giang", "07:00", "08:15", 150_000m, "Điểm khởi đầu huyền thoại của cung đường vùng cao."),
                    new("Ngắm Núi Đôi Cô Tiên tại Cổng trời Quản Bạ", "Cổng trời Quản Bạ", "QL4C, Thị trấn Tam Sơn, Quản Bạ", "09:45", "11:00", 60_000m, "Góc toàn cảnh thung lũng Tam Sơn hùng vĩ."),
                    new("Chinh phục Dốc Thẩm Mã & Dinh thự họ Vương", "Xã Sà Phìn, Đồng Văn", "QL4C Sà Phìn, Đồng Văn, Hà Giang", "14:00", "16:30", 140_000m, "Kiến trúc cổ gần 100 năm tuổi giữa cao nguyên đá."),
                    new("Lẩu gà đen & dạo Phố cổ Đồng Văn về đêm", "Phố cổ Đồng Văn", "Thị trấn Đồng Văn, Hà Giang", "18:30", "21:00", 380_000m, "Thưởng thức thắng dền nóng trong sương lạnh.")
                ]),
                new DayBlueprint("Cột cờ Lũng Cú, Đèo Mã Pí Lèng & Sông Nho Quế", "Đoạn đèo đẹp nhất tứ đại đỉnh đèo Việt Nam.", [
                    new("Chào cờ tại Cột cờ Quốc gia Lũng Cú", "Cột cờ Lũng Cú", "Xã Lũng Cú, Đồng Văn, Hà Giang", "08:00", "10:00", 120_000m, "Điểm cực Bắc thiêng liêng của Tổ quốc."),
                    new("Đi thuyền sông Nho Quế xuyên Hẻm vực Tu Sản", "Bến thuyền Tà Làng", "Pải Lủng, Mèo Vạc, Hà Giang", "11:00", "13:30", 280_000m, "Dòng sông xanh ngọc bích dưới vách đá dựng đứng."),
                    new("Ngắm cảnh trên đỉnh Đèo Mã Pí Lèng", "Đèo Mã Pí Lèng", "QL4C nối Đồng Văn - Mèo Vạc", "14:30", "16:30", 100_000m, "Dừng chân Panorama uống cà phê ngắm hẻm vực.")
                ])
            ]);
        }

        if (key.Contains("nha trang") || key.Contains("khanh hoa") || key.Contains("cam ranh"))
        {
            return new DestinationBlueprint("Nha Trang", [
                new DayBlueprint("Di sản Tháp Bà, Tắm bùn khoáng & Biển Trần Phú", "Kết hợp văn hóa Chăm Pa và thư giãn khoáng nóng.", [
                    new("Bún cá sứa Năm Beo chợ Đầm", "Bún cá Năm Beo", "B2 Chung cư Phan Bội Châu, Nha Trang", "07:30", "08:45", 160_000m, "Nước lèo cá cờ trong vắt ngọt thanh."),
                    new("Tham quan Tháp Bà Ponagar cổ kính", "Tháp Bà Ponagar", "Đường 2/4, Vĩnh Phước, Nha Trang", "09:15", "11:00", 120_000m, "Quần thể kiến trúc Chăm Pa hơn 1.000 năm tuổi."),
                    new("Tắm bùn khoáng nóng I-Resort Nha Trang", "I-Resort Nha Trang", "Tổ 19 Xuân Ngọc, Vĩnh Ngọc, Nha Trang", "14:00", "16:45", 450_000m, "Phục hồi năng lượng cực tốt cho cả nhóm."),
                    new("Tiệc hải sản bờ kè Thanh Sương", "Hải sản Thanh Sương", "21 Trần Phú, Vĩnh Nguyên, Nha Trang", "18:30", "20:45", 620_000m, "Tôm hùm, ốc hương nướng mọi tươi ngon giá hợp lý.")
                ]),
                new DayBlueprint("Vịnh biển san hô & VinWonders Hòn Tre", "Ngày vui chơi giải trí trọn vẹn trên đảo.", [
                    new("Khám phá Viện Hải dương học Nha Trang", "Viện Hải dương học", "1 Cầu Đá, Vĩnh Nguyên, Nha Trang", "08:30", "10:30", 120_000m, "Chiêm ngưỡng hơn 20.000 mẫu sinh vật biển."),
                    new("Vui chơi VinWonders Hòn Tre & xem Tata Show", "Đảo Hòn Tre", "Vĩnh Nguyên, TP. Nha Trang", "11:30", "20:30", 850_000m, "Công viên nước, tàu lượn và nhạc nước hoành tráng.")
                ])
            ]);
        }

        if (key.Contains("vung tau") || key.Contains("ba ria") || key.Contains("ho tram"))
        {
            return new DestinationBlueprint("Vũng Tàu", [
                new DayBlueprint("Gió biển Bãi Sau, Tượng Chúa Kitô & Hải Đăng", "Lịch trình gọn gàng giàu trải nghiệm ven biển.", [
                    new("Điểm tâm Bánh khọt Gốc Vú Sữa giòn rụm", "Bánh khọt Gốc Vú Sữa", "14 Nguyễn Trường Tộ, Phường 2, Vũng Tàu", "08:00", "09:15", 220_000m, "Bánh khọt tôm tươi ăn kèm rau sống và đu đủ bào."),
                    new("Chinh phục Tượng Chúa Kitô Vua Núi Nhỏ", "Núi Nhỏ Vũng Tàu", "Đường Thùy Vân, Phường 2, Vũng Tàu", "09:30", "11:15", 40_000m, "Lưu ý trang phục lịch sự khi lên bậc thang."),
                    new("Ngắm hoàng hôn tại Ngọn Hải Đăng & sữa chua Cô Tiên", "Hải Đăng Vũng Tàu", "Núi Nhỏ, Phường 2, Vũng Tàu", "16:00", "17:45", 120_000m, "Trứng lòng đào & sữa chua dẻo trứ danh trên đồi."),
                    new("Lẩu cá đuối Hoàng Minh Trương Công Định", "Lẩu cá đuối Hoàng Minh", "44 Trương Công Định, Phường 3, Vũng Tàu", "18:30", "20:30", 420_000m, "Nước lẩu măng chua cay nồng ăn kèm bún tươi.")
                ]),
                new DayBlueprint("Mũi Nghinh Phong & Cafe biển Bãi Trước", "Thư thả chụp ảnh cổng trời trước giờ về.", [
                    new("Check-in Cổng Trời Mũi Nghinh Phong", "Mũi Nghinh Phong", "1 Hạ Long, Phường 2, Vũng Tàu", "07:45", "09:15", 50_000m, "Phía trước là biển lớn, sau lưng là núi xanh."),
                    new("Cafe view biển Bãi Trước & mua bông lan trứng muối", "Gốc Cột Điện Vũng Tàu", "17B Nguyễn Trường Tộ, Phường 2, Vũng Tàu", "10:00", "11:45", 240_000m, "Quà đặc sản Vũng Tàu được yêu thích nhất.")
                ])
            ]);
        }

        if (key.Contains("sa pa") || key.Contains("sapa") || key.Contains("lao cai") || key.Contains("fansipan"))
        {
            return new DestinationBlueprint("Sa Pa", [
                new DayBlueprint("Bản Cát Cát mờ sương & Chợ đêm phố núi", "Mang giày thể thao bám tốt để đi bộ bản làng.", [
                    new("Ăn sáng Phở cốn sủi Ông Há ấm bụng", "Cốn sủi Ông Há", "436 Điện Biên Phủ, Thị xã Sa Pa", "08:00", "09:00", 160_000m, "Món ăn sáng đặc trưng chỉ có ở Sa Pa."),
                    new("Khám phá Bản Cát Cát & Thác Tiên Sa", "Bản Cát Cát", "Xã San Sả Hồ, Thị xã Sa Pa", "09:30", "12:30", 300_000m, "Tìm hiểu nghề dệt thổ cẩm người H'Mông giữa thung lũng."),
                    new("Check-in Nhà thờ Đá Sa Pa & Quảng trường", "Nhà thờ Đá Sa Pa", "Trung tâm Thị xã Sa Pa, Lào Cai", "16:00", "17:30", 80_000m, "Biểu tượng kiến trúc Pháp cổ giữa sương mù."),
                    new("Lẩu cá tầm cá hồi & đồ nướng Sa Pa", "Nhà hàng A Phủ Sa Pa", "15 Fansipan, Thị xã Sa Pa", "18:30", "20:45", 550_000m, "Cá tầm tươi nấu măng chua rau rừng Tây Bắc.")
                ]),
                new DayBlueprint("Chinh phục Nóc nhà Đông Dương Fansipan & Đèo Ô Quy Hồ", "Trải nghiệm tàu hỏa leo núi Mường Hoa & cáp treo.", [
                    new("Tàu hỏa Mường Hoa & Cáp treo đỉnh Fansipan 3.143m", "Sun World Fansipan Legend", "Đường Nguyễn Chí Thanh, Sa Pa", "08:00", "13:00", 950_000m, "Săn biển mây hùng vĩ trên đỉnh cao nhất Đông Dương."),
                    new("Ngắm hoàng hôn tại Đèo Ô Quy Hồ - Cổng Trời", "Đèo Ô Quy Hồ", "QL4D giáp Lai Châu - Lào Cai", "15:30", "17:30", 180_000m, "Một trong tứ đại đỉnh đèo huyền thoại miền Bắc.")
                ])
            ]);
        }

        var cleanDest = string.IsNullOrWhiteSpace(rawDestination) ? "Điểm đến" : rawDestination.Trim();
        return new DestinationBlueprint(cleanDest, [
            new DayBlueprint($"Khởi hành & Khám phá trung tâm {cleanDest}", $"Lịch trình cân bằng giúp cả nhóm làm quen nhịp độ tại {cleanDest}.", [
                new($"Điểm tâm đặc sản địa phương {cleanDest}", $"Khu ẩm thực trung tâm {cleanDest}", $"Trung tâm {cleanDest}", "08:00", "09:15", 180_000m, "Nạp năng lượng buổi sáng cùng các món đặc trưng vùng miền."),
                new($"Tham quan thắng cảnh biểu tượng tại {cleanDest}", $"Quảng trường & Di tích {cleanDest}", cleanDest, "09:45", "11:45", 200_000m, "Chụp ảnh lưu niệm tập thể toàn đoàn."),
                new($"Ăn trưa nhà hàng bản địa {cleanDest}", $"Nhà hàng đặc sản {cleanDest}", cleanDest, "12:15", "13:45", 380_000m, "Gọi mâm món chia sẻ cho cả nhóm."),
                new($"Cafe ngắm hoàng hôn & Check-in góc cao {cleanDest}", $"Tiệm Cafe Panorama {cleanDest}", cleanDest, "16:00", "17:45", 200_000m, "Nghỉ chân trò chuyện và ngắm cảnh chiều."),
                new($"Khám phá phố đêm & Ẩm thực đường phố {cleanDest}", $"Phố đi bộ / Chợ đêm {cleanDest}", cleanDest, "19:00", "21:15", 340_000m, "Trải nghiệm không khí nhộn nhịp buổi tối.")
            ]),
            new DayBlueprint($"Trải nghiệm thiên nhiên & Văn hóa {cleanDest}", "Ưu tiên trang phục thoải mái dễ vận động.", [
                new($"Tham quan khu sinh thái / danh thắng ngoại ô {cleanDest}", $"Khu du lịch sinh thái {cleanDest}", $"Ngoại ô {cleanDest}", "08:30", "11:30", 350_000m, "Khởi hành sớm để tận hưởng không khí mát mẻ."),
                new($"Thưởng thức bữa trưa dân dã địa phương", $"Làng ẩm thực {cleanDest}", cleanDest, "12:00", "13:30", 360_000m, "Hương vị truyền thống được người bản xứ yêu thích."),
                new($"Hoạt động trải nghiệm nhóm & Mua quà lưu niệm {cleanDest}", $"Chợ đặc sản {cleanDest}", cleanDest, "15:00", "17:30", 300_000m, "Chọn mua quà đặc sản đóng gói sẵn cho người thân.")
            ])
        ]);
    }

    private async Task<Trip> ValidateMutableTripAndMembershipAsync(
        Guid tripId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var trip = await _tripRepository.GetByIdWithDetailsAsync(tripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (!await _tripService.IsActiveMemberAsync(tripId, actorUserId, ct))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        if (trip.Status is TripStatus.Settling or TripStatus.Completed)
        {
            throw new InvalidOperationException(
                "Không thể áp dụng gợi ý lịch trình khi chuyến đi đang tất toán hoặc đã hoàn tất.");
        }

        return trip;
    }

    private async Task<IReadOnlyList<TripDay>> EnsureDaysSeededAsync(Trip trip, CancellationToken ct)
    {
        var existingDays = await _tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
        if (existingDays.Count > 0)
        {
            return existingDays;
        }

        var dayCount = Math.Max(1, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1);
        var seeded = Enumerable.Range(0, dayCount)
            .Select(offset => new TripDay
            {
                Id = Guid.NewGuid(),
                TripId = trip.Id,
                DayNumber = offset + 1,
                Date = trip.StartDate.AddDays(offset),
                Title = $"Ngày {offset + 1}"
            })
            .ToList();

        await _tripDayRepository.AddRangeAsync(seeded, ct);
        return await _tripDayRepository.GetByTripIdWithItemsAsync(trip.Id, ct);
    }

    private static (TimeOnly? Start, TimeOnly? End) ParseSafeTimeRange(string? rawStart, string? rawEnd)
    {
        var start = TryParseFlexibleTime(rawStart);
        var end = TryParseFlexibleTime(rawEnd);

        if (start.HasValue && end.HasValue && end.Value <= start.Value)
        {
            var adjustedMinutes = start.Value.Hour * 60 + start.Value.Minute + 90;
            end = adjustedMinutes < 24 * 60
                ? new TimeOnly(adjustedMinutes / 60, adjustedMinutes % 60)
                : null;
        }

        return (start, end);
    }

    private static TimeOnly? TryParseFlexibleTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        string[] formats = ["HH:mm", "H:mm", "HH:mm:ss", "h:mm tt", "hh:mm tt"];
        if (TimeOnly.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
            ? parsed
            : null;
    }

    private static string CleanMarkdownCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine > 0)
        {
            trimmed = trimmed[(firstNewLine + 1)..];
        }

        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }

    private static string NormalizeAsciiKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var formD = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(ch switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => ch
            });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static decimal RoundToNearestThousand(decimal amount)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        return Math.Max(10_000m, decimal.Round(amount / 1_000m, 0, MidpointRounding.AwayFromZero) * 1_000m);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? NullIfWhiteSpace(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Truncate(value.Trim(), maxLength);
    }

    private sealed class GeminiScheduleEnvelope
    {
        public List<GeminiDayItem>? Days { get; set; }
    }

    private sealed class GeminiDayItem
    {
        public int DayNumber { get; set; }
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public List<GeminiStopItem>? Items { get; set; }
    }

    private sealed class GeminiStopItem
    {
        public string? Title { get; set; }
        public string? LocationName { get; set; }
        public string? Address { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public decimal EstimatedCost { get; set; }
        public string? Notes { get; set; }
    }

    private sealed record DestinationBlueprint(string DisplayName, IReadOnlyList<DayBlueprint> Days);

    private sealed record DayBlueprint(string ThemeTitle, string DayNotes, IReadOnlyList<StopBlueprint> Stops);

    private sealed record StopBlueprint(
        string Title,
        string LocationName,
        string Address,
        string StartTime,
        string EndTime,
        decimal BaseCostVnd,
        string Notes);
}
