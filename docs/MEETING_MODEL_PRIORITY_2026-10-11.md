# Ưu tiên Flash Lite và tách danh sách model theo key

## Quy tắc hiện hành

Cả Google và nhánh NVIDIA không xử lý được → Google dùng cùng `GoogleMeetingSettings.OrderVerifiedModels`:

1. `gemini-3.5-flash-lite`
2. `gemini-3.1-flash-lite-preview`
3. `gemini-flash-lite-latest`
4. Flash Lite đã xác minh khác, ví dụ `gemini-3.1-flash-lite`
5. Model đã xác minh còn lại theo tên.

**Xếp riêng cho từng key.** Không tự thêm tên model, không ánh xạ bản thường sang preview, không hợp nhất danh sách hai key. Nếu một tên ưu tiên không có trong danh sách của key thì bỏ qua tên đó. Bộ đếm/cooldown và lỗi vẫn theo cặp key/model. Thử hết ứng viên chưa lỗi của key đang dùng rồi sang key dự phòng theo thứ tự lưu; cùng một model trên key khác là ứng viên khác.

Tại thời điểm rà cấu hình cục bộ, Google chính có ba model ưu tiên, `gemini-3-flash-preview`, `gemini-3.5-flash`, `gemini-3.6-flash`. Google 1 có thêm `gemini-3.1-flash-lite`, `gemini-3.8-flash`, `gemini-flash-latest`. Không ghi secret trong báo cáo hoặc log; đọc danh sách không gọi HTTP và không thay kho key.

## Kết quả rà soát và sửa

Nhánh NextFallback trước sửa đã đặt đúng ba model ưu tiên. Điểm chưa thống nhất nằm ở khởi tạo phiên/chọn nhanh dùng model cũ đã lưu và danh sách cache chỉ ưu tiên 3.5 rồi xếp theo tên. Đã đưa việc xếp model về một hàm chung cho cache/menu, helper dự phòng, pool nhiều key và lựa chọn Google lúc mở phiên/chuyển nhanh.

`GoogleSettingsForSession` lấy model đã xác minh theo đúng key; model đã lưu qua bước xác minh được giữ làm ứng viên khi cache thiếu. Người dùng chọn model cụ thể trong hộp API của phiên thì tôn trọng lựa chọn đó. Phiên đang chạy hoặc pause giữ model thực của worker; không tự quay về 3.5 chỉ vì cập nhật nút hoặc tiếp tục. Tự chuyển model không đổi cấu hình mặc định đã lưu.

Giữ cơ chế tránh quay vòng: ứng viên lỗi cố định bị loại; đang cooldown thì bỏ qua. Model tạm lỗi dù hết cooldown không chen trước ứng viên chưa thử trong đợt hiện tại; sau khi hết pool mới xét model hồi phục theo cùng thứ tự ưu tiên. Đã kiểm trường hợp request chậm làm cooldown cũ hết hạn giữa đợt, không vòng lại 3.5 trước khi thử preview/latest. Không thay prompt, gom câu, quota, FIFO, revision, timeout hoặc NVIDIA tự cứu trước Google.

## Kiểm chứng

```powershell
pwsh -NoProfile -File tests/MeetingPriority/Run.ps1
pwsh -NoProfile -File tests/MeetingPriority/Run-Ui.ps1
pwsh -NoProfile -File tests/MeetingPriority/Run.ps1 -InspectSaved
```

- HTTP giả xác minh chính xác thứ tự request của Google và NVIDIA → Google, 429/404/503, key A hết model trước B, pool cố tình đảo B trước A khi đang chạy A. Hai bộ danh sách khác nhau có và không có hậu tố preview. Từng request phải thuộc danh sách model của chính key gửi request.
- Mô phỏng cooldown cũ hết giữa lỗi 503, hồi phục sau hết pool, lỗi cố định, không tự thêm model thiếu, không đổi tên preview, không chuyển khi model vẫn dịch tốt.
- Kiểm 16 caption/lô, id đảo thứ tự vẫn ánh xạ đúng, FIFO/session/history giữ nguyên sau chuyển, pause/resume, chọn model cụ thể bằng tay và hết pool không mất nguồn/không xoay liên tục.
- Harness UI chạy trực tiếp assembly portable, light/dark, không gọi HTTP: khởi tạo ưu tiên 3.5, explicit giữ model đã chọn, key B không lấy preview của key A, đang chạy/pause không reset model, chọn nhanh quay vào Google dùng ưu tiên và giữ pause.

Kết quả cuối ghi ở `.codex-artifacts/meeting-priority/offline.txt` và `ui.txt`. Bộ service: 216 assertion; UI: 14 assertion. Chỉ dùng key giả theo GUID cho HTTP giả; cache/ledger giả được xóa sau khi harness đóng. Không lưu key giả vào credential/key ring của người dùng. Không chạy lại cuộc họp/API thật ở lượt sửa thứ tự này; các kiểm live trước thuộc báo cáo trước.

Release x64/C# 7.3/.NET Framework 4.8 build qua `scripts/Update-Portable.ps1 -BuildOnly -NoPause`, SHA-256 khớp 40 runtime. EXE: `BDA5BEB53F61BD0589EDE3EA0A6EC41D40A89B36173374777F0DECE241471C1D`. BuildOnly không phát hành hoặc thay metadata release cũ. Bản đồ source cập nhật và chạy Check. Không commit/push; không ghi thư mục model Tekla.
