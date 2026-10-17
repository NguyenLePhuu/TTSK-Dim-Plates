# Điều phối lỗ mép assembly theo ký hiệu Dummy

## Phạm vi

`PHU_Shape.cs`, Front của assembly H/I ngang và dọc: cụm tại hai đầu có ký hiệu Dummy dùng DIM đại diện; cụm không có ký hiệu dùng đủ chuỗi hàng/cột. Theo yêu cầu bổ sung của chủ dự án, **Dummy chỉ có giá trị tại hai mép thanh**, không kiểm/áp dụng cho lỗ giữa. Giữ các hàm nền, Top/Bottom, section, rãnh, tổng, Slot03 và các engine C/L/Box. Không thêm setting hay trạng thái static vào sản phẩm.

## Quy tắc Dummy ban đầu (nhánh ngang được thay thế ở mục 2026-10-12)

1. Đọc Drawing.Part trong đúng view, map bằng ModelIdentifier. Snapshot duy nhất cho lần gọi, không ghi model. Điểm solid đi từ work plane hiện tại → global → DisplayCoordinateSystem; giữ Z khi phân loại.
2. Ký hiệu dùng token BJ kèm số, JOINT hoặc JOYCON từ name/material/position. Không hard-code BJ12z. Riêng `PART_POS=DUMMY-*` không đủ: trong model này cả thép/tấm tham chiếu cũng dùng tiền tố đó.
3. Giữ nhóm phi/M và splitter mép/giữa hiện có; quyết định trên từng BoltGroup ở từng đầu. Ký hiệu phải **chạm mặt đầu thanh** trong dung sai 2 mm model, không phụ thuộc scale giấy. Tâm ngang ký hiệu nằm trong vùng bao hàng/cột của cùng BoltGroup, kể cả giữa hai hàng; chiều sâu ký hiệu phải bao vị trí Z của nhóm. Cột/hàng gần mép nằm trong `FRONT_END_HOLE_ZONE` có sẵn (300 mm). Ngang dùng trục dọc X; dọc dùng Y. Ký hiệu ở giữa thanh bị loại dù cùng hàng với lỗ.
4. Một Dummy chỉ ghép nhóm phù hợp gần đầu nhất; nếu nhiều nhóm hòa trong TOL=1 mm thì giữ đầy đủ lỗ. Sắp snapshot theo phi/M/hình học trước khi phân nhóm và cấp tầng; không dựa thứ tự API. Các nhóm độc lập không bị gộp rồi mất một nhóm khi một nhóm có Dummy và nhóm kia không có. Lỗ giữa không tham gia ghép Dummy; từ bản sửa 2026-10-12, mẫu hàng cùng phi/M lặp ít nhất ba cụm ngoài vùng mép dùng chuỗi ngang đầy đủ như mục bổ sung bên dưới. Những trường hợp giữa còn lại giữ đường cũ.
5. Có ký hiệu: gọi writer assembly đại diện nguyên bản. Không có, với H/I ngang: tách từng mẫu hàng/cụm rồi gọi nguyên `CreateFrontHoleXDimsByGeometry` và `CreateFrontHoleYDimsByGeometry`. Việc tách tránh single suy nhầm cụm thứ hai cùng đầu thành mép đối diện. Lấy đủ cột X/hàng Y, giữ chân dóng và khoảng cách cũ.
6. H/I dọc không có Dummy: `BuildAssemblyVerticalFullHolePlan` tạo toàn bộ chân trước khi ghi. Chuỗi ngang gồm mép trái → các cột X → mép phải, đặt trên cho đầu trên/dưới cho đầu dưới, lấy hàng xa đường DIM. Chuỗi dọc từ mép đầu thật → từng hàng Y vào trong, luôn đặt trái, chọn cột phải để đường dóng phủ cụm. Reuse `ResolveAssemblyVerticalMemberHoleOuterEndEdge` để giữ neo ngoài thật tại rãnh trái. Điểm không hợp lệ trả về legacy trước khi tạo bất kỳ set mới của nhánh này.
7. Tầng tăng theo DIM được tạo; hai đầu có bộ đếm riêng, cùng dùng tầng nếu không chồng vùng. Bỏ qua tầng dành cho rãnh; trả tầng cao nhất thực sự đã tạo cho tổng. Lỗi/null của một writer mới không làm thoát toàn bộ Front hoặc bỏ DIM tổng; không coi set thất bại là đã chiếm tầng.
8. Lỗi đọc snapshot không được coi là “không có Dummy”: giữ nhánh đại diện cũ. Thiếu BoltGroup/điểm không hữu hạn làm hủy phân tuyến mới cho view. Nếu splitter cũ gán phía khác hình học hoặc nhóm vượt qua nửa đối diện của thanh, giữ toàn bộ nhánh cũ. Khi tất cả nhóm có Dummy, gọi nguyên entry legacy với toàn bộ input để giữ kết quả cũ.

