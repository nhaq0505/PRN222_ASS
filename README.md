# SplitTrip — Nền tảng Lập Lịch Trình Nhóm, Tách Hóa Đơn AI & Tất Toán Công Nợ Tối Ưu VietQR

> **Đồ án môn học PRN222** — Xây dựng trên nền tảng **.NET 10.0**, **ASP.NET Core Razor Pages**, **Entity Framework Core 10 (SQL Server)**, **SignalR Real-time**, **BackgroundService**, **Google Gemini 2.5 Flash AI**, và **Napas VietQR**.

---

## 1. Giới thiệu tổng quan

Khi đi du lịch theo nhóm, việc thống nhất lịch trình nhiều ngày, ghi chép hàng chục khoản chi tiêu ứng trước chéo nhau (homestay, vé xe, vé tham quan, ăn uống tách món kèm VAT/giảm giá) và đòi nợ sau chuyến đi thường gây nhầm lẫn và mất thời gian.

**SplitTrip** giải quyết trọn vẹn vòng đời chuyến đi nhóm từ lúc lên kế hoạch đến khi tất toán hoàn tất `0đ`:
- **Lập lịch trình cộng tác thời gian thực (`Live Itinerary`)**: Nhiều thành viên cùng thêm điểm đến, kéo-thả sắp xếp thứ tự giữa các ngày, bình chọn `Upvote`/`Downvote` trực tiếp qua `SignalR`, và nhờ **Gemini 2.5 Flash AI** gợi ý lịch trình theo ngân sách.
- **Quản lý chi tiêu & Quét hóa đơn AI nhận món trực tiếp (`Smart Expenses`)**: Hỗ trợ 5 chế độ chia tiền (`Chia đều`, `Số tiền chính xác`, `Phần trăm %`, `Trọng số phần`, và `Tách món AI OCR`), kiểm soát sai lệch số học ở bước duyệt nháp (`DraftReview`) và mở bảng cho từng thành viên bấm nhận món mình đã ăn theo thời gian thực (`ClaimingOpen -> Locked`) với phân bổ `VAT/Phí + Giảm giá` theo tỷ lệ pro-rata chính xác từng đồng VNĐ.
- **Tối ưu dòng tiền đồ thị `Min-Cash-Flow` & Tất toán `VietQR` (`Debt Settlement`)**: Gom toàn bộ nợ chéo phức tạp giữa `M` thành viên về tối đa `M - 1` giao dịch chuyển khoản, sinh mã QR ngân hàng Napas tự động điền sẵn số tiền & nội dung, xác nhận thanh toán 2 chiều (`MarkedPaid -> Confirmed / Disputed`) và tự động chuyển trạng thái chuyến đi sang `Completed` ngay khi toàn đoàn cân bằng `0đ`.

---

## 2. Kiến trúc phân tầng chuẩn PRN222 (5-Layer Architecture)

Dự án tuân thủ nghiêm ngặt kiến trúc **5 tầng tách biệt** của học phần PRN222 (cùng dự án kiểm thử tự động `xUnit`):

```text
SplitTrip.sln
├── src/
│   ├── SplitTrip.Web                 [Presentation Layer]
│   │   ├── Pages/                    Razor Pages (Account, Trips, Itinerary, Expenses, Settlement, Admin)
│   │   ├── Hubs/                     Partial SignalR Hub (TripHub.cs, .Itinerary.cs, .Expenses.cs, .Settlement.cs)
│   │   ├── Workers/                  BackgroundService (DepartureReminderWorker, TripSettlementWorker)
│   │   └── Extensions/               Module DI Wiring (*ModuleExtensions.cs)
│   │         │ (chỉ tham chiếu Services & BusinessObjects — KHÔNG tham chiếu DataAccessObjects)
│   │         ▼
│   ├── SplitTrip.Services            [Business Logic Layer]
│   │   ├── Auth/, Trips/             Xác thực BCrypt, Admin cấu hình, Sổ cái NetBalance, Guard rời nhóm
│   │   ├── Itinerary/                Quản lý lịch trình, bình chọn, GeminiTripPlannerService (+ Smart Fallback)
│   │   ├── Expenses/                 ExpenseSplitCalculator (Largest-Remainder VNĐ), GeminiReceiptScannerService
│   │   ├── Settlements/              MinCashFlowService, VietQrService, SettlementService, AdminAnalyticsService
│   │   └── Seed/                     DemoDataSeeder (tự động tạo chuyến đi mẫu Đà Lạt 3N2Đ idempotent)
│   │         ▼
│   ├── SplitTrip.Repositories        [Repository Layer]
│   │   └── Interfaces & Implementations cô lập hoàn toàn truy vấn dữ liệu khỏi tầng nghiệp vụ
│   │         ▼
│   ├── SplitTrip.DataAccessObjects   [Data Access Layer]
│   │   ├── SplitTripDbContext.cs     EF Core 10 SQL Server, decimal(18,0) VNĐ, DeleteBehavior.Restrict
│   │   ├── Daos/                     Các lớp DAO bất đồng bộ theo từng phân hệ
│   │   └── Migrations/               EF Core InitialCreate Migration
│   │         ▼
│   └── SplitTrip.BusinessObjects     [Domain / Entity Layer]
│       ├── Entities/                 12 POCO Entities thuần (0 phụ thuộc NuGet ngoài)
│       ├── Enums/                    8 Domain Enums (TripStatus, ExpenseSplitMode, ReceiptScanStatus, ...)
│       └── Options/                  Strongly-typed Options (AdminAccountOptions, GeminiOptions)
│
├── database/
│   └── SplitTrip_Schema.sql          Script SQL khởi tạo CSDL SQL Server idempotent
└── tests/
    └── SplitTrip.Tests               55 bài kiểm thử đơn vị & tích hợp xuyên phân hệ (xUnit)
```

