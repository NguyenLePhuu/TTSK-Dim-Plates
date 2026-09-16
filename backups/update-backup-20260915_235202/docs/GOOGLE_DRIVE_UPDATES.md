# Cập nhật portable qua GitHub và Google Drive

## Luồng sử dụng

Mở `Cap_Nhat_Portable.bat`, chọn phát hành **Yes**, sau đó chọn **1: GitHub** hoặc **2: Google Drive**. Chọn **No** để chỉ build portable tại máy.

- GitHub: build, kiểm tra portable, commit/push và để GitHub Actions đóng gói/phát hành như trước.
- Google Drive: build tại máy, dùng cùng script đóng gói ZIP / `release.json` / `SHA256.csv` / checksum ZIP. Tải asset mới lên Drive, tải lại bằng quyền đọc công khai để đối chiếu SHA256, cuối cùng mới thay nội dung file chỉ mục cố định. Không cần push GitHub. `commit` trong metadata là HEAD làm gốc của source local; source chưa commit vẫn được build.
- Cả hai nhánh lấy số phiên bản cao nhất đang công bố rồi tăng patch. Khi không đọc được một nguồn đã cấu hình, **publisher dừng** để tránh cấp version sai. Chỉ chạy một publisher tại một thời điểm, kể cả GitHub Actions và các máy khác; chưa có khóa phân tán giữa Google Drive và GitHub.
- TTSK kiểm tra hai nguồn song song; version số lớn hơn thắng, bằng nhau ưu tiên GitHub. Nếu một nguồn lỗi, dùng nguồn còn lại và thông báo khi kiểm tra thủ công. Nếu cả hai lỗi, báo lỗi, không báo đã là bản mới nhất. Trước khi tải, TTSK kiểm tra lại nguồn và version; vẫn giữ toàn bộ kiểm tra SHA256, đường dẫn ZIP, manifest và rollback hiện tại.

## Cần tài khoản / cấu hình một lần

1. Một tài khoản Google có quyền ghi vào **thư mục riêng chỉ chứa các bản phát hành TTSK**. Chia sẻ thư mục này thành **Anyone with the link / Viewer**. Script không tự đổi quyền chia sẻ. Tất cả file bên trong sẽ dành cho người dùng tải công khai; không đặt dữ liệu riêng tư ở đó.
2. Bật **Google Drive API** trong một Google Cloud project; tạo API key dùng để đọc dữ liệu công khai và giới hạn key chỉ cho Google Drive API. Key này đi cùng ứng dụng, không phải bí mật đăng nhập. Hạn chế IP/referrer không phù hợp có thể chặn ứng dụng desktop.
3. Cấu hình Google Auth Platform (OAuth consent), thêm tài khoản phát hành vào **Test users** nếu ứng dụng còn ở chế độ Testing. Tạo OAuth client loại **Desktop app**, tải JSON vào máy (ví dụ thư mục Downloads). Công cụ phát hành cần scope `https://www.googleapis.com/auth/drive` để ghi vào thư mục/file chỉ mục đã có; xem quyền trên màn hình Google trước khi đồng ý. Không đưa JSON OAuth, password hay refresh token vào source, App.config hoặc chat.
4. Trong **Windows PowerShell**, đăng nhập một lần rồi chạy setup:

```powershell
& .\scripts\Connect-GoogleDrivePublisher.ps1 -ClientJson "$env:USERPROFILE\Downloads\client_secret_DESKTOP.json"
& .\scripts\Initialize-GoogleDriveUpdates.ps1 -FolderId 'ID_THU_MUC' -ApiKey 'API_KEY_DOC_CONG_KHAI'
```

