# CourtBook — Sàn Đặt Sân Pickleball & Cầu Lông Đa Chủ Sân (Multi-Owner Marketplace)

**CourtBook** là nền tảng sàn thương mại điện tử đặt sân **Pickleball & Cầu lông** theo mô hình **Multi-Owner Venue Marketplace**, xây dựng theo chuẩn kiến trúc **5-Layer Architecture (PRN222)** bằng **ASP.NET Core Razor Pages (.NET 8 LTS)**, **SignalR Real-time**, hỗ trợ kép **SQLite & SQL Server 2022**.

Nền tảng hỗ trợ **3 vai trò độc lập (`Member`, `CourtOwner`, `Admin`)**:
- **Người chơi (`Member`)**: khám phá các cụm sân đang hoạt động theo khu vực, môn thể thao, mức giá và điểm đánh giá; đặt chỗ theo ma trận lịch trực tuyến từng cụm sân (`/Venues/{id}`) với giữ chỗ tạm thời 2 phút qua SignalR; thanh toán linh hoạt (Tại sân / Chuyển khoản VietQR tự động sinh QR Napas 24/7); quản lý vé đặt sân tại `/MyBookings`; quản lý hồ sơ và đổi mật khẩu tại `/Account/Profile`; đánh giá và chấm điểm 5 sao cho cụm sân.
- **Chủ cụm sân (`CourtOwner`)**: tự đăng ký tài khoản đối tác (`/Account/Register?owner=true`), tạo & quản lý nhiều cụm sân, tải ảnh bìa và thư viện ảnh thực tế, cấu hình sân con (giá thường / giờ vàng, bảo trì), bảng điều khiển tài chính (doanh thu tháng, tổng đơn), check-in nhanh 1 chạm theo mã đặt sân, chuyển đổi trạng thái thanh toán trực tiếp, xuất danh sách đặt sân ra file CSV chuẩn UTF-8 tại `/Owner` với cơ chế **cô lập dữ liệu theo chủ sở hữu (tenant isolation)**.
- **Quản trị sàn (`Admin`)**: phê duyệt cụm sân mới (`PendingApproval` → `Active`), tạm khóa/mở lại cụm sân (`Suspended` ↔ `Active`), theo dõi hoa hồng sàn (10% GMV), lọc tìm kiếm người dùng tức thì theo từ khóa/vai trò và theo dõi GMV toàn sàn tại `/Admin`.

---

## 1. Kiến trúc 5 tầng chuẩn PRN222 (5-Layer Architecture)

Solution [`CourtBook.sln`](CourtBook.sln) được tổ chức theo kiến trúc 5 tầng chuẩn PRN222 cùng bộ kiểm thử tự động xUnit:

| Tầng | Dự án | Trách nhiệm chính |
| :--- | :--- | :--- |
| **Layer 1: BusinessObjects** | `src/CourtBook.BusinessObjects` | Entities (`AppUser`, `Venue`, `Court`, `Booking`, `VenueReview`), Enums (`UserRole`, `VenueStatus`, `SportType`, `BookingStatus`), giờ hoạt động (`Venue.GetOperatingHours`), ảnh (`Venue.CoverImageUrl`, `Venue.GalleryImageUrls`), tính giá giờ thường / giờ vàng (`Court.IsPeakHour`, `Court.GetPriceForHour`) và PBKDF2 hashing (`PasswordHelper`). |
| **Layer 2: DataAccessObjects** | `src/CourtBook.DataAccessObjects` | `CourtBookDbContext` (Hỗ trợ SQLite & SQL Server), các DAO (`UserDao`, `VenueDao`, `CourtDao`, `BookingDao` với `TryAddSlotAsync`, `VenueReviewDao`), và `DbInitializer` (tự động tạo bảng & seed dữ liệu Multi-Owner, 5 ảnh mẫu, đánh giá sao mẫu). |
| **Layer 3: Repositories** | `src/CourtBook.Repositories` | Các interface (`IUserRepository`, `IVenueRepository`, `ICourtRepository`, `IBookingRepository`, `IVenueReviewRepository`) và implementation bọc DAO phục vụ Dependency Injection & Unit Testing. |
| **Layer 4: Services** | `src/CourtBook.Services` | Business logic, DTO, bộ lọc nâng cao, xuất báo cáo CSV, thanh toán VietQR, kiểm tra URL ảnh (`ImageUrlValidator`), kiểm soát phân quyền (`AuthService`, `VenueService`, `CourtService`, `BookingService`, `VenueReviewService`, `PlatformAdminService`), khóa đồng bộ slot (`SemaphoreSlim`), tự động nhả giữ chỗ hết hạn, phát sự kiện real-time qua `ISlotRealtimeNotifier`. |
| **Layer 5: Presentation (Web)** | `src/CourtBook.Web` | Razor Pages (`Index`, `Venues/Details`, `MyBookings/Index`, `Owner/Index`, `Admin/Index`, `Account/*`), dịch vụ lưu ảnh multipart (`IPhotoStorageService`), SignalR Hub (`/hubs/court`), `SignalRSlotRealtimeNotifier`, `ExpiredHoldCleanupWorker`, Cookie Auth, CSS & JS client (`wwwroot/js/courtbook.js`, `wwwroot/uploads/venues/`). |
| **Tests** | `tests/CourtBook.Tests` | **42 kiểm thử tự động xUnit** (100% Passed) bao phủ `DbInitializer`, đăng ký/đăng nhập, đổi mật khẩu, cập nhật hồ sơ, đánh giá 5 sao, thanh toán VietQR, xuất báo cáo CSV, check-in nhanh, hoa hồng sàn, giữ chỗ đồng thời (`Task.WhenAll`), hết hạn TTL 2 phút, và cô lập dữ liệu chủ sân. |