---

## 3. Điểm nhấn kỹ thuật nổi bật

| Hạng mục PRN222 | Triển khai thực tế trong SplitTrip |
|---|---|
| **SignalR Real-time (`TripHub`)** | Thiết kế `public partial class TripHub : Hub` quản lý phòng `trip:{tripId}` và kênh cá nhân `user:{userId}`. Mọi thao tác kéo-thả lịch trình, bình chọn, bấm nhận món BBQ, đánh dấu đã chuyển khoản/xác nhận nhận tiền đều cập nhật tức thì trên mọi trình duyệt đang mở. |
| **Background Workers (`IHostedService`)** | 1. **`DepartureReminderWorker`**: Quét mỗi 60 giây tìm hoạt động sắp diễn ra trong vòng `30 phút` tới (giờ Việt Nam `UTC+07:00`), gửi thông báo real-time và đóng dấu `ReminderSentAtUtc` chống gửi lặp.<br/>2. **`TripSettlementWorker`**: Tự động chuyển chuyến đi `Ongoing` đã qua `EndDate` sang `Settling` kèm sinh phác đồ `Min-Cash-Flow`, đồng thời nhắc nợ quá hạn mỗi `24 giờ` qua `LastNudgedAtUtc`. |
| **Thuật toán `Min-Cash-Flow` & Số nguyên VNĐ** | Tiền tệ lưu trữ chuẩn `decimal(18, 0)` (nguyên đồng VNĐ). `ExpenseSplitCalculator` dùng thuật toán **Largest-Remainder** bảo toàn tuyệt đối `∑ OwedAmount == TotalAmount` trên cả 5 chế độ chia. `MinCashFlowService` khử nợ vòng tròn, giảm tới 50%–75% số lần chuyển khoản ngân hàng. |
| **AI Đa phương thức (`Gemini 2.5 Flash`)** | Tích hợp REST `gemini-2.5-flash` cho **AI Trip Planner** (sinh JSON lịch trình theo điểm đến/ngân sách) và **AI Receipt Vision OCR** (đọc ảnh hóa đơn `JPG`/`PNG`/`WEBP`, phát hiện lệch `Tạm tính + VAT - Giảm giá ≠ Tổng cộng`). Có sẵn **Smart Demo Fallback** tiếng Việt hoạt động ngay cả khi để trống `Gemini:ApiKey`. |
| **Napas VietQR & Bắt tay 2 bước** | Sinh URL `img.vietqr.io` chuẩn `compact2` tự động gắn mã BIN ngân hàng Việt Nam (`VCB`, `TCB`, `MB`, `ACB`, `BIDV`...), số tiền và nội dung chuyển khoản chuẩn hóa không dấu. Chỉ giao dịch đã được người nhận bấm `Confirmed` mới trừ vào công nợ gốc. |

---

## 4. Cài đặt & Khởi chạy nhanh (Quick Start)

### Yêu cầu môi trường
- **.NET SDK 10.0** trở lên (`dotnet --version`)
- **SQL Server LocalDB** (`(localdb)\mssqllocaldb` — đi kèm Visual Studio / SQL Server Express) hoặc SQL Server bất kỳ

### Chạy ngay trong 2 lệnh
Từ thư mục gốc của repository (`SplitTrip.sln`):

```powershell
# 1. Biên dịch & chạy toàn bộ 55 bài kiểm thử tự động (Release)
dotnet test SplitTrip.sln --configuration Release

# 2. Khởi chạy ứng dụng Web (tự động chạy EF Migration + nạp dữ liệu mẫu Đà Lạt 3N2Đ nếu DB trống)
dotnet run --project src/SplitTrip.Web
```

