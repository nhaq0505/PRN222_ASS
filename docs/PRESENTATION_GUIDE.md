# Hướng Dẫn Thuyết Trình Đồ Án PRN222 — CourtBook
## Hệ Thống Đặt Sân Thể Thao Đa Chủ Sân (Multi-Owner Marketplace)

### Thành viên phụ trách: tphuy227266-star

---

## 1. Phân Chia Vai Trò Nhóm 3 Thành Viên
- **Thành viên 1:** System Architecture, Database (Dual Database SQLite/SQL Server), Core Real-time SignalR Hub (`/hubs/court`), Concurrency Control (SemaphoreSlim + Filtered Unique Index + Background Worker).
- **Thành viên 2:** Nghiệp vụ Người chơi (Member), Marketplace Search/Filter/Sorting, Ma trận 16 khung giờ x N sân con, Tính giá giờ vàng (Peak Hours), Thanh toán VietQR Napas 24/7, Đánh giá 5 sao, Quản lý hồ sơ cá nhân và 42 Unit Tests xUnit.
- **Thành viên 3:** Cơ chế Tenant Isolation giữa các chủ sân, Phân hệ Chủ sân (Owner Portal - KPI doanh thu tháng, Check-in nhanh 1 chạm, Đổi trạng thái thanh toán, Xuất CSV UTF-8), Phân hệ Admin sàn (Duyệt sân, Hoa hồng 10%, Live User Search), Upload ảnh bảo mật Magic Bytes và Live Demo.

---

## 2. Các Điểm Sáng Tạo Nổi Bật Của Đồ Án
1. **Kiến trúc 5 tầng chuẩn PRN222:** BusinessObjects, DataAccessObjects, Repositories, Services, Web.
2. **Dual Database:** Hỗ trợ SQLite chạy ngay lập tức không cần cài đặt + SQL Server 2022 cho môi trường doanh nghiệp với cơ chế auto-fallback.
3. **Chống Double-Booking 3 lớp:** SignalR real-time UI + SemaphoreSlim memory lock + Filtered Unique Index database constraint.
4. **Thanh toán VietQR Napas 24/7:** Tự động tạo mã QR động ngân hàng MB Bank đúng số tiền và nội dung chuyển khoản `CB {BookingCode}`.
5. **Đánh giá & Xếp hạng 5 sao:** Tự động tính điểm trung bình hiển thị huy hiệu sao trên từng thẻ sân.
6. **Bảng điều khiển tài chính & Check-in nhanh cho Chủ sân:** Tra cứu nhanh theo mã vé, xuất báo cáo CSV chuẩn UTF-8.
7. **Hoa hồng sàn 10%:** Thống kê GMV và hoa hồng nền tảng theo thời gian thực cho Platform Admin.
8. **Kiểm thử tự động:** 42 bài xUnit tests đạt 100% Passed.
