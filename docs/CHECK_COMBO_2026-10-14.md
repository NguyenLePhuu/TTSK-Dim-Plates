# Combo kiểm bản vẽ — 14/10/2026

## Cách dùng

- Chuột phải trên **Check Dim** hoặc `Shift+C` chạy combo. Chuột trái trên Check Dim, Enter/Space và `C` vẫn chạy Check DIM riêng như trước. Menu Check Scale giữ hai mục Check Dim / Revision, không thêm nút Combo.
- Active: kiểm bản đang mở. Batch: lấy 1/nhiều dòng đang chọn trong TTSK, rồi Document Manager nếu không chọn dòng; dùng cùng quy tắc target hiện có của Check DIM. Một target được mở visible/chuyển Active theo luồng cũ.
- Assembly: **Scale → DIM → Góc quay**. Single-part: **Scale → DIM**, hoàn toàn không có mục góc.
- Mỗi bước ghi Đúng / Sai / Chưa xác minh. Kết luận chung chỉ Đúng khi mọi bước áp dụng đã đạt. DIM chỉ xác nhận phạm vi chân DIM thẳng của thuật toán hiện có; DIM góc được ghi ngoài phạm vi ở chi tiết.
- Màn đầu mỗi bản vẽ chỉ **một thẻ thấp**: tên bản và ba nút **Scale / DIM / Góc quay** (Single-part chỉ hai nút). Sai đỏ, đúng xanh, chưa xác minh màu hổ phách; mỗi nút có trạng thái và tóm tắt/số lỗi ngay bên dưới, không phải hover để biết số lỗi. Tooltip giữ nội dung đầy đủ. Một bản vừa màn hình không hiện thanh cuộn thừa.
- **Xem lỗi đầu tiên** ưu tiên mục sai đã xác định rồi đến mục chưa xác minh trong danh sách đang lọc/tìm. Nhiều bản có ba bộ lọc trực tiếp **Tất cả / Cần xem / Đạt** kèm số lượng; Cần xem gồm lỗi, chưa xác minh và lỗi đọc. Không có mục phù hợp thì nút mở lỗi bị vô hiệu hóa. Không tự chuyển bản vẽ hoặc tự chạy kiểm tra khi đổi bộ lọc.
- Trong chi tiết có các tab màu **Scale / DIM / Góc quay** và viền đánh dấu mục đang xem; bấm tab hoặc **Alt+1/2/3** chuyển ngay trong cùng cửa sổ, dùng kết quả của lần đọc hiện tại. Chuyển tab không mở lại bản vẽ hoặc chạy lại combo. **F5/C** kiểm lại bản hiện tại từ cả ba trang qua worker; trạng thái tab cập nhật theo kết quả mới. Scale/Góc có nút Kiểm tra lại ở footer; DIM giữ nút của báo cáo chính.
- Bấm nút để mở đúng target, đọc lại combo và chuyển sang nội dung chức năng **ngay trong cửa sổ hiện tại**. **← Kết quả**, **Esc** hoặc **Alt+←** quay về, giữ bộ lọc/tìm kiếm, danh sách và kích thước màn tổng hợp. Scale ghi view và tỉ lệ đang có so với Title 3; góc ghi view và nguyên nhân sai. DIM dùng lại báo cáo Check DIM với handles mới, chọn từng DIM hoặc vị trí trước/sau; C/F5 đọc lại qua worker STA. Thiếu dữ liệu DIM mở phần thông báo, không dùng snapshot cũ để chọn.
- Báo cáo DIM được gắn làm control con với `TopLevel=false`; không ShowDialog, không có titlebar thứ hai. Chỉ giữ **← Kết quả ở góc trên**, bỏ nút quay về ở footer cả sau F5. Nút điều hướng không làm CancelButton của hộp thoại; Esc/Alt+← xử lý bằng ProcessCmdKey. Lưu ý dữ liệu cũng nằm trong trang con; lỗi chọn/kiểm lại hiện trong báo cáo. Chặn quay về và đóng cửa sổ khi worker đang đọc/chọn, giữ callback quay về sau F5. Quay về tự cập nhật trạng thái nút từ kết quả vừa kiểm.
- Trong combo, lỗi DIM có nhãn đỏ **LỖI DIM**; chọn thành công hiện **Đã chọn DIM** và thứ tự vị trí. Khi đọc lại thất bại, báo **Kết quả cũ**, khóa chọn/vị trí trước-sau và xóa cache handles của trang combo; không dùng lại dữ liệu cũ sau khi đổi tab. Kiểm lại thành công mới mở lại chọn DIM. Kiểm ngữ cảnh model/bản vẽ trước selection vẫn giữ nguyên.
- Màn tổng hợp chỉ có trạng thái, số lỗi và hành động; không trải đoạn giải thích dài. Báo cáo DIM trong combo hiện câu nguyên nhân đầu tiên, tooltip giữ giải thích đầy đủ; không có Revision. Màn một bản thu gọn và chỉ giữ một nút kiểm lại. Batch vẫn có lọc/tìm, kiểm lại từng bản/mục cần xử lý và dừng/tiếp tục sau bản hiện tại. Đóng báo cáo không đóng bản người dùng đang giữ; nếu đang mở bản khác, người dùng lưu/đóng bản đó trước khi chuyển.
- `C`, báo cáo Check DIM và Slot 09 vẫn hoạt động như trước. UI hiện có được chọn làm cơ sở theo xác nhận của người dùng; `de9e693` không tồn tại trong Git local.

