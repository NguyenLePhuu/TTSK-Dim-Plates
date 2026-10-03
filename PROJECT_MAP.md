# PROJECT MAP — TTSK Dim Plates

Bản đồ để chọn nơi sửa; chi tiết lớp/hàm tra khi cần. `AGENTS.md` hướng Codex đọc file này trên mọi máy. Đường dẫn `.cs` bên dưới tính từ **`TTSK Dim Plates/TTSK Dim Plates/`**. Tên file cũ sau gộp là tên `#region`, không phải file còn tồn tại.

## 1. Tra đúng chỗ trong vài dòng

1. Chọn chức năng ở bảng dưới; đọc chú thích đầu file và hàm đích.
2. Dùng `-Find` để lấy khai báo, file, dòng đầu/cuối; dùng `rg` tìm caller và chuỗi reflection.
3. Đọc caller, thông số và phép đổi đơn vị liên quan trước khi sửa. Không nạp toàn bộ source hoặc cache.

```powershell
pwsh -NoProfile -File scripts/Update-CodeMap.ps1 -Find RunCurrentAutoDimScript -Kind Method
pwsh -NoProfile -File scripts/Update-CodeMap.ps1 -Find ExecuteAfterShape -File '*Column*' -Limit 5
pwsh -NoProfile -File scripts/Update-CodeMap.ps1 -Find 'DIM_TIER_SCALE_5' -File PHU_Shape.cs -Kind Field
rg -n -F 'TênHàm' 'TTSK Dim Plates/TTSK Dim Plates' -g '*.cs'
```

`-Find` tìm theo tên/owner, không phân biệt hoa thường, hỗ trợ `*`/`?`; mặc định trả tối đa 12 kết quả và báo tổng số. `-File` lọc đường dẫn; `-Kind` nhận Type/Method/Field/Property/Event/Namespace/Constant/Local/Alias/Attribute/Region. Không tìm thấy thì kiểm source/chuỗi bằng `rg`. Công cụ phân tích khai báo, không tự kết luận quan hệ gọi qua reflection.

Tra cứu cần PowerShell 7 và .NET SDK ≥8. Nếu máy thiếu, chọn file từ bảng rồi dùng `rg -n`; bản đồ vẫn đọc được. Cache `.codex-artifacts/code-map-tool/catalogue-v2.json` tự sinh/tự làm mới, không cần mang sang máy khác hoặc đưa vào ngữ cảnh AI.

## 2. Nơi sửa theo chức năng


