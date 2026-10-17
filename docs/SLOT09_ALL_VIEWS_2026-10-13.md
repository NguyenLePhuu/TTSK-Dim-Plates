# Slot 09 — kiểm trục mọi view, giữ bộ tìm góc view chính

## Chuẩn section theo hướng chính X/Y — bản bàn giao mới nhất

**Rút lại kết luận C2-e-44 Section A sai ở mốc bên dưới. Người dùng đúng:** bản này theo hướng Y nên mặt cắt phải được xét theo trục ngang −X, không phải +X. Lần sửa “chuẩn luôn +X/+Y” đã bỏ mất ngữ cảnh hướng chính và gây báo sai giả. Việc không tự đảo chuẩn theo view đang kiểm vẫn cần giữ, nhưng chuẩn phải lấy từ hệ X/Y đã chọn của chính bản vẽ.

Quy tắc đầy đủ tái dùng chính `Geometry.Targets(false,alongY,key)[1]` đã được bộ góc xác nhận:

| Phương án/hướng | Trục dọc U | Trục ngang V | Mặt đứng dọc | Section ngang | Mặt bằng |
|---|---|---|---|---|---|
| X / cột theo quy ước X trái–Y phải | +X grid | +Y grid | Right=U, Up=Z | Right=V, Up=Z | Right=U, Up=V |
| Dầm Y | +Y grid | −X grid | Right=U, Up=Z | Right=V, Up=Z | Right=U, Up=V |

V tương ứng Z×U. Pháp tuyến chỉ chọn họ mặt chiếu trong hệ (U,V,Z), không quyết định dấu target. Chuẩn vì thế độc lập với view đang sai nhưng vẫn giữ đúng ngữ cảnh X/Y. Chỉ đổi `CheckViewAxes`; source ngoài method này nguyên byte, bao gồm Geometry, solver góc, AutoApply và UI gọn.

Đọc thật C2-e-44: Front Right=+Y/Up=+Z; Top Right=+Y/Up=−X; Section A #239586 Right=−X/Up=+Z. Ba frame nhất quán. BXa có X=0, origin cấu kiện X=−1287,5; chiếu vào Right=−X cho −1287,5 mm, tức **BXa bên trái** trên section. Không có cơ sở báo section này sai.

Đối chứng 2-BK-1: snapshot Section B #313217 đã lưu có Right=−Y/Up=+Z dưới ngữ cảnh X/cột, vẫn sai vì cần +Y. Đảo riêng section đó thành +Y thì đúng; đổi chiều section không làm đổi bộ góc đề xuất. Khi đọc lại ngày 13/10, bản 2-BK-1 hiện đã trở về Section A #279123 Right=+Y và không còn B trong snapshot mới. Bộ kiểm giữ **case-2-BK-1.json** cũ riêng, không thay thế ca lỗi bằng trạng thái thuận mới.

Kiểm trước bàn giao:

- Ca thật C2-e-44 + kỳ vọng −X đúng thất bại trên EXE trước sửa, đạt sau sửa. Thử đảo thành +X trong RAM phải sai. Kiểm đồng thời ca 2-BK-1 −Y sai/+Y đúng, không sửa model để làm test đạt.
- 21.834 assertions đạt: 3.840 ca ma trận góc, 1.344 tư thế trục theo bảng X/Y, chiều nhìn thuận/ngược, quay giấy, biên dung sai và snapshot thật.
- 2.560 kết quả solver cũ giống nguyên byte; so hai assembly trước/sau trên cùng snapshot năm bản hiện tại và snapshot Section B cũ cũng giữ nguyên toàn bộ kết quả solver chính.
- Đọc lại năm bản bằng API không mở/đóng/lưu: C2-e-44, C2-e-28, A2-a-30, 2-BK-15 và 2-BK-1 ở trạng thái hiện tại đều giữ các view đúng. Snapshot B ngược của 2-BK-1 vẫn Misaligned. Chỉ kết luận sai giả của C2-e-44 đổi từ Misaligned thành Correct so với bản trước lượt này.
- 28 assertions UI light/dark đạt, gồm C2-e-44 đúng, section đảo +X sai, góc hiển thị không đổi; khung gọn không đổi. Ảnh UI dùng snapshot API với thuộc tính fixture, không thay cho đọc native.
- Trong lúc làm việc người dùng đổi active sang C2-e-26. Luồng `KeyplanApply.Run(AutoApply=false)` đọc native trên C2-e-26: Model, gợi ý 0/0/0, cả ba view đúng. Kiểm frame trước/sau nguyên vẹn. Không tuyên bố đã mở hộp native của C2-e-44 trong lượt này; C2-e-44 được đọc bằng Drawing API khi đóng.
- Build Release x64/net48, 40 runtime file khớp staging, 41/41 SHA-256 đạt. EXE: `27AC7F1325C06102B885C1AC97C4A3D4B82FA68974FC3032B6B5530E31762EB6`.

