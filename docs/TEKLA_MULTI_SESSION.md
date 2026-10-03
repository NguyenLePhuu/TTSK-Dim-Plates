# Chọn và chuyển dự án Tekla 2025

## Sử dụng

- Khởi động hiện riêng logo PNG công ty giữa màn hình, alpha trong suốt, không form nền/khung/chữ phụ. Nếu Tekla/model đã sẵn sàng thì chuyển thẳng vào giao diện chính (hoặc hộp chọn khi nhiều dự án). Form chờ chỉ xuất hiện khi chưa có Tekla/model hoặc child chưa kết nối được; không hiện popup lỗi và không thoát.
- Form chờ dùng logo lớn làm trọng tâm, vẽ trực tiếp lên nền theme: không vòng tròn, không tile trắng hay viền trang trí quanh logo. Resources/logo.png là bản light 1385×1136 chữ mỏng `株式会社辻鉄商工`; logo_dark.png chỉ đổi RGB chữ thành #FCFCFC, giữ toàn bộ alpha và biểu tượng. Header, startup/logo-only, icon title/taskbar của cửa sổ đang chạy đều theo theme. CompanyLogoRenderer thu nhỏ một lần về đúng pixel đích và form cache bitmap; không phóng lại mỗi frame. Bản gốc, bản đậm và đối chiếu ở docs/assets; chi tiết tại docs/LOGO_SOURCE.md.
- Màn logo chuyển mờ 140 ms rồi giao diện chính hiện dần 180 ms; không bắt người dùng chờ logo theo thời lượng tối thiểu. Đóng màn logo hủy quá trình đang chờ/kết nối. Nút Chọn dự án cho phép chọn lại, kể cả lúc retry.
- Mở TTSK khi có nhiều phiên Tekla sẵn sàng: bấm thẻ dự án để làm việc ngay. Tên dự án lớn, thông tin view/bản vẽ và folder ở dòng phụ. Thẻ sáng lên khi hover hoặc focus; hỗ trợ Tab/Enter và Escape/Đóng để hủy. Không có checkbox, ListBox hoặc nút xác nhận phụ.
- Dòng **Dự án: … ▾** dưới tên ứng dụng cho biết dự án đang kết nối. Bấm dòng này để chọn lại, kể cả kết nối lại cùng PID sau khi đổi model.
- Sau khi chọn thẻ, **Tự động** theo cửa sổ Tekla vừa thao tác; chỉ chuyển khi TTSK hết lệnh/batch/in và đóng các hộp thoại công cụ.
- Nếu mở thêm phiên Tekla trong lúc TTSK đang làm việc: đợi cửa sổ mới có model rồi hiện hộp thẻ để chọn dự án. Hủy giữ phiên hiện tại; không gọi API sớm khi cửa sổ mới còn ở màn hình khởi động. Các phiên đã biết vẫn chuyển tự động khi thao tác cửa sổ.
- **Cố định** chỉ là trạng thái sau một lần bàn giao lỗi; chọn lại bằng thẻ để bật theo cửa sổ.
- Một lệnh đã bắt đầu luôn thuộc phiên cũ. Không chuyển hoặc chạy lại lệnh đó ở dự án mới. Nếu bấm lệnh khi đang chờ chuyển, lệnh được chặn; chạy lại sau khi tên dự án đổi.
- Đổi dự án tạo tiến trình TTSK mới. Vị trí cửa sổ và ghim trên cùng được chuyển; danh sách drawing, trạng thái resume, selection/reference và cache API không chuyển theo. Load Selected lại nếu cần làm batch.
- Lỗi kết nối dự án mới giữ cửa sổ cũ và chuyển về chế độ cố định, tránh vòng lặp tự mở. Chọn lại dự án để thử lại.
- Tiêu đề TTSK chỉ hiện tên dự án, không PID. Status điều phối ngắn; hover status để đọc đủ các thông báo dài khác. Hộp chọn dùng màu dark/light của MainForm; lúc khởi động đọc theme.cfg cạnh ứng dụng. Tên dự án trùng được phân biệt bằng folder/cửa sổ ở dòng phụ.
- Nút Đóng/thẻ dự án luôn vẽ lại nền opaque theo ancestor trước khi vẽ đường bo, loại bỏ nền đen do Clear(Color.Transparent) trên buffer GDI. Khung ngoài dùng compositor DWM Windows 11 để bo mép có antialias và viền theo theme; bỏ Region ở cả form/container và không dùng TransparencyKey. Bán kính ngoài theo chính sách Windows. Dựng đủ control rồi autoscale; chốt chiều cao từng thẻ 110 px × DPI/96 sau autoscale để không tích lũy tỷ lệ.

## Cơ chế

`Tekla.Structures.Internal.Channels` của bản Tekla 2025 đã cài chốt `SESSIONNAME` vào trạng thái static. Phiên Tekla mở thêm có thể dùng `Console-<PID>`, phiên đầu dùng `Console`; không thể suy ra từ thứ tự Process.GetProcessesByName.

