# Nhiều key dự phòng cho dịch họp

## Sử dụng

Shortcut Settings → API dịch họp → **Quản lý / thêm key dự phòng**, hoặc **Chọn model API** ngay trong cửa sổ dịch, cùng mở một danh sách dùng chung.

1. Chọn Google Gemini hoặc NVIDIA Riva → **+ Thêm key**.
2. Nhập key. Google bấm **Kiểm tra model** rồi chọn model đã xác minh; NVIDIA xác minh key trước khi lưu.
3. **Lưu dự phòng** thêm key và giữ cửa sổ mở để thêm tiếp; không đổi API đang dịch. **Áp dụng ngay** chọn key/model cho phiên hiện tại.
4. Mở danh sách key để chọn Google chính/Google 1/Google 2… hoặc NVIDIA tương ứng. **Xóa key** chỉ xóa mục đang chọn, có xác nhận.

Key cũ tự xuất hiện trong danh sách; thêm key mới không ghi đè key cũ. Lưu lại cùng key cập nhật model của mục đó, không sinh bản sao. Danh sách đặt tên tự động, không hiện secret trong menu. Giao diện sáng xanh, tối cam; Riva vẫn là model cố định.

## Lưu trữ

`MeetingKeyRepository` lưu danh sách tại `%LocalAppData%/TTSK/MeetingTranslation/keys.dpapi`, mã hóa bằng Windows DPAPI CurrentUser. Dùng một file mã hóa cho nhiều key thay vì nhét n key vào một credential có giới hạn blob. Không có file key rõ; file tạm cũng đã mã hóa. Mutex theo đường dẫn và ghi nguyên tử tránh hai cửa sổ ghi mất dữ liệu của nhau. File hỏng/không giải mã được báo lỗi, không ghi đè file cũ.

Credential Manager cũ giữ key mặc định tương thích. Lần lưu đầu tiên đưa cả key cũ và key mới vào kho; đọc danh sách không tự ghi. Tên/ID/secret/model đều nằm trong phần mã hóa. Cache model và ngân sách tiếp tục theo hash key/model ngoài Tekla. Kho gắn tài khoản Windows; copy file sang tài khoản/máy khác không bảo đảm giải mã được. Không tạo hay ghi gì vào folder model Tekla.

Các điểm `GoogleApiKeyStore.Save`, `NvidiaApiKeyStore.Save` lưu mục vào danh sách trước khi cập nhật mặc định. Sự kiện `MeetingApiKeyRing.Changed` cập nhật pool dự phòng của cửa sổ dịch cùng tiến trình khi thêm/xóa ở Settings; form unsubscribe khi dispose. Thêm key không tự gọi chuyển API. Các tiến trình khác đọc danh sách khi mở/chọn API hoặc bắt đầu phiên.

## Điều phối

`ConfigureAutomaticKeyFallback` nhận snapshot danh sách key và model đã xác minh. Tách định danh lỗi thành hash(provider|key) + model; lỗi một model trên key A không loại cùng model trên key B.

- API đang chạy tốt: không đổi key/model.
- Mặc định đã lưu là key chính khi mở phiên mới; Lưu dự phòng không đổi mặc định. Đổi tự động trong phiên không ghi đè key chính đã lưu.
- 429/quota và Google 401/403: đánh dấu cặp key/model lỗi, thử hết các model còn lại của chính key đó rồi mới sang key dự phòng. Đây là thứ tự chủ dự án yêu cầu, kể cả lỗi có khả năng áp dụng cho toàn key.
- 404: ưu tiên model Flash Lite khác của key hiện tại, rồi các key khác.
- Timeout/mạng/5xx: vẫn cần hai lỗi liên tiếp; quality ba lỗi; thành công reset đếm. Lỗi reader không đổi API.
- NVIDIA lỗi: thử NVIDIA key dự phòng rồi Google Flash Lite. Google lỗi giữ nhánh Google, không tự chuyển ngược về NVIDIA.
- Mỗi key Google thử toàn bộ danh sách model đã xác minh của key đó, không giới hạn 6 model. Model đang dùng trước; khi lỗi, ưu tiên 3.5 Flash Lite → 3.1 Flash Lite Preview → Flash Lite Latest → model đã xác minh khác theo tên. Không dò/tự thêm model chưa xác minh. Sau khi hết key chính, thử lần lượt các key dự phòng theo thứ tự lưu; mỗi key phải hết model rồi mới đến key tiếp theo.
- Hết mọi cặp key/model: giữ nguồn và báo lỗi/retry theo quy tắc hiện có; không lặp vô hạn. Key mới cùng Google project có thể vẫn chia sẻ quota phía nhà cung cấp.

