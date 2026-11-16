# Tổng Quan Bảo Mật Hệ Thống (Security Overview)

## 1. Cơ Chế Mã Hóa Mật Khẩu (PBKDF2)
- Lớp `PasswordHelper` sử dụng thuật toán **PBKDF2 (Password-Based Key Derivation Function 2)** với thuật toán băm **HMAC-SHA256**.
- Số lượng vòng lặp: **100.000 iterations**.
- Muối ngẫu nhiên (Salt): **16 bytes** sinh từ bộ sinh số ngẫu nhiên an toàn mật mã (`RandomNumberGenerator`).
- Kích thước khóa dẫn xuất: **32 bytes**.
- Định dạng lưu trữ: `{SaltBase64}:{SubkeyBase64}`. Ngăn chặn hoàn toàn tấn công Rainbow Table.

## 2. Cô Lập Đa Chủ Sân (Tenant Isolation)
- Quyền truy cập cụm sân và sân con được kiểm soát chặt chẽ ở tầng Service:
  ```csharp
  if (venue.OwnerId != actorUserId && !isAdmin)
  {
      return ServiceResult.Fail("Bạn không có quyền quản lý cụm sân này.");
  }
  ```
- Chủ sân A không thể xem danh sách đặt sân, doanh thu, hoặc can thiệp dữ liệu của chủ sân B.

## 3. Bảo Vệ File Upload (Magic Bytes Validation)
- `LocalPhotoStorageService` thực hiện kiểm tra an toàn 3 lớp:
  1. Giới hạn kích thước tệp tối đa: **5MB**.
  2. Danh sách phần mở rộng được phép: `.jpg`, `.jpeg`, `.png`, `.webp`.
  3. **Kiểm tra Magic Bytes (File Signature) ở mức nhị phân:**
     - JPEG: `FF D8 FF`
     - PNG: `89 50 4E 47 0D 0A 1A 0A`
     - WebP: `52 49 46 46 ... 57 45 42 50`
  - Ngăn chặn triệt để kẻ xấu đổi tên file mã độc `.exe` hoặc web shell `.php`/`.asp` thành `.jpg` để upload.

## 4. Chống Tấn Công Web (Anti-CSRF & XSS)
- Toàn bộ biểu mẫu Razor Pages POST đều được bảo vệ tự động bằng ASP.NET Core **Anti-Forgery Tokens** (`ValidateAntiForgeryToken`).
- Hệ thống escape dữ liệu HTML tự động trên Razor Pages chống tấn công Cross-Site Scripting (XSS).
- Cookie xác thực được gắn cờ `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, và `SecurePolicy = SameAsRequest`.
