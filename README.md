# CourtBook — Sàn Đặt Sân Pickleball & Cầu Lông Đa Chủ Sân (Multi-Owner Marketplace)

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/xUnit%20Tests-66%20Passed-22C55E?logo=xunit)](tests/CourtBook.Tests/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![VietQR](https://img.shields.io/badge/Payment-VietQR%20Napas%2024%2F7-005BAA)](https://vietqr.net/)

**CourtBook** là nền tảng sàn thương mại điện tử đặt sân **Pickleball & Cầu lông** theo mô hình **Multi-Owner Venue Marketplace**, xây dựng theo chuẩn kiến trúc **5-Layer Architecture (PRN222)** bằng **ASP.NET Core Razor Pages (.NET 8 LTS)**, **SignalR Real-time**, hỗ trợ kép **SQLite & SQL Server 2022**.

Nền tảng phân tách chặt chẽ **3 vai trò độc lập (`Member`, `CourtOwner`, `Admin`)**:
- **Hội viên (`Member`)**: khám phá các cụm sân đang hoạt động theo từ khóa, khu vực, môn thể thao và sắp xếp nâng cao (`Giá tăng/giảm`, `Đánh giá cao`, `Nhiều sân nhất`); đặt lịch theo ma trận giờ trực tuyến từng cụm sân (`/Venues/{id}`) với giữ chỗ tạm thời 2 phút qua SignalR; chọn thanh toán **Tại sân** hoặc **Chuyển khoản VietQR** (kèm mã QR Napas 24/7 & vé điện tử có thể in); quản lý lịch đặt & tự hủy trước giờ chơi tối thiểu 2 tiếng tại `/MyBookings`; cập nhật hồ sơ & đổi mật khẩu tại `/Account/Profile`; gửi đánh giá 5 sao và nhận xét cho cụm sân.
- **Chủ cụm sân (`CourtOwner`)**: tự đăng ký tài khoản đối tác (`/Account/Register?owner=true`), tạo & quản lý nhiều cụm sân, tải lên ảnh bìa / thư viện ảnh thực tế / ảnh từng sân con (`multipart/form-data` kèm kiểm tra magic-byte), xem KPI doanh thu ngày (thực thu check-in vs chờ thu) & doanh thu tháng, **Check-in nhanh 1 chạm theo mã đơn**, bật/tắt trạng thái thanh toán (`Đã thu` ↔ `Chưa thu`), xuất báo cáo CSV chuẩn UTF-8 BOM tại `/Owner` với cơ chế **cô lập dữ liệu theo chủ sở hữu (Multi-Owner Tenant Isolation)**.
- **Quản trị sàn (`Admin`)**: phê duyệt cụm sân mới (`PendingApproval` → `Active`), tạm khóa/mở lại cụm sân (`Suspended` ↔ `Active`), khóa/mở tài khoản người dùng (tự động hủy phiên đăng nhập tức thì & nhả giữ chỗ), theo dõi GMV toàn sàn & **Hoa hồng sàn (10%)**, lọc tìm kiếm người dùng tức thì tại `/Admin` (không tham gia đặt sân, check-in hay chỉnh sửa tài sản đối tác).

---

## 1. Kiến trúc 5 tầng chuẩn PRN222 (5-Layer Architecture)

Solution [`CourtBook.sln`](CourtBook.sln) được tổ chức theo kiến trúc 5 tầng chuẩn PRN222 cùng bộ kiểm thử tự động xUnit trên `.NET 10.0`:

| Tầng | Dự án | Trách nhiệm chính |
| :--- | :--- | :--- |
| **Layer 1: BusinessObjects** | `src/CourtBook.BusinessObjects` | Entities (`AppUser`, `Venue`, `Court`, `Booking`, `VenueReview`), Enums (`UserRole`, `VenueStatus`, `SportType`, `BookingStatus`), quy tắc giờ hoạt động (`Venue.GetOperatingHours`), quản lý ảnh (`Venue.CoverImageUrl`, `Venue.GalleryImageUrls`, `Venue.GetAllPhotos()`, `Court.ImageUrl`), tính giá giờ thường / giờ vàng (`Court.IsPeakHour`, `Court.GetPriceForHour`) và PBKDF2 password hashing (`PasswordHelper`). |
| **Layer 2: DataAccessObjects** | `src/CourtBook.DataAccessObjects` | `CourtBookDbContext` (EF Core 10.0.15 cho SQL Server & SQLite, Filtered Unique Index chống double-booking), Migration SQL Server `InitialCourtBookV6` (`Migrations/`), các DAO (`UserDao`, `VenueDao`, `CourtDao`, `BookingDao` với `TryAddSlotAsync`, `VenueReviewDao`), và `DbInitializer` (tự động migrate/tạo bảng & seed dữ liệu Multi-Owner, 5 ảnh mẫu `/uploads/venues/*.jpg`, đơn mẫu và đánh giá mẫu). |
| **Layer 3: Repositories** | `src/CourtBook.Repositories` | Các interface (`IUserRepository`, `IVenueRepository`, `ICourtRepository`, `IBookingRepository`, `IVenueReviewRepository`) và implementation bọc DAO phục vụ Dependency Injection & Unit Testing. |
| **Layer 4: Services** | `src/CourtBook.Services` | Business logic, DTO, bộ lọc marketplace nâng cao, xuất CSV chuẩn UTF-8 BOM, sinh URL VietQR Napas 24/7, kiểm tra URL ảnh (`ImageUrlValidator`), kiểm tra tài khoản hoạt động (`ActorGuard`), phân quyền chặt theo vai trò (`AuthService`, `VenueService`, `CourtService`, `BookingService`, `VenueReviewService`, `PlatformAdminService`), khóa đồng bộ slot (`SemaphoreSlim`), tự động nhả giữ chỗ hết hạn, và phát sự kiện real-time qua `ISlotRealtimeNotifier`. |
| **Layer 5: Presentation (Web)** | `src/CourtBook.Web` | Razor Pages (`Index`, `Venues/Details`, `MyBookings/Index`, `Owner/Index`, `Admin/Index`, `Account/Login`, `Account/Register`, `Account/Profile`, `Account/AccessDenied`), dịch vụ lưu ảnh multipart (`IPhotoStorageService` / `LocalPhotoStorageService`), SignalR Hub (`/hubs/court`), `SignalRSlotRealtimeNotifier`, `ExpiredHoldCleanupWorker`, Cookie Auth với `OnValidatePrincipal`, CSS & JS client (`wwwroot/css/site.css`, `wwwroot/js/courtbook.js`, `wwwroot/uploads/venues/`). |
| **Tests** | `tests/CourtBook.Tests` | **66 kiểm thử tự động xUnit** (100% Passed) bao phủ `DbInitializer`, đăng ký/đăng nhập, cập nhật hồ sơ & đổi mật khẩu, đánh giá 5 sao, VietQR (`VietQrAndPricingTests`), CSV export, check-in nhanh, hoa hồng sàn 10%, ảnh cụm sân/sân con (`VenueAndCourtPhotoTests`), giữ chỗ đồng thời (`Task.WhenAll`), hết hạn TTL 2 phút, cô lập chủ sân, 6 quy tắc đảm bảo nghiệp vụ và phân tách 3 vai trò (`BusinessRulesHardeningTests`, `NewFeaturesTests`). |

---

## 2. Tính năng nổi bật

1. **Marketplace Photo-First & Bộ lọc/Sắp xếp nâng cao (`/`):**
   - Thanh tìm kiếm 4 phân vùng (`.cb-search-pill`): **Từ khóa** (tên cụm sân / địa chỉ), **Khu vực**, **Môn thể thao** (`Pickleball` / `Cầu lông`), và **Sắp xếp** (`Nổi bật`, `Giá tăng dần`, `Giá giảm dần`, `Đánh giá cao`, `Nhiều sân nhất`), kèm dãy chip lọc nhanh.
   - Thẻ cụm sân hiển thị ảnh bìa tỷ lệ `16:10`, huy hiệu số ô trống trong ngày, số lượng ảnh thư viện, điểm sao trung bình & tổng lượt đánh giá, tên chủ sân và giá khởi điểm.
2. **Đặt sân Real-time, VietQR Napas 24/7 & Đánh giá 5 sao (`/Venues/{id}` & `/MyBookings`):**
   - Lưới ảnh Bento (`.cb-venue-gallery`), thumbnail từng sân con (`.cb-court-thumb`) và modal Lightbox (`#cbLightbox`).
   - Ma trận khung giờ trực tuyến đồng bộ theo nhóm `venue-{venueId}` qua SignalR (`/hubs/court`), giữ chỗ tạm thời 2 phút (tối đa 6 ô/người/cụm sân), thanh `#holdStickyBar` chọn phương thức thanh toán **Tại sân** hoặc **Chuyển khoản VietQR**.
   - Trang `/MyBookings` tích hợp chi tiết **VietQR Napas 24/7** + **Vé điện tử có thể in** trên các đơn `Confirmed` / `CheckedIn`, cùng ngưỡng tự hủy trước ≥ 2 giờ.
   - Khu vực **Đánh giá & Nhận xét 5 sao** (`VenueReviews`), ẩn form gửi đánh giá khi người xem là `Admin` hoặc `CourtOwner` của chính cụm sân đó.
3. **Hồ sơ cá nhân (`/Account/Profile`):**
   - Hiển thị thống kê hoạt động cá nhân (Tổng lượt đặt sân, Lịch sắp tới, Tổng chi tiêu — tự động ẩn với `Admin`), cập nhật Họ tên / Số điện thoại và Đổi mật khẩu PBKDF2.
4. **Kênh Chủ sân (`/Owner`):**
   - Tải ảnh bìa (`CoverPhoto`), thư viện ảnh thực tế (`GalleryPhotos`, tối đa 8 ảnh) và ảnh từng sân con (`CourtPhoto`) với vùng kéo thả, preview cục bộ và magic-byte validation (`5MB/tệp`, `.jpg`/`.jpeg`/`.png`/`.webp`).
   - 5 thẻ KPI trực quan gồm **Doanh thu tháng này**, **Thực thu hôm nay (Check-in)** kèm **Chờ thu**, công suất lấp đầy và giữ chỗ live.
   - Thanh **Check-in nhanh** theo `BookingCode`, nút bật/tắt trạng thái thanh toán (`Đã thu` ↔ `Chưa thu`) cho đơn `Confirmed`, và nút **Xuất báo cáo CSV** chuẩn UTF-8 BOM.
5. **Bảng điều khiển Quản trị sàn (`/Admin`):**
   - 5 thẻ KPI toàn sàn gồm **Hoa hồng sàn (10%)** (ngày & tích lũy toàn thời gian), phân tách thực thu vs chờ thu, phê duyệt / tạm khóa cụm sân, khóa / mở tài khoản và thanh tìm kiếm lọc người dùng real-time (`filterUsersTable()`).

---

## 3. Cấu hình Cơ sở dữ liệu: Dual Database (`Auto`: SQL Server 2022 & SQLite Fallback)

Cấu hình tại `src/CourtBook.Web/appsettings.json` (`"DatabaseProvider": "Auto"`) và `src/CourtBook.Web/Program.cs`:
- **Microsoft SQL Server 2022 (Ưu tiên khi `127.0.0.1:1433` sẵn sàng):**
  - Chạy SQL Server bằng Docker Compose:
    ```bash
    docker compose up -d
    ```
  - Hệ thống tự phát hiện cổng `1433`, sử dụng SQL Server, chạy migration `InitialCourtBookV6` (`src/CourtBook.DataAccessObjects/Migrations/`) và seed dữ liệu mẫu.
  - Script SQL idempotent cho SQL Server được lưu sẵn tại [`database/CourtBook_Schema.sql`](database/CourtBook_Schema.sql).
- **SQLite Fallback tự động (`CourtBook.db`):**
  - Nếu cổng SQL Server `1433` không phản hồi sau `500ms` (hoặc đặt `"DatabaseProvider": "Sqlite"`), ứng dụng tự động chuyển sang SQLite (`CourtBook.db`), khởi tạo đầy đủ bảng (gồm `VenueReviews`, `PaymentMethod`, `IsPaid`) và seed dữ liệu để chạy ngay không cần Docker.

---

## 4. Hướng dẫn Build, Chạy Tests & Khởi chạy Web App

### Bước 1: Build toàn bộ Solution (Release)
```bash
dotnet build CourtBook.sln --configuration Release
```
*(Kết quả mong đợi: `0 Warning(s), 0 Error(s)`)*

### Bước 2: Chạy toàn bộ 66 Unit & Integration Tests (xUnit)
```bash
dotnet test CourtBook.sln --configuration Release --no-build
```
*(Kết quả mong đợi: `Passed: 66, Failed: 0, Skipped: 0`)*

### Bước 3: Khởi chạy ứng dụng Web
```bash
dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj --configuration Release --urls http://localhost:5050
```
Mở trình duyệt truy cập: **`http://localhost:5050`**

*(Nếu muốn tạo lại migration EF Core với SQL Server đang chạy trên Docker:)*
```bash
dotnet ef migrations add InitialCourtBookV6 --project src/CourtBook.DataAccessObjects --startup-project src/CourtBook.Web --output-dir Migrations
dotnet ef migrations script --idempotent -o database/CourtBook_Schema.sql --project src/CourtBook.DataAccessObjects --startup-project src/CourtBook.Web
```

---

## 5. Danh sách Tài khoản Demo & Cụm sân mẫu

Tại `/Account/Login`, hệ thống có sẵn các nút đăng nhập nhanh 1 chạm:

| Vai trò (`UserRole`) | Họ tên | Email | Mật khẩu | Chức năng chính |
| :--- | :--- | :--- | :--- | :--- |
| **Member** (`1`) | Nguyễn Văn An | `an@courtbook.vn` | `User@123` | Khám phá sân, lọc giá/sao, đặt sân ma trận 16h, VietQR, `/MyBookings`, `/Account/Profile`, gửi đánh giá |
| **Member** (`1`) | Trần Thị Bình | `binh@courtbook.vn` | `User@123` | Tab ẩn danh thứ 2 kiểm tra SignalR giữ chỗ đồng thời giữa 2 người chơi |
| **CourtOwner** (`2`) | Lê Minh Khôi | `khoi.owner@courtbook.vn` | `Owner@123` | Quản trị `Thảo Điền Racquet Club` & `Sala Riverside`, tải ảnh, doanh thu tháng, check-in nhanh, đổi trạng thái TT, xuất CSV |
| **CourtOwner** (`2`) | Phạm Thu Hà | `ha.owner@courtbook.vn` | `Owner@123` | Quản trị `Phú Mỹ Hưng Sport Center` & `Tân Bình Arena` |
| **Admin** (`3`) | Quản trị sàn CourtBook | `admin@courtbook.vn` | `Admin@123` | Bảng điều khiển `/Admin`: duyệt/khóa cụm sân, khóa/mở tài khoản, GMV & hoa hồng 10%, tìm kiếm người dùng |

### Cụm sân mẫu được seed tự động

| ID | Tên cụm sân | Khu vực | Chủ sân | Đánh giá mẫu | Trạng thái | Số sân con |
| :-: | :--- | :--- | :--- | :-: | :--- | :--- |
| `1` | **Thảo Điền Racquet Club** | TP. Thủ Đức | Lê Minh Khôi | ⭐ 5.0 (2 đánh giá) | `Active` | 6 sân (4 Pickleball, 2 Cầu lông) |
| `2` | **Sala Riverside Pickleball** | TP. Thủ Đức | Lê Minh Khôi | ⭐ 5.0 (1 đánh giá) | `Active` | 4 sân (4 Pickleball) |
| `3` | **Phú Mỹ Hưng Sport Center** | Quận 7 | Phạm Thu Hà | ⭐ 4.0 (1 đánh giá) | `Active` | 5 sân (2 Pickleball, 3 Cầu lông) |
| `4` | **Tân Bình Arena** | Quận Tân Bình | Phạm Thu Hà | Chưa có đánh giá | `PendingApproval` | 3 sân (3 Pickleball — chờ Admin duyệt) |

---

## 6. Đảm bảo Nghiệp vụ (6 Hardened Business Rules) & Phân tách 3 Vai trò

1. **Từ chối phiên tức thì & thu hồi giữ chỗ khi khóa tài khoản (`IsActive`):**
   - `CookieAuthenticationEvents.OnValidatePrincipal` kiểm tra `IsActive` & `Role` trên mỗi request; tài khoản bị khóa bị `RejectPrincipal()` + `SignOutAsync` ngay lập tức (`401` JSON cho AJAX / redirect đăng nhập cho trang Razor).
   - Mọi thao tác trong `BookingService`, `VenueService`, `CourtService` và `VenueReviewService` kiểm tra `ActorGuard.GetActiveActorAsync`; khi Admin khóa tài khoản, toàn bộ `Held` đang hoạt động của tài khoản đó tự động chuyển sang `Expired` và phát `SlotsUpdated`.
2. **Check-in đúng ngày chơi (`PlayDate == todayVn`):**
   - Cả `CheckInBookingAsync` và `QuickCheckInByCodeAsync` chỉ cho phép check-in khi `PlayDate` trùng ngày hiện tại theo giờ Việt Nam (`UTC+7`) và đồng bộ `IsPaid = true`.
3. **Ngưỡng tự hủy lịch trước 2 giờ (`MemberCancelLeadHours = 2`):**
   - Người chơi chỉ được tự hủy đơn `Confirmed` khi còn ít nhất 2 tiếng trước giờ bắt đầu; đơn sát giờ ẩn nút hủy và chỉ `CourtOwner` sở hữu cụm sân có quyền hủy ngoại lệ.
4. **Tách bạch Thực thu (`CheckedIn`), Chờ thu (`Confirmed`), Doanh thu tháng & Hoa hồng 10%:**
   - `/Owner` tách rõ thực thu check-in, chờ thu, tổng đặt ngày và doanh thu tháng; `/Admin` theo dõi thêm hoa hồng sàn 10% theo ngày và tích lũy toàn thời gian.
5. **Giới hạn đặt trước 30 ngày, kiểm tra giờ khi xác nhận & biên giá sân:**
   - Giới hạn đặt trước tối đa `30` ngày (`MaxAdvanceBookingDays`), kiểm tra lại giờ bắt đầu khi xác nhận đơn, và ràng buộc giá sân con trong khoảng `10.000đ – 5.000.000đ` với `PeakPricePerHour >= BasePricePerHour`.
6. **Nhả giữ chỗ tức thì & cảnh báo đơn sắp tới khi bảo trì / đổi giờ / khóa cụm sân:**
   - Khi chuyển sân sang bảo trì, thu hẹp giờ hoạt động hoặc tạm khóa cụm sân, toàn bộ `Held` bị ảnh hưởng tự động hết hạn + phát SignalR, đồng thời `ServiceResult.WarningMessage` cảnh báo chủ sân nếu có đơn `Confirmed` sắp tới.
7. **Phân tách trách nhiệm 3 vai trò (`Admin` vs `CourtOwner` vs `Member`):**
   - **`Admin`**: chỉ điều hành sàn tại `/Admin` và chỉnh sửa hồ sơ cá nhân tại `/Account/Profile`; bị chặn `403` / `AccessDenied` ở `/Owner` và `/MyBookings`, bị từ chối ở tầng Service khi tự đặt sân, gửi đánh giá, check-in, đổi thanh toán hoặc sửa cụm sân đối tác.
   - **`CourtOwner`**: chỉ quản lý, check-in, đổi trạng thái thanh toán, xuất CSV và sửa sân thuộc sở hữu (`venue.OwnerId == actorUserId`); bị chặn tự đặt sân hoặc tự đánh giá trên chính cụm sân mình sở hữu (*anti-self-dealing*), chỉ đặt lịch được ở cụm sân của chủ khác.
   - **`Member`**: khám phá (`/`), đặt lịch & đánh giá tại `/Venues/{id}`, quản lý vé tại `/MyBookings`, bị chặn truy cập `/Owner` và `/Admin`.

---

## 7. Cấu trúc thư mục dự án

```text
PRN222_ASS/
├── CourtBook.sln
├── Directory.Build.props
├── docker-compose.yml
├── README.md
├── database/
│   └── CourtBook_Schema.sql              # Script DDL SQL Server idempotent (InitialCourtBookV6)
├── src/
│   ├── CourtBook.BusinessObjects/        # Entities, Enums, Security (PasswordHelper)
│   ├── CourtBook.DataAccessObjects/      # DbContext, Migrations (InitialCourtBookV6), DAOs, DbInitializer
│   ├── CourtBook.Repositories/           # Repository interfaces & implementations
│   ├── CourtBook.Services/               # Domain services, DTOs, VietQR, CSV Export, Validation
│   └── CourtBook.Web/                    # Razor Pages, SignalR Hub, Workers, Static files & Uploads
└── tests/
    └── CourtBook.Tests/                  # 66 xUnit unit & integration tests
```

---

## 8. Đội ngũ phát triển (Contributors)
- **tphuy227266-star** (`tphuy227266@gmail.com`)
- **nhaq0505** (`anhquan050503@gmail.com`)
