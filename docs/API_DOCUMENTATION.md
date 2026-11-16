# Tài Liệu Đặc Tả Service Layer (API Documentation)

## 1. IAuthService
- `AuthenticateAsync(string email, string password)`: Xác thực thông tin đăng nhập, đối soát băm PBKDF2.
- `RegisterAsync(RegisterRequestDto req)`: Đăng ký tài khoản khách hàng hoặc đối tác chủ sân.
- `GetProfileAsync(int userId)`: Trả về thông tin cá nhân và 3 chỉ số KPI (tổng đơn, đơn sắp tới, chi tiêu).
- `UpdateProfileAsync(int userId, UpdateProfileRequestDto req)`: Cập nhật Họ tên, Số điện thoại.
- `ChangePasswordAsync(int userId, ChangePasswordRequestDto req)`: Đổi mật khẩu an toàn.

## 2. IBookingService
- `GetScheduleMatrixAsync(int venueId, DateOnly playDate, SportType? sport, int? currentUserId)`: Tạo ma trận 16 khung giờ cho tất cả sân con.
- `ToggleHoldSlotAsync(int userId, int courtId, DateOnly playDate, int startHour)`: Giữ chỗ tạm thời 2 phút, khóa SemaphoreSlim.
- `ConfirmMyHoldsAsync(int userId, int venueId, string? notes, string? paymentMethod)`: Xác nhận đơn đặt với phương thức thanh toán VietQR hoặc tại sân.
- `QuickCheckInByCodeAsync(string bookingCode, int actorUserId, bool isAdmin)`: Check-in nhanh 1 chạm theo mã đơn.
- `TogglePaymentStatusAsync(int bookingId, int actorUserId, bool isAdmin)`: Đổi trạng thái thanh toán trực tiếp.
- `ExportOwnerBookingsCsvAsync(int venueId, int actorUserId, bool isAdmin)`: Xuất danh sách lịch đặt ra CSV chuẩn UTF-8.
- `ReleaseExpiredHoldsAsync()`: Background cleanup worker nhả các slot quá hạn.

## 3. IVenueService & ICourtService
- `GetMarketplaceAdvancedAsync(string? keyword, string? district, SportType? sport, DateOnly todayVn, string? sortBy, decimal? maxPrice)`: Tìm kiếm và sắp xếp đa chiều.
- `CreateVenueAsync` / `UpdateVenueAsync`: Tạo mới/cập nhật cụm sân kèm ảnh bìa & bộ sưu tập.
- `CreateCourtAsync` / `UpdateCourtAsync`: Thêm/sửa sân con, thiết lập giá thường/giờ vàng.

## 4. IVenueReviewService
- `GetVenueReviewsAsync(int venueId)`: Lấy danh sách đánh giá của cụm sân.
- `AddReviewAsync(int userId, CreateReviewRequestDto req)`: Gửi đánh giá 1-5 sao và bình luận.
- `GetAverageRatingAsync(int venueId)`: Tính điểm đánh giá trung bình.
