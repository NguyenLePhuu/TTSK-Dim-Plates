# Nhật ký dự án · Chức năng 11

Bấm **⌬** ở chân TTSK. Chọn khoảng ngày, Tuần này hoặc Tháng này, rồi Xuất TXT.

- Gộp theo **Project number + tên**, kể cả dự án xuất hiện ở nhiều model hoặc nhiều ngày.
- Mỗi dự án có các ngày và giờ đóng cuối tương ứng; A → B → A giữ một dòng/ngày cho A và một dòng/ngày cho B.
- Tên và mã là ô chỉ đọc không viền: quét chọn và Ctrl+C để copy.
- Mỗi dự án hiện tối đa hai hàng ô ngày; nhiều ngày thì chuyển trang bằng ‹ / › ngay trong khối. Số cột tự đổi theo chiều rộng cửa sổ. TXT luôn xuất toàn bộ ngày đã chọn, không chỉ trang đang xem.
- Không hiện phiên mở, ngày chưa có giờ đóng, tổng giờ, thời gian nghỉ hoặc lời giải thích trong báo cáo.
- Cùng một phiên TTSK có nhiều dự án thì giờ đóng phiên áp dụng cho các dự án đã ghi nhận trong ngày đó.
- Cụm chọn ngày, lịch xổ xuống, nút, khung/tiêu đề cửa sổ, thẻ dự án và thanh cuộn dùng giao diện riêng, đồng bộ dark/light của TTSK lúc mở báo cáo.
- Kéo thanh tiêu đề để di chuyển; nhấp đúp để phóng to/khôi phục; kéo mép cửa sổ để đổi kích thước.
- Qua ngày mới tự tạo phiên theo ngày. Giữ giờ đóng thực sự đã ghi hôm trước; không gán 23:59 hay thời điểm tự tách ngày thành giờ đóng.

TXT chỉ có khoảng ngày, mã/tên dự án và các dòng ngày–giờ đóng. UTF-8 BOM giữ tiếng Nhật.
Ví dụ bố cục: `.codex-artifacts/WorkJournal/TTSK_GioLam_Mau.txt` (giờ và dự án B là dữ liệu minh họa).

## Lưu trữ

`%LocalAppData%\TTSK Dim Plates\WorkJournal`: mỗi phiên một XML; thay tệp nguyên khối khi lưu.
Tương thích nhật ký của bản trước. Không sửa dữ liệu model/bản vẽ Tekla.
Chỉ đọc GetConnectionStatus / GetProjectInfo / GetInfo; kiểm tra mỗi 30 giây lúc TTSK rảnh,
khi mở báo cáo và trước lúc đóng. Model đổi quá nhanh hoặc lúc TTSK bận có thể chưa được ghi nhận.
Mất kết nối giữ dữ liệu đã đọc. Mất điện/kill/crash không tạo giờ đóng giả.
Giờ dùng đồng hồ máy tính; chỉ có dữ liệu từ khi dùng chức năng này. Không khôi phục công quá khứ.

## Kiểm tra

24 kiểm tra độc lập: gộp dự án qua ngày/model, A–B–A, phiên mở bị loại, giữ giờ cuối,
qua nửa đêm, XML cũ, UTF-8, lỗi tệp và lưu nguyên khối.
UI smoke kiểm tra cả dark/light, lịch riêng chọn ngày, ô copy chỉ đọc, bố cục cửa sổ nhỏ,
thanh cuộn riêng và đóng/hủy đóng. Dữ liệu thử dùng thư mục riêng, không ghi vào nhật ký người dùng.
