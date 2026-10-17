# Dịch họp với Google AI Studio — 2026-10-09

## Bản bàn giao

Google Gemini thay lựa chọn Azure trên giao diện. NVIDIA, mã backend Azure và credential cũ giữ nguyên. Không thay thuật toán đọc Teams, tracker, chia câu, ưu tiên câu mới, giữ câu cũ hoặc thuật toán Tekla/DIM. Chỉ sửa ba file C# có sẵn: MeetingTranslationService.cs, MeetingTranslatorForm.cs, ShortcutSettingsForm.cs.

Key người dùng cung cấp đã gọi Google thành công. Model được kiểm tra và chọn ban đầu: **gemini-3.5-flash-lite**. Key không tự quyết định một model duy nhất; ứng dụng chọn model trong mỗi yêu cầu. Key/model lưu trong Windows Credential Manager với target `TTSKDimPlates/GOOGLE_GEMINI`, riêng với NVIDIA/Azure. Không đưa key vào source, URL, log hoặc tài liệu.

## Đổi model

1. Shortcut Settings → API dịch họp → Google AI Studio.
2. Giữ ô key trống để dùng key đã lưu, hoặc nhập key mới.
3. Bấm **Chọn model**. Danh sách lấy trực tiếp từ key hiện tại, có các model Gemini hỗ trợ generateContent phù hợp văn bản, bao gồm Flash/Pro và lựa chọn Tự chọn nhanh. Không đưa model tạo ảnh/âm thanh/robotics/chuyên dụng vào danh sách dịch họp. Vẫn có thể nhập tên model thủ công.
4. Chọn model, nhấn **Lưu & kiểm tra**. Chọn trong menu chưa thay cấu hình đang lưu. Chỉ khi dịch thử thành công mới lưu key/model; lỗi giữ cấu hình trước đó.
5. Cửa sổ Dịch họp → GOOGLE AI → Bắt đầu. Nếu đang dịch, Dừng rồi Bắt đầu lại để nạp model mới. Di chuột lên nút GOOGLE AI để xem model đã lưu; tooltip từng bản dịch ghi model thực sự dùng trong phiên.

Model có trong models.list chưa bảo đảm key có quyền/quota generateContent. Kiểm tra thực tế: gemini-2.5-flash-lite xuất hiện trong danh sách nhưng trả 404; gemini-3.5-flash-lite dịch được. Vì vậy auto dò danh sách rồi dịch thử; nếu 404 thử model Flash khác có trong danh sách. Không tự đổi model âm thầm giữa phiên, không chuyển nhà cung cấp khi lỗi. Pro hoặc model khác có thể chậm hơn; kiểm tra thành công không bảo đảm độ trễ/quota về sau.

## Giao thức

- GET `https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000`; có phân trang và chống trùng, pageToken URL-encode.
- POST `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`.
- Chỉ dùng header `x-goog-api-key` cho cả key AQ. và AIza, không kết hợp Bearer/query key. HTTP redirect tắt để không chuyển key sang endpoint khác.
- Caption được giữ từng ký tự trong `contents[0].parts[0].text`; chỉ dẫn dịch nằm riêng trong systemInstruction. Nhật → Việt trực tiếp, một generation/câu, không Riva, pivot tiếng Anh hoặc editor.
- Giảm thinking theo họ model đã biết; maxOutputTokens 4096. Chỉ công bố bản dịch có finishReason STOP, không rỗng; loại phần thought, ghép phần text. MAX_TOKENS/bị chặn/JSON lỗi không được hiển thị như bản dịch hoàn chỉnh.
- Timeout 8 giây mỗi yêu cầu, hủy theo Dừng/đóng. Lỗi mạng tạm thời vẫn dùng cơ chế giữ câu và thử lại; vì vậy tổng thời gian khi lỗi có thể hơn 8 giây.
- 429 tôn trọng Retry-After/RetryInfo và chia sẻ chờ giữa worker. Quota bằng 0/quota ngày nhận từ QuotaFailure là lỗi cần xử lý tài khoản, không thử vô hạn. 401/403/404/quota cố định chặn gửi lặp trong phiên, vẫn giữ nguồn câu lỗi. Không in nội dung lỗi thô từ máy chủ.

## Kiểm chứng

Build Release x64 cuối: `.codex-artifacts/portable-update/20261009-091643-94db12cb/build`.

357 kiểm tra đạt trên bản cuối:

- GoogleTests 71: payload nguyên văn, đúng host/header, không cầu nối, parsing/blocked/truncated/thought, phân loại lỗi, retry/cancel, model discovery + generation validation, ưu tiên câu mới/không mất câu, nhập key/model hai theme.
- MenuTests 165: hồi quy lỗi disposed menu và native mouse chọn NVIDIA/Google, Escape, đóng/mở nhiều lần, cửa sổ nhỏ, đóng form khi menu mở.
- VerbatimTests 18; RivaSourceContractTests 38; LivePriorityTests 27.
- ModelTests 38: phân trang/lọc/trùng model; danh sách model Google thật trong UI, bấm chọn qua menu thật, không lưu trước validation, từ chối tên model sai, nút Lưu & kiểm tra gọi Google thật thành công; key/model cũ được giữ sau kiểm tra.

Đo Google thật bằng production translator: 908 / 846 / 1100 ms cho ba câu mẫu. Câu kỹ thuật giữ M20, 12mm và phủ định chưa phê duyệt.

Đo production coordinator với nguồn caption giả lập (Google API thật): ba câu tổng 1770 / 1931 / 1909 ms, thời gian hàng đợi 2 / 10 / 2 ms. Cả ba nguồn Nhật khớp nguyên văn. Đây là số đo mẫu tại thời điểm kiểm tra, không phải cam kết cho cuộc họp hoặc mọi model. Chưa thử trong cuộc họp Teams thật trong lần nâng cấp này.

Test UI dùng desktop riêng; harness async thiết lập WindowsFormsSynchronizationContext tương đương message loop thật. Ảnh sáng/tối và log nằm tại `.codex-artifacts/google-provider/`. Không điều khiển hoặc tắt ứng dụng người dùng. Các test process được tạo riêng và dọn khi cần.

Backup hash-verified trước sửa: `backup/google-provider-20261009-090434/manifest.csv`.

## Tài liệu Google

- [API keys và x-goog-api-key](https://ai.google.dev/gemini-api/docs/generate-content/api-key)
- [Danh sách model và supportedGenerationMethods](https://ai.google.dev/api/models)
- [Thinking và cấu hình độ trễ](https://ai.google.dev/gemini-api/docs/thinking)
## Loại model không dùng được khỏi danh sách — 2026-10-09

Đã đổi Chọn model sang danh sách xác nhận bằng generateContent thực tế với đúng key, thay vì chỉ dựa vào models.list. GetVerifiedModelsAsync dịch thử câu ngắn, chỉ nhận output đầy đủ theo parser của luồng dịch. Hai request tối đa cùng lúc; progress trên UI, hủy khi đóng cửa sổ. Toàn bộ model lỗi hoặc chưa xác nhận đều không có trong menu (không hiện disabled). Nếu không có model tốt, hiển thị lỗi thay vì quay về danh sách thô/auto.

Kết quả quét thật 17 model với key người dùng, 24,14 giây:

- 6 dùng được: gemini-3-flash-preview, gemini-3.1-flash-lite-preview, gemini-3.5-flash, gemini-3.5-flash-lite, gemini-3.6-flash, gemini-flash-lite-latest.
- 7 lỗi cố định/kết quả không hợp lệ: gemini-2.5-flash, gemini-2.5-flash-lite, gemini-2.5-pro, gemini-3.1-pro-preview, gemini-omni-1.1-flash, gemini-omni-flash-preview, gemini-pro-latest.
- 4 chưa xác nhận do lỗi tạm thời/timeout: gemini-3.1-flash-lite, gemini-3.7-flash, gemini-3.8-flash, gemini-flash-latest. Cũng bị loại khỏi danh sách chọn.

Đây là trạng thái kiểm tại thời điểm chạy với key này, không phải kết luận model hỏng cho mọi tài khoản. Cache riêng theo hash key, không chứa key, hết hạn 30 phút. Nút Kiểm tra lại bỏ qua cache; model chỉ được đưa trở lại nếu dịch thử thành công. Auto sử dụng kết quả đã kiểm trước khi chọn. Key và model đang lưu không bị đổi chỉ vì quét danh sách.

Build cuối `.codex-artifacts/portable-update/20261009-093824-bc3c43e4/build`. 292 kiểm tra trên bản cuối đạt: FilterTests 14 (lọc thật, lỗi tạm thời, quota, giới hạn song song, hủy, all-fail); GoogleTests 71; MenuTests 165; ModelTests 42 (UI chỉ chứa đúng model đã xác nhận, không còn rejected/deferred, mouse chọn/menu lifecycle và kiểm/lưu Google thật). Quét Google thật và cache/credential preservation: FilterTests --live 17 kiểm tra đạt trên build ngay trước đó; bản cuối chỉ đổi TTL cache thành 30 phút và bỏ Label không dùng. Preview sáng/tối, log và danh sách đã quét: `.codex-artifacts/google-model-filter/`.

Backup trước sửa: `backup/google-model-filter-20261009-092653/manifest.csv`. Chỉ sửa hai C# hiện có: MeetingTranslationService.cs và ShortcutSettingsForm.cs. Không thay thuật toán dịch câu, nguồn Teams, NVIDIA hoặc bản vẽ Tekla.