namespace CourtBook.Services.Common;

/// <summary>
/// Trợ giúp ghi log nhật ký kiểm toán cho các chuyển đổi trạng thái đặt sân (Booking Lifecycle).
/// Tác giả: tphuy227266-star
/// </summary>
public static class OperationLogger
{
    public static string FormatBookingTransition(string bookingCode, string fromStatus, string toStatus, int actorUserId)
    {
        return $"[AUDIT] Đơn đặt sân #{bookingCode}: Trạng thái chuyển từ [{fromStatus}] -> [{toStatus}] bởi User #{actorUserId} lúc {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
    }

    public static string FormatPaymentStatusToggle(string bookingCode, bool isPaid, int actorUserId)
    {
        string statusText = isPaid ? "ĐÃ THANH TOÁN" : "CHƯA THANH TOÁN";
        return $"[PAYMENT] Đơn đặt sân #{bookingCode}: Cập nhật trạng thái thanh toán -> [{statusText}] bởi User #{actorUserId}";
    }
}