| Chức năng | File / điểm bắt đầu |
| --- | --- |
| Khởi động, logo/chờ Tekla, worker; chọn nhiều dự án | `Program.cs`: Program.Main, CompanyLogoWindow, TeklaStartupForm.ConnectWhenReadyAsync/StartWorkspaceAsync, ProgramTeklaStartup.Run, TeklaSessionCoordinator, TeklaProjectPickerForm |
| Logo light/dark, icon title/taskbar, cache DPI | `Program.cs`: CompanyLogoRenderer.Load/Render/CreateIcon; `MainForm.cs`: SetFormIconFromLogo; `Resources/logo.png`, `logo_dark.png`; `docs/LOGO_SOURCE.md` |
| UI, chọn engine, Active/Batch | `MainForm.cs`: RunActiveDrawing, RunBatchDrawings, RunCurrentAutoDimScript |
| Chạy Tekla trên luồng riêng; chuyển dự án khi rảnh | `TeklaOperationDispatcher.cs`; region MainForm.Operations: InitializeTeklaSessionUi, SwitchTeklaSession, ExecuteTeklaCommandAsync |
| Target của bản vẽ/selection | `DrawingTargetSelection.cs`, `MainForm.DrawingTargets.cs` |
| Phím tắt/batch từ phím | `ShortcutManager.cs`, `ShortcutSettingsForm.cs`, `MainForm.BatchShortcuts.cs` |
| DIM tấm Single Part | `PHU_AutoDim_OK-V3.cs`: Script.Run, CreateDimsBySectionPolygon |
| DIM H/I | `PHU_Shape.cs`: ShapeScript.Run, CreateDimsFor*View |
| DIM C / hộp / L / dự phòng | `PHU_Shape_C.cs`, `PHU_Shape_Box.cs`, `PHU_Shape_L.cs`, `PHU_Shape_Unknown.cs` |
| Tạo section tấm/H/C | `PHU_Section.cs`: AutoSectionCoordinator.Run, SectionScript |
| Slot 01 main part được chọn | `PHU_Slot01_SelectedMainPartAutoDim.cs` |
| Slot 02 tấm neighbor/reference | `PHU_Slot02_NeighborReferencePlateDim.cs` |
| Slot 03 DIM tấm section neighbor | `PHU_Slot03_NeighborPlateSectionDim.cs` |
| Slot 04 pallet/profile và lệch trái/giữa/phải | `PHU_Slot04_PalletAutoDim.cs` |
| Slot 05 cạnh/lỗ tấm | `PHU_Slot05_SelectedPlateEdgeHoleDim.cs` |
| Slot 06 giằng Type 1/2 | `PHU_Slot06_BraceConnectionAutoDim.cs`: PHU_AutoDimSlot06.Run |
| Slot 07 Data Center/Inzai | `MainForm.cs`: RunDataCenterCurrentDrawing |
| Grid dầm | `PHU_BeamGridDimensionEngine.cs` |
| Grid cột H/C/L, chọn dự án | `PHU_ColumnGridDimensionEngine.cs` |
| Cột Inzai neighbor/splice/section | `PHU_InzaiColumnNeighborDimensionEngine.cs` |
| Liên kết và grid cột hộp Slot 07 | `PHU_Slot07_ColumnConnections.cs`: Read/Build/BuildColumnGrid/Execute |
| Dầm Data Center Type 2, section Type 1 | `PHU_Slot07_DataCenterBeamType2DimensionEngine.cs` |
| Liên kết tham chiếu / splice Inzai | `PHU_RefConnectionAutoDim_Temp.cs` |
| Nishi Azabu hai topology | `PHU_NishiAzabuAutoDim_Temp.cs` |
| Slot 08 dọn neighbor | `PHU_Slot08_NeighborCleanup.cs` |
| Slot 18 audit spacing | `PHU_Slot18_DimSpacingAudit.cs` |
| Sắp spacing DIM | `PHU_DimSpacing.cs`: PHU_DimSpacingNormalize |
| Vẽ line bằng chiều dài | `PHU_LineDistance.cs` |
| Mở grid / fit view | `PHU_OpenGridView.cs`, `PHU_FitNeighborEnvelope.cs` |
| Arrange section | `PHU_ArrangeView.cs` |
| Kiểm tra chân DIM | `PHU_DimensionCheck.cs` |
| Kiểm DIM batch và form | `PHU_DimensionBatch.cs`, `PHU_DimensionReportForm.cs` |
| Revision/tô màu/tạo lại giữ layout | `PHU_DimensionRevision.cs` |
| Arrange theo mẫu | region PHU_ArrangeByReference trong `PHU_DimensionRevision.cs` |
| Đánh dấu part đã xóa | `PHU_AllPartsDeleted.cs` |
| Chụp bản vẽ tham chiếu | `PHU_CopyDrawingSnapshot_Temp.cs` |
| Load standard | `PHU_LoadStandard.cs`, `Phu_Macro_LoadStandard.cs` |
| Hiện/ẩn grid bằng macro | `Phu_Macro_GridVisibility.cs`; MainForm |
| Slot 09 Keyplan | `PHU_Keyplan.cs`: Geometry, DrawingSettingsReader, KeyplanForm |
| PDF/in/gộp | `PHU_PDF-Merge.cs`; MainForm.RunPdfCommand |
| Từ điển Nhật–Việt | `JapaneseDictionary.cs`, `Data/JapaneseDictionary.tsv` |
| Nhật ký công việc | `WorkJournal.cs`; MainForm.WorkJournal |
| Kiểm/tải cập nhật | `Updater/UpdateManager.cs`, `Updater/UpdateSources.cs` |
| Manifest/version/bảo vệ đường dẫn | `Updater/UpdateManifest.cs` |
| Cập nhật file/khôi phục | `Updater/UpdateTransaction.cs` |
| Phiên bản và file sinh tự động | `Properties/AssemblyInfo.cs`, `Properties/Resources.Designer.cs`, `Properties/Settings.Designer.cs` |