## Ranh giới chỉ đọc

- Một adapter/session giữ model và định danh bản vẽ. Ba bước dùng chung lần Open/CloseOwned; không tự đóng bản khác đang mở.
- Scale đọc Title3 mới từ active Drawing và `View.Attributes.Scale`; không select view, không dùng UI properties. Phạm vi view giữ Front/Bottom/Top/Back/Section như Check Scale cũ. Không đọc được view/tỉ lệ hoặc không có view thì không báo Đúng. So mẫu số tỉ lệ với dung sai 0,0001, không làm tròn dữ liệu để báo đạt.
- DIM gọi nguyên `PHU_DimensionCheck.Analyze`. Kết quả batch giữ dữ liệu lỗi gọn, bỏ graph hình học và handles của lượt đọc.
- Góc dùng `Reader.CaptureGeometry` + `Reader.CheckCurrentViews`: chọn grid/phương cấu kiện tự động, kiểm mọi view bằng cùng lõi `CheckViewAxesCore` của Slot 09. Không gọi `DrawingSettingsReader`, tìm nghiệm góc hay apply. View thiếu dữ liệu không bị bỏ qua; grid/phương không duy nhất báo Chưa xác minh. Không kiểm vị trí đường cắt hoặc đề xuất góc quay.
- Mỗi bước xác minh model/active trước và sau. Lỗi dữ liệu một bước không cản các bước còn lại; lỗi ngữ cảnh hủy kết quả target đó. Kết quả cũ không được dùng thay cho DIM vừa đọc lỗi.
- Chỉ `CloseActiveDrawing(false)` cho bản hidden do session mở. Không Save / Modify / Commit / đổi work plane. Xuất báo cáo combo từ UI chặn đường dẫn nằm trong thư mục model.
- Combo dùng `TeklaOperationDispatcher` STA, UI chỉ cập nhật kết quả. Chặn đóng form trong lúc đọc; dừng nhận ngay trên UI và có hiệu lực sau bản hiện tại.

## Kiểm chứng

### Gắn chuột phải trên Check Dim — 15/10/2026

