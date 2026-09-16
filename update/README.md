# Công cụ cập nhật TTSK

- `Cap_Nhat_Portable.bat`: build portable, chọn phát hành GitHub hoặc Google Drive.
- `Cai_Dat_Google_Drive.bat`: đăng nhập Google cho máy phát hành, thiết lập chỉ mục.
- `Kiem_Tra_Google_Drive.bat`: kiểm tra đọc công khai và đăng nhập máy phát hành.
- `release-request.json`: số phiên bản đã giữ chỗ và ghi chú phát hành GitHub.
- `release-version-prefix.txt`: nhánh phiên bản major.minor.
- `release-run-offset.txt`: tương thích yêu cầu GitHub cũ.

Các BAT chạy được khi bấm đúp hoặc gọi từ thư mục khác. Script kỹ thuật ở `../scripts`; tài liệu ở `../docs/GOOGLE_DRIVE_UPDATES.md`.

Người dùng cuối không cần tài khoản Google hoặc API key. OAuth chỉ dùng để tải lên và được mã hóa bằng Windows DPAPI ngoài repository.
