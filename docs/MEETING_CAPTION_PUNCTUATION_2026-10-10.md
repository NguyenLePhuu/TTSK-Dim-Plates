# Lỗi thừa dòng phụ đề do dấu câu — đã bắt được trên Teams thật

## Nguyên nhân đã xác minh

Trong khi người dùng phát âm thanh qua Teams, probe chỉ đọc UIA bắt được cùng body ID `42.1378156.4.5.26.5325`, cùng người nói:

| Mốc probe (ms) | Nguyên văn Teams |
| --- | --- |
| 13551 | まあ |
| 14074 | まあ仕事 |
| 14612 | まあ、仕事と人生 |
| 18578 | まあ、仕事と人生ねむちゃむちゃ大好大きなテーマですよね |
| 21769 | まあ、仕事と人生ねむちゃむちゃ大好大きなテーマですよね。 |

Ở 14612 ms, tracker cũ gọi Emit cho `まあ仕事` và tạo Sequence mới cho `まあ、仕事と人生`. Đây là đoạn tồn tại dưới 800 ms, đáng lẽ còn trong settling; nhưng nhánh nhận diện “node tái dùng cho câu mới” ép flush ngay. Sau đó thẻ fragment và thẻ đầy đủ tồn tại song song.

Điều kiện cũ so tiền tố nguyên văn nên gặp dấu `、` mới chèn thì tiền tố chỉ còn `まあ`. IsCorrection loại nguồn ngắn dưới 6 ký tự trước khi kiểm sửa nhẹ; `まあ仕事` có 4 ký tự. Vì thế cùng một dòng đang được Teams hoàn thiện lại bị phân loại thành hai lượt. Đây là lỗi tracker chung, xảy ra trước API Google/NVIDIA; không phải model tự cắt câu, bộ gom 16 câu hoặc cache tạo ra fragment.

Giả thuyết đầu phiên về body thay RuntimeId không phải nguyên nhân trong dữ liệu live vừa bắt được. Thử nghiệm alias theo khung cha đã được gỡ hoàn toàn trước build bàn giao. Cây live cho thấy cha Flex dùng chung nhiều caption, không phải danh tính riêng của từng câu.

## Bản sửa có phạm vi hẹp

- Thêm IsSameRowPunctuationRevision: chỉ dùng sau khi đã khớp ID dòng và người nói. Key so sánh loại dấu câu/khoảng trắng/format và chuẩn hóa width; **nguồn thật không bị sửa**, không trim/ghép/cắt chữ.
- Nếu chỉ đổi dấu câu, hoặc tiếp tục câu chưa hoàn tất sau khi chèn dấu, giữ Sequence và tăng Revision. Nếu thẻ ngắn đã dịch thì full text cập nhật thẻ đó; nếu API vẫn xử lý bản cũ thì cơ chế IsCurrent hiện có loại kết quả cũ.
- Phục hồi cùng ID qua khoảng UIA trống ngắn và đổi thứ tự nhãn tên người nói cũng dùng kiểm dấu câu này.
- Không suy ra cùng lượt từ tiền tố ở các ID khác nhau. Hai dòng thật cùng tiền tố, hai người nói hoặc câu lặp thật có ID riêng vẫn giữ riêng. Câu đã kết thúc rồi node tái dùng cho câu mới không được nối chỉ vì cùng từ đầu.
- Không đổi reader UIA, identity/alias, thời gian settling 400/800 ms, prompt/API, batch, RPM, memory, UI hay thuật toán Tekla.

## Bằng chứng và kiểm tra

- Live baseline: `.codex-artifacts/caption-identity/baseline-live.log`. Probe đọc 45 giây, ghi 56 snapshot thay đổi; thấy chính lỗi `まあ仕事` được flush thành Sequence riêng.
- Replay toàn bộ baseline bằng tracker sửa: 16 body Teams → 16 dòng cuối. Tập nội dung Nhật cuối khớp từng ký tự với nội dung cuối từng body, không còn dòng dư `まあ仕事`.
- Đọc lại Teams thật trên build sửa 45 giây: `.codex-artifacts/caption-identity/fixed-live.log`. Có 5 snapshot thay đổi, chủ yếu các caption cũ hết thời gian hiển thị; replay đạt 14 body → 14 dòng cuối đúng nguyên văn. Không tuyên bố lần live sau đã quan sát đủ mọi kiểu sửa câu của Teams.
- CaptionPunctuationTests: 30 kiểm tra đạt, thêm 3 kiểm replay fixed-live. Có case source cùng screenshot, dấu ngoặc/phẩy, nguồn 2 ký tự, chờ >3 giây, shortening thật, full-width/space/newline, M20/M22, đổi người nói, co-present rows và request đang bay/đã công bố.
- VerbatimTests cũ: 18 kiểm tra đạt; bảo vệ khỏi regression mất nguồn/tự gộp hai ID theo prefix.
- OrderedPipeline: 44 kiểm tra đạt. GoogleBatchTests: 121 kiểm tra đạt. Tổng lượt kiểm đạt trong lần sửa: 216, có cả replay dữ liệu live; không gọi thêm Google/NVIDIA thật trong lần điều tra này.
- Build Release x64/C# 7.3/.NET Framework 4.8 và portable: `.codex-artifacts/portable-update/20261010-194928-0c4a10fb/`. SHA-256 so output build với đủ 40 file runtime; metadata phát hành không được tạo lại bởi BuildOnly.
- Harness: `tests/MeetingTranslation/CaptionIdentityProbe.cs`, `CaptionPunctuationTests.cs`, `Run-CaptionIdentity.ps1`. Probe chỉ đọc Teams, không thao tác âm thanh/cuộc họp, không ghi vào folder model Tekla, không commit/push/phát hành.

## Phạm vi “giống Teams”

Nguồn Nhật đi qua ứng dụng được giữ nguyên từ UIA; sửa dấu câu trong cùng dòng phải cập nhật đúng dòng đó. Bản dịch tiếng Việt không thể “giống từng ký tự” nguồn Nhật hoặc được bảo đảm đúng mọi nghĩa. Ứng dụng cũng không phục hồi được chữ Teams đã xóa trước khi UIA kịp đọc hoặc nội dung Teams không xuất qua API accessibility. Bản sửa khắc phục nguyên nhân đã bắt được, không tuyên bố bao phủ mọi thay đổi UIA chưa quan sát.

Các thẻ dư trong tiến trình cũ không được tự dọn hồi tố bằng cách so tiền tố vì dễ xóa nhầm câu thật. Đóng/mở bản portable mới và bắt đầu phiên mới để dùng tracker đã sửa; lịch sử chỉ lưu RAM như trước.
