# Arrange View — Theo bản mẫu

Chỉ thêm một nút **Theo bản mẫu**, ngay dưới ARRANGE trong MainForm.
Không thêm lựa chọn hoặc nút tải. ARRANGE hiện hành giữ nguyên.

## Luồng thực hiện

1. Bấm nút: chuyển sang Batch và gọi đúng luồng Load Selected hiện có (`btnLoad_Click` → `GetDrawingSelector().GetSelected()`). Chỉ tải các bản người dùng đang chọn trong Document Manager; không lấy toàn model, không dùng lại danh sách cũ khi selection rỗng.
2. Giữ thứ tự danh sách Load Selected trả về; dòng 1 là chuẩn. Chọn 5 bản thì chỉ tải 5 bản: giữ bản đầu và căn 4 bản còn lại. Chọn dưới 2 bản thì không chạy căn view. Nếu Load lỗi, xóa cả danh sách nguồn và bảng hiển thị để không chạy trên dữ liệu tải dở.
3. Yêu cầu lưu/đóng drawing đang mở để không tự lưu hoặc bỏ thay đổi của người dùng.
4. Mở bản đầu, đọc presentation hiện hành với reader của Revision. Không đọc snapshot lịch sử, không sửa bản chuẩn. Nếu bản chuẩn không hợp lệ thì dừng, không tự đổi chuẩn.
5. Mỗi bản sau: đọc toàn bộ view, lập và kiểm tra kế hoạch trước khi dịch chuyển; commit, đọc lại vị trí/scale, lưu rồi đóng. Kết quả từng bản nằm ở STATUS/RESULT.
6. Có tiến độ và dừng sau bản hiện tại. Các bản đã lưu giữ kết quả; bản lỗi ghi rõ nguyên nhân. Nếu lưu lỗi thì dừng và giữ bản đó đang mở.

## Thuật toán

- Chỉ hỗ trợ SinglePartDrawing/AssemblyDrawing cùng loại và khổ giấy với bản mẫu.
- Resolve main part theo ModelIdentifier; mỗi view phải chứa main part duy nhất và presentation khớp View.Origin.
- Ghép view theo `ViewType + tên chuẩn hóa`, một-một, kiểm tra hướng nhìn khi có dữ liệu trục. Fallback theo ViewType chỉ khi duy nhất ở cả hai bản và không phải Unknown. Nếu không ghép được thì so sánh trục view trong hệ tọa độ main part: Section A/B có thể tương ứng Top/Front dù cấu kiện xoay/nghiêng trong model. Cần ứng viên duy nhất và ánh xạ một-một. Không ghép ID hoặc thứ tự collection giữa hai drawing.
- Dùng biên main part trong presentation (mm trên giấy), giống Revision → Create Drawing. Không lấy biên DIM/text hoặc khung xanh làm mốc.
- `target.X = mẫu.Origin.X + mẫu.part.CenterX - đích.part.CenterX`
- `target.Y = mẫu.Origin.Y + mẫu.part.MinY - đích.part.MinY`
- Giữ Z, scale, DIM và hình học. Mọi view hợp lệ đều được căn, bao gồm trường hợp hai view.
- Kiểm tra số view, tính duy nhất, số hữu hạn và main part nằm trong khổ giấy trước khi Modify. Chỉ Locked là điều kiện khóa. Frozen/Issued/Ready for issue không chặn dịch view, không tự gỡ hay đặt lại các trạng thái này.
- Sau Modify/commit: đọc presentation mới, kiểm tra main part đã về đúng mốc và scale từng view còn nguyên. Sau Save phải mở lại đúng drawing đã lưu rồi xác minh lần nữa: `SaveActiveDrawing()` có thể đóng drawing mở ẩn. Không giả định active drawing còn tồn tại sau Save. Chỉ ghi OK khi xác minh dữ liệu đã lưu thành công; lỗi lưu/mở lại/xác minh thì dừng để kiểm tra.
- Nếu Modify/commit/read-back thất bại: hoàn tác vị trí cũ khi vẫn đúng drawing. Batch đóng không lưu drawing lỗi; lỗi save thì giữ mở để kiểm tra.

## Giới hạn và kiểm thử

- Căn tâm ngang/mép dưới hình học, không ép góc khung view trùng nhau. Scale khác vẫn giữ scale riêng.
- Không tự xử lý va chạm DIM, title block hoặc view; kiểm tra ngoài giấy chỉ dựa trên main part. View thiếu main part hoặc trùng tên/type không phân biệt được sẽ bị từ chối.
- Build Release x64 thành công. Bộ `tests/ArrangeReference` kiểm tra cả 16 tổ hợp Locked/Frozen/Issued/Ready và giữ nguyên cờ; thêm Section A/B ↔ Top/Front bằng local axes, đảo thứ tự, hướng nhìn không tương thích, thiếu/trùng view, ánh xạ một-một, Unknown, dịch/chênh kích thước và chạy lặp lại.
- Audit chỉ đọc trực tiếp 5 bản B-C150-385/387/388/389/392: tất cả Frozen, không Locked, 388/389/392 Issued. Cả 5 lập được phương án 2 view. Báo cáo: `.codex-artifacts/ArrangeReference/layout-audit.txt`.
- Kiểm thử thực tế theo yêu cầu người dùng bằng `--live-main`: chạy đúng Load Selected và batch MainForm trên 5 bản chính thức, mở lại từng bản để kiểm tra vị trí, scale và chữ ký DIM; chạy lần hai để kiểm tra không dịch thêm. Log nằm trong `live-main-report.txt` ở thư mục chạy lệnh. Chỉ kết luận đạt khi log có dòng `SUCCESS`.
- Kết quả ngày 2026-09-20: PASS cả hai lượt MainForm và kiểm tra mở lại 5/5 bản. Bản chuẩn B-C150-385 giữ nguyên; B-C150-392/387/388/389 đều OK, lượt thứ hai không dịch thêm. Log xác nhận lưu tại `.codex-artifacts/ArrangeReference/live-main-report.txt`; executable đã kiểm thử nằm trong thư mục `verified`.

Build ứng dụng thử nghiệm:

```powershell
dotnet msbuild 'TTSK Dim Plates/TTSK Dim Plates/TTSK Dim Plates.csproj' -t:Build -p:Configuration=Release -p:Platform=x64 -p:OutputPath=../../.codex-artifacts/ArrangeReference/verified/
dotnet build tests/ArrangeReference/ArrangeReference.csproj
& '.codex-artifacts/ArrangeReference/tests/ArrangeReference.exe'
```
