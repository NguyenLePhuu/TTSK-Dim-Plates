# Slot 02 — hướng side mark tại mép plate

## Hoàn thiện và tối ưu — kiểm chứng bổ sung 2026-10-02

Đã kiểm chứng **toàn tuyến không-selection**, từ tự nhận diện đến tạo dim và side mark, trên bản vẽ hiện tại `[3D-sb15W-1]`, model `D:\26-024)Yakult_Dormitory`. Bộ nhận diện tìm 4 liên kết và tạo 5 dim. Chạy toàn tuyến selection tạo đúng cùng 5 dim. Phần kiểm chứng thiếu của lần bàn giao đầu đã được bổ sung; ghi chú về `[A-G13-2]` bên dưới là lịch sử fixture trước đó.

Mã production mới đã được **gộp vào `PHU_Slot02_NeighborReferencePlateDim.cs`**. Xóa file riêng `PHU_Slot02_PlateSideMarks.cs` và khai báo compile tương ứng. Giữ class helper trong cùng file để phân tách trách nhiệm và giữ entry point native ổn định. Test vẫn nằm ngoài source production.

Các tối ưu bảo toàn ý nghĩa dimension:

- Kiểm tra trục reference của main bằng chính điều kiện writer trước khi đọc toàn bộ model part của view không dùng được.
- Lọc H/flange face trước khi lấy solid; lọc plate cùng assembly và quan hệ bolt trước khi lấy solid plate. Không nới điều kiện topology hoặc hướng chiếu.
- Cache danh sách part tham gia bolt theo ID trong một lần nhận diện; giữ kiểm tra liên kết hai chiều như resolver gốc. Có fallback resolver cũ nếu đọc cache gặp lỗi API.
- Cache bounds theo part trong một lần ghi ở một view; không dùng lại giữa các view/lần chạy.
- Gửi ViewId của dim mới cho bước native để chỉ quét đúng view đó; vẫn kiểm tra model, drawing GUID, set/child ID, hình học và các thuộc tính trước/sau ghi.

Kết quả đo trên cùng bản vẽ, cùng 5 dim:

| Phép đo | Trước | Sau | Giảm |
| --- | ---: | ---: | ---: |
| Toàn tuyến tự động, lần đo lặp | 3.962 s | 2.750 s | 30.6% |
| Toàn tuyến selection, lần đo lặp | 1.794 s | 1.428 s | 20.4% |

Thời gian chỉ bao gồm `Run()`; không gồm test lưu baseline, đối chiếu và dọn dim thử. Lần đo đầu ghi nhận tự động 5.009 → 2.793 s và selection 1.914 → 1.361 s; dùng số đo lặp ở bảng để tránh phóng đại ảnh hưởng warm-up.

Kiểm chứng bổ sung:

- 127 ca pure geometry vẫn pass.
- 360 cặp plate/H trong drawing, cả kết nối và không kết nối; cache so với resolver gốc theo hai thứ tự duyệt đều giống nhau (720 so sánh).
- 8 tổ hợp trái/phải × trên/dưới × dấu Distance đều pass sau gộp/tối ưu; chạy lại sau commit giữ đúng kết quả.
- Toàn tuyến tự động và selection được chạy trước/sau tối ưu. Bản kê chân đo, hướng và khoảng cách của 5 dim có cùng SHA-256 `5A3835A4CF450D8D00310C228725CD4DD988BE565A189B16775C8C71452E3849`.
- Các dim thử đều được dọn. Baseline xác nhận số lượng, ID, hình học và thuộc tính công khai của dim có sẵn không đổi.
- Workflow BuildOnly cập nhật và xác minh 39 file runtime. Không publish/commit/push. SHA-256 portable và staging đều là `6606A26E94F1B303D2BA90EEB1EB40F22967B09354D149B61F180B48D688380C`.
- Hai file HIGE của model Yakult giữ nguyên: RIGHT `87DD41F48E81500549723E8F05AC130256F8398B502BC92BAD8E5D632D7792D6`, LEFT `1542EE04A10EFD6B9404F2279BDE9F8F04D7EC127F14F978929DC6FD2996DFD4`. Hai file ở model Sinjuku cũng giữ nguyên hash của lần đầu.

Log đo nằm trong `.codex-artifacts/slot02-completion/`; test chạy lại từ `tests/Slot02SideMarks`. Các kết quả xác nhận những fixture và biến thể đã kiểm thử, không phải bảo đảm tuyệt đối cho mọi model/Tekla version chưa kiểm tra.

## Quy tắc đã triển khai

Side mark ở chân mép phải hướng vào phần thân plate tính từ mép đó. Chân reference để trống. Hướng side mark độc lập với hướng phóng đại của HIGE và phía đặt đường dim.

| Hình học trong hệ tọa độ view | Chân trái | Chân phải |
| --- | --- | --- |
| Reference bên trái, mép trái của plate ở bên phải | Trống (`-1`) | Hướng phải (`2`) |
| Mép phải của plate ở bên trái, reference bên phải | Hướng trái (`0`) | Trống (`-1`) |

Các giá trị này là giá trị native đã kiểm tra trên Tekla Structures 2025 đang cài. `proSideMarkType = 1` tương ứng Specified. Chân 1/2 vẫn là trái/phải trong view khi đường dim đặt phía dưới. Không đảo side mark theo `UpDirection` hoặc dấu `Distance`.

