# Từ điển người dùng — 15-10-2026

## Cách dùng

- Mở **Dict → + Thêm từ**, nhập tiếng Việt và tiếng Nhật, bấm **Lưu từ** hoặc Ctrl+S. Từ xuất hiện ngay, được chọn trong list và giữ lại khi mở lại app. Không cần sửa source/build để thêm từ.
- Chọn dòng → **Sửa** hoặc nhấp đúp/F2. Có thể sửa cả từ có sẵn; thay đổi được lưu riêng cho tài khoản Windows hiện tại.
- **Xóa** hiện xác nhận ngay trong panel; **Giữ lại** hủy thao tác. Không thay đổi file từ điển đi kèm ứng dụng.
- Tìm cả Việt/Nhật, tiếng Việt có thể gõ không dấu. Không bỏ dấu dakuten/handakuten của chữ Nhật. Bấm icon copy ở cuối dòng, Enter hoặc Ctrl+C trong list để copy tiếng Nhật Unicode. Giữ quy tắc ký hiệu đường kính `⌀` → `φ` đã có trước đây.
- Ctrl+N thêm, Ctrl+F tìm, Ctrl+S lưu. Trong editor, Enter dành cho nhập/IME, không tự lưu; Tab di chuyển ô. MainForm không chạy shortcut bản vẽ khi focus nằm trong từ điển.
- Ô rỗng, cặp từ trùng hoặc lỗi lưu hiển thị ngay trong panel. Nội dung nhập không bị xóa khi lưu thất bại, đổi theme hay ẩn/mở lại panel; **Hủy** chủ động bỏ bản nháp và tải lại dữ liệu mới.
- Giới hạn 500 ký tự mỗi vế. Khoảng trắng, tab, xuống dòng được chuẩn hóa về một dấu cách khi lưu; chặn trùng cặp Việt–Nhật sau chuẩn hóa, vẫn cho phép cùng nghĩa Việt với bản dịch Nhật khác.
- Nội dung tiếng Việt, ô tìm/placeholder và ô nhập Việt dùng **Segoe UI 10pt Regular**; tiếng Nhật trong list/ô nhập dùng **MS UI Gothic 10pt Regular** theo yêu cầu mới nhất. Tiêu đề/nút dùng Segoe UI; icon copy vector, thanh cuộn theo theme. Ô tìm có placeholder **Tìm từ Việt hoặc Nhật** khi trống và chưa focus; bấm **×** hoặc Esc khi đang tìm để xóa nhanh bộ lọc. Từ điển không có tooltip; nhấp đúp/Sửa để xem đầy đủ nội dung dài trong editor.

## Dữ liệu

- `Data/JapaneseDictionary.tsv`: bộ có sẵn, chỉ đọc. Không thay đổi nội dung hoặc cấu hình đóng gói.
- `%LocalAppData%/TTSK/Dictionary/entries.xml`: phần người dùng thêm/sửa/xóa; dùng chung giữa các cửa sổ TTSK của cùng tài khoản Windows. Nằm ngoài EXE/portable/model, không bị bản cập nhật phần mềm ghi đè.
- Từ gốc được nhận diện bằng hash cặp Việt–Nhật chuẩn hóa; từ mới có GUID. Sửa từ gốc là một bản ghi ghi đè riêng; xóa từ gốc lưu dấu ẩn. Bộ đi kèm bổ sung từ mới vẫn được hợp vào danh sách khi tải lại.
- Ghi file tạm cùng thư mục, flush rồi thay file nguyên tử. `entries.xml.bak` giữ phiên bản ngay trước lần ghi gần nhất; file tạm tự dọn sau thao tác. `entries.xml.lock` khóa ghi giữa các tiến trình; đọc lại dữ liệu trong khóa trước mỗi thao tác, không ghi nguyên snapshot cũ.
- Sửa/xóa phải khớp nội dung đã chọn với dữ liệu mới nhất; nếu cửa sổ khác vừa sửa/xóa, yêu cầu tải lại, giữ bản nháp. Thêm mới không làm mất từ cửa sổ khác vừa thêm.
- XML sai định dạng/phiên bản không hỗ trợ không được ghi đè. UI giữ list đọc thành công gần nhất, hoặc hiện bộ từ có sẵn khi chưa có snapshot. DTD bị chặn, giới hạn tài liệu 8 Mi ký tự và 5.000 bản ghi riêng.

## Phạm vi và kiểm tra

