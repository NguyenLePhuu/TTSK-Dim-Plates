# Print/Merge theo kích thước sheet — 17-10-2026

## Lỗi thực tế và phạm vi

`[2-BK-13]`, Assembly DB ID `123195022`, model đang mở `D:\27-011)ATTOKPJ(Data_Center)`: sheet **841 × 594 mm**, renderer màu cũ xuất **914 × 610 mm**. Khổ Auto trong catalog Tekla cộng lề/padding mặc định chọn trang rộng hơn, tạo dải trắng bên phải. Nhánh API dự phòng trước đó cố định Assembly A1/Single A3. Chỉ thay chọn giấy/render và chuẩn hóa PDF trong `PHU_PDF-Merge.cs`; không đổi DIM, layout, selection, naming, merge/cleanup hoặc adapter mở/đóng bản vẽ.

Yêu cầu bổ sung của chủ dự án: không chặn/force-stop chỉ vì khổ tùy chỉnh không có trong catalog. Không dùng nhánh từ chối khổ dài/vuông.

## Thuật toán cuối

1. Sau khi `PdfDrawingReadScope` đã xác minh đúng bản, lấy Width/Height từ `drawing.GetSheet()` (mm giấy).
2. Nhánh màu: `CustomPaperSize` đúng kích thước sheet, Scale = 1, Auto orientation, một trang. Xóa lề/padding **trong bộ cấu hình RAM của lượt in** vì sheet đã có khung/lề. Không sửa DrawingSizes.dat, PaperSizesForDrawings.dat, drawing hay model.
3. Nhánh dự phòng: gọi DPMPrinterCommand của Tekla 2025 trong tiến trình ẩn. A0 chỉ là **trang trung gian**. Tra đúng A0 từ catalog của chính Tekla; bảng A0 của PDFsharp cũ bị làm tròn nên không dùng để dự đoán tọa độ. Chọn scale ≤ 1 để giữ cả mép sheet; bản lớn hơn A0 tự thu nhỏ để chứa hết. Serializer Tekla ghi thiết lập màu/tỷ lệ vào XML tạm ngoài model; không dùng SaveNamed/Save của UserSettings. CLI bản 2025 đang cài không áp dụng đầy đủ một số tham số scale/color nên truyền settingsFile đã kiểm thực tế.
4. Tính ma trận in bằng `DefaultPaperSizeHandler`/`Utility.CalculateMultipleSheets`, rồi áp dụng ma trận nghịch đảo trên PDF vector. Đặt MediaBox/CropBox đúng sheet và bỏ các box cắt phụ cũ. Không dò vùng trắng, không kéo riêng hai trục và không raster hóa. Khổ dài/vuông/đứng không cần có tên trong catalog.
5. Watermark do Tekla vẽ theo trang trung gian được fit đều vào trang cuối. Chỉ nhận nhóm text xám cuối cùng theo cấu trúc DPM 2025, giữ nguyên chữ/font/màu; không sửa nội dung drawing hoặc xóa watermark.
6. Kiểm một trang, hướng và kích thước trong dung sai **0,1 mm** trước khi trả thành công. Lỗi kết nối, dữ liệu hỏng, I/O hoặc native không phản hồi vẫn có thể báo lỗi; khổ giấy tùy chỉnh không còn là lý do dừng.

PDFsharp-Hybrid được tham chiếu bằng alias `DpmPdf`, Private=false, lấy từ Tekla đã cài. PDFsharp 6 dùng để merge vẫn giữ nguyên. Settings/raw PDF/log native ở `%TEMP%/TTSK/PdfNative/<GUID>`; chỉ dọn thư mục lượt này sau khi child kết thúc. Không ghi công cụ/cache/config vào model.

## Kiểm chứng

- Release x64, C# 7.3/.NET Framework 4.8; cập nhật portable qua `Update-Portable.ps1 -BuildOnly -NoPause`, đối chiếu SHA-256.
- `tests/PdfSheetPaper/Run.ps1`: **390 assertions**, dùng bộ tính giấy thật của DPM và helper sản phẩm; không kết nối Tekla. **16 kích thước** gồm A0/A1/A2/A3/A4/A5, ngang/đứng, vuông, số lẻ, dài 2.400 mm, sheet nhỏ; lề/padding bất đối xứng; giữ một trang, tỷ lệ, nguồn tọa độ; merge khác khổ; loại dữ liệu NaN/Infinity và PDF sai số trang.
- `Verify-Geometry.py` đọc PDF fixture bằng pdfplumber độc lập: đủ bốn dấu màu ở góc, cùng kích thước/tọa độ; sai số vị trí tối đa **0,00043 mm** sau chuẩn hóa native.
- Live trên đúng bản `[2-BK-13]` người dùng chọn: direct, tiến trình `--print-color-worker`, `--print-list-worker`, và ép riêng nhánh native đều in thành công. Kết quả đều **841 × 594 mm**, **817 ký tự, 2.067 line, 269 curve**. Đối chiếu multiset chữ; toàn bộ tọa độ line/màu khớp, sai số native tối đa **0,00017 mm**. Watermark được xem lại bằng ảnh render.
- Ghép ba PDF live bằng engine sản phẩm: đủ ba trang, giữ đúng kích thước và nội dung từng trang. Kiểm offline merge A1 + A3 đứng + khổ dài giữ đúng thứ tự/kích thước. Merge File bên ngoài không thay đổi.
- Render Poppler và xem ảnh trước/sau; dải giấy dư bên phải đã hết. Một số cảnh báo font thay thế của Poppler có ở cả trước/sau; không thay cài đặt EmbedFonts của luồng màu.
- Trước/sau live: không có active drawing; selection vẫn `[2-BK-13]`. Scope chỉ mở ngầm rồi đóng không lưu bản do lượt thử mở. Không Save/Modify/CommitChanges, không DIM, không đổi sheet/layout.

Bằng chứng làm việc: `.codex-artifacts/pdf-paper/comparison.json`, các PDF/PNG cùng thư mục; `.codex-artifacts/pdf-sheet-regression`. PDF mẫu bàn giao: `output/pdf/2-BK-13.pdf`. Chỉ kiểm live một Assembly; các khổ/loại khác được kiểm thuật toán offline, không tuyên bố đã in live Single/GA/CastUnit.

Tài liệu PDF_ACCUMULATION/READ_ONLY_DRAWING_COMMANDS trước ngày này mô tả mốc lịch sử khổ giấy cũ. Chính sách selection, read-scope, queue, màu và merge/cleanup của chúng vẫn được giữ; phần chọn giấy dùng thuật toán trên.
