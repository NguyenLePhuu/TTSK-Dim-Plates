# Sửa kẹt NVIDIA và kiểm Teams thật — 11/10/2026

## Hành vi bàn giao

NVIDIA ưu tiên tự phục hồi: Riva Nhật → Anh → Việt; nếu bản chưa qua guard, rỗng, JSON lỗi, lỗi mạng/503/timeout thì Nemotron dịch trực tiếp nguyên văn. Chỉ khi NVIDIA không xử lý được mới chuyển key/model dự phòng. Không vượt 429 hoặc lỗi quyền. Khi có dự phòng, tổng một lần xử lý NVIDIA có trần 18 giây, gồm cả pivot và rescue; không có dự phòng giữ các hạn rescue cũ. Mọi kết quả vẫn phải hoàn tất, không công bố stream dở.

Các gốc lỗi đã sửa trong MeetingTranslationService.cs:

- Ngưỡng lỗi chất lượng dùng số lần lỗi của chính caption/revision đang pending. Thành công của worker thứ hai không làm câu đã ngừng retry bị kẹt mà chưa đủ điều kiện switch.
- Riva lỗi được cứu bằng NVIDIA trực tiếp trước, kể cả khi có Google trong pool. Bản trực tiếp hợp lệ tiếp tục dùng cache nguyên văn như cũ.
- Chuẩn hóa JSON hỏng, output rỗng/thiếu finish_reason thành lỗi chất lượng; không để thành exception chung khiến hàng đợi dừng không chuyển.
- Chờ HTTP, đọc body, mở stream và đọc dòng SSE có cancellation độc lập với Task mạng; quan sát lỗi và dispose phản hồi đến muộn, không công bố bản cũ.
- Model/key lỗi tạm thời được thử lại sau nghỉ 5/10/20/40/60 giây, ít nhất Retry-After. Ưu tiên hết pool chưa lỗi trước; không blacklist lỗi mạng suốt phiên. Trong thời gian giữ pending vẫn xét ứng viên vừa hồi phục hoặc key vừa thêm.

Giữ gom 16 caption/4.000 ký tự và ngân sách Google cho mọi model, FIFO, revision, session/history, pause/manual priority; không đổi thuật toán DIM.

## Kiểm chứng

`tests/MeetingRouting/Run.ps1 -Mode offline`: **177 assertion PASS**, log `.codex-artifacts/meeting-routing/offline-final.txt`. Hai lỗi quality bị reset bởi worker khác và model hồi phục bị blacklist đã được tái hiện FAIL trước khi sửa, PASS sau sửa. Các ca NVIDIA tự cứu quality/rỗng/JSON/missing finish/mạng/503/timeout không gửi Google; HTTP bỏ qua Cancel vẫn thoát theo ngân sách rồi chuyển; SSE treo và response muộn được hủy/dọn. Cổng mở stream được bổ sung sau full suite; chạy lại riêng `-Mode nvidia-repair` trên source cuối (14 assertion).

Kiểm live bằng cùng source dịch họp, key đã lưu, bộ đọc Teams thật. Lỗi chủ động chỉ nằm trong HTTP handler test; không đổi key/cấu hình mặc định để ép lỗi:

| Lượt | Kết quả |
| --- | --- |
| Google bình thường | 23 kết quả, 13 HTTP, không switch; có lô 14 câu/HTTP |
| Google lỗi 503 giả sau ba request | 23 kết quả; chuyển 3.5 Flash Lite → 3.1 Flash Lite, API dự phòng trả thật |
| Ép key chính lỗi 503 | Thử hết 6 model key chính rồi Google 1; 24 kết quả thật, không đổi cấu hình lưu |
| NVIDIA sau sửa tự cứu | 19 kết quả: 6 Riva, 2 NVIDIA trực tiếp; NVIDIA trả 429 thật ở request 19 rồi Google dịch nốt 11 câu trong một request; pending cuối = 0 |

Các lượt trên ghi **0 nguồn không khớp snapshot và 0 kết quả trùng cùng sequence/source**. Log có nguồn Nhật/bản Việt trong `.codex-artifacts/meeting-routing/*.jsonl`, không có key rõ. Đây là kiểm tính toàn vẹn nguồn và đường dịch; không bảo đảm mọi bản dịch máy chính xác nghĩa, nhất là caption Teams nhận sai/lẫn ngôn ngữ. Đã đọc đối chiếu mẫu thực tế.

Lượt NVIDIA đầu có lỗi SSL/TLS do harness thiếu TargetFrameworkAttribute dù tham chiếu net48. Đã sửa harness để khớp runtime sản phẩm; NVIDIA thật sau đó trả được cả Riva và Nemotron. Không coi lỗi harness này là lỗi kết nối của bản sản phẩm. Một lượt probe chỉ 6 giây không đủ trả kết quả đã ghi FAIL; không gộp vào kết quả đạt.

Phiên UI cũ kẹt tại câu 14 đã được chuyển tạm Google để xử lý đủ hàng chờ, rồi pause và xuất **107 lượt** thành `C:/Users/phu17/OneDrive/Desktop/11-10-26.txt` trước khi đóng/cập nhật. Bản dịch cũ vẫn nằm trong file đó; phiên mới không nhập lại lịch sử RAM.

Build bàn giao: `scripts/Update-Portable.ps1 -BuildOnly -NoPause`, Release x64/net48/C#7.3, script kiểm SHA-256 40 runtime. Không cập nhật metadata phát hành cũ. Compiler harness có hai cảnh báo cũ CS4014/CS0067. Không commit/push/phát hành; không ghi model Tekla và không gọi DIM.
