# Arrange View — Theo bản mẫu

Chỉ thêm một nút **Theo bản mẫu**, ngay dưới ARRANGE trong MainForm.
Không thêm lựa chọn hoặc nút tải. ARRANGE hiện hành giữ nguyên.

## Luồng thực hiện

1. Bấm nút: chuyển sang Batch và gọi đúng luồng Load Selected hiện có (`btnLoad_Click` → `GetDrawingSelector().GetSelected()`). Chỉ tải các bản người dùng đang chọn trong Document Manager; không lấy toàn model, không dùng lại danh sách cũ khi selection rỗng.
2. Giữ thứ tự danh sách Load Selected trả về; dòng 1 là chuẩn. Chọn 5 bản thì chỉ tải 5 bản: giữ bản đầu và căn 4 bản còn lại. Chọn dưới 2 bản thì không chạy căn view. Nếu Load lỗi, xóa cả danh sách nguồn và bảng hiển thị để không chạy trên dữ liệu tải dở.
3. Yêu cầu lưu/đóng drawing đang mở để không tự lưu hoặc bỏ thay đổi của người dùng.
4. Mở bản đầu, đọc presentation hiện hành với reader của Revision. Không đọc snapshot lịch sử, không sửa bản chuẩn. Nếu bản chuẩn không hợp lệ thì dừng, không tự đổi chuẩn.
5. Mỗi bản sau: chỉ lập kế hoạch căn view chính; lưu vị trí/scale các view còn lại để xác minh không thay đổi. Commit, đọc lại vị trí/scale, lưu rồi đóng. Kết quả từng bản nằm ở STATUS/RESULT.
6. Có tiến độ và dừng sau bản hiện tại. Các bản đã lưu giữ kết quả; bản lỗi ghi rõ nguyên nhân. Nếu lưu lỗi thì dừng và giữ bản đó đang mở.
7. Lỗi hình học, thiếu/không ghép được view hoặc bản đích Locked chỉ ghi ERROR cho bản đó, đóng không lưu rồi tiếp tục bản kế tiếp. Bản mẫu không hợp lệ, mất kết nối/model thay đổi, không đóng được đúng bản hoặc lỗi lưu/xác minh sau lưu mới dừng toàn batch. Không nuốt lỗi đọc hình học để báo OK dù bỏ sót view chính.

## Thuật toán

- Chỉ hỗ trợ SinglePartDrawing/AssemblyDrawing cùng loại và khổ giấy với bản mẫu.
- Căn các view chính kiểu Front, Top, Back, Bottom, End, Model và 3D. `SectionView` chỉ được xem là view chính khi mặt cắt thể hiện phần dọc dài của main part: trục dọc của main part nằm trong mặt phẳng hiển thị và vùng cắt phủ ít nhất 55% chiều dài main part. Mặt cắt ngang hoặc mặt cắt phụ nhỏ như A–A trong ảnh bị giữ nguyên; Detail và Unknown cũng được giữ nguyên. Không gọi Modify lên view được giữ nguyên, kể cả trong hoàn tác. Bản chỉ có mặt cắt phụ sẽ báo không có view chính để căn.
- Resolve main part theo ModelIdentifier; mỗi view chính (kể cả SectionView đủ điều kiện) phải chứa main part duy nhất và presentation khớp View.Origin. View được giữ nguyên không cần main part/presentation hợp lệ; vị trí và scale của chúng được ghi lại để xác minh sau lưu.
- Ghép view chính theo `ViewType + tên chuẩn hóa`, một-một, kiểm tra hướng nhìn khi có dữ liệu trục. Fallback theo ViewType khi duy nhất, rồi theo trục view trong hệ tọa độ main part; vì vậy SectionView dọc có thể tương ứng Front/Top khi đúng hình học, còn SectionView phụ không lọt vào tập ghép. Số lượng/tên mặt cắt phụ có thể khác giữa bản mẫu và bản đích. Không ghép ID hoặc thứ tự collection giữa hai drawing.
- Dùng biên main part trong presentation (mm trên giấy), giống Revision → Create Drawing. Không lấy biên DIM/text hoặc khung xanh làm mốc.
- `target.X = mẫu.Origin.X + mẫu.part.CenterX - đích.part.CenterX`
- `target.Y = mẫu.Origin.Y + mẫu.part.MinY - đích.part.MinY`
- Giữ Z, scale, DIM và hình học. Chỉ view chính hợp lệ được căn.
- Kiểm tra số view, tính duy nhất, số hữu hạn và main part nằm trong khổ giấy trước khi Modify. Chỉ Locked là điều kiện khóa. Frozen/Issued/Ready for issue không chặn dịch view, không tự gỡ hay đặt lại các trạng thái này.
- Sau Modify/commit: đọc presentation mới, kiểm tra main part đã về đúng mốc và scale từng view còn nguyên. Kiểm tra các view được giữ nguyên theo ID trong cùng drawing: không mất/thêm view, không đổi loại, vị trí hoặc scale. Sau Save phải mở lại đúng drawing đã lưu rồi xác minh lần nữa: `SaveActiveDrawing()` có thể đóng drawing mở ẩn. Không giả định active drawing còn tồn tại sau Save. Chỉ ghi OK khi xác minh dữ liệu đã lưu thành công; lỗi lưu/mở lại/xác minh thì dừng để kiểm tra.
- Nếu Modify/commit/read-back thất bại: hoàn tác vị trí cũ khi vẫn đúng drawing. Batch đóng không lưu drawing lỗi; lỗi save thì giữ mở để kiểm tra.

