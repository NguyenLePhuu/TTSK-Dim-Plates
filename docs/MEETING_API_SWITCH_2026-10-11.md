# Chọn model/API ngay trong phiên dịch — 11/10/2026

## Hành vi

- Nút **Chọn model API** thay **Thử lại API**, dùng trước khi bắt đầu, đang dịch, gặp lỗi hoặc tạm dừng. Menu provider trên đầu form cũng mở hộp này.
- Hai thẻ Google Gemini / NVIDIA Riva, ô key có Hiện/Ẩn bên trong, nút bo góc, scale DPI sau khi dựng đủ control. Nền tối dùng màu cam chủ đạo RGB(222,150,89), nền nhấn nâu cam như phần mềm; nền sáng dùng xanh. Google có popup danh sách model theo key; NVIDIA hiện Riva cố định.
- Mở/chọn model chỉ đọc danh sách đã xác minh theo key. **Kiểm tra model** gọi API để lấy danh sách và dịch thử. Key Google mới phải có model đã kiểm tra; key NVIDIA mới được xác minh trước khi áp dụng. Hủy không thay cấu hình phiên.
- **Áp dụng ngay** thay bộ dịch trong coordinator hiện tại; không tạo phiên khác, không reset lịch sử. Cấu hình dùng ngay trong RAM rồi lưu Credential Manager. Nếu lưu thất bại, thông báo rõ và vẫn giữ cấu hình đang hoạt động.
- Phiên đang chạy tiếp tục nhận phụ đề khi mở hộp chọn và kiểm tra key/model. Phiên tạm dừng vẫn tạm dừng sau khi đổi.

## Tính toàn vẹn khi chuyển

`SwitchApiAsync` tạo translator thay thế trước, sau đó hủy lifetime riêng của các worker cũ. Các điểm chờ dispatch, admission, câu đầu, HTTP và retry đều nhận cancellation. Câu đã lấy khỏi queue nhưng chưa gửi hoặc request chưa hoàn tất được trả về queue; chờ worker cũ kết thúc trước khi gắn bộ mới.

Giữ reader, tracker, cửa sổ context, SessionId, Sequence, completed texts và lịch sử. Kết quả đã công bố không được gửi lại chỉ vì đổi API. Kết quả HTTP cũ bị hủy không được công bố sau chuyển. Revision thật của phụ đề do Teams sửa vẫn đi theo quy tắc hiện có, không được coi là thao tác đổi model.

Worker mới xóa lỗi/cooldown trong coordinator của cấu hình trước. Bộ đếm Google trên đĩa dùng hash của model + key: key khác có ngân sách riêng, chuyển lại key cũ không reset ngân sách. Ledger cũ theo model được gán một lần cho key cũ; đổi key trong store thực hiện migration trước khi lưu key mới. Chạy đồng thời với bản portable cũ chỉ có ledger theo model không còn dùng chung bộ đếm; giới hạn phía Google vẫn áp dụng. Key cùng dự án có thể chia sẻ quota phía Google dù khác chuỗi key.

Đóng form trong lúc chuyển chờ tác vụ chuyển kết thúc và dispose cả translator chưa gắn. Không ghi gì vào thư mục model Tekla.

## Kiểm chứng

Chạy offline bằng `pwsh -NoProfile -File tests/MeetingApiSwitch/Run.ps1`: **139 kiểm tra PASS** trên bản portable cuối. Harness dùng HTTP giả và nguồn caption giả, biên dịch C# 7.3 x64 với portable đã build; hình UI/log nằm trong `.codex-artifacts/meeting-api-switch/`. `-UiOnly` chỉ dựng/kiểm giao diện, không gọi API. Popup native dùng PrintWindow client-only để tránh titlebar giả do DrawToBitmap.

Các nhóm kiểm tra:

