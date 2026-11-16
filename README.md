# CourtBook — Sàn Đặt Sân Pickleball & Cầu Lông Đa Chủ Sân (Multi-Owner Marketplace)

**CourtBook** là nền tảng sàn thương mại điện tử đặt sân **Pickleball & Cầu lông** theo mô hình **Multi-Owner Venue Marketplace**, xây dựng bằng **ASP.NET Core Razor Pages (.NET 10)**, **SignalR** và **SQL Server 2022**.

Nền tảng hỗ trợ **3 vai trò độc lập (`Member`, `CourtOwner`, `Admin`)**:
- **Người chơi (`Member`)**: khám phá các cụm sân đang hoạt động theo khu vực, môn thể thao và từ khóa; đặt chỗ theo ma trận lịch trực tuyến từng cụm sân (`/Venues/{id}`) với giữ chỗ tạm thời 2 phút qua SignalR và quản lý vé tại `/MyBookings`.
- **Chủ cụm sân (`CourtOwner`)**: tự đăng ký tài khoản đối tác (`/Account/Register?owner=true`), tạo & quản lý nhiều cụm sân, cấu hình sân con (giá thường / giờ vàng, bảo trì), check-in & hủy đơn riêng cho các cụm sân thuộc quyền sở hữu tại `/Owner` với cơ chế **cô lập dữ liệu theo chủ sở hữu (tenant isolation)**.
- **Quản trị sàn (`Admin`)**: phê duyệt cụm sân mới (`PendingApproval` → `Active`), tạm khóa/mở lại cụm sân (`Suspended` ↔ `Active`), quản lý tài khoản người dùng và theo dõi GMV toàn sàn tại `/Admin`.

---

## 1. Kiến trúc 5 tầng (5-Layer PRN222 Architecture)

Solution [`CourtBook.sln`](CourtBook.sln) được tổ chức theo kiến trúc 5 tầng chuẩn PRN222 cùng bộ kiểm thử tự động xUnit:

| Tầng | Dự án | Trách nhiệm chính |
| :--- | :--- | :--- |
| **Layer 1: BusinessObjects** | `src/CourtBook.BusinessObjects` | Entities (`AppUser`, `Venue`, `Court`, `Booking`), Enums (`UserRole`, `VenueStatus`, `SportType`, `BookingStatus`), giờ hoạt động (`Venue.GetOperatingHours`), danh sách ảnh (`Venue.CoverImageUrl`, `Venue.GalleryImageUrls`, `Venue.GetAllPhotos()`, `Court.ImageUrl`), tính giá giờ thường / giờ vàng (`Court.IsPeakHour`, `Court.GetPriceForHour`) và PBKDF2 hashing (`PasswordHelper`). |
| **Layer 2: DataAccessObjects** | `src/CourtBook.DataAccessObjects` | `CourtBookDbContext` (EF Core 10 + SQL Server), Migration `InitialCourtBookV3` (`Migrations/`), các DAO (`UserDao`, `VenueDao`, `CourtDao`, `BookingDao` với `TryAddSlotAsync`), và `DbInitializer` (tự động migrate & seed dữ liệu Multi-Owner cùng 5 ảnh mẫu `/uploads/venues/*.jpg`). |
| **Layer 3: Repositories** | `src/CourtBook.Repositories` | Các interface (`IUserRepository`, `IVenueRepository`, `ICourtRepository`, `IBookingRepository`) và implementation bọc DAO phục vụ Dependency Injection & Unit Testing. |
| **Layer 4: Services** | `src/CourtBook.Services` | Business logic, DTO, kiểm tra URL ảnh (`ImageUrlValidator`), và kiểm soát phân quyền theo chủ sân (`AuthService`, `VenueService`, `CourtService`, `BookingService`, `PlatformAdminService`), khóa đồng bộ slot (`SemaphoreSlim`), tự động nhả giữ chỗ hết hạn, phát sự kiện real-time qua `ISlotRealtimeNotifier`. |
| **Layer 5: Presentation (Web)** | `src/CourtBook.Web` | Razor Pages (`Index`, `Venues/Details`, `MyBookings/Index`, `Owner/Index`, `Admin/Index`, `Account/*`), dịch vụ lưu ảnh multipart (`IPhotoStorageService` / `LocalPhotoStorageService`), SignalR Hub (`/hubs/court`), `SignalRSlotRealtimeNotifier`, `ExpiredHoldCleanupWorker`, Cookie Auth, CSS & JS client (`wwwroot/js/courtbook.js`, `wwwroot/uploads/venues/`). |
| **Tests** | `tests/CourtBook.Tests` | **35 kiểm thử tự động xUnit** bao phủ `DbInitializer`, đăng ký/đăng nhập theo vai trò, bộ lọc marketplace, vòng đời duyệt cụm sân, cô lập dữ liệu chéo chủ sân (`CourtOwner` tenant isolation), giữ chỗ đồng thời (`Task.WhenAll`), hết hạn TTL 2 phút, bảo toàn `DateTimeKind.Utc`, KPI toàn sàn và validate/lưu trữ ảnh cụm sân & sân con (`VenueAndCourtPhotoTests`). |

