# Slot 08 — hai dầm reference chồng nhau

## Đính chính kết luận trước

Người dùng xác nhận trong ảnh thanh 1 là dummy đang dùng, thanh 2 màu xám là thanh chưa dùng. Kiểm tra trước đó chỉ chứng minh rằng code giữ đúng đối tượng mà Connection đang trỏ tới; chưa chứng minh đối tượng đó đúng với ý đồ thiết kế. Vì Connection vẫn trỏ thanh chưa dùng, kết luận chọn `109028105` trong báo cáo trước là sai về ngữ nghĩa sử dụng.

Các test không đổi graph/Check DIM vẫn có giá trị về hồi quy, nhưng không phải bằng chứng lựa chọn support đúng. Không dùng số test đạt để thay cho quy ước kỹ thuật người dùng.

## Bằng chứng 3D chỉ đọc

Bản vẽ `[C2-e-34]`, main `117633970`, plate `112256909`.

| Thuộc tính | Thanh 1 cần giữ | Thanh 2 bị giữ nhầm |
| --- | --- | --- |
| Model ID | 130973315 | 109028105 |
| Name / profile / material | G16M / SH1000*250*16*22 / SM490A | giống thanh 1 |
| Class | 1010 | 1 |
| Permanent representation RGB gần đúng | 231,83,92 — đỏ | 157,157,169 — xám |
| Chiều dài theo endpoint | 500 mm | 11.480 mm |
| DUMMY mark | DUMMY-1767 | DUMMY-1210 |
| Cao độ đỉnh theo endpoint | 25.816 mm | 25.820 mm |
| Liên kết API từ plate | Không có bolt/weld/Connection trực tiếp trong dữ liệu đọc được | Connection 112261832: primary=109028105, secondary=112256909 |
| UDA đáng chú ý | proIfcEntityOvrd=IfcBeam | initial_GUID, initial_profile |

Plate bắt bulông `119741859` vào main. Bulông `112261867` do Connection tạo có hai đầu cùng là plate; không chứng minh liên kết vật lý với dầm nhận.

Màu đọc bằng `ModelObjectVisualization.GetRepresentation`, không lấy màu pixel hoặc màu selection. Chỉ dùng để đối chiếu ảnh và quy ước người dùng; màu có thể phụ thuộc representation của model/view nên không mặc định coi RGB là trạng thái kỹ thuật ổn định.

Mặt bụng gần plate của cả hai solid cùng ở X≈−8 trong work plane đang đọc. Thanh ngắn nằm trong phạm vi dọc của thanh dài, cùng hướng và tiết diện nhưng lệch cao 4 mm. Plate có contour tại X=−8, Z=24.840..25.794 và chamfer/bo góc 20 mm. Vì vậy chỉ so bounding box, mặt bụng gần nhất hoặc chồng hình chiếu không đủ. Khác biệt cao độ cánh là bằng chứng phụ; chưa tính boolean giao solid và không kết luận cấn 2 mm chỉ từ contour thô có bo góc.

## Vì sao quy tắc vừa thêm sai

`RetainConnectionReferenceSupports` dùng primary input của Connection làm support, trong khi model giữ Connection trên thanh reference cũ. Tính duy nhất trong quan hệ API không đồng nghĩa đúng trạng thái sử dụng. `Dummy` hiện gom nhiều loại reference, không phân biệt đang dùng và chưa dùng.

Không sửa bằng cách giữ tất cả dummy, thay bằng thanh ngắn nhất, ưu tiên màu bất kỳ, hoặc chuyển mọi Class 1 sang 1010. Những cách đó có thể thay đầu ra ổn định trên model khác.

## Quy tắc đã triển khai, phạm vi giới hạn

1. Giữ nguyên Resolve, bolt/weld graph, quy tắc dummy cũ và Check DIM.
2. Chỉ xét nhánh bổ sung support qua Connection khi phát hiện các reference cùng vùng liên kết chồng nhau. Connection là nguồn tìm vùng/cụm, không tự quyết định thanh cuối cùng.
3. Người dùng cho phép tự chọn phương án và xác nhận quy ước màu xám là chưa dùng. Dùng permanent representation đọc qua API, không dùng màu selection/temporary; không hard-code Class 1/1010. RGB có chroma ≤0,08 và saturation ≤0,15 được nhận là xám; chroma ≥0,15 và saturation ≥0,25 là màu rõ. Vùng giữa, đen/trắng và màu không đọc được là chưa rõ. Quy tắc này chỉ áp reference của nhánh Connection mới, không đổi phân loại dummy hoặc các engine khác.
4. Kiểm từng ứng viên bằng tọa độ 3D nhất quán: hướng trục, tiết diện, mặt plate tiếp giáp bụng/cánh và vùng dọc thực sự chứa giao diện. Dùng mặt/contour có chamfer, không dùng bbox như phép chứng minh tiếp xúc.
5. Chỉ chuyển sang support đang dùng khi có đúng một ứng viên phù hợp trạng thái và hình học. Lưu quyết định riêng cho visibility, không thêm cạnh giả vào graph của Check DIM.
6. Theo yêu cầu người dùng: nếu trạng thái không rõ, nhiều ứng viên cùng phù hợp hoặc dữ liệu mâu thuẫn thì giữ/hiện cả nhóm nghi vấn, báo cảnh báo và tiếp tục neighbor khác. Không dùng force-stop cho sự mơ hồ này. Các guard cũ về sai bản vẽ, macro pending, ghi thất bại vẫn giữ vì đó là lỗi thực thi, không phải không phân biệt được hai reference.