Sau khi chạy, mở trình duyệt tại địa chỉ hiển thị trên terminal:
- **HTTP**: `http://localhost:5044`
- **HTTPS** (`dotnet run --project src/SplitTrip.Web --launch-profile https`): `https://localhost:7112`

### Khởi tạo CSDL thủ công qua SQL Script (Tùy chọn)
Nếu sử dụng SQL Server Management Studio (SSMS) thay vì LocalDB mặc định:
1. Chạy script idempotent tại [`database/SplitTrip_Schema.sql`](database/SplitTrip_Schema.sql) trên database đích.
2. Cập nhật `ConnectionStrings:DefaultConnection` trong `src/SplitTrip.Web/appsettings.Development.json` (hoặc User Secrets).
3. Khi khởi động lần đầu với bảng `Users` trống, `DemoDataSeeder` vẫn tự động tạo sẵn 4 tài khoản mẫu và chuyến đi **Đà Lạt Săn Mây 3N2Đ**.

---

## 5. Cấu hình Google Gemini API Key (Tùy chọn)

Mặc định `Gemini:ApiKey` trong [`src/SplitTrip.Web/appsettings.json`](src/SplitTrip.Web/appsettings.json) được để trống (`""`) để đảm bảo **không lưu khóa bí mật trong Git**. Khi chưa cấu hình khóa, cả **AI Trip Planner** và **AI Receipt OCR** đều tự động chuyển sang chế độ **Smart Demo Fallback** tiếng Việt để demo trơn tru không phụ thuộc mạng ngoài.

Để kích hoạt gọi trực tiếp mô hình `gemini-2.5-flash` thật của Google AI Studio, cấu hình qua `.NET User Secrets`:

```powershell
dotnet user-secrets init --project src/SplitTrip.Web
dotnet user-secrets set "Gemini:ApiKey" "AIzaSy_YOUR_GEMINI_API_KEY" --project src/SplitTrip.Web
```

Hoặc đặt biến môi trường trong phiên PowerShell hiện tại:

```powershell
$env:Gemini__ApiKey = "AIzaSy_YOUR_GEMINI_API_KEY"
dotnet run --project src/SplitTrip.Web
```

---

## 6. Tài khoản Demo & Dữ liệu mẫu có sẵn

Trang `/Account/Login` tích hợp sẵn các nút **Điền nhanh tài khoản Demo (1-click)** để giảng viên và nhóm bảo vệ đăng nhập tức thì:

| Vai trò | Email | Mật khẩu | Ngân hàng VietQR đã cấu hình | Ghi chú trong dữ liệu mẫu |
|---|---|---|---|---|
| **System Admin** | `admin@splittrip.vn` | `Admin@123` | *(Tài khoản cấu hình `appsettings.json`)* | Truy cập trang thống kê toàn hệ thống `/Admin` |
| **Trưởng đoàn (Owner)** | `an@splittrip.vn` | `User@123` | Vietcombank (`970436`) — `1029384756` | Chủ chuyến **Đà Lạt Săn Mây 3N2Đ** (`DALAT3N2`), ứng tiền Homestay & BBQ |
| **Thành viên** | `binh@splittrip.vn` | `User@123` | Techcombank (`970407`) — `19038475621018` | Ứng tiền vé xe Limousine khứ hồi (`Equal`) |
| **Thành viên** | `chi@splittrip.vn` | `User@123` | MB Bank (`970422`) — `0987654321` | Ứng tiền combo săn mây & chèo SUP (`Percentage`) |
| **Thành viên** | `dung@splittrip.vn` | `User@123` | ACB (`970416`) — `23456789` | Ứng tiền cà phê Túi Mơ To (`ExactAmount`) |

> **Chuyến đi mẫu có sẵn**: `"Đà Lạt Săn Mây 3N2Đ"` (Mã mời: `DALAT3N2`, trạng thái `Ongoing`) gồm **3 ngày lịch trình** (8 địa điểm + bình chọn), **4 khoản chi đa chế độ** tạo nợ vòng tròn giữa cả 4 thành viên, và **1 hóa đơn BBQ (`ClaimingOpen`)** còn 2 món chưa nhận để trình diễn nhận món trực tiếp!

---

## 7. Kịch bản Demo Bảo Vệ Đồ Án PRN222 trong 3 Phút (2 Trình Duyệt Song Song)

Mở **2 cửa sổ trình duyệt đặt cạnh nhau** (Cửa sổ Trái = Trình duyệt thường; Cửa sổ Phải = Cửa sổ Ẩn danh / Incognito) cùng truy cập `http://localhost:5044/Account/Login`:

### Phút 1 (`0:00 – 1:00`): Đăng nhập 1-Click & Cộng tác Lịch trình Real-time (`SignalR` + `AI`)
1. **Cửa sổ Trái**: Bấm badge **`An (Owner)`** -> Đăng nhập. **Cửa sổ Phải**: Bấm badge **`Bình`** -> Đăng nhập.
2. Cả hai cùng bấm vào chuyến đi **"Đà Lạt Săn Mây 3N2Đ"** -> chuyển sang tab **Lịch trình (`/Trips/Itinerary`)**.
3. Trên **Cửa sổ Phải (Bình)**: Bấm bình chọn `Upvote` hoặc kéo-thả đổi vị trí một điểm tham quan -> quan sát **Cửa sổ Trái (An)** tự động nhảy điểm bình chọn và cập nhật thứ tự tức thì qua `SignalR` mà không cần F5.
4. *(Tùy chọn)* Bấm **Gợi ý lịch trình bằng AI** để xem `GeminiTripPlannerService` tạo gợi ý điểm đến kèm chi phí dự kiến.

### Phút 2 (`1:00 – 2:00`): Quét Hóa đơn AI & Bấm Nhận Món Ăn Trực Tiếp (`Pro-Rata VAT/Discount`)
1. Cả hai chuyển sang tab **Chi tiêu (`/Trips/Expenses`)** -> bấm mở hóa đơn **"Tiệc nướng BBQ Tiệm Nướng Thương Thương"** đang ở trạng thái **`Đang mở chọn món` (`ClaimingOpen`)**.
2. **Cửa sổ Phải (Bình)** bấm nhận món *"Lẩu gà lá é nấm rừng"* (`220.000đ`) và **Cửa sổ Trái (An)** cùng bấm nhận chung món *"Lẩu gà lá é"* + *"Trà đào cam sả"* (`40.000đ`).
3. Chỉ rõ trên màn hình: Avatar người nhận món và bảng **Số tiền thực trả (đã cộng VAT 8% và trừ giảm giá theo tỷ lệ pro-rata)** nhảy đồng bộ ngay lập tức trên cả 2 cửa sổ với tổng đối soát khớp đúng `1.000.000đ`.
4. Trên **Cửa sổ Trái (An - Owner)**: Khi tất cả 5/5 món đã có người nhận, bấm **Khóa & Chốt hóa đơn (`Locked`)**.

### Phút 3 (`2:00 – 3:00`): Tối ưu Nợ `Min-Cash-Flow`, Mã `VietQR` & Dashboard `Admin`
1. Trên **Cửa sổ Trái (An)**: Quay lại tổng quan chuyến đi bấm **Chuyển sang Tất toán (`Ongoing -> Settling`)**, sau đó cả hai mở tab **Tất toán (`/Trips/Settlement`)**.
2. Trình bày chỉ số **Tối ưu dòng tiền `Min-Cash-Flow`**: Từ hàng chục nghĩa vụ nợ chéo giữa 4 thành viên được rút gọn còn tối đa **3 lệnh chuyển khoản** bảo toàn tổng số dư bằng `0đ`.
3. Trên **Cửa sổ Phải (người nợ)**: Bấm ** Thanh toán VietQR** để mở mã QR Napas tự động điền số tài khoản & nội dung -> bấm **Đánh dấu đã chuyển tiền (`MarkedPaid`)**.
4. Trên **Cửa sổ Trái (người nhận)**: Nhận thông báo real-time -> bấm **Xác nhận đã nhận tiền (`Confirmed`)**. Khi giao dịch cuối cùng được xác nhận, chuyến đi tự động chuyển sang **`Hoàn tất` (`Completed`)**!
5. Đăng xuất 1 cửa sổ, bấm badge **`Admin`** trên `/Account/Login` -> mở **`/Admin`** để trình chiếu Dashboard thống kê tổng chi tiêu theo danh mục, tỷ lệ áp dụng AI OCR/Planner và hiệu suất cắt giảm giao dịch `Min-Cash-Flow` toàn hệ thống.

---

## 8. Các lệnh kiểm thử hữu ích

```powershell
# Chạy toàn bộ unit tests & end-to-end integration tests
dotnet test SplitTrip.sln --configuration Release

# Chỉ chạy kịch bản kiểm thử tích hợp xuyên phân hệ (Wave 3 E2E)
dotnet test SplitTrip.sln --filter "FullyQualifiedName~Integration"

# Kiểm tra biên dịch nghiêm ngặt 0 cảnh báo / 0 lỗi
dotnet build SplitTrip.sln --configuration Release
```