`TeklaSessionCoordinator` đọc riêng `SESSIONNAME` của từng tiến trình x64 cùng Windows session qua API chỉ đọc. Nó không xuất toàn bộ environment. Không có quyền đọc, không đúng x64/2025 hoặc phiên đã đóng thì không đoán kênh khác. Cấu trúc PEB x64 dùng offset nội bộ Windows; nếu nền tảng thay đổi cần xác minh lại, không fallback vào phiên đầu tiên.

Tiến trình màn logo không nạp Model/Drawing API. Scan process ở nền mỗi 750 ms, chỉ một scan/attempt tại một thời điểm. Không đọc environment của Tekla khi chưa có model title. Metadata bin/version cache theo PID + start time; SESSIONNAME vẫn đọc lại vì có thể đổi khi Tekla khởi tạo. `IsReady` kiểm process Responding và mở read-only event kênh remoting Model/Drawing của bản Tekla 2025 đã cài. Event tồn tại chỉ là điều kiện để thử; child vẫn phải xác minh cả hai API và model path.

Mỗi attempt kết nối chạy trong một child mới, có timeout 30 giây. Nếu Tekla đóng/đổi model/chưa phục vụ API thì giữ splash, retry sau 2–10 giây; không tái dùng static proxy đã thất bại. Hủy chỉ kết thúc child do attempt đó tạo ra. Parent thoát sau handshake; child kiểm PID + start time của parent trước chờ thoát. Nhờ vậy splash vẫn phản hồi nếu remoting treo. Có thêm một tiến trình ngắn hạn lúc mở ứng dụng; không giữ hai tiến trình làm việc sau bàn giao.

Tối ưu latency: Prepare ở child tìm đúng PID thay vì quét toàn bộ; truyền tập phiên đã biết sang MainForm; scan chuyển phiên chạy background và bỏ kết quả khi trạng thái/focus đổi trong thời gian await. Cache tên PNG theo PictureBox giúp BuildUi/ApplyTheme không giải mã lại cùng ảnh; đổi theme vẫn thay ảnh đúng và dispose ảnh khi đóng. Những thay đổi này không bỏ preflight API.

Trước khi nạp Model/Drawing API, đặt môi trường của **tiến trình TTSK** theo phiên được chọn. Preflight đối chiếu PID + start time, SESSIONNAME, hai connection status, đường dẫn ModelInfo và ranh giới đường dẫn trong tiêu đề cửa sổ. Không dựa vào tên dự án để chọn PID. GetBoundProcess cũng được dùng cho Akit/nhận diện dialog trong Load Standard, Section và Keyplan.

Khung picker giữ WS_CAPTION/WS_THICKFRAME trong CreateParams để DWM nhận diện frame, xử lý WM_NCCALCSIZE để dùng header tự vẽ và WM_NCHITTEST để không resize hộp chọn. DwmSetWindowAttribute(33, ROUND) làm mượt mép ngoài, attribute 34/20 đặt màu viền/dark mode. Không có Region/layered alpha vốn chặn bo góc DWM theo [tài liệu Microsoft](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners). Nếu Windows cũ không hỗ trợ attribute 33, giữ viền thẳng thay vì quay lại góc cắt răng cưa. Chỉ kiểm chứng compositor trực tiếp trên Windows 11 hiện tại.

Đổi kết nối bằng tiến trình mới để loại bỏ static remoting proxy và object/cache cũ. Windows events readiness/accept bảo vệ bàn giao; child chờ parent thoát trước khi hiện UI. Thời hạn chuẩn bị 30 giây; lỗi hủy child do chính lần chuyển tạo ra. Không đóng Tekla.

Foreground hook theo dõi cả cửa sổ drawing/dialog cùng PID. Timer 500 ms chỉ thực hiện yêu cầu khi rảnh; polling foreground là fallback nếu không đăng ký được hook. Worker hole-mark/print kế thừa môi trường phiên. Worker render PNG đã có dữ liệu xuất riêng nên không cần Tekla còn mở.

**Không tạo/ghi/sửa file hoặc thư mục trong model.** Điều phối dùng biến môi trường cục bộ và Windows events, không đặt marker/macro/cache/log trong model. Quy tắc này đã được ghi vào AGENTS.md. Việc kiểm tra ở đây không chạy chức năng sản phẩm có tác động bản vẽ.

## Kiểm chứng ngày 2026-10-03

