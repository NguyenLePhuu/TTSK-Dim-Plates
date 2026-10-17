# Gom câu, ngân sách và chuyển API cho mọi model Google

## Hành vi hiện hành

- Mọi model Gemini hợp lệ đi qua cùng `GoogleModelRequestPacer` và luồng gom tối đa 16 caption/4.000 ký tự, gồm model chọn thủ công và model tự chuyển. Không chờ đủ lô. Giữ kiểm STOP, đủ id, không trùng id, Unicode, revision và FIFO. Model từ chối schema vẫn có cơ chế sửa head bằng request đơn rồi giảm trần lô.
- Ngân sách TTSK vẫn là 15 lượt/60,1 giây và 500 lượt/ngày Pacific, dự phòng 2 lượt, bù 4,1 giây/lượt, khoảng cách tối thiểu 1,1 giây. Ledger theo cặp key/model, chia sẻ giữa instance/tiến trình; đổi về cặp cũ không xóa số đã dùng. Tất cả model dùng bộ đếm này, kể cả kiểm key và retry.
- Đây là **ngân sách cục bộ**, không tự dò quota tài khoản. Google áp quota theo project và model; nhiều key cùng project có thể cùng hết quota. Quota thực tế còn phụ thuộc tier và TPM. Xem [Google rate limits](https://ai.google.dev/gemini-api/docs/rate-limits). Không suy ra quota độc lập chỉ từ việc key khác nhau.
- Pacer báo cho coordinator khi bị chặn bởi RPM/RPD/cooldown, để thử ứng viên khác ngay trước khi HTTP. Nhịp bù token thông thường vẫn chờ gom câu, không tự xoay model vì khoảng cách 1,1–4,1 giây.
- HTTP 429 giữ cooldown của cặp lỗi và tôn trọng Retry-After/RetryInfo; chỉ dùng 61 giây khi thiếu thời gian. Cặp dự phòng không nhận cooldown của cặp cũ. Mạng, timeout và 5xx chuyển ngay từ lỗi đầu; quality vẫn chờ ba lỗi để có cơ hội sửa lô. 400/413/422 không tự xoay theo lỗi cấu hình request.
- Cả helper model-only và pool nhiều key nhận toàn bộ model đã xác minh, bỏ lọc Flash Lite và trần 6 của helper cũ. Ưu tiên Flash Lite như trước, sau đó model đã xác minh khác. Pool nhiều key thử hết model của key hiện tại rồi mới sang key kế tiếp, kể cả 401/403. Helper model-only vẫn chặn xoay cùng key khi Google từ chối quyền.
- Hết ứng viên không quay vòng vô hạn: giữ nguồn, retry/chờ theo trạng thái lỗi hoặc cho người dùng chọn lại. Pause, manual priority, generation guard và dispose vẫn đi qua bộ chuyển hiện có. Không ghi đè lựa chọn mặc định khi tự chuyển.
- Bỏ lớp settings/store/exception/translator Azure và worker/cooldown/routing liên quan. Chỉ giữ chuỗi `Azure` khi đọc cấu hình provider cũ thành Google. Enum Google giữ số 2 để không làm sai kho key DPAPI đã lưu. Không xóa credential Azure cũ trong Windows.

## Kiểm chứng

Chạy lại bằng:

```powershell
pwsh -NoProfile -File tests/MeetingRouting/Run.ps1
```

Harness biên dịch trực tiếp source dịch họp bằng C# 7.3, reference .NET Framework 4.8, x64. HTTP giả hoàn toàn; không dùng key thật, không gửi caption thật, không mở Teams/Tekla. Key giả theo GUID; ledger quota giả dùng Temp hoặc LocalAppData ngoài model, không thay kho credential/config người dùng. Sau khi coordinator đóng, test xóa đúng ledger của các key giả đã tạo. Test và artifact không phải phụ thuộc chạy sản phẩm.

Lượt cuối: **153 assertion PASS**, log `.codex-artifacts/meeting-routing/results.txt`:

- Năm tên model gồm mặc định, Flash Lite dự phòng, Flash, Pro và model hợp lệ khác: 16 caption/15 nguồn duy nhất trong một HTTP; id đảo thứ tự vẫn ánh xạ đúng; memory hit không HTTP.
- Chặn JSON thiếu/trùng id, Unicode hỏng, output bị cắt; schema 400 chuyển về câu đơn, giữ đủ nguồn.
- 1.500 reservation mô phỏng trên ba model: rolling RPM, 500 RPD, restart, reset Pacific, tách key/model; 24 instance tranh lượt không cấp trùng; clock lùi, ledger hỏng và hủy lúc chờ.
- Retry-After 2/120 giây giữ đúng giá trị, thiếu header mới dùng 61 giây.
- Với từng lỗi 429/401/403/404/503/408/mạng: thử 8 model key A trước key B dù B đứng trước trong pool; B dịch đủ 16 câu, đúng source/FIFO/session, cả chuỗi hoàn tất dưới ngưỡng test 8 giây.
- NVIDIA key A → key B → Google, hai worker không khóa chuyển. Không kết luận NVIDIA key B có quota độc lập từ HTTP giả.
- Hết pool không lặp liên tục; pause trước gửi và pause tại lỗi, resume chỉ chuyển một lần; đóng lúc chờ quota hoặc lúc request dự phòng đang chạy.
- Đổi thủ công hủy request cũ, dùng revision hiện hành; kiểm bốn lượt manual được ưu tiên sau automatic. Kết quả đã công bố được serialize đối chiếu nguyên vẹn sau đổi API; không replay lịch sử. Lỗi reader UIA không chuyển API.
- RPM/RPD/cooldown cục bộ đều chuyển sang model khác trước HTTP, chỉ một request thành công cho đủ 16 câu.

Lượt test ban đầu đặt ledger giả trong workspace OneDrive gặp IOException khi ghi nhanh. Đã chuyển ledger mô phỏng sang Temp ngoài thư mục đồng bộ, phù hợp vị trí ngoài OneDrive mà sản phẩm đang dùng. Chưa xác định tác nhân gây lỗi I/O ban đầu; không sửa sản phẩm để bỏ qua lỗi lưu ngân sách. Các lượt sau đạt 104 rồi 153 assertion khi bổ sung coverage. Compiler harness còn cảnh báo có sẵn CS4014 (ContinueWith dọn reader) và CS0067 (event chưa dùng).

Build `scripts/Update-Portable.ps1 -BuildOnly -NoPause` Release x64 thành công, script đã đối chiếu SHA-256 **40 file runtime**. EXE portable: `E04B9CA68C3761CC0A0CCA3A6835E476759BA5EB1EE29A8BA35CD44F092A6469`. Bảng kiểm độc lập: `.codex-artifacts/meeting-routing/runtime-sha256.csv`. BuildOnly không cập nhật metadata phát hành cũ `portable/SHA256.csv`/`release.json`.

Không kiểm API/Teams thật trong lần sửa này; các ghi nhận live ở tài liệu trước thuộc phiên trước. Không kiểm bản vẽ thực tế, không gọi DIM, không ghi thư mục model Tekla; không commit/push/phát hành.