## Giới hạn và kiểm thử

- Căn tâm ngang/mép dưới hình học, không ép góc khung view trùng nhau. Scale khác vẫn giữ scale riêng.
- Không tự xử lý va chạm DIM, title block hoặc view; kiểm tra ngoài giấy chỉ dựa trên main part. View thiếu main part hoặc trùng tên/type không phân biệt được sẽ bị từ chối.
- Bộ `tests/ArrangeReference` kiểm tra cả 16 tổ hợp Locked/Frozen/Issued/Ready và giữ nguyên cờ; chấp nhận SectionView dọc đủ dài ↔ Front/Top khi trùng local axes, từ chối SectionView ngang hoặc nhỏ kể cả trùng tên, khác số lượng mặt cắt, phát hiện đổi vị trí/scale hoặc mất mặt cắt phụ, đảo thứ tự, hướng nhìn không tương thích, thiếu/trùng view, ánh xạ một-một, Unknown, dịch/chênh kích thước và chạy lặp lại.
- Cập nhật 2026-09-26: giữ nguyên mặt cắt phụ trong Theo bản mẫu; mặt cắt dọc đóng vai trò view chính vẫn được căn. Các kết quả live ngày 2026-09-20 bên dưới là lịch sử của hành vi cũ.
- Audit chỉ đọc trực tiếp 5 bản B-C150-385/387/388/389/392: tất cả Frozen, không Locked, 388/389/392 Issued. Cả 5 lập được phương án 2 view. Báo cáo: `.codex-artifacts/ArrangeReference/layout-audit.txt`.
- Kiểm thử thực tế theo yêu cầu người dùng bằng `--live-main`: chạy đúng Load Selected (await Task<bool>) và batch MainForm trên danh sách đang chọn, mở lại từng bản để kiểm tra vị trí, scale, restriction box, hệ trục, chữ ký DIM và ID mọi đối tượng trong tất cả view; chạy lần hai để kiểm tra không dịch thêm. Log/snapshot trước chạy nằm trong `selected-<timestamp>/live-main-report.txt` cạnh executable test. `--review-selected` chỉ mở/đọc/đóng không lưu, ghi rõ vai trò từng view và phương án. Chỉ kết luận đạt khi log live có dòng `SUCCESS`.
- Kết quả live ngày 2026-09-26 trên đúng 4 bản `[C2-e-7]`, `[C2-e-9]`, `[C2-e-12]`, `[C2-e-14]`: PASS hai lượt và mở lại xác minh toàn bộ view. C2-e-12 căn được SectionView dọc id 562029 như view chính; SectionView nhỏ id 192788 và các mặt cắt phụ id 194364, 194169, 192853 được giữ nguyên. Không đổi scale, restriction box, hệ trục, DIM hoặc ID đối tượng. Báo cáo: `.codex-artifacts/ArrangeReference/tests/selected-20260926-154902/live-main-report.txt`.
- Tối ưu tốc độ: đọc trực tiếp main part bằng `GetModelObjects(main.Identifier)`, dùng lại snapshot vừa đọc và bỏ Save/đóng/mở lại khi bản đích đã thẳng hàng. Đo trên cùng 4 bản: lượt có dịch chuyển 37.924 giây; lượt đã thẳng hàng 22.572 giây (benchmark no-op độc lập 21.025/20.602 giây), so với 39.002/39.005 giây trước tối ưu.
- Kết quả ngày 2026-09-20: PASS cả hai lượt MainForm và kiểm tra mở lại 5/5 bản. Bản chuẩn B-C150-385 giữ nguyên; B-C150-392/387/388/389 đều OK, lượt thứ hai không dịch thêm. Log xác nhận lưu tại `.codex-artifacts/ArrangeReference/live-main-report.txt`; executable đã kiểm thử nằm trong thư mục `verified`.

Build ứng dụng thử nghiệm:

```powershell
dotnet msbuild 'TTSK Dim Plates/TTSK Dim Plates/TTSK Dim Plates.csproj' -t:Build -p:Configuration=Release -p:Platform=x64 -p:OutputPath=../../.codex-artifacts/ArrangeReference/verified/
dotnet build tests/ArrangeReference/ArrangeReference.csproj
& '.codex-artifacts/ArrangeReference/tests/ArrangeReference.exe'
```