Log/source trước lượt này ở `.codex-artifacts/keyplan-section-context`; bộ kiểm `tests/KeyplanViews`. Không ghi thư mục model, tạo macro, sửa bản vẽ, commit/push/phát hành. Các kết luận/test/hash ở mốc lịch sử dưới đây phải đọc theo ngữ cảnh thời điểm, đặc biệt kết luận C2-e-44 sai đã bị thay thế.

## Lịch sử sửa section đảo trục — trước khi bổ sung ngữ cảnh Y

Người dùng phát hiện Section B bên phải trên 2-BK-1 có BY2 → BY1 nhưng vẫn được báo đúng. **Đây là lỗi phần kiểm từng view mới thêm, không phải lỗi đọc API hoặc lượt thu gọn giao diện.** So source trước/sau thu gọn: toàn bộ phần ngoài form giống nguyên byte. Bộ test trước đó sai tiêu chí khi chấp nhận phía nhìn đối diện như một chuẩn hợp lệ, vì vậy không chứng minh được việc giữ chiều tăng nhãn grid.

Đo thật chỉ đọc:

- Section B #313217: Right=(0,−1,0), Up=(0,0,1), Normal=(−1,0,0).
- Grid Y: BY1 tại 0, BY2 tại 13900, direction=(0,1,0). Vector BY1→BY2 chiếu lên Right của section bằng −13900: chiều hiển thị đang ngược chuẩn người dùng.
- Code cũ lấy `key.Up * sign(normal.X)` làm target. Khi normal.X âm, target cũng đổi sang −Y và tự chấp nhận chính view đang ngược.

Sửa **chỉ `Reader.CheckViewAxes`**: dùng pháp tuyến để nhận họ mặt chiếu, không dùng dấu của pháp tuyến để đảo chiều trục chuẩn. Họ YZ luôn phải +Y và lên +Z; họ XZ phải +X và lên +Z; mặt bằng giữ quy ước X/Y của keyplan. Nếu phía nhìn ngược chuẩn thì báo sai và cần đổi phía nhìn, không đưa góc quay giấy vì quay giấy không đảo pháp tuyến. Geometry, dữ liệu góc, solver, native Apply và giao diện gọn giữ nguyên byte ngoài method này. Không thêm lại Recreate/Freeze guards.

Kiểm trước bàn giao lần sửa này:

