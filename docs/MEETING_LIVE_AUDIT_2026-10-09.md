# Rà soát cuộc họp thật — 09/10/2026

## Bằng chứng và nguyên nhân

Đã xem cửa sổ Teams/TTSK trước khi người dùng yêu cầu không điều khiển máy. Sau yêu cầu đó không thao tác desktop. Probe bộ đọc sản phẩm chỉ đọc UIA, lưu ngoài model Tekla trong .codex-artifacts/meeting-live-audit.

53 snapshot thay đổi trong 40 giây: cùng ID UIA 42.919034.4.5.112.24111 đổi tên TTSK VN thành VN TTSK và bổ sung dấu chấm. Tracker cũ tạo Sequence 22 rồi 23 cho cùng dòng. Corpus có 28 ID nhưng tracker cũ phát 36 lượt. Đây là lỗi nhận diện lượt, không phải hai người nói cùng nội dung.

Trong cửa sổ thật có bản gần nghĩa vẫn là nguyên tiếng Nhật và thời gian tổng hơn 60 giây. API probe: Riva ja-en timeout sau 20.034 giây; Nemotron với prompt cũ/tiếng Anh trả nguyên câu Nhật sau 1.109 giây. Prompt tiếng Việt trả bản dịch sau 2.401 giây. Mốc hủy direct 4 giây còn khiến yêu cầu cần 4.5 giây bị hủy rồi gửi lại; được tái hiện bằng mock có thời gian cố định.

Riva sử dụng đúng tag ja-en/en-vi theo tài liệu NVIDIA: https://huggingface.co/nvidia/Riva-Translate-4B-Instruct-v2/blob/main/README.md. Không tự đổi sang cặp ja-vi không có trong danh sách công bố. NVIDIA cũng yêu cầu timeout phù hợp với thời gian sinh: https://docs.nvidia.com/nim/large-language-models/latest/troubleshooting/requests.html.

## Thay đổi sản phẩm

Chỉ MeetingTranslationService.cs; không thêm .cs sản phẩm, không thay DIM/Tekla, Google, key hoặc thao tác cuộc họp.

- Giữ Sequence khi cùng node đổi thứ tự token tên và chữ là phần tiếp/sửa của cùng câu. Không gộp tên trên toàn phiên, không gộp hai ID khác nhau; đổi Alice→Bob trên cùng node vẫn tách lượt.
- Bản gần nghĩa vẫn phải có tiếng Việt; cấm chấp nhận echo nguyên nguồn hoặc phần lớn chữ Nhật. Lỗi chất lượng giữ tại đầu FIFO và retry, không sinh thẻ giả.
- Prompt trực tiếp bằng tiếng Việt, giữ đại từ/ngữ cảnh câu cụt, hai ví dụ ngắn. User message cuối nguyên văn nguồn.
- Ngân sách direct 12 giây thay 4 giây, để một yêu cầu có cơ hội hoàn tất; không phải thêm sleep 12 giây. Riva vẫn 5 giây; sau lỗi giãn probe 60–300 giây thay 30 giây.
- Đầu vào chỉ chữ Latin không qua pivot ja-en. Direct cache tối đa 64 mục theo nguyên văn, không dùng history; cache chỉ tránh HTTP lặp, vẫn phát đủ lượt có Sequence riêng.
- Giữ FIFO nghiêm ngặt, Retry-After, nguồn đầy đủ, pause/resume. Không cắt/bỏ câu để báo độ trễ thấp giả.

## Xác minh

Build Release x64: .codex-artifacts/portable-update/20261009-144907-32bf8372/build.
Backup đã kiểm hash: backup/meeting-live-audit-20261009-144114.

59 kiểm tra đạt trên build cuối: AuditTests 15, StrictTests 26, VerbatimTests 18. Corpus thật phát lại còn đúng 28 lượt; toàn bộ chữ cuối mỗi dòng và thứ tự khớp, không mất câu. Kiểm cả speaker thật khác nhau, ID khác cùng tên/câu, echo tiếng Nhật, timeout 4.5 giây không bị hủy, cache không xóa lượt nói, HTTP 429 chờ đúng rồi tiếp tục FIFO.

API thật sau lần sửa đầu: 5/5 câu theo thứ tự, tổng 24.447 giây, có HTTP 429 giữa chuỗi; pending cuối 0. Bản cuối kiểm 2/2 câu: tổng 10.443 giây; câu đầu 9.472 giây (gồm 5 giây Riva chậm), câu sau hoàn tất ở 10.381 giây, delta khoảng 0.909 giây. Prompt cuối sửa đúng đại từ ở mẫu “Know that you don't have？” và không thêm giải thích như probe trước đó. Không đủ để bảo đảm mọi câu/ngữ nghĩa hoặc cuộc họp dài luôn hoàn hảo.

Công cụ live dùng source đã thu và key đã lưu, không thay key/settings. Không chạy thêm dịch song song lâu dài với ứng dụng người dùng. Model Qwen probe 404 và Llama probe 410, không chọn hay lưu chúng vào sản phẩm.

## Triển khai và giới hạn

Lần đầu bị khóa portable/System.Buffers.dll. Lần kiểm tra cuối ứng dụng đã đóng, script cập nhật portable thành công 40 file runtime; không điều khiển desktop hoặc đóng/kill ứng dụng của người dùng. Build bàn giao: .codex-artifacts/portable-update/20261009-161410-aaa243a9/build. Manifest được đối chiếu riêng.

HTTP 429/timeout vẫn phụ thuộc dịch vụ NVIDIA. Với hợp đồng tuần tự và không bỏ câu, câu sau phải đợi khi đầu hàng gặp lỗi. Tăng throughput/giảm gọi dư giúp giảm backlog nhưng không thể đảm bảo độ trễ dưới 10 giây trong mọi điều kiện. Nguồn vẫn giữ trong RAM khi phiên mở; đóng cửa sổ không lưu bền hàng chờ.
## Rà soát bổ sung trước bàn giao

Phát hiện và tái hiện thêm: snapshot UIA trống 200 ms rồi cùng ID hiện lại tạo 2 lượt ở bản trước. Đã giữ danh tính dòng vừa biến mất trong 2 giây, tối đa 256 dòng. Chỉ khôi phục khi cùng ID, cùng người nói (hoặc đảo tên đã xác minh), chữ giống/tiếp nối/sửa nhẹ; không nối ID mới, người mới hoặc nội dung khác. Câu ngắn biến mất vẫn flush ngay, không đợi khung hồi phục.

FinalReview bổ sung 18 kiểm tra: gap/reappearance/revision, đổi người thật, đổi ID, hết hạn ID, flush câu ngắn, chuỗi 200 câu với lỗi reader tạm thời, một tác vụ dịch và zero speculative preparation, sửa nguồn trong lúc HTTP đang chạy. Không bỏ/gửi trùng lượt; kết quả cũ bị chặn và toàn bộ nguồn mới đi trước câu sau. Tổng trên build cuối: 77 kiểm tra (18 + 15 + 26 + 18). Không đổi translator/API sau lần kiểm thật đã ghi ở trên. Không gọi lại API hàng loạt để tránh ảnh hưởng hạn mức của người dùng.

Logs: .codex-artifacts/meeting-final-review. Backup: backup/meeting-final-review-20261009-161349. Người dùng yêu cầu không chiếm máy: chỉ code/build/test nền; không mở lại cửa sổ TTSK hoặc Teams.