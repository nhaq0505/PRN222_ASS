# Hướng Dẫn Triển Khai Hệ Thống (Deployment Guide)

## 1. Yêu Cầu Môi Trường
- .NET 8.0 SDK (hoặc .NET 9.0 SDK tương thích ngược).
- Hệ điều hành: Windows 10/11, macOS, hoặc Ubuntu Linux 20.04+.
- Trình duyệt: Chrome, Edge, Firefox, Cốc Cốc.

## 2. Triển Khai Cục Bộ (Local Development)
```bash
# 1. Restore các package NuGet
dotnet restore CourtBook.sln

# 2. Build dự án
dotnet build CourtBook.sln -c Release

# 3. Chạy 42 kiểm thử tự động
dotnet test CourtBook.sln -c Release

# 4. Khởi chạy ứng dụng Web
dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj
```

## 3. Triển Khai Với Docker & SQL Server 2022
```bash
# Khởi động container SQL Server
docker compose up -d

# Chạy ứng dụng web kết nối tới SQL Server
dotnet run --project src/CourtBook.Web/CourtBook.Web.csproj
```