1. Test kỳ vọng mới chạy trên EXE cũ thất bại ở ca đảo phía nhìn; không dùng code đang sửa để sinh đáp án kỳ vọng.
2. **1.344 tổ hợp đầy đủ**: 24 tư thế trục vuông góc × 4 hướng grid (gồm 37,25°) × 2 quy ước X/Y × 7 loại view. Oracle dựa vào thứ tự hai điểm trục có nhãn và chiều tăng cố định, không theo dấu normal của view.
3. Thêm quay giấy, đảo riêng trục ngang/trục đứng, đảo cả hai, ngưỡng ±0,00009°/±0,00011°, view xiên, chưa đọc được, unsupported. **21.825 assertions** đạt, gồm 3.840 ca ma trận kiểm bộ tìm góc đã có.
4. **2.560 kết quả solver cũ** giống nguyên byte. Chạy lại EXE trước/sau với snapshot API hiện tại của 2-BK-1 và bốn bản cũ: tất cả kết quả solver chính giống nhau. Đây là so kết quả trên cùng dữ liệu; không gán giả giá trị góc lưu cho bản đóng.
5. Đọc được các bản cũ bằng Drawing API khi chúng đang đóng, không SetActiveDrawing/Save/Close/Modify. Trạng thái hiện tại đã khác lượt quay lịch sử ở một số bản, nên không gọi đây là chạy lại 320 frame lịch sử đã bị dọn.

| Bản | Kết quả kiểm từng view sau sửa trên dữ liệu thật hiện tại |
|---|---|
| 2-BK-1 | Top và Section A đúng; Section B sai (−Y) |
| 2-BK-15 | Cả 3 view đúng, giữ nguyên |
| A2-a-30 | Cả 4 view đúng, giữ nguyên |
| C2-e-28 | Cả 3 view đúng, giữ nguyên |
| C2-e-44 | Front/Top đúng, giữ nguyên; Section A #239586 sai (−X), cũng từng bị lỗi cũ báo đúng |

Trong 16 view hiện có của năm bản, 14 kết quả đúng giữ nguyên; chỉ hai kết luận đúng giả ở section đảo chiều chuyển thành sai. Đã chạy **luồng đầy đủ `KeyplanApply.Run(AutoApply=false)`** trên 2-BK-1: đọc native Model, gợi ý vẫn Local 270/0/0, báo Section B sai, không Apply/Modify; so frame trước/sau không thay đổi. UI light/dark: **22 assertions** đạt, thông báo gọn **Sai trục: Section B**. Không xoay hoặc sửa bản vẽ người dùng để làm test đạt.

Build Release x64/net48 thành công; 40 runtime file khớp staging, manifest **41/41 SHA-256**. EXE: `6272459A57F48EFB30759F19F361193F86C732671D1C01CCADC37F659B20C2F2`. Công cụ và dữ liệu ở `tests/KeyplanViews` / `.codex-artifacts/keyplan-axis-sign` / `.codex-artifacts/keyplan-views`, không ghi model. Không commit/push/phát hành. Phạm vi kiểm được nêu cụ thể, không khẳng định mọi dạng hình học tùy ý đều đã test.

Lệnh bổ sung: `Run.ps1 -Mode audit-history`, `-Mode audit-report`, `-Mode evidence -Baseline`, `-Mode evidence`. Evidence dùng đúng hai EXE trước/sau cùng snapshot để chỉ ra hai kết luận thay đổi; dữ liệu chi tiết trong `evidence-before.txt`, `evidence-after.txt`, `main-live-before.txt`, `main-live-after.txt`.

## Cập nhật giao diện theo phản hồi người dùng

Đã bỏ DataGridView/bảng kết quả và hàng layout tăng chiều cao. Khung trở về bố cục gọn trước khi thêm bảng. Kết quả thành công chỉ ghi **✓ Đúng trục theo keyplan**. Nếu sai, ghi tên view, ví dụ **Sai trục: Section A (trên)**; view trùng tên được phân biệt bằng vị trí trên giấy khi có căn cứ. Hiển thị tối đa hai tên cùng số view còn lại; tooltip chứa đủ danh sách và nguyên nhân. Chưa xác minh vẫn ghi riêng, không báo đúng nhầm. Không hiện ID hoặc các vector trong thông báo chính.

Chỉ đổi region form: toàn bộ code hình học, đọc/nhận diện view, tìm góc và áp dụng trước region UI được đối chiếu nguyên byte không đổi so với trước lượt thu gọn. Kiểm lại form light/dark bằng fixture: **22 assertions** đạt, gồm lỗi Section, tên trùng, 23 view không tăng chiều cao và xóa thông báo cũ. Không đọc/ghi Tekla trong lượt thu gọn. Build Release x64 thành công, 40 runtime file khớp staging, **41/41 SHA-256** đạt. EXE: `08FCE0E66AF906243CBEB0B51AB34D3F15C8CFAD1688DB0B6DEE3FE0E3827835`.

