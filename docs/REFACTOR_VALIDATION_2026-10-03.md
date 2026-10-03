# Kiểm chứng gộp mã nguồn — 03/10/2026

## Kết quả

- 95 mục C# biên dịch trong project còn 53: gộp 62 file thành 20 file chức năng, bỏ 42 file con sau khi chuyển đủ nội dung.
- File nhận có mục lục và `#region` mô tả nhiệm vụ bằng tiếng Việt. Xem [sơ đồ nguồn](SOURCE_ORGANIZATION.md).
- Debug x64 và Release x64: **0 lỗi, 0 cảnh báo**, kiểm tra bằng Rebuild với `/warnaserror`.
- Portable đã cập nhật bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`; 39 file runtime khớp SHA-256 với staging. Bốn file cấu hình `.cfg` trong portable không thay đổi.
- Không commit, push hoặc phát hành bản cập nhật.

## Mốc đối chiếu và bảo toàn code

Thư mục làm việc đã có nhiều sửa đổi và xóa file trước yêu cầu này. Mốc so sánh là **source đang có trên máy trước khi gộp**, không phải HEAD của Git. Bản chụp nằm tại `.codex-artifacts/consolidation-20261003/source-before/` (không đưa vào Git).

Việc gộp được kiểm chứng riêng trước khi xử lý warning:

1. Roslyn phân tích C# 7.3 và đối chiếu multiset token của toàn bộ khai báo cấp namespace/global: **183 trước, 183 sau, không khác biệt**. Bao gồm thân hàm, field initializer, hằng số, điều kiện và các phần partial.
2. So sánh assembly Release x64 bằng Mono.Cecil, chuẩn hóa metadata token thành tên thành viên và nhánh IL thành chỉ số lệnh: **15.462 mục trước/sau, không khác biệt**. So sánh thân hàm, local variables, exception handlers, trường, kiểu, property/event, resource và assembly reference.
3. Giữ thứ tự partial theo Compile của project cũ; các phần của `PHU_DimensionReportForm` và `PHU_Slot07_ColumnConnections` được gom cùng nhóm để tránh đổi thứ tự khởi tạo và lambda.
4. Mỗi phần giữ using/alias trong namespace riêng. Macro và tài nguyên do Visual Studio sinh vẫn giữ cách quản lý cũ.
5. Project cuối không có Compile trùng hoặc trỏ tới file đã xóa. `git diff --check` đạt.

Sau bước sửa warning, đối chiếu 8.623 thành viên nguồn với 8.613 thành viên còn lại chỉ ghi nhận **26 thành viên sửa/xóa** thuộc đúng phạm vi bên dưới; không có thay đổi ngoài danh sách. Assembly cuối và portable có **15.450 mục khớp hoàn toàn**. Không dùng kết quả IL của bước gộp để khẳng định IL sau sửa warning giống hệt ban đầu.

## 30 cảnh báo C# đã xử lý

| Loại | Số | Cách xử lý |
| --- | ---: | --- |
| CS0168 | 8 | Bỏ tên biến exception không dùng trong Open Grid; giữ nguyên kiểu exception, thân catch và xử lý lỗi. IL của Open Grid không đổi. |
| CS0162 | 10 | Bảy cờ private trong Shape H/C/Box/L đổi từ `const bool` sang `static readonly bool`; giữ nguyên từng giá trị true/false và toàn bộ nhánh DIM/debug dự phòng. Không bật tính năng đang tắt và không xóa thuật toán. |
| CS0649 | 7 | Xóa lớp private `ViewScaleEntry` không được dùng (5 trường); ghi rõ mặc định null cho hai icon tùy chọn. |
| CS0169 | 2 | Bỏ hai trường Panel không có nơi sử dụng trong MainForm. |
| CS0414 | 3 | Bỏ `arrangeMainHorizontal` không được đọc; bỏ trường `SelectedMainPartForBoltFilter` và phép reset null không được đọc trong C/Box. Trường cùng tên của Shape L vẫn được dùng nên giữ nguyên. |

Không thêm `NoWarn`, không giảm WarningLevel, không nuốt thêm exception. Tên lớp/điểm gọi nghiệp vụ được giữ; chỉ lớp private không dùng `ViewScaleEntry` được bỏ trong bước dọn warning. Không thay chân DIM, công thức hình học, dung sai, scale, thứ tự tạo/xóa DIM hoặc route chọn thuật toán.

## Kiểm thử

| Bộ kiểm tra | Kết quả |
| --- | --- |
| BoxRouting | 7.337 kiểm tra đạt ở bản trước, bản cuối và portable mới. |
| RecessedFlange | 2.515 trường hợp và fixture hình học đã lưu đạt trước/sau. |
| Slot02SideMarks | 127 trường hợp hình học đạt trước/sau. |
| BatchShortcuts | Điều phối batch, lỗi/rỗng/busy/dispose, 36 ngữ cảnh chọn target và migration phím tắt đạt trước/sau. |
| Updater | 32/32 test đạt: phiên bản/manifest, checksum và đường dẫn, khóa, transaction/rollback, bảo toàn cấu hình, worker/restart, UI và publisher/provider giả lập. Không phát hành lên dịch vụ thật. |
| Debug x64 / Release x64 | Rebuild thành công khi coi warning là error. |
| Debug AnyCPU / Release AnyCPU | Build thành công, mỗi cấu hình còn 2 MSB3270; xem quyết định bên dưới. |

`tests/Updater` đã bị xóa khỏi working tree trước khi bắt đầu. Lệnh test gốc không chạy được vì thiếu suite. Để kiểm tra mà không khôi phục đè các xóa có sẵn, suite được lấy từ HEAD vào `.codex-artifacts/consolidation-20261003/historical-tests/`. Runner tạm chỉ sửa đường dẫn fixture; test worker trích nguyên region Program từ file đã gộp để tiếp tục dùng stub khởi động không Tekla như test cũ. Không sửa assertion. Sau khi thích ứng đường dẫn và region, toàn bộ 32 test đạt. Lệnh `scripts/Test-Updater.ps1` thông thường vẫn cần thư mục test bị xóa đó nếu muốn chạy lại ngoài harness tạm.

Không chạy lệnh DIM hoặc thao tác ghi lên bản vẽ Tekla đang mở. Các kết quả trên là build, so sánh source/IL, regression và integration Updater; chưa thay thế kiểm chứng trực tiếp các loại bản vẽ thực tế.

## Phần cần chủ dự án quyết định: AnyCPU

Hai cấu hình AnyCPU vẫn dùng PlatformTarget=AnyCPU, trong khi `DPMPrinter` và `Tekla.Structures.Geometry3d.Compatibility` là AMD64. Đây là hai cảnh báo MSB3270 về kiến trúc; thay cấu hình có thể thay cách chạy ứng dụng nên chưa tự sửa.

Khuyến nghị tiếp tục dùng **Release x64**, đúng cấu hình portable và Tekla đang dùng. Nếu muốn xử lý cả AnyCPU, chủ dự án có thể chọn chuyển cấu hình đó sang x64 hoặc bỏ cấu hình AnyCPU. Không đề xuất tắt cảnh báo kiến trúc.

## Bằng chứng trên máy

Thư mục `.codex-artifacts/consolidation-20261003/` chứa snapshot nguồn, kế hoạch gộp, công cụ đối chiếu và log build/test:

- `build-before.log`, `build-merged.log`, `build-final.log`, `matrix-*.log`.
- `il-merge.log`, `source-members.log`, `source-member-differences.json`, `il-portable.log`.
- `test-before-*.log`, `test-final-*.log`, `test-portable-BoxRouting.log`, `test-updater-isolated.log`.
- `portable-update.log`, `portable-settings-before.json`, `git-status-before.txt`.

SHA-256 portable EXE tại thời điểm hoàn tất:

```text
4894498F384225C764166E98ED728E7BD15F20FE3E4701D4D2E5FC26D8222BBD
```
