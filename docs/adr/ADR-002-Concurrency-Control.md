# ADR-002: Kiểm Soát Đồng Thời 3 Lớp Chống Double-Booking

## Bối Cảnh (Context)
Tại các sàn đặt sân thể thao, nhiều người dùng có thể cùng click vào một khung giờ trống tại cùng một micro-giây. Cần giải pháp ngăn chặn tuyệt đối lỗi đặt trùng lịch (Double-Booking).

## Quyết Định (Decision)
Triển khai mô hình kiểm soát đồng thời 3 lớp (3-tier Concurrency Defense):
1. **Lớp 1 (Presentation & SignalR):** Bắn sự kiện `SlotUpdated` đến nhóm `venue-{id}` ngay khi có người giữ chỗ, đổi màu ô sang cam và vô hiệu hóa ô đó trên tất cả các trình duyệt khác.
2. **Lớp 2 (Service Semaphore):** Sử dụng `SemaphoreSlim` để đồng bộ hóa các request đặt chỗ đồng thời đến cùng một cụm sân.
3. **Lớp 3 (Database Constraint):** Cấu hình Filtered Unique Index trên `(CourtId, PlayDate, StartHour) WHERE Status IN (1, 2, 3)`. Bắt ngoại lệ `DbUpdateException` tại DAO và trả về thông báo lỗi thân thiện.
4. **TTL Expiry Worker:** Background service chạy mỗi 10 giây thu hồi các giữ chỗ quá hạn 2 phút.
