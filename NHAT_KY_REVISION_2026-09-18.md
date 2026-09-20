# Nhật ký tối ưu CHECK REVISION — 18/09/2026

Ghi và cập nhật lúc 12:40 (UTC+07:00), theo yêu cầu của người dùng.

Workspace: `C:\Users\ADMIN\OneDrive\Desktop\A\TTSK_Dim_Plates`.

## Trạng thái bàn giao

**Phần code và kiểm thử offline đã hoàn tất trong phạm vi an toàn.** Phần ghi màu riêng từng đoạn đã được chuyển sang Dispatcher luồng chính Tekla, build và test lại thành công. Audit Revision đọc-only trên bản vẽ BmC-117 cũng đã chạy ổn: nhận đúng `688.2 → 690.2` trong chuỗi 9 đoạn, nhận riêng `2553 → 2555`, và giữ 14 đoạn không đổi. Ghi màu native trực tiếp vẫn chưa chạy Apply trên bản vẽ thật sau sự cố probe trước đó.

EXE Release đã được Rebuild sau thay đổi cuối. Portable không được cập nhật trong phiên này.

## Yêu cầu và các đính chính của người dùng

1. Dim 690.2 khác snapshot; khi tô đỏ phải giữ nguyên trong cụm dãy dim lỗ, không tách thành dim/cụm mới.
2. REV phải nhận ra thay đổi sau Add dimension point.
3. REV phải nhận ra thay đổi khi thêm lỗ, thêm/xóa chân dim hoặc xóa cả cụm và dim lại; không phụ thuộc cứng vào ID lịch sử.
4. Người dùng xác nhận có thể chọn riêng dim 690.2 và đổi màu thủ công mà vẫn giữ cụm. Nhận định ban đầu của trợ lý rằng cần tô cả chuỗi hoặc thêm dấu REV đã bị người dùng bác bỏ; **không có chấp thuận cho hai phương án đó**.
5. Arrange DIM chỉ là tham chiếu về xử lý dim riêng lẻ và **giữ nguyên mark**. Không được hiểu là yêu cầu đổi màu mark hoặc sửa thuật toán Arrange DIM.

Ảnh minh họa do người dùng gửi: `C:\Users\ADMIN\AppData\Local\Temp\codex-clipboard-1f2bac32-592a-4d41-b28a-f3007ca7229a.png`.

## Sao lưu và trạng thái ban đầu

Đã sao lưu sáu file trước khi sửa, kiểm tra SHA-256 bản sao bằng bản gốc:

`TTSK Dim Plates\TTSK Dim Plates\backup\01_RevisionTopology_20260918\`

Trong thư mục có `SHA256-before.csv` và `git-status-before.txt`.

Workspace đã có nhiều thay đổi chưa commit từ trước, gồm MainForm, Check DIM, Batch, REV và portable. Không quy các thay đổi đó cho phiên này. Các file REV hiện vẫn là file untracked trong Git; cần so với backup để thấy diff riêng của phiên.

## Phân tích nguyên nhân

- Bộ ghi màu cũ chủ động tạo các nhóm dim liên tiếp theo màu, kiểm tra, rồi xóa set gốc. Đây là nguyên nhân cụm 690.2 bị tách.
- So khớp cũ ưu tiên ID. Khi số đoạn bằng nhau, ghép theo thứ tự; khi số đoạn khác nhau, thử ID hoặc khoảng đo. Add/remove point có thể thay toàn bộ ID hoặc tái sử dụng ID trên một khoảng mới.
- Quá trình duyệt cụm cũ lấy snapshot theo kiểu lấy trước dùng trước, dễ phụ thuộc thứ tự khi có các cụm gần giống nhau.
- Arrange DIM hiện chỉnh từng child để giữ mark; không gọi parent set.Modify. Đã đọc mã tham khảo, không sửa file Arrange DIM.

## Những thay đổi đã thực hiện

### PHU_RevisionPresentation.cs

- Chọn cụm dựa trên hướng đo, khoảng đo và chân dóng; ID chỉ phân biệt giữa các ứng viên hình học phù hợp.
- Khi hai cụm dùng cùng chân đo, dùng khoảng cách đường DIM để chọn ứng viên gần nhất; chỉ giữ review nếu vị trí cũng trùng/không đủ phân biệt.
- Thêm `MatchChains` để xét các cụm cùng lượt. Nhiều cụm dựng lại tranh cùng snapshot sẽ được đánh dấu cần kiểm tra, không tùy tiện tiêu thụ snapshot theo thứ tự ID.
- `MatchChainSegments` giữ các khoảng không đổi làm mốc, chia vùng chỉnh sửa cục bộ.
- Khoảng bị chia hoặc gộp không mượn ID cũ làm bằng chứng không đổi.
- Ghép tuần tự trong vùng chỉnh sửa để giữ các đoạn phía sau khi vừa thêm chân vừa đổi kích thước.
- Có xét trường hợp vừa thêm vừa xóa khiến tổng số đoạn không đổi; xử lý đầu vào đảo thứ tự.
- Tiền tính thông tin chia/gộp để tránh lặp lại phép đếm trong mỗi ô ghép.

### PHU_DimensionRevision.cs

- Áp dụng ghép cụm theo lượt.
- Chỉ đưa toàn bộ dữ liệu một set vào so khớp khi đọc đủ các đoạn; tránh cụm thiếu do một child đọc lỗi.
- Thêm chữ ký toàn bộ set vào kết quả để phát hiện thêm/xóa chân hoặc đổi vị trí sau khi kiểm tra, trước khi ghi màu.
- Bỏ việc tự chuyển các đoạn không đổi đang đỏ thành đen; giữ màu người dùng.

### PHU_RevisionColors.cs

- Thay bộ xóa/tạo lại set bằng bộ ghi thuộc tính riêng `proFontColor` qua lớp native `DrawingPropertiesObjectFactory`, được tìm thấy trong đường xử lý bảng Properties của Tekla.
- Đây là tích hợp nội bộ Tekla, **không phải API công khai đã được xác nhận ổn định**.
- Tạo yêu cầu ở LocalAppData, không trong model. Kiểm tra model, GUID bản vẽ, hash snapshot, ID/số đoạn, chân đo 3D, hướng, vị trí dòng dim, số đo và màu trước khi ghi.
- Kiểm tra lại cả các đoạn cùng cụm và thuộc tính/mark sau ghi; cố gắng khôi phục màu nếu lỗi.
- Có thời hạn, hủy yêu cầu, kiểm soát yêu cầu đang chạy và bản sao assembly theo hash nhằm tránh dùng lại mã cũ đã nạp trong Tekla.
- Chuyển toàn bộ thao tác native qua WPF Dispatcher của Tekla; kiểm tra tên tiến trình và OS thread ID đối chiếu main window trước khi gọi native.
- Bỏ `Task.Run` quanh `Operation.RunMacro`. Lệnh Tekla API này chạy trên context hiện tại; chỉ phần thao tác native trong macro được marshal sang Dispatcher.
- Không có thao tác chọn UI, parent Modify, Delete hoặc CreateDimensionSet trong đường ghi màu mới.
- Không tự nối lại các set đã bị phiên bản cũ tách.

### PHU_DimensionRevisionForm.cs

- Thêm điều kiện `!item.Review` trước khi tự ghi màu.

### tests/Slot09DimensionCheck/RevisionFeatureTests.cs

- Thêm ca thêm chân, xóa chân, chia/gộp, tái sử dụng ID, dựng lại cụm, di chuyển dòng dim, đảo thứ tự, cạnh tranh snapshot.
- Thêm kiểm thử adapter native bằng đối tượng giả, lỗi SetProperty/UpdateObject, yêu cầu hủy/hết hạn và biên dịch macro sinh ra.
- Thêm ca chặn gọi native ngoài tiến trình Tekla; ca này đã chạy đạt.

### tests/Slot09DimensionCheck/REVISION.md

- Bổ sung mô tả hướng sửa, bộ phân giải theo vị trí đường DIM và giới hạn xác minh.
- Phân biệt kết quả live lịch sử của bộ tách chuỗi cũ với audit đọc-only của bộ mới.

## Kiểm thử đã chạy

Test project build Release: 0 lỗi, 0 cảnh báo.

Lần chạy offline gần nhất sau bản sửa Dispatcher và bỏ `Task.Run`:

- 90 ca classifier đạt.
- 506 assertions hardening đạt.
- 60 assertions REV đạt.
- Kiểm tra lựa chọn/session REV đạt.
- 60 assertions Batch đạt.

Log: `.codex-artifacts\RevisionTopology\tests.txt`.

Preview UI REV sau bản sửa cũng đạt ở cả hai theme và hai kích thước cửa sổ. Log mới nằm trong `.codex-artifacts/RevisionTopology/offline-preview-after-direct-runmacro.txt`.

Đã build lại ứng dụng Release x64 thành công bằng:

```powershell
dotnet msbuild "TTSK Dim Plates.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /nologo /v:minimal
```

Thư mục chạy: `TTSK Dim Plates\TTSK Dim Plates`. Build có cảnh báo ở các file ngoài phạm vi sửa như OpenGridView, Shape, MainForm, ShortcutSettingsForm.

Đã rà diff với backup mới `02_RevisionDispatcher_20260918`: backup SHA-256 còn nguyên, không thấy lỗi whitespace; Git báo chuyển CRLF/LF ở một số file.

`.codex-artifacts\RevisionTopology\SHA256-after.csv` là ảnh chụp trung gian, **được ghi trước bản sửa Dispatcher cuối**, không phải hash cuối cùng. Manifest cuối của các file REV và nhật ký là `.codex-artifacts/RevisionTopology/SHA256-final.csv`.

## Sự cố Tekla — phải giữ lại thông tin này

Trợ lý chạy hai lần macro thăm dò **chỉ đọc** để khảo sát thuộc tính riêng. Không gọi SetProperty/UpdateObject trong các probe này. Cả hai không trả kết quả và Tekla crash.

Windows ghi hai sự kiện:

- 12:07:34, PID 32832, exception `0x80000003`.
- 12:16:31, PID 33632, exception `0x80000003`.

Log model xác nhận cả hai thời điểm có assertion:

`REASON: (!"Not in main thread"), cmn_threadwatch.cpp, Line: 30`

Callstack có `common::DebugBreakIfNotInMainThread` và `teklastructures_application!SetEnvironmentVariable`.

Đường dẫn log:

- `C:\Users\ADMIN\OneDrive\Desktop\Phu Hoc\27-006)GS_Okegawa\logs\error_ADMIN_20260918_115342.log`
- `C:\Users\ADMIN\OneDrive\Desktop\Phu Hoc\27-006)GS_Okegawa\logs\error_ADMIN_20260918_120917.log`

Crash dumps:

- `C:\Users\ADMIN\AppData\Local\CrashDumps\TeklaStructures.exe.32832.dmp`
- `C:\Users\ADMIN\AppData\Local\CrashDumps\TeklaStructures.exe.33632.dmp`

Kết luận kỹ thuật: gọi native Properties trực tiếp từ luồng macro là không an toàn. Cần marshal sang luồng chính trước **mọi tương tác native**; catch C# không ngăn được assertion dẫn đến crash.

Trợ lý đã báo sự cố và xin lỗi người dùng. Đã dừng probe; chỉ kết thúc tiến trình helper Probe.exe do trợ lý tạo, không dùng lệnh kill Tekla. Chưa chạy thử ghi màu live. Không khẳng định có hay không mất các sửa chưa lưu của người dùng.

Sau khi bổ sung kiểm tra luồng chính và bỏ `Task.Run` quanh `Operation.RunMacro`, audit đọc-only cuối đã chạy được trên Tekla đang mở. Kết quả là 17 DIM được đối chiếu, 14 không đổi, 3 thay đổi hợp lệ (`690.2`, và `2555` ở hai hình chiếu). Console còn in cảnh báo `libprotobuf` về UTF-8 trong tên DPM; cảnh báo này không làm audit thất bại. Không chạy `--revision-apply`, không đổi màu và không tạo/xóa đối tượng trong audit cuối.

## Việc còn lại khi tiếp tục

1. Khi người dùng sẵn sàng kiểm thử trên bản sao an toàn của bản vẽ, chạy một lần Apply để xác minh riêng màu `690.2`, parent/child IDs, số đoạn, mark/vị trí/chân dim và khả năng khôi phục. Đây là bước duy nhất còn chưa kiểm chứng live.
2. Xác minh Add dimension point, remove point, xóa cụm/dim lại bằng dữ liệu thật. Audit hình học hiện tại đã bao phủ trường hợp Add point/đổi giá trị; dữ liệu thao tác thật vẫn cần người dùng xác nhận.
3. Chưa đưa bản mới vào portable hoặc coi là dùng ổn định trước khi kiểm chứng đường ghi màu.
4. Không sửa các thuật toán Arrange DIM, geometry, PDF hoặc module ngoài phạm vi.

Không commit/push, không sửa memory và không cập nhật portable trong lần ghi nhật ký này.
