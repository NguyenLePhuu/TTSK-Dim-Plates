# Check Revision và Create drawing riêng — 2026-09-19

## Giao diện

Trong cụm Check Scale có hai mục độc lập: Check Dim và Check Revision.
Check Dim không còn chứa nút mở Revision. Mở Check Revision chỉ hiển thị các lựa chọn;
không tự phân tích hoặc ghi màu.

- **Bắt đầu / F5:** chạy đối chiếu snapshot và ghi đỏ các đoạn thay đổi đủ điều kiện.
- **Create drawing:** tạo lại DIM bằng nhánh riêng, sau đó chạy Check REV.
- **Delete REV:** giữ nguyên luồng xóa mặt cắt và đánh dấu xóa đã có.
- **DIM trước / DIM tiếp**, hoặc **Ctrl + ← / →:** duyệt các đoạn trong bộ lọc,
  tô nền thẻ đang xem, cuộn tới thẻ và chọn cụm DIM chứa đoạn đó trong Tekla.
  Tekla DrawingObjectSelector trả về parent set khi chọn một child DIM.
- Bộ lọc: Tất cả thay đổi, Đổi giá trị, DIM mới, Cần kiểm tra, Đã tắt.
  DIM đã đỏ vẫn duyệt được; bộ lọc trống vô hiệu hóa hai nút điều hướng.

Thanh thao tác dưới dùng TableLayoutPanel và hàng AutoSize, tính cả chiều cao nút,
margin/padding và bóng. Không còn hàng 44 px cố định lồng FlowLayoutPanel làm cắt đáy nút.
Bộ lọc dùng cùng kiểu nút/menu theo theme với Check DIM Batch.

## Luồng tạo lại DIM

`MainForm.CheckMenu.CreateRevisionDimensions`
→ `PHU_DimensionRevision.CreateRevisionDrawing`
→ scope riêng → nhánh DIM Plate/H/C/L/Box/Unknown tương ứng
→ kiểm tra DIM thay thế → căn mốc snapshot → xóa DIM cũ
→ commit/đọc lại → đóng scope → Check REV.

Không gọi luồng Create Drawing đầy đủ của MainForm: không Load Standard, Auto Section,
Fit Grid, autoscale, center/arrange, sửa RestrictionBox hoặc tạo/sửa hole mark.
Các phương thức base chỉ được bổ sung điều kiện thoát khi scope Revision hoạt động.
Đã đối chiếu với bản sao đầu phiên: loại bỏ các điều kiện này thì sáu file engine
khớp nguyên bản. Không thay công thức chọn chân/tạo DIM của các engine.

## Mốc vị trí khi thanh dài/ngắn đi

- Dùng DPM snapshot đã được giữ cố định, theo đúng model và GUID bản vẽ.
- Ghép view bằng drawing view ID; ghép main part bằng ModelIdentifier và drawing part ID.
- Đọc các đoạn hình học của main part trong DPM (`ObjectType=259`), không lấy khung xanh
  chứa DIM, mark hoặc text để căn view.
- Giữ **cạnh dưới** và **tâm ngang** main part ở đúng tọa độ giấy của snapshot:

  `OriginX = SnapshotOriginX + SnapshotPartCenterX - CurrentPartCenterX`

  `OriginY = SnapshotOriginY + SnapshotPartMinY - CurrentPartMinY`

- Chiều dài tăng/giảm phân bố đều hai phía; scale hiện hành không đổi.
- View đã đạt mốc không gọi Modify (ngưỡng đọc lại 0,001 mm trên giấy).
- Đọc lại hình học hiển thị sau tạo DIM và sau thay DIM để kiểm tra/căn lần cuối.
- Thiếu view/main part hoặc không có dữ liệu biên duy nhất: dừng trước tạo/xóa DIM,
  không ghép theo tên/thứ tự hoặc lấy kích thước khung làm mốc thay thế.

## Bảo vệ dữ liệu

DIM cũ được giữ trong lúc tạo DIM mới. Chỉ xóa sau khi kiểm tra view không đổi,
đã có DIM thay thế theo view/loại, set có các đoạn đo hợp lệ và vị trí/scale đã được xác minh.
Nếu lỗi trước bước xóa, thu hồi DIM mới và trả vị trí view về đầu lượt chạy.
Nếu bước xóa DIM cũ đã bắt đầu mà thất bại, giữ DIM mới và báo chưa hoàn tất;
không hứa rollback nguyên tử cho toàn bộ Tekla Drawing API.

Snapshot cache gốc không được ghi đè. Chưa gọi SaveActiveDrawing hoặc tự xuất bản phát hành portable.

## Ghép Revision sau khi dựng lại

DIM tổng và chuỗi lỗ có thể cùng chân đầu/cuối nhưng đổi vị trí dòng DIM sau khi tạo lại.
Khi có nhiều ứng viên, ưu tiên các khoảng đo trùng và cấu trúc số đoạn trước khoảng cách
dòng DIM. Vẫn giữ review khi ứng viên không phân biệt được. Có regression đảo thứ tự cụm.
Không sửa đường ghi màu native; sử dụng bộ ghi đã có, chạy trên luồng chính Tekla.

## Kiểm chứng

Bản vẽ thật: **[BmC-117]**, profile **C150*75*20*3.2**, ba view, scale **1:10**.

- Trước tạo lại: 17 đoạn đang có vẫn giống snapshot; hình học mới dài hơn 2 mm.
- Lần tạo lại đầu: 2 view cần dịch; Section giữ nguyên.
- Lần thứ hai: 8 set / 19 đoạn, **0 lần dịch view**, cả ba view vẫn scale 1:10.
- Kết quả cuối: **7 đoạn đỏ, 12 không đổi, 0 cần kiểm tra**.
  Bao gồm `688.2 → 690.2`, hai đoạn `2553 → 2555` và bốn đoạn được nhận diện là DIM mới.
- Fault injection sau khi tạo một DIM: thu hồi đúng DIM mới; ID/chân/giá trị DIM cũ,
  vị trí/scale view giữ nguyên.
- Duyệt trước/tiếp/vòng lại: chọn đúng parent set chứa đoạn đang xem.
  Bộ lọc Đổi giá trị chứa đúng 3 đoạn; không đổi hình học, scale hoặc màu.
- Kiểm tra giao diện sáng/tối ở hai kích thước; đo cả đáy nút/padding; quan sát menu thật.
- Build ứng dụng Release x64 đạt (cảnh báo sẵn có ở các file khác).
- Test build đạt, 0 warning; 90 classifier cases, 506 hardening assertions,
  67 Revision assertions, session checks, 60 batch assertions đều đạt.

Các biến thể dài/ngắn, dịch ngang/dọc và trường hợp không đổi đã được kiểm tra bằng
dữ liệu hình học thuần. Chưa chạy live Create drawing riêng trên các profile khác C;
chưa thử hình học biên cong không có đủ đoạn thẳng trong DPM. Delete REV được giữ nguyên,
không xóa thêm một mặt cắt chỉ để kiểm thử giao diện.

Log, ảnh kiểm tra và bản sao nguồn trước sửa: `.codex-artifacts/RevisionCreate/`.
Executable: `TTSK Dim Plates/TTSK Dim Plates/bin/x64/Release/TTSK Dim Plates.exe`.
