# Rà soát điều phối MainForm — 2026-10-12

Người dùng báo Check Scale hiện `Sai tỉ lệ: 1/2` nhưng bảng trống; yêu cầu đối chiếu bản đang hoạt động đúng tại `C:\Users\ADMIN\OneDrive\Desktop\TTSK_Dim_Plates` và giữ cơ chế nhận diện Active/một bản/list. Bản Desktop chỉ được đọc, không bị sửa.

## Nguyên nhân và phạm vi sửa

1. `RunScaleCheck` xóa bảng rồi chỉ thêm dòng khi `errors.Count > 1`. Một bản lỗi vẫn nằm trong `_selectedDrawings` nhưng không có dòng để đọc lỗi hoặc chọn lại. Commit `80b831e` ngày 2026-10-04 thêm điều kiện này khi đưa vào điều phối đối tượng. Khôi phục lời gọi `AddScaleErrorGridRow` vô điều kiện trong vòng lặp, đúng bản Desktop; giữ nguyên bộ đọc scale, tiêu chí sai và cách tạo nội dung RESULT.
2. `RefreshContextDrawingRows` cũng ẩn mọi trường hợp một bản, kể cả Batch một bản từ Document Manager/list. Vì writer trạng thái bỏ qua chỉ số không có dòng, trạng thái từng bản của Check Scale/PDF/Batch không hiện. Giữ giao diện gọn cho Active một bản; Batch luôn hiện dòng. Không đổi cách quyết định Active/Batch theo nguồn đối tượng.
3. `LoadContextDrawingsCore` gọi `ResetDimensionCheckBatch` mỗi lần, nên phiên Check DIM cũ bị mất trước khi `RunDimensionCheckBatch` có thể tái sử dụng nó. Chỉ reset khi model hoặc danh sách đối tượng theo thứ tự thay đổi. Phiên dừng giữa chừng cùng danh sách giữ các entry đã kiểm; form hiện có tiếp tục các entry Pending. Phiên đã hoàn tất vẫn theo quy tắc kiểm lại của form hiện hành.
4. `dgvDrawings_CellDoubleClick` chỉ chặn `operationRunning`, còn PDF dùng `_pdfCommandRunning`. Input filter cho phép thao tác bảng nên có thể mở bản khác trong lúc PDF chạy. Thêm kiểm cờ PDF tại chính sự kiện mở bản vẽ; nhấp đúp khi rảnh vẫn hoạt động. Điểm này cũng có trong source Desktop đối chiếu, không quy toàn bộ lỗi cho commit điều phối.

Source sản phẩm chỉ sửa `MainForm.cs` và `MainForm.DrawingTargets.cs`. Backup trước sửa cùng manifest và EXE: `backup/006-check-scale-error-list-20261012`, đã đối chiếu SHA-256. Không sửa resolver `DrawingTargetSelection`, worker/dispatcher, shortcut, thuật toán DIM, nhận diện scale, in PDF hoặc bản Desktop.

## Các đường đã rà

- Check Scale/Check DIM qua `RunLoadedBatchShortcutAsync`: chờ bước load hoàn tất; load thất bại không đặt `ready`, không chạy lệnh từ selection cũ.
- Resolver: Active đang mở có ưu tiên khi chọn Active; Batch ưu tiên dòng chọn theo thứ tự chỉ số, rồi Document Manager; gộp trùng theo định danh, giữ Batch kể cả một bản; chỉ dự phòng Active khi không còn lựa chọn.
- Kiểm model và chỉ số dòng trước khi dùng selection; từ chối list thuộc model khác hoặc dòng không hợp lệ.
- PDF và xuất snapshot dùng cùng bộ nạp; Create Drawing giữ đường Active/Batch và snapshot Stop/Resume hiện có, không tự tải lại danh sách khi resume.
- Check DIM Batch vẫn kiểm lại định danh/số lượng entry trước khi tái sử dụng session. Load Selected chủ động tiếp tục reset phiên như trước.
- Dòng kết quả Scale sau lọc có cùng thứ tự với `_selectedDrawings`, nên chọn dòng/nhấp đúp trỏ đúng bản lỗi.

## Kiểm chứng offline

