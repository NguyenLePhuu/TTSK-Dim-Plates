# Sửa phát lại câu và lịch sử dài — 2026-10-11

## Lỗi đã tái hiện trước khi sửa

Bộ `ReplayRecoveryTests` chạy trên portable trước sửa có 9 kiểm tra thất bại. Không dùng API thật hoặc nội dung cuộc họp của người dùng.

- Tracker quên ID dòng đã biến mất sau 2 giây hoặc vượt 256 dòng. Cùng ID/người nói/nội dung xuất hiện lại sau Dừng/Tiếp tục hoặc snapshot UIA trống bị cấp Sequence mới và dịch lại.
- Khi nguồn đổi A → B → A trong lúc request A chưa xong, queue còn revision A mới. Kết quả A đang chạy vẫn đúng nguồn nên được công bố. Lần dispatch sau lấy lại A trong queue vì `IsCurrent` chỉ so nguồn hiện tại; nó gửi HTTP thừa rồi chờ công bố một Sequence đã hoàn tất. Với một worker, câu tiếp theo có thể bị kẹt; hai worker cũng có thể giữ việc thừa không kết thúc.

## Thay đổi luồng xử lý

- Nhớ dòng đã biến mất theo ID suốt vòng đời coordinator; chỉ hồi phục khi ID, người nói và nguồn tương thích. Lượt thật có ID khác, dù nói cùng câu, vẫn riêng biệt. Không lọc trùng toàn cục theo chữ hoặc dùng bản dịch để nhận diện câu nguồn.
- Đăng ký nguồn của các update vừa flush rồi mới đăng ký snapshot hiện tại. Queue loại revision lỗi thời và mục có cùng Sequence/nguồn đã hoàn tất. Cổng nhận việc và cổng công bố cùng kiểm `IsCompleted`; công bố cũng dọn bản trùng trong queue. Một bản sửa thực sự khác nguồn vẫn được xử lý và cập nhật đúng dòng.
- `AsyncBoundedQueue.RemoveWhere` cập nhật queue/semaphore dưới cùng khóa; coordinator gọi dưới revision gate. Giữ FIFO, retry, pause/resume, buffer kết quả và giới hạn worker cũ. Không thay key, prompt, model hoặc cổng quota.

## Làm lại lịch sử từ luồng cập nhật trước giới hạn

Thay cách giữ mọi `HistoryCardControl` bằng `MeetingHistoryRow` chứa snapshot dữ liệu, phiên/lượt, revision và kích thước. Cập nhật vẫn dựa trên SessionId/Sequence, giữ quy tắc revision và bản dịch nháp trước đó. Không có giới hạn 200 lượt, không xóa lịch sử cũ khi thêm dòng mới.

`MeetingHistoryPanel` chỉ tạo control cho vùng nhìn thấy và hủy control khi cuộn ra ngoài. Cuộn đến câu cũ tái tạo thẻ từ dữ liệu, không gọi coordinator/API. Tìm dòng đầu bằng tìm kiếm nhị phân trên vị trí; giữ neo đọc khi lịch sử hoặc kích thước thay đổi. Đo chiều cao bằng cùng phép đo chữ với TextBox, dùng font chung và cache theo width/scale/DPI thay vì tạo control đo cho mỗi dòng. Font riêng của thẻ được dispose cùng thẻ.

Toàn bộ lịch sử vẫn ở RAM đến Xóa phiên/đóng cửa sổ. Bộ nhớ dữ liệu tăng theo thời lượng cuộc họp; số control native chỉ theo viewport. Đổi chiều rộng/chế độ cần đo lại lịch sử, nên vẫn có chi phí tăng theo số dòng; đây không phải cam kết thời gian không đổi cho lịch sử vô hạn.

## Xuất text

Nút Xuất file text ghi thẳng `Environment.SpecialFolder.DesktopDirectory`, bao gồm Desktop được chuyển sang OneDrive. Tên `dd-MM-yy.txt`, ví dụ `11-10-26.txt`; lần trùng dùng `11-10-26 (2).txt`. `FileMode.CreateNew` giữ file đã có kể cả hai cửa sổ xuất đồng thời. UTF-8 BOM giữ nguyên chữ Nhật/Việt, có số lượt, thời gian, người nói và trạng thái nháp/gần nghĩa. Xuất không phụ thuộc các thẻ đang hiển thị.

## Kiểm chứng

- `Run-ReplayRecovery.ps1`: gap dài, 400 dòng biến mất/hiện lại, A/B/A với 1 và 2 worker, queue rỗng trước khi câu tiếp theo xuất hiện, Dừng/Tiếp tục; bản sửa đạt toàn bộ.
- `Run-OrderedPipeline.ps1`: 44 kiểm tra; FIFO, sửa câu sớm/muộn, buffer, 429, retry và pause. Một lần chạy sớm gặp kiểm tra timing 429 thất bại; đã đối chiếu portable cũ và chạy lại bản cuối, cả hai đạt.
- `Run-GoogleBatch.ps1`: 121 kiểm tra offline, gồm sửa head và revision trong batch, pause/quota/cancel, ánh xạ nguồn và bản dịch.
- `Run-CaptionIdentity.ps1 -Offline`: 26 kiểm tra dấu câu, rút ngắn nguồn thật, thay số, ID riêng và sửa câu đang dịch. Chế độ này không yêu cầu file ghi cuộc họp live cũ.
- `Run-Bottleneck.ps1`: 20 kiểm tra offline, gồm lỗi dịch giữ nguồn và dừng vòng retry chất lượng.
- `Run-HistoryViewport.ps1`: 5.000 dòng mỗi theme, số control/USER handle, quay lại câu đầu và sửa câu ngoài viewport, native selection, resize/chế độ đọc, xuất đủ 5.000 lượt, clear và tên file/UTF-8. Tạo form thử ngoài vùng nhìn thấy; ảnh DrawToBitmap và log ở `.codex-artifacts/history-viewport/tests/`.

Release x64/C# 7.3/.NET Framework 4.8 được cập nhật bằng `Update-Portable.ps1 -BuildOnly -NoPause`, kiểm 40 file SHA-256. Không gọi dịch vụ thật, không chạy DIM hoặc ghi folder model Tekla. Không commit/push/phát hành. Dữ liệu kiểm thử xuất trong `.codex-artifacts/`, không đặt file giả lên Desktop người dùng.
