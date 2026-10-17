# Check DIM: mốc giữa bề dày plate nhìn cạnh

## Nguyên nhân và phạm vi

Audit API chỉ đọc bản đang mở `胴縁 / [B-C150-171]`: 6 view, 28 chuỗi,
87 chân, không có cảnh báo thiếu hình học. Hai chân của hai chuỗi 70–70 bị
ERROR vì REF của plate vốn bị cấm; mép solid gần nhất cách đúng 3 mm.
Cả hai thuộc ContourPlate `B-RP-95`, profile `PL6`, trong primary fabrication unit.

| View | DIM / chân | Plate | Tọa độ Y chân | Hai mặt solid Y | Trước → sau |
| --- | --- | --- | --- | --- | --- |
| 10248 | 23742 / 1 | 23019696 | 3495 | 3492 / 3498 | ERROR → OK |
| 10248 | 23760 / 1 | 23019552 | 1135 | 1132 / 1138 | ERROR → OK |

Đơn vị mm trong View.DisplayCoordinateSystem. ID, tên, số 6/70 và tọa độ
chỉ dùng làm bằng chứng/test; không được mã hóa trong source sản phẩm.

Giữ nguyên Classify, ValidateChain, ReferenceAllowed, ConstrainReferences,
ResolveConnected, CheckConnectedMemberSpans và CheckNotchPairs. Nhánh mới
`CheckPlateMidplanes` chạy **sau** tất cả luật cũ để không tạo thêm companion
cho kiểm rãnh hoặc ảnh hưởng cứu biên neighbor.

- Metadata `PrimaryPlate` chỉ nhận ContourPlate primary không dummy và được
  sao chép cùng geometry global → view; không thay Allowed.
- Chỉ xét chuỗi hợp lệ có ERROR, không REVIEW/NOTCH_CONFLICT, không thiếu
  geometry; mọi chân phải có phương đo đồng nhất.
- REF cần ít nhất ba điểm không thẳng hàng trong 3D nhưng có cùng tọa độ
  theo phương đo. Như vậy chỉ nhận mặt plate nhìn cạnh, không suy tâm từ REF
  thẳng hoặc chấp nhận toàn bộ điểm nằm trên REF plate.
- Mọi đầu cạnh solid phải ở hai mức cực trị theo phương đo. Cạnh ở hai mặt
  phải ghép được theo cả tọa độ ngang và Z của view, cùng một owner.
- Chân phải nằm trên đoạn giữa hai cạnh thật, trong dung sai 0,04 mm không
  nhân scale. Lấy tâm từ solid, vì plate có thể offset so với mặt REF.
- Chỉ đổi ERROR đã có bằng chứng thành OK/PLATE_MIDPLANE, xóa gợi ý lỗi cũ;
  không sửa các chân đã OK hoặc feature nguồn.

Giới hạn có chủ ý: không bổ sung cho Beam profile PL, neighbor/dummy, plate
nhìn mặt/xiên, solid có thêm mức qua bề dày, geometry thiếu, hoặc chân dóng
đã kéo ra ngoài đoạn giữa hai mặt. Các trường hợp này theo kết luận cũ.

## Kiểm chứng

Mốc trước sửa là **working tree đang có**, build Release x64 riêng, không
lấy từ Git HEAD. Backup source/map có đối chiếu SHA-256 tại
`backup/005-check-dim-plate-midplane-20261014/`.

- Live audit trước/sau: **85 chân còn lại khớp toàn bộ trường dữ liệu**;
  đúng hai chân trên đổi ERROR → OK. Số chuỗi/chân/view, geometry cũ, Allowed,
  liên kết, cảnh báo, part metadata và drawing/model identity đều khớp.
- Hồi quy offline: **700 ca** trên cùng runtime trước/sau; mọi kết quả của
  pipeline cũ khớp. **384 ca ngoài phạm vi giữ nguyên kết quả cuối**;
  **316 ca tâm plate hợp lệ** đổi đúng dự kiến (gồm replay 87 chân thực tế).
  Bản trước đạt 1.561 assertions; bản sau đạt 2.895 assertions.
- Bao gồm bề dày 0,1/3/6/9/25/60 mm; offset; xoay/đối xứng/dịch xa;
  đảo thứ tự cạnh/chân; biên dung sai 0,039/0,041 mm; lệch tâm 1 mm;
  ngoài đoạn; nhìn mặt/xiên; REF thiếu/thẳng; ghép sai cạnh/Z/owner;
  neighbor/dummy; geometry thiếu; REVIEW; xung đột rãnh ngay tại tâm plate;
  mép/lỗ vốn đúng; đo tổng neighbor, REF giới hạn vùng nối, graph và DIM góc.
- Probe chỉ Prepare/VerifyConnection/Analyze và đọc metadata. Không chạy
  GUI, đổi selection/work plane, tạo/sửa/lưu/đóng bản vẽ hoặc ghi model.
  Dữ liệu audit/test nằm ngoài model trong `.codex-artifacts/plate-midplane/`.
- Build trước/sau Release x64 thành công, cùng 3 warning có sẵn ở dịch họp
  (CS4014, CS0169, CS0067), không phát sinh warning mới.
- Portable cập nhật bằng `Update-Portable.ps1 -BuildOnly -NoPause`; đối chiếu
  độc lập SHA-256 **40/40 file** với build stage. Chính runtime portable đã
  chạy lại đủ 700 ca / 2.895 assertions và live audit, cùng kết quả nêu trên.
  SHA-256 EXE: `2B8C1CFFAC1B5B668288ECCA23179536C27EC5E8E514B155390E5BA4CC3023BD`.
  Manifest kiểm local: `.codex-artifacts/plate-midplane/verified-runtime.csv`.
  BuildOnly giữ metadata phát hành cũ; không dùng `portable/SHA256.csv` để
  xác minh bản local này. Không commit/push/phát hành.

Đây là kiểm thực tế **chỉ đọc** trên bản nêu trên và kiểm hồi quy dữ liệu;
không khẳng định đã kiểm mọi loại bản vẽ hay chạy thao tác tạo/sửa DIM.

## Chạy lại

```powershell
pwsh -NoProfile -File tests/DimensionCheck/Run-Probe.ps1 -ProductPath portable -OutputPath .codex-artifacts/plate-midplane/current.xml -TeklaPid <PID>
pwsh -NoProfile -File tests/DimensionCheck/Run-Regression.ps1 -ProductPath portable -OutputPath .codex-artifacts/plate-midplane/regression-current.xml -Snapshot .codex-artifacts/plate-midplane/before.xml
pwsh -NoProfile -File tests/DimensionCheck/Compare-Audits.ps1 -Before .codex-artifacts/plate-midplane/before.xml -After .codex-artifacts/plate-midplane/current.xml
pwsh -NoProfile -File tests/DimensionCheck/Compare-Regression.ps1 -Before .codex-artifacts/plate-midplane/regression-before.xml -After .codex-artifacts/plate-midplane/regression-current.xml
```

Probe dùng Tekla 2025 tại `C:/TeklaStructures/2025.0/bin`; sửa đường dẫn test
nếu cài ở nơi khác. Hai script Compare cố ý xác minh mẫu đã chốt này; dừng
nếu người dùng đã thay đổi bản vẽ. Replay cần snapshot trước sửa; test là
công cụ kiểm ngoài runtime, không phải phụ thuộc để build/chạy sản phẩm.
