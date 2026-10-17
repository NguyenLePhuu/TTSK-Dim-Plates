> Báo cáo lịch sử. Hành vi cho phép vượt câu lỗi trong tài liệu này đã được thay thế bởi MEETING_STRICT_SEQUENCE_2026-10-09.md theo yêu cầu tuần tự nghiêm ngặt mới nhất.

# Chuẩn hóa NVIDIA/Riva và điều phối câu dịch — 09/10/2026

## Phạm vi cuối

Theo điều chỉnh của người dùng, hoàn thiện NVIDIA trước; Google làm sau. Chỉ sửa hai file sản phẩm có sẵn trong MeetingTranslation: MeetingTranslationService.cs và MeetingTranslatorForm.cs. Không thay bộ đọc Teams, thuật toán DIM/Tekla, key hoặc model Google đã lưu. Thử nghiệm Google batch được cất ở `.codex-artifacts/meeting-fifo/google-batch-deferred.txt`, không đưa vào sản phẩm. Khối GoogleTranslationException/GoogleJapaneseVietnameseTranslator đã đối chiếu bằng chuỗi với backup và giữ nguyên. Điều phối FIFO/pause chung cũng được dùng khi chọn Google, nhưng chưa tuyên bố luồng Google đã hoàn thiện.

## Nguyên nhân được xác minh

1. TTSK trước đây hủy request của worker ưu tiên để nhường câu mới, rồi gửi lại câu cũ. Hủy phía client không bảo đảm nhà cung cấp ngừng tính request. Khi phụ đề liên tục, phát sinh công việc thừa và các lượt khó hoàn tất.
2. Ba worker và retry cùng thức dậy có thể gửi dồn. Ảnh người dùng còn thể hiện phiên đã Dừng nhưng các dòng vẫn ghi Chờ dịch lại: form đã dispose coordinator nên thực tế không còn tác vụ nào thử lại.
3. NihongoFlow có đường realtime chỉ dùng bản Riva đã qua guard. TTSK gọi editor cả khi bản Riva đã dùng được.
4. NVIDIA thật có độ trễ ở ja-en, không chỉ do queue: trace ghi request 30 giây timeout, request 5 giây bị ngắt bởi ngân sách mới. Key của TTSK và NihongoFlow giống nhau (chỉ ghi boolean, không xuất key). Câu kiểm tra cố định HTTP 200 trong 842 ms và 2955 ms ở hai lần gọi; câu phụ đề trộn Nhật/Anh có lần quá 12 giây, lần sau 8312 ms với payload giống nhau. Không kết luận key hỏng hoặc chỉ do serialization.

## Hành vi hiện tại

- Một worker production gửi câu theo Sequence/FIFO. Không hủy câu đang chạy vì có câu mới. Teams sửa chữ vẫn cập nhật queue; kết quả revision cũ bị chặn, không dùng vào context hay UI.
- Riva ja-en → en-vi có ngân sách tổng 5 giây trong chế độ realtime. Bản đạt guard được trả ngay, không thêm editor. Bản cần cứu dùng model NVIDIA Nemotron đã có trong ứng dụng, dịch trực tiếp Nhật → Việt tối đa 4 giây.
- Cứu trực tiếp gửi nguyên văn câu Nhật làm user message, không đưa lịch sử vào prompt để tránh tự hoàn thiện câu ngắn. Giữ guard về ngôn ngữ, mã/số, thuật ngữ và tính đầy đủ; đầu ra finish_reason khác stop không được coi là thành công.
- Khi Riva timeout/5xx/lỗi dịch vụ phù hợp, tạm dùng đường NVIDIA trực tiếp trong 30 giây trước lần dò Riva kế tiếp. Tránh mỗi câu đều mất thêm 5 giây vào cùng endpoint đang chậm. Không dùng fallback để vượt 429, 401 hoặc 403. HTTP/Retry-After của đường trực tiếp cũng đi qua bộ phục hồi, không bị che thành lỗi chất lượng.
- Chờ cooldown trước lấy câu; chưa gửi HTTP thì không tạo dòng TranslationFailed giả. Chỉ câu có lỗi thực được giữ pending. Câu lỗi vẫn có thể được xử lý lại sau các câu đã sẵn sàng; FIFO không có nghĩa ép mọi câu thành công phải chờ một câu không dịch được.
- Dừng trở thành pause: ngừng nhận/gửi mới, hoàn tất câu đang chạy, giữ tracker/queue/pending trong RAM. Tiếp tục dùng lại đúng coordinator. Đóng cửa sổ mới hủy/dispose; chưa có lưu bền qua tắt ứng dụng.
- Provider menu/timing mô tả Riva và NVIDIA dự phòng. Không cho đổi provider khi phiên còn công việc giữ lại, để tránh mất câu khi chuyển luồng. Không đổi model/key Google tự động.