Không dò mạng/dịch thử trên đường dịch bình thường để xây pool. Manual và automatic dùng chung `SwitchWorkersAsync`, giữ reader/tracker/context/SessionId/Sequence/completed texts/history. **Đổi key hay model đều tiếp tục câu chưa hoàn tất, không dịch lại từ đầu.** Pause vẫn pause, manual được ưu tiên và close chờ worker dọn xong. Bổ sung chờ transition có cancellation để hai worker NVIDIA không tự khóa khi switch vừa được xếp lịch nhưng chưa tăng generation. Revision của pool loại tác vụ còn chờ chọn key vừa bị xóa.

## Kiểm chứng

`pwsh -NoProfile -File tests/MeetingApiSwitch/Run.ps1` chạy regression trước đó và các nhóm mới:

- Kho riêng trong `.codex-artifacts`, không ghi key giả vào kho người dùng: giữ key legacy, dedup, 24 writer song song giữ đủ 26 mục, mã hóa, xóa đúng mục, từ chối key sai, không ghi đè file hỏng.
- HTTP giả: 429/401/403 key A → B; NVIDIA A → B; giữ bản dịch cũ và tiếp tục queue; hết key không lặp; healthy không switch; cùng model dùng được trên key khác.
- UI sáng/tối: danh sách chỉ hiện tên, chọn key đồng bộ model riêng, thêm key xóa draft cũ, Settings mở cùng manager.
- Toàn bộ kiểm hồi quy đổi model, batch mapping, revision, pause/resume, close/manual race và lịch sử cũ.

Không có key dự phòng thật thứ hai được cung cấp trong yêu cầu này; chưa xác minh luồng hai key thật có quota độc lập. Kiểm HTTP giả xác nhận request dùng key mới và output đúng nguồn, không suy diễn thành kiểm thực tế hai tài khoản. Các thử Teams/API thật của phiên trước ghi riêng trong `MEETING_API_SWITCH_2026-10-11.md`.

Lượt kiểm nhiều key ban đầu: **256 kiểm tra PASS** (`.codex-artifacts/meeting-api-switch/multi-key-final.txt`). Đã xem ảnh giao diện sáng/tối của picker và thẻ Settings; dark giữ cam. Bổ sung `TestPrimaryModelsFirst` kiểm 8 model key A → 2 model key B → key C với 429/401/403/404, danh sách pool cố tình đặt B trước A; kiểm một model khác của A dịch tốt thì không dùng B. Kết quả lượt mới ghi trong `primary-first-regression.txt`. Các kiểm soát pause/manual/close và không replay lịch sử vẫn chạy trong cùng suite.

Lượt ưu tiên key chính cuối: **285 kiểm tra PASS**. Build Release x64 qua `Update-Portable.ps1 -BuildOnly -NoPause`; đối chiếu SHA-256 **40 file runtime** với output build, ghi `primary-first-runtime-sha256.csv`. EXE: `0F7F146FC1C180A63CB0A6AEA08854137C19A9FEF05E880F71D03C32EE7C8C20`. Metadata `portable/SHA256.csv`/`release.json` là bản phát hành cũ, BuildOnly không tạo phát hành mới. Không commit/push/phát hành.