Phần dưới ghi lại thiết kế và kiểm chứng thuật toán ở mốc trước thu gọn; bảng từng view đã được bỏ theo yêu cầu mới.

## Yêu cầu và nguyên nhân

Người dùng yêu cầu thông báo đúng/sai phải kiểm toàn bộ view trên bản vẽ, kể cả section/detail. Bộ đề xuất góc phải giữ nguyên: lấy view chính như phiên bản đã kiểm chứng, không đưa section vào tìm góc chung.

Đọc thực tế **[2-BK-1]** trên Tekla **2025.0.56843.0**, model `D:\27-011)ATTOKPJ(Data_Center)`, main part ID 111613135 (PLATE). Có đúng ba view; reader cũ loại toàn bộ section/detail nên chỉ còn một Top. `CompleteViews` của bộ góc là false, và UI dùng cờ đó để kết luận toàn bản vẽ thiếu view. Đây là lỗi trộn hai phạm vi kiểm.

| View thật | Hướng Phải | Hướng Lên | Kết quả mới |
|---|---|---|---|
| Top #263272 | +X grid | +Z model | Đúng trục |
| Section A #272146 | +X grid | +Y grid | Đúng trục |
| Section A #279123 | +Y grid | +Z model | Đúng trục |

Hai section có cùng tên A, vì vậy giữ ID để phân biệt. Những vector trên được đọc qua `DisplayCoordinateSystem`, không suy từ ảnh. Native Model/Drawing API đều kết nối thành công với đúng PID/start/session; helper net48/x64 có AppConfig Patcher. Chỉ đọc, không mở/đóng/lưu/quay bản vẽ, không tạo DIM, không tạo macro, không viết vào model.

## Thiết kế

- `Snapshot.Views` giữ nguyên tiêu chí cũ: view không phải section/detail và có main part. `Snapshot.AllViews` thu mọi `Drawing.View` từ sheet. Hai danh sách độc lập về phạm vi; view phụ không tham gia solve.
- `Reader.AnalyzeMainViews` là thân thuật toán `Analyze` cũ. Public `Analyze` gọi nó rồi thêm `ViewChecks`; không gán lại góc, State, CanApply, hướng/layout hoặc ánh xạ cặp view của bộ góc.
- `CheckViewAxes` dùng pháp tuyến để chọn **họ** mặt chiếu so với grid đã chọn và góc hiệu chỉnh, sau đó dùng chuẩn chiều trục cố định và `Geometry.KeyplanError` để kiểm. Không đảo target theo dấu normal hiện tại. Các mặt đứng phải theo +X/+Y và giữ +Z hướng lên; mặt bằng theo quy ước X/Y. Đổi tên hoặc số thứ tự view không đổi kết quả hình học. Xem phần sửa lỗi đầu tài liệu; các số liệu ở mốc đầu dưới đây là lịch sử, không thay cho kiểm đảo chiều bổ sung.
- Nếu view có thể sửa bằng quay trên giấy, ghi rõ góc **xoay riêng view**, không trộn với Around X/Y/Z của thuộc tính bản vẽ. Trục xiên giữa hai phương không có chuẩn duy nhất, frame không đọc được hoặc view phản chiếu/khai triển/undeformed được đánh dấu Unverified. Không coi Unverified là Passed.
- Với section, kiểm chiều trục theo mặt cắt đang có. Đây không phải kiểm tính đúng đắn vị trí đường cắt, độ sâu cắt hay ý đồ chọn mặt nhìn; những thông tin đó không được suy từ riêng frame.
- `VerifyApplied` giữ kiểm góc/cặp view chính cũ, thêm báo cáo toàn bộ view từ snapshot sau ghi. Giữ đúng keyplan đã xoay khi đọc lại.
- UI có dòng tổng **Toàn bản vẽ** và bảng từng view. Tất cả dòng được giữ, bảng cuộn khi nhiều view; tổng xanh chỉ khi toàn bộ đều Correct. Bên dưới ghi **GÓC VIEW CHÍNH · LOCAL**, cảnh báo thiếu cặp chỉ nói về góc view chính. Cửa sổ giới hạn theo vùng màn hình và cuộn khi cần.
- Không khôi phục hai xử lý Recreate/Freeze mà người dùng đã yêu cầu gỡ. Không thay đổi Geometry/Search/LocalView, luồng native Apply hoặc các slot khác.