- Source sản phẩm chỉ sửa `MainForm.cs`: dùng `CheckDimEntryButton` riêng cho nút Check Dim, chặn sự kiện Click có `MouseButtons.Right`, rồi gọi `RunComboCheckShortcut` từ MouseClick phải. WinForms phát Click trước MouseClick; nếu chỉ thêm handler chuột phải sẽ có nguy cơ gọi cả Check DIM cũ. Không có trạng thái nhấn chuột mới cần reset.
- Giữ nguyên handler Click → RunDimensionCheck, Enter/Space/PerformClick, shortcut C/Shift+C, Revision và SafeRoundedButton dùng chung. Đối chiếu toàn bộ phương thức MainForm cũ ngoài InitializeCheckMenu giữ nguyên từng ký tự; SHA-256 các file C# sản phẩm khác khớp snapshot đầu lượt sửa. Thuật toán Scale/DIM/góc, loader chọn target và guard chạy lệnh không đổi.
- `pwsh -NoProfile -File tests/CheckDimEntry/Run.ps1`: **130 assertions**, phát thông điệp chuột/bàn phím native lên control .NET Framework 4.8 x64 trên desktop thử riêng, kiểm hit-test trước từng lượt. Dùng nguyên lớp nút, InitializeCheckMenu, bộ điều phối shortcut và guard ở source; giả lập loader/dispatcher/API Tekla với 1/3 target. Kiểm trái/phải riêng, Enter/Space/PerformClick, bấm phải rồi trái/phím, thả ngoài nút, release không press, hủy/capture chuyển rồi release ở control khác, bấm đúp sau khi menu ẩn, vô hiệu hóa menu, điều hướng Revision/Esc, pending/busy/report/update, target rỗng, load lỗi và đổi trạng thái sau await. Kiểm thêm nút thật trong EXE portable bằng reflection. Không gọi API Tekla, không ghi model, không chuyển input desktop của người dùng.
- Release x64 BuildOnly thành công: `20261015-005201-fb895590`; 40 runtime khớp output build, cập nhật và kiểm đủ 41 dòng manifest SHA-256. Code-map cập nhật/Check. Chưa live-test trên bản vẽ Tekla thật ở lượt này; không commit/push/phát hành.

### Lượt thuận tiện phát hiện lỗi và chuyển chức năng

- Backup đã đối chiếu SHA-256: `backup/005-combo-usability-20261014`. Source sản phẩm chỉ sửa `PHU_DimensionReportForm.cs`; không đổi thuật toán Scale/DIM/Góc, Session/TeklaAdapter, MainForm hoặc phím gọi combo.
- **495 assertions** trong `.codex-artifacts/combo-usability/Verify.cs`: mở từ nút đầu tiên qua handler thật, ưu tiên lỗi so với chưa xác minh, lọc/tìm/không có kết quả, Assembly/Single-part, đổi tab không đọc/mở thêm, cache DIM cập nhật sau F5 và xóa khi lỗi, khóa ngay selection cũ, context guard trước selection, phản hồi chọn DIM, recheck Scale/Góc, busy/double-recheck/đóng cửa sổ, Esc/Alt+←, giữ handle/kích thước/bộ lọc khi quay về; legacy batch và standalone DIM giữ luồng cũ.
- Các nút được gọi bằng PerformClick và kiểm hit-test tại vị trí vẽ. ShowDialog chạy trên desktop thử nghiệm riêng, không chuyển input desktop; chỉ adapter giả, không thao tác cửa sổ người dùng hoặc bản vẽ Tekla thật. Render sáng/tối một/nhiều bản và ba trang chi tiết, hit-test ở chiều rộng tối thiểu 1.000 px với tên bản dài; nội dung ngắn không có scrollbar ngang thừa, nội dung dài xuống dòng/cuộn dọc; kiểm geometry co giãn 125/150%; chưa xác nhận DPI native trên các màn hình thật.
- Release x64 BuildOnly thành công: `20261014-142953-6e808dc1`; đối chiếu 40 runtime với output build và cập nhật/kiểm 41 dòng SHA-256. Code-map cập nhật/Check. Không commit/push/phát hành; chưa live-test chọn DIM trên Tekla ở lượt này. Probe/ảnh có thể dọn, không phải phụ thuộc sản phẩm.

### Sửa nút quay về duy nhất ở góc trên