Đây là ghép ký hiệu bằng metadata và hình học 3D; không đòi Dummy cùng assembly hay có bolt trực tiếp vì ký hiệu BJ của mẫu là part độc lập không có bolt. Ký hiệu không chạm đầu, lệch vùng ngang/chiều sâu ngoài dung sai sẽ không rút gọn cụm lỗ. Đây là phạm vi nhận diện được kiểm; chưa khẳng định bao phủ mọi cách dựng ký hiệu Dummy của dự án khác.

## Bằng chứng từ bản vẽ đang mở

Đọc qua Model + Drawing API Tekla 2025, assembly `[C2-e-26]`, main `116962992`, `C2mHz-66`, H125*125*6.5*9; chỉ đọc, không đổi work plane và không gọi DIM.

| Đối tượng | Tọa độ Front, mm | Quyết định |
| --- | --- | --- |
| BJ12z `119256223`, material JOINT | X 0..10; Y 35..45; Z -5..5 | Đại diện nhóm trái |
| BoltGroup `119256381` | (40,40,0), (90,40,0), phi 22/M20 | Trái: representative |
| BoltGroup `119373585` | (897,35,0), (957,35,0), phi 18/M16 | Phải: full |
| PLATE `116963251`, DUMMY-1719, SS400 | Tấm liên kết bên phải | Không phải ký hiệu Dummy |
| BJ12z `131126066` của dầm kế bên | X 825..835; Y 35..45; Z 67.5..77.5 | Không ghép vào nhóm phải |

Main X=0..997, Y=-22.5..102.5. Các chân single bên phải tương ứng chuỗi ngang 60–40 và chuỗi dọc 67.5–57.5. Người dùng đã xác nhận chạy thử bản điều phối ngang đầu tiên cho kết quả đúng. Đợt rà sâu giữ kết quả phân tuyến mẫu này và kiểm đầu ra writer bằng capture offline; agent không chạy lệnh DIM trên bản vẽ thật.

## Kiểm tra

- 52 assertion offline, trong đó một assertion chạy 60 lưới sinh với số hàng/cột, vị trí, kích thước, hướng đầu và thứ tự điểm thay đổi. Có cả nhận metadata, đổi số hiệu, loại DUMMY plate/substring, không/một/hai đầu có ký hiệu, đảo thứ tự, dịch XYZ, đối xứng, khác hàng/Z, Dummy giữa thanh, cùng phi/M hai đầu, nhiều nhóm cùng đầu, lỗ giữa khác phi/M, neo rãnh, đủ chân và tầng dành riêng.
- Đã tái hiện test lỗi ở bản trước khi sửa: Dummy giữa hai hàng không được nhận. Thêm regression cho một Dummy bị nhiều nhóm cùng hàng nhận, và Dummy nằm trong thanh dù nằm giữa mép và lỗ.
- `Run-WriterCapture.ps1` trích source thật với dependency closure bằng Roslyn, chỉ thay reader Dummy bằng snapshot và `StraightDimensionSetHandler` bằng recorder. Chạy chính coordinator + writer, kiểm toàn bộ feet/direction/distance, exact legacy khi cả hai đầu có Dummy hoặc read lỗi; ba mẫu hàng cùng đầu giữ đúng phía và tầng; H/I dọc hai chế độ; scale 5/10/15/20/30; tier trả cho tổng; inject exception/null của writer. Không kết nối/ghi Model hoặc Drawing API trong capture.
- Audit chỉ đọc gọi chính `ReadAssemblyFrontDummies` + `BuildAssemblyFrontHoleRoutes` từ portable: 2 ký hiệu, 4 lỗ Front; `Lrep:119256381,119256381;Rfull:119373585,119373585`.
- So sánh cú pháp C# với source gốc trước thay đổi Dummy: 351 method sẵn có trong PHU_Shape.cs giữ nguyên; chỉ caller `CreateDimsForFrontView` thay hai nhánh gọi. Các writer nền ngang/dọc và Slot03 không sửa.
- Release x64/net48/C# 7.3 qua `Update-Portable.ps1 -BuildOnly -NoPause`; workflow xác minh 40 file runtime bằng SHA-256. Không commit/push/phát hành.
- Agent chưa chạy tạo/xóa DIM thực tế cho bản rà sâu. Không có bản vẽ H/I dọc tại phiên này: dọc mới kiểm hình học và writer offline, chưa xác minh layout/read-back/idempotency trong Tekla. Không khẳng định tuyệt đối không còn lỗi ngoài tập đã kiểm.

