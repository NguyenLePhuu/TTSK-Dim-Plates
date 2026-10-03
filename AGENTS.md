# Làm việc với TTSK Dim Plates

- Không tạo, ghi hoặc chỉnh sửa bất kỳ file/thư mục nào trong folder model Tekla. Cache, log, cấu hình, dữ liệu điều phối và công cụ kiểm tra phải nằm ngoài model; kiểm tra kết nối chỉ đọc.

- Đầu phiên làm việc với code, đọc `PROJECT_MAP.md` cùng thư mục để chọn đúng nhóm chức năng. Bản đồ ngắn dùng đường dẫn tương đối, áp dụng được trên máy khác.
- Tra khai báo khi cần bằng `pwsh -NoProfile -File scripts/Update-CodeMap.ps1 -Find TênHàm -File TênFile.cs -Kind Method`; mặc định tối đa 12 kết quả. Thu hẹp truy vấn nếu có nhiều kết quả; dùng `rg` tìm caller/chuỗi reflection rồi đọc đúng đoạn source liên quan.
- Không đọc trực tiếp cache `.codex-artifacts/code-map-tool/catalogue-v2.json` hoặc xuất toàn bộ khai báo vào chat. Query tự kiểm hash và cập nhật vị trí theo source hiện tại. Công cụ cần PowerShell 7 và .NET SDK ≥8; nếu thiếu, dùng bảng trong map + `rg -n`.
- Ưu tiên bổ sung vào file chức năng hiện có, dùng chú thích tiếng Việt ngắn gọn. Ghi đơn vị, tác động và phạm vi của thông số; phân biệt thiết lập với trạng thái lần chạy và hằng không còn nơi đọc.
- Giữ tên lớp/namespace/điểm gọi reflection, thứ tự khởi tạo partial và thay đổi có sẵn trong working tree. Không khôi phục/xóa thay đổi của người dùng để làm sạch Git.
- Quy ước dọn rác của chủ dự án: chỉ bảo vệ file bắt buộc để build/chạy ứng dụng. Test, log, cache, dump và bản sao bản vẽ trước live-test không thuộc nhóm “không xóa”. Không lưu phụ thuộc sản phẩm vào `tests/` hoặc `.codex-artifacts/`; công cụ dùng cache phải tự tạo lại. Việc dọn thực hiện sau khi lệnh đang chạy đã kết thúc.
- Dự án dùng C# 7.3, .NET Framework 4.8, Tekla 2025; kiểm tra Release x64. Chỉ sửa chú thích thì đối chiếu token và IL khi có công cụ; không gọi DIM trên bản vẽ thật để kiểm tra chú thích.
- Sau sửa source C# sản phẩm, cập nhật portable bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause` và kiểm SHA-256. Với thay đổi chỉ tài liệu/công cụ tra cứu, kiểm tra tài liệu/công cụ; không cần rebuild runtime. Không commit/push/phát hành nếu chưa được yêu cầu.
- Trước kết thúc, cập nhật và chạy `Update-CodeMap.ps1 -Check`; sửa phần mô tả luồng trong bản đồ nếu hành vi đã thay đổi. Không tuyên bố đã thử bản vẽ thực tế khi chỉ build/test offline.