## 3. Luồng chính và ranh giới dễ sửa nhầm

```text
Program.Main → updater worker/recovery hoặc logo TeklaStartupForm (normal launch) → dò nền/chờ Tekla+model+kênh API → chọn dự án
Logo → child có PID/start/model đã chốt → TeklaSessionCoordinator.Prepare → ProgramTeklaStartup.Run → VerifyConnection → MainForm → ready/accept → fade
Worker/child bàn giao → Prepare trực tiếp đúng PID; snapshot PNG worker độc lập bỏ qua Prepare
Chọn dự án/cửa sổ Tekla foreground → chờ hết lệnh/dialog → tiến trình TTSK mới theo SESSIONNAME → xác minh Model + Drawing → bàn giao UI
Phiên Tekla vừa mở → chờ model xuất hiện trong tiêu đề → hộp thẻ chọn dự án → chọn để chuyển / hủy để giữ phiên cũ
Active/Batch → RunCurrentAutoDimScript → nhận part → Auto Section tùy chọn → Plate/H/C/L/Box/Unknown
Slot 07 → RunDataCenterCurrentDrawing → chọn cột H/C/L, cột hộp hoặc dầm Type 1/2
Cột H/C/L → Shape → CaptureShapeDimensionsBeforeAddOns → Neighbor → Splice → Grid → Section → căn layout
Grid dầm → Prepare → Shape báo tầng → CreatePreparedDimensions
```

