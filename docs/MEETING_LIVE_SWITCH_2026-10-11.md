# Kiểm chuyển model bằng phụ đề Teams thật — 11/10/2026

Chạy sau khi người dùng báo đã phát âm thanh, khoảng 23:17–23:20 giờ Nhật. Dùng bộ đọc Teams và coordinator/source sản phẩm hiện hành, key thật trong kho đã lưu, model đã xác minh riêng từng key. Harness net48/C#7.3/x64 tại `tests/MeetingPriority/Live.cs`; không sửa source sản phẩm trong lượt này.

## Kết quả

| Luồng | Đường đã quan sát | Kết quả công bố | Sai nguồn / trùng câu |
| --- | --- | ---: | --- |
| Riva → Google | Riva timeout thật → NVIDIA trực tiếp → lỗi 503 chủ động → gemini-3.5-flash-lite | 12 | 0 / 0 |
| Google chính (key 1) | gemini-3.5-flash-lite → gemini-3.1-flash-lite-preview → gemini-flash-lite-latest | 22 | 0 / 0 |
| Google 1 (key 2) | gemini-3.5-flash-lite → gemini-3.1-flash-lite-preview → gemini-flash-lite-latest | 17 | 0 / 0 |

Riva được cho phép tự phục hồi bằng Nemotron như sản phẩm trước khi ép lỗi chuyển. Riva timeout thật; Nemotron có 4 phản hồi HTTP 200 và đã công bố bản dịch. Sau đó handler test trả 503 cho NVIDIA để kiểm chuyển; Google trả 9 HTTP 200 thật.

Từng key Google được chạy với pool chỉ chứa chính key đó để key khác không che lỗi. Mỗi model 3.5 và preview trả ít nhất một HTTP 200 thật rồi handler mới trả 503 tại request tiếp theo; latest tiếp tục API thật. Key 1: 1/1/15 HTTP 200 theo thứ tự ba model; key 2: 1/1/12 HTTP 200. Không có HTTP lỗi thực của Google trong các phản hồi hoàn tất đã ghi; các 503 Google là lỗi chủ động trong harness. Không cố tình làm cạn quota, không ghi cooldown 429 giả.

Mọi nguồn gửi Google và nguồn đi kèm bản dịch được đối chiếu nguyên văn với snapshot đọc từ Teams. Không lặp kết quả cùng session/sequence/source. Caption có revision thực được công bố lại theo cùng sequence là hợp lệ, không tính là trùng. Cấu hình Google mặc định trước/sau mỗi lượt được đối chiếu bằng key và model, không thay đổi. Không ghi secret vào log. Không quét model mới, không thay provider đã lưu.

Các lượt có thời hạn 60/75/75 giây. Khi hết thời gian harness pause/dispose; key 1 và key 2 còn một mục đang xử lý tại thời điểm dừng. Đây không phải bài kiểm drain đến khi nguồn âm thanh kết thúc và không khẳng định tất cả tiếng nói sau thời hạn đã được dịch. Hai lượt key chạy chồng thời gian một phần, dùng ledger riêng key/model; key 1 bắt đầu sau lượt Riva hoàn tất.

## Giới hạn và chất lượng

Đã đọc đối chiếu mẫu Nhật–Việt sau chuyển: các câu về làm việc để trả sinh hoạt phí, làm vì tiền và tìm giá trị khác vẫn đúng chủ đề. Có bản NVIDIA đầu dịch tên chương trình “Bite-Sized Japanese” thành cụm công việc bán thời gian; đây là lỗi ngữ nghĩa của model, không phải nhầm nguồn hay lỗi switch. Không tự sửa prompt/thuật toán nền theo một mẫu và không tuyên bố độ chính xác ngữ nghĩa tuyệt đối. Các câu chưa nói hết vẫn có thể tạo bản dịch cụt tương ứng.

Không phát hiện lỗi điều phối cần sửa trong ba lượt này. Giữ bản portable đã bàn giao. Không kiểm DIM và không ghi thư mục model Tekla; không commit/push/phát hành.

## Chạy lại và bằng chứng

```powershell
pwsh -NoProfile -File tests/MeetingPriority/Run-Live.ps1 -Mode inspect
pwsh -NoProfile -File tests/MeetingPriority/Run-Live.ps1 -Mode riva -Seconds 60
pwsh -NoProfile -File tests/MeetingPriority/Run-Live.ps1 -Mode key1 -Seconds 75
pwsh -NoProfile -File tests/MeetingPriority/Run-Live.ps1 -Mode key2 -Seconds 75
```

Các mode ngoài inspect gửi phụ đề thật tới API, chỉ chạy khi được người dùng cho phép. Log cục bộ ngoài model Tekla:

- `.codex-artifacts/meeting-priority-live/riva-20261011-231739.jsonl`
- `.codex-artifacts/meeting-priority-live/key1-20261011-231851.jsonl`
- `.codex-artifacts/meeting-priority-live/key2-20261011-231828.jsonl`
- `.codex-artifacts/meeting-priority-live/summary.json`

Log chứa nội dung phụ đề/bản dịch, không chứa API key. Không đưa log cuộc họp vào sản phẩm hoặc phát hành.
