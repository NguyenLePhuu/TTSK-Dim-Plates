# Check DIM: hai cách bắt chân đo tổng hợp lệ

## Bằng chứng nguyên nhân

Đọc API chỉ đọc trên `[C2-PC2-7]`, mặt cắt C (view 343373), 2026-10-05 JST.
53 chuỗi / 135 chân / 5 view; trước sửa có đúng 2 ERROR.

| DIM | Chân dưới | Chân trên | Nghĩa đo |
| --- | --- | --- | --- |
| 343519 | (110, -125) | (100, 0) | Mép dưới neighbor → mốc primary cùng cao độ mép trên |
| 492560 | (-110, -125) | (-110, 0) | Hai mép ngoài neighbor |

Đơn vị mm trong View.DisplayCoordinateSystem; không dùng pixel ảnh.
Hai neighbor 120947188/120947149 có profile H125*125*6.5*9, liên kết bolt thật
với plate primary 121489987/121489955. Solid có biên ngoài Y=-125 và Y=0;
mép trong Y=-116/-9. REF thẳng dọc X. Hai chân dưới nằm chính xác trên cạnh
ngoài neighbor. Chân trên được classifier cũ chấp nhận, trực tiếp hoặc qua phép chiếu.

Lỗi cũ: Extract chỉ cho phép mép/vertex/hole primary, neighbor chỉ được dùng
REF có ràng buộc. Classify từng chân không biết chuỗi đang đo **toàn chiều cao
của thép liên kết** nên báo sai mép dưới; mốc primary gần nhất lệch 12,5 mm
không phải ý định đo. Đây không phải lỗi làm tròn hoặc dung sai.

## Phạm vi sửa

Giữ Classify, ResolveConnected, ConstrainReferences và thuật toán tạo DIM.
Thêm `CheckConnectedMemberSpans` giữa classifier và `CheckNotchPairs`:

1. Chỉ chuỗi hai chân đủ dữ liệu, phương đo hợp lệ, đang có ERROR; bỏ REVIEW.
2. Cùng một thép neighbor có bằng chứng liên kết thật và REF thẳng, không suy
   từ tên/profile/ID hoặc gần bbox. Đo vuông góc trục trong view, không đo chiều dài.
3. Hai tọa độ đo phải trùng hai cực trị solid của **cùng owner**; mỗi cực trị
   phải có cạnh thật giữ nguyên tọa độ theo phương đo.
4. Mỗi chân ERROR được cứu phải chạm cạnh ngoài thật. Chân dóng còn lại phải
   đã OK theo classifier cũ và cùng tọa độ cực trị đối diện.
5. Chỉ đổi verdict của chân có proof; không bật Allowed cho feature neighbor.
   CandidateOwners là chứng cứ hình học, không khẳng định associativity Tekla.

Không mã hóa 125, H125, tên C, ID hay tọa độ mẫu trong source sản phẩm.
Dung sai giữ 0,04 mm hình học, không nhân scale. Không tự chấp nhận mép trong,
điểm nằm giữa cạnh dọc, lỗ neighbor, dummy, plate REF, REF cong/đầu nhìn thẳng,
chuỗi nhiều chân hoặc dữ liệu thiếu. Các trường hợp này theo kết luận cũ.

CheckNotchPairs giữ nguyên phép quyết định; khi báo NOTCH, evidence/residual
được cập nhật theo mâu thuẫn thay vì để lại nhãn EXACT từ classifier.
Báo người dùng giữ đồng thời phía chuỗi, đoạn đo và vị trí chân. Với lỗi thiếu
mốc, có tọa độ hiện tại, độ lệch, owner/feature tham khảo và tọa độ dóng gần nhất.
Đây là gợi ý để đối chiếu, không tự sửa và không khẳng định điểm định bắt.

## Kiểm chứng

- Probe chỉ đọc kết nối đúng API và active drawing; không Modify/Insert/Delete/
  CommitChanges/save, không đổi selection/work plane, không tạo file trong model.
- Trước/sau trên cùng bản vẽ: 53 chuỗi, 135 chân; chỉ hai ERROR trên đổi thành OK
  với `CONNECTED_MEMBER_OVERALL`; 133 chân OK còn lại giữ nguyên.
- Hồi quy thuần: hai cách đo, 75/125/294 mm, xoay/đối xứng/dịch chuyển/đảo thứ tự;
  lệch 1 mm và biên dung sai; mép trong; thiếu/ref cong/ref end-on/sai phương;
  dữ liệu thiếu, chuỗi nhiều chân, điểm chỉ chiếu không chạm mép; quy tắc rãnh,
  đo tổng main, projected vertex, dummy/plate REF, khoảng đặt âm.
- Kết quả: 551 assertions Check DIM (gồm replay 135 chân), 47 regression graph/
  neighbor và 43 regression overlap/contact đều đạt.
- Release x64 .NET Framework 4.8 / C# 7.3, portable cập nhật bằng BuildOnly,
  SHA-256 từng runtime được workflow đối chiếu. Không commit/push/phát hành.
  Kiểm độc lập lần cuối 40 file portable khớp build stage; kết quả lưu ngoài model
  tại `.codex-artifacts/dimension-check/verified-runtime.csv`. BuildOnly giữ nguyên
  metadata phát hành cũ (`portable/SHA256.csv`, `release.json`), không dùng chúng
  để xác minh bản build local này.

Chạy offline (không cần Tekla đang mở):

```powershell
pwsh -NoProfile -File tests/DimensionCheck/Run-ReadOnly.ps1 -Offline -TeklaBinPath '<Tekla 2025 bin>'
```

Chạy audit active drawing (chỉ đọc):

```powershell
pwsh -NoProfile -File tests/DimensionCheck/Run-ReadOnly.ps1 -TeklaPid <PID> -Name audit
```

Output trong `.codex-artifacts/dimension-check/`. Replay snapshot `before.xml`
là kiểm bổ sung cho mẫu này nếu còn file; các fixture thuần không phụ thuộc cache.
Chưa chạy lệnh tạo/sửa DIM thật; live audit mới xác minh trên bản vẽ nêu trên.
Không thể suy ý định kỹ thuật của mọi kích thước từ số đo hoặc hình học đơn thuần.