---

## 2. Yêu cầu môi trường & Thiết lập Docker SQL Server

### Yêu cầu
- [.NET 10 SDK](https://dotnet.microsoft.com/) (`10.0.x`)
- Docker Desktop / Docker Engine (để chạy SQL Server 2022)
- Node.js (tùy chọn, để kiểm tra cú pháp `courtbook.js`)
- EF Core CLI (`dotnet-ef`, nếu cần chạy lệnh migration thủ công)

### Bước 1: Khởi động SQL Server 2022 bằng Docker
```bash
docker compose up -d
```
Cấu hình trong [`docker-compose.yml`](docker-compose.yml):
- **Container:** `courtbook-sqlserver` (`mcr.microsoft.com/mssql/server:2022-latest`)
- **Host / Port:** `127.0.0.1:1433`
- **Database:** `CourtBookDb` (tự động tạo & migrate khi khởi động ứng dụng)
- **Tài khoản `sa`:** `CourtBook@2026!`

Script SQL idempotent của schema v3 được xuất sẵn tại [`database/CourtBook_Schema.sql`](database/CourtBook_Schema.sql), tương ứng với migration `InitialCourtBookV3` trong `src/CourtBook.DataAccessObjects/Migrations/`. Nếu cần cập nhật database thủ công bằng EF Core CLI:
```bash
dotnet ef database update --project src/CourtBook.DataAccessObjects --startup-project src/CourtBook.Web
```

---

## 3. Build, Kiểm thử & Chạy ứng dụng

```bash
# 1. Build toàn bộ solution (Release)
dotnet build CourtBook.sln --configuration Release

# 2. Chạy toàn bộ 35 unit tests (xUnit)
dotnet test CourtBook.sln --configuration Release

# 3. Kiểm tra cú pháp JavaScript client (tùy chọn)
node --check src/CourtBook.Web/wwwroot/js/courtbook.js

# 4. Khởi chạy Web App (tự động migrate + seed 5 users, 4 venues, 18 courts, 8 bookings)
dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj
```

Sau khi chạy, mở trình duyệt tại URL hiển thị trên terminal (mặc định `http://localhost:5000` nếu không chỉ định `--urls`, hoặc truyền `--urls http://127.0.0.1:5050` tùy chọn).

---

## 4. Dữ liệu Seed & Tài khoản Demo (1-Click Demo Login)

Khi khởi chạy trên database trống, `DbInitializer.InitializeAsync` tự động seed **5 tài khoản**, **4 cụm sân** (**18 sân con**) và **8 đơn đặt sân mẫu**. Tại `/Account/Login`, có thể bấm trực tiếp nút **Đăng nhập nhanh 1 chạm** được phân nhóm theo vai trò:

### Tài khoản Demo (5 tài khoản · 3 vai trò)

| Vai trò (`UserRole`) | Họ tên | Email | Mật khẩu | Phạm vi & Quyền hạn |
| :--- | :--- | :--- | :--- | :--- |
| **Member** (`1`) | Nguyễn Văn An | `an@courtbook.vn` | `User@123` | Khám phá sân, giữ chỗ & đặt sân tại `/Venues/{id}`, xem/hủy vé tại `/MyBookings` |
| **Member** (`1`) | Trần Thị Bình | `binh@courtbook.vn` | `User@123` | Khám phá sân, giữ chỗ & đặt sân (dùng mở tab thứ 2 test SignalR real-time) |
| **CourtOwner** (`2`) | Lê Minh Khôi | `khoi.owner@courtbook.vn` | `Owner@123` | Quản lý 2 cụm sân tại `/Owner`: `Thảo Điền Racquet Club` & `Sala Riverside Pickleball` |
| **CourtOwner** (`2`) | Phạm Thu Hà | `ha.owner@courtbook.vn` | `Owner@123` | Quản lý 2 cụm sân tại `/Owner`: `Phú Mỹ Hưng Sport Center` & `Tân Bình Arena` |
| **Admin** (`3`) | Quản trị sàn CourtBook | `admin@courtbook.vn` | `Admin@123` | Quản trị toàn sàn tại `/Admin`: duyệt/tạm khóa/mở lại cụm sân, quản lý tài khoản & GMV |

### Danh sách Cụm sân được Seed (4 cụm sân · 18 sân con)

| ID | Tên cụm sân | Khu vực | Chủ sân | Trạng thái | Số sân con |
| :-: | :--- | :--- | :--- | :--- | :--- |
| `1` | **Thảo Điền Racquet Club** | TP. Thủ Đức | Lê Minh Khôi | `Active` | 6 sân (4 Pickleball, 2 Cầu lông) |
| `2` | **Sala Riverside Pickleball** | TP. Thủ Đức | Lê Minh Khôi | `Active` | 4 sân (4 Pickleball) |
| `3` | **Phú Mỹ Hưng Sport Center** | Quận 7 | Phạm Thu Hà | `Active` | 5 sân (2 Pickleball, 3 Cầu lông) |
| `4` | **Tân Bình Arena** | Quận Tân Bình | Phạm Thu Hà | `PendingApproval` | 3 sân (3 Pickleball — ẩn khỏi `/` cho tới khi Admin duyệt) |

---

## 5. SignalR Cô Lập Theo Cụm Sân & Bảo Toàn Đa Chủ Sở Hữu

1. **Cô lập SignalR theo từng cụm sân (`venue-{venueId}`):**
   - Hub `/hubs/court` (`CourtHub`) cung cấp `JoinVenueGroup(int venueId)` và `LeaveVenueGroup(int venueId)`.
   - Khi người chơi mở trang lịch `/Venues/{id}`, `courtbook.js` tự động tham gia nhóm `venue-{venueId}` sau khi kết nối và tái kết nối (`onreconnected`).
   - `SignalRSlotRealtimeNotifier` chỉ phát `SlotUpdated`, `SlotsUpdated` và `CourtStatusChanged` tới đúng nhóm `Clients.Group($"venue-{venueId}")` — thay đổi ở cụm sân này không làm nhiễu client đang xem cụm sân khác.
2. **Giữ chỗ tạm thời (2 phút), chống Double-Booking & Tự động nhả slot:**
   - Khung giờ mở cửa theo từng cụm sân (`5 <= OpenHour < CloseHour <= 23`, tối thiểu 4 giờ, mặc định `06:00 - 22:00`); **Giờ vàng (17:00 - 21:00)** áp dụng `PeakPricePerHour`.
   - Người chơi click ô trống trên `/Venues/{id}` → gửi `POST /Venues/{id}?handler=ToggleHold` kèm Anti-Forgery Token.
   - Tối đa **6 giữ chỗ đồng thời / người chơi / cụm sân** (`MaxActiveHoldsPerUser = 6`), khóa đồng thời bằng `SemaphoreSlim` ở tầng Service và bảo vệ tuyệt đối ở tầng SQL Server bằng **Filtered Unique Index** `[IX_Bookings_CourtId_PlayDate_StartHour]` trên `(CourtId, PlayDate, StartHour) WHERE [Status] IN (1, 2, 3)`.
   - Mã sân `Code` là duy nhất trong phạm vi từng cụm sân qua composite unique index `(VenueId, Code)` — cho phép hai chủ sân khác nhau đều có mã sân `PB-01`.
   - `ExpiredHoldCleanupWorker` chạy nền định kỳ mỗi **10 giây** gọi `ReleaseExpiredHoldsAsync()` để chuyển các giữ chỗ quá hạn sang `Expired = 5` và phát `SlotsUpdated` theo từng `venueId`.
3. **Bảo vệ ranh giới đa chủ sân (Multi-Owner Tenant Isolation):**
   - Mọi thao tác tại `VenueService`, `CourtService` và `BookingService` (cập nhật cụm sân, thêm/sửa/bảo trì sân con, check-in, hủy đơn của khách) đều kiểm tra `venue.OwnerId == actorUserId || isAdmin`. Chủ sân A không thể xem danh sách thao tác hoặc gửi POST chỉnh sửa cụm sân/sân/đơn của chủ sân B.
   - Các cụm sân mới do `CourtOwner` tạo ở trạng thái `PendingApproval`, chỉ hiển thị công khai trên `/` và cho phép đặt sân tại `/Venues/{id}` sau khi `Admin` phê duyệt (`Active`).

---

## 6. Giao Diện Khám Phá Marketplace & Tải Ảnh Cụm Sân / Sân Con (v3)

1. **Giao diện Khám phá Cụm sân (`/`) theo phong cách Photo-First Marketplace:**
   - Thanh tìm kiếm hợp nhất 3 phân vùng (`.cb-search-pill`) hỗ trợ lọc theo **Từ khóa** (tên cụm sân / địa chỉ), **Khu vực** và **Môn thể thao** (`Pickleball` / `Cầu lông`), đi kèm dãy chip lọc nhanh (`.cb-chip-list`) và bộ đếm cụm sân / sân đang mở trực tuyến.
   - Thẻ cụm sân `.cb-venue-card` hiển thị ảnh bìa tỷ lệ `16:10` (hoặc SVG sân thể thao khi chưa có ảnh), huy hiệu số ô trống trong ngày, số lượng ảnh thư viện, khu vực/giờ mở cửa, badge số sân theo môn, tên chủ sân và mức giá khởi điểm (`Từ ...đ/giờ`).
2. **Thư viện ảnh Bento & Lightbox phóng to (`/Venues/{id}` & `/Admin`):**
   - Trang đặt lịch `/Venues/{id}` hiển thị lưới ảnh Bento `.cb-venue-gallery` (1 ảnh bìa chính + tối đa 2 ảnh phụ) ngay phía trên ma trận lịch 16 khung giờ và ảnh thumbnail `.cb-court-thumb` cạnh từng tên sân con.
   - Trang quản trị `/Admin` hiển thị thumbnail `.cb-admin-thumb` cho từng cụm sân trong bảng phê duyệt. Mọi ảnh gắn `data-lightbox-src` mở modal `#cbLightbox` (hỗ trợ đóng bằng nút đóng, click nền hoặc phím `Escape`).
3. **Tải ảnh Multipart & Kiểm tra bảo mật tệp tin (`/Owner`):**
   - Trang quản lý chủ sân `/Owner` hỗ trợ biểu mẫu `multipart/form-data` với vùng kéo thả `[data-dropzone]`, xem trước ảnh cục bộ trước khi gửi (`[data-upload-preview]`) và xóa ảnh đã lưu (`[data-remove-existing-photo]`):
     - **Cụm sân (`CreateVenue` / `UpdateVenue`):** hỗ trợ **1 ảnh bìa** (`CoverPhoto`) và **thư viện ảnh thực tế** (`GalleryPhotos`, tối đa 6 ảnh khi tạo mới; tối đa 8 ảnh trong thư viện). Nếu tạo cụm sân mới chưa chọn ảnh bìa riêng, ảnh đầu tiên của thư viện sẽ tự động được dùng làm ảnh bìa.
     - **Sân con (`SaveCourt`):** hỗ trợ **1 ảnh thực tế** (`CourtPhoto`) và cờ xóa ảnh (`RemoveCourtPhoto`).
   - `LocalPhotoStorageService` lưu tệp vào `wwwroot/uploads/venues/` và `wwwroot/uploads/courts/` dưới tên `<guid>.<ext>`, giới hạn **5MB/tệp**, chỉ nhận phần mở rộng `.jpg`, `.jpeg`, `.png`, `.webp`, đồng thời kiểm tra **magic-byte header** để từ chối các tệp giả mạo phần mở rộng.
   - Tầng Service kiểm tra bổ sung qua `ImageUrlValidator`: chỉ chấp nhận đường dẫn trống, `/uploads/...` hoặc `https://...`, chặn `javascript:`, `data:`, `http://` và đường dẫn traversal.
   - Dữ liệu seed mặc định đính kèm sẵn **5 ảnh `.jpg` thực tế** trong `src/CourtBook.Web/wwwroot/uploads/venues/` cho cả 4 cụm sân và 18 sân con.