Ứng viên phải là dummy không plate, không thuộc primary, có cùng profile/material, trong view cùng chứa support nguồn và plate bằng chứng. Đổi tọa độ current plane → global; không đổi work plane. Kiểm trục song song/đảo chiều bằng cosine 0,999999. Để tự chọn, cùng một mặt thật của solid plate phải được mặt đối hướng của cả hai support bao đủ với khoảng cách mặt ≤0,05 mm. Chỉ mặt phẳng lồi một loop có diện tích >0,01 mm² dùng làm bằng chứng; mặt có lỗ/lõm không được lấp bằng bbox. Bbox chỉ khoanh nhóm nghi vấn để giữ, không dùng chứng minh lựa chọn.

Lõi Resolve → RetainLocalDummies chạy trước. Không sửa Nodes/Edges/primary/dummy và không thêm cạnh giả. Quyết định thay thế/giữ cả hai chỉ nằm ở Keep và danh sách hiện lại từng view. Với nhóm mơ hồ, giữ nguyên reason Keep cũ nhưng cho phép hiện lại cả thành viên đã ẩn khi view có primary và plate bằng chứng đang hiện.

Rà nhánh lỗi đã bỏ catch giữ tất cả reference cùng profile trên cả bản vẽ. Chỉ nhóm đã khoanh vùng được bảo vệ; ứng viên cụ thể đọc lỗi được giữ riêng và báo cảnh báo, không kéo theo các ứng viên đọc được nhưng không liên quan. Nếu support nguồn chưa đọc được trước khi xác định nhóm, chỉ giữ source đã biết, không bịa quan hệ thay thế. Các tình huống mất API hoàn toàn không thể đảm bảo nhận diện đúng mọi part; không tuyên bố bao phủ tuyệt đối.

Các hồi quy cần thêm: đảo ID/thứ tự; active dài hơn hoặc ngắn hơn; không có bản trùng; hai active trùng; metadata thiếu; class đổi ở dự án khác; Connection còn trỏ reference cũ; plate đã ẩn; mặt nghiêng/mirrored; bo góc; support không có trong target view; tải nhiều dummy. So baseline ngoài nhóm tranh chấp, không chỉ kiểm giữ thanh 1.

## Kiểm chứng và trạng thái

Trước khi được phép live-test: 47 hồi quy cũ + 35 ca màu/tiếp giáp/giữ cả hai đạt; 2.000 đồ thị so baseline đạt. Đối chiếu Check DIM trên EXE cũ/mới: báo cáo (trừ thời gian) và 7.294 feature tọa độ/owner/vai trò giống nhau. Mẫu `[C2-e-34]` đọc thực tế chọn đúng 130973315, dự kiến hiện lại một view; giữ 109028105 ở danh sách ẩn theo lõi.

Đã có phép thử dùng hình học thật nhưng giả lập màu không rõ trong cache bộ nhớ: cả hai support được đưa vào Keep/restore ở hai view, các neighbor khác không đổi và Check DIM graph không đổi. Không thay màu trong model.

Sau đó người dùng cho phép chạy trên `[C2-e-34]` và `[C2-e-73]`. Kết quả live-test và giới hạn được cập nhật trong `SLOT08_IMPACT_REVIEW_2026-10-04.md`. Công cụ live riêng `Run-LiveReview.ps1`; không dùng nhầm công cụ audit để ghi. File probe, snapshot và log luôn ngoài model; chỉ API ghi bản vẽ/đổi bản đang mở trong phạm vi live-test được cho phép.

Kết quả live cuối: `[C2-e-34]` giữ/hiện đúng thanh 1, ẩn thanh 2; `[C2-e-73]` giữ hai plate của trường hợp cũ. Cả hai chạy lần hai không thay đổi. Nhánh mơ hồ đã được Apply thật bằng kế hoạch giả lập color-cache: hiện cả hai thanh trong hai view, không đổi đối tượng không liên quan, rồi chạy quy tắc thật trả về trạng thái đúng và lưu. `[C2-e-34]` được mở lại cho người dùng.
