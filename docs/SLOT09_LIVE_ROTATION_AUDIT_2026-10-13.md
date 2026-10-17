# Slot 09 — đo thực tế phép quay và nâng cấp ngày 12–13/10/2026

## Cập nhật theo yêu cầu chủ dự án: gỡ hai phần điều khiển ghi

Sau báo cáo live, người dùng yêu cầu bỏ hai bổ sung xử lý Recreate và chặn Freeze/Locked. Đã xóa `ReadRecreationRequirement` và `ValidateEditability`, khôi phục nguyên `FillAngles`/`CommitInDialog` trước nâng cấp. Cơ chế Recreate/xác nhận native vốn có vẫn như trước; không xóa bộ tìm góc, hỗ trợ Z liên tục, xét thứ tự view thay thế hoặc trạng thái view hiện tại.

Đối chiếu source phần Geometry/Reader và dữ liệu góc trước/sau gỡ giữ nguyên từng byte. Kiểm lại offline trên portable mới: Pure 28.805, dữ liệu live đã đo 1.891, UI 230 assertions đều đạt. Không quay/thử ghi bản vẽ thật lại trong lượt gỡ này. Build Release x64 và manifest 41/41 SHA-256 đạt; CodeMap cập nhật. Các mục 1–2 và hash/kết quả live bên dưới mô tả **phiên bản đã thử trước khi người dùng yêu cầu gỡ**, không còn là hành vi của bản bàn giao sau cùng. Bộ tìm góc vẫn được đối chiếu lại với toàn bộ 320 frame đã đo.

## Phạm vi được phép và dữ liệu thực

Người dùng yêu cầu thử trực tiếp bản đang mở và C2-e-44, C2-e-28, 2-BK-15 trong model test; cho phép thay đổi/tái tạo bản vẽ, không cần backup hoặc khôi phục. Chỉ thay đổi bản vẽ qua Tekla và cờ Freeze phục vụ thử. Không tạo macro, preset, file/cache/log hoặc công cụ trong thư mục model.

- Tekla thực: `C:\Program Files\Tekla Structures\2025.0\bin\TeklaStructures.exe`, **2025.0.56843.0**.
- Model: `D:\27-011)ATTOKPJ(Data_Center)`; bản đang mở đầu lượt **[A2-a-30]**.
- Helper .NET Framework 4.8/x64 ghép đúng PID/start/session trước chạm API; `TSAppConfigPatcherTask 2.1.3` báo `AppConfig Patch Completed`, config có codeBase; Model/Drawing connection đều True.
- Source/test/log đặt trong repo C:, `.codex-artifacts/keyplan-live-20261012`. Source sản phẩm chỉ đổi `PHU_Keyplan.cs`; không đổi MainForm, engine DIM hoặc model geometry trong lượt này.
- Đọc main part bằng liên kết thật. A2-a-30 là POST đứng lên; hai C2 là BEAM; 2-BK-15 là đối tượng Beam tên PLATE, trục Local X hướng xuống. Không quy mẫu 2-BK này thành ContourPlate.

## Bằng chứng phép quay

Gọi B là ma trận có ba cột là trục Local của main part ở global. Hệ trục dùng để sinh view là:

```text
B' = B · Rx(-AroundX) · Ry(-AroundY) · Rz(-AroundZ)
Front  = (B'.X,  B'.Y)
Back   = (B'.X, -B'.Y)
Top    = (B'.X, -B'.Z)
Bottom = (B'.X,  B'.Z)
```

Đây là phép quay trên một hệ 3D chung trước khi chiếu. Không được coi Around Z là quay độc lập hai view trên giấy. Khi tùy chọn đặt cột đứng áp dụng, quay frame trên giấy −90° cho main X hướng lên; +90° khi hướng xuống. Tọa độ tâm khung view trên sheet chỉ quyết định bố trí trái/phải/trên/dưới, không đưa vào phép quay 3D.

