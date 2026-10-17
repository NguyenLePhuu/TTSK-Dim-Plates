# Danh sách model đã xác minh và chế độ đọc phụ đề — 2026-10-10

## Cập nhật cỡ chữ phụ đề

Chế độ đọc không còn khóa 12 pt. Cỡ chữ Việt là 12 × scale, với scale = clamp(min(rộng logic/1100, cao logic/720), 1,2, 1,6), làm tròn theo bước 0,05. Kích thước logic trừ tỷ lệ DeviceDpi/96 để không nhân DPI hai lần. Khoảng chữ 14,4–19,2 pt; 1920×1080 tại 100% dùng 18 pt. Câu ngắn căn giữa dọc trong thẻ, câu dài tăng chiều cao theo nội dung. Tên người nói vẫn nhỏ hơn nội dung; không thay pipeline dịch.

Build portable: `.codex-artifacts/portable-update/20261010-192733-f124914c/`; SHA-256 đối chiếu đủ 40 file runtime. FocusModelTests đạt 189 kiểm tra ở hai theme, kích thước 880×620, 1040×780, 1500×950 và 1920×1080, kiểm không cắt chữ/khôi phục giao diện/cuộn. Harness phải hoàn tất PerformLayout của các panel sau đổi ClientSize trên desktop riêng rồi mới đo; lần đo trước layout chưa phản ánh kích thước mới. Đã xem ảnh dark 1500 và light 880; ảnh 1920 bị giới hạn vùng render của desktop kiểm thử, không coi là ảnh chụp fullscreen màn hình người dùng. Source sản phẩm chỉ sửa MeetingTranslatorForm.cs. Không gọi API/Teams thật để kiểm thay đổi chữ.

## Chọn model Google

- **Chọn model** chỉ đọc danh sách cục bộ theo SHA-256 của key hiện tại. Không gọi HTTP, không dịch thử, không tự quét khi danh sách quá 30 phút hoặc mở lại ứng dụng.
- **Kiểm tra lại** là thao tác quét chủ động: lấy models.list, dịch thử tối đa hai model đồng thời, thay danh sách bằng đúng các model thành công. Tên model lỗi hoặc chưa xác nhận không được lưu vào danh sách hay đưa ra menu. Nếu quét bị hủy/lỗi discovery, giữ danh sách trước; nếu quét hoàn tất mà không model nào thành công, lưu danh sách trống.
- Key mới chưa có danh sách: thông báo bấm Kiểm tra lại. Không dùng danh sách của key khác hoặc tự thêm model chưa kiểm tra.
- Menu không còn mục auto. Vẫn giữ tương thích validate auto cũ và nhập tên thủ công; **Lưu & kiểm tra** chỉ lưu sau khi dịch thành công, đồng thời thêm model đã xác thực vào danh sách.
- Mặc định trong GoogleMeetingSettings là `gemini-3.5-flash-lite`. Cấu hình đã lưu được giữ nguyên. Mở menu không ghi credential.
- Danh sách lưu dưới LocalAppData/TTSK/MeetingTranslation, chỉ có model thành công và thời điểm; không chứa API key. Đây là kết quả kiểm tra ở thời điểm xác minh, quyền/quota của nhà cung cấp có thể thay đổi về sau.

Quét Google thật trong lần sửa này xác minh 6 model:

1. gemini-3.5-flash-lite
2. gemini-3-flash-preview
3. gemini-3.1-flash-lite-preview
4. gemini-3.5-flash
5. gemini-3.6-flash
6. gemini-flash-lite-latest

Model đang lưu là `gemini-3.5-flash-lite`; key và model được đối chiếu trước/sau và không đổi. Không liệt kê model không dùng được. Các hướng dẫn về tự quét/TTL trong tài liệu ngày 2026-10-09 là hành vi lịch sử, được thay bằng luồng này.

## Cửa sổ phụ đề

Nút **Cửa sổ phụ đề** chuyển chính cửa sổ dịch sang chế độ đọc lịch sử. Không mở thêm cửa sổ chỉ có câu mới nhất. Ẩn heading, chọn provider, điều khiển Teams và hai ô live; giữ thanh tiêu đề và nút **Quay lại · Esc** nhỏ. Toàn bộ vùng còn lại dành cho bản dịch tiếng Việt, giữ tên người nói; ẩn tiếng Nhật, timing và các câu chưa có bản dịch. Thẻ gọn hơn, dùng hết chiều rộng chữ và giữ cỡ chữ 12 pt theo DPI khi phóng lớn để hiện nhiều câu hơn.

