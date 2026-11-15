using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SplitTrip.BusinessObjects.Entities;
using SplitTrip.BusinessObjects.Enums;
using SplitTrip.BusinessObjects.Options;
using SplitTrip.Repositories.Expenses;
using SplitTrip.Repositories.Trips;
using SplitTrip.Services.Expenses.Dtos;

namespace SplitTrip.Services.Expenses;

public sealed class GeminiReceiptScannerService(
    HttpClient httpClient,
    IOptions<GeminiOptions> geminiOptions,
    ITripRepository tripRepository,
    ITripMemberRepository memberRepository,
    IReceiptRepository receiptRepository) : IAiReceiptScannerService
{
    public const int MaxReceiptImageBytes = 10 * 1024 * 1024;
    public const string DemoTitlePrefix = "[Demo AI]";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public void ValidateReceiptImage(string fileName, string? contentType, ReadOnlySpan<byte> imageBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Tên tệp ảnh hóa đơn không hợp lệ.", nameof(fileName));
        }

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            throw new ArgumentException(
                "Chỉ chấp nhận ảnh hóa đơn định dạng JPG, JPEG, PNG hoặc WEBP.",
                nameof(fileName));
        }

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("Tệp ảnh hóa đơn đang trống (0 byte).", nameof(imageBytes));
        }

        if (imageBytes.Length > MaxReceiptImageBytes)
        {
            throw new ArgumentException("Kích thước ảnh hóa đơn không được vượt quá 10 MB.", nameof(imageBytes));
        }

        var detectedMime = DetectMimeFromMagicBytes(imageBytes);
        if (detectedMime is null)
        {
            throw new ArgumentException(
                "Nội dung tệp không phải là định dạng ảnh hợp lệ (JPG, PNG hoặc WEBP).",
                nameof(imageBytes));
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var normalizedContentType = contentType.Trim().ToLowerInvariant();
            if (normalizedContentType is not ("image/jpeg" or "image/jpg" or "image/png" or "image/webp" or "application/octet-stream"))
            {
                throw new ArgumentException(
                    $"Kiểu MIME '{contentType}' không được hỗ trợ cho quét hóa đơn.",
                    nameof(contentType));
            }
        }
    }

    public async Task<ReceiptDraftDto> ScanAndCreateDraftAsync(
        ScanReceiptRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trip = await tripRepository.GetByIdAsync(request.TripId, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy chuyến đi.");

        if (trip.Status is TripStatus.Settling or TripStatus.Completed)
        {
            throw new InvalidOperationException(
                "Chuyến đi đang tất toán hoặc đã hoàn tất, không thể quét thêm hóa đơn mới.");
        }

        var activeMembers = await memberRepository.GetActiveMembersByTripAsync(trip.Id, ct);
        var activeMap = activeMembers.ToDictionary(m => m.UserId, m => m);

        if (!activeMap.ContainsKey(request.ActorUserId))
        {
            throw new UnauthorizedAccessException("Bạn không phải là thành viên đang hoạt động của chuyến đi này.");
        }

        var paidByUserId = request.PaidByUserId ?? request.ActorUserId;
        if (!activeMap.TryGetValue(paidByUserId, out var payerMember))
        {
            throw new InvalidOperationException("Người ứng trả hóa đơn phải là thành viên đang hoạt động của chuyến đi.");
        }

        string resolvedMimeType = "image/jpeg";
        if (!request.UseDemoSample)
        {
            if (request.ImageBytes is null || request.ImageBytes.Length == 0)
            {
                throw new ArgumentException("Vui lòng chọn tệp ảnh hóa đơn để quét AI.", nameof(request));
            }

            ValidateReceiptImage(request.FileName ?? "receipt.jpg", request.ContentType, request.ImageBytes);
            resolvedMimeType = DetectMimeFromMagicBytes(request.ImageBytes) ?? "image/jpeg";
        }
        else if (request.ImageBytes is { Length: > 0 })
        {
            ValidateReceiptImage(request.FileName ?? "receipt.jpg", request.ContentType, request.ImageBytes);
            resolvedMimeType = DetectMimeFromMagicBytes(request.ImageBytes) ?? "image/jpeg";
        }

        var options = geminiOptions.Value ?? new GeminiOptions();
        var apiKey = (options.ApiKey ?? string.Empty).Trim();
        var model = string.IsNullOrWhiteSpace(options.Model) ? "gemini-2.5-flash" : options.Model.Trim();

        ParsedReceiptPayload parsed;
        bool isDemoFallback;
        string? fallbackNotice;

        if (request.UseDemoSample || string.IsNullOrWhiteSpace(apiKey) || request.ImageBytes is not { Length: > 0 })
        {
            parsed = BuildSmartVietnameseHotpotFallback();
            isDemoFallback = true;
            fallbackNotice = string.IsNullOrWhiteSpace(apiKey)
                ? "Chế độ Demo AI (chưa cấu hình Gemini:ApiKey) — Đã nạp mẫu hóa đơn Lẩu & Nướng kèm VAT 8% và giảm giá để trình diễn trực tiếp."
                : "Đã nạp hóa đơn mẫu Demo AI (Lẩu & Nướng + VAT 8% + Voucher giảm giá) để kiểm thử quy trình duyệt OCR & nhận món.";
        }
        else
        {
            var aiResult = await TryExtractWithGeminiVisionAsync(apiKey, model, request.ImageBytes, resolvedMimeType, ct);
            if (aiResult.Payload is not null)
            {
                parsed = aiResult.Payload;
                isDemoFallback = false;
                fallbackNotice = null;
            }
            else
            {
                parsed = BuildSmartVietnameseHotpotFallback();
                isDemoFallback = true;
                fallbackNotice = $"Không thể phân tích phản hồi trực tiếp từ Gemini Vision ({aiResult.ErrorReason ?? "định dạng không hợp lệ"}) — hệ thống đã chuyển sang mẫu hóa đơn Demo thông minh để bạn tiếp tục chỉnh sửa.";
            }
        }

        var nowUtc = DateTime.UtcNow;
        var spentAtUtc = ParseSpentDateOrFallback(parsed.SpentDate, nowUtc);
        var category = ParseCategoryOrFallback(parsed.Category);

        var rawTitle = (parsed.MerchantTitle ?? parsed.Title ?? "Hóa đơn quét AI").Trim();
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            rawTitle = "Hóa đơn quét AI";
        }

        if (rawTitle.Length > 200)
        {
            rawTitle = rawTitle[..200];
        }

        var userNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var combinedNotes = isDemoFallback && !string.IsNullOrWhiteSpace(fallbackNotice)
            ? (string.IsNullOrWhiteSpace(userNotes) ? fallbackNotice : $"{userNotes}\n({fallbackNotice})")
            : userNotes;

        if (combinedNotes is not null && combinedNotes.Length > 2000)
        {
            combinedNotes = combinedNotes[..2000];
        }

        var subtotal = NormalizeWholeVnd(parsed.SubtotalAmount);
        var taxOrTip = NormalizeWholeVnd(parsed.TaxOrTipAmount);
        var discount = NormalizeWholeVnd(parsed.DiscountAmount);
        var total = NormalizeWholeVnd(parsed.TotalAmount);

        var expenseId = Guid.NewGuid();
        var itemEntities = (parsed.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.ItemName))
            .Select((item, idx) =>
            {
                var qty = item.Quantity <= 0 ? 1 : item.Quantity;
                var lineTotal = NormalizeWholeVnd(item.LineTotal);
                var unitPrice = NormalizeWholeVnd(item.UnitPrice);

                if (lineTotal == 0m && unitPrice > 0m)
                {
                    lineTotal = unitPrice * qty;
                }
                else if (unitPrice == 0m && lineTotal > 0m && qty > 0)
                {
                    unitPrice = decimal.Floor(lineTotal / qty);
                }

                var cleanName = item.ItemName!.Trim();
                if (cleanName.Length > 250)
                {
                    cleanName = cleanName[..250];
                }

                return new ReceiptItem
                {
                    Id = Guid.NewGuid(),
                    ExpenseId = expenseId,
                    ItemName = cleanName,
                    Quantity = qty,
                    UnitPrice = unitPrice,
                    LineTotal = lineTotal,
                    SortOrder = idx
                };
            })
            .ToList();

        if (subtotal == 0m && itemEntities.Count > 0)
        {
            subtotal = itemEntities.Sum(i => i.LineTotal);
        }

        if (total == 0m && subtotal > 0m)
        {
            total = Math.Max(0m, subtotal + taxOrTip - discount);
        }

        var draftExpense = new Expense
        {
            Id = expenseId,
            TripId = trip.Id,
            PaidByUserId = paidByUserId,
            Title = rawTitle,
            Category = category,
            SubtotalAmount = subtotal,
            TaxOrTipAmount = taxOrTip,
            DiscountAmount = discount,
            TotalAmount = total,
            SplitMode = ExpenseSplitMode.Itemized,
            ReceiptImageUrl = request.ReceiptImageUrl,
            ScanStatus = ReceiptScanStatus.DraftReview,
            SpentAtUtc = spentAtUtc,
            Notes = combinedNotes,
            CreatedAtUtc = nowUtc
        };

        await receiptRepository.CreateReceiptDraftAsync(draftExpense, itemEntities, ct);

        var payerName = payerMember.User?.FullName ?? "Thành viên";
        return BuildReceiptDraftDto(draftExpense, itemEntities, payerName, isDemoFallback, fallbackNotice);
    }

    internal static ReceiptDraftDto BuildReceiptDraftDto(
        Expense expense,
        IReadOnlyList<ReceiptItem> orderedItems,
        string payerName,
        bool? explicitDemoFlag = null,
        string? explicitFallbackNotice = null)
    {
        var itemDtos = orderedItems
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new ReceiptDraftItemDto(
                i.Id,
                i.ItemName,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal,
                i.SortOrder))
            .ToList();

        var itemsSum = itemDtos.Sum(i => i.LineTotal);
        var subtotalDelta = itemsSum - expense.SubtotalAmount;
        var expectedTotalFromItems = itemsSum + expense.TaxOrTipAmount - expense.DiscountAmount;
        var expectedTotalFromSubtotal = expense.SubtotalAmount + expense.TaxOrTipAmount - expense.DiscountAmount;
        var totalDelta = expectedTotalFromItems - expense.TotalAmount;

        var reasons = new List<string>();

        if (itemDtos.Count == 0)
        {
            reasons.Add("Hóa đơn chưa có dòng món ăn/mặt hàng nào.");
        }

        if (expense.SubtotalAmount <= 0m || expense.TotalAmount <= 0m)
        {
            reasons.Add("Tạm tính và Tổng thanh toán phải lớn hơn 0đ.");
        }

        if (expense.DiscountAmount > expense.SubtotalAmount + expense.TaxOrTipAmount)
        {
            reasons.Add("Giảm giá đang vượt quá tổng tạm tính và thuế/phí.");
        }

        if (itemsSum != expense.SubtotalAmount)
        {
            var diff = itemsSum - expense.SubtotalAmount;
            reasons.Add(
                $"Tổng tiền các món ({itemsSum:N0}đ) lệch {(diff > 0 ? "+" : "")}{diff:N0}đ so với Tạm tính ({expense.SubtotalAmount:N0}đ).");
        }

        if (expectedTotalFromItems != expense.TotalAmount || expectedTotalFromSubtotal != expense.TotalAmount)
        {
            var diff = expectedTotalFromItems - expense.TotalAmount;
            reasons.Add(
                $"Tổng các món + Thuế/Phí ({expense.TaxOrTipAmount:N0}đ) - Giảm giá ({expense.DiscountAmount:N0}đ) = {expectedTotalFromItems:N0}đ, lệch {(diff > 0 ? "+" : "")}{diff:N0}đ so với Tổng hóa đơn ({expense.TotalAmount:N0}đ).");
        }

        var hasMismatch = reasons.Count > 0;
        var warningMessage = hasMismatch ? string.Join(" ", reasons) : null;

        var isDemo = explicitDemoFlag ?? expense.Title.StartsWith(DemoTitlePrefix, StringComparison.OrdinalIgnoreCase);
        var fallbackNotice = explicitFallbackNotice ?? (isDemo
            ? "Dữ liệu hóa đơn được khởi tạo từ chế độ Demo AI thông minh — bạn có thể chỉnh sửa tự do trước khi mở chọn món."
            : null);

        return new ReceiptDraftDto(
            expense.Id,
            expense.TripId,
            expense.PaidByUserId,
            payerName,
            expense.Title,
            expense.Category,
            expense.ReceiptImageUrl,
            expense.ScanStatus,
            expense.SpentAtUtc,
            expense.Notes,
            expense.SubtotalAmount,
            expense.TaxOrTipAmount,
            expense.DiscountAmount,
            expense.TotalAmount,
            itemsSum,
            subtotalDelta,
            expectedTotalFromItems,
            totalDelta,
            hasMismatch,
            warningMessage,
            isDemo,
            fallbackNotice,
            itemDtos);
    }

    private async Task<(ParsedReceiptPayload? Payload, string? ErrorReason)> TryExtractWithGeminiVisionAsync(
        string apiKey,
        string model,
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        const string systemInstruction = """
            Bạn là trợ lý OCR kế toán chuyên đọc hóa đơn Việt Nam và quốc tế cho ứng dụng chia tiền nhóm SplitTrip.
            Hãy đọc ảnh hóa đơn và trích xuất chính xác sang JSON theo đúng cấu trúc:
            {
              "merchantTitle": "Tên quán / cửa hàng trên hóa đơn",
              "category": "Food | Transport | Accommodation | Ticket | Shopping | Entertainment | Other",
              "spentDate": "YYYY-MM-DD",
              "items": [
                {
                  "itemName": "Tên món / sản phẩm",
                  "quantity": 1,
                  "unitPrice": 50000,
                  "lineTotal": 50000
                }
              ],
              "subtotalAmount": 50000,
              "taxOrTipAmount": 4000,
              "discountAmount": 0,
              "totalAmount": 54000
            }
            Quy tắc bắt buộc:
            - Mọi số tiền tính bằng số nguyên VNĐ (ví dụ 85.000đ -> 85000, nếu hóa đơn ghi đơn vị nghìn như 85 -> nhân 1000 thành 85000).
            - Giữ nguyên con số Tạm tính (subtotalAmount), Thuế VAT / Phí phục vụ (taxOrTipAmount), Giảm giá / Voucher (discountAmount) và Tổng thanh toán (totalAmount) in trên hóa đơn để hệ thống kiểm tra sai lệch OCR.
            - Chỉ trả về đối tượng JSON thuần túy, không kèm văn bản giải thích.
            """;

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = systemInstruction },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = Convert.ToBase64String(imageBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.1
            }
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(endpoint, requestBody, ct);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"HTTP {(int)response.StatusCode}");
            }

            var envelope = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(JsonOptions, ct);
            var rawJson = envelope?.Candidates?
                .FirstOrDefault()?
                .Content?
                .Parts?
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Text))?
                .Text;

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return (null, "phản hồi rỗng từ mô hình");
            }

            var cleanedJson = StripMarkdownCodeFences(rawJson);
            var parsed = JsonSerializer.Deserialize<ParsedReceiptPayload>(cleanedJson, JsonOptions);
            if (parsed is null || parsed.Items is null || parsed.Items.Count == 0)
            {
                return (null, "không trích xuất được danh sách món");
            }

            return (parsed, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return (null, "JSON không hợp lệ");
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    private static ParsedReceiptPayload BuildSmartVietnameseHotpotFallback() => new()
    {
        MerchantTitle = $"{DemoTitlePrefix} Lẩu & Nướng Khói Thông Đà Lạt",
        Category = "Food",
        SpentDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        Items =
        [
            new ParsedReceiptLineItem
            {
                ItemName = "Set bò Mỹ nướng tảng sốt tiêu đen",
                Quantity = 1,
                UnitPrice = 220_000m,
                LineTotal = 220_000m
            },
            new ParsedReceiptLineItem
            {
                ItemName = "Lẩu cá tầm măng chua cỡ lớn",
                Quantity = 1,
                UnitPrice = 280_000m,
                LineTotal = 280_000m
            },
            new ParsedReceiptLineItem
            {
                ItemName = "Ba chỉ heo cuộn nấm kim châm",
                Quantity = 2,
                UnitPrice = 85_000m,
                LineTotal = 170_000m
            },
            new ParsedReceiptLineItem
            {
                ItemName = "Rau rừng & nấm tổng hợp ăn kèm",
                Quantity = 1,
                UnitPrice = 60_000m,
                LineTotal = 60_000m
            },
            new ParsedReceiptLineItem
            {
                ItemName = "Trà đào cam sả (ca lớn dùng chung)",
                Quantity = 2,
                UnitPrice = 45_000m,
                LineTotal = 90_000m
            }
        ],
        SubtotalAmount = 820_000m,
        TaxOrTipAmount = 65_600m, // 8% VAT
        DiscountAmount = 50_000m, // Voucher nhóm -50k
        TotalAmount = 835_600m
    };

    private static string? DetectMimeFromMagicBytes(ReadOnlySpan<byte> bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        // WEBP: RIFF....WEBP
        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }

    private static string StripMarkdownCodeFences(string raw)
    {
        var trimmed = raw.Trim();
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

    private static decimal NormalizeWholeVnd(decimal value)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        return decimal.Truncate(decimal.Round(value, 0, MidpointRounding.AwayFromZero));
    }

    private static ExpenseCategory ParseCategoryOrFallback(string? rawCategory)
    {
        if (!string.IsNullOrWhiteSpace(rawCategory)
            && Enum.TryParse<ExpenseCategory>(rawCategory.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return ExpenseCategory.Food;
    }

    private static DateTime ParseSpentDateOrFallback(string? rawDate, DateTime fallbackUtc)
    {
        if (!string.IsNullOrWhiteSpace(rawDate) && DateTime.TryParse(rawDate.Trim(), out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return fallbackUtc;
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        public string? Text { get; set; }
    }

    private sealed class ParsedReceiptPayload
    {
        public string? MerchantTitle { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public string? SpentDate { get; set; }
        public List<ParsedReceiptLineItem>? Items { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal TaxOrTipAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class ParsedReceiptLineItem
    {
        public string? ItemName { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}
