# Dịch gối đầu, giữ thứ tự và nguyên văn — 09/10/2026

## Bản ổn định đã sao lưu trước khi sửa/test

`backup/meeting-speed-baseline-20261009-233351/`: 492 file source, tài nguyên, portable, cấu hình hiện có, AGENTS.md và PROJECT_MAP.md. Từng file được đối chiếu SHA-256 sau copy; danh sách nằm trong `SHA256-backup.csv`. EXE baseline: `D484C6277DBAA26C87DA82F097E15557C266611C37C620FDF92808184CF19244`. Không sao chép credential từ Windows Credential Manager. Không ghi folder model Tekla.

## Thay đổi có giới hạn

- Production NVIDIA dùng tối đa hai worker dịch gối đầu. Mỗi worker có translator, HttpClient và metrics riêng. Chỉ lấy câu sớm nhất chưa được worker khác nhận; không cho câu đã ổn định vượt câu trước còn đang settling.
- Kết quả chỉ công bố theo Sequence và source revision hiện tại. Context/lịch sử/preview đi theo cùng thứ tự. Kết quả xong sớm giữ chờ, không đọc tiếp hàng loạt backlog; câu lỗi vẫn giữ đầy đủ và không tạo bản dịch giả.
- Nếu Teams sửa câu đã công bố trong khi hai worker giữ câu sau, kết quả đã xong được giữ trong RAM và trả về queue để nhường slot cho revision trước. Khi tới lượt, dùng lại đúng kết quả, không gửi HTTP trùng. Source/Revision không khớp thì loại kết quả cũ và dịch revision mới. Đây là điều kiện cần để tránh deadlock của publication barrier.
- Hai worker dùng chung cooldown sức khỏe Riva. Chỉ một worker dò Riva lần đầu; nếu Riva chậm/lỗi, worker kia biết ngay và không lặp probe 5 giây. Giữ các ngân sách Riva/direct đã có.
- Cổng dùng chung cho **từng HTTP**, gồm pivot thứ hai và rescue, tôn trọng 429/Retry-After, nhịp gửi sau 429 và lỗi cấu hình. Một request đã gửi trước lúc biết 429 có thể còn chạy; không tự gửi dồn thêm khi hết cooldown.
- Thời gian trong tooltip tách thêm `Chờ đúng lượt`; thời gian xử lý không gộp phần kết quả đã xong nhưng chờ câu trước. `Tổng` vẫn bao gồm tất cả thời gian người dùng thực sự chờ.

Chỉ đổi `MeetingTranslationService.cs`; không đổi model, prompt, glossary, quality guard, ngưỡng nhận bản gần nghĩa, bộ đọc Teams, normalizer/tracker, UI hay thuật toán DIM. Đã đối chiếu các vùng prompt/guard/reader/Google với baseline bằng nội dung source. Riva realtime không dùng context; prompt direct cũng không dùng lịch sử, nên làm trước câu sau không làm mất ngữ cảnh mà đường này vốn đang sử dụng. Google/Azure tiếp tục một worker. Overload delegate thử nghiệm mặc định vẫn tuần tự; bật `orderedPipeline: true` để kiểm riêng luồng mới.

## Kiểm chứng và số đo

- `tests/MeetingTranslation/Run-OrderedPipeline.ps1 -Runtime <runtime>`: 44 kiểm tra. Bao gồm câu sau xong trước, giới hạn công việc đang chạy/chờ, source nguyên văn, revision đang chạy/đã buffer/đã công bố, retry, pause/resume, 429 chung, probe Riva chung, hủy hai worker, 200 câu không mất/lặp/đảo thứ tự.
- `Run-Bottleneck.ps1`: 20; StrictTests: 26; VerbatimTests: 18; FinalReview: 18. Tổng cuối: 126 assertion, chạy trên EXE portable bàn giao. FinalReview/StrictTests vẫn kiểm đường delegate tuần tự, không dùng chúng để kết luận production mới chỉ có một worker.
- API giả lập cùng độ trễ 200 ms/câu, cùng 12 nguồn: khoảng 3,33–3,35 giây tuần tự và 2,14–2,16 giây gối đầu (gồm settling), giảm khoảng 36% tổng thời gian. Đây là số đo điều phối có kiểm soát, **không phải tốc độ NVIDIA thật**.
- Phát lại thật bốn nguồn cố định: baseline 11,707 giây; bản gối đầu đầu tiên 15,460 giây; bản gối đầu có probe chung 18,726 giây; baseline đo lại ngay sau đó 19,432 giây. Tất cả đủ 4/4, đúng thứ tự, pending=0, không báo lỗi; các bản dịch phù hợp với các câu thử. Bản cuối sửa thêm xử lý revision muộn, không thay translator/API so với bản probe chung.
- Các lượt API thật không đồng thời và không đổi key/model đã lưu. Không có xác nhận người dùng đã dừng phiên UI ở toàn bộ thời gian đo; tải server và tải tài khoản có thể ảnh hưởng. Không suy ra phần trăm tăng tốc ngoài thực tế từ các mẫu này, không loại bỏ kết quả chậm khỏi báo cáo. Artifact: `.codex-artifacts/meeting-speed/`.

## Giới hạn và phục hồi

Đã cập nhật bằng `Update-Portable.ps1 -BuildOnly -NoPause`, Release x64. Build `.codex-artifacts/portable-update/20261009-234749-3486ffc9/build`; 40 file runtime khớp build và 41 mục manifest SHA-256 được kiểm độc lập. EXE: `F96AA96A2F3BF4C0E334BEE1FD2E7EE698452CE64B277AFBB4A2E56F314675BF`. Kiểm lại 492 file backup sau cập nhật: không thay đổi. Không tự mở lại TTSK hay thao tác cuộc họp.

Gối đầu giảm thời gian các câu chờ nhau, không làm một request đang chờ máy chủ 20 giây tự hoàn tất trong 2 giây. Với nguồn dồn, throughput có thể tăng; nếu nhà cung cấp nghẽn hoặc giảm tốc khi nhận đồng thời, mức cải thiện có thể thấp hoặc âm. Chưa xác minh cuộc họp dài live sau nâng cấp. Kết quả xong sau vẫn phải chờ câu trước để giữ đúng thứ tự; dữ liệu chưa được lưu bền qua đóng phiên.

Khi cần quay lại baseline, đóng TTSK và khôi phục các file source/portable tương ứng từ backup trên, đối chiếu `SHA256-backup.csv`; không khôi phục toàn bộ repository hay xóa thay đổi ngoài phạm vi dịch họp. Không commit/push/phát hành trong lần nâng cấp này.
