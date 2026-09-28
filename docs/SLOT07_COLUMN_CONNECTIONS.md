# Slot 07 — liên kết cột hộp, mép plate và trục

## Luồng và phạm vi

Slot 07 phân loại bằng MainPart assembly và REF trong tọa độ global. Cột hộp STKR/SHS/RHS/BOX đứng dùng nhánh `PHU_Slot07_ColumnConnections`; cột H/C/L đứng được chọn rõ ràng vào luồng cột Inzai gốc; dầm còn lại tiếp tục luồng dầm. Quyết định được tập trung trong `PHU_Slot07_DrawingRoute` trước khi mở context Beam Type1/Type2. Không dùng tên dự án, mark, ID cố định hoặc số đo mẫu để định tuyến.

Nhánh cột lập toàn bộ kế hoạch trước khi ghi: liên kết H/I, chuỗi dọc cột theo mép plate, tổng và Grid. Sau đó coordinator gọi nguyên bộ giải mặt cắt `PHU_InzaiColumnSectionDimensionEngine`. Luồng cột yêu cầu hai hình chiếu chính đứng hướng lên, các đoạn cột đồng trục có REF nối tiếp, plate ngang bao cột xác định duy nhất, trục đứng gần nhất duy nhất và hai cao độ tầng trong mỗi view. Mặt cắt được xử lý riêng. Trường hợp xiên/đảo view/thiếu hình học hoặc nhiều ứng viên tương đương sẽ báo rõ; không ép về số đo mẫu.

Slot 09 riêng đã bỏ; ô 09 trở lại chỗ trống. Không có hai bộ giải cột–dầm trùng nhau.

## Chân DIM đúng

`PHU_Slot07_ColumnGrid.ResolveColumnPlateFeet` dùng REF để tìm các node, rồi tìm chủ sở hữu và mép thật cho chân DIM:

- Chân đáy chuỗi: đỉnh solid bên trái trên mặt dưới base plate.
- Tại liên kết: mép thấp/cao của tiết diện nhánh xác định vai trò diaphragm dưới/trên. Xét cả nhánh nhìn ngang và nhìn đầu thanh qua tọa độ X/Z của view.
- Chọn đúng một plate ngang cùng assembly bao các đoạn cột kề node. Mỗi node phải có vai trò rõ; không dùng REF làm chân thay thế nếu plate thiếu.
- Diaphragm dưới lấy mặt ngoài thấp nhất; diaphragm trên lấy mặt ngoài cao nhất. Chọn đỉnh solid trái thực trên mặt đó, với thứ tự phụ theo chiều sâu để độc lập thứ tự enumerate.
- Chuỗi dùng mọi chân plate theo cao độ. Tổng lấy cao độ đáy thực trên trục cột tới mép ngoài plate trên cùng. Chuỗi tầng dùng cùng chân plate trên cùng và tọa độ GridPoint thật của hai trục tầng.
- DIM ngang trục: trục trùng tim thì mép ngoài–tim–mép ngoài; lệch thì REF–Grid. Chọn trục gần nhất, từ chối khi có hai trục gần bằng nhau.

Không gán cứng +3, +6, số lượng liên kết, tọa độ, mark hoặc ID. Một plate có thể thay đổi chiều dày và bề rộng; chân và trị số phải đi theo solid của plate đó. REF của đoạn MainPart đơn lẻ không đại diện tổng cột nhiều đoạn.

Hệ tọa độ: current work plane không đổi -> global -> DisplayCoordinateSystem. Chân DIM lưu ở hệ view. X–X/Y–Y là quy ước hướng nhìn người dùng; mã không đổi tên hoặc vị trí view để suy ra hình học.

## Không nhầm hai loại DIM

DIM 150/300 tại **đầu dầm** vẫn đo chiều cao tiết diện dầm và được giữ như bản người dùng duyệt. Phần sửa là **chuỗi dọc cột ở phía trái**, tổng cột và chuỗi lên trục tầng. Trong mẫu này, chuỗi plate là 156–644–306–1197; tổng 2303; đoạn lên tầng 3597; tổng tầng 5900.

Lỗi đọc trước đây: helper chỉ xuất set có text 150/300/2300, nên bỏ sót chuỗi người dùng đã sửa thành 156/306/2303 và tiếp tục thấy các DIM đầu dầm. Lệnh `--read-dimensions` hiện đọc TẤT CẢ set và mọi chân/đoạn, không lọc theo trị số. Kết quả `MISSING` của planner cũ chỉ có nghĩa là khác kế hoạch cũ, không chứng minh DIM người dùng bị thiếu hay sai.