- Backup SHA-256: `backup/004-combo-back-fix-20261014`. Chỉ sửa điều hướng/footer trong `PHU_DimensionReportForm.cs`, không đổi thuật toán kiểm hoặc API Tekla.
- Tái hiện lỗi trước sửa bằng `Button.PerformClick`: nút trên có `DialogResult.Cancel`; hàm quay về chạy nhưng cũng gán Cancel cho form cha, làm kết thúc ShowDialog. Probe trước đây gọi thẳng ReturnToComboResults nên chưa bắt được hành vi này.
- Gỡ CancelButton khi đang xem chi tiết; Click/Esc/Alt+← chỉ đổi trang. Bỏ nút footer của cả Scale/DIM/Góc; F5 không tạo lại nút. Áp dụng cùng quy tắc cho nút trở lại DIM trong trang lưu ý. Màn tổng hợp khôi phục nút Đóng ban đầu.
- **354 assertions offscreen + 210 assertions modal**: bấm nút trên thật qua PerformClick, hit-test tại giữa nút, ba trang ở theme sáng/tối, Esc từ control con/Alt+← từ cha, F5 rồi quay về, qua trang lưu ý, bấm khi đang bận, giữ native handle/danh sách/bộ lọc/kích thước. Hộp thoại vẫn mở ở lượt message-loop tiếp theo; nút ba chức năng dùng được sau khi trở về.
- Probe modal chạy ShowDialog trên desktop riêng không chuyển input desktop; chỉ dùng adapter giả, không chiếm màn hình/focus hoặc thao tác app người dùng. Preview offscreen sáng/tối vẫn kiểm 100/125/150% với dữ liệu giả. Đây chưa phải xác nhận chọn DIM trên bản vẽ Tekla thật.
- Release x64 BuildOnly thành công (`20261014-112741-bdd8b84d`); 40 file runtime khớp output build, 41 dòng manifest SHA-256 được cập nhật/kiểm. Code-map cập nhật/Check; không commit/push/phát hành.

### Lượt ba nút trạng thái và điều hướng một cửa sổ

- Backup SHA-256: `backup/003-combo-inline-20261014`. Chỉ thay UI/điều hướng trong `PHU_DimensionReportForm.cs`; giữ thuật toán và vòng đời target của Session/TeklaAdapter.
- **336 assertions offline**: giữ hồi quy context/handles/selection; thêm số lượng nút, chiều cao thẻ, nội dung nhúng không có cửa sổ top-level mới, cùng native handle trước/sau, quay về giữ lọc/danh sách/kích thước, F5 giữ điều hướng và không nhân titlebar, lưu ý dữ liệu inline, chặn back/close khi worker bận, gọi thật handler OpenBatchEntry với adapter giả.
- Render offscreen sáng/tối cho thẻ một/nhiều bản, Scale/DIM/Góc trong cửa sổ; co giãn thẻ 100/125/150%. Không Show cửa sổ top-level, không điều khiển cửa sổ người dùng hoặc API bản vẽ thật. `Show()` chỉ áp dụng control con có `TopLevel=false` bên trong form probe ẩn.
- Release x64 BuildOnly thành công; kiểm 40 file runtime với output build và 41 dòng SHA-256; cập nhật code-map/Check. Không commit/push/phát hành. Chưa kiểm chọn DIM hoặc DPI native trực tiếp trong Tekla ở lượt này.

### Lượt thu gọn màn đầu và bung chi tiết

- Backup SHA-256: `backup/002-combo-compact-20261014`. Chỉ thay UI combo trong `PHU_DimensionReportForm.cs`; giữ luồng mở/đọc/chọn DIM của lượt trước.
- Dòng tổng hợp có chấm trạng thái và tóm tắt một dòng; hover/focus và mũi tên thể hiện khả năng bấm. Phần bung dùng các dòng Scale/DIM/Góc, số thứ tự, mô tả và nhãn trạng thái nhẹ; không còn ba khối nút lớn. Ẩn số thống kê bằng 0; cửa sổ một bản tự tăng/giảm chiều cao khi bung/thu.
- Probe offline cập nhật: **282 assertions**, giữ hồi quy điều hướng/selection/F5; thêm mặc định thu gọn, bấm không gọi Tekla, chỉ bung một bản, chặn khi đang chạy và giữ trạng thái bung sau lọc/tìm. Render sáng/tối cho một/nhiều bản, co giãn màn đầu 100/125/150%; không điều khiển cửa sổ người dùng hoặc gọi API bản vẽ thật.
- Release x64 BuildOnly thành công; đối chiếu 40 file runtime với build và cập nhật/kiểm 41 dòng `portable/SHA256.csv`. Code-map Check phải khớp source trước bàn giao. Không commit/push/phát hành; chưa kiểm tương tác trên bản vẽ thật hoặc DPI native ở lượt này.

