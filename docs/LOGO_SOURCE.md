# Logo được chọn ngày 2026-10-04

Chủ dự án chọn bản ImageGen sắc nét, sau đó yêu cầu làm riêng chữ mỏng gần bản gốc nhưng giữ nguyên nội dung. Đã edit bằng built-in ImageGen với hai reference: bản sắc nét là target, ảnh 190×156 là tham chiếu độ mỏng. Lượt thử đầu còn đậm; lượt thứ hai được dùng. Không dùng ảnh Lanczos thử nghiệm.

- Runtime/source: `TTSK Dim Plates/TTSK Dim Plates/Resources/logo.png`, 1385×1136 RGBA, chữ mỏng; SHA-256 `63626a0685475c9de134d1ad3a294fe7bdce7785471c93795b5b3a2b536278b2`. Bản lưu cùng bytes: `docs/assets/logo-imagegen-thin.png`.
- Bản ImageGen chữ đậm trước chỉnh: `docs/assets/logo-imagegen-bold.png`; SHA-256 `a912fa2e87f66ef5dbd1e1aea166dcff5fc50d0b759ba811623385cc86699a36`.
- Bản gốc để đối chiếu/khôi phục: `docs/assets/logo-original-190x156.png`, 190×156; SHA-256 `08765f330a0eecedc5689de7c57b76fe01ee0c94898555a278fea2c446a47333`. Không là phụ thuộc runtime.
- Ảnh đối chiếu cùng chiều rộng: `docs/assets/logo-comparison.png`; tạo lại bằng `tests/TeklaSessions/Compare-Logo.ps1`. Không kéo méo hoặc sửa hai hình để so sánh.
- So sánh riêng chữ ba hàng (gốc/đậm/mỏng): `docs/assets/logo-text-comparison.png`, tạo bằng `tests/TeklaSessions/Compare-LogoText.ps1`, giữ tỉ lệ khi phóng vùng chữ.
- Nguồn tạo: built-in `image_gen.imagegen`, transparent_background=true, tham chiếu đúng PNG gốc. Ảnh chọn được sao chép nguyên bytes vào project; không chỉnh thêm nội dung.

## Khác biệt quan sát được

1. Ảnh ImageGen có độ phân giải lớn hơn khoảng 7,3 lần theo chiều rộng; biên logo và chữ rõ hơn khi cùng hiển thị rộng 450 px.
2. Nội dung chữ vẫn là `株式会社辻鉄商工`. Bản mới giảm rõ độ dày nét so với ImageGen trước, tăng khoảng trống bên trong chữ. Gần bản gốc hơn về độ mỏng nhưng không khẳng định trùng chính xác font/độ dày nét raster cũ.
3. Cam/xanh của bản chọn bão hòa hơn, có biến thiên màu nhẹ; bản gốc phẳng hơn. Một số đường cong/nét trắng, khe giữa bốn ô và khoảng cách biểu tượng–chữ cũng có khác biệt.
4. Tổng thể vẫn là bốn ô màu và dòng tên công ty. Không mô tả bản chọn là bản sao pixel-identical hoặc chỉ tăng độ phân giải từ ảnh cũ.

## Kiểm nội dung chữ

Đã đối chiếu trực quan đủ tám ký tự từ trái sang phải trên ba hàng ở kích thước phóng lớn và ảnh form thực. Nội dung mục tiêu: `株` U+682A, `式` U+5F0F, `会` U+4F1A, `社` U+793E, `辻` U+8FBB, `鉄` U+9244, `商` U+5546, `工` U+5DE5. Không dịch, thay từ, đổi thứ tự hoặc dùng chữ Hán giản thể thay chữ Nhật. Đây là đối chiếu trực quan; không tuyên bố OCR tự động chứng nhận ảnh. Thay đổi độ đậm không đổi tên công ty/nội dung chữ.

Độ phủ alpha trung bình trong bounding box chữ (chỉ số diện tích, không phải độ dày từng nét): gốc khoảng 0,38; ImageGen đậm 0,53; bản mỏng 0,30. Giảm diện tích mực không đi kèm bỏ ký tự. Biểu tượng phía trên giữ thiết kế/nhận diện qua đối chiếu trực quan; ImageGen không đảm bảo từng pixel phần ngoài chữ hoàn toàn giống ảnh đậm.

## Tích hợp và kiểm tra

CompanyLogoRenderer chỉ thu ảnh lớn về đúng kích thước hiển thị bằng GDI+, giữ tỉ lệ/alpha. Render 1:1 clone trực tiếp. Logo-only dùng bitmap pixel đích; form chờ cache bitmap rồi DrawImageUnscaled. Theo yêu cầu bổ sung dark mode, Load chọn hai asset cố định như phần dưới, không tô lại chữ mỗi frame.

Kiểm `logo-render`: giữ nguyên pixel 1:1, vùng màu đặc, alpha góc và tỉ lệ; đo render 375 px khoảng 23–27 ms trên máy thử. Form chờ/cancel đạt. Lúc đầu phiên Tekla không có hoặc API chưa sẵn sàng, bài probe kết nối thất bại; đã sửa probe chặn danh sách rỗng và skip khi IsReady=false, lỗi probe chỉ báo stderr/exit code thay vì popup exception. Sau khi Tekla sẵn sàng đã kiểm lại `wait-ready`: logo-only trong suốt → MainForm đúng `27-000)HULIC_B`, form chờ không hiện, child đóng bình thường. Không gọi thao tác ghi model/bản vẽ.