---

## 2. Tính năng mới nổi bật (Professional Enhancements)

1. **Quản lý Hồ sơ cá nhân (`/Account/Profile`):**
   - Xem thống kê hoạt động cá nhân: Tổng số đơn đặt sân, số đơn sắp tới, tổng chi tiêu tích lũy (VNĐ).
   - Cập nhật thông tin liên hệ (Họ và tên, Số điện thoại).
   - Đổi mật khẩu an toàn có kiểm tra mật khẩu hiện tại bằng mã hóa PBKDF2.
2. **Hệ thống Đánh giá & Xếp hạng 5 sao (`VenueReviews`):**
   - Khách hàng đã trải nghiệm có thể gửi nhận xét và chấm điểm sao (1 - 5 ⭐).
   - Điểm đánh giá trung bình và tổng lượt review được hiển thị trực tiếp trên thẻ cụm sân tại trang chủ và trang chi tiết.
3. **Thanh toán VietQR Napas 24/7:**
   - Hỗ trợ 2 hình thức: **Thanh toán tại sân** và **Chuyển khoản VietQR**.
   - Khi chọn VietQR, vé đặt sân tự động tích hợp mã QR Napas 24/7 (ngân hàng MB Bank) kèm số tiền chính xác và cú pháp nội dung `CB {BookingCode}` giúp khách chuyển khoản tức thì bằng mọi app ngân hàng.
4. **Bộ lọc & Sắp xếp Nâng cao tại Marketplace (`/`):**
   - Lọc theo mức giá tối đa / giờ (VNĐ).
   - Sắp xếp linh hoạt: Mặc định, Giá tăng dần, Giá giảm dần, Đánh giá cao nhất, Số lượng sân con nhiều nhất.
5. **Bảng điều khiển Tài chính, Check-in nhanh & Xuất báo cáo CSV cho Chủ sân (`/Owner`):**
   - KPI thống kê Doanh thu trong tháng (VNĐ) và Số lượt đặt sân trong tháng.
   - Thanh tra cứu & Check-in nhanh 1 chạm theo mã đơn đặt sân (`BookingCode`).
   - Nút bật/tắt trạng thái thanh toán trực tiếp ("Chưa TT" ↔ "Đã TT") trên từng dòng đơn đặt.
   - Nút **Xuất danh sách đặt sân ra file CSV** chuẩn định dạng UTF-8 (kèm BOM để mở trực tiếp trong Excel không bị lỗi font tiếng Việt).
6. **Thống kê Hoa hồng sàn Platform Admin (`/Admin`):**
   - Tính toán tỷ lệ phí hoa hồng nền tảng (10%) trên tổng doanh thu GMV ngày và toàn thời gian.
   - Thanh tìm kiếm lọc người dùng thời gian thực (Live Client-side Search) giúp Admin tra cứu nhanh theo tên, email, vai trò hoặc trạng thái tài khoản.

---

## 3. Cấu hình Cơ sở dữ liệu: Dual Database (SQLite & SQL Server)

Dự án được cấu hình thông minh tại `src/CourtBook.Web/Program.cs` và `appsettings.json`:
- **Chạy ngay với SQLite (Mặc định - Khuyên dùng khi chấm bài / demo):** Không cần cài đặt bất kỳ phần mềm cơ sở dữ liệu hay Docker nào. Ứng dụng tự động tạo file `CourtBook.db` và seed toàn bộ dữ liệu mẫu khi khởi động.
- **Chạy với SQL Server 2022 (Nếu có Docker hoặc SQL Server nội bộ):** Nếu muốn dùng SQL Server, chỉ cần đổi cấu hình trong `appsettings.json`:
  ```json
  "DatabaseProvider": "SqlServer"
  ```
  *(Nếu SQL Server không phản hồi trên cổng 1433, hệ thống sẽ tự động fallback về SQLite để đảm bảo ứng dụng luôn chạy thành công mà không bị crash).*