`tests/DrawingCoordination/Run.ps1` trích các method thật bằng Roslyn, chạy với DataGridView/Label/Button WinForms thật nhưng không Show/Activate cửa sổ; thay API Tekla bằng dữ liệu RAM. Không mở, lưu, tạo DIM hoặc ghi folder model.

```powershell
pwsh -NoProfile -File tests/DrawingCoordination/Run.ps1 -SourceDirectory 'backup/006-check-scale-error-list-20261012/TTSK Dim Plates/TTSK Dim Plates' -ExpectOldFailures
pwsh -NoProfile -File tests/DrawingCoordination/Run.ps1
```

- Source trước sửa: tái hiện cả bốn hành vi lỗi, 269 assertions theo kết quả cũ.
- Source sau sửa: 331 assertions đạt. Bao gồm 28 tổ hợp scale/Active/Batch/1–3 bản/0–n lỗi, kiểm lại một lỗi, đúng MARK/RESULT/màu/STT/đối tượng; thiếu title; ưu tiên lựa chọn; gộp trùng; giữ/reset phiên theo context; từ chối model/dòng sai; chặn mở bản khi PDF/lệnh chạy và cho phép khi rảnh.
- So token: mọi phần MainForm ngoài `RunScaleCheck` và `dgvDrawings_CellDoubleClick` nguyên vẹn. Bộ kiểm không gọi API Tekla và không xác nhận hoạt động live trong Tekla.
- Cảnh báo compiler harness chỉ là field không dùng/chưa gán do thay API bằng fixture.
- Build Release x64/net48 bằng `Update-Portable.ps1 -BuildOnly -NoPause` thành công; 40 runtime file khớp staging, 41 mục SHA-256 portable khớp. EXE: `9304AAC631CCDDA46A5A7D7957A547299DF55223BF5799BB2C049E156DF4C0DD`. Code map đạt 57/57 file, 10.662 khai báo. `PHU_Shape.cs` vẫn nguyên byte so với bản hoàn tác đã chấp nhận (`79DBA394235991931B63AB7C23D28C5F7AF709623D06ECDDEE28B93E25B8BAA0`). Không commit/push/phát hành.

Các điểm còn phụ thuộc live: đọc scale từ bản vẽ thật, API mở/đóng bản vẽ, PDF và layout trên máy người dùng. Không suy từ kiểm offline rằng toàn bộ tình huống runtime đã được chứng minh.

## Rà soát tiếp: giới hạn Active / một bản được chọn / list

Người dùng xác nhận các khác biệt ở luồng khác là có chủ đích; không hoàn tác hoặc sửa các engine DIM, Fit, Arrange, Slot, Exact, DIM góc hay DimSpacing trong lượt này. Bản Desktop chỉ dùng để đối chiếu. Các số build/kiểm ở mục trên là mốc sửa bốn lỗi đầu tiên.

Phát hiện thêm tại ranh giới điều phối mới và hàm cũ `CheckDrawingScale`: trước đây chỉ phục vụ Batch nên luôn đóng bản đang mở, mở lại target và đóng sau kiểm. Khi resolver mới đưa bản Active vào, thao tác đó đóng/mở lại chính bản người dùng đang làm; helper Close cũ truyền true cho tham số bool. Mặt khác, kiểm type + mark không phân biệt được hai bản vẽ trùng mark.

Sửa riêng vòng đời/định danh: nếu active đã đúng `IsSameDatabaseObject`, đọc tại chỗ và không nhận quyền đóng. Nếu phải mở target, giữ đường Batch hiện có. Sau mở phải đúng định danh, không chỉ cùng mark; trong finally chỉ đóng khi lượt kiểm đã mở và đúng target vẫn active. Không đổi bộ đọc scale, tiêu chí scale lỗi, thứ tự ưu tiên resolver hoặc thuật toán bản vẽ.

Backup trước lượt này: `backup/007-checkout-audit-20261012`. Test `Run.ps1 -Ownership` trích method thật, thay handler bằng RAM: 15 assertions tái hiện lỗi ở bản backup và đạt sau sửa, gồm Active thành công/lỗi vẫn mở; Batch tự mở được đóng; hai ID cùng mark bị từ chối; không đóng bản khác thay thế giữa lúc đọc. Bộ kiểm list bổ sung 36 tổ hợp Active có/không, chế độ Active/Batch, 0/1/2 dòng chọn và 0/1/2 bản trong Document Manager. Không gọi Tekla hoặc mở cửa sổ trong các phép kiểm.