Lịch sử, coordinator và hàng đợi không bị khởi động lại. Câu đang chờ vẫn được giữ trong RAM và tự hiện khi dịch xong. Các revision cập nhật tại chỗ. Quay lại/Esc khôi phục khung điều khiển, các câu chờ và thiết lập Hiện câu Nhật. Lỗi API vẫn có hàng báo lỗi để tránh phiên dừng âm thầm. Cuộn lên giữ vị trí đọc khi có câu mới; ở cuối tự theo bản dịch mới. Giới hạn lịch sử 200 thẻ của ứng dụng giữ nguyên.

## Kiểm chứng

- Release x64, C# 7.3 / .NET Framework 4.8: `scripts/Update-Portable.ps1 -BuildOnly -NoPause` thành công; SHA-256 đủ 40 file runtime khớp giữa output build và portable. SHA-256 EXE: `562B8CDADD7F78B4A64F3A9FC233953553DE9E2533C80A3EFDAE2A09C0867CCE`. BuildOnly không sinh lại metadata phát hành `release.json`/`SHA256.csv`; các file này vẫn thuộc lần đóng gói trước.
- `tests/MeetingTranslation/Run-FocusModels.ps1 -Runtime portable`: 155 kiểm tra offline đạt. Model discovery với transport giả, lọc lỗi, cache cũ, tách key, menu mở đồng bộ và vòng đời click; form với câu thành công/chờ/revision, resize, cuộn, đổi chế độ nhiều lần, Esc, khôi phục tiếng Nhật và phiên chỉ có câu chờ.
- `Run-FocusModels.ps1 -Runtime portable -Live`: 5 kiểm tra API Google thật đạt; danh sách đã xác minh được lưu cho key hiện tại, credential giữ nguyên.
- `Run-OrderedPipeline.ps1 -Runtime portable`: 44 kiểm tra đạt, bảo toàn thứ tự và câu chờ.
- `Run-Bottleneck.ps1 -Runtime portable`: 20 kiểm tra đạt, timeout/streaming/giữ câu lỗi/thử lại thủ công.
- UI được dựng trên desktop kiểm thử riêng, xem ảnh dark 1040×780 và light 880×620; harness cũng render 1500×950. Không điều khiển desktop người dùng.
- Artifact kiểm thử: `.codex-artifacts/meeting-focus/tests/`; build bàn giao: `.codex-artifacts/portable-update/20261010-141031-dc3cb6e9/`.

Không chạy cuộc họp Teams thật hoặc thao tác bản vẽ Tekla trong lần kiểm tra này. Không ghi vào folder model. Không commit, push hoặc phát hành.

## Căn thẻ API và thiết kế menu model

Lần cập nhật giao diện tiếp theo trong ngày 2026-10-10:

- Hai nút NVIDIA/Google cùng rộng 312 px. Hai ô key/model cùng rộng 336 px. Bốn nút Lưu & kiểm tra/Xóa/Chọn model/Kiểm tra lại cùng rộng 140 px, cao 36 px; khe ngang 8 px và khe hàng 10 px theo AutoScaleMode.None của form.
- Hiện/Ẩn là nút phụ nằm trong ô key, dành riêng khoảng trống bên phải cho editor/placeholder. Gợi ý key được vẽ lại ngay khi đổi provider.
- Menu model rộng đúng bằng ô nhập model (336 px), căn thẳng hai mép, dùng khung DWM như popup Teams, màu theo theme, icon AI, tên đậm, trạng thái xác minh, nền chọn và dấu check. Có tooltip tên đầy đủ. Mũi tên nút đổi khi mở/đóng. Không thay key, lựa chọn đã lưu hoặc gọi thêm API.
- Chỉ sửa source sản phẩm trong ShortcutSettingsForm.cs. Bộ preview hiện có bổ sung ảnh thẻ/menu ở hai theme; ảnh native dùng PrintWindow vì DrawToBitmap dựng sai non-client DWM của popup và bỏ sót placeholder WinForms.
- Build Release x64/portable mới: `.codex-artifacts/portable-update/20261010-144713-a69d86af/`. Đối chiếu SHA-256 output build và portable đủ 40 file đạt; bảng hash tại `.codex-artifacts/meeting-focus/verified-runtime.csv`.
- Chạy lại FocusModelTests trên bản này: 155 kiểm tra offline đạt, gồm menu chọn/mở/đóng nhiều lần và chế độ phụ đề. Đã xem ảnh native thẻ/menu sáng/tối; ảnh tại `.codex-artifacts/meeting-focus/tests/api-card-dark.png`, `api-card-light.png`, `api-menu-dark.png`, `api-menu-light.png`. Không gọi Google API thật lại cho thay đổi giao diện này.
