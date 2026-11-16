namespace CourtBook.BusinessObjects.Entities;

/// <summary>
/// Thực thể Đánh giá & Xếp hạng sao của người chơi cho cụm sân thể thao.
/// Tác giả: tphuy227266-star
/// </summary>
public class VenueReview
{
    /// <summary>Mã định danh duy nhất của đánh giá</summary>
    public int Id { get; set; }

    /// <summary>Mã cụm sân nhận đánh giá</summary>
    public int VenueId { get; set; }

    /// <summary>Thực thể cụm sân liên kết</summary>
    public Venue? Venue { get; set; }

    /// <summary>Mã tài khoản người chơi gửi đánh giá</summary>
    public int UserId { get; set; }

    /// <summary>Thực thể tài khoản người dùng gửi đánh giá</summary>
    public AppUser? User { get; set; }

    /// <summary>Số sao đánh giá (thang điểm chuẩn từ 1 đến 5 sao)</summary>
    public int Rating { get; set; }

    /// <summary>Nội dung nhận xét chi tiết của người chơi</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Thời điểm gửi đánh giá (chuẩn UTC)</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
