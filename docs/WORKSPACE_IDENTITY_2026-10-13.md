# Ngăn refresh khi phiên Tekla không đổi

## Nguyên nhân đã tái hiện bằng code

`PollTeklaWorkspaceAsync` lấy phiên hiện tại từ danh sách **Ready**, không phải toàn bộ process đang tồn tại. `IsReady` có thể trả false khi Tekla bận/không phản hồi, kênh API chưa đọc được hoặc title tạm chưa có đường dẫn model. Code cũ lập tức đặt `workspaceRefreshRequired = true`.

Khi cùng PID/start/session/model trở lại Ready, nhánh giữ phiên cũ yêu cầu `!workspaceRefreshRequired && SameWorkspace(...)`. Cờ đã bật làm nhánh này không chạy. Sau hai lượt scan, bộ điều phối gọi `SwitchTeklaSession`, tạo TTSK mới rồi đóng cửa sổ cũ khi handshake thành công. Danh sách và kết quả chỉ giữ trong RAM mất theo cửa sổ cũ; đây là chuyển process thật, không phải lỗi repaint.

Một nhánh khác coi việc bound process vắng khỏi một snapshot là đã thoát, rồi có thể chọn dự án duy nhất còn lại. Discovery bỏ qua process khi không đọc được metadata/quyền/window, nên snapshot thiếu chưa đủ chứng minh process đã đóng.

## Sửa tại nơi quyết định chuyển phiên

- `workspaceRefreshRequired` chỉ chặn lệnh khi cần xác minh ngữ cảnh. Nó không còn là lý do tự mở lại cùng dự án.
- `KeepCurrentWorkspaceAsync` giữ phiên khi PID, thời điểm khởi động, SESSIONNAME và model path đều khớp. Sau một khoảng mất readiness, xác minh cả Model/Drawing API trên STA dispatcher rồi bỏ trạng thái chờ. Thất bại đọc tiếp tục giữ UI/list và chờ lại, không tạo child.
- Không gọi API thêm ở các lượt scan khỏe mạnh; không tăng timer để che lỗi. Chu kỳ scan 500 ms và thời gian ổn định candidate 500 ms giữ nguyên.
- Phân biệt `workspaceResetRequired`: chỉ bật cho đóng model được xác nhận bằng cửa sổ Tekla còn phản hồi, title hết model, `GetInfo` trả thông tin model rỗng và Model API không còn kết nối. Empty title, process bận, exception hoặc thông tin null là chưa biết, không phải bằng chứng đóng. Đóng thật rồi mở lại cùng path vẫn được tạo phiên sạch.
- `HasProcessExited` kiểm PID/start/process name thật trước fallback sang phiên duy nhất khác. Lỗi đọc/quyền không được coi là đã thoát.
- Cả chọn dự án thủ công và tự động đều kiểm cùng identity trước handoff. Chọn lại phiên khỏe mạnh không refresh. Nếu API cũ hỏng không phục hồi, người dùng vẫn có thể bấm tên dự án và chọn lại để chủ động reconnect; timer không tự làm việc này.
- Khi model/kênh/PID/start thực sự đổi: giữ candidate ổn định, chờ hết lệnh/dialog, tạo child, xác minh cả hai API và ready/accept như trước. Không mang Drawing object hoặc cache của model cũ sang model mới.
- Kiểm lại busy/pending sau await, không xóa yêu cầu foreground mới tới giữa lúc quét/xác minh.
- Một lần readiness hụt không ghi đè status kết quả bằng “đang chờ” rồi “sẵn sàng”. Người dùng bấm lệnh trong lúc chưa xác minh vẫn nhận thông báo và lệnh bị chặn.

## Phạm vi và bằng chứng

- Backup đã so SHA-256: `backup/005-workspace-refresh-20261013`.
- Chỉ hai file C# sản phẩm thay đổi: `MainForm.cs` (điều phối), `Program.cs` (hai hàm xác minh chỉ đọc). Baseline hash mọi source có sẵn ở `source-hashes.csv` trong backup. Source DIM, Check DIM/Scale, PDF/Merge, Snapshot không đổi byte.
- Test: `dotnet run --project tests/WorkspaceIdentity/WorkspaceIdentity.csproj --configuration Release -- .`.
- Harness trích các phương thức thật của sản phẩm, biên dịch C# 7.3; process, model API và UI là dữ liệu giả lập. Trích cả Poll cũ từ backup để tái hiện **một lần hụt readiness → handoff cùng workspace**, rồi chạy 38 tình huống với code mới.
- Các ca gồm: idle, hụt readiness một/nhiều lần, Tekla bận lâu, snapshot rỗng/thiếu bound, lỗi quyền, lỗi Model/Drawing API, cooldown, cùng identity/case/slash/title drawing, title trống, đóng/mở model thật giả lập, đổi model cùng PID, đổi SESSIONNAME, PID tái dùng, nhiều phiên, hủy picker, offline startup, yêu cầu/busy đổi trong await và reconnect thủ công.
- 540 phương thức có sẵn ngoài phần sửa được so token với backup và giữ nguyên. Không thay API/static connection tại chỗ.
- `Update-Portable.ps1 -BuildOnly -NoPause`: Release x64 thành công, 40 file runtime được so hash; manifest portable 41 mục được kiểm đầy đủ. CodeMap cập nhật và `-Check` đạt.

## Giới hạn

Đã đọc metadata process/log hiện có nhưng không điều khiển cửa sổ, không đóng/mở model và không chạy DIM/in trên bản vẽ thật. Không ghi file trong folder model. Kiểm thử chứng minh quyết định điều phối với các trạng thái mô phỏng; chưa quan sát một phiên sử dụng dài sau khi thay EXE mới. Chu kỳ đóng–mở model hoàn tất giữa hai scan có thể không được quan sát; guard API đúng model tại thời điểm chạy lệnh vẫn giữ nguyên. Nếu kết nối cũ không thể phục hồi, reconnect chủ động vẫn có sẵn.