- **Thứ tự ghi:** tấm/Shape/Slot 06 có đoạn xóa DIM trước tạo; Nishi tạo và kiểm tra trước xóa nền; Revision có scope xác minh thay thế. Giữ đúng chính sách từng engine.
- **Tác động:** DimensionCheck.Analyze/Audit chỉ đọc; DimensionRevision.Analyze có thể ghi màu; resolver thuộc tính Section có thể mở dialog. Xem body trước dùng làm audit.
- **MainForm:** xử lý theo `_selectedDrawings`; RefreshContextDrawingRows chỉ hiện bảng khi >1 bản. Một bản/Active vẫn được xử lý; không lấy số dòng UI làm số target.
- **Nhiều phiên Tekla:** đọc SESSIONNAME từ tiến trình Tekla 2025 x64 cùng Windows session trước khi chạm API. Ghép PID + thời điểm khởi động; xác minh model path với tiêu đề cửa sổ. Không sửa static cache Tekla để reconnect: chuyển bằng tiến trình mới, handshake readiness/accept qua Windows event, thất bại giữ UI cũ. Không mang drawing object/selection/batch resume sang dự án mới. Akit Load Standard/Section/Keyplan dùng PID đã gắn. Snapshot PNG worker độc lập không cần Tekla; worker khác kế thừa phiên từ môi trường.
- **Khởi động/chờ:** TeklaStartupForm chỉ dùng process/event, không nạp Model/Drawing API; ban đầu opacity=0. CompanyLogoWindow hiện đúng PNG công ty bằng UpdateLayeredWindow alpha từng pixel, không nền/viền/chữ phụ. Tekla/model sẵn sàng thì chỉ logo → picker nếu nhiều dự án → main. Chưa sẵn sàng/lỗi child mới hiện form chờ với logo lớn trực tiếp trên nền theme, không vòng tròn/tile trắng. Scan nền 750 ms không chồng. IsReady mở event kênh 2025 đã tồn tại rồi child xác minh API thực. Retry tiến trình mới giãn 2–10 giây, timeout 30 giây; đóng splash hủy đúng child. Fade logo 140 ms/main 180 ms, không giữ logo theo thời lượng tối thiểu. Chọn lại phá thời gian chờ retry. Các cache metadata chỉ ở RAM; không ghi model.
- **Tối ưu:** Prepare có PID chỉ đọc process đó; phiên đã biết truyền qua environment giúp MainForm bỏ scan startup trùng. Scan chuyển phiên chạy nền, kiểm lại trạng thái rảnh/PID sau await, không dùng kết quả cũ khi focus đổi. MainForm.LoadAutoDimSlotPicture bỏ giải mã lại PNG cùng control/tên file; đổi theme vẫn nạp ảnh tương ứng, ảnh giải phóng khi dispose.
- **Logo:** nguồn ImageGen 1385×1136, chữ mỏng `株式会社辻鉄商工`. Light dùng logo.png; dark dùng logo_dark.png với chữ #FCFCFC, giữ nguyên alpha/toàn bộ pixel ngoài chữ (tạo/kiểm bằng scripts/Update-DarkLogo.ps1). CompanyLogoRenderer.Load dùng chung cho logo-only/splash/main; Render thu đúng pixel và cache. CreateIcon tạo ICO 16/24/32/48/64/128/256; SetFormIconFromLogo đổi logo header + Form.Icon trong ApplyTheme, dispose icon cũ thay GetHicon bị rò handle. Icon title/taskbar đang chạy được cập nhật cùng theme. Bản gốc và bản đậm giữ ở docs/assets; xem docs/LOGO_SOURCE.md.
- **Hộp chọn dự án:** thẻ bấm một lần, không checkbox/ListBox/xác nhận phụ; màu theo MainForm hoặc theme.cfg khi khởi động. Tên dự án tách từ tiêu đề process, không truy cập model. Phiên mới chưa có đường dẫn model thì giữ pending; khi sẵn sàng và TTSK rảnh mới hỏi chọn. Escape/Đóng giữ phiên cũ. Status chuyển dự án ngắn, thông báo khác có tooltip để đọc trọn; title chính không hiện PID.
- **Góc bo hộp chọn:** ProjectCardButton vẽ lại nền opaque từ ancestor trước mỗi paint, không Clear(Color.Transparent). Khung ngoài dùng DWM Windows 11 (corner preference 33 + border color 34), không Region/TransparencyKey để tránh mép cắt nhị phân răng cưa. CreateParams giữ WS_CAPTION/WS_THICKFRAME cho compositor, WM_NCCALCSIZE giữ header tự vẽ, WM_NCHITTEST chặn resize; drag header vẫn hoạt động. Windows không hỗ trợ DWM rounding giữ viền thẳng. Dựng layout xong mới autoscale; chiều cao RowStyle chốt theo DPI khi Shown để tránh tích lũy tỷ lệ. Kiểm native 125% bằng `tests/TeklaSessions/Run-ReadOnly.ps1 -PreviewOnly -NativePreview`: capture sau animation, xác minh chuyển sắc cả bốn góc (cấu hình DPI chỉ của probe).
- **Folder model:** không tạo/ghi/sửa file hoặc thư mục trong model khi phát triển/kiểm tra. Bộ điều phối chỉ đọc thông tin process/API và dùng môi trường tiến trình + Windows event, không đặt marker/macro/config/cache trong model.
- **Context:** General/Inzai, manual scale và Type 1/2 theo Configure/Prepare/Begin/Reset/Dispose của lần chạy. activeQuickDimensionOnly của Data Center giữ layout đã chuẩn bị; nhánh đầy đủ còn Open Grid/Fit.
- **Tọa độ:** xác nhận current-plane/global/view trước tính chân DIM. Khoảng view/model là mm hình học; sheet là mm giấy; gap giấy × mẫu số scale mới thành khoảng view.
- **Reflection/partial:** giữ tên lớp/namespace/public static Run và LastRunSucceeded/LastRunMessage; tìm cả chuỗi khi đổi tên. Giữ phạm vi using/alias và thứ tự field initializer khi gộp partial.
- **Macro:** C# nằm trong string (Slot08/OpenGrid) và macro Content là code thực thi; sửa comment trong chuỗi vẫn thay dữ liệu runtime.