Kết quả cuối: 435 assertions điều phối/list + 15 assertions vòng đời/định danh = 450 đạt. Build portable Release x64 thành công, 40 runtime file khớp staging và 41 mục manifest khớp SHA-256. EXE hiện tại: `4027627FF02807E55EE5950045C53E970E04B49CAE65F7BA44F12D8A1DCEC5BF`. Code map đạt 57/57 file, 10.662 khai báo. Source sản phẩm trong lượt rà tiếp chỉ đổi `CheckDrawingScale` của `MainForm.cs`; `PHU_Shape.cs` vẫn đúng hash của bản đã hoàn tác 300 mm. Chưa kiểm live Tekla, không ghi folder model.

## Sửa kẹt Check DIM sau Check Scale → mở bản vẽ — lượt tiếp theo 2026-10-12

Các kết quả/hash phía trên là lịch sử trước lượt này. Người dùng cung cấp ảnh Batch 0/1, nút Bắt đầu và Kiểm tra bản này đều không chạy, thông báo yêu cầu lưu/đóng bản vẽ.

### Nguyên nhân gốc

- `LoadContextDrawingsCore` giữ Batch cho bản chọn từ list là đúng. Nhưng `Session.Start` và `Step` dùng `RequireClosed` vô điều kiện; `TeklaAdapter.Open(false)` cũng từ chối mọi active. Vì vậy bản vừa mở từ Check Scale bị chặn ngay cả khi đúng định danh. Đổi nhãn/nút hoặc ép sang Active chỉ che mâu thuẫn giữa nguồn target và quyền sở hữu bản vẽ.
- `owned` còn lại sau lỗi đóng/mất model. Khi mở lại đúng bản để review, nhánh return sớm có thể giữ quyền đóng cũ. Test source trước sửa tái hiện việc đóng bản đã giao cho người dùng.
- Hàng đợi được lấy mất phần tử trước khi mở; Start xóa snapshot toàn bộ; UI dựng phần tiếp tục từ `Pending`. Việc kiểm lại một tập con, dừng giữa chừng hoặc lỗi ngữ cảnh dễ mất kết quả/phạm vi chạy.
- Active F5 gọi Analyze không ràng buộc target/model; nhấp đúp list đi thẳng API trên UI, thiếu kiểm model nguồn và các trạng thái bận ngoài operation/PDF.
- Scale tự đóng bản khác đang mở, bỏ qua lỗi đóng và thường trình bày lỗi đọc thành sai tỉ lệ vì TitleScale rỗng.

### Điều phối sau sửa

1. `PHU_DimensionBatch.TeklaAdapter` dùng chung cho Check DIM, Check Scale và mở dòng. Đúng active: mượn để đọc, không nhận quyền đóng. Active khác: báo cần người dùng lưu/đóng; không tự chuyển hoặc lưu. Không có active: mở target, xác minh model và định danh. Chỉ hidden do lượt này mở mới được `CloseOwned(false)`; quyền đóng được xóa trước cleanup kể cả khi cleanup thất bại. Mở visible luôn thuộc người dùng.
2. Session giữ hàng đợi riêng với snapshot. `FindActive` ưu tiên bản đang mở thuộc lượt được yêu cầu, không đổi thứ tự/số thứ tự các thẻ. `Start` không xóa kết quả trước khi lấy được target; lỗi ngữ cảnh trước đọc giữ nguyên hàng đợi/snapshot. `Continue` tiếp tục đúng phần còn lại, bao gồm kiểm lại một tập con đã từng Passed. Guard `stepping` ngăn callback/nested event khởi động lượt khác trước khi cleanup xong.
3. Form dùng `RemainingCount` để hiện Tiếp tục và giữ phiên qua đóng/mở báo cáo. Đúng bản đang mở thì Bắt đầu, Kiểm tra bản này, Kiểm tra lại hoạt động. Khi cần chuyển bản khác, form vẫn cho đóng và tiếp tục; report giữ modal hiện có, muốn dùng công cụ DIM trên Main Form thì đóng report. Không mở đồng thời các engine sửa bản vẽ trong lúc báo cáo đang kiểm.
4. Active Analyze/F5 và Chọn DIM kiểm model + định danh bằng cùng adapter. Nhấp đúp chốt đối tượng/model trên UI rồi chạy `ExecuteTeklaCommandAsync`, dùng các guard bận chung và không tiêu thụ dòng từ model cũ.
5. Scale dùng cùng Open/ValidateActive/CloseOwned, kiểm lại context sau đọc; `CHECK FAILED` và thông điệp lỗi tách khỏi `SCALE ERROR`, giữ các bản chưa kiểm trong list. Không sửa tiêu chí scale hoặc thuật toán đọc chân DIM. Bộ chọn Active/list/Document Manager, dispatcher, PDF, snapshot, AutoDim/Section/Fit/Arrange và engine hình học không đổi.

