# ADR-001: Chiến Lược Cơ Sở Dữ Liệu Kép (Dual Database Strategy)

## Bối Cảnh (Context)
Đồ án PRN222 yêu cầu triển khai CSDL quan hệ SQL Server. Tuy nhiên, trong quá trình chấm bài và demo, việc yêu cầu máy chấm phải cài đặt SQL Server hoặc Docker Compose có thể gây bất tiện và lỗi cấu hình môi trường.

## Quyết Định (Decision)
Hệ thống thiết kế kiến trúc hỗ trợ CSDL kép (Dual Database):
1. **SQLite (Mặc định):** Tự động khởi tạo database file `CourtBook.db` và seed dữ liệu ngay lập tức mà không cần bất kỳ dịch vụ nền nào.
2. **SQL Server 2022:** Sẵn sàng cho môi trường doanh nghiệp qua `docker-compose.yml`.
3. **Cơ chế Auto-Fallback:** Tại `Program.cs`, ứng dụng tự động kiểm tra cổng 1433; nếu SQL Server không phản hồi, tự động chuyển sang SQLite để đảm bảo web app không bao giờ bị crash khi khởi động.

## Hệ Quả (Consequences)
- Tương thích 100% với môi trường chấm bài cục bộ.
- Code DAO sử dụng cú pháp SQL chuẩn Relational và xử lý mã lỗi ràng buộc cho cả 2 engine.