## Ghi và bảo toàn

- Đọc/lập kế hoạch tất cả view trước ghi; kiểm tra ID active drawing trước từng lần tạo và commit.
- Chuỗi plate, tổng và chuỗi tầng đối chiếu cả tọa độ chân, không chỉ độ dài. DIM đã duyệt được giữ nguyên chân, khoảng đặt và ID.
- Chỉ thêm DIM thiếu. DIM chuỗi tắt combine, Modify/Select và kiểm tra lại thuộc tính; đọc lại số đoạn và chân DIM vừa tạo. Lỗi chỉ thu hồi DIM mới.
- Không xóa DIM cũ không có dấu sở hữu. Khi gặp đúng footprint REF sai từ bản slot 07 trước, dừng và báo xung đột trước ghi, để tránh vẽ chồng hoặc tự xóa DIM thủ công. Một footprint cũng khớp DIM Grid hợp lệ không bị coi là DIM REF lỗi.
- Phần mặt cắt dùng nguyên engine Inzai cũ; trạng thái được reset trong finally. Lỗi/xung đột mặt cắt không được báo hoàn tất toàn luồng.

## Kiểm chứng ngày 2026-09-25

Nguồn chuẩn: bản người dùng sửa tại model Sinjuku_Slope, [A-C12-4], Drawing ID 43760826. Front 2610 bên trái, Bottom 2859 bên phải, scale 15. Đây là metadata của phép kiểm chứng, không phải điều kiện thuật toán.

API đọc đầy đủ đã xác nhận ở cả hai view:

- Node chân plate: -19, 1178, 1484, 2128, 2284.
- Chuỗi từ trên xuống: 156 / 644 / 306 / 1197; tổng 2303.
- Plate trên -> tầng trên: 3597; tổng tầng: 5900.
- Chân đáy chuỗi Front ở X=-225; Bottom ở X=-150, theo bề rộng base plate thật. Các diaphragm ở X=-150.

Planner cuối khớp 39/39 kế hoạch với DIM hiện có; replay bỏ DIM trong DTO giữ nguyên mọi chân, hướng và khoảng đặt. Chạy coordinator của executable portable giữ nguyên đủ 39 DIM đã duyệt, thêm 25 DIM trên 4 mặt cắt. Lần hai tạo 0, reused 39+25, conflict 0. Đã so ID, chân từng đoạn, khoảng đặt của mọi DIM cũ; origin/scale/restriction box và work plane không đổi.

86 kiểm tra offline bao gồm: chuỗi chuẩn đủ chân, false positive cùng trị số nhưng bám REF, đổi số cụm/chiều dày/kích thước, base/diaphragm rộng khác nhau, đổi ID/mark/thứ tự, dịch/mirror/nhánh nhìn đầu, scale, lệch trục, nhiều trục/tie, thiếu/mơ hồ plate, sai assembly, REF giữa tiết diện, đoạn cột không nối tiếp, DIM REF cũ và tổng tầng trùng REF.

Bảo toàn Inzai: Splice 24/24; Section cardinal/mirror/partial và các topology B/C/D qua; ColumnGrid Type2 4/4; BeamGrid 1.236 checks với 72 chữ ký cũ không đổi; routing 23 assertions qua. 13 file geometry legacy đối chiếu SHA-256 với đầu phiên giống hoàn toàn. Các file Inzai/Shape/ColumnGrid gốc không sửa. Ở mốc 2026-09-25 chưa chạy live trên model Inzai. Kiểm chứng thực tế Inzai bổ sung ngày 2026-09-26 được ghi bên dưới; không tuyên bố đã thử mọi dự án thực tế.

Release x64 được cập nhật qua Update-Portable -BuildOnly; 39 runtime files được đối chiếu SHA-256. Không publish/push.

## Lệnh kiểm tra

```powershell
dotnet build tests/Slot07Connections/Slot07Connections.csproj -c Release
& ./.codex-artifacts/slot07-connections/tests/Slot07Connections.exe
# Read-only: mọi DIM thực tế, không lọc trị số
& ./.codex-artifacts/slot07-connections/tests/Slot07Connections.exe --read-dimensions all-feet.txt
# Read-only: planner và replay trong bộ nhớ
& ./.codex-artifacts/slot07-connections/tests/Slot07Connections.exe --audit plans.txt
# Read-only: audit Inzai và hồi quy geometry gốc
& ./.codex-artifacts/slot07-connections/tests/Slot07Connections.exe --legacy-audit 'portable/TTSK Dim Plates.exe' legacy.txt
# MUTATING: chỉ chạy trên drawing đã định danh và có chủ ý tạo DIM thiếu
# --apply-product <portable-exe> <expected-drawing-id> <report-path>
```
## Sửa gốc phân luồng Inzai ngày 2026-09-26

