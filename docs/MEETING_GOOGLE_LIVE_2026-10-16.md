# Google Live — UI chung, ưu tiên tiếng Việt — 16/10/2026

## Cách dùng hiện tại

1. Mở **Dịch họp → Google Live** trong menu provider. Live dùng chính cửa sổ và các thẻ lịch sử của Gemini/Riva, không có form riêng.
2. Chọn đúng loa/tai nghe Teams đang phát, rồi **Bắt đầu**. Dropdown dùng chung `MeetingWindowPicker` và renderer với Gemini/Riva: biểu tượng loa, tên thiết bị, dòng mô tả Teams/mặc định và dấu chọn; hỗ trợ bàn phím và theme sáng/tối. Khung điều khiển chỉ giữ chọn nguồn, Tìm thiết bị, Bắt đầu và Dừng; bỏ Ưu tiên tiếng Nhật/Kiểm tra tiếng. Gợi ý nguồn Nhật vẫn bật như mặc định cũ. **Bắt đầu** mới gửi toàn bộ âm thanh đầu ra đã chọn; không micro, không file âm thanh và không phát tiếng dịch. Không cần Live Captions cho Live.
3. **Chọn model API** dùng hộp quản lý key chung. Live có model cố định `gemini-3.5-live-translate-preview`, ưu tiên nguồn Nhật mặc định, đích Việt. Có thể dùng Google key đã lưu hoặc key Live riêng tại Windows Credential Manager `TTSKDimPlates/GOOGLE_LIVE`. Không thay key/model văn bản.
4. Mặc định **Xem tiếng Nhật được bật**, vẫn giữ **tiếng Việt là chính**: Việt lớn/đậm, Nhật nhỏ phía dưới **ngay trong cùng thẻ**, giống luồng Gemini chính; không xen các thẻ Nhật riêng. Phần nguồn mới nhất xuất hiện ở ô phía trên. Bỏ chọn để ẩn Nhật và mở rộng ô Việt toàn chiều ngang. Nút này chỉ đổi cách đọc, không khởi động lại dịch.
5. Mỗi thẻ có giờ nhận. Tự theo câu mới khi đang ở cuối; cuộn lên đọc cũ thì giữ vị trí. **↓ Về mới nhất** trở lại cuối và tiếp tục tự theo. Chế độ phụ đề, xuất text và xóa phiên dùng các nút cũ; xuất giữ cả nguồn đang ẩn.
6. Dừng giữ lịch sử, Bắt đầu mở phiên Live mới. Chọn Gemini/Riva để quay lại caption, giữ hàng đợi/cấu hình cũ. Tùy chọn xem Nhật của caption và Live độc lập trong mỗi lần mở form, không ép caption chuyển sang chỉ Việt.

## Gom hai ngôn ngữ trong cùng thẻ

`GoogleLiveTranscriptRows` chỉ làm việc trên chữ Google đã trả, không gọi dịch lại hoặc sửa prompt/thu âm. Giữ nguồn và bản dịch của đoạn đang nhận trong một `MeetingTranslationLine`, cùng SessionId/Sequence và revision tăng. Nguồn tới trước được giữ trong RAM, khi có Việt mới hiện thẻ; nguồn tới sau bổ sung ngay dưới bản dịch cũ. Nhiều câu Nhật và một câu Việt có thể nằm chung đoạn, không ép số câu hai phía bằng nhau.

Chốt đoạn khi cả hai phía hết câu và có chữ mới, hoặc không có chữ mới 1,8 giây sau khi đã nhận hai phía. Nếu chỉ có một phía, chờ 10 giây trước khi chốt. Đoạn dài ngắt tại biên chữ sau 480 ký tự; giới hạn cứng 1.200 ký tự mỗi phía (cho phép thêm một code unit để giữ cặp surrogate). Chỉ ngưỡng trình bày: không cắt PCM, không kết thúc WebSocket và không đổi thuật toán dịch. Bảo toàn chữ của mỗi stream và số thập phân; khoảng trắng đầu đoạn có thể được bỏ.

Google trả hai stream độc lập, không có ID ghép câu hoặc tên người nói trong phép đo thực tế của phiên này. Việc gom là theo đoạn nhận để đọc thuận tiện, không bảo đảm đối chiếu chính xác từng câu khi Google chậm/gộp/tách câu. Không tự gán tên người nói. Caption Teams cũ vẫn giữ tên/màu người nói.

## Bảo toàn luồng đang chạy tốt