### Lượt nâng cấp điều hướng theo từng mục

- Backup trước sửa và kiểm SHA-256: `backup/001-combo-navigation-20261014`.
- Chỉ sửa UI/điều hướng trong `MainForm.cs` và `PHU_DimensionReportForm.cs`; giữ nguyên thuật toán Scale/DIM/góc, target và vòng đời bản vẽ của Session/TeklaAdapter.
- Release x64 bằng Update-Portable BuildOnly; 40 file runtime khớp SHA-256 với build. Đã sao lưu và cập nhật dòng EXE trong `portable/SHA256.csv` (BuildOnly không tự cập nhật CSV), kiểm đủ 41 dòng gồm metadata `release.json` đang có. Không đổi version/metadata phát hành, không commit/push/phát hành.
- Probe offline `.codex-artifacts/combo-navigation/Verify.cs`: 259 assertions cho nhánh Assembly/Single-part, handles mới so với snapshot gọn, chi tiết đúng mục, dữ liệu DIM thiếu, chặn model/active đổi trước selection, selection/F5 bất đồng bộ không lặp, chặn đóng khi worker bận, giữ bản khác đang mở và Check DIM cũ.
- Render offscreen sáng/tối, layout co giãn 100/125/150%; đối chiếu ảnh tổng hợp và chi tiết, thẻ một bản không cần cuộn. Probe không Show/ShowDialog, không thao tác cửa sổ người dùng, không gọi API vào bản vẽ thật. Đây là mô phỏng layout, chưa xác nhận DPI native hoặc thao tác chọn DIM trực tiếp trong Tekla ở lượt này. Probe/ảnh trong `.codex-artifacts` có thể dọn, không phải phụ thuộc runtime.

### Bằng chứng của lượt triển khai combo trước

- Release x64, C# 7.3 / .NET Framework 4.8 / Tekla 2025; cập nhật bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`, workflow đối chiếu SHA-256 cho 40 file runtime.
- `tests/ComboCheck/Run.ps1`: tỉ lệ sai/thiếu/NaN/không view, tỷ số tương đương/thập phân, tổng hợp trạng thái, Single-part không có góc, giữ target Active/1/nhiều dòng, session mở/đóng/quyền sở hữu/dừng/tiếp tục/lỗi ngữ cảnh, bỏ kết quả cũ, phím C/Shift+C, đối chiếu trạng thái từng view với Slot 09.
- `tests/ComboCheck/Run.ps1 -Mode ui`: render sáng/tối cho batch, Single-part, chi tiết và form batch cũ; kiểm UI Single-part không có Góc quay; chạy dispatcher thật với adapter giả để kiểm UI phản hồi, chặn đóng khi đọc và dừng/tiếp tục.
- `tests/KeyplanViews/Run.ps1 -Mode regression`: 21.871 assertions, 3.840 ca ma trận, 1.344 hướng trục có dấu; thêm đối chiếu kết quả combo với Slot 09 trên snapshot lịch sử.
- `tests/ComboCheck/Run.ps1 -Mode live`: đọc Assembly active `[C2-PC2-18]` trên worker STA: Scale **1:20 / 4 view đúng**; DIM **39 chuỗi thẳng đúng**; góc **4 view đúng**. Active giữ nguyên. Probe không mở/đóng/lưu bản vẽ, không đổi selection/work plane và không tạo dữ liệu trong model.
- Chưa chạy batch trên các bản thật hoặc đọc một Single-part thật trong lượt này; hai nhánh này đã kiểm offline bằng session giả và UI. Người dùng đã xác nhận Slot 09 đúng qua kiểm thủ công trước khi phát triển combo.
- Không commit/push/phát hành.