Lỗi xác nhận tại [C2-PC2-14], MainPart POST H294*200*8*12, model ATTOKPJ(Data_Center). REF global từ (0,38645,19318) đến (0,38645,22376): cột đứng. Hai view chính Front/Bottom, ba mặt cắt A/B/C, scale 20. Trước sửa bản vẽ có 0 bộ DIM.

Nguyên nhân: `RunDataCenterCurrentDrawing` chỉ tách cột hộp, còn H/C/L vẫn vào scope dầm Data Center. Scope đó ép Beam Grid và kích hoạt Beam Type1 Section. Shape H tự nhận hướng đứng nên tắt BeamGrid, trong khi coordinator vẫn đòi BeamGrid và hai REF connectors. Vì vậy giữ nguyên file geometry Inzai chưa đủ: luồng cột không được gọi, và thông báo cuối có thể chỉ hiện thông báo Type1 Section không phản ánh nguyên nhân.

Sửa tại lớp điều phối:

1. `PHU_Slot07_DrawingRoute` quyết định một lần trước mutation/context dầm, dựa vào profile classifier đang dùng và REF global MainPart: Beam / LegacyColumn / HollowColumn / UnsupportedColumn.
2. Cột H/C/L: gọi nguyên pipeline Shape -> Neighbor -> Splice -> ColumnGrid=3 -> Section -> alignment. Luồng này không yêu cầu Slot04/Top/Front REF connectors của dầm.
3. Các tham số grid/column của lần chạy được truyền trực tiếp; không sửa UI hoặc cấu hình bền vững. AutoSection và LoadStandard không chạy trong nhánh DIM cột Slot07 sử dụng view/mặt cắt đã chuẩn bị.
4. `ColumnGrid.PreflightPreparedGeometry(out message)` là gate read-only trả bool, dùng lại các reader/selector gốc để xác nhận hai view và Grid trước Shape replacement; không dò câu chữ audit để quyết định thành công. Chỉ thêm gate, không sửa công thức chân/tier của ColumnGrid.
5. Thành công yêu cầu Grid/layout và kết quả các pass cột thành công, không có conflict mặt cắt. Reset tất cả context trong finally, kể cả return sớm trước writer.
6. HollowColumn tiếp tục luồng plate đã kiểm chứng. Cột profile không hỗ trợ báo rõ, không tự coi là dầm. Dầm nằm ngang giữ pipeline Beam cũ.

Kiểm chứng trực tiếp bằng executable portable trên [C2-PC2-14]: 43 bộ DIM cuối = 18 Connection/terminal + 9 composite Shape/Grid + 16 section. Pipeline legacy thay đúng Shape DIM trung gian theo hợp đồng cũ. Lần hai có cùng số lượng, mọi chân/normal/distance giống lần đầu. View origin/scale/restriction box, work plane và các tùy chọn UI được giữ. Đã cố tình đặt UI Beam/Grid=1/AutoSection bật trong helper để xác nhận slot cột chọn đúng recipe mà không sửa những cài đặt đó.

Audit sau chạy: Splice 18/18 existing; Section 16/16 exact; typed Column preflight true và trường hợp context chưa chuẩn bị trả false. 94 kiểm tra offline gồm 86 trường hợp plate/liên kết và 8 trường hợp route mới; BeamGrid 1.236 kiểm tra/72 chữ ký cũ không đổi; project-routing scope 23 assertions qua. Các bộ giải Inzai, Shape và plate-grid không đổi; ColumnGrid chỉ có thêm gate read-only. Không mở lại model Sinjuku trong lượt này; kiểm tra plate-chain bằng hồi quy và đối chiếu source giữ nguyên.

Lệnh đọc hình học Inzai hiện tại: `--inzai-audit <portable-exe> <report-path>`.
Lệnh integration có ghi bản vẽ H/C/L: `--apply-inzai <portable-exe> <expected-drawing-id> <report-path>`. Đây là pipeline replacement của Shape legacy, không phải lệnh chỉ đọc; luôn xác minh ID trước chạy.