- Backup trước lượt chỉnh điều khiển: `backup/005-google-live-controls-20261016`; lượt gom thẻ: `backup/004-google-live-vietnamese-20261016`; tích hợp trước: `backup/003-google-live-shared-20261016`.
- `MeetingTranslationService.cs` byte-identical so với trước sửa: reader caption, FIFO, retry, model/key fallback, Google text và Riva không đổi.
- Toàn bộ `GoogleLiveTranslation.cs` (adapter, session, WASAPI, chọn endpoint, PCM converter, giao thức) byte-identical trước lượt chỉnh điều khiển. Không đổi DIM/Tekla/PDF; không ghi folder model. Chọn tay giữ theo ID endpoint; làm mới không tự thay đầu ra nếu thiết bị đã chọn mất kết nối.
- Timer UI 100ms, queue/epoch/cancellation giữ cơ chế cũ. Callback caption hoàn tất khi đang Live vẫn lưu lịch sử nhưng không chiếm ô Live. Callback nguồn khi ẩn không làm vùng bản dịch đổi kích thước. Dừng/đóng chờ giải phóng capture/send/receive.
- Dữ liệu vẫn lưu cả Nhật/Việt trong RAM/export; không xóa nguồn để làm giao diện gọn. Clear xóa cả buffer và lịch sử, các đoạn mới nhận tiếp bình thường.

## Kiểm tra lượt chỉnh giao diện

`pwsh -NoProfile -File tests/MeetingLiveShared/Run.ps1` build Release x64 và chạy giao diện trên desktop cô lập, không chuyển desktop hay thao tác cửa sổ người dùng.

- Core: 100 cách chia/interleave packet, giữ chữ nguồn/Việt, Unicode, số thập phân, không gán tên; gom nguồn tới trước/sau, hai câu nguồn với một bản dịch chậm; session/revision; parser, PCM các format và WASAPI khởi tạo/hủy. Lượt chạy hiện tại 16.449 assertion với 4 endpoint.
- UI: 120 assertion sáng/tối; Nhật bật sẵn, Việt vẫn nổi bật; nguồn trong cùng thẻ; ẩn nguồn mở rộng Việt; giữ export, tùy chọn caption và vị trí cuộn; clear/restart/key sai. Dropdown chung được kiểm F4/Space/Escape, chọn đúng ID và giữ chọn tay, mất endpoint không tự đổi loa, trạng thái rỗng, đóng khi disable/ẩn/resize và dispose theo form. Nút/picker cùng hàng/cùng chiều cao ở 1040×780 và 880×620. Ảnh thường và popup native `PrintWindow` trong `.codex-artifacts/meeting-live-shared-tests/ui`; không chuyển desktop người dùng. Popup bỏ PRF_NONCLIENT khi nhận WM_PRINT: ảnh xem trước không dựng thêm caption/viền Windows che hàng đầu; đối chiếu pixel DrawToBitmap với PrintWindow ở hai theme đạt sai khác dưới 1%.
- OnlineSynthetic ở lượt gom thẻ trước: ba câu Nhật tổng hợp trong RAM, stereo 48kHz qua converter mono 16kHz, khoảng nghỉ 3 và 10 giây. Kết quả **3 thẻ song ngữ**, giữ đủ chữ của cả hai stream, 8 cập nhật Việt từ 3.497ms đến 25.203ms. Lượt sửa điều khiển không gọi lại API vì engine không đổi. Không dùng phép thử trước để khẳng định mọi bản dịch ngữ nghĩa hoàn hảo.
- Có 3 warning cũ: CS4014, CS0067 trong service, CS0169 ở subtitle form. Không warning mới. Không commit/push/phát hành. Người dùng đã xác nhận bản gom thẻ hoạt động tốt; lượt chỉnh điều khiển mới kiểm offline, chưa thao tác cuộc họp thật.

## Lịch sử âm thanh và giới hạn

Trước khi tích hợp UI, đã xác nhận có lúc Teams active ở Earbuds nhưng app chọn C2423H; sửa ưu tiên endpoint theo session Teams. Các lượt silence tiếp theo không đủ bằng chứng quy lỗi Bluetooth. Sau khi người dùng mở loa, đo local có 149/149 chunk khác 0 trong 14,9 giây, peak 0,42328. Người dùng đã xác nhận dịch ổn trước lượt chỉnh giao diện này. Không dùng kết quả đó thay cho xác nhận cuộc họp thật với UI vừa sửa.

Model Live có thể dịch sai nghĩa: phép thử trước từng trả “chưa tháo” cho câu “chưa lắp” về bu lông. Lượt này giữ nguyên bộ dịch đang được người dùng chấp nhận; không thay model/prompt để che sai số hoặc tuyên bố hoàn hảo.

## Nguồn chính thức

- [Google Live Translation](https://ai.google.dev/gemini-api/docs/live-api/live-translate)
- [Live transcription](https://ai.google.dev/gemini-api/docs/live-api/live-transcribe)
- [Live API schema](https://ai.google.dev/api/live)
- [Microsoft WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)

## Bản portable sau lượt chỉnh điều khiển

Release x64 tại .codex-artifacts/portable-update/20261016-115257-22dd08d8; xác minh 40/40 runtime và 41/41 mục SHA256.csv. Entrypoint TTSK_AutoDim_Plates.Program.Main; không còn form Live riêng. EXE SHA-256: DF3C6CDE06BF89083F7776AC69F608069925DDE043497C91AAC6367FCB4EA691. Bản chạy đã cập nhật, không commit/push/phát hành.
