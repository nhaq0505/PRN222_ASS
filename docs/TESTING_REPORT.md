# Báo Cáo Kiểm Thử Chất Lượng Phần Mềm (Testing Report)

## 1. Tổng Quan Kết Quả Kiểm Thử (xUnit)
- **Tổng số bài test tự động:** 42 bài kiểm thử (xUnit 2.9.2).
- **Trạng thái:** **42 Passed, 0 Failed, 0 Skipped (100% Passed)**.
- **Thời gian thực thi:** ~1.2 giây.
- **Môi trường:** .NET 8.0 InMemory Database & Mutable TimeProvider.

## 2. Phân Bố Kiểm Thử Theo Chuyên Đề

| File kiểm thử | Số lượng test | Chức năng kiểm thử chính |
| :--- | :---: | :--- |
| `AuthServiceTests.cs` | 4 tests | Xác thực đăng nhập, mã hóa PBKDF2, đăng ký khách hàng & đối tác |
| `BookingServiceTests.cs` | 12 tests | Giữ chỗ đồng thời, nhả giữ chỗ, xung đột giờ, TTL 2 phút, SignalR broadcast |
| `CourtServiceTests.cs` | 6 tests | CRUD sân con, tính giá giờ thường / giờ vàng, bật/tắt bảo trì |
| `VenueAndCourtPhotoTests.cs` | 8 tests | Upload ảnh multipart, kiểm tra magic bytes, thư viện ảnh Bento |
| `VenueAndMarketplaceTests.cs` | 5 tests | Tìm kiếm theo khu vực, môn thể thao, từ khóa, kiểm duyệt admin |
| `NewFeaturesTests.cs` | 7 tests | Profile cá nhân, đổi mật khẩu, VietQR, Quick Check-in, Toggle TT, Xuất CSV, Hoa hồng 10% |

## 3. Lệnh Chạy Toàn Bộ Test Suite
```bash
dotnet test CourtBook.sln
```
