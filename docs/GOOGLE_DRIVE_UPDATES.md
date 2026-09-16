# Cập nhật qua GitHub và Google Drive

## Phát hành

Mở `update/Cap_Nhat_Portable.bat`, chọn Yes rồi GitHub hoặc Google Drive, nhập ghi chú và END. No chỉ build portable.

Cả hai nguồn dùng chung ZIP, release.json, SHA256.csv và checksum ZIP. Drive tải asset mới, tải lại công khai để đối chiếu checksum, rồi mới cập nhật chỉ mục. File chỉ mục giữ nguyên ID qua các lần phát hành.

## Không dùng API key

Updater đọc file công khai qua HTTPS tại `drive.usercontent.google.com`. Không gửi API key, OAuth token hay yêu cầu đăng nhập người dùng cuối. Chỉ chấp nhận host/path/query được kiểm tra; không theo redirect sang trang đăng nhập hay tự xử lý trang xác nhận tải. Nếu Drive trả HTML, giới hạn tải hoặc chặn truy cập, bản tải không vượt qua kiểm tra JSON/checksum/ZIP. GitHub vẫn là nguồn dự phòng khi kiểm tra Drive lỗi.

Cách này đã thử với chỉ mục và gói hiện tại. Drive có thể áp dụng quota hoặc trang xác nhận cho file lớn; publisher phải tải lại công khai thành công mới công bố. Giới hạn ZIP của updater vẫn 200 MiB, không có nghĩa Drive bảo đảm tải trực tiếp mọi file đến kích thước này.

App.config chỉ cần GoogleDriveFolderId và GoogleDriveManifestId. API key không được đóng vào portable. Tham số ApiKey cũ còn được chấp nhận trong helper để tương thích lời gọi cũ nhưng không dùng.

## Máy phát hành

Cần bật Drive API cho OAuth client Desktop và tài khoản có quyền ghi thư mục RELEASE. Đăng nhập bằng `update/Cai_Dat_Google_Drive.bat` hoặc `scripts/Connect-GoogleDrivePublisher.ps1 -ClientJson <file JSON Desktop>`.

Refresh token và client secret được mã hóa Windows DPAPI tại `%LOCALAPPDATA%/TTSK Dim Plates/Publisher/google-oauth.xml`, ngoài source và portable. Người dùng cuối không cần file này. OAuth Testing có thể yêu cầu đăng nhập lại khi token hết hạn.

Thư mục RELEASE phải cho phép Anyone with the link / Viewer. Không đặt dữ liệu riêng tư trong thư mục phát hành. Không xóa chỉ mục hoặc asset đang dùng. Có thể khởi tạo bằng `scripts/Initialize-GoogleDriveUpdates.ps1 -FolderId <ID>`; không cần API key.

## Phiên bản và giao diện

Ứng dụng kiểm tra hai nguồn song song, so sánh phiên bản theo số, ưu tiên GitHub nếu bằng nhau. Lỗi riêng một nguồn chỉ ghi log. Không tự đổi sang bản thấp hơn bản đã chọn khi kiểm tra lại. SHA256, kiểm tra đường dẫn, manifest và rollback vẫn giữ nguyên.

Publisher cấp số lớn hơn phiên bản GitHub, Drive, yêu cầu GitHub đang chờ và số giữ chỗ tại máy. GitHub lưu số vào `update/release-request.json`, Actions dùng đúng số này. GitHub 1.0.10 đang chờ thì Drive tiếp theo lấy 1.0.11. Không đọc được nguồn đã cấu hình thì dừng cấp số. Phát hành lần lượt từ một checkout; chưa có khóa phân tán cho nhiều máy đồng thời.

Không xóa `.codex-artifacts/portable-update/version-reservation.json` khi có yêu cầu chưa hoàn tất. Bộ đọc vẫn hỗ trợ đường dẫn release-request cũ trên GitHub trong giai đoạn chuyển sang update/.

## Chuyển từ bản có API key

Phát hành bản mới qua GitHub hoặc Drive khi key cũ vẫn hoạt động để máy cũ nhận code không cần key. Xóa key khỏi code không xóa key trong lịch sử Git, backup hoặc các ZIP đã phát hành. Sau khi chuyển các máy cũ sang bản mới, thu hồi API key cũ trên Google Cloud; nếu thu hồi ngay, máy cũ chỉ còn đường GitHub để nâng cấp. Không tự viết lại lịch sử Git hoặc sửa các gói đã công bố.

## Kiểm thử

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Updater.ps1`

Tham khảo: [Google Drive downloads](https://developers.google.com/workspace/drive/api/guides/manage-downloads), [Google API key practices](https://docs.cloud.google.com/docs/authentication/api-keys-best-practices).