Chạy lại:

```powershell
pwsh -NoProfile -File tests/AssemblyFrontDummy/Run-ReadOnly.ps1
pwsh -NoProfile -File tests/AssemblyFrontDummy/Run-ReadOnly.ps1 -LiveReadOnly
pwsh -NoProfile -File tests/AssemblyFrontDummy/Run-WriterCapture.ps1
```

Script dò bin từ một phiên Tekla đang chạy; có thể truyền `-TeklaBinPath` khi chạy offline hoặc có nhiều phiên. Reflection tests mặc định kiểm portable; `-RuntimePath` cho phép kiểm build staging. Toàn bộ project/bin/cache tự tạo tại `.codex-artifacts/assembly-front-regression` và `.codex-artifacts/assembly-writer-capture`, ngoài model; sản phẩm không phụ thuộc tests/cache. Capture cần .NET SDK ≥8, executable test vẫn net48/x64/C# 7.3.

## 2026-10-12: sửa tại điều phối để web dùng chung thuật toán single

Người dùng xác nhận bản vá X đầu tiên vẫn sai: DIM dọc của các cụm lỗ giữa không có như single. Bản vá chỉ gọi full-chain X rồi giữ writer đại diện Y của assembly; điều kiện cùng phi/M với lỗ mép còn bỏ hẳn Y giữa. Các kiểm thử trước đó chủ yếu yêu cầu giữ đầu ra assembly cũ và đối chiếu X, không chứng minh tương đương single cho toàn bộ X/Y. Bản vá này đã được thay thế, không tiếp tục mở rộng nó.

Nguồn gốc qua call graph:

1. `Run` → `ResolveHShapeSemanticDimensionViews` đổi `dimensionTopView`/`dimensionFrontView`, rồi khởi tạo catalog theo các view đã đổi vai. Đây là chọn mặt cánh/bụng theo hình học; không đổi loại bản vẽ.
2. `CreateDimsForFrontView` lấy lỗ bụng trong cùng hệ tọa độ view, nhưng `isAssemblyDrawing` vẫn đưa assembly ngang vào `CreateAssemblyFrontHoleDimsByDummy`; single gọi X/Y theo hình học. Vì thế trước sửa hai bên thực tế không chạy cùng thuật toán lỗ.
3. Router assembly chỉ giữ cụm đầu/cuối theo `SplitFrontHolesByX`; `middleHoles` không được đưa tới writer. Nhánh đại diện lấy một lỗ mỗi phía, Y chỉ từ mép trên tới lỗ đại diện. Single Y đi từng cụm, lấy các hàng và cả hai mép trên/dưới. Swap không thể khôi phục dữ liệu bị bỏ tại điều phối.

Cách sửa hiện hành:

- `CreateAssemblyFrontHoleDimsByDummy` dùng chung thân thuật toán X/Y của single cho toàn bộ phần lỗ cần DIM đầy đủ. Không có Dummy hợp lệ: cùng input tạo cùng chân, hướng, khoảng cách, số chuỗi và bộ đếm tầng như single. Không thêm điều kiện tên bản vẽ, chiều dài, phi hoặc số cụm.
- `CreateFrontHoleXDimsByGeometry` và `CreateFrontHoleYDimsByGeometry` giữ chữ ký, chuyển tiếp tới `CreateFrontHoleXDimsWithViewContext`/`CreateFrontHoleYDimsWithViewContext`. Thân thuật toán single giữ nguyên; chỉ tách ngữ cảnh toàn bộ lỗ của view khỏi tập lỗ thực sự cần vẽ.
- Dummy là ngoại lệ tại đầu: dùng router hiện có để ghép đúng nhóm, giữ writer đại diện nguyên bản, cấp tầng riêng từng phía. Các lỗ còn lại chạy đầy đủ X/Y một lần. Dữ liệu toàn view vẫn xác định cụm trái/phải/giữa, không suy lại từ tập đã lọc Dummy.
- Reader Dummy lỗi: chỉ giữ đại diện bảo thủ tại đầu; phần còn lại vẫn chạy X/Y đầy đủ. Snapshot lỗ không hợp lệ, không thể phân tuyến: giữ fallback cũ. X tạo thất bại không chặn lời gọi Y.
- Bỏ `BuildAssemblyFrontInteriorChainFamilies`, coordinator mép phụ và core đại diện có cờ tắt X của bản vá đầu. Writer đại diện gốc được khôi phục nguyên bản. Không thay resolver swap, catalog, H/I dọc, rãnh, tổng hay engine khác.

