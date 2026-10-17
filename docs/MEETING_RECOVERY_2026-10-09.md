# Phục hồi câu dịch khi API lỗi hoặc nhiều câu tới liên tục

## Nguyên nhân

`TeamsDesktopCaptionCoordinator` trước đây giữ worker bên trong vòng retry/delay của lỗi mạng. Cả ba worker có thể cùng chờ; NVIDIA còn tự retry HTTP nhiều lần bên trong một lượt dịch. Các lỗi chất lượng, phản hồi trống/hỏng hoặc cấu hình đi ra nhánh `TranslationFailed` rồi mất khỏi công việc cần dịch. Tracker không gửi lại câu Teams không đổi, nên dòng lỗi không có cơ hội phục hồi.

## Thay đổi

- Giữ công việc thất bại trong RAM của phiên theo Sequence/Revision, nguyên văn tiếng Nhật và tên người nói. Lịch retry riêng kiểm mỗi 100 ms; worker trả về xử lý công việc khác ngay khi một lượt dịch thất bại.
- Mạng/timeout/HTTP tạm thời tự thử lại sau 1, 2, 4, 8 rồi tối đa 15 giây; 429 tối thiểu 2 giây, tôn trọng Retry-After tối đa một ngày. Hạn 429 dùng chung cho provider. Các câu đến trong khoảng chờ không tạo HTTP mới, không tăng bộ đếm lỗi và không kéo dài hạn chờ đó.
- Đầu ra không hợp lệ/chất lượng không đạt: tối đa ba lượt điều phối rồi giữ chờ người dùng bấm Dịch lại. HTTP 400/413/422 giữ để thử thủ công. Key/quyền/model/quota cố định dừng tự gọi tiếp; bấm Dịch lại gỡ khóa thử lại trong phiên. Không tự thay model hoặc gửi sang provider khác.
- Production NVIDIA chỉ thực hiện một lần HTTP cho mỗi bước trong mỗi lượt điều phối; vòng chất lượng, cache pivot hợp lệ, biên tập và kiểm tra thuật ngữ giữ nguyên. Bộ kiểm tra key/translator độc lập vẫn dùng chính sách HTTP cũ.
- Giữ ba worker và cơ chế ưu tiên câu mới. Kết quả retry cập nhật cùng dòng, không tạo câu trùng. Revision cũ bị hủy khi Teams sửa chữ; không ghi đè preview của câu mới.
- UI thêm Dịch lại (số câu), trạng thái Chờ dịch lại/Cần thử lại và tooltip nguyên nhân. Bản nháp dùng được không bị thông báo lỗi ghi đè. Giới hạn 200 dòng chỉ loại dòng đã dịch; không loại dòng đang chờ.
- Stop hủy cả scheduler và request; không chạy retry muộn sau đóng. Pending là dữ liệu của phiên đang chạy, không phải lưu bền qua tắt ứng dụng. Dừng phiên/chuyển provider/khởi động lại không khôi phục tự động công việc cũ. Key/model vẫn được đọc khi bắt đầu phiên.

## Kiểm tra

Không sửa thuật toán DIM/Tekla, bộ đọc UIA, chuẩn hóa hoặc settling phụ đề. Chỉ sửa hai file sản phẩm hiện có: MeetingTranslationService.cs và MeetingTranslatorForm.cs.

- Backup hash-verified: `backup/meeting-recovery-20261009-094332`.
- RecoveryTests: 50 kiểm tra ban đầu, gồm 24 câu dồn có lỗi mạng, quality retry/thủ công, Google phản hồi 200 hỏng, 429 trong khi câu mới tới, key lỗi, stop, revision và pipeline NVIDIA 12 câu với HTTP 503. Đầu vào/đầu ra giả lập; không phải số đo tốc độ NVIDIA thực tế. Câu mới trong test 24 câu hoàn tất khoảng 600–611 ms gồm settling.
- GoogleTests 71, VerbatimTests 18, RivaSourceContractTests 38, LivePriorityTests 27: đều qua. Hai assertion FIFO của bài Riva cũ được thay bằng hợp đồng mới: nguồn giữ nguyên, mọi câu phục hồi, retry không chặn câu tiếp theo. Bản test cập nhật nằm trong thư mục artifact mới; không sửa test gốc.
- Native WinForms được kiểm trên desktop riêng, không chiếm desktop người dùng. Kiểm bản nháp, cập nhật đúng dòng và bảo vệ hơn 200 câu pending; ảnh light/dark ở 1040×780, 1920×1080. Kiểm UI cuối bổ sung 880×620.
- API thật, dùng hai câu thử cố định, không đổi key/model lưu sẵn: Google gemini-3-flash-preview trả đủ hai câu khoảng 1.972 và 2.400 giây gồm settling, không retry. NVIDIA gặp HTTP 502 và timeout; một câu phục hồi thành công ở khoảng 37.102 giây, câu còn lại chưa hoàn tất khi kết thúc bài thử 45 giây. Không tuyên bố NVIDIA luôn nhanh hay đã xác nhận toàn bộ cuộc họp thực tế.
- Lần harness live đầu thiếu TargetFramework 4.8 nên gặp lỗi TLS, không dùng kết quả đó đánh giá sản phẩm. Harness đúng target được chạy lại, kết quả như trên.
- Artifact: `.codex-artifacts/meeting-recovery/` (harness, log, ảnh). Không chứa key. Gói cuối: `.codex-artifacts/portable-update/20261009-095525-6bb07f9a/build`, Release x64.

## Giới hạn thực tế

Không thể đảm bảo mọi câu có kết quả trong vài giây khi dịch vụ trả 429/5xx, mất mạng hoặc phản hồi chậm. Bản sửa đảm bảo công việc lỗi được giữ trong phiên và có đường phục hồi; không đổi câu Nhật, không coi thông báo lỗi là bản dịch thành công. Khả năng đọc phụ đề thực tế vẫn phụ thuộc Teams cung cấp cây UIA và nội dung còn hiển thị.