## 4. Thông số cần tìm

| Muốn chỉnh | Nguồn đang điều khiển / đơn vị |
| --- | --- |
| Tầng tấm | `NORMAL_NO_CHAMFER_DIM_OFFSET`, `InitializeCurrentDimTierSpacing`, `GetCleanDimOffsetByTier`: mặc định 100 mm view; manual scale ×10. `DIM_TIER_1..4` cũ không được đọc. |
| Tầng Shape | `DIM_TIER_SCALE_*_BASE/STEP/MIDDLE`; `GetSteelDimOffsetByTier` = BASE + max(0,tier) × STEP, mm view. Current*/Last*/Pending* là trạng thái. |
| Manual scale | `ManualDrawingScaleOverride.BeginRun`: null tự động, dương ép, âm loại scale; IsAllowedScale/ChooseAutoScale phải khớp bảng tầng và UI. |
| Bật/tắt, lọc hình học | `AUTO_DIM_*`, `ENABLE_*`, `*_TOL`, Geometry/MatchTolerance; chỉ có hiệu lực tại nơi đọc. Tăng dung sai có thể đổi feature/nhóm chân; cosine gần 1 siết hướng. |
| Lỗ, chamfer/notch | `HOLE_*`, `TOP_BOTTOM_VERTICAL_CLUSTER_*`: mm/ratio; không dùng một quy tắc cụm cho cả ngang/dọc. EXTRA_OFFSET cũ không dùng ở H/Box, còn caller rãnh riêng trong C/L. |
| Section | B_EXTRA_DEPTH/C_EXTRA_START: mm hình học; *_PAPER_MM: mm giấy; DEFAULT_SECTION_GAP kết hợp 0.65 × chiều cao view. LINE_LEFT_TO_RIGHT có thể đảo hướng cắt. |
| Style/mark/Arrange | Tên attribute phải nạp được; ID dialog/index dòng phụ thuộc Tekla. *_PAPER là mm giấy; *_DEG là độ; *_PENALTY là trọng số. Arrange chỉ move section thường; mainHorizontal giữ tương thích. |
| Kiểm DIM/revision | Tolerance 0.04 / FootTolerance 0.025 mm, không nhân scale; nới có thể che sai chân/ghép sai revision. ExpectedPlanCount là invariant, không phải nút tăng số DIM. |
| UI/updater | Main*/Slide*/padding: pixel; font: điểm chữ. Max*Bytes: byte; timeout: đọc hậu tố Seconds/Milliseconds; _checkInterval: chu kỳ kiểm nền. |

Cấu hình: `theme.cfg`, `auto_section.cfg` cạnh EXE; `shortcut.cfg` theo baseFolder; keyplan ở `%LocalAppData%/TTSK/Keyplan/keyplan.cfg` (có migrate bản cũ); ManualScaleDenominator thuộc .NET user settings; provider trong `App.config`; `TeklaBinPath` chọn DLL. Thiết lập đã lưu có thể ghi đè mặc định.

## 5. Build, kiểm tra và tài liệu sâu

- **Dọn rác:** `tests/` và `.codex-artifacts/` không phải phụ thuộc build/chạy sản phẩm; test, cache, dump và bản sao bản vẽ có thể dọn khi lệnh đã kết thúc. Xóa test làm mất khả năng chạy lại test đó, không làm hỏng ứng dụng. Giữ source project/tài nguyên/thư viện để build, `portable/` để chạy và cấu hình người dùng để giữ thiết lập.