Theo tài liệu hãng, X nhận 0/90/180/270, Y nhận 0/180, Z cho góc bất kỳ. Local dùng trục main part; Model dùng trục global. Plate có thể chịu quy tắc đặt hướng riêng nên vẫn phải kiểm view thực tế. [Tekla 2025: Define part orientation](https://support.tekla.com/doc/tekla-structures/2025/dra_setting_part_orientation). Đo hướng hiển thị bằng `DisplayCoordinateSystem`; lưu cả `ViewCoordinateSystem` trong log để không trộn hai nguồn. [Tekla View properties](https://developer.tekla.com/doc/tekla-structures/2025/view-properties-54397).

Mỗi bản được thử 32 tổ hợp góc vuông X/Y/Z, cộng 8 tổ hợp Z không vuông phủ cả tám cặp X/Y: 30°, 37,25°, 123,456°, 270,125°, 330° (tương đương −30°), 359,999°, 30° (tương đương 390°), 315,75°. Mỗi lượt đọc lại góc đã lưu trong hộp native rồi đọc frame mới bằng Drawing API. Không dùng giá trị còn hiển thị trong ô nhập làm bằng chứng duy nhất.

| Bản vẽ | Lượt hợp lệ | Frame thật | Sai lệch frame lớn nhất (độ) |
|---|---:|---:|---:|
| A2-a-30 | 40 | 80 | 1,009 × 10⁻¹⁰ |
| C2-e-44 | 40 | 80 | 1,007 × 10⁻¹⁰ |
| C2-e-28 | 40 | 80 | 1,007 × 10⁻¹⁰ |
| 2-BK-15 | 40 | 80 | 7,409 × 10⁻¹⁰ |

Tổng **160 lượt / 320 frame**, đều dưới dung sai **0,0001°**. Các góc vuông có sai lệch so với writer sản phẩm bằng 0. Oracle ma trận độc lập dùng sin/cos và phép nhân 3×3, không gọi `LocalView/Rx/Ry/Rz` của sản phẩm để dựng hướng kỳ vọng. Ngoài so forward, đã kiểm **160 tập nghiệm tìm ngược**, **152 gợi ý** trên cặp frame đo thật của chính góc được gợi ý. Identity, trục Local và hai đầu cấu kiện model không đổi giữa các lượt.

## Bốn điểm sửa từ kiểm thực tế

### 1. Recreate đổi trạng thái sau khi nhập Z

Ban đầu đổi Z từ 0 sang 90 làm `SelectIndex(Recreate,0)` lỗi “danh sách khác cấu hình”, dù count vẫn đúng 2. Đo controls sau lỗi cho thấy Tekla đã tự chọn Yes và khóa control sau bước chuẩn hóa Z. Đây là trạng thái thay đổi giữa hai lần đọc, không phải cấu hình combo sai.

`ReadRecreationRequirement` đọc selection/enabled đến khi ổn định (3 khoảng 50 ms, timeout 2 s), ưu tiên No chỉ khi còn được chỉnh. Nếu Tekla vừa khóa Yes thì giữ Yes và dùng luồng xác nhận được phép; không cưỡng bức control disabled. Vẫn kiểm count, giá trị và readback. 160 lượt đo hợp lệ cùng các lượt apply đã đi qua luồng này.

### 2. Freeze cho lưu góc nhưng không quay view

A2-a-30 ban đầu `Frozen=True, Locked=False, Issued=True`. 32 lượt chẩn đoán đầu lưu được các góc khác nhau nhưng frame view giữ nguyên. Các log này nằm riêng ở `frozen-diagnostic`; **không cộng vào 160 lượt xác minh phép quay**.

Bộ test bỏ Freeze của đúng các target đã được người dùng cho phép, đo lại. Sản phẩm bổ sung `KeyplanApply.ValidateEditability` ngay trước commit native: chặn Locked/Freeze và giải thích cách xử lý; không tự đổi cờ. Đã live-test bật Freeze riêng A2-a-30, gọi luồng Apply thật, thấy từ chối trước ghi và đối chiếu góc lưu không đổi; sau đó bộ test bỏ Freeze để chạy tiếp.

### 3. Bỏ sót Z không vuông

Bản cũ chỉ dò 32 bộ, constructor từ chối Z=37,25°. Test oracle tái hiện một cặp frame có nghiệm 37,25° nhưng search cũ báo vô nghiệm.

`Angles.Z` chuyển thành double hữu hạn, chuẩn hóa modulo 360. X/Y giữ miền rời rạc. Giữ 32 ứng viên cũ để ổn định góc vuông; với mỗi X/Y, `SolveZCandidates` giải `a·cos(Z)+b·sin(Z)+c=0` từ các tích vô hướng chéo của trục chiếu và ràng buộc pháp tuyến. Ứng viên Z làm tròn 0,001° rồi kiểm lại cả hai view, chiều trục/phía nhìn và dung sai gốc. Không quét theo bước độ, không chấp nhận nghiệm gần nhất. Nếu độ chính xác ô nhập không biểu diễn đủ góc cần thiết, vẫn báo không có nghiệm đạt dung sai.

### 4. 2-BK-15 bị báo vô nghiệm do thứ tự view

Bản này dùng Front/Top, nhưng Top bên trái và Front bên phải. Với hướng trục main part đang từ trên xuống, thứ tự đó không có nghiệm theo quy ước keyplan X trái/Y phải. Thử đúng cùng hai view ở thứ tự ngược có nghiệm **Local/270/0/0**.

Mở rộng nhánh thử ánh xạ trái/phải ngược cho mọi cột hai view tiêu chuẩn, không chỉ GenericMember như trước. Chỉ dùng nhánh khi thứ tự hiện tại vô nghiệm và thứ tự ngược có nghiệm. Giữ loại view thật, không thêm view giả, không tự di chuyển view. Kết quả ghi rõ yêu cầu đặt Front trái/Top phải; sau Modify xác minh đúng chính ánh xạ đã dùng để tìm góc.

## Luồng sản phẩm và trạng thái cuối bản test

Chạy trực tiếp `KeyplanApply.Run(AutoApply=true)` sau các lượt làm sai hướng rồi chạy lại lần hai:

| Bản vẽ | Góc Local cuối X/Y/Z | Kết quả lần áp dụng | Lần chạy lại |
|---|---|---|---|
| A2-a-30 | 0 / 0 / 0 | Correct | Không Modify |
| C2-e-44 | 0 / 0 / 0 | Correct | Không Modify |
| C2-e-28 | 0 / 0 / 0 | Correct | Không Modify |
| 2-BK-15 | 270 / 0 / 0 | DirectionsMatch=True, LayoutMismatch | Không Modify, tiếp tục nhắc Front trái/Top phải |

Bản đang mở cuối lượt là A2-a-30. Không khôi phục trạng thái ban đầu theo yêu cầu; bản test đã được tái tạo/quay và có thể mất chỉnh tay do chính native Recreate. Không sửa/copy/backup file model bằng filesystem, không tạo macro. Các lệnh lưu/đóng bản test để chuyển target do Tekla quản lý; chỉ các bản nằm trong allowlist của harness được tác động.

## Kiểm tra hồi quy và bàn giao

- `tests/KeyplanLive/Pure.ps1`: **3.840 ca / 28.805 assertions**, 5 hệ main part, cả thứ tự các cặp Front/Top/Back/Bottom, đủ X/Y, Z vuông/lẻ, oracle độc lập, giữ nghiệm ưu tiên cũ ở góc vuông, góc không đủ độ phân giải bị từ chối.
- `tests/KeyplanLive/Analyze.ps1`: **1.894 assertions** từ 320 frame đo thật, tìm ngược, gợi ý so với frame thật, geometry model bất biến, Freeze/Locked và so sánh góc modulo.
- `tests/KeyplanViewStatus/Run.ps1`: **230 assertions**, UI light/dark và 144 trường hợp Reader; không đọc/ghi Tekla trong bộ UI.
- Tổng **30.929 assertions offline**, cộng các phép live ở trên. Không dùng số kiểm live lịch sử trong `SLOT09_KEYPLAN.md` để thay cho kiểm lần này.
- Build cuối Release x64/C# 7.3/net48 bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`; 40 runtime file khớp staging, manifest **41/41 SHA-256**.
- EXE SHA-256: `E88EEBCA9DEBD406320E67D1A9238B599AB71497EFB68F0138478000012C9E62`.
- Không commit/push/phát hành. Không thay đổi engine DIM/Section/Scale hoặc các thay đổi người dùng đã có trong working tree.

Phạm vi xác nhận là Tekla build trên và bốn bản đã đo, cộng các ca hình học thuần. Đây không phải lời đảm bảo mọi loại bản vẽ/ContourPlate/phiên bản Tekla đều đúng tuyệt đối. Những trường hợp chưa rõ main part, quy tắc đặt hướng đặc biệt, view phản chiếu/khai triển/undeformed, thiếu view hoặc không có nghiệm đạt dung sai vẫn bị từ chối hoặc báo chưa đủ dữ liệu.
