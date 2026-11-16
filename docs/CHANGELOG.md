# Nhật Ký Thay Đổi (Changelog)

Tất cả các thay đổi đáng chú ý của dự án CourtBook sẽ được ghi nhận tại đây.

## [2.0.0] - 2026-11-16
### Đã thêm (Added)
- Hỗ trợ cơ sở dữ liệu kép (Dual Database): SQLite out-of-the-box và SQL Server 2022 tự động fallback.
- Quản lý hồ sơ cá nhân và đổi mật khẩu an toàn (/Account/Profile).
- Tích hợp cổng thanh toán VietQR Napas 24/7 tự động sinh QR cho ngân hàng MB Bank 0901976201.
- Hệ thống Đánh giá & Xếp hạng 5 sao cho từng cụm sân (VenueReviews).
- Bộ lọc nâng cao theo giá và sắp xếp đa chiều tại Marketplace.
- Phân hệ vận hành chủ sân (/Owner): KPI doanh thu tháng, check-in nhanh theo mã, đổi trạng thái thanh toán, xuất báo cáo CSV UTF-8.
- Phân hệ quản trị sàn (/Admin): Thống kê GMV & hoa hồng 10%, tìm kiếm người dùng realtime.
- Bộ 48 bài kiểm thử tự động xUnit đạt 100% Passed.

## [1.1.0] - 2026-11-15
### Đã thêm (Added)
- Bổ sung thư viện ảnh thực tế Bento cho cụm sân và ảnh thumbnail cho từng sân con.
- Upload ảnh multipart có kiểm tra Magic Bytes nhị phân chống file độc hại.

## [1.0.0] - 2026-11-14
### Khởi tạo (Initial Release)
- Khởi tạo kiến trúc 5 tầng chuẩn PRN222.
- Ma trận lịch trực tuyến 16 khung giờ với SignalR Hub.
- Thuật toán khóa đồng thời SemaphoreSlim và Filtered Unique Index chống Double-booking.
