# In theo danh sách — luồng riêng, bản sửa lần 2

> Cập nhật 15-10: giao diện modal/queue riêng dưới đây là lịch sử. UI hiện tại là panel trượt đồng bộ MainForm; xem [PDF_DRAWER_2026-10-15.md](PDF_DRAWER_2026-10-15.md). Engine in giữ nguyên; kiểm live dưới đây không thay thế kiểm UI mới.

## Cách dùng

1. **Chuột phải Print** mở thẳng **In theo danh sách**, dùng ở cả Active và Batch.
2. Chọn trong Document Manager → **+ Thêm bản vẽ**; lặp lại với selection mới. A rồi chỉ B rồi chỉ C vẫn giữ A + B + C.
3. **Print PDF / Merge PDF** chạy toàn bộ queue theo thứ tự nạp. **Bỏ dòng** loại dòng chọn; **Xóa hết** chỉ xóa queue.

Queue ban đầu rỗng, không nhập bảng cũ. Đóng/mở lại cửa sổ giữ queue trong phiên TTSK; thoát/chuyển tiến trình dự án không mang queue sang phiên mới. Thêm/bỏ dòng hoặc mở lại cửa sổ đặt trạng thái về “Chờ in”.

## Ranh giới bắt buộc: thuật toán gốc không đổi ký tự

- `PHU_PDF-Merge.cs`: giữ nguyên toàn bộ nội dung trước lượt sửa, chỉ chèn region **PDF_LIST_ONLY**. Bỏ region này phải khớp **từng ký tự** với `.codex-artifacts/pdf-accumulation-v2/PHU_PDF-Merge.before.cs`.
- `MainForm.RunPdfCommandAsync` giữ nguyên từng ký tự. Nút/shortcut cũ vẫn `RunPdfCommand` → `RunPdfCommandAsync` → `PrintPdfsAsync` → `ExecutePdfWorkflow` → `--print-color-worker`. Không sửa nguồn, renderer, fallback, thời gian chờ, màu/khổ giấy, tên file, merge/cleanup của luồng gốc.
- Luồng mới: `PdfAccumulationForm.PrintAsync` → **RunAccumulatedPdfCommandAsync** → **PrintAccumulatedPdfsAsync** → **ExecuteListPdfWorkflow** → **PrintListDrawingWithTeklaApiAsync** → **--print-list-worker** → **ExecuteListColorPrintWorker**. Chỉ luồng này dùng **ResolveListDrawingByKey** tra ID trực tiếp.
- `Program.cs` chỉ thêm nhận diện/dispatch worker mới. Cố ý có phần điều phối riêng, không refactor thuật toán cũ.
- `PdfPrintEntryButton` kế thừa `SafeRoundedButton`, chặn Click thường khi nhấn chuột phải trước MouseUp. Chuột trái/keyboard/PerformClick tiếp tục gọi handler Print cũ. Không sửa lớp nút dùng chung hoặc handler in cũ.

## Nguyên nhân và tối ưu chỉ cho luồng mới

Worker cũ chỉ tìm trong selection DM. Bản đã gom nhưng bỏ chọn không tìm thấy, dẫn đến fallback DPM trong tiến trình UI, thêm lượt thử và nguy cơ đổi DPI. Worker mới tra khóa DB độc lập selection; DPM/native fallback đều nằm trong worker mới. Worker lỗi không chạy renderer trong UI, không chờ thêm 60 giây cho PDF không được tạo.

Phương án quét mọi drawing kèm metadata đã bị loại sau live-read: một bản cuối danh mục mất **74.889 ms**. Factory `Tekla.Structures.DrawingInternal.Operation.GetDrawing(Identifier)` của Tekla 2025 tạo đối tượng theo DB ID; `Select()` đọc lại rồi kiểm ID/ID2/GUID/type/mark, không mở editor. Nếu factory không có, fallback quét identifier với `SelectInstances=false`, chỉ Select đúng ID. Khóa legacy thiếu ID vẫn xét selection và yêu cầu duy nhất. Không gán thay GUID thực tế bằng GUID kỳ vọng.

Preflight queue dùng `Drawing.Select()` đúng từng bản, không quét model; không đọc cột Changes không dùng ở cửa sổ mới. Tiến độ cập nhật đúng dòng. UI theo màu/nút/typography Check DIM, chỉ giữ tên, Rev, trạng thái. Khóa sửa/đóng queue khi bận, vẫn cuộn được; khai báo row heading rõ ràng và dựng layout trước autoscale để đúng net48/DPI 125%.

