# Ô tiếng Nhật chỉ hiện lượt nói mới nhất

## Vấn đề và cách sửa

Coordinator trước đây ghép toàn bộ snapshot bằng `string.Join`, nên các dòng Teams vẫn giữ trên màn hình đều xuất hiện ở ô đang nhận. Kết quả dịch về sau đó còn ghi đè ô này bằng câu đã gửi trước đó.

- Tracker cung cấp `LatestCaptionText` theo Sequence của lượt nói. Hiện trọn nội dung lượt mới nhất, bổ sung chữ ngay khi đọc được; không chờ NVIDIA và không cắt thành từ cuối cùng.
- Đổi thứ tự UIA, sửa một lượt cũ hoặc thay node cùng lượt không làm preview quay ngược. Nếu node mới nhất mất nhưng các node cũ còn, giữ câu vừa nhận; khi vùng phụ đề trống thì xóa preview.
- `PartialChanged` là nguồn nội dung duy nhất cho preview Nhật. Kết quả dịch chỉ cập nhật tiếng Việt và lịch sử. Overlay lưu riêng nguồn Nhật đã dịch để không ghép bản dịch cũ với câu đang nhận mới hơn.
- Toàn bộ lượt nói vẫn vào hàng đợi dịch. Giữ nguyên chống trùng, settling, cache, model, prompt, quality guard và lịch sử.

## Kiểm chứng

- Release x64 thành công; cập nhật portable bằng `Update-Portable.ps1 -BuildOnly -NoPause`.
- 37 kiểm tra chuyên biệt đạt: snapshot nhiều dòng, đổi thứ tự, câu đang tăng chữ, sửa câu cũ, node biến mất, snapshot trống, lượt lặp thật, Guest mirror/node handoff, đổi người nói, luồng coordinator và UI dark/light.
- Mô phỏng đúng bốn dòng trong ảnh: preview chỉ có `止ま。`, cả bốn câu vẫn vào translator đúng thứ tự và có đủ bốn thẻ lịch sử.
- 77 kiểm tra hiệu năng/hàng đợi/translator giả lập trước đó vẫn đạt.
- Đã xem ảnh dựng UI ở cả light/dark; không điều khiển ứng dụng của người dùng. Đây là kiểm chứng offline, chưa xác nhận bằng cuộc họp/NVIDIA thật trong lượt sửa này.
- Công cụ, ảnh và log: `.codex-artifacts/meeting-latest-preview/`. Bản gốc đã sao lưu và kiểm hash: `backup/meeting-latest-preview-20261006/`.

Thứ tự ban đầu vẫn dựa trên thứ tự node mà bộ đọc Teams cung cấp. Các snapshot tiếp theo dùng danh tính lượt đã theo dõi; dữ liệu UIA không cung cấp thời gian phát âm thực tế.