Kiểm bằng `tests/AssemblyWebMiddle/Run.ps1` (PowerShell 7, .NET SDK 10; runtime sản phẩm vẫn net48/C# 7.3). Roslyn trích source thật, reader Dummy là snapshot RAM và writer là recorder; không kết nối Tekla. Fixture mô phỏng các tọa độ đã biết; phi/M và ID là dữ liệu kiểm thử, không phải native snapshot của A-Hb17W-10.

- Trước sửa: 72/90 tổ hợp khác single về X/Y. Mẫu có hai cụm mép và bốn cụm giữa: single tạo 6 chuỗi Y, assembly tạo 4 chuỗi Y với hai chuỗi đại diện không tương đương single.
- Sau sửa: 90/90 tổ hợp khớp toàn bộ X/Y và bộ đếm tầng với single; single cũng khớp source backup trước sửa. Bao gồm một/hai/nhiều cụm, cùng/khác phi/M, mẫu hàng hỗn hợp, hai hàng, dịch tọa độ, đảo thứ tự và tầng rãnh.
- Mẫu tạo đủ chuỗi X 500–1200–1190–1200–496 và 6 chuỗi Y (hai mép + bốn giữa), mỗi chuỗi giữa đi qua mép trên → hàng lỗ → mép dưới.
- Dummy trái/phải/cả hai, cùng/khác phi/M: mọi Y ngoài nhóm Dummy giữ đúng single, không thêm Y trùng. Reader Dummy null và writer X null/exception không làm mất Y giữa.
- 363 method cũ giữ nguyên token; tất cả chữ ký cũ giữ nguyên. Hai thân X/Y dùng chung khớp nguyên thuật toán single sau khi thay tham chiếu ngữ cảnh lỗ; chỉ coordinator assembly thay chính sách.
- Build Release x64 thành công qua `Update-Portable.ps1 -BuildOnly -NoPause`; 40 file runtime khớp staging. Đã cập nhật checksum EXE và đối chiếu đủ 41 mục của `portable/SHA256.csv`. Code map: 57/57 file, 10662 khai báo.

```powershell
pwsh -NoProfile -File tests/AssemblyWebMiddle/Run.ps1
```

Script tự tạo cache và baseline từ source ở `backup/001-assembly-web-middle-20261012`; có thể truyền `-BaselineSourcePath`. Backup bản vá đầu trước sửa tiếp ở `backup/002-assembly-web-shared-flow-20261012`. Không có phụ thuộc sản phẩm vào tests/cache. Các kiểm thử và audit ở phần trên là lịch sử đợt Dummy, không phải đã chạy lại toàn bộ lần này. Chưa tạo/xóa DIM hoặc xác minh layout trong Tekla thật; không ghi folder model.

## 2026-10-12: thống nhất tầng vẽ lỗ và tầng báo cho DIM tổng

Người dùng đã thử thực tế và xác nhận lỗi chồng DIM được phát hiện trong đợt rà thuật toán. Lỗi có sẵn trong thuật toán X của single, rồi được assembly kế thừa khi dùng chung luồng: `allocationTier` bỏ qua tầng rãnh một lần, `drawTier` lại bỏ qua lần nữa. Writer vẽ theo `drawTier` nhưng trả `highestUsedTier` theo `allocationTier`; DIM tổng dùng tầng được báo cộng một nên có thể trùng ngay tầng lỗ đã vẽ. Lỗi xảy ra khi có tầng rãnh dành riêng và nhóm lỗ đi tới hoặc vượt tầng đó. Trường hợp hai mép dùng chung tầng còn báo một tầng ngoài chưa thực sự dùng.

Sửa tại `CreateFrontHoleXDimsWithViewContext`: tính duy nhất `drawTier` từ thứ tự nhóm, bỏ qua tầng rãnh đúng một lần, dùng chính giá trị này cho khoảng cách vẽ và tầng cao nhất trả về. Chỉ chuỗi tạo thành công mới tăng bộ đếm và cập nhật tầng; quy tắc hai mép đủ điều kiện dùng chung tầng được giữ. Không sửa công thức đặt DIM tổng để bù lỗi. Cả single và assembly nhận cùng quy tắc tầng mới; chân DIM, phân nhóm, hướng và thuật toán Y giữ nguyên. Những khẳng định single khớp toàn bộ source cũ ở mục trước mô tả lần sửa điều phối; từ lần này khoảng cách X và tầng báo cho tổng được sửa có chủ đích.

Ví dụ kiểm hồi quy (mm trong hệ tọa độ view): mép trên Y=87,5; khoảng cách đầu 100; bước tầng 40; dành tầng 1 cho rãnh. Trước sửa, chuỗi lỗ giữa vẽ tầng 3 (Y=307,5), báo tầng 2; tổng đặt tầng 3 và chồng lên lỗ. Sau sửa, chuỗi giữa vẽ và báo tầng 2 (Y=267,5), tổng ở tầng 3 (Y=307,5). Hai mép dùng chung tầng không còn báo thêm tầng ngoài rỗng.

Kiểm offline bằng source thật qua `tests/AssemblyWebMiddle/Run.ps1`:

- Đã chạy ca tái hiện trước sửa: thất bại đúng điều kiện `reported highest=2, actual=3`; sau sửa vượt qua.
- 1.584 tổ hợp hình học, khoảng cách, tầng bắt đầu, tầng rãnh và chế độ Dummy: tầng báo bằng tầng cao nhất thực tế, không chiếm tầng rãnh, gọi chính `CreateFrontTotalDims` xác nhận tổng nằm ngoài mọi chuỗi X. Grid takeover được stub tắt trong capture; đây là kiểm nhánh tổng trực tiếp của Shape.
- 90 tổ hợp vẫn giữ toàn bộ chân/hướng/số chuỗi và đầy đủ khoảng cách Y so với single gốc; assembly không Dummy khớp single hiện hành cả X/Y/tầng. Giữ chuỗi X 500–1200–1190–1200–496 và đủ DIM Y ở bốn cụm giữa.
- Kiểm riêng hai mép chung tầng với tầng rãnh -1/0/1; writer X trả null hoặc ném lỗi không làm mất Y và không tính chuỗi thất bại là đã dùng tầng.
- So với backup ngay trước sửa tầng, toàn bộ token ngoài thân `CreateFrontHoleXDimsWithViewContext` và mọi chữ ký giữ nguyên. So với nguồn trước sửa điều phối, 363 method cũ vẫn nguyên token.
- Build Release x64/net48/C# 7.3 thành công qua `Update-Portable.ps1 -BuildOnly -NoPause`; 40 file runtime khớp staging, đủ 41 mục `portable/SHA256.csv` khớp file thực tế. Không commit/push/phát hành.

Backup trước sửa tầng: `backup/003-front-hole-tier-contract-20261012`. Có thể truyền `-TierBaselineSourcePath` để kiểm phạm vi thay đổi với source tương ứng. Extractor và capture có thư mục build riêng, chặn output ngoài đúng tên file dưới `.codex-artifacts`. Không có phụ thuộc sản phẩm vào backup/tests/cache. Người dùng xác nhận lỗi cũ bằng Tekla thật; bản sửa tầng mới chỉ được agent kiểm offline, không gọi tạo/xóa DIM và không ghi folder model.

## 2026-10-12: phân vai cụm lỗ theo đầu thanh thật — mẫu 2CmHz-80 (đã hoàn tác)

Trạng thái hiện tại: người dùng yêu cầu bỏ lần sửa dùng 300 mm để phân loại mép/giữa. Nội dung và kết quả kiểm dưới đây chỉ là lịch sử lần sửa đã hoàn tác; xem mục hoàn tác ở cuối tài liệu.

Đã đọc bản vẽ đang mở bằng Model/Drawing API ở chế độ chỉ đọc, không đổi work plane: SinglePart `[2CmHz-80]`, main `262738556`, H200*200*8*12, Front `1202`, scale 20. Biên Front X=0..6205, Y=-100..100 theo các chân DIM hiện có. BoltGroup `266327443`, phi18/M16 có hai lỗ tại X=2102,5 và 4102,5, Y=0. BoltGroup `264747492`, phi22/M20 có hai hàng tại X=6165, Y=±30. Dữ liệu capture ngoài model: `.codex-artifacts/front-current-readonly/current.json`.

Đầu ra thực tế trước sửa: hai DIM ngang riêng 2102,5 về hai đầu; thiếu đoạn 2000 nối hai lỗ giữa. DIM Y tại cụm X=2102,5 lấy chân mép trên/dưới tại X=0, kéo đường dóng từ đầu thanh vào giữa. Cụm X=4102,5 dùng neo tại cụm. Đây là khác biệt ngay trong thuật toán single, không do swap.

Nguồn gốc:

1. X chỉ nhận một family là giữa khi có lỗ khác ở cả hai phía, hoặc ép full-chain khi có ít nhất ba cụm. Thiếu lỗ mép trái làm `hasOtherHoleOnLeft=false`; đúng hai cụm bị đưa vào writer mép trái/phải, mất khoảng giữa.
2. Y coi cụm chứa X nhỏ nhất toàn tập là cụm mép trái. Điều kiện khoảng cách 300 mm chỉ áp dụng cho chọn tầng ngoài, chưa áp dụng cho chọn chân neo. Vì thế có thể dùng tầng giữa nhưng chân đầu thanh.
3. Điều kiện hai family chung tầng lại cho phép vùng mép 300+tol, khác ngưỡng 300 mm của Y. Khi thống nhất vai, phải thống nhất cả điều kiện này để hai chuỗi giữa không cùng chiếm tầng dành cho hai mép.

Sửa tại tầng phân loại, giữ writer hình học hiện có:

- `GetFrontHoleClusterEndSide`: tâm cụm trong vùng 300 mm model của đầu gần nhất là mép trái/phải, ngoài vùng là giữa. Không phụ thuộc scale giấy, số cụm hoặc sự có mặt của lỗ mép khác.
- X có cụm giữa thì dùng full-chain sẵn có dù chỉ một/hai cụm; nhóm mép đưa vào đúng phía theo vai hình học. Giữ quy tắc full-chain cũ cho các trường hợp đã thuộc nhánh này và giữ thứ tự family/cấp tầng.
- Y chỉ được lấy chân đầu thanh khi cụm vừa thuộc vùng mép thật vừa là cụm ngoài cùng tương ứng trong ngữ cảnh toàn view. Cụm giữa dùng neo cục bộ, đủ mép trên → các hàng lỗ → mép dưới.
- Chia tầng hai family đối diện dùng cùng classifier; điều kiện phi/M và mẫu hàng giữ nguyên. Ngưỡng 300..302 mm trước đây chỉ được nới ở điều kiện chia tầng nay theo đúng 300 mm như X/Y.
- Không đổi resolver swap, catalog lỗ, cách nhóm phi/M/mẫu hàng, writer tạo chân X/Y cấp thấp, quy tắc Dummy, H/I dọc, rãnh hay tổng. Không đặt ngoại lệ theo tên bản vẽ hoặc tọa độ mẫu.

Kiểm bằng `tests/AssemblyWebMiddle/Run.ps1`:

- Fixture tọa độ đọc thật thất bại trước sửa ở chuỗi X; sau sửa tạo 2102,5–2000–2102,5 và hai chuỗi Y cục bộ 100–100. Single và assembly không Dummy khớp đầy đủ.
- 192 tổ hợp 1–4 cột giữa, không/có mép trái/phải/cả hai, đối xứng, dịch X, một/hai hàng, đảo thứ tự điểm: đủ chuỗi X và Y không lấy nhầm chân đầu thanh. Kiểm riêng ngưỡng 299/300/300,01/301/302 mm và điều kiện chung tầng.
- 90 tổ hợp single/assembly vẫn khớp toàn bộ X/Y/tầng; 54 trường hợp không thuộc lỗi thiếu lỗ mép giữ nguyên chân/hướng/số chuỗi/khoảng cách Y của nguồn ban đầu. Những ca thiếu mép đổi kết quả có chủ đích và được kiểm bằng tiêu chí hình học riêng, không còn yêu cầu giữ đầu ra sai cũ.
- 1.584 trường hợp cấp tầng/rãnh/Dummy, ca writer null/exception, chuỗi A-Hb17W-10 và DIM Y giữa đều tiếp tục đạt.
- Khóa token so với backup ngay trước sửa: chỉ hai core X/Y, điều kiện chung tầng và một classifier mới được đổi; mọi chữ ký cũ giữ nguyên. 362 method cũ giữ nguyên so với baseline trước các lần sửa điều phối.

Build Release x64/net48/C# 7.3 thành công, portable khớp 40 file runtime staging và đủ 41 checksum trong manifest. Code map cập nhật theo source. Backup hash-verified: `backup/004-front-hole-physical-role-20261012`; test nhận `-PlacementBaselineSourcePath` khi dùng nguồn baseline khác. Đã đọc bản vẽ thật trước sửa; chưa chạy tạo/xóa DIM để xác minh kết quả mới trong Tekla. Không ghi folder model, không commit/push/phát hành.

## 2026-10-12: hoàn tác phân loại mép/giữa bằng 300 mm

Khôi phục nguyên byte `PHU_Shape.cs` và ba file kiểm hồi quy `Capture.cs`, `Extract.cs`, `Run.ps1` từ `backup/004-front-hole-physical-role-20261012`, là trạng thái ngay trước khi thêm `GetFrontHoleClusterEndSide`. SHA-256 source sau khôi phục: `79DBA394235991931B63AB7C23D28C5F7AF709623D06ECDDEE28B93E25B8BAA0`. Trạng thái trước hoàn tác được sao lưu và kiểm hash trong `backup/005-front-hole-role-rollback-20261012`.

Giữ nguyên luồng X/Y dùng chung single/assembly và sửa lỗi tầng vẽ/tầng báo cho tổng. Nhận diện family giữa, chọn chân Y và điều kiện hai mép chung tầng trở về đúng quy tắc trước lần sửa phân loại. Các nơi vốn dùng `FRONT_END_HOLE_ZONE = 300.0` trong thuật toán trước đó vẫn giữ nguyên; đây không phải thao tác xóa mọi hằng số 300 trong source.

Hệ quả đã biết: trường hợp thiếu lỗ đầu thanh như `[2CmHz-80]` trở về đầu ra cũ; chưa có phương án thay thế cho lỗi thiếu đoạn X 2000 và neo Y về đầu thanh. Không gọi tạo/xóa DIM và không ghi folder model trong lần hoàn tác này.

Kiểm sau hoàn tác: rebuild extractor để bỏ binary cache từ lần phân loại, rồi `tests/AssemblyWebMiddle/Run.ps1` đạt 90 ca đối chiếu single/assembly, đủ chuỗi 500–1200–1190–1200–496 và DIM Y giữa, cùng 1.584 ca cấp tầng/Dummy. Kiểm token xác nhận 363 method gốc nguyên vẹn và sửa tầng chỉ nằm trong core X dùng chung. `Update-Portable.ps1 -BuildOnly -NoPause` build Release x64 thành công, xác minh 40 file runtime; đủ 41 mục manifest SHA-256 khớp. EXE portable và staging cùng SHA-256 `C181C1AF7910B6AE75CCDB164AEFCE02CA087150F478F1C77E3CE3C793CE0589`. `Update-CodeMap.ps1 -Check` đạt 57/57 file, 10.662 khai báo. Đây là kiểm offline; không thử tạo DIM trên bản vẽ thật.