- Source sản phẩm chỉ sửa `JapaneseDictionary.cs` và phần tích hợp từ điển trong `MainForm.cs` (guard focus, chiều cao panel, truyền palette). Giữ tên lớp, điểm gọi mở panel/theme/status; không gọi API Tekla hoặc chỉnh engine DIM/PDF/dịch họp. Không thêm phụ thuộc runtime vào tests/cache.
- Backup hash verified: `backup/005-user-dictionary-20261015` (hai source, PROJECT_MAP, SHA256.csv trước sửa).
- Backup sửa lỗi vẽ: `backup/006-dictionary-render-fix-20261015` (source từ điển, công cụ kiểm, tài liệu/map, SHA256.csv trước sửa). Phần đọc/ghi dữ liệu từ điển giữ nguyên so với backup này.
- `pwsh -NoProfile -File tests/Dictionary/Run.ps1`: 464 assertions với source thật, .NET Framework 4.8 x64/C#7.3. Kiểm lưu/mở lại, sửa/xóa gốc và từ riêng, trùng/rỗng/Unicode, nhiều store, xung đột sửa/xóa, file khóa/hỏng, nguyên tử/backup, dữ liệu gốc không đổi, cập nhật bộ từ, Ctrl+S/Enter, tìm/scroll và vòng đời UI. Kiểm trạng thái graphics, giới hạn clip và đối chiếu từng pixel khi vẽ toàn ô/vẽ từng phần cho 12 tổ hợp theme/chọn dòng/cột. 24 preview light/dark ở bố cục/font giả lập 100/125/150% (list/editor/error/xác nhận xóa); không phải kiểm DPI trực tiếp trên cửa sổ người dùng.
- `pwsh -NoProfile -File tests/Dictionary/Run-Native.ps1`: toàn bộ MainForm/BuildUi/theme thật; bản probe chỉ bỏ startup updater/journal/workspace. 231 assertions trong 12 lượt focus/cuộn/hover/đổi theme/thêm/lưu/xóa/mở lại. MainForm được mở trên desktop kiểm thử riêng, không chuyển desktop của người dùng; panel vẫn nằm trong MainForm khi thử và chụp hình. File thử nằm dưới `.codex-artifacts/dictionary`, không ghi từ điển thật, không điều khiển cửa sổ người dùng hoặc gọi Tekla.
- Build Release x64/portable bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`; kiểm SHA-256 runtime/manifest và CodeMap. Kiểm offline không chứng minh thao tác IME/clipboard trực tiếp: không sửa clipboard người dùng hoặc nhập thử vào Tekla.

## Tăng diện tích list và khôi phục font gốc

- Bỏ hẳn hàng status 44px trong panel; list dùng toàn bộ chiều cao còn lại. Status thành công/cảnh báo/lỗi đi qua sự kiện hiện có tới thanh trạng thái MainForm. Lỗi nhập liệu vẫn ở editor.
- Chiều cao hàng từ 54px xuống 46px tại 100%; panel chuẩn hiển thị đầy đủ 7 từ gốc thay vì 5. Không thu nhỏ font tiếng Việt. Số từ vẫn hiện ở tiêu đề; tìm kiếm và thanh cuộn tiếp tục dùng khi danh sách dài hơn.
- Xác nhận xóa chỉ chiếm chỗ trong lúc xác nhận, có tên từ cụ thể và hai nút Xóa/Giữ lại. Hủy hoặc xóa xong trả lại toàn bộ diện tích cho list.
- Khôi phục MS UI Gothic 9pt theo source trước nâng cấp (backup 005), thay Yu Gothic UI 10.5pt; chữ Nhật trong list/editor dùng cùng font. Chữ Việt/nút dùng Segoe UI như MainForm.
- Backup hash verified: `backup/007-dictionary-space-font-20261015-151030`. Kiểm 7 hàng hiện trọn, phần list chạm đáy, font sau đổi theme, lỗi vẫn đến status chung, xác nhận xóa và chiều cao được trả lại. Phần storage không đổi.
## Nền thống nhất và tận dụng khoảng trống dưới list

- Chiều cao panel từ điển lấy theo vị trí nút Shortcut Settings, cách nút 8px thay cho khoảng trống 44px. Phần đầu gọn thêm 4px; MainForm chuẩn hiển thị trọn 8 hàng khi đủ dữ liệu, không đổi chiều cao hàng/font đã chốt. Mở rộng vùng list thêm 40px so với lần sửa 007.
- MainForm truyền trực tiếp màu nền khung công cụ và màu nút chính vào `JapaneseDictionaryPanel.ApplyTheme`. Tiêu đề, thanh thao tác, search, header Việt/Nhật, thân list, thanh cuộn và editor cùng nền; bỏ các mảng trắng/xám xanh tách rời. Đường chia hàng nhẹ hơn, selection vẫn rõ; nút chính và icon theo accent của MainForm.
- Backup hash verified: `backup/008-dictionary-unified-surface-20261015-155931`. Storage và font không đổi. Preview full MainForm light/dark với 8 từ (gồm một từ thử, chỉ ở kho kiểm thử): `.codex-artifacts/dictionary/native/main-light-eight-words.png`, `main-dark-eight-words.png`. Không thêm từ thử vào từ điển người dùng.
## Sửa viền ô bị chồng khi cuộn/chọn dòng

- Painter icon Copy cũ đổi `Graphics.SmoothingMode` dùng chung sang AntiAlias nhưng không khôi phục, đồng thời không giới hạn nét vẽ theo vùng invalidation. Test quan sát sự kiện CellPainting thật đã bắt được trạng thái AntiAlias rò sang các ô kế tiếp trước khi sửa. Cột nút native cũng có trạng thái hover/pressed riêng không phù hợp với icon tự vẽ.
- `PaintDictionaryCell` vẽ đồng nhất nền, chữ, đường kẻ ngang và icon; luôn Save/Restore graphics, cắt clip theo ô/vùng invalidation/phần thân list. Cột Copy dùng text cell với icon vector và hover riêng; bỏ viền ô native, invalidate list sau scroll. Vẽ lại một phần phải cho kết quả pixel giống vẽ toàn ô.
- Ảnh kiểm sau tương tác trên MainForm thật: `.codex-artifacts/dictionary/native/main-light-scrolled.png`, `main-dark-scrolled.png`. Không còn viền chữ nhật chồng quanh các ô Copy trong các lượt kiểm này. Đây là kiểm UI cô lập, không phải kiểm trực tiếp phiên Tekla của người dùng.

## Thiết kế lại phần đầu từ điển

- Tiêu đề hai dòng **Từ điển / Việt → Nhật**, nút **+ Thêm từ** nằm cạnh tiêu đề. Ô tìm đặt ngay dưới; dòng công cụ có badge số từ bên trái và Sửa/Xóa kèm icon vector bên phải. Khi lọc, badge hiện số kết quả trên tổng số từ.
- Tổng ba hàng đầu vẫn **108px ở 100%** (40/40/28), giữ nguyên diện tích list và khả năng hiện 8 hàng trong panel MainForm chuẩn. Chiều cao hàng và font tiếng Nhật giữ nguyên.
- Ô tìm/ô nhập có nền tách nhẹ và viền theo focus; nút có hover/pressed/focus, thao tác Xóa có màu cảnh báo. Dòng được chọn thêm dấu nhấn bên trái. Cả theme sáng/tối lấy accent từ MainForm.
- Chỉ sửa UI trong `JapaneseDictionary.cs`; phần `JapaneseDictionaryStore` giữ nguyên. Bản nháp editor, sự kiện status, phím tắt, dữ liệu mặc định và cơ chế ghi file không đổi. Thêm nút xóa bộ lọc và Esc trong ô tìm; khóa nút này trong lúc xác nhận xóa từ.
- Kiểm UI cô lập: 338 kiểm tra bố cục/trạng thái, ảnh light/dark và mô phỏng layout/font 100/125/150%; tìm Việt không dấu/Nhật, xóa tìm kiếm, trạng thái rỗng, thêm/lưu, giữ nháp khi đổi theme, xác nhận/hủy xóa và 8 hàng hiển thị. Công cụ/ảnh nằm dưới `.codex-artifacts/dictionary-redesign/`; kho thử chỉ ở thư mục này. Ảnh từ panel thật, không phải mockup; mô phỏng scale không thay cho kiểm DPI trực tiếp trên máy người dùng.
- MainForm thật với các dịch vụ nền được bỏ riêng trong probe: 37 kiểm tra qua 2 lượt sáng/tối, focus/cuộn/hover/thêm/lưu/xóa/mở lại. Chạy trên desktop riêng, chụp cửa sổ bằng `PrintWindow` để giữ đúng thứ tự TextBox/placeholder; không chuyển desktop người dùng, không gọi Tekla hoặc ghi clipboard. Phần source storage được đối chiếu nguyên văn với bản trước sửa và không thay đổi.
- Build Release x64 bằng `Update-Portable.ps1 -BuildOnly -NoPause`; 40 file runtime khớp SHA-256 với output build. Đồng bộ checksum EXE trong `portable/SHA256.csv` (BuildOnly chỉ đối chiếu output/copy); kiểm lại đủ 41 dòng manifest gồm `release.json`. Cập nhật CodeMap và chạy `-Check`.

## Bỏ tooltip, hoàn thiện tiêu đề và khôi phục font ô gốc

- Xóa component `ToolTip`, mọi lệnh `SetToolTip`/`ToolTipText` và phần dispose tương ứng khỏi từ điển. Tắt `ShowCellToolTips` để DataGridView không tự hiện tooltip khi nội dung bị cắt; bỏ `AutoEllipsis` của nhãn xác nhận xóa vì Label cũng có thể tự sinh tooltip. Placeholder trong ô tìm và thông báo lỗi inline vẫn phục vụ nhập liệu.
- Cụm đầu dùng biểu tượng hai thẻ chữ A/文, nhãn **TỪ ĐIỂN**, tiêu đề **Việt · Nhật** và nút Thêm từ dạng pill có dấu cộng trong vòng tròn. Vẽ bằng GDI theo palette sáng/tối; không thêm ảnh hoặc thư viện runtime. Phần đầu vẫn 108px, list giữ chiều cao cũ.
- Đối chiếu source trước lần bổ sung chức năng ở `backup/005-user-dictionary-20261015/.../JapaneseDictionary.cs`: `_bodyFont = new Font(DictionaryFontName, 9F)`, `DictionaryFontName = "MS UI Gothic"`, áp dụng cho toàn grid. Khôi phục **cả tiếng Việt và tiếng Nhật** về đúng family/9pt/Regular/Point; đặt font trực tiếp cho hai cột và các ô nhập. Font tiêu đề/nút là UI riêng.
- Kiểm 426 điều kiện UI/bố cục gồm tooltip rỗng, không còn component ToolTip, font kế thừa thực tế của hai cột, font ô nhập, tìm/xóa bộ lọc, editor và theme; ảnh layout/font giả lập 100/125/150%. MainForm cô lập đạt 53 kiểm tra sau đổi theme/cuộn/hover/thêm/lưu/xóa/mở lại, gồm kiểm font gốc và ngăn tooltip native. Ảnh thực chụp bằng PrintWindow ở `.codex-artifacts/dictionary-refinement/native/dictionary-light.png` và `dictionary-dark.png`; không gọi bản vẽ Tekla hay ghi dữ liệu thật.
- Storage giữ nguyên so với trước lượt sửa này. Cập nhật portable bằng Release x64/BuildOnly, đối chiếu output và SHA-256; cập nhật CodeMap và chạy `-Check`.

## Chốt font 10pt và màu thanh cuộn dark mode

- Theo điều chỉnh tiếp theo của chủ dự án: tiếng Việt/ô tìm về Segoe UI 10pt; chữ Nhật vẫn MS UI Gothic và tăng lên 10pt. Áp dụng tương ứng cho ô nhập Việt/Nhật, giữ Regular và kích thước hàng hiện tại.
- Thumb dark mode pha 65% accent với nền list, thay màu xám xanh cố định. Nền rãnh cuộn lấy trực tiếp nền list; màu sáng giữ nguyên.
- 426 kiểm tra UI/layout với font mới và preview giả lập 100/125/150%; 62 kiểm tra MainForm cô lập, bao gồm font sau đổi theme, danh sách 9 từ/8 hàng hiển thị, cuộn đến cuối và màu pixel thực của thumb/rãnh. Ảnh: `.codex-artifacts/dictionary-font-scroll/native/dictionary-light.png`, `dictionary-dark.png`. Hai dòng xem trước chỉ thêm vào RAM của probe; không sửa từ điển người dùng.
- Storage, cơ chế bỏ tooltip và chức năng từ điển giữ nguyên. Build Release x64/portable, đối chiếu SHA-256 và chạy CodeMap `-Check` sau sửa.

Không commit/push/phát hành. Sau khi nhận bản nâng cấp, mở lại ứng dụng một lần; các lần thêm/sửa từ sau đó không cần build hay mở lại.
