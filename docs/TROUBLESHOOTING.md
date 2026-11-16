# Hướng Dẫn Khắc Phục Sự Cố & FAQ (Troubleshooting Guide)

## 1. Lỗi Cổng Mạng Bị Chiếm Dụng (Port Already in Use)
- **Triệu chứng:** Khi chạy `dotnet run`, thông báo lỗi `Failed to bind to address http://localhost:5000: address already in use`.
- **Cách xử lý:** Chỉ định một cổng khác khi khởi chạy:
  ```bash
  dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj --urls "http://127.0.0.1:5123"
  ```

## 2. Kiểm Tra Trạng Thái Cơ Sở Dữ Liệu Kép (Dual Database)
- Hệ thống hỗ trợ 2 loại database tùy chọn trong `appsettings.json`:
  ```json
  "DatabaseProvider": "Sqlite"
  ```
  - *Sqlite:* Tự động tạo file `CourtBook.db` trong thư mục `src/CourtBook.Web/`. Không cần cài đặt bất kỳ công cụ nào.
  - *SqlServer:* Yêu cầu Docker hoặc SQL Server nội bộ cổng 1433 (`127.0.0.1:1433`). Nếu cổng 1433 không bật, ứng dụng tự động fallback về SQLite.

## 3. Khôi Phục Lại Dữ Liệu Mẫu Ban Đầu (Reset Seed Data)
- Muốn xóa trắng và nạp lại toàn bộ 5 tài khoản mẫu, 4 cụm sân và 18 sân con:
  - Chỉ cần dừng ứng dụng (`Ctrl + C`).
  - Xóa file `src/CourtBook.Web/CourtBook.db*`.
  - Khởi động lại ứng dụng, `DbInitializer` sẽ tự động tạo lại và seed mới hoàn toàn.
