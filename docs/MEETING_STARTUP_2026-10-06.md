# Giảm chờ bản dịch câu hiện tại lúc bắt đầu

## Quan sát

Người dùng xác nhận vẫn dịch được nhưng giai đoạn bắt đầu vào Teams chậm. Đọc chỉ số trực tiếp, không điều khiển cửa sổ: tìm thấy hai cửa sổ Teams; cửa sổ có phụ đề đọc mất 194 ms và có 7 caption; cửa sổ còn lại mất 88 ms và không có vùng phụ đề. Đây là một lần đo, không phải cam kết cho mọi cuộc họp.

Coordinator cũ đưa toàn bộ caption còn hiện từ trước lúc Bắt đầu vào hàng đợi tuần tự. Vì vậy câu hiện tại phải chờ dịch các câu tồn sẵn. Việc preview chỉ hiện câu mới nhất khiến độ trễ này dễ nhận thấy hơn; phép đo không chứng minh thời gian tìm vùng Teams tăng do thay đổi preview.

## Thay đổi

- Chỉ ở snapshot có chữ đầu tiên của phiên: bắt đầu dịch từ lượt mới nhất. Không dịch lại các caption cũ vốn đã có trước khi bấm Bắt đầu.
- Giữ các entry cũ và đánh dấu nội dung hiện tại đã xử lý để chúng không bị flush vào hàng đợi khi biến mất. Nếu có revision mới sau khi bắt đầu thì vẫn dịch revision đó.
- Các lượt xuất hiện sau khởi tạo vẫn giữ đủ, đúng thứ tự; câu lặp lại ở lượt mới vẫn được dịch. Snapshot trống/lỗi tìm vùng phụ đề không làm mất bước khởi tạo này.
- Ô Nhật chỉ hiện câu mới nhất như bản trước. Không sửa bộ đọc UIA, settling, NVIDIA model/prompt/cache, kiểm định chất lượng hoặc xử lý bản vẽ.
- Nạp key/cấu hình một lần; trạng thái sau Bắt đầu báo đang tìm vùng phụ đề, không báo đã đọc trước khi có snapshot.

## Kiểm chứng

- 17 kiểm tra startup đạt. Mô phỏng 7 caption ban đầu, mỗi lượt dịch 200 ms: bản dịch của câu hiện tại trước là 1.896 ms/7 request, sau là 612 ms/1 request. Chênh lệch này đo loại bỏ chờ hàng đợi, không phải tốc độ mạng/NVIDIA hay tốc độ kết nối Teams.
- 37 kiểm tra preview/UI và 77 kiểm tra hiệu năng/quality/hàng đợi đạt. Bộ preview giữ tùy chọn đối chiếu dịch toàn bộ snapshot; startup tests kiểm mặc định sản phẩm mới.
- Đã build Release x64 bằng script portable, đối chiếu EXE đã test và hash manifest.
- Thử NVIDIA bằng câu mẫu cố định không lấy từ cuộc họp: cấu hình/key đọc 11 ms, khởi tạo translator tổng khoảng 20–21 ms; lời gọi API từ harness không thành công (`HttpRequestException`/`WebException`), nên chưa xác nhận tốc độ dịch API thật. Không ghi key hay nội dung cuộc họp vào log. Không kết luận ứng dụng người dùng mất dịch từ lỗi harness này.
- Log/công cụ: `.codex-artifacts/meeting-startup/`; bản gốc: `backup/meeting-startup-20261006/`.
