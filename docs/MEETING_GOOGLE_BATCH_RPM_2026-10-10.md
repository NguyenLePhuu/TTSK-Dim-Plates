# Gemini 3.5 Flash Lite: gom phụ đề và giới hạn 15 RPM

## Phạm vi và hành vi

Trước thay đổi, Google có một worker, mỗi TeamsCaptionUpdate gọi generateContent riêng. Một lượt có thể xuống nhiều dòng khi hiển thị, nhưng không có cơ chế gom ba lượt phụ đề/request. Thay đổi chỉ nằm trong MeetingTranslationService.cs, áp dụng gom lô và điều tiết cho đúng `gemini-3.5-flash-lite`. NVIDIA, Azure, các model Google khác, bộ đọc Teams, tracker/settling và UI giữ luồng cũ.

### Nhịp gửi

- GoogleModelRequestPacer cách hai lần cấp lượt ít nhất **4,1 giây**. Với 16 lượt cần 61,5 giây; không có burst ở đầu/cuối phút. Khi có đủ nguồn liên tục, trần danh nghĩa khoảng 14,6 request/phút, không cố dùng đủ 15 khi không có câu.
- Cổng dùng chung theo model cho các key trong cùng tài khoản Windows/phiên Windows trên máy. Named mutex bảo vệ việc đọc/ghi; thời điểm tiếp theo nằm trong LocalAppData/TTSK/MeetingTranslation/google-rpm-[hash-model].txt. Tạo lại translator, dừng/tiếp tục hay mở lại chương trình không xóa nhịp. File ghi bằng thay thế nguyên tử; cache hỏng tự dựng lại và chờ 61 giây. Không lưu key hoặc phụ đề, không ghi thư mục model Tekla.
- Request đơn, gom lô, retry và Lưu & kiểm tra đều đi qua cổng này với Flash Lite. models.list không phải generation và không dùng cổng. Lỗi lưu trạng thái không cho đi tắt gửi HTTP.
- HTTP 429 dùng chung hạn chờ ít nhất 61 giây hoặc Retry-After lớn hơn (giữ giới hạn 24 giờ hiện có). Retry không mất nguồn. Quota ngày/quota bằng 0/quyền truy cập vẫn dùng cơ chế giữ câu và báo lỗi cấu hình cũ; không retry nóng.
- Đây là giới hạn từ TTSK trong phạm vi trên. Google áp dụng hạn mức theo project, nên ứng dụng khác, máy khác hoặc key khác cùng project vẫn có thể gây 429. RPM không thay thế TPM/RPD. Ảnh người dùng có RPD 500; ở mức gửi liên tục khoảng 14,6/phút, 500 lượt tương ứng khoảng 34 phút nếu toàn bộ ngân sách ngày dành cho phiên này. Đó là giới hạn ngân sách, không phải thời lượng họp được bảo đảm.

### Gom và trả từng dòng

1. Worker cũ giữ câu đầu và chờ lượt gửi. Sau khi được cấp lượt, chốt lại snapshot mới nhất từ queue: nhận thêm các câu đã ổn định trong lúc chờ, tối đa **8 lượt, tổng 4.000 ký tự nguồn**. Nếu câu tiếp theo chưa ổn định hoặc đã có bản dịch buffered thì dừng gom tại đó. Một câu không bị cắt để vừa lô.
2. Có một câu: giữ payload/prompt dịch đơn cũ. Có nhiều câu: JSON `{captions:[{id,text}]}` với id riêng trong request và text Nhật giữ nguyên, kể cả khoảng trắng, xuống dòng, câu lặp. Đây là một generateContent trực tiếp, không dùng dịch vụ Batch API bất đồng bộ.
3. Yêu cầu structured JSON `{translations:[{id,text}]}`. Nhận đủ id đúng tập, không trùng, không rỗng; id trả đảo thứ tự vẫn ánh xạ chính xác. finishReason phải STOP. Không nhận output bị cắt, JSON lỗi, thiếu/thừa id, Unicode replacement hoặc ký tự điều khiển ngoài tab/xuống dòng. Không tự đoán và sửa văn bản hỏng.
4. Trước khi công bố, kiểm lại revision/text từng câu. Bản cũ của câu đã bị Teams sửa bị loại. Kết quả còn hợp lệ được giữ trong `_buffered`; các câu vẫn ở queue và đi qua `PublishInOrderAsync` cũ, cập nhật từng thẻ theo Sequence. Không đưa cả lô thành một thẻ, không dịch lại phần buffer còn đúng, không để câu sau vượt câu trước.
5. Lô lỗi cấu trúc/ký tự: chưa công bố bất kỳ phần nào, giữ đủ nguồn. Lượt retry dịch riêng câu đầu; thành công thì giảm trần lô còn một nửa số câu của lô lỗi, không tăng lại trong phiên. Ví dụ lô 8 lỗi → sửa câu đầu → lô tối đa 4. Schema HTTP 400/422 bị từ chối thì dùng nhánh đơn trong phiên. Tất cả lượt sửa vẫn qua cổng RPM và retry cũ; không tách thành nhiều HTTP gửi dồn.
6. Pause khi còn chờ lượt gửi không gửi lô mới; HTTP đã bắt đầu được hoàn tất theo hành vi cũ. Resume tiếp tục nguồn đang giữ. Đóng hủy cả chờ quota và HTTP. Câu chưa dịch chỉ tồn tại trong RAM phiên như trước, không có persistence phụ đề sau khi thoát.

