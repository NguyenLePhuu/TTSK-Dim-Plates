# Điều tra thiếu chữ, lệch câu và báo lỗi dịch

## Bằng chứng

- Đọc UIA chỉ đọc trên Teams đang mở: 4 body phụ đề; mỗi body hiện có 2 Text trực tiếp (tên + lời nói). Không thấy chữ lồng trong lần đo này, nên chưa thể quy mọi thiếu chữ trong ảnh cho bộ đọc. Không thao tác mic/cuộc họp.
- Code cũ chỉ đọc Text con trực tiếp, bỏ qua các container lồng; khi node biến mất giữa chừng thì bỏ qua riêng node và vẫn trả snapshot thiếu. RuntimeId lỗi còn được thay bằng GUID mới, tạo khả năng phát lại cùng câu.
- Tracker cũ dựa vào prefix tối thiểu 6 ký tự. Chuỗi trong ảnh `私はレフトと申し` → `私はレフトもし` → `私はレフトと申します。` có prefix chung 5 ký tự, có thể bị tách Sequence khi sửa ASR. Đã tái hiện bằng test thời gian/ID xác định.
- Worker cũ vẫn công bố bản dịch đang bay dù Teams đã sửa nguồn, đưa bản nháp đó vào context. Preview Nhật đã mới trong khi Việt vẫn là kết quả cũ.
- NVIDIA thật, sample cố định từ ảnh, trên runtime .NET Framework 4.8: trước sửa, `私はレフトと申します。` trả `Tôi là một người theo chủ nghĩa tả.` (2.097 ms). Đây là sai nghĩa tên riêng, không phải mất kết nối.
- Sau sửa, API thật trả `Tôi tên là レフト.` (2.234 ms); giữ đúng cách viết tên từ nguồn, không đoán tên Latin hay lấy tên người nói thay vào câu.
- Sample `Any のちょっちゅう。` bị quality guard từ chối (9.616 ms), được phân loại `TranslationQualityException`. Không kết luận đây là lỗi mạng; không hạ guard để hiển thị bản dịch đoán nghĩa.
- Sample `図面の寸法は250 mmです。` qua NVIDIA thật trả `Kích thước của bản vẽ là 250 mm.` (1.965 ms), giữ đủ số và đơn vị.

## Sửa đổi

1. Duyệt Text trong các container lồng đúng thứ tự; không nối cả Name của cha và con. Node mất làm bỏ cả snapshot và đọc lại. Giới hạn cây vượt ngưỡng báo rõ thay vì trả phần chữ cắt cụt; không tạo GUID để thay node đã mất.
2. Giữ Sequence cho sửa nhẹ cùng câu trong cửa sổ 3 giây, xét tiền tố của câu chưa hoàn chỉnh và khoảng cách chỉnh sửa có giới hạn; hỗ trợ node thay thế khi node trước đã mất. Các lượt không liên quan/đổi người nói hoặc lặp lại sau snapshot trống vẫn riêng biệt.
3. Theo dõi text hiện tại theo Sequence. Bỏ queue item lỗi thời; hủy request đang dịch nếu chính lượt đó đổi chữ, không hủy câu trước chỉ vì người nói đã sang câu khác. Provider bỏ qua cancellation vẫn bị chặn khi kết quả về. Không đưa bản nháp lỗi thời vào context.
4. Form nhận Sequence + text qua `CaptionObserved`. Chỉ ghép Việt với đúng nguồn Nhật đang hiện; khi chờ thì báo chờ. History cập nhật tại chỗ theo Sequence/Revision; giữ các lượt trước.
5. Phân loại quality/auth/HTTP (gồm 429)/timeout/network/response. Không xuất raw exception hoặc key vào giao diện. Banner lỗi dịch được xóa sau thành công, không xóa nhầm lỗi đọc khác.
6. Bảo vệ tên trong mẫu tự giới thiệu trọn vẹn, rõ ràng (`私はXと申します`, `私の名前はXです`, ...), giữ nguyên tên nguồn. Không biến câu nghề nghiệp `私は技術者です` thành tên, không đoán câu còn dở. Quality guard chỉ miễn chữ Nhật của đúng tên đã lấy từ mẫu này; vẫn kiểm tra tiếng Việt/phần còn lại/số liệu.

## Kiểm chứng và bàn giao

- 39 kiểm tra mới: ASR sửa câu, node handoff, lượt mới/lặp thật, hủy request, provider không tuân hủy, context sạch, UIA container lồng thật trên form thử, guard tên và phân loại lỗi, nguồn/bản dịch khớp và cập nhật một card ở light/dark.
- Các bộ trước vẫn đạt: startup 17, preview 37, translator/queue 77, responsive UI 332. Tổng 502 assertions.
- API thật được thử bằng ba câu cố định nêu trên, không gửi thêm toàn bộ cuộc họp. Harness gắn `TargetFramework(.NETFramework,Version=v4.8)` và cấu hình runtime sản phẩm. Các harness cũ chưa gắn target framework từng lỗi WebException; đó không đủ bằng chứng để kết luận mạng/API trong ứng dụng hỏng.
- Release x64 build thành công. Lúc đầu portable bị khóa; người dùng đã đóng TTSK rồi mới cập nhật, không kết thúc tiến trình người dùng bằng công cụ.
- Backup: `backup/meeting-integrity-20261006/`. Log/harness: `.codex-artifacts/meeting-integrity/`. Không sửa DIM/Tekla hay lưu dữ liệu trong model.

## Giới hạn còn lại

UIA cung cấp phụ đề do Teams nhận dạng, không cung cấp âm thanh hay transcript cuối có ID chuẩn. Khi Teams tự nghe sai hoặc không xuất chữ, ứng dụng không thể phục hồi chắc chắn câu gốc. Phân biệt sửa ASR với lượt mới vẫn là heuristic có giới hạn; dữ liệu source khác nội dung tiếng nói cần đối chiếu trực tiếp Teams. Bản dịch tự động không bảo đảm đúng nghĩa mọi câu. Đã kiểm API thật và đọc cấu trúc Teams thật, chưa có kiểm chứng toàn bộ một cuộc họp có tiếng nói biến đổi liên tục sau khi cài bản cuối.