- Audit bổ sung: mô phỏng discovery không Tekla → Tekla chưa model → phiên thật sẵn sàng; kiểm cancel lúc chờ và lúc child bắt đầu; mô phỏng model snapshot stale để xác minh child từ chối rồi host retry kết nối lại bằng child mới. Không đóng/mở lại Tekla của người dùng để tạo trạng thái thử. Host chờ không nạp assembly Model/Drawing, không sót child sau cancel.
- 12 trường hợp parser/path: model/view/drawing title, tên folder có ` - `, đường dẫn UNC và chống nhận nhầm prefix. Đường dẫn model được truyền sang child cùng PID/start để chặn đổi model giữa lúc chọn và bàn giao.
- Hai phiên thực tế trong lần audit: Hulic B `[CmH-14]`, Shinjuku `[A-CDA-4]`, đều kết nối được Model + Drawing bằng probe chỉ đọc. Startup logo → picker → main và chuyển hai chiều vẫn qua guard bận/hủy/lỗi.
- Đo A/B 3 lượt cùng máy/cùng probe (baseline build trước audit, cache hệ thống ấm): median Prepare 19 → 8 ms; median dựng MainForm 730 → 576 ms; dựng + Shown 818 → 674 ms. Verify API dao động theo trạng thái Tekla, không dùng để tuyên bố tăng tốc. Logo first paint trong phép thử khoảng 354–480 ms từ lúc dựng splash. Đây là đo từng pha tại máy hiện tại, không phải cam kết thời gian khởi động tổng hoặc tốc độ mọi máy; tiến trình cách ly và animation cũng có chi phí.
- Splash dark/light đã capture cửa sổ thật ở 125%; logo lớn không vòng tròn/ô trắng. Test `wait-ready` xác minh Tekla sẵn sàng giữ form chờ opacity=0, không vào WaitingPresentation, logo-only có alpha góc bằng 0 rồi bàn giao main. Test `wait-cancel` xác minh chưa Tekla bật form chờ opacity=1. Ảnh nguồn được thay sau audit theo lựa chọn ImageGen ngày 2026-10-04; không thay thuật toán phiên Tekla.
- Release x64, C# 7.3 / .NET Framework 4.8; portable cập nhật bằng Update-Portable.ps1 -BuildOnly -NoPause và kiểm 39 SHA-256.
- Probe chỉ đọc kết nối được Model + Drawing của cả Shinjuku (`27-027)Sinjuku_Slope`) và Hulic B (`27-000)HULIC_B`). Shinjuku không có drawing active; Hulic B có SinglePartDrawing `[BmH-12]`.
- Bàn giao UI thực tế kiểm cả hướng Shinjuku → Hulic B và Hulic B → Shinjuku, xác minh title child rồi đóng bình thường.
- Kiểm PID/start time cũ, đổi model context, pending focus rồi quay lại phiên hiện tại, các cờ đang bận trên form đang hiện, child không thể kết nối và giữ nguyên parent.
- Test tự động cung cấp sự kiện focus cho bộ theo dõi, kiểm timer dẫn tới bàn giao; không thao tác chuyển focus cửa sổ Tekla của người dùng.
- Kiểm bổ sung UI: render thẻ dark/light với ba dự án, tên dài và chọn một lần; mô phỏng phiên thứ hai mới xuất hiện bằng bộ nhận diện chưa biết PID trên hai phiên có sẵn, xác minh hộp chọn hiện trước bàn giao, hủy giữ UI, chọn thẻ chuyển đúng. Không khởi chạy thêm Tekla/model để kiểm thử.
- Kiểm góc bo: render bitmap ở 96 DPI và capture cửa sổ thật trên nền thử ở 120 DPI (125%) cho dark/light. Đối chiếu cả bốn góc nút với nền form (sai số màu tối đa 2 do compositor), xác minh không còn Region, preference DWM là ROUND và cả bốn góc có pixel chuyển sắc sau khi animation mở cửa sổ kết thúc. Capture có 24–37 màu chuyển sắc/góc tùy theme. Chiều cao thẻ đúng tỷ lệ DPI, một lần bấm vẫn chọn đúng. Manifest/cấu hình DPI chỉ thêm cho probe, không thay DPI của MainForm/Tekla.
- Không chạy DIM, Insert/Modify/Delete/CommitChanges, macro, thay selection, mở/lưu drawing hoặc ghi folder model. Chưa kiểm mutating DIM hoặc trường hợp ba phiên Tekla đang mở thực tế.

Lệnh kiểm: `pwsh -NoProfile -File tests/TeklaSessions/Run-ReadOnly.ps1`. Thêm `-UiHandoff [-StartPid <PID>]` khi TTSK đang đóng để kiểm cửa sổ và bàn giao; `-StartupWait -NativePreview` kiểm chuỗi chờ/hủy/retry/kết nối từ nguồn discovery mô phỏng tới API phiên thật. `-PreviewOnly` render picker độc lập, không kết nối Tekla; thêm `-NativePreview` để capture cửa sổ thử thật ở DPI màn hình. Sau compile, SessionProbe nhận mode `audit-pure` hoặc `benchmark`. Build/probe/manifest/PNG nằm trong `.codex-artifacts/session-tests`; không phải phụ thuộc runtime.