## Kiểm chứng

Backup hash-verified: `backup/meeting-fifo-20261009-115631`.

- NvidiaRecoveryTests: 23 kiểm tra qua, gồm chậm 30 giây được cứu trong ngân sách, circuit không lặp chờ mỗi câu, thứ tự và nguồn nguyên văn, output Riva hợp lệ không gọi editor, pause/resume, giữ lượt nói lặp giống chữ, 429 không bị vượt bằng editor, đầu ra bị cắt không được công bố.
- VerbatimTests 18 và RivaSourceContractTests 38 qua: reader/normalizer/tracker/payload nguyên văn, corpus nguồn, revision, backlog 71 câu và recovery. Bản translator standalone vẫn giữ retry/quality cũ; thay ngân sách chỉ ở production realtime.
- PauseUiTests 10 qua trên desktop Windows riêng, light/dark, kích thước 880×620 và 1040×780. Không điều khiển desktop của người dùng.
- Teams live trước sửa: Google dịch 8 câu rồi 429 với Retry-After khoảng 24 giây; lần sau báo hết quota. Đã dừng kiểm Google theo yêu cầu mới. Không dùng các lỗi này để kết luận NVIDIA hỏng.
- Teams live NVIDIA trước giới hạn: 0 câu xong/40 giây, ja-en timeout30 giây. Sau thêm giới hạn + cứu câu: 6 câu xong, 0 failed attempts trong 40 giây, còn backlog. Kết quả này khiến bổ sung circuit nghỉ Riva 30 giây, tránh lặp chờ 5 giây cho mọi câu.
- Vùng phụ đề sau đó không còn chữ nên kiểm circuit bằng API thật với 3 câu cố định, gồm câu phụ đề từng bị treo: trả đủ 3 câu/12.446 giây, một lượt phải retry. Câu đầu total6.575 giây, câu thứ ba total11.775 giây (có hàng chờ), câu thứ hai hoàn tất retry total12.403 giây. Không gọi đây là chứng minh mọi câu luôn dưới10 giây hoặc một cuộc họp dài đã hết backlog.
- Build cuối Release x64: `.codex-artifacts/portable-update/20261009-121332-94abf169/build`; runtime portable kiểm40 file, manifest SHA256 kiểm41 mục. Artifact kiểm tra tại `.codex-artifacts/meeting-fifo/`.

## Giới hạn

Giữ nguyên nguồn không bảo đảm API dịch ngữ nghĩa luôn đúng, đặc biệt phụ đề sai/trộn ngôn ngữ hoặc chỉ vài ký tự. Ngân sách xử lý mỗi lượt không bao gồm thời gian xếp hàng hoặc Retry-After do dịch vụ yêu cầu. Nếu API trả chậm hơn tốc độ nói kéo dài, backlog vẫn tăng; không bỏ câu để làm số đo đẹp hơn. Các câu chưa xong được giữ trong phiên đang mở, người dùng có thể Tiếp tục/Dịch lại.