Model/connection được kiểm trước/sau đọc. Nạp lỗi giữ queue. Bản trùng ID bỏ qua, cùng MARK khác ID giữ riêng. Bản bị xóa/tạo lại hoặc khóa không khớp bị chặn; không thay bằng bản cùng tên. Queue không tự chạy/xóa sau in.

## Kiểm tra ngày 14-10-2026

### Offline

`pwsh -NoProfile -File tests/PdfAccumulation/Run.ps1` trích code sản phẩm, biên dịch C# 7.3 với API/process giả lập; chạy lại form trên **.NET Framework 4.8 x64, DPI hệ thống 125%**. Artifact ở `.codex-artifacts`.

- Queue: nạp rời, trùng, lỗi giữa chừng, đổi model, xóa/tạo lại, snapshot độc lập, nguồn in cũ.
- UI: nạp/bỏ/xóa/mở lại, in toàn bộ, khóa khi bận, light/dark/thu nhỏ/empty, owner không đổi kích thước; chuột phải không phát Print cũ, chuột trái/PerformClick giữ nguyên.
- Worker: ID/ID2/GUID/type/mark; không selection vẫn đúng; timeout/model/active; renderer chỉ worker.
- So hai workflow bằng fixture: thứ tự, tên/nội dung output, tiến độ, partial failure, lỗi merge, context-stop, cleanup đúng lượt và giữ file có sẵn. Fixture text không thay thế PDF thật.
- Audit **từng ký tự** toàn bộ source PDF gốc ngoài region mới và điều phối gốc; thêm so token 9 nhóm hàm chọn nguồn cũ.

### Live chỉ đọc

`tests/PdfAccumulation/Run-ReadOnly.ps1 -AssemblyPath <exe> -TeklaProcessId <pid>` chọn phiên theo PID/start, không thay selection hoặc mở/đóng/lưu/in. `-Baseline` chỉ đo resolver gốc của binary cũ, không đưa thuật toán ID mới vào đó.

| Nguồn / bản | 5 lần tìm đúng định danh (ms) |
| --- | --- |
| Gốc, `[2-SB14-15]` đang chọn | 71, 66, 68, 67, 70 |
| Mới, cùng bản đang chọn | 19, 16, 17, 16, 16 |
| Mới, `[A2-d-29]` không chọn | 26, 26, 27, 27, 26 |

Active/model/selection không đổi sau probe. Bằng chứng: `.codex-artifacts/pdf-accumulation-live/*-result.txt`.

### Live Print/Merge và so PDF

- Kiểm UI phát hiện thao tác chuột phải ban đầu đã kích hoạt Print gốc, tạo bốn PDF. Đã báo người dùng, thêm `PdfPrintEntryButton`, kiểm lại chuột phải: mở thẳng list, bảng chính rỗng, không in. Bốn PDF giữ làm đối chiếu.
- Nạp `2-SB14-15`, `16`, `17`, `18`, Rev 2; đổi DM sang **chỉ chọn `2-SB14-18`**. Print mới xuất đủ 4/4; Merge mới tạo đúng 4 trang/thứ tự, dọn đúng 4 PDF con của lượt Merge. File có sẵn/lượt Print trước giữ nguyên. Sau kiểm đã trả selection về bốn bản ban đầu.
- Tổng `pipeline_ms` bốn bản: **gốc 19.711 ms**, **Print mới 18.819 ms**, **pha in Merge mới 18.845 ms**. Đây là tổng pipeline mẫu bốn bản, không gồm thao tác UI, không cam kết tốc độ mọi bản vẽ.
- Các worker mới đều thành công. Cửa sổ chính **1220×840** trước/sau Print/Merge; list **1100×700** ở 125% DPI.
- `Compare-LivePdfs.py`/Poppler **150 DPI**: 4 trang Print mới và 4 trang Merge mới **khớp từng pixel**, cùng MediaBox, tập dòng chữ và thứ tự trang so với PDF gốc. Thứ tự trích xuất text có thể khác do object order; tập dòng/ảnh không khác. Không dùng hash PDF để suy khác nội dung.
- Đã xem ảnh render. Báo cáo: `.codex-artifacts/pdf-accumulation-v2/merge-comparison/comparison.json`. Đầu ra Desktop/TTSK_PDF/14-10-26; test/log/baseline ngoài model. Không gọi DIM/Save/Modify/CommitChanges; scope chỉ đóng không lưu drawing hidden do nó mở.

Giới hạn: live kiểm bốn Assembly trên model hiện tại, không suy mọi loại bản/máy. Build Release x64; cập nhật portable bằng `Update-Portable.ps1 -BuildOnly -NoPause`, SHA-256 và CodeMap Check trước bàn giao. Không commit/push/phát hành.