## Bảo toàn kết quả đã kiểm

Đầu lượt, `tests/KeyplanLive`, `tests/KeyplanViewStatus` và JSON 320 frame trước đây đã không còn trong workspace. Chỉ còn báo cáo lịch sử; không tuyên bố đã chạy lại các file đo đã bị dọn.

Để kiểm phiên bản hiện tại:

1. Lưu đúng source và EXE trước sửa vào `.codex-artifacts/keyplan-views/before` (không phải backup bản vẽ).
2. Chạy hai assembly trước/sau trong hai process độc lập với **2.560 trường hợp**: bốn dạng basis cột/dầm tương ứng mẫu lịch sử, 32 góc vuông + 8 Z lẻ, Local/Model, 0/1/2/3 view chính, grid 0°/37,25°. Toàn bộ góc, trạng thái, CanApply, hướng/layout, ánh xạ và thông báo của bộ góc khớp nguyên byte.
3. Đối chiếu nguyên source region Geometry: không đổi. Kiểm **3.840 ca ma trận độc lập** cho năm basis 3D, mọi thứ tự cặp Front/Top/Back/Bottom, X/Y rời rạc và Z vuông/lẻ; kết quả forward/inverse đạt.
4. Kiểm section/detail/user view, sáu mặt nhìn, grid xoay, view quay 0/90/180/270, hướng chiếu xiên, lỗi đọc, reflected/undeformed, view không chứa main, tên trùng, giữ solver khi thêm view phụ và kiểm sau Apply với bốn góc hiệu chỉnh keyplan. Tổng bộ regression **20.237 assertions** đạt.
5. Form WinForms thật với dữ liệu RAM light/dark: **16 assertions** đạt; đếm 3/3, báo section sai, giữ đủ 23 dòng khi cuộn, không tích lũy chiều cao, xóa kết quả cũ, không tràn màn hình. Ảnh UI dùng frame thật đã đọc nhưng thuộc tính Model/0/0/0 là fixture; không coi ảnh fixture là đọc thuộc tính native hiện tại.
6. Live chỉ đọc lại 2-BK-1 bằng portable mới: AllViews=3, Views=1, ViewChecks=3 và cả ba Correct. Kết quả góc vẫn xử lý một Top như trước.

```powershell
pwsh -NoProfile -File tests/KeyplanViews/Run.ps1 -Mode audit
pwsh -NoProfile -File tests/KeyplanViews/Run.ps1 -Mode golden -Baseline
pwsh -NoProfile -File tests/KeyplanViews/Run.ps1 -Mode golden
pwsh -NoProfile -File tests/KeyplanViews/Run.ps1 -Mode regression
pwsh -NoProfile -File tests/KeyplanViews/Run.ps1 -Mode ui
```

Regression/UI dùng snapshot `active-audit.json` ở artifacts; audit tạo lại bằng đọc bản đang mở. `-Baseline` cần EXE trước sửa. Sản phẩm không phụ thuộc thư mục tests/artifacts.

Build Release x64/C# 7.3/net48 bằng `Update-Portable.ps1 -BuildOnly -NoPause` thành công; 40 file runtime khớp staging, manifest SHA-256 kiểm đủ 41 mục. Không commit/push/phát hành. Những thay đổi MainForm/PDF/Program có sẵn từ lượt khác được giữ nguyên.
