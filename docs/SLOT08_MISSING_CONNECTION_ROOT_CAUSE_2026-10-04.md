# Slot 08 — sửa nguyên nhân thiếu interface khi không còn Connection

## Chẩn đoán bản hiện tại

Assembly `小梁 [C2-e-16]`, main `117599033`, 3 target view. Phiên Tekla mới; lúc kiểm không có TTSK chạy. Cả Model và Drawing API kết nối thành công. Đây là topology dữ liệu khác hai mẫu đã kiểm trước, không có bằng chứng lỗi do UI đang chạy bản cũ.

- Plate `112229909` là ContourPlate PL9, class 99, mark `DUMMY-1712`; bulông `119374745` nối trực tiếp plate với main.
- Plate này và hai dầm reference không trả về father/component/Connection nối plate–support trong audit. Plate không có weld tới support; nhánh bổ sung dựa vào native Connection không có điểm bắt đầu.
- Reference xám `108819612`, Class 1, profile SH1000*300*16*25; reference đỏ `130971858`, Class 1010, cùng profile nhưng segment 500 mm. Tọa độ chính xác, contour/bo góc và mặt solid được đọc ngoài mọi thao tác ghi.
- Trước sửa, thuật toán đã Keep plate trực tiếp với main, nhưng cả hai reference đều HIDE. Reference đỏ đang ẩn ở Front và hiện ở Top; kế hoạch cũ sẽ tiếp tục ẩn nó tại Top.

## Nguyên nhân gốc và giới hạn bản sửa trước

Flag Dummy vốn dùng để chặn mở rộng graph qua reference, không phải khẳng định part không tham gia liên kết. Với plate mang DUMMY, graph nền dừng tại seed; điều đó được giữ để tránh lan sang toàn model. Nhánh bù trước đó lại yêu cầu native Connection và secondary plate không Dummy. Vì vậy nó bỏ sót plate là interface thật nhưng thiếu component/weld hoặc có nhãn DUMMY.

Sửa bằng cách bỏ toàn bộ guard Dummy, nới khoảng cách hay giữ mọi reference cùng tên sẽ ảnh hưởng các mối nối khác. Bản sửa này không làm những việc đó.

## Luồng bổ sung

1. Giữ nguyên Resolve, CanFollow, BuildConnections extraction và RetainLocalDummies. Nhánh Connection/màu/chồng nhau đã có chạy trước.
2. `UnresolvedPrimaryInterfacePlates`: seed bằng cạnh bolt/weld thật từ primary không dummy tới plate đã Keep. Không dùng lỗ self; không dùng riêng mark/name/class/assembly. Plate có thể mang DUMMY vì quan hệ trực tiếp đã được chứng minh.
3. Loại seed đã có receiver thép thường qua cạnh thật hoặc đã có proof trong ReferenceSupportPlates; các ca ổn định đó không bị suy lại.
4. Chỉ xét dummy Beam ngoài primary có drawing part trong view chứa plate. Toàn bộ dữ liệu solid được chuẩn hóa current plane → global, không đổi work plane. Mặt solid đối hướng của support phải bao đủ một mặt thực của plate trong dung sai 0,05 mm đã có; không nới tolerance hoặc dùng bbox để tạo mối nối.
5. Một ứng viên màu rõ được chọn khi các contact thuộc cùng nhóm profile/material/trục và cùng interface plate. Không hard-code ID, mark, Class 1/1010, chiều dài 500 hoặc chênh cao độ của mẫu.
6. Nếu nhiều ứng viên, hình học khác nhóm, màu chưa rõ hoặc đọc thiếu: giữ contact đã chứng minh, cảnh báo và tiếp tục. Lỗi đọc một candidate không dừng xét các candidate sau và không tạo kết luận duy nhất từ tập dữ liệu thiếu.
7. Chỉ bổ sung Keep/proof để restore support trong view có primary + plate đang hiện. Không đổi Nodes/Edges/Dummy hoặc tạo cạnh giả cho Check DIM. Không tự hiện plate DUMMY ở section khi chưa có điều kiện của luồng plate cũ.

## Kiểm ảnh hưởng

- Đối chiếu planner cũ/mới trên `[C2-e-16]`: chỉ quyết định cho `130971858` ở hai view đổi; source/part còn lại giữ nguyên. Reference xám vẫn theo quyết định ẩn nền.
- 47 hồi quy cũ + 43 ca overlap/interface = **90 ca đạt**. Các ca mới gồm plate DUMMY/đổi mark, thiếu cạnh, lỗ self, dummy main, liên kết remote, receiver đã được giải quyết bằng cạnh thật hoặc native Connection, và plate ngoài Keep.
- **2.000 đồ thị** so baseline: Resolve/warnings, component rules, primary/dummy và membership Check DIM vẫn giống. Live read-only impact review giữ nguyên mọi Keep/reason cũ, Nodes/Edges và Check DIM membership.
- Source sản phẩm chỉ thêm nhánh trong PHU_Slot08_NeighborCleanup.cs; không sửa các engine DIM, thuật toán Fit, macro batch, tiêu chuẩn nhận dạng dummy hoặc cấu hình model.

## Live-test được thực hiện

Snapshot tại `.codex-artifacts/neighbor-live/20261004-232817`, ngoài folder model.

- Trước: bản hiện tại có **23 chuỗi DIM, 3 view**.
- Apply thật: **ẩn 1** reference xám còn hiện ở Top, **hiện 1** reference đỏ đang ẩn ở Front.
- Mọi item được so với trạng thái dự kiến; object ID/type, DIM point/distance/direction, view position/scale/restriction box và Check DIM report trước/sau giống nhau ngoài visibility đã lập kế hoạch.
- Apply lần hai trả **0**. Save → close → reopen chính `[C2-e-16]`: visibility khớp trạng thái đã kiểm, Apply lại trả **0**. Bản này được để mở cho người dùng.
- Release x64, portable được cập nhật bằng workflow và xác minh 40 file SHA-256. Hash EXE lúc bàn giao: `67ACFB20F71061153EF9127CB0750D08FF20CA2365BA4A256ECDCC01FF6BD510`.

Đã kiểm thực tế topology này; không khẳng định mọi loại solid cong, mặt nhiều lỗ/lõm hoặc mọi representation đều được tự phân biệt. Trường hợp chưa đủ bằng chứng giữ contact đã đọc được và cảnh báo theo yêu cầu, không force-stop vì mơ hồ. Không thay đổi dữ liệu 3D, màu hoặc work plane; không đặt file kiểm tra trong model.
