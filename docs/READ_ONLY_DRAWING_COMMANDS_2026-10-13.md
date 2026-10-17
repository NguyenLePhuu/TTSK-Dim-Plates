# Print / Merge / Check DIM / Scale / Snapshot: không tự lưu bản vẽ

Phạm vi: năm lệnh trên trong checkout `Desktop/A/TTSK_Dim_Plates`. Không đổi luồng Create Drawing, AutoDim, Revision, Arrange hoặc thuật toán phân loại DIM/scale.

## Kết quả rà soát

| Lệnh | Trước sửa | Sau sửa |
| --- | --- | --- |
| Print, Merge PDF từ bản vẽ | `ExecutePdfWorkflow` gọi `CloseActiveDrawing(true)` với bản đang mở trước khi in; khi lỗi thử đóng mặc định. In trực tiếp có thể chuyển active, `finally` đóng không xác minh lại danh tính. | Bỏ đóng/lưu đầu lượt. Mượn đúng active và giữ mở; chỉ đóng không lưu bản ngầm do lệnh tự mở. Worker, direct và native fallback cùng dùng `PHU_DimensionBatch.TeklaAdapter`. |
| Merge File từ PDF có sẵn | Chỉ thao tác file PDF. | Giữ nguyên; không gọi Drawing API để lưu bản vẽ. |
| Check DIM | `Analyze` đọc dữ liệu; adapter chỉ đóng bản tự mở bằng `CloseActiveDrawing(false)`. | Giữ nguyên source và bộ phân loại. |
| Check Scale | `CheckDrawingScale` dùng cùng adapter, đọc Title3 và scale các view. | Giữ nguyên. |
| Snapshot | Đọc DPM có sẵn dưới `drawings/Snapshots`; worker đọc file và xuất PNG, kiểm lại hash nguồn. Không tạo lại snapshot. | Giữ nguyên. Ảnh phản ánh snapshot có sẵn, không đảm bảo chứa chỉnh sửa chưa lưu trong editor. |

`Save` của PDF/PNG hoặc cấu hình worker là lưu file đầu ra, không phải lưu bản vẽ Tekla. Đầu ra tự động của các lệnh này ở Desktop/Temp/LocalAppData. Không chạy hay ghi file vào model để kiểm thử lần này.

## Quy tắc Print/Merge hiện tại

- Đúng active: đọc dữ liệu hiển thị hiện tại, giữ editor và các thay đổi chưa lưu.
- Không có active: mở target ngầm, xác minh đúng model + `IsSameDatabaseObject`, in rồi đóng `false`.
- Active khác target: báo lỗi vào đúng dòng; yêu cầu người dùng tự xử lý/đóng bản đang mở. Không tự lưu, đóng hoặc chuyển bản đó.
- Chốt model từ đầu hàng đợi, truyền qua khóa worker. Kiểm lại trước nhánh dự phòng và sau in. Không tiếp tục sang model mới dù ID/mark trùng nhau.
- Mất kết nối, đổi active/model, lỗi đóng hoặc worker timeout: dừng các lượt tiếp theo của hàng đợi và báo nguyên nhân trên các dòng còn lại. Không thử engine khác để vượt lỗi ngữ cảnh; không chờ PDF 60 giây khi đã bị chặn trước in. Kết quả in thành công trước đó vẫn được giữ, merge/cleanup thành công một phần theo chính sách cũ.
- Native `PrintDrawing` chỉ được gọi bên trong scope đã mở/xác minh target; không để native fallback tự quyết định đóng bản người dùng.
- Nhận đúng giá trị bool từ `LoadDpmFromActiveDrawing()`. Trước đây giá trị false bị coi là đã nạp thành công; nay chỉ chạy renderer khi đã nạp dữ liệu.

Toàn bộ thay đổi sản phẩm ở `PHU_PDF-Merge.cs`; không thêm file sản phẩm và không sửa adapter dùng chung. Các hàm đặt tên, thiết lập màu/khổ in, merge và cleanup giữ nguyên token so với bản sao trước sửa (phần thân renderer chỉ thêm scope, kiểm kết quả nạp và xác minh ngữ cảnh).

## Bằng chứng API và kiểm tra

- [Tekla 2025 CloseActiveDrawing(bool)](https://developer.tekla.com/doc/tekla-structures/2025/close-active-drawing-method-boolean-48559): tham số `save` quyết định lưu khi đóng.
- Đọc IL/thân managed của DLL đang cài, không gọi API live: overload đóng không tham số chuyển thành `false`; `PrintHandler.PrintDrawingOperation` có nhánh `SetActiveDrawing` và đóng mặc định, nên phải quản lý active trước native fallback. `DpmData.LoadDpmFromActiveDrawing` đọc presentation model đang mở; `DPMPrinterCommand` với `printActive:true` cũng dùng đường đọc này, không gọi Save/Close.
- DLL `Tekla.Structures.Drawing.dll`: SHA-256 `8D5FE218CC354CDC2D1D9B87EED2F89C8FB6B9FF2335CE33BDDAAA13A9A1C8F2`.
- DLL `DPMPrinter.dll`: SHA-256 `B84BE57493B502E49C229DF95F40BCE708A8EA3821975565460B927660A412DC`.
- Backup trước sửa, đã so hash: `backup/004-no-drawing-save-20261013`.
- `dotnet run --project tests/NoDrawingSave/NoDrawingSave.csproj --configuration Release -- .`: 31 tình huống offline, 1594 kiểm tra source qua Roslyn. Trích trực tiếp phương thức sản phẩm và adapter hiện tại, biên dịch C# 7.3, dùng Tekla/DPM/process giả lập trong RAM. Gồm active chưa lưu, mở ngầm, cùng mark khác ID, lỗi mở một phần, lỗi render/native/đóng, đổi model/active, worker lỗi/timeout, truyền khóa model và quyền đóng không được dùng lại.
- `scripts/Update-Portable.ps1 -BuildOnly -NoPause`: Release x64 thành công, 40 file runtime được đối chiếu SHA-256. Manifest portable 41 mục được cập nhật hash EXE rồi xác minh đầy đủ.
- Chưa thử Print/Merge hay thao tác bản vẽ thật. Kiểm offline chứng minh các nhánh của ứng dụng; không thay đổi hay vô hiệu hóa thiết lập autosave riêng của Tekla.