Resolver dùng đúng `NeighborRef`, `PlateEdge` và giới hạn X của plate mà slot 02 đã tính trong cùng hệ tọa độ view. Nó yêu cầu mép là MinX hoặc MaxX và reference ở ngoài mép đó. Trường hợp chân trùng, giá trị không hữu hạn hoặc mép không xác định rõ sẽ dừng trước khi tạo dim nhỏ. Không dùng số đo 3,5, số hiệu part hoặc ID mẫu trong mã production.

## Tích hợp cả hai luồng

Hai luồng có/không có selection đều vào `CreateNeighborPlateReferenceDims`:

1. Nhánh automatic truyền `exactConnections` đã nhận diện.
2. Nhánh selection dùng resolver neighbor hiện có.
3. Cả hai tạo dim nhỏ ở cùng một vòng lặp và gọi duy nhất `PHU_Slot02PlateSideMarks.Apply` cho các dim vừa tạo trong view.

Vẫn chọn `GEO_HIGE_RIGHT/GEO_HIGE_LEFT` theo quy tắc cũ. Sau khi nạp HIGE, đọc lại set/child rồi hiệu chỉnh ba thuộc tính native của child. Không gọi `set.Modify()` sau bước này. Chuỗi dim chính, chọn reference, chọn neighbor, chân đo và các slot khác không đổi.

## Hàm Tekla và giới hạn macro

API Drawing công khai trong DLL Tekla 2025 không có thuộc tính plate side mark. Hàm native đã thử thành công:

```text
teklastructures.cli.DrawingPropertiesObjectFactory.CreateObject(childId, "")
ObjectPropertiesProxy.GetProperty(propertyName)
ObjectPropertiesProxy.SetProperty(propertyName, value)
DrawingPropertiesObjectFactory.UpdateObject(proxy)
```

Chỉ ghi `proSideMarkType`, `proSideMarkSymbol1`, `proSideMarkSymbol2`. Màu, size, offset, exaggeration và các thuộc tính native còn lại được đọc trước/sau để kiểm tra bảo toàn.

Native Properties phải chạy trong tiến trình Tekla trên luồng UI chính. Một macro ngắn chỉ nạp DLL và gọi entry point C#; không click UI, không chọn dropdown, không chứa thuật toán nhận diện. Dùng lại bộ điều phối `WithNativeColorFactory` hiện có mà không sửa logic Revision. Drawing/Model API chạy ngoài luồng UI native để tránh deadlock.

Writer DLL được sao chép theo SHA-256 để Tekla không dùng nhầm bản DLL cũ. Mỗi yêu cầu kiểm tra model, GUID bản vẽ, ID set/child, hai chân, hướng và khoảng cách. Ghi xong phải đọc lại đúng mark và kiểm chứng hình học. Khi có lỗi, khôi phục mark; chỉ xóa dim mới khi worker đã xác nhận kết thúc. Yêu cầu quá hạn chưa xác nhận giữ nguyên đối tượng để tránh xóa trong lúc worker đang chạy.

Không ghi bất kỳ file `.dim` nào. Cách dùng Specified tương ứng thao tác thủ công được mô tả trong [hướng dẫn Tekla](https://support.tekla.com/doc/tekla-structures/2020/dra_showing_plate_side_marks_manually).

## Kiểm chứng ngày 2026-10-02

Trên bản vẽ đang mở `[A-G13-2]`, model `D:\27-027)Sinjuku_Slope`:

- 127 ca pure geometry: đổi phía, tọa độ, độ rộng plate, cao/thấp, gap nhỏ; từ chối hình học mơ hồ/NaN/infinity.
- 8 dim thử qua đúng writer nạp HIGE của slot: trái/phải × trên/dưới × Distance dương/âm. Tất cả ghi và đọc lại thành công; chạy lại sau commit vẫn đúng.
- Hai nhánh của hàm chung, truyền cùng cặp plate–neighbor thực: nhánh có `exactConnections` và nhánh resolver selection đều tạo 2 dim, hình học trùng nhau.
- Chạy toàn bộ `Run()` với selection plate + neighbor: tạo 2 dim đúng như hai nhánh trên.
- Xóa toàn bộ dim thử sau mỗi lượt; kiểm tra số lượng, ID, hình học, thuộc tính công khai của dim có sẵn. Các thuộc tính native của dim gốc cũng khớp hoàn toàn trước/sau.
- Đối chiếu trực quan nét xiên trái/phải trên/dưới; dim gốc người dùng đã chỉnh đúng được giữ nguyên.

**Lịch sử kiểm chứng fixture cũ:** bộ tự nhận diện `FindAutomaticSlot02Targets` hiện có trả về 0 trên bản vẽ này. Vì vậy kiểm tra nhánh automatic phía sau nhận diện dùng cặp model được truyền rõ ràng; chưa xác nhận toàn tuyến không-selection trên bản vẽ này. Không nới điều kiện nhận diện để làm test chạy. Bộ nhận diện đó không bị sửa.

SHA-256 hai file gốc trước/sau giống nhau:

```text
GEO_HIGE_RIGHT.dim EF8D2BD00129B4DB7AF57E3BF58824F8A7239749917D979F1B18BCCCFE136FEB
GEO_HIGE_LEFT.dim  1542EE04A10EFD6B9404F2279BDE9F8F04D7EC127F14F978929DC6FD2996DFD4
```

Release x64 đã build bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`. Workflow xác minh đủ 39 file runtime. SHA-256 executable trong staging và portable:

```text
9988AFC17F217C98100111E5D355AC73D9BA20019C492C228E7AF1A0B9E76517
```

Không publish, commit hoặc push trong lần sửa này.
