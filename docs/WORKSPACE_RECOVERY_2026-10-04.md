# Sửa nhận diện workspace khi đổi model cùng cửa sổ

## Bằng chứng nguyên nhân

Probe chỉ đọc trên PID 12840: Model=True, Drawing=True, ModelInfo.ModelPath=`D:\27-011)ATTOKPJ(Data_Center)`, active AssemblyDrawing `[C2-e-73]` / `小梁`. Tiêu đề thực là `Tekla Structures - Educational version - D:\27-011)ATTOKPJ(Data_Center) `, có **một khoảng trắng cuối**. Code MatchesModelTitle cũ chỉ nhận suffix rỗng hoặc suffix bắt đầu bằng dấu `-`; suffix chỉ có khoảng trắng bị từ chối, dù hai API và model đều đúng. Log Startup gate lặp lại thông báo model không khớp.

Lỗi độc lập thứ hai: ObserveTeklaForeground xóa pending khi PID/start không đổi, timer chỉ scan khi pending khác 0. Đổi model trong chính process không tạo yêu cầu bàn giao. Guard API vẫn chặn model mới so với ModelPath cũ, nhưng không có cơ chế tự làm mới ngữ cảnh. Lỗi kết nối còn tự tắt FollowForeground, gây ngừng dò sau một lần thất bại.

## Cách sửa và ranh giới an toàn

- MatchesModelTitle dùng cùng ModelFolder parser với discovery, chuẩn hóa case/khoảng trắng cuối/trailing slash; so toàn bộ đường dẫn, không so prefix. Vẫn từ chối `D:\Model_A` khi title là `D:\Model_A - Tower B`.
- SameWorkspace so **PID + start time + SESSIONNAME + model path**. PollTeklaWorkspaceAsync định kỳ khi rảnh, kể cả không đổi focus và chế độ cố định. Scan process/event ở nền; không chồng scan, kiểm lại cờ bận/pending sau await.
- Model/kênh mất: chặn lệnh mới dùng ngữ cảnh cũ. Khi model khác sẵn sàng, đợi cùng identity ổn định ít nhất 500 ms qua hai scan rồi tạo child. Nếu đã quan sát đóng model, mở lại cùng đường dẫn cũng tạo kết nối sạch.
- Child kiểm lại process identity và cả hai API, đối chiếu model thật với snapshot đã chọn. Chỉ ready/accept thành công mới đóng parent. Không đổi các proxy tĩnh tại chỗ và không chuyển Drawing object/selection/resume cache sang model mới.
- Nếu child chưa kết nối được: giữ parent, hủy đúng child, retry có khoảng nghỉ 5 giây. Không tự tắt FollowForeground. Nếu người dùng quay lại phiên hiện tại hợp lệ, bỏ candidate cũ. Hủy picker được ghi nhận, không tự fallback vào phiên duy nhất rồi phủ nhận lựa chọn hủy.
- Không thay thuật toán DIM, không bỏ guard đúng model và không chạy lệnh đang bận sang dự án khác.

## Kiểm tra đạt

| Nhóm | Bằng chứng |
| --- | --- |
| Title/API thật | Hai API kết nối đúng ATTOKPJ và đọc `[C2-e-73]` sau sửa |
| Parser | 21 case title view/drawing/không view; khoảng trắng/tab cuối; UNC; tên folder có ` - `; thêm case prefix dấu gạch và null |
| Identity | Cùng PID khác model, PID tái dùng/start time khác, kênh khác, thiếu model, cùng path khác case/slash |
| Snapshot monitor | Không đổi không reconnect; đang bận không scan; đóng/tải model chặn lệnh; mở lại cùng path tạo candidate |
| Trạng thái chuyển tiếp | Title B rồi C reset ổn định; quay lại A bỏ candidate; retry cooldown được tôn trọng |
| Bàn giao cùng PID | Chỉ đặt ModelPath của TTSK thành đường dẫn cũ mô phỏng; không thay Tekla. Không foreground event, FollowForeground=false: đợi hết busy, chặn stale command, child mới xác minh ATTOKPJ và hiện đúng title |
| Build | Release x64/C# 7.3/net48; portable 40 file runtime được workflow kiểm SHA-256 |

Lệnh tái kiểm: `pwsh -NoProfile -File tests/TeklaSessions/Run-ReadOnly.ps1 -WorkspaceRecovery -StartPid <PID>`. Cần model Tekla sẵn sàng và TTSK đang đóng; chỉ đọc API. Probe chặn popup exception và xuất lỗi/exit code. Log lần chạy tại `.codex-artifacts/workspace-recovery-validation.txt` có thể xóa; không là phụ thuộc sản phẩm.

## Giới hạn kiểm chứng

Không chủ động đóng/mở/chuyển model thật hoặc chạy DIM trên bản vẽ người dùng. Đổi model/đóng/tải được mô phỏng bằng snapshot/trạng thái phía TTSK, còn xác minh API và bàn giao process dùng model thật. Máy hiện có một phiên Tekla nên chưa lặp lại thử đa cửa sổ thật trong lần sửa này. Polling không bảo đảm phát hiện chu kỳ đóng–mở lại chính model đó hoàn tất hoàn toàn giữa hai lượt scan; các command preflight vẫn giữ. Không tuyên bố mọi hành vi Tekla hoặc mọi thuật toán DIM đã được chứng minh tuyệt đối.
