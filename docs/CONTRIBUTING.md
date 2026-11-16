# Quy Chuẩn Đóng Góp & Phát Triển Mã Nguồn (Contributing Guidelines)

Dự án CourtBook tuân thủ các quy chuẩn lập trình C# / .NET hiện đại và mô hình phân nhánh Git Flow:

## 1. Quy Chuẩn Đặt Tên Commit (Conventional Commits)
- `feat`: Tính năng mới (ví dụ: `feat(payment): integrate VietQR Napas`).
- `fix`: Sửa lỗi (ví dụ: `fix(concurrency): handle unique constraint violation`).
- `docs`: Tài liệu hướng dẫn (ví dụ: `docs(arch): add architecture diagram`).
- `test`: Viết thêm kiểm thử tự động (ví dụ: `test(auth): add password hashing tests`).
- `refactor`: Tái cấu trúc mã nguồn không làm thay đổi tính năng.

## 2. Quy Chuẩn Kiến Trúc 5 Tầng PRN222
- Tầng trên chỉ phụ thuộc tầng dưới thông qua Interface.
- Không inject `CourtBookDbContext` trực tiếp vào Razor Pages.
- Toàn bộ thời gian lưu trong CSDL phải chuẩn hóa `DateTimeKind.Utc`.
