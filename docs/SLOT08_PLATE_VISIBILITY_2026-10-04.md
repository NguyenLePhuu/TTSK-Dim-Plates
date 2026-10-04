# Slot 08: plate ẩn riêng trong view và dầm reference qua Connection

> Phần support qua Connection ban đầu của `[C2-e-34]` chọn nhầm reference cũ. Đã bổ sung màu gốc + tiếp giáp 3D và fallback giữ/hiện cả nhóm, không dừng khi mơ hồ. Kết quả cũ bên dưới là lịch sử; xem kết quả cuối ở `SLOT08_OVERLAPPING_REFERENCE_ANALYSIS_2026-10-04.md` và `SLOT08_IMPACT_REVIEW_2026-10-04.md`. Bản sửa plate của `[C2-e-73]` là lỗi riêng; không suy rộng Connection = support đúng.

## Bằng chứng chỉ đọc

Đọc bằng kết nối mới vào Tekla 2025, bản vẽ Assembly `小梁 [C2-e-73]`, main `112007009`; cả Model và Drawing API trả kết nối thành công. Không gọi Run/Apply, không đổi selection/work plane, không ghi folder model.

| Plate | Quan hệ với main | Quan hệ với neighbor | Front 1120 | Top 1224 |
| --- | --- | --- | --- | --- |
| 119336702 | bolt 131131855 | weld 119336812 → 112006769 | đã ẩn | đang hiện |
| 119337652 | bolt 131130999 | weld 119337762 → 117046115 | đã ẩn | đang hiện |

Cả hai plate đều có `KEEP direct bolt/weld to primary assembly` từ thuật toán cũ. Vì Apply chỉ ẩn thêm và bỏ qua trạng thái ẩn sẵn, plate Front không được hiện lại. Snapshot không xác định thao tác lịch sử nào đã ẩn chúng.

Các plate 119337200/119338067 cùng nhãn `C2-GPz-4` thuộc mối nối của neighbor với thanh khác, nên vẫn phải ẩn. Không mở rộng graph hoặc giữ cả assembly neighbor để sửa hiện tượng này. ID trên chỉ là bằng chứng mẫu, không mã hóa trong sản phẩm.

## Phạm vi sửa

Chỉ sửa source sản phẩm `PHU_Slot08_NeighborCleanup.cs`:

- Giữ nguyên Resolve, CanFollow, BuildConnections, RetainLocalDummies và batch macro ẩn.
- ResolvePlateRestores là bộ lọc thuần dữ liệu. Plate phải nằm ngoài primary, không dummy, đang thuộc Keep, có cạnh trực tiếp tới primary không dummy và cạnh tới thép hình neighbor không dummy cũng thuộc Keep.
- Trong chính view chứa plate bị ẩn, phải đang hiện ít nhất một part primary không dummy và thanh neighbor có cạnh tới plate. Giữ nguyên trạng thái ẩn của primary, dummy, thép hình và plate không đủ bằng chứng.
- Analyze chỉ tạo kế hoạch và báo WouldShow; Check DIM gọi Analyze vẫn không ghi bản vẽ, graph không đổi.
- Apply kiểm lại plate đang ẩn trước khi ghi. Sau batch ẩn thành công mới hiện các plate kế hoạch bằng ShowInDrawingView + Modify, đọc lại trạng thái rồi commit. Không có target ẩn vẫn xử lý được plate cần hiện.
- Khi lỗi, đưa plate vừa hiện về ẩn và đưa target vừa ẩn về hiện; đọc lại và kiểm commit rollback. Nếu macro pending/preflight thất bại thì giữ chính sách cũ, không chạy phần hiện plate.
- LastRunMessage báo riêng số part ẩn và plate hiện lại; giá trị trả về Apply vẫn là số part ẩn để giữ tương thích.

Luồng gọi giữ nguyên: Slot 08 qua reflection; RunFitView → RunFitViewCore → RunNeighborCleanupAfterFit khi Fit thành công và active drawing không đổi. Không sửa Fit envelope, cách chọn view, routing MainForm hoặc thuật toán DIM khác.

## Kiểm chứng

