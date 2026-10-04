# Công cụ cập nhật TTSK

- `Luu_Tru_Code.bat`: lưu code, không build, không tăng version, không tạo release.
- `Cap_Nhat_Portable.bat`: build portable, chọn phát hành GitHub hoặc Google Drive.
- `Cai_Dat_Google_Drive.bat`: đăng nhập Google cho máy phát hành, thiết lập chỉ mục.
- `Kiem_Tra_Google_Drive.bat`: kiểm tra đọc công khai và đăng nhập máy phát hành.
- `release-request.json`: số phiên bản đã giữ chỗ và ghi chú phát hành GitHub.
- `release-version-prefix.txt`: nhánh phiên bản major.minor.
- `release-series.json`: đợt chính thức từ v1.0.0 và định danh gói test Drive cũ được bỏ qua; giữ nguyên sau khi phát hành.
- `release-run-offset.txt`: tương thích yêu cầu GitHub cũ.

Các BAT chạy được khi bấm đúp hoặc gọi từ thư mục khác. Script kỹ thuật ở `../scripts`; tài liệu ở `../docs/GOOGLE_DRIVE_UPDATES.md`.

Người dùng cuối không cần tài khoản Google hoặc API key. OAuth chỉ dùng để tải lên và được mã hóa bằng Windows DPAPI ngoài repository.

## 1. Lưu trữ code để tiếp tục phát triển

Bấm `Luu_Tru_Code.bat`, chọn GitHub hoặc Google Drive. Công cụ commit các thay đổi source local (kể cả file mới không bị `.gitignore` bỏ qua). Hãy kiểm tra thay đổi trước khi chạy. Thay đổi trong `portable` và `release-request.json` được giữ lại cho luồng phát hành; nếu chúng đang staged, cần unstage trước.

- GitHub: push vào nhánh `backup/<nhánh hiện tại>`, ví dụ `backup/main`. Nhánh local vẫn giữ nguyên. Không force push; nếu lịch sử remote bị phân kỳ, cần xử lý trước khi thử lại.
- Google Drive: tự lưu vào [thư mục lưu trữ code đã chọn](https://drive.google.com/drive/folders/1PKjvRjjE3yJQo1rbxN-O3XCE9O77fuNF), không cần nhập lại ID. Dùng tài khoản đã đăng nhập bằng `Cai_Dat_Google_Drive.bat` và có quyền ghi vào thư mục này. ZIP chứa snapshot của commit (bao gồm những file portable đã có trong commit), không chứa lịch sử `.git`, file ignored hay thay đổi chưa commit. Không dùng thư mục release công khai. Công cụ kiểm tra kích thước/checksum sau upload và không sửa chỉ mục cập nhật người dùng.

Ví dụ: `Luu_Tru_Code.bat -Destination GitHub -CommitMessage "Luu tien do" -NoPause`.
Khi chọn Google Drive, nhập tên tệp lưu trữ theo ý muốn (hỗ trợ tiếng Việt và khoảng trắng). Công cụ tự thêm `.zip` nếu chưa có; bấm Enter để giữ tên tự động gồm thời gian và mã commit. Chỉ nhập tên tệp, không nhập đường dẫn.
Drive với tên tùy chọn: `Luu_Tru_Code.bat -Destination GoogleDrive -ArchiveName "Ban luu tru hom nay" -NoPause`.
Với `-NoPause`, công cụ không hỏi tên; nếu không truyền `-ArchiveName` thì dùng tên tự động.
Phải truyền `-Destination` khi dùng `-NoPause`; thiếu nơi lưu sẽ báo lỗi ngay, không chờ nhập.

Mỗi lần lưu Drive tạo thư mục ZIP local riêng tại `.codex-artifacts/portable-update/source-<GUID>/`, nên tên trùng không ghi đè bản local trước. Drive tạo file mới mỗi lần chạy, kể cả khi tên trùng. Nếu upload hoặc xác minh thất bại, công cụ báo commit và đường dẫn ZIP còn giữ lại để kiểm tra/thử lại; không tự reset commit. Upload có thể đã tới Drive dù kiểm tra sau đó lỗi, nên kiểm tra thư mục Drive trước khi chạy lại nếu muốn tránh bản trùng. Có thể dọn các ZIP local sau khi đã xác minh bản lưu và lệnh đã kết thúc.

Công cụ chỉ chạy tại gốc Git repository và chặn merge/rebase/cherry-pick/revert đang dở, cũng như khóa của luồng cập nhật/lưu trữ khác. Các thao tác Git thủ công vẫn cần thực hiện sau khi lệnh lưu trữ kết thúc.
Kiểm thử hồi quy offline: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SourceBackup.ps1`. Bộ thử dùng repository tạm, remote Git local và Drive giả; không upload hay commit dự án thật.

Nếu Google từ chối phiên đã lưu (`invalid_grant`), chạy BAT tương tác sẽ tự mở đăng nhập lại bằng OAuth client đã lưu. Xác nhận trên trang Google rồi công cụ tiếp tục lưu trữ. Chế độ `-NoPause` báo lỗi và dừng, không mở trình duyệt; có thể chạy `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Connect-GoogleDrivePublisher.ps1` để đăng nhập lại trước. Lỗi mạng hoặc OAuth client không hợp lệ được báo riêng, không tự lặp đăng nhập.
Nếu muốn lưu vào thư mục khác cho một lần chạy, thêm `-DriveFolderId "ID_THU_MUC_KHAC"`.

Lưu code **không cần chỉ mục `latest.json`**, không cần thiết lập bản release và không cần chia sẻ công khai thư mục. Thư mục rỗng vẫn lưu được. Trước khi commit/upload, công cụ kiểm tra thư mục tồn tại, không nằm trong thùng rác và tài khoản Google có quyền thêm file. Sau upload, công cụ đối chiếu kích thước và checksum ZIP rồi mới báo thành công.

Kiểm tra riêng kết nối lưu code, không commit hay upload: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-SourceDriveFolder.ps1` (chạy từ thư mục gốc dự án).

## 2. Phát hành cho người dùng

Chạy `Cap_Nhat_Portable.bat` như trước, chọn Yes, nơi phát hành và nhập ghi chú. GitHub chỉ tự phát hành trên `main` khi `update/release-request.json` thay đổi; push code hoặc portable thông thường không kích hoạt release. Có thể chạy workflow thủ công để thử lại yêu cầu phát hành. Drive vẫn đóng gói, xác minh và cập nhật `latest.json` như trước.

Thay đổi workflow cần được đưa lên GitHub để áp dụng cho các lần push trực tiếp vào `main`. Luồng lưu trữ dùng nhánh `backup/` nên không kích hoạt workflow release hiện tại ngay cả trước bước đó. Khi sẵn sàng phát hành, chạy luồng release từ nhánh `main` có code cần phát hành.