- Project: `TTSK Dim Plates/TTSK Dim Plates/TTSK Dim Plates.csproj`; Compile liệt kê tường minh. C# 7.3, .NET Framework 4.8, Tekla 2025, Release x64; AnyCPU có cảnh báo DLL AMD64.
- Build/bàn giao code: `scripts/Update-Portable.ps1 -BuildOnly -NoPause` xác minh 40 file runtime (đã gồm logo_dark.png); Publish/backup/upload là lệnh riêng. Không sửa thuật toán trong portable.
- Test offline: BoxRouting, RecessedFlange, Slot02SideMarks, BatchShortcuts. DrawingTargets cần Tekla; đọc README trước mode live-* vì có mode ghi bản vẽ. `tests/Updater` đang bị xóa từ trước; không tự khôi phục xóa của người dùng.
- Kiểm đa phiên chỉ đọc: `tests/TeklaSessions/Run-ReadOnly.ps1`; thêm `-UiHandoff` để kiểm cửa sổ/bàn giao, `-StartupWait -NativePreview` kiểm nguồn discovery giả lập không Tekla → chưa model → phiên thật sẵn sàng, hủy/retry/không sót child. TTSK phải đang đóng. Probe `audit-pure` kiểm parser đường dẫn, `benchmark` đo từng pha. Không gọi DIM, đổi selection, mở/lưu bản vẽ hoặc ghi model. Chi tiết: `docs/TEKLA_MULTI_SESSION.md`.
- Sau đổi cấu trúc/hành vi: cập nhật bảng/luồng này, chạy `pwsh -NoProfile -File scripts/Update-CodeMap.ps1`, rồi thêm `-Check`. Query luôn kiểm hash source; Check còn kiểm snapshot bản đồ. Nếu chỉ sửa tài liệu/công cụ, kiểm tra chúng, không cần build lại runtime.
- Chi tiết theo việc: `docs/SOURCE_ORGANIZATION.md` (file cũ→mới); `docs/SLOT02_SIDE_MARKS_2026-10-02.md`; `docs/ARRANGE_VIEW_2026-09-21.md`; `docs/ARRANGE_REFERENCE.md`; `docs/CHECK_REVISION_CREATE_DRAWING.md`; `docs/SLOT09_KEYPLAN.md`; `docs/GOOGLE_DRIVE_UPDATES.md`. Chỉ đọc tài liệu liên quan; source hiện tại ưu tiên hơn mô tả lịch sử.
- Tài nguyên: `.resx`/`.settings` là nguồn Designer; `Data/JapaneseDictionary.tsv` là dữ liệu từ điển; `Resources/` là artwork. Script đóng gói/phát hành trong `scripts/`, yêu cầu tại `update/release-request.json`, workflow `.github/workflows/release-portable.yml`. Chỉ mục kiểm hash cả các file phụ hiện có.

<!-- BEGIN GENERATED CODE MAP -->

## Độ phủ kiểm bằng máy

- Bảng điều hướng bao phủ **55/55 file C#**: 53 Compile + 2 macro Content; không có C# ngoài danh mục project.
- Tra theo yêu cầu: **9457 khai báo**, **588 kiểu/delegate**, **4920 method/constructor/operator**; kể cả partial/nested type, field, property, event, enum và hằng cục bộ.
- Phân tích C# 7.3, hợp TRACE/DEBUG+TRACE. Độ phủ cấu trúc không chứng minh mọi hành vi runtime; caller/chuỗi reflection tìm trong source.
- Chỉ mục cục bộ tự tạo lại khi đầu vào đổi hoặc sang máy mới; không đọc trực tiếp file cache.
<!-- source-fingerprint: f5b8f7424ce7eea302409aa07725be5ff3da85534a67e6f311ebc03e7d49ab47 -->
