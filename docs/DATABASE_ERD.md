# Sơ Đồ Cơ Sở Dữ Liệu (Entity-Relationship Diagram - ERD)

```mermaid
erDiagram
    USERS ||--o{ VENUES : "owns (1-N)"
    USERS ||--o{ BOOKINGS : "places (1-N)"
    USERS ||--o{ VENUE_REVIEWS : "writes (1-N)"
    VENUES ||--o{ COURTS : "contains (1-N)"
    VENUES ||--o{ VENUE_REVIEWS : "receives (1-N)"
    COURTS ||--o{ BOOKINGS : "schedules (1-N)"

    USERS {
        int Id PK
        string FullName
        string Email UK
        string PhoneNumber
        string PasswordHash
        int Role "1=Member, 2=Owner, 3=Admin"
        bool IsActive
        datetime CreatedAtUtc
    }

    VENUES {
        int Id PK
        string Slug UK
        string Name
        string District
        string Address
        string PhoneNumber
        string Description
        int OpenHour "0-23"
        int CloseHour "0-23"
        int OwnerId FK
        int Status "1=Pending, 2=Active, 3=Suspended"
        string CoverImageUrl
        string GalleryImageUrls
        datetime CreatedAtUtc
    }

    COURTS {
        int Id PK
        int VenueId FK
        string Code "PB-01, CL-01"
        string Name
        int SportType "1=Pickleball, 2=Badminton"
        string Surface
        bool IsIndoor
        decimal BasePricePerHour
        decimal PeakPricePerHour
        bool IsActive
        int DisplayOrder
        string ImageUrl
    }

    BOOKINGS {
        int Id PK
        string BookingCode UK
        int CourtId FK
        int UserId FK
        date PlayDate
        int StartHour "0-23"
        decimal PriceVnd
        int Status "1=Held, 2=Confirmed, 3=CheckedIn, 4=Cancelled, 5=Expired"
        string PaymentMethod "Tại sân, Chuyển khoản VietQR"
        bool IsPaid
        datetime HoldExpiresAtUtc
        datetime ConfirmedAtUtc
        datetime CheckedInAtUtc
        datetime CancelledAtUtc
        string Notes
        datetime CreatedAtUtc
    }

    VENUE_REVIEWS {
        int Id PK
        int VenueId FK
        int UserId FK
        int Rating "1 to 5 stars"
        string Comment
        datetime CreatedAtUtc
    }
```

### Các chỉ mục quan trọng (Indexes & Constraints):
1. **Filtered Unique Index (Chống trùng giờ đặt):** `[CourtId, PlayDate, StartHour] WHERE Status IN (1, 2, 3)`
2. **Composite Unique Index (Mã sân trong cụm sân):** `[VenueId, Code]`
3. **Unique Email Index:** `[Email]`
