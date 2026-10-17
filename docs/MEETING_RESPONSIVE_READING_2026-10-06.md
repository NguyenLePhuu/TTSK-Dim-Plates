# Chữ dịch lớn theo kích thước cửa sổ

Chỉ sửa UI trong `MeetingTranslatorForm.cs`. Bộ đọc Teams, startup, hàng đợi, NVIDIA và thuật toán dịch không đổi.

## Bố cục

- Hệ số đọc tính lại từ kích thước client, không nhân dồn khi kéo/maximize/restore; trừ DPI và giới hạn 100–180%, bước 5% để giảm việc thay font khi kéo cửa sổ.
- Chữ Việt/Japanese trong live panel và history tăng cùng hệ số. Tên/ảnh người nói tăng tối đa 135%; timing tăng tối đa 120%; nút điều khiển giữ gọn.
- Live panel tăng chiều cao theo nội dung, tối đa 30% chiều cao cửa sổ để giữ diện tích lịch sử. Câu quá dài có tooltip và toàn bộ nội dung trong thẻ lịch sử.
- Thẻ lịch sử tự xuống dòng, đo lại chiều cao và chừa riêng cột timing. Khoảng đệm/badge tính cả DPI. Ẩn/hiện tiếng Nhật đo lại bố cục.
- Khi người dùng đang cuộn lên đọc, resize giữ thẻ đang đọc; khi ở cuối vẫn tự theo bản dịch mới. Thẻ mới dùng đúng hệ số của cửa sổ hiện tại.
- Font tạo khi resize được quản lý và giải phóng khi thay/đóng.

## Kiểm chứng bản Release x64

332 kiểm tra UI và 37 kiểm tra preview/luồng hiển thị đều đạt. Đã xem ảnh dựng light/dark với câu dài, tên dài, nhiều người nói; chạy form thử ngoài màn hình hoặc opacity 0, không điều khiển ứng dụng đang dùng.

| Client size ở 96 DPI | Hệ số | Chữ Việt live | Chữ Việt history |
|---|---:|---:|---:|
| 880 × 620 | 1,00 | 14 pt | 12 pt |
| 1040 × 780 | 1,00 | 14 pt | 12 pt |
| 1366 × 728 | 1,20 | 16,8 pt | 14,4 pt |
| 1920 × 1040 | 1,60 | 22,4 pt | 19,2 pt |

Đã kiểm maximize/restore thực của form thử, lặp resize, thẻ mới lúc phóng to, thẻ không có tên người nói, giữ thẻ đang đọc khi cuộn, ẩn/hiện Nhật và câu dài không chồng timing/thẻ kế tiếp. Chưa xác nhận đa màn hình/DPI khác 96 hoặc cuộc họp/NVIDIA thật trong lượt UI này.

Ảnh/log/harness: `.codex-artifacts/meeting-responsive-reading/`. Backup hash-verified: `backup/meeting-responsive-reading-20261006/`. Build bằng `Update-Portable.ps1 -BuildOnly -NoPause`, đối chiếu EXE thử và 41 file trong manifest portable.