Đăng nhập dùng trình duyệt hệ thống, PKCE và callback trên loopback 127.0.0.1. Refresh token/client secret được mã hóa bằng Windows DPAPI trong `%LOCALAPPDATA%\TTSK Dim Plates\Publisher\google-oauth.xml`; chỉ tài khoản Windows trên máy đã lưu mới đọc được. BAT tự làm mới access token. Chế độ OAuth **Testing** thường làm refresh token hết hạn sau 7 ngày với scope Drive, khi đó chạy lại đăng nhập. Xem [quy tắc vòng đời token của Google](https://developers.google.com/identity/protocols/oauth2).

Setup tạo `TTSK-update-latest.json` nếu chưa có, hoặc dùng lại ID trong App.config / tham số `-ManifestId`; kiểm tra đọc công khai, rồi lưu `GoogleDriveManifestId`, `GoogleDriveFolderId` và `GoogleDriveApiKey` vào `TTSK Dim Plates/TTSK Dim Plates/App.config`. Chỉ mục giữ nguyên File ID qua mọi lần phát hành. Nếu setup thất bại sau khi tạo file, thư mục có thể còn file chỉ mục chưa dùng; kiểm tra quyền và dùng lại ID hoặc chạy lại setup.

Thư mục hiện tại: `1sTL3OSwqUFrCuXqr0fhjzsiQZQtxQqsh`. File chỉ mục đã tạo: `1OkbudULOlyIFyjqxCa3uJFIhnNQyZdX4`. Cần hoàn tất API key và OAuth tại máy để bật phát hành.

Phát hành sau khi đã đăng nhập:

```powershell
& .\Cap_Nhat_Portable.bat -Publish -Destination GoogleDrive -ReleaseNotes 'Noi dung cap nhat'
# Hoặc giữ luồng GitHub:
& .\Cap_Nhat_Portable.bat -Publish -Destination GitHub -ReleaseNotes 'Noi dung cap nhat'
```

Có thể mở BAT trực tiếp sau khi lưu đăng nhập. `-Publish` không kèm `-Destination` sẽ hỏi chọn nguồn; `-BuildOnly` không cần tài khoản. Khi chạy tự động, có thể cung cấp access token ngắn hạn qua biến môi trường `TTSK_GOOGLE_DRIVE_ACCESS_TOKEN`; biến này được ưu tiên hơn đăng nhập đã lưu, không lưu vào source/log. Không cần tài khoản Google trên máy người dùng TTSK.

## Đưa tính năng đến người dùng hiện có

Sau setup, phát hành **một bản GitHub chứa code mới và App.config đã cấu hình Drive** để các máy cũ nhận được khả năng đọc Drive. Sau khi cài bản này, máy người dùng không cần đăng nhập Google; có thể nhận bản mới từ cả hai nguồn. Máy đang dùng code updater cũ chỉ biết GitHub, không thể tự phát hiện Drive trước lần nâng cấp này.

Không xóa hay thay ID file chỉ mục; giữ asset cũ để các phiên cập nhật đang chạy còn tải được. Asset tải lên dang dở không được tham chiếu bởi chỉ mục, có thể kiểm tra/dọn thủ công sau. Nếu publish báo không xác minh được chỉ mục sau lần ghi cuối, kiểm tra trên Drive trước khi thử lại.

## Kiểm thử và giới hạn

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Updater.ps1` build Release x64, chạy kiểm thử updater và mô phỏng publisher hai nguồn. Kiểm thử mô phỏng không chứng minh tài khoản/quota/chính sách tổ chức của Google hoạt động. Chỉ coi triển khai Drive thực tế hoàn tất sau khi setup, publish và một máy TTSK tải/cài thành công bản mới từ Drive.

Drive dùng API `files.get?alt=media` qua HTTPS, không xử lý trang xác nhận tải của giao diện web. Redirect bất thường, giới hạn tải, Google Workspace chặn chia sẻ, API key bị hạn chế hoặc hết quota đều được báo lỗi. Giới hạn ZIP hiện tại: 200 MiB. Người dùng vẫn nhận được kết quả kiểm tra từ GitHub nếu kiểm tra Drive thất bại.

Tham khảo Google: [upload](https://developers.google.com/workspace/drive/api/guides/manage-uploads), [download](https://developers.google.com/workspace/drive/api/guides/manage-downloads), [sharing](https://developers.google.com/workspace/drive/api/guides/manage-sharing).
