# Rà soát và nâng cấp tốc độ Dịch họp — 06/10/2026

## Kết luận

Bản trước chưa tối ưu hết khoảng chuyển từ nhận chữ sang dịch. Câu đã có dấu kết thúc vẫn phải chờ 800 ms ổn định, chu kỳ đọc 300 ms làm phép thử mất khoảng 971 ms trước khi gọi dịch. Hàng đợi giữ cả các revision cũ và con số trên UI chưa gồm thời gian chờ. Ba yêu cầu NVIDIA tuần tự vẫn là chi phí chính của câu mới; không có số đo máy chủ hợp lệ trong lượt kiểm tra này vì kết nối NVIDIA thất bại ở cả bản trước và bản sau.

## Thay đổi đã áp dụng

- Câu Nhật kết thúc bằng 。！？!? ổn định 400 ms được gửi sớm; câu còn dở giữ ngưỡng 800 ms. Chu kỳ đọc giảm từ 300 xuống 200 ms.
- Chỉ kiểm tra HWND Teams đã chọn, không quét toàn bộ cửa sổ/process mỗi vòng. Chuẩn hóa snapshot một lần; partial/state chỉ gửi tới UI khi thực sự đổi; lỗi đọc giống nhau nhắc tối đa một lần mỗi 3 giây.
- Hàng đợi tối đa 32 phần tử thay revision cùng Sequence ngay tại vị trí cũ. Các lượt nói khác nhau giữ thứ tự, không âm thầm bỏ câu. Khi đầy, producer chờ tín hiệu có chỗ thay vòng polling 50 ms.
- Cache tối đa 64 kết quả pivot Riva trong bộ nhớ của một phiên: khóa theo cặp ngôn ngữ và chữ chính xác. Hai bước này không nhận ngữ cảnh trong thuật toán hiện tại. Llama editor luôn chạy lại theo ngữ cảnh mới, rồi toàn bộ kiểm tra tiếng Việt, số/mã và thuật ngữ vẫn chạy. Cache xóa khi guard từ chối và khi hủy translator; không ghi nội dung cuộc họp ra đĩa.
- HTTP response được đọc trong phạm vi cancellation/timeout của yêu cầu, giới hạn buffer 1 MB. Không đổi model, prompt, số lần retry hay fallback biên tập.
- Chặn khởi động hai worker cho cùng coordinator; nhiều lệnh Dừng dùng chung một tác vụ dọn dẹp.
- UI đổi từ “Dịch” sang “Tổng”. Tooltip phân tách chờ câu, hàng đợi, dịch, chuyển lên UI, số request NVIDIA và số pivot tái dùng. Đồng hồ Stopwatch đo khoảng thời gian để tránh sai khi đồng hồ hệ thống thay đổi.

## Số đo có thể đối chiếu

Cùng caption Nhật có dấu kết thúc, cùng bộ dịch giả lập trễ 200 ms; chạy bản source trước sửa và bản source sau sửa trong thư mục build riêng.

| Chỉ số | Trước | Sau, build cuối |
|---|---:|---:|
| Bắt đầu quan sát → gọi hàm dịch | 971 ms | 439 ms |
| Bắt đầu quan sát → callback kết quả | 1.177 ms | 647 ms |
| Sự kiện state khi caption không đổi trong phép thử | 7 | 1 |
| Sự kiện partial khi caption không đổi trong phép thử | 7 | 1 |

Đây là phép thử tổng hợp, không phải tốc độ dịch cuộc họp thực tế. Thời gian xử lý UIA, mạng và model sẽ khác. “Tổng” trên UI tính từ lần ứng dụng quan sát phiên bản chữ đang được dịch, không từ lúc người nói phát âm và không gồm độ trễ nhận dạng của Teams; phần “Hiện” đo chuyển callback lên luồng UI, không đo pixel hoàn tất vẽ.

Phép thử HTTP giả lập xác nhận: lần đầu gọi đủ 3 yêu cầu; câu lặp cùng chữ chỉ gọi 1 yêu cầu editor, tái dùng 2 bước Riva. Đầu ra không đạt guard vẫn bị từ chối và retry thực sự gọi lại các bước pivot. Đây là tiết kiệm số request, không phải cam kết mọi câu dịch nhanh gấp ba.

## Kiểm tra

- 77 assertion hiệu năng/lifecycle/quality/queue đạt (bao gồm chuỗi thử liên tiếp 49 revision); 17 assertion UI và hồi quy đạt.
- Kiểm tra câu hoàn chỉnh/câu dở, coalesce khi queue đầy, FIFO giữa các lượt khác nhau, hủy producer bị chặn, drain khi đóng, cancellation khi response body chậm, cache không bỏ editor/guard, số đo tổng khớp các chặng.
- Kiểm tra lại Guest mirror, UIA node thay đổi, câu nói lặp thật, dark/light, cập nhật tổng độ trễ, wrap lịch sử, ẩn câu Nhật, maximize/restore và xóa phiên. Ảnh UI dùng dữ liệu mẫu.
- Release x64 build thành công; portable khớp đúng EXE đã thử; toàn bộ manifest SHA256 được xác minh. Code map 57/57 file khớp.
- Nguồn baseline: `backup/meeting-performance-20261006/`. Harness, kết quả và ảnh: `.codex-artifacts/meeting-performance/`. Chỉ hai file sản phẩm MeetingTranslationService.cs và MeetingTranslatorForm.cs được sửa trong lượt này; không đổi thuật toán DIM.

## Giới hạn và theo dõi thực tế

Đã thấy 2 cửa sổ Teams nhưng bộ đọc không lấy được vùng phụ đề trong lần probe. Không lấy/gửi nội dung cuộc họp để thử. NVIDIA được thử bằng câu tổng hợp “図面を確認します。” với key đang lưu, nhưng kết nối thất bại sau retry ở cả hai bản; không thay đổi key hay cấu hình mạng. Cần phiên có Live captions và kết nối NVIDIA hoạt động để xác nhận p50/p95 thực tế.

Worker vẫn tuần tự để ngữ cảnh được cập nhật đúng. Nếu NVIDIA chậm hơn tốc độ các lượt nói khác nhau trong thời gian dài, queue vẫn có thể đầy; ứng dụng giữ cảnh báo backpressure hiện tại. Cache chỉ giúp chữ đã gặp, không tăng tốc hai bước phụ thuộc nhau của một câu hoàn toàn mới. Chu kỳ 200 ms là khoảng nghỉ sau một lần đọc UIA, không đảm bảo 5 snapshot/giây nếu UIA chậm.

Chạy bản mới: mở lại `portable/TTSK Dim Plates.exe`, vào Dịch họp và xem tooltip của con số Tổng khi có kết quả.
