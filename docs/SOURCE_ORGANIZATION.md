# Sơ đồ mã nguồn sau khi gộp

Ngày 03/10/2026. Thư mục nguồn: `TTSK Dim Plates/TTSK Dim Plates`. Số mục C# biên dịch giảm từ **95 xuống 53** (gộp 62 file thành 20 file chức năng, bỏ 42 file con). Hai macro Content và các file Designer vẫn giữ riêng.

Bản đồ ngắn cho AI: [PROJECT_MAP.md](../PROJECT_MAP.md). Tra khai báo bằng `scripts/Update-CodeMap.ps1 -Find TênHàm`; chỉ mục đầy đủ nằm trong cache cục bộ, không cần đọc toàn bộ. Thêm `-Check` để kiểm tra độ mới của bản đồ.

## Cách tìm thuật toán

Mỗi file đã gộp có mục lục ở đầu và `#region` ghi tên phần, nhiệm vụ bằng tiếng Việt, cùng tên file cũ. Tên lớp, namespace, điểm gọi Run và các tuyến reflection giữ nguyên. Các lớp partial vẫn có thể xuất hiện nhiều lần trong cùng file; cách này giữ phạm vi using/alias của từng phần và thứ tự khởi tạo.

| File hiện tại | Nhiệm vụ và các file đã đưa vào |
| --- | --- |
| [PHU_AutoDim_OK-V3.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_AutoDim_OK-V3.cs) | Điều phối Auto DIM, hậu xử lý hole mark, xác định main part và scale theo lần chạy.<br>Gộp thêm: `PHU_HoleMarkPostDimensionService.cs`, `PHU_MainPartResolver.cs`, `ManualDrawingScaleOverride.cs` |
| [PHU_ArrangeView.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_ArrangeView.cs) | Sắp xếp view, trình bày, bố trí section và giữ bố cục đứng.<br>Gộp thêm: `PHU_SectionLayoutPlanner.cs`, `PHU_ArrangePresentation.cs`, `PHU_VerticalShapeViewLayoutContext.cs` |
| [PHU_ColumnGridDimensionEngine.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_ColumnGridDimensionEngine.cs) | DIM cột theo grid: chọn dự án, quản lý tầng DIM và lập kế hoạch. Điểm gọi Slot 07 nằm trong MainForm và PHU_Slot07_ColumnConnections.<br>Gộp thêm: `PHU_ColumnProjectRouter.cs`, `PHU_ColumnDimensionTierContext.cs`, `PHU_GeneralColumnGridPlanner.cs` |
| [PHU_InzaiColumnNeighborDimensionEngine.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_InzaiColumnNeighborDimensionEngine.cs) | DIM cột Inzai: neighbor, splice và các mặt cắt.<br>Gộp thêm: `PHU_InzaiColumnSpliceDimensionEngine.cs`, `PHU_InzaiColumnSectionDimensionEngine.cs` |
| [PHU_Slot07_ColumnConnections.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Slot07_ColumnConnections.cs) | Slot 07: phân tuyến bản vẽ, ngữ cảnh Data Center và liên kết cột.<br>Gộp thêm: `PHU_Slot07_DataCenterContext.cs`, `PHU_Slot07_DrawingRoute.cs`, `PHU_Slot07_ColumnGrid.cs` |
| [PHU_Slot07_DataCenterBeamType2DimensionEngine.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Slot07_DataCenterBeamType2DimensionEngine.cs) | DIM dầm Data Center: ngữ cảnh Type 2, DIM Type 2 và section Type 1.<br>Gộp thêm: `PHU_Slot07_DataCenterBeamType2Context.cs`, `PHU_Slot07_DataCenterBeamType1SectionDimensionEngine.cs` |
| [PHU_Slot06_BraceConnectionAutoDim.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Slot06_BraceConnectionAutoDim.cs) | Slot 06: DIM liên kết giằng Type 1 và Type 2.<br>Gộp thêm: `PHU_Slot06_BraceConnectionAutoDim.Type2.cs` |
| [PHU_NishiAzabuAutoDim_Temp.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_NishiAzabuAutoDim_Temp.cs) | DIM Nishi Azabu và điểm gọi topology thứ hai.<br>Gộp thêm: `PHU_NishiAzabuTopology2AutoDim_Temp.cs` |
| [PHU_Shape.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Shape.cs) | DIM H/I và xử lý cánh lõm có bevel.<br>Gộp thêm: `PHU_Shape_RecessedFlangeBevel.cs` |
| [PHU_Section.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Section.cs) | Tạo section và phân giải thuộc tính Section View / Section Mark.<br>Gộp thêm: `PHU_SectionViewAttributeResolver.cs` |
| [PHU_DimensionRevision.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_DimensionRevision.cs) | Đối chiếu revision DIM, snapshot bản vẽ và sắp xếp theo mẫu.<br>Gộp thêm: `PHU_ArrangeByReference.cs` |
| [PHU_Keyplan.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_Keyplan.cs) | Tạo keyplan và giao diện thiết lập keyplan.<br>Gộp thêm: `PHU_KeyplanForm.cs` |
| [WorkJournal.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/WorkJournal.cs) | Ghi nhật ký công việc và cửa sổ báo cáo.<br>Gộp thêm: `WorkJournalReportForm.cs` |
| [MainForm.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/MainForm.cs) | Giao diện chính, điều phối lệnh, menu kiểm tra, arrange, keyplan, nhật ký và cập nhật.<br>Gộp thêm: `MainForm.CheckMenu.cs`, `MainForm.Operations.cs`, `MainForm.ArrangeReference.cs`, `MainForm.WorkJournal.cs`, `MainForm.Keyplan.cs`, `MainForm.Updater.cs` |
| [Program.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/Program.cs) | Khởi động ứng dụng, nạp thư viện Tekla và các kiểu tương thích cũ.<br>Gộp thêm: `Program.TeklaStartup.cs`, `AkitStub.cs`, `Class1.cs` |
| [Updater/UpdateManager.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/Updater/UpdateManager.cs) | Điều phối cập nhật, hộp thoại tiến độ và tiến trình worker.<br>Gộp thêm: `Updater/UpdateDialog.cs`, `Updater/UpdateWorker.cs` |
| [Updater/UpdateSources.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/Updater/UpdateSources.cs) | Nguồn cập nhật GitHub / Drive và tải gói cập nhật.<br>Gộp thêm: `Updater/GitHubReleaseService.cs`, `Updater/UpdateDownloader.cs` |
| [Updater/UpdateManifest.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/Updater/UpdateManifest.cs) | Manifest, phiên bản và kiểm tra an toàn gói cập nhật.<br>Gộp thêm: `Updater/UpdateVersion.cs`, `Updater/UpdateSecurity.cs` |
| [Updater/UpdateTransaction.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/Updater/UpdateTransaction.cs) | Giao dịch cập nhật, journal khôi phục và khóa tiến trình.<br>Gộp thêm: `Updater/UpdateJournal.cs`, `Updater/UpdateLock.cs` |
| [PHU_DimensionReportForm.cs](../TTSK%20Dim%20Plates/TTSK%20Dim%20Plates/PHU_DimensionReportForm.cs) | Cửa sổ báo cáo DIM: kiểm tra, batch và revision.<br>Gộp thêm: `PHU_DimensionBatchForm.cs`, `PHU_DimensionRevisionForm.cs` |