- Release x64 bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`; 40 file runtime được xác minh SHA-256.
- 24 hồi quy offline: Front/Top, chạy lại, không có target ẩn, hidden/absent main và receiver, section chứa secondary, dummy, primary plate, thiếu cạnh, fallback keep, plate cùng assembly, tên thay đổi, đảo cạnh/thứ tự, nhiều connector và graph Check DIM.
- Audit trên portable mới dự kiến hiện đúng 2 plate tại Front; Top không có plate cần hiện. WouldHide vẫn là 11 như trước.
- Đối chiếu trước/sau audit: cùng identity bản vẽ, toàn bộ LINK/NODE/VIEWPART, quan hệ bolt/weld bổ sung và thông tin view giống nhau. Bản vẽ thực tế không bị sửa.
- Chưa thực thi Apply trên bản vẽ thật; đường ghi/rollback được build và rà source, chưa xác minh hành vi hiển thị thực tế bằng live test.

## Chạy lại kiểm tra

```powershell
pwsh -NoProfile -File tests/NeighborCleanup/Run-ReadOnly.ps1 -Offline -TeklaBinPath 'C:\Program Files\Tekla Structures\2025.0\bin'
pwsh -NoProfile -File tests/NeighborCleanup/Run-ReadOnly.ps1 -TeklaPid <PID đang mở>
```

Probe không phụ thuộc một PID cố định; lấy thời điểm bắt đầu tiến trình và DLL từ Tekla được chọn, Prepare + VerifyConnection trước khi đọc. Khi nhiều phiên Tekla, bắt buộc chọn PID. Offline chỉ cần đường dẫn DLL, không kết nối Tekla. Cache/công cụ sinh trong `.codex-artifacts/neighbor-cleanup`, có thể tạo lại; không thuộc runtime sản phẩm.

## Bổ sung: dầm phải của [C2-e-34]

Audit tiếp theo trên Assembly `小梁 [C2-e-34]`, main `117633970`, phát hiện một kiểu dữ liệu liên kết khác dù cấu tạo nhìn giống mẫu trước:

- Bulông `119741859` nối main tới plate `112256909`.
- Native Connection `112261832` có primary input là dầm reference `109028105` (`G16M SH1000*250*16*22`, `DUMMY-1210`), secondary input là chính plate `112256909`.
- Connection không cung cấp weld/bolt nối plate với dầm. Bulông `112261867` có hai đầu cùng là plate, chỉ là lỗ và không được coi là cạnh nối.
- Tọa độ được đọc qua current-plane → global → DisplayCoordinateSystem: main có X=0..995; dầm reference X=1005..1255; plate X=855..1122, xác nhận đây là mối nối bên phải ở cả Top/Front. Không dùng các giá trị/ID này trong resolver sản phẩm.
- Tại thời điểm audit, dầm `109028105` đang hiện, nhưng kế hoạch cũ sẽ ẩn nó trong cả hai view. Bản sửa chuyển đúng hai quyết định này sang giữ. Đây là lỗi thiếu quan hệ Connection của quy tắc cũ, khác với lỗi plate đã ẩn ở mẫu trước.

`RetainConnectionReferenceSupports` chỉ xét node dummy không plate ngoài primary. Phải chứng minh node là primary input thật của `Model.Connection`; secondary input phải là plate không dummy có cạnh bolt/weld thật tới part primary không dummy. Children của component, father chung, cùng assembly hoặc khoảng cách gần không đủ. Plate nằm trong primary cũng phải có cạnh thật, không chỉ là thành viên assembly.

Quan hệ đã chứng minh được lưu riêng trong `ReferenceSupportPlates` và thêm Keep cho đúng support; không thêm Edge, không mở rộng BFS qua dummy. Sau rà soát ảnh hưởng, quy tắc dummy cũ chạy trước, nhánh mới chỉ xét support chưa được Keep và chỉ công bố bằng chứng sau khi toàn bộ lần đọc thành công. Lỗi đọc không đổi quyết định cũ. Nếu support mới đã ẩn thì `ResolveReferenceSupportRestores` chỉ hiện lại trong view có primary và plate bằng chứng đang hiện. Part reference khác như `130973315` không có bằng chứng Connection này nên không được giữ theo quy tắc mới. Không tự khôi phục mọi dummy.

### Phạm vi ảnh hưởng đã đối chiếu

| Nơi dùng | Tác động |
| --- | --- |
| Slot 08 trực tiếp | Giữ/khôi phục đúng reference support đủ bằng chứng |
| RunFitView → RunNeighborCleanupAfterFit | Nhận cùng bản sửa qua entry point cũ, không đổi gating/active drawing guard |
| PHU_FitNeighborEnvelope / PHU_OpenGridView | Không sửa cách tính biên, tọa độ hoặc chọn view |
| PHU_DimensionCheck.Analyze / ResolveConnected | Graph Nodes/Edges và phân loại Dummy giữ nguyên; Keep primary không đổi, quan hệ DIM không mở rộng |
| RetainLocalDummies / Resolve / CanFollow | Quy tắc cũ chạy đầy đủ trước; nhánh mới chỉ thêm support chưa được Keep; không ghi đè Keep/reason cũ, chỉ bỏ cảnh báo thiếu receiver của plate đã chứng minh |
| Batch macro và xử lý pending | Giữ nguyên; phần hiện reference support dùng đường hiện/đọc lại/rollback của bản sửa plate |

Build Release x64 và portable: 40 file runtime khớp SHA-256. Tổng 47 hồi quy offline, gồm toàn bộ 24 ca trước và 23 ca mới kiểm input Connection, lỗ self, thiếu cạnh, dummy/primary, đổi thứ tự/tên, trạng thái view và bảo toàn graph Check DIM. Audit hiện tại: WouldHide giảm 7 → 5, WouldShow=0; chỉ `109028105` ở hai view đổi HIDE → KEEP. Toàn bộ LINK/NODE/VIEWPART/BOUNDS/COMPONENT/CONNECTION và trạng thái ẩn thực tế trước/sau audit giống nhau.

Chưa chạy Apply ghi bản vẽ thật; việc giữ/hiện trong native UI và rollback vẫn chưa được live-test. Không ghi file/thư mục vào folder model, không commit/push/phát hành.

Rà soát sâu tiếp theo và giới hạn bằng chứng: `docs/SLOT08_IMPACT_REVIEW_2026-10-04.md`.
