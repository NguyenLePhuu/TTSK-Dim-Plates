# Azure Translator trong Dịch họp — 2026-10-08

## Sử dụng

1. Mở Shortcut Settings, tìm thẻ **API dịch họp — NVIDIA / Azure**.
2. Dán key Azure vào ô nhập chung. Key NVIDIA có tiền tố nvapi-; key dạng 32/64 ký tự hex tự chọn Azure. Đây chỉ là nhận dạng định dạng, không phải xác thực. Nếu định dạng chưa được nhận dạng, chọn dịch vụ bằng nút tương ứng.
3. Nhập Region đúng như Azure Portal; tài nguyên Global dùng `global`. Nhấn **Lưu & kiểm tra**. Ứng dụng chỉ lưu sau khi dịch vụ trả bản dịch kiểm tra hợp lệ.
4. Mở Dịch họp, chọn **AZURE F0** hoặc **NVIDIA** ở góc trên phải, rồi Bắt đầu. Muốn đổi dịch vụ, Dừng phiên trước.

Máy giữ đồng thời hai key. Xóa/lưu key của dịch vụ này không thay key dịch vụ kia. Không tự chuyển dịch vụ khi thiếu key hoặc khi dịch lỗi. F0 là gói được chọn khi tạo tài nguyên Azure; ứng dụng không thể biến một tài nguyên trả phí thành F0 bằng cấu hình máy khách.

## Luồng và bảo mật

Azure dùng endpoint công khai cố định:

`POST https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=ja&to=vi&textType=plain&profanityAction=NoAction`

Mỗi lần dịch gửi một JSON array chứa Text nguyên văn được coordinator cung cấp, giữ dấu câu và khoảng trắng. Azure trả tiếng Việt trực tiếp, không qua tiếng Anh, Riva hoặc bước hiệu đính NVIDIA. Luồng đọc Teams, nhận dạng người nói, cập nhật câu và thuật toán NVIDIA hiện có được giữ nguyên.

Key gửi qua Ocp-Apim-Subscription-Key; tài nguyên regional thêm Ocp-Apim-Subscription-Region, Global bỏ header Region. Không hỗ trợ endpoint riêng/VNET trong lần nâng cấp này.

Windows Credential Manager lưu hai credential riêng:

- NVIDIA: `TTSKDimPlates/NVIDIA_API_KEY` (giữ target cũ).
- Azure: `TTSKDimPlates/AZURE_TRANSLATOR` (key và Region trong một credential).

Không ghi key vào file cấu hình, log hoặc thông báo lỗi. File `%LOCALAPPDATA%/TTSK/MeetingTranslation/provider.cfg` chỉ lưu lựa chọn dịch vụ.

Coordinator Azure dùng ba worker theo cơ chế ưu tiên hiện có, giữ các câu cũ đang chờ. Mỗi yêu cầu giới hạn 8 giây; mạng chậm/lỗi tạm thời có thể cần thử lại nên không bảo đảm tổng thời gian dưới 8 giây. HTTP 429 áp dụng Retry-After dùng chung giữa các worker. HTTP 401/403 ngăn gửi lặp trong phiên; nguồn câu lỗi vẫn còn. Cần sửa key/Region/quota, rồi dừng và bắt đầu lại. Không hứa tốc độ hoặc hạn mức miễn phí chưa đo trên tài khoản thật.

## Phạm vi và kiểm chứng

Chỉ bổ sung ba file sản phẩm có sẵn: MeetingTranslationService.cs, MeetingTranslatorForm.cs và ShortcutSettingsForm.cs. Không thêm file .cs sản phẩm; không thay thuật toán bản vẽ/DIM.

- Build Release x64 thành công; portable cập nhật từ `.codex-artifacts/portable-update/20261008-124042-2d811be5/build`.
- ProviderTests: 62 kiểm tra, gồm request ja → vi chính xác, nguồn nguyên văn, header/Region, lỗi HTTP, hủy, thử lại, ưu tiên câu mới/giữ câu cũ, credential tạm độc lập, chọn provider, nhập key và UI.
- Hồi quy: VerbatimTests 18, RivaSourceContractTests 38, LivePriorityTests 27. Tổng **145 kiểm tra đạt**.
- Preview sáng/tối và cửa sổ hẹp trong `.codex-artifacts/azure-provider/`.
- Chưa kiểm tra Azure thật do chưa có key. Không coi HTTP giả lập là xác nhận tốc độ/chất lượng Azure thật, không thử bản vẽ Tekla hoặc gọi NVIDIA thật trong lần này.
- Backup trước sửa: `backup/azure-provider-20261008-123107/manifest.csv`.

## Tài liệu Microsoft đã đối chiếu

- [Translate API v3](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate)
- [Xác thực và Region](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/authentication)
- [REST quickstart và tạo tài nguyên F0](https://learn.microsoft.com/en-gb/azure/ai-services/translator/text-translation/quickstart/rest-api)
## Sửa menu chọn dịch vụ sau phản hồi — 2026-10-08

Nguyên nhân lỗi `Cannot access a disposed object. Object name: ContextMenuStrip`: handler Closed gọi Dispose ngay trong chuỗi xử lý nhả chuột. WinForms sau đó tiếp tục OnItemClicked → SetVisibleCore → ReparentToDropDownOwnerWindow và truy cập Handle đã bị hủy.

MeetingTranslatorForm giờ giữ một _providerMenu trong suốt vòng đời cửa sổ, đóng/mở lại cùng menu, đồng bộ dấu chọn trước khi mở. Closed chỉ cập nhật hình mũi tên. Form Dispose mới giải phóng menu. Không bắt/nuốt lỗi để che nguyên nhân.

Chỉ sửa MeetingTranslatorForm.cs. Nút có nền dịu, chấm màu xanh lá NVIDIA/xanh dương Azure, mũi tên tự vẽ; menu hai dòng với dấu chọn và màu sáng/tối. Không đổi thuật toán đọc phụ đề, dịch hoặc lưu key.

Build Release x64: `.codex-artifacts/portable-update/20261008-135700-f0f90f3d/build`.

Kiểm tra trên desktop Windows riêng, không chiếm bàn phím/chuột người dùng:

- MenuTests: 165 kiểm tra đạt. Tái hiện được ObjectDisposedException trên mẫu handler cũ; bấm bằng thông điệp chuột Windows trên menu thật của bản sửa, 24 lần chọn qua lại ở hai theme, Escape, đóng ngoài, bấm lại, trạng thái bận, đóng form khi popup còn mở, mở cửa sổ mới, kích thước tối thiểu.
- ProviderTests: 62 kiểm tra hồi quy đạt trên cùng EXE; không gọi dịch vụ thật và không thay key người dùng.
- Tổng lần sửa này: 227 kiểm tra đạt. Ảnh xem trước và log: `.codex-artifacts/azure-menu-fix/`.
- Backup trước sửa: `backup/azure-menu-fix-20261008-135534/manifest.csv`.