- Đổi giữa HTTP đang chạy, 429 Retry-After, 429 quota ngày, 403, 404 và lô Flash Lite. Có trường hợp HTTP cố tình trả kết quả cũ sau hủy.
- Đối chiếu từng nguồn với output riêng, đảo thứ tự ID trong phản hồi batch để bắt lỗi ánh xạ. Không thiếu/trùng/sai thứ tự caption.
- So sánh toàn bộ dữ liệu serialize của kết quả cũ trước/sau chuyển và chuyển lại; không HTTP lại cho các caption đã hoàn tất. Caption mới dùng kết quả của bộ mới.
- Giữ pause/resume, session identity; revision đang chờ được thay bằng nguyên văn mới nhất.
- Google → NVIDIA → Google; dispose đủ hai worker NVIDIA. Đóng xen kẽ chuyển 12 lượt, chuyển liên tiếp 10 lượt, cấu hình không hợp lệ giữ bộ cũ.
- Ngân sách theo key: cùng key giữ pacing khi tạo lại, key khác không bị cooldown key cũ.
- UI sáng/tối, Google chọn model/Riva cố định, nút dùng được trước khi bắt đầu, kiểm kích thước theo DPI.

Kiểm API thật bằng `Probe.exe <output-folder> --live` chỉ gửi câu mẫu do test tạo, không gửi nội dung họp. Kết quả đã quan sát:

| API | Nguồn mẫu | Kết quả |
| --- | --- | --- |
| gemini-3.5-flash-lite | 会議を始めます。 | Bắt đầu cuộc họp. |
| gemini-3-flash-preview | 次の議題に進みます。 | Tiếp theo chúng ta sẽ chuyển sang nội dung nghị sự kế tiếp. |
| NVIDIA | 会議を終了します。 | Kết thúc cuộc họp. |

Thời gian quan sát lần kiểm câu mẫu khoảng 2,33 s / 1,82 s / 2,23 s; không phải cam kết độ trễ. Đối chiếu lịch sử trước/sau hai lần chuyển đều giữ nguyên. Key đọc từ Credential Manager, không xuất vào log. Kiểm Teams thật được bổ sung bên dưới; chưa có key thứ hai thực tế để kiểm quota khác dự án. Không suy rộng các câu mẫu thành bảo đảm chất lượng mọi bản dịch.

Build bàn giao: `scripts/Update-Portable.ps1 -BuildOnly -NoPause`, Release x64, đối chiếu SHA-256 40 file runtime với output build mới; báo cáo `verified-runtime-sha256.csv` trong thư mục artifact test. `portable/SHA256.csv` và `release.json` thuộc gói phát hành trước; BuildOnly không cập nhật metadata phát hành. EXE bản tự động chuyển: `9D86B832098050B79A20312B226323F881BEC954FE4E8C3F6E32A4AF2204C291`. Không commit/push/phát hành.

## Tự chuyển khi lỗi và kiểm Teams trực tiếp

Chỉ bật khi có key Google và danh sách Flash Lite đã xác minh. Thứ tự: `gemini-3.5-flash-lite` → `gemini-3.1-flash-lite-preview` → `gemini-flash-lite-latest` → Flash Lite khác trong danh sách. Loại model đang dùng/model đã lỗi trong đợt, tối đa 6 ứng viên. Không quét HTTP trên đường dịch; không tự chọn Pro hoặc model ngoài danh sách. Cấu hình mặc định đã lưu không bị thay bằng lựa chọn dự phòng của phiên.

| Tình huống | Hành vi |
| --- | --- |
| 429/quota, 404, NVIDIA 401/403 | Chuyển ngay khi có ứng viên |
| Timeout/mạng/5xx | Hai lỗi liên tiếp; thành công reset đếm |
| Đầu ra rỗng/cắt dở/quality | Ba lỗi liên tiếp |
| Google 401/403, request 400/413/422 | Giữ nguồn, báo để sửa key/cấu hình; không xoay model cùng key |
| Đang chạy tốt, reader UIA lỗi, đang pause | Không đổi model |
| Hết ứng viên | Giữ cơ chế retry/banner; không quay vòng |

Lỗi Riva có thể bọc `HttpRequestException` trong `InvalidOperationException`. Live test đã bắt trường hợp này; bộ phân loại dùng chuỗi nguyên nhân giống cơ chế retry hiện có. Khi có Google dự phòng, lỗi Riva được đưa ra coordinator, tránh mắc kẹt ở rescue NVIDIA cùng dịch vụ; khi không có Google đủ điều kiện, giữ rescue NVIDIA cũ. `Resume` xét lại lỗi đã phát sinh lúc pause nhưng không tăng số lỗi chỉ vì người dùng bấm tiếp tục.

`_apiGeneration` loại lỗi worker cũ; chỉ một tác vụ chuyển tự động chạy. Lựa chọn thủ công đợi chuyển đang chạy rồi được ưu tiên. Đóng form đợi cả tác vụ chuyển tự động; worker không đợi tác vụ đang đợi chính nó. UI cập nhật provider/model sau chuyển. Cả đổi thủ công và tự động giữ nguyên session, tracker, queue, context, completed texts, lịch sử: **tiếp tục câu chưa hoàn tất, không dịch lại từ đầu**.

Kiểm nguồn Nhật đang phát trong Teams theo yêu cầu người dùng: đọc bằng `TeamsDesktopCaptionReader`, gọi API thật, đối chiếu output với nguyên văn caption, không sửa Teams/model Tekla. Các lệnh harness:

```powershell
# Chạy từ root sau Run.ps1; title là tiêu đề đã quan sát của cửa sổ cuộc họp.
& .codex-artifacts/meeting-api-switch/Probe.exe .codex-artifacts/meeting-api-switch --teams '<meeting title>'
& .codex-artifacts/meeting-api-switch/Probe.exe .codex-artifacts/meeting-api-switch --teams-failover '<meeting title>'
& .codex-artifacts/meeting-api-switch/Probe.exe .codex-artifacts/meeting-api-switch --teams-nvidia-failover '<meeting title>'
```

Harness dùng cùng config .NET Framework 4.8 của portable. Lỗi chủ động chỉ nằm ở HTTP handler test: Google giả 404 (không ghi cooldown giả vào ledger), NVIDIA giả 429; không thay key/model đã lưu. Bài lỗi mạng NVIDIA cũng được tái hiện offline bằng exception bọc như sản phẩm.

Kết quả sau sửa Riva: `teams-google-after-riva.txt` ghi 16 kết quả thật, 0 chuyển tự động, 0 nguồn không khớp, 0 kết quả cùng sequence/revision bị ghi đè. `teams-google-failover-after-riva.txt`: 16 câu bằng 3.5, giả 404, chuyển một lần sang 3.1 rồi tiếp tục câu 17–20; không quay lại câu 1. `teams-nvidia-failover-fixed.txt`: NVIDIA gặp lỗi kết nối, chuyển một lần sang 3.5, xử lý 16 câu thật; không cần lỗi giả. Log có caption thật chỉ nằm ở artifact cục bộ ngoài Tekla, không chứa key.

Hồi quy bổ sung: lỗi lồng nhau Riva, Google bình thường không đổi, lỗi thoáng qua được reset bởi thành công, không chuyển do UIA, Google key sai không xoay, chuỗi lỗi tức thì hết ứng viên không lặp, pause/resume, manual ưu tiên, đóng giữa chuyển, giữ toàn bộ dữ liệu serialize của kết quả cũ. Lượt cuối **216 kiểm tra PASS**, ghi tại `regression-final.txt`; Google API thật được kiểm lại sau sửa Riva (16 câu không chuyển, lượt lỗi tiếp tục từ 17–20). Các mẫu nhận dạng Teams có thể sai chữ/cụt câu; kiểm này xác nhận không ghép nhầm nguồn hoặc thay lịch sử, không bảo đảm ngữ nghĩa mọi bản dịch máy.
