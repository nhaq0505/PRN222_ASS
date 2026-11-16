# ADR-003: Tích Hợp Cổng Thanh Toán Động VietQR Napas 24/7

## Bối Cảnh (Context)
Khách đặt sân cần thanh toán nhanh chóng, chính xác mà không phải nhập số tài khoản hoặc số tiền thủ công để tránh nhầm lẫn nội dung đơn đặt.

## Quyết Định (Decision)
Tích hợp chuẩn VietQR Napas 24/7 tạo mã QR động:
1. **Ngân hàng:** MB Bank (BIN Code: `970422`).
2. **Số tài khoản:** `0901976201`.
3. **Cơ chế QR động:** Nhúng tự động số tiền thực tế (`amount`) và nội dung chuyển khoản chuẩn hóa `CB {BookingCode}`.
4. **Khả năng tương thích:** Tương thích 100% với tất cả ứng dụng Mobile Banking của mọi ngân hàng tại Việt Nam.
