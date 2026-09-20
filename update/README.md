# Công cụ cập nhật TTSK

- `Luu_Tru_Code.bat`: lưu code, không build, không tăng version, không tạo release.
- `Cap_Nhat_Portable.bat`: build portable, chọn phát hành GitHub hoặc Google Drive.
- `Cai_Dat_Google_Drive.bat`: đăng nhập Google cho máy phát hành, thiết lập chỉ mục.
- `Kiem_Tra_Google_Drive.bat`: kiểm tra đọc công khai và đăng nhập máy phát hành.
- `release-request.json`: số phiên bản đã giữ chỗ và ghi chú phát hành GitHub.
- `release-version-prefix.txt`: nhánh phiên bản major.minor.
- `release-run-offset.txt`: tương thích yêu cầu GitHub cũ.

Các BAT chạy được khi bấm đúp hoặc gọi từ thư mục khác. Script kỹ thuật ở `../scripts`; tài liệu ở `../docs/GOOGLE_DRIVE_UPDATES.md`.

Người dùng cuối không cần tài khoản Google hoặc API key. OAuth chỉ dùng để tải lên và được mã hóa bằng Windows DPAPI ngoài repository.

## 1. Lưu trữ code để tiếp tục phát triển

Bấm `Luu_Tru_Code.bat`, chọn GitHub hoặc Google Drive. Công cụ commit các thay đổi source local (kể cả file mới không bị `.gitignore` bỏ qua). Hãy kiểm tra thay đổi trước khi chạy. Thay đổi trong `portable` và `release-request.json` được giữ lại cho luồng phát hành; nếu chúng đang staged, cần unstage trước.

- GitHub: push vào nhánh `backup/<nhánh hiện tại>`, ví dụ `backup/main`. Nhánh local vẫn giữ nguyên. Không force push; nếu lịch sử remote bị phân kỳ, cần xử lý trước khi thử lại.
- Google Drive: tự lưu vào [thư mục lưu trữ code đã chọn](https://drive.google.com/drive/folders/1PKjvRjjE3yJQo1rbxN-O3XCE9O77fuNF), không cần nhập lại ID. Dùng tài khoản đã đăng nhập bằng `Cai_Dat_Google_Drive.bat` và có quyền ghi vào thư mục này. ZIP chứa snapshot của commit (bao gồm những file portable đã có trong commit), không chứa lịch sử `.git`, file ignored hay thay đổi chưa commit. Không dùng thư mục release công khai. Công cụ kiểm tra kích thước/checksum sau upload và không sửa chỉ mục cập nhật người dùng.

Ví dụ: `Luu_Tru_Code.bat -Destination GitHub -CommitMessage "Luu tien do" -NoPause`.
Drive: `Luu_Tru_Code.bat -Destination GoogleDrive -NoPause`.
Nếu muốn lưu vào thư mục khác cho một lần chạy, thêm `-DriveFolderId "ID_THU_MUC_KHAC"`.

Lưu code **không cần chỉ mục `latest.json`**, không cần thiết lập bản release và không cần chia sẻ công khai thư mục. Thư mục rỗng vẫn lưu được. Trước khi commit/upload, công cụ kiểm tra thư mục tồn tại, không nằm trong thùng rác và tài khoản Google có quyền thêm file. Sau upload, công cụ đối chiếu kích thước và checksum ZIP rồi mới báo thành công.

Kiểm tra riêng kết nối lưu code, không commit hay upload: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SourceDriveFolder.ps1` (chạy từ thư mục gốc dự án).

## 2. Phát hành cho người dùng

Chạy `Cap_Nhat_Portable.bat` như trước, chọn Yes, nơi phát hành và nhập ghi chú. GitHub chỉ tự phát hành trên `main` khi `update/release-request.json` thay đổi; push code hoặc portable thông thường không kích hoạt release. Có thể chạy workflow thủ công để thử lại yêu cầu phát hành. Drive vẫn đóng gói, xác minh và cập nhật `latest.json` như trước.

Thay đổi workflow cần được đưa lên GitHub để áp dụng cho các lần push trực tiếp vào `main`. Luồng lưu trữ dùng nhánh `backup/` nên không kích hoạt workflow release hiện tại ngay cả trước bước đó. Khi sẵn sàng phát hành, chạy luồng release từ nhánh `main` có code cần phát hành.