Timeout HTTP đơn giữ 8 giây; lô nhiều câu có trần 20 giây và tối đa 8192 output tokens, so với 4096 của nhánh đơn. Chờ lượt RPM nằm ngoài timeout mạng.

## Kiểm chứng

- Build Release x64 / C# 7.3 / .NET Framework 4.8; portable cập nhật bằng Update-Portable.ps1 -BuildOnly -NoPause. Build cuối: `.codex-artifacts/portable-update/20261010-150756-c24564a4/`.
- GoogleBatchTests: **55 kiểm tra offline đạt trên build cuối**, mô phỏng 1.000 lượt qua nhiều instance, mọi cửa sổ 60 giây ≤15 lượt; kiểm 5 tiến trình tranh cùng quota, persistence khi tạo lại instance, đổi giờ lùi, cache hỏng, hủy chờ. Đo HTTP qua transport giả và đồng hồ thật: các lượt 4 / 4121 / 8233 ms.
- Hồi quy batch: 41 câu ngắn dùng 6 HTTP, 41 dòng đúng nguồn/thứ tự; source lặp/nguyên văn; giới hạn ký tự; sửa câu đầu/giữa/đã công bố trong lúc lô chạy; giữ buffer hợp lệ; pause/resume khi chờ quota; output thiếu/trùng/sai id/Unicode hỏng/MAX_TOKENS; hạ kích thước lô để phục hồi; retry không vượt cổng.
- Bộ OrderedPipeline: 44 kiểm tra đạt; Bottleneck: 20 kiểm tra đạt trên build trước phần bổ sung guard Unicode/thu nhỏ lô Google. Các phần NVIDIA được kiểm không thay đổi ở build cuối.
- Google thật: đã kiểm lô 8 câu và coordinator thật với caption đầu vào giả lập. Một mẫu đầu trả đủ 8 câu trong 1.928 ms, một mẫu sau 3.124 ms. Không dùng các số này làm cam kết độ trễ.
- Trong kiểm thử thật từng gặp output có ký tự điều khiển thay chữ tiếng Việt; đã bổ sung guard và tình huống hồi quy tương ứng trước bàn giao. Lần live sau guard đạt **7 kiểm tra**, đủ id/Unicode, bảo toàn M20/12mm, giữ 8 nguồn và câu lặp đúng thứ tự; coordinator hoàn tất sau 2424 ms, gồm thời gian chờ nhịp. Live chạy trước chỉnh cuối về giảm trần lô khi phục hồi; đường dịch thành công không đổi. Kiểm giao thức không chứng minh mọi lựa chọn từ/chính tả của model đều hoàn hảo; ứng dụng không tự viết lại bản dịch.
- Log và harness: `.codex-artifacts/google-batch/tests/`, `tests/MeetingTranslation/Run-GoogleBatch.ps1`. Key/model lưu trước/sau live-test không đổi. Không gọi DIM, không đổi bản vẽ hoặc ghi folder model, không commit/push/phát hành. Chưa thử trong cuộc họp Teams thật trong lần sửa này.

## Nguồn

- [Google: RPM/TPM/RPD và hạn mức theo project](https://ai.google.dev/gemini-api/docs/rate-limits).
- [Google: structured outputs và giới hạn schema](https://ai.google.dev/gemini-api/docs/structured-output).

Các mô tả “một request mỗi câu”/“Google batch đã hoãn” trong tài liệu ngày 2026-10-09 là hành vi trước thay đổi này.
