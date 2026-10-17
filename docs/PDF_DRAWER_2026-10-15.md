# Panel in danh sách đồng bộ — 15-10-2026

## Cách dùng

- Chuột phải **Print** mở panel ở bên phải MainForm. Màn hình không đủ rộng dùng panel phủ bên phải; đóng panel giữ danh sách/kết quả. Công cụ trượt khác và panel in không mở chồng nhau.
- Danh sách chính là nguồn chung. **Thêm bản vẽ** đọc selection mới trong Document Manager, bỏ trùng theo đối tượng, giữ các dòng đã có. Load/Clear Log ở MainForm cũng phản ánh sang panel. Chọn dòng đồng bộ hai chiều; không đổi selection của Tekla chỉ vì chọn dòng trên UI.
- **Bỏ dòng/Xóa hết** sửa cả hai bảng. Không xóa drawing trong Tekla. Kết quả, tooltip đi theo đối tượng khi thêm/bỏ/đổi thứ tự; bản trùng tên không bị gộp nhầm.
- Ưu tiên diện tích danh sách: bỏ ô tìm kiếm, nhóm lọc, hàng hướng dẫn/công tắc và chú thích lặp lại. Mọi trạng thái luôn nằm trong cùng list; **In PDF/Gộp PDF dùng toàn bộ danh sách**. Tổng số nằm dưới tiêu đề, trạng thái/lỗi giữ ở từng dòng và thanh thông báo.
- Kéo cụm chấm ở đầu dòng để sắp xếp luôn sẵn dùng khi không bận; hỗ trợ nhóm dòng đã chọn, vạch chỉ điểm thả và cuộn khi kéo sát mép. Alt+Up/Down di chuyển các dòng chọn; Delete bỏ dòng, Esc thu gọn. **Bỏ dòng** xuất hiện cùng hàng Thêm bản vẽ/Xóa hết khi có selection.
- Tự cuộn theo bản đang in luôn bật, không cần công tắc. Giao diện dùng icon nét vector, font Segoe UI/Segoe UI Semibold, trạng thái màu và thanh cuộn theo theme. Bo 4 góc ngoài bán kính thiết kế 16 px, vẽ khử răng cưa trên nền cha. Đổi theme MainForm cập nhật ngay panel.
- Ở panel 470×628 px, vùng list cao 412 px, thêm 168 px so với bố cục trước; hiển thị 10 dòng đầy đủ cùng header. Khi đổi chiều cao, vùng list nhận phần diện tích còn lại.

## Dữ liệu và vòng đời

`ReadMainPdfQueue` chỉ đọc metadata/đối tượng đã nạp, không gọi API Tekla. Nạp dở (số đối tượng và số dòng chưa bằng nhau) được hoãn; thao tác in/thêm không dùng lại snapshot cũ khi gặp trạng thái này. Snapshot in mới vẫn được xác minh model/đối tượng trên worker bằng `CaptureAccumulatedPdfRequestCore` như trước.

`PublishPdfQueue` chụp trạng thái/tooltip/style **trước** khi clear hàng; không đọc cell của row đã bị DataGridView tách khỏi bảng. Thứ tự ảnh hưởng cả MainForm và thứ tự in. Đồng bộ chọn có cờ chống gọi lại; callback trước khởi tạo/sau dispose bị chặn, timer/sự kiện được gỡ khi hủy panel. Phím tắt MainForm không chạy xuyên qua panel. Trong lệnh Tekla/PDF/Check DIM/updater, khóa sửa danh sách và in lồng; xem/cuộn vẫn hoạt động.

Kéo thả dùng token riêng của panel, giữ identity/model/thứ tự lúc bắt đầu; từ chối drag ngoài, đổi model, thay danh sách hoặc đang bận. Không serialize Drawing/Control qua OLE.

Các điểm xử lý PDF cũ và engine `PHU_PDF-Merge.cs` không thay đổi. RunPdfCommandAsync, RunAccumulatedPdfCommandAsync, CapturePdfAdditionCore/CaptureAccumulatedPdfRequestCore và CapturePdfDrawingRequestCore được so nguyên văn với backup đầu lượt. Không gọi DIM, Save, Modify, CommitChanges trong phần UI mới; không tạo file trong model.

## Lỗi được người dùng báo

Ảnh Microsoft .NET Framework chỉ có “Object reference not set to an instance of an object”, không có Details/stack trace và thao tác chính xác. **Chưa tái hiện được đúng lỗi trong ảnh**, không khẳng định đã xác định nguyên nhân. Đã sửa lỗi snapshot row phát hiện qua kiểm thử, bảo vệ vòng đời khởi tạo/hủy, nguồn đang nạp dở và ngăn kiểu nút bị theme tổng quát thay lại. Lỗi tại điểm mở/thêm/in/chỉnh danh sách được hiển thị inline; stack trace ghi tại `%LocalAppData%/TTSK/Logs/pdf-list-ui.log`, ngoài model, để truy nguyên nếu tái diễn.

## Kiểm tra

- `pwsh -NoProfile -File tests/PdfDrawer/Run.ps1`: trích đúng UI/đồng bộ sản phẩm, fake API/dispatcher; 208 assertions gồm dữ liệu, selection, thêm/bỏ, đổi thứ tự nhóm, drop handler/token/model/identity, giữ kết quả, in toàn bộ, tự cuộn khi in sau đổi theme, đủ 10 dòng, 4 góc bo, callback sau dispose, nguồn nạp dở và layout. 6 ảnh light/dark ở tỷ lệ bố cục/font giả lập 100/125/150%; đã xem ảnh. Không coi đây là thao tác DPI trên app người dùng.
- `pwsh -NoProfile -File tests/PdfDrawer/Run-Native.ps1`: biên dịch toàn bộ source .NET Framework 4.8 x64 trong thư mục riêng; chỉ bỏ 3 lệnh khởi động updater/journal/workspace trong **bản test**. Dùng MainForm/BuildUi/theme/panel thật; 12 chu kỳ đổi theme/mở/ẩn/render, list rỗng/có dòng và dispose; xác minh ảnh không trống. Không Show cửa sổ, không điều khiển máy hoặc gọi Tekla.
- Release x64 và portable: `pwsh -NoProfile -File scripts/Update-Portable.ps1 -BuildOnly -NoPause`; kiểm SHA-256 manifest và EXE build/portable; CodeMap generate + Check.
- Chưa in thật hoặc kiểm thao tác kéo bằng chuột trên Tekla trong lượt này. Các kết quả live PDF ngày 14-10 thuộc giao diện trước; không dùng chúng làm bằng chứng live của UI mới.

Backup: `backup/001-pdf-list-drawer-20261015`, `backup/002-pdf-drawer-polish-20261015`, `backup/003-pdf-drawer-drag-20261015`, `backup/004-pdf-drawer-list-first-20261015`. Source sản phẩm chỉ sửa MainForm.cs. Không commit/push/phát hành.