## Những phần cố ý giữ riêng

- `PHU_Shape_C.cs`, `PHU_Shape_L.cs`, `PHU_Shape_Box.cs`, `PHU_Shape_Unknown.cs`: mỗi file là một bộ DIM lớn, có quy tắc hình học riêng. Không trộn tất cả vào một file khổng lồ.
- Các Slot 01–05, Slot 08, kiểm tra spacing, Open Grid, PDF, dictionary và shortcut: là các chức năng độc lập.
- `MainForm.BatchShortcuts.cs`, `MainForm.DrawingTargets.cs`, `DrawingTargetSelection.cs`, `ShortcutManager.cs`: còn được các test hiện có biên dịch trực tiếp để cô lập ngữ cảnh và điều phối; giữ riêng để tránh kéo toàn bộ WinForms/Tekla vào test.
- `PHU_FitNeighborEnvelope.cs`: phần partial của `PHU_OpenGridView` trong global namespace, giữ nguyên ranh giới với nhóm Arrange.
- `Properties/*.Designer.cs`, `AssemblyInfo.cs` và hai macro Tekla: giữ cách quản lý/biên dịch hiện tại.

## Quy tắc khi sửa tiếp

1. Tìm nhóm chức năng và bổ sung vào file sẵn có; mỗi nhóm helper/context/planner có chú thích nhiệm vụ rõ ràng.
2. Không tạo file nhỏ riêng chỉ vì thêm helper hoặc partial. Tách file khi đó là một chức năng độc lập đủ lớn.
3. Không gộp hoặc đổi thuật toán chỉ vì tên hàm giống nhau: chân DIM, biến đổi tọa độ, hướng/tầng DIM và chính sách xóa/tạo có thể khác nhau.
4. Khi chuyển code, giữ tên kiểu, phạm vi using/alias, thứ tự partial/field initializer và cập nhật Compile trong csproj.
5. Build x64, chạy regression liên quan và cập nhật portable bằng BuildOnly; không tự thay kiến trúc AnyCPU để che cảnh báo.

Kết quả kiểm chứng và phần AnyCPU cần quyết định: [báo cáo kiểm tra](REFACTOR_VALIDATION_2026-10-03.md).