### Kiểm chứng của lượt này

Backup đầu lượt: `.codex-artifacts/drawing-session-before`, chỉ chứa các source liên quan và PROJECT_MAP. Tất cả test/log/cache nằm ngoài model. Bộ `tests/DrawingCoordination` của lượt trước đã không còn trong workspace khi bắt đầu; không dùng số 450 cũ để khẳng định kiểm hiện tại.

```powershell
pwsh -NoProfile -File tests/DrawingSession/Run.ps1 -BeforeFix
pwsh -NoProfile -File tests/DrawingSession/Run.ps1
pwsh -NoProfile -File tests/DrawingSession/Run.ps1 -Main
pwsh -NoProfile -File tests/DrawingSession/Run.ps1 -Ui
```

- Trước sửa: 4 assertions tái hiện chặn đúng active và quyền đóng còn sót. `-BeforeFix` cần backup đầu lượt, không phải phụ thuộc runtime/test thường.
- Session + TeklaAdapter source thật với Tekla RAM: 253 assertions; 1–5 target, active ở mọi vị trí, bản khác mở, pause/resume/tập con/recheck, unsupported, mở trả false/ném lỗi sau activation, đóng thất bại, mất/đổi model, đổi active giữa đọc, source kết quả sai, reentry và review.
- MainForm: 128 assertions từ các method trích nguyên source, WinForms controls thật và Tekla RAM; 1–4 bản, active ở mọi vị trí, list một lỗi, phân biệt lỗi đọc/sai scale, giữ row → target, chuỗi Scale → mở dòng → Check DIM, model cũ, context đổi trong khi đọc và lỗi cleanup. Cảnh báo CS0414 chỉ thuộc hai field fixture không có reader.
- Form portable thật: 36 assertions light/dark, Shown auto-start, event Click của Kiểm tra bản này/Bắt đầu, trạng thái nút, pause/resume, đóng/mở lại form và tiếp tục kiểm lại tập con. Không Show/Activate cửa sổ; adapter giả không gọi Tekla.
- Tổng kiểm sau sửa: **417 assertions offline**. Không mở, lưu, tạo DIM hoặc sửa bản vẽ thật; chưa xác nhận live Tekla.

Source sản phẩm thay đổi trong lượt này: `MainForm.cs`, `MainForm.DrawingTargets.cs`, `PHU_DimensionBatch.cs`, `PHU_DimensionReportForm.cs`. Các thay đổi có sẵn trong working tree được giữ. Không commit/push/phát hành.

Build cuối Release x64/C# 7.3/net48 bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause` thành công; 40 runtime file khớp staging. Khóa EXE tạm thời ở một lần copy đã hết ở lần thử lại, không dừng tiến trình người dùng. Cập nhật đúng hash EXE trong `portable/SHA256.csv`, xác minh đủ **41/41** mục. SHA-256 EXE: `B77E01ED9DBEFAB49ED0C09E4FD55C46BD50071CF4AD479E73E98128672EF697`. Bộ UI 36 assertions đã chạy lại trên chính portable cuối. Code map: 57/57 file, 10.669 khai báo.

## Một bản được chọn chạy như Active — 2026-10-13

Yêu cầu mới thay chính sách trình bày/chạy một bản của các lượt trước. Không chỉ ẩn dòng Batch: `LoadContextDrawingsCore` và Load Selected thông thường mở đúng target bằng `TeklaAdapter.Open(entry, true)` rồi kiểm lại model/định danh, sau đó mới chuyển radio Active. Bản đang mở đúng target được mượn; bản khác đang mở thì dừng chuyển, không tự lưu/đóng. Một bản dùng report Check DIM đơn và luồng CREATE DRAWING Active hiện có. Danh sách nhiều bản giữ Batch. Nhãn đếm ghi mark của bản đơn; danh sách lỗi Scale vẫn tạo đầy đủ dòng, không thêm điều kiện số lượng vào writer lỗi.

Tách ý định mở editor khỏi việc lấy nguồn: PDF/snapshot gọi overload `LoadContextDrawingsCore(false)`; Batch Create và căn theo bản mẫu gọi `LoadSelectedDrawingsCore(false)`. Nhờ đó chúng không vô tình mở thêm Drawing Editor/chuyển sang pipeline Active. Các điểm gọi không tham số được giữ. Thứ tự chọn Active/dòng list/Document Manager, thuật toán DIM, đọc scale, batch session, F5/Chọn DIM, pipeline Active và Batch không thay đổi.

Backup hash-verified: `backup/001-single-selection-active-20261013`. So SHA-256 tất cả source `.cs` cấp thư mục sản phẩm với snapshot đầu lượt: chỉ `MainForm.cs` và `MainForm.DrawingTargets.cs` thay đổi. Không thay các thay đổi có sẵn của người dùng. Bộ test DrawingSession cũ đã không còn khi bắt đầu; số 417 ở trên là bằng chứng lịch sử, không phải số test lượt này.

Kiểm hiện tại: `pwsh -NoProfile -File tests/SingleDrawingFlow/Run.ps1` đạt **191 assertions offline**, cộng 4 kiểm điểm gọi giữ chính sách PDF/snapshot/Batch Create/căn mẫu. Roslyn trích nguyên các method MainForm thực, toàn bộ resolver/partial target và TeklaAdapter thực; biên dịch C# 7.3/x64, dùng WinForms controls thật trong RAM, Tekla giả và report spy. Phủ Active/Batch, nguồn row/Document Manager, 1/2/3/5 bản, bản đã mở/chưa mở, giữ session, trùng mark khác định danh, model cũ/mất kết nối/thiếu model, mở sai bản/từ chối mở, chuyển model giữa mở/đọc, Scale lỗi đơn/nhiều/lỗi đọc, Scale → mở dòng → report đơn, F5/Chọn DIM từ chối bản khác, định tuyến Create và các nhánh opt-out. Không Show cửa sổ, không chạy engine DIM thật hoặc API Tekla thật; chưa xác nhận live.

Portable Release x64 cập nhật thành công: **40 runtime file** khớp staging `20261013-085356-7ebd2f94`; manifest **41/41** SHA-256 khớp. EXE: `5CA3C179BBBE693261EE0A43683C7B1B4E53317E16ABDA4445BDFDFEF34CA6FD`. Không ghi vào folder model, không dừng tiến trình người dùng, không commit/push/phát hành.

## Print/Merge lấy lựa chọn mới, không ghim bản đã Load — lượt tiếp 2026-10-13

Yêu cầu: A → Load → Print, sau đó chọn B trong Document Manager → Print phải in B; không cần Load lại. Lượt trước làm gọn xử lý bản đơn nhưng vẫn để PDF dùng resolver ưu tiên Active/list, vì vậy radio được bật tự động sau Load và dòng do progress chọn có thể giữ nguồn cũ.

Trước sửa đã bổ sung fixture chạy nguyên `RunPdfCommand` thực, chỉ thay biên Tekla/native printer/dialog bằng RAM. Test thất bại: `A -> Load -> Print -> select B -> Print must use B; observed 1 (keepActive=True)`. Điều kiện này mô phỏng A còn Active sau lượt in; cũng kiểm trường hợp editor đã đóng và dòng cũ còn chọn. Không dùng kết quả offline để khẳng định native PDF đã in thật.

Thay đổi điều phối:

- `ResolveForOutput` riêng cho Print/Merge: Document Manager mới → dòng TTSK được chọn → active dự phòng. Không phụ thuộc radio, không tự tái dùng toàn bộ list cũ; lỗi API không âm thầm chuyển sang nguồn cũ. Loại trùng theo định danh, giữ thứ tự; trùng mark khác định danh vẫn là hai bản.
- `CapturePdfDrawingRequestCore` chốt nguồn, model và metadata jobs trên worker; đọc/kiểm model xong mới thay list. Danh sách jobs của lượt in độc lập với selection thay đổi trong Document Manager. `ShowPdfDrawingRequest` giữ radio Active/Batch và hiện kết quả cho từng bản, kể cả một bản.
- Print/Merge có guard bận chung, bao gồm report DIM, load phím tắt, updater và chuyển phiên. Kiểm lại kết nối/model trước giao jobs cho engine. Lỗi capture không đánh PRINT FAIL lên list cũ; lỗi item ghi thông điệp thực vào RESULT/tooltip. Exception đến sau không ghi đè kết quả item đã hoàn tất. Cuối lượt bỏ selection do progress tạo để tránh in lại nhầm.
- Nút và phím tắt dùng chung `RunPdfCommand`. Metadata nguồn được hiển thị trên status. Không đổi engine tạo DIM, native PDF, merge, cleanup; không đổi snapshot, Merge File, Batch Create hoặc căn theo mẫu. Resolver Check DIM/Scale giữ nguyên; chỉ tách hai callback đọc selection thành helper dùng chung.

Backup: `backup/002-pdf-fresh-selection-20261013`, đối chiếu SHA-256 trước sửa. Source sản phẩm chỉ đổi `DrawingTargetSelection.cs`, `MainForm.DrawingTargets.cs`, và hai method `RunPdfCommand`/`UpdatePdfProgress` trong `MainForm.cs`. Các thay đổi khác đã có trong working tree được giữ.

`pwsh -NoProfile -File tests/SingleDrawingFlow/Run.ps1`: **378 assertions offline**, cộng 4 kiểm caller. Gồm toàn bộ 191 checks của lượt trước và luồng PDF thực với native printer giả: Print/Merge; active còn mở/đã đóng; 1/2/4 target; đổi bản sau in; selection thay đổi giữa lượt; model cũ/mất kết nối/metadata lỗi; nguồn trống; ưu tiên DM trước dòng cũ; fallback row/active; trùng mark/định danh; một/nhiều lỗi/hủy/exception; giữ kết quả đã xong; 11 trạng thái bận; giữ/reset phiên DIM đúng target. WinForms controls trong RAM, không Show/Activate cửa sổ, không API Tekla thật, không PDF thật.

Build Release x64 qua `scripts/Update-Portable.ps1 -BuildOnly -NoPause` thành công; **40 runtime file** khớp staging `20261013-091303-dc1199fd`, **41/41** manifest SHA-256 đúng. EXE: `95B973B1D652A9E5D9BF02961ECFCA488BC78797EC9FEED980E93955E1EF2243`. Chưa xác nhận live Tekla. Không ghi folder model, không dừng tiến trình, không commit/push/phát hành.

## Gộp source điều phối vào MainForm — 2026-10-13

Theo yêu cầu, gộp `MainForm.BatchShortcuts.cs`, `MainForm.DrawingTargets.cs` và `DrawingTargetSelection.cs` vào ba region cùng tên ở đầu `MainForm.cs`, theo đúng thứ tự các phần trước gộp. Giữ nguyên tên lớp/namespace (DrawingTargetSelection vẫn là lớp trong namespace, không đổi thành nested class), mọi field/method/initializer và nội dung thực thi. Chuyển using của từng file vào đúng namespace block tương ứng. Bỏ ba file nguồn riêng và ba Compile item khỏi project. Không đổi luồng/thuật toán. Backup hash-verified: `backup/003-mainform-consolidation-20261013`.

Roslyn đối chiếu toàn bộ token khai báo, imports hiệu lực và thứ tự của 10 namespace block trước/sau gộp, đạt cả `TRACE` và `DEBUG+TRACE`. Cập nhật extractor test để đọc region từ MainForm thay vì file cũ; **378 assertions offline** và 4 kiểm caller vẫn đạt. Project không còn tham chiếu Compile tới ba file đã gộp; tất cả Compile input tồn tại. Bộ test không phụ thuộc backup; đối chiếu token là chế độ kiểm riêng có tham số đường dẫn backup.

Build portable Release x64 thành công, **40 runtime file** khớp staging `20261013-092730-49bdb024`, manifest **41/41** SHA-256 đúng. EXE: `5F14A3D59BE4DCAEDFC1DA5A12B727443FCEECDCCD5C91E09C16AC01428AC07D`. Chỉ tái tổ chức source; không chạy DIM/in PDF hoặc thay đổi bản vẽ thật, không ghi folder model.