---

## 4. Hướng dẫn Build, Chạy Tests & Khởi chạy Web App

### Bước 1: Build toàn bộ Solution
```bash
dotnet build CourtBook.sln
```

### Bước 2: Chạy toàn bộ 42 Unit Tests
```bash
dotnet test CourtBook.sln
```
*(Kết quả: 42 passed, 0 failed, 0 skipped)*

### Bước 3: Khởi chạy ứng dụng Web
```bash
dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj
```
Mở trình duyệt truy cập: **`http://localhost:5000`** (hoặc URL hiển thị trên terminal).

---

## 5. Danh sách Tài khoản Demo (1-Click Demo Login)

Tại trang `/Account/Login`, bạn có thể bấm trực tiếp các nút đăng nhập nhanh 1 chạm được chuẩn bị sẵn:

| Vai trò (`UserRole`) | Họ tên | Email | Mật khẩu | Chức năng chính |
| :--- | :--- | :--- | :--- | :--- |
| **Member** (`1`) | Nguyễn Văn An | `an@courtbook.vn` | `User@123` | Khám phá sân, lọc giá/đánh giá, đặt sân ma trận 16h, VietQR, xem vé `/MyBookings`, hồ sơ cá nhân `/Account/Profile`, viết đánh giá sân |
| **Member** (`1`) | Trần Thị Bình | `binh@courtbook.vn` | `User@123` | Dùng mở tab ẩn danh thứ 2 để kiểm tra SignalR Real-time giữ chỗ đồng thời giữa 2 người chơi |
| **CourtOwner** (`2`) | Lê Minh Khôi | `khoi.owner@courtbook.vn` | `Owner@123` | Quản trị 2 cụm sân (`Thảo Điền Racquet Club` & `Sala Riverside`), xem doanh thu tháng, check-in nhanh theo mã, đổi trạng thái thanh toán, xuất báo cáo CSV |
| **CourtOwner** (`2`) | Phạm Thu Hà | `ha.owner@courtbook.vn` | `Owner@123` | Quản trị cụm sân `Phú Mỹ Hưng Sport Center` và `Tân Bình Arena` |
| **Admin** (`3`) | Quản trị sàn CourtBook | `admin@courtbook.vn` | `Admin@123` | Bảng điều khiển quản trị `/Admin`: duyệt/tạm khóa cụm sân, thống kê GMV & hoa hồng 10%, tìm kiếm người dùng realtime |

---

## 6. Dữ liệu Cụm sân mẫu (Seed Data)

| ID | Tên cụm sân | Khu vực | Chủ sân | Đánh giá | Trạng thái | Số sân con |
| :-: | :--- | :--- | :--- | :-: | :--- | :--- |
| `1` | **Thảo Điền Racquet Club** | TP. Thủ Đức | Lê Minh Khôi | ⭐ 4.7 (3 đánh giá) | `Active` | 6 sân (4 Pickleball, 2 Cầu lông) |
| `2` | **Sala Riverside Pickleball** | TP. Thủ Đức | Lê Minh Khôi | ⭐ 4.5 (2 đánh giá) | `Active` | 4 sân (4 Pickleball) |
| `3` | **Phú Mỹ Hưng Sport Center** | Quận 7 | Phạm Thu Hà | ⭐ 4.6 (2 đánh giá) | `Active` | 5 sân (2 Pickleball, 3 Cầu lông) |
| `4` | **Tân Bình Arena** | Quận Tân Bình | Phạm Thu Hà | Mới | `PendingApproval` | 3 sân (3 Pickleball — đang chờ Admin duyệt) |

---

## 7. Cấu trúc thư mục dự án

```text
PRN222_ASS-main/
├── CourtBook.sln
├── Directory.Build.props
├── README.md
├── src/
│   ├── CourtBook.BusinessObjects/    # Entities, Enums, Hash passwords, Business rules
│   ├── CourtBook.DataAccessObjects/  # DbContext, DAOs, DbInitializer (Seed data)
│   ├── CourtBook.Repositories/       # Repository Pattern interfaces & implementations
│   ├── CourtBook.Services/           # DTOs, Domain services, SignalR notifier, VietQR, CSV Export
│   └── CourtBook.Web/                # Razor Pages, SignalR Hubs, Background workers, Static files
└── tests/
    └── CourtBook.Tests/              # 42 xUnit tests (Integration & Unit tests)
```

---

## 8. Đội ngũ phát triển (Contributors)
- **tphuy227266-star** (`tphuy227266@gmail.com`)
- **nhaq0505** (`anhquan050503@gmail.com`)
