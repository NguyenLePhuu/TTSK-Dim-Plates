# Rà soát chú thích và bản đồ Codex — 03/10/2026

Ghi chú cập nhật: báo cáo dưới đây mô tả lần tạo chỉ mục đầy đủ ban đầu. Thiết kế hiện tại đã thu gọn `PROJECT_MAP.md`; các khai báo được tra theo yêu cầu bằng `Update-CodeMap.ps1 -Find`, không còn in toàn bộ vào Markdown. Số liệu hiện tại lấy từ phần độ phủ của map và kết quả truy vấn.

## Thay đổi

- Chỉnh chú thích trong **30 file C#**, gồm toàn bộ **20 file nhận code sau gộp** và các engine liên quan.
- Đầu file nêu nhiệm vụ, luồng gọi, điểm sửa và phạm vi thông số; region quan trọng có tên tiếng Việt cụ thể.
- Bổ sung đơn vị và tác động của nhóm tầng DIM, scale, ngưỡng nhận dạng, cụm lỗ, chamfer/notch, section, mark, UI và updater.
- Đánh dấu **28 khai báo private const không có lần dùng identifier trực tiếp khác trong file**. Không xóa code và không biến chúng thành công tắc mới.
- Tạo [PROJECT_MAP.md](../PROJECT_MAP.md), [AGENTS.md](../AGENTS.md) và [script cập nhật chỉ mục](../scripts/Update-CodeMap.ps1).

## Những chú thích sai/lỗi thời đã sửa

- `PHU_AutoDim_OK-V3`: `DIM_TIER_1..4` và các offset Top/Bottom cũ không được đọc; tầng hiện tại dùng `CurrentDimTierUnit`, mặc định 100 mm view hoặc scale thủ công × 10.
- Dự phòng Auto Scale của tấm lấy `AUTO_SCALE_DIM_VERTICAL_RESERVE`, không phải con số 200 trong chú thích cũ. Chọn view sau chạy lấy danh sách thực, không cố định hai view.
- `PHU_Shape.cs` là luồng H/I; các Shape khác có engine riêng. Công thức tầng là BASE + max(0,tier) × STEP.
- `CHAMFER_DIM_EXTRA_OFFSET` không dùng trực tiếp ở H/Box; C/L còn truyền vào helper rãnh, không thể ghi chung “chamfer = kích thước + 200”.
- Cờ notch Top chỉ chặn những nhánh có kiểm cờ; một số nhánh chuyên biệt có điều kiện khác, không mô tả rằng false luôn tắt mọi DIM notch Top.
- Arrange chỉ move section thường; `mainHorizontal` còn chữ ký tương thích. Presentation đọc vật cản/khung đang hiển thị, không phải bộ tự căn khoảng view.
- Engine grid cột không chứa điểm gọi UI Slot 07; điểm gọi ở MainForm và nhóm ColumnConnections. Region ColumnGrid trong ColumnConnections là planner grid cột hộp.
- Tấm/Shape/Slot 06 có luồng xóa DIM trước tạo. Nishi và một số scope khác có thứ tự khác. Không ghi một chính sách rollback chung cho toàn dự án.
- Revision `Analyze` có thể ghi màu, không gọi là audit chỉ đọc.

## Bản đồ có độ phủ kiểm được

Chỉ mục phân tích **55/55 file C# của project: 53 Compile và 2 macro Content** bằng Roslyn C# 7.3, hợp cấu hình TRACE và DEBUG+TRACE. Có **9.296 khai báo**, gồm **578 khai báo kiểu/delegate**, **4.832 method/constructor/operator**, cùng **42 nhóm hằng cục bộ**. Namespace, partial, nested type, field, property, event, enum, accessor và alias đều có vị trí để tra. Không có file C# production ngoài danh mục project.

Mỗi file đối chiếu số mục xuất ra với cây cú pháp. Loại khai báo không hỗ trợ làm generator báo lỗi thay vì lặng lẽ bỏ qua. SHA-256 bao gồm cả chú thích nên đổi code hoặc số dòng đều khiến `-Check` yêu cầu cập nhật. Danh mục phụ gồm build configuration, reference, resource, cấu hình, script, test và tài liệu có trên máy.

Đây là độ phủ **cấu trúc nguồn**, không phải chứng minh 100% hành vi thuật toán hoặc mọi trạng thái Tekla. Phần mô tả nghiệp vụ được viết/đối chiếu thủ công; sau sửa hành vi phải cập nhật phần đầu bản đồ. AI tra bản đồ rồi đọc hàm/caller liên quan, không đọc toàn bộ chỉ mục một lượt và không sửa chỉ dựa trên chú thích.

## Kiểm tra

- Token của **183 khai báo cấp namespace/global** trước/sau khớp hoàn toàn; không thay thân hàm, hằng, điều kiện hoặc điểm gọi.
- Release x64 Rebuild với `/warnaserror` thành công: **0 lỗi, 0 cảnh báo**.
- So sánh IL/thành viên/resource/reference trước/sau: **15.450 mục, 0 khác biệt**. Bản portable cuối cũng được so với assembly trước sửa chú thích.
- Kiểm tra bản đồ cũ: `-Check` đã từ chối khi source/tài liệu đổi; sau sinh lại thì đạt.
- Portable cập nhật bằng BuildOnly, xác minh SHA-256 của **39 file runtime**; bốn file `.cfg` giữ nguyên.
- Không chạy DIM hoặc ghi lên bản vẽ Tekla; không cần lặp regression hình học cho thay đổi chỉ chú thích khi token và IL đã khớp. Không commit/push/phát hành.

Snapshot nguồn và log kiểm tra nằm trong `.codex-artifacts/comments-20261003/`; công cụ map tự dựng trong `.codex-artifacts/code-map-tool/`. Đây là artifact phát triển, không phải runtime phụ thuộc.