Sau chỉnh chữ mỏng: `logo-render` đạt (41 ms ở 375 px trong lượt kiểm); capture form chờ thật 125% dark/light, cancel sạch. Lần chỉnh độ mỏng chỉ thay PNG; lần bổ sung dark mode dưới đây có tích hợp C# và kiểm Release x64.

## Biến thể dark mode — chỉ đổi màu chữ

- Light: `Resources/logo.png` vẫn giữ nguyên hash `63626a0685475c9de134d1ad3a294fe7bdce7785471c93795b5b3a2b536278b2`.
- Dark: `Resources/logo_dark.png`, 1385×1136 RGBA, hash `80f3875d3f0dc1b4da87d62e05be21815c23c19374956d8f6deff3d0e1c0f6ad`.
- Màu chữ dark `#FCFCFC` lấy trung vị các pixel gần trắng của biểu tượng (vùng mũi tên người dùng chỉ). Không chạy ImageGen cho thay đổi màu đơn thuần này, không tái dựng glyph.
- `scripts/Update-DarkLogo.ps1` đổi đúng 91.289 pixel có alpha khác 0 trong vùng chữ. Kiểm từng pixel ảnh đã lưu: alpha toàn ảnh giống hoàn toàn; mọi pixel ngoài vùng chữ và pixel trong suốt giữ nguyên ARGB; chỉ RGB pixel chữ thành #FCFCFC. Vì hình/alpha chữ không đổi nên giữ nguyên nét mỏng và nội dung `株式会社辻鉄商工`.
- Chọn asset theo theme ở main header, startup chờ/logo-only và icon của các cửa sổ. ApplyTheme đổi Form.Icon tức thì; ICO gồm 16/24/32/48/64/128/256 để Windows chọn theo title bar/taskbar/DPI. Dispose icon cũ; bỏ GetHicon không được DestroyIcon trong luồng cũ.
- Kiểm `theme-logo` trên form: light → dark → light; so sánh ảnh header, đọc WM_GETICON cả ICON_SMALL và ICON_BIG. Độ sáng trung bình chữ trên icon lớn chuyển ~50,8 → 250,8. Kiểm dark startup/cancel, render UI. Xác minh icon của cửa sổ đang chạy; icon shortcut ghim tĩnh/Explorer do Windows quản lý không được sửa trong tác vụ này.
- Portable hiện có 40 file runtime. Cấu hình cá nhân không bị ghi đè; không ghi vào model Tekla.

## Prompt đã dùng

```text
EDIT TARGET: attached small original corporate logo. Strict fidelity image restoration ONLY: upscale and sharpen the exact existing logo, preserve every contour, proportion, spacing, composition and original color. Do not redesign, stylize, reinterpret, add or remove anything. Produce clean sharp antialiased edges at high resolution, transparent RGBA background. The mark has three orange quadrants and a green upper-right quadrant, white precise shapes, exactly as source. Preserve the original dark blue-gray Japanese lettering exactly: 株式会社辻鉄商工. Do not typeset in a different font or alter any glyph. Preserve original canvas aspect ratio 190:156 and exact relative positions: top logo mark approximately x42..150,y0..108, lettering across bottom y132..155 in the 190x156 reference. No extra margins, no white backdrop, no glow, no shadow, no outline, no invented details. Only improve edge clarity and resolution of this same image. Aim 1520x1248 if possible.
```

Prompt là yêu cầu ban đầu, không phải khẳng định kết quả giữ đúng mọi invariant. Các khác biệt đã nêu ở trên; bản kết quả được người dùng chọn rõ ràng.

## Prompt chỉnh chữ mỏng đã áp dụng

```text
Edit image 1. CHANGE ONLY THE BOTTOM JAPANESE LETTERING TO CLEARLY THIN REGULAR WEIGHT. Current letters are extremely bold. They must become visibly much thinner, roughly one third current stroke width, with slender stems, wide clear counters. The letter height stays the same; do NOT just shrink the text. Image 2 is the original thin-letter reference. Match its regular-weight Japanese sans-serif/Gothic appearance but render beautifully sharp at high resolution. Output exact text 株式会社辻鉄商工, same 8 characters, no changes to content: 株 式 会 社 辻 鉄 商 工. Keep glyph details intact, don't omit thin strokes, punctuation none. Reference numerical target: where original image-1 text is about 160 pixels high, stems should be only about 9-12 pixels wide, not 25-40 pixels. Use regular/light typography, never semibold/bold/heavy. Keep baseline, text-line overall width and position and dark gray-blue color. Preserve the upper orange-green-white four-quadrant emblem completely unchanged; no redesign, same canvas and image proportions, same spacing. Transparent RGBA background. No background color, no shadow, no border, no added effects. The ONLY intentional difference should be visibly thinner, sharp bottom lettering matching image 2.
```

Tool: built-in `image_gen.imagegen`, `transparent_background=true`. Kết quả dùng: `exec-97c74c4b-5a59-4268-babb-b366b42e788a.png`, sao chép nguyên bytes vào source/portable.
