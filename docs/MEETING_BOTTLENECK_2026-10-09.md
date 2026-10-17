# Nghẽn câu dài trong luồng dịch họp — 09/10/2026

## Nguyên nhân có bằng chứng

- Cửa sổ TTSK hiện lịch sử tăng từ khoảng 40–50 giây lên 239–323 giây sau hai câu dài. Chỉ đọc cửa sổ qua Computer Use; không điều khiển Teams/thu âm hoặc ghi folder model Tekla.
- Phát lại nguyên văn hai câu Nhật (83 và 100 ký tự) cùng một câu ngắn qua coordinator production với key NVIDIA đã lưu: bản cũ chưa hoàn thành câu nào sau 75 giây. Riva bị hủy sau 5 giây; Nemotron bị hủy nhiều lần đúng 12 giây. Log chỉ ghi model, độ dài, thời gian, trạng thái HTTP, bản dịch; không ghi credential.
- Phép đo streaming riêng nhận HTTP 500 sau 19,833 giây. Phép đo tiếp theo với một mệnh đề: chữ đầu ở 26,772 giây, hoàn tất ở 27,074 giây. Đây là bằng chứng thời gian chờ máy chủ lớn, không chứng minh mọi câu dài đều cần chừng ấy thời gian.
- Trace còn ghi ja-en thành công trong 3,081 giây nhưng en-vi bị hủy sau 1,828 giây: cả hai pivot dùng chung ngân sách 5 giây. Bản Riva không đạt guard còn gọi editor ngay trong ngân sách này, rồi catch gọi direct thêm lần nữa. Những lần gọi thừa và hủy trước khi có kết quả kéo dài thời gian chờ FIFO.
- Giới hạn 700 token cố định là một rủi ro độc lập đối với nguồn dài. Chưa thấy `finish_reason=length` trên hai câu đang khảo sát; không quy kết đây là nguyên nhân thực tế của chúng.

## Sửa trong MeetingTranslationService.cs

- Riva có hạn 5 giây **cho từng HTTP pivot**; bản đạt guard trả ngay. Khi bản nháp không đạt, ra khỏi nhánh Riva và chỉ gọi một lần cứu trực tiếp trong lượt đó.
- NVIDIA direct dùng SSE (`stream=true`, `ResponseHeadersRead`): chờ phản hồi/chữ đầu tối đa 60 giây, sau khi có chữ thì giới hạn im lặng 10 giây, trần tuyệt đối 90 giây/request. Đây là deadline, không phải thời gian ngủ. Heartbeat hoặc reasoning không gia hạn tiến độ dịch.
- Theo dõi thời gian chờ máy chủ mỗi giây, số ký tự nhận và hàng đợi. Chỉ công bố bản dịch sau `finish_reason=stop` và quality guard; không đưa bản còn dở vào lịch sử/context.
- Token theo chiều dài nguồn, tối đa 4096. Direct bị `length` tăng giới hạn ở lượt retry, không lặp mãi cùng giới hạn. Giới hạn phản hồi 1 MiB. Đóng phiên hủy cả đọc body đang chờ.
- Lỗi đầu ra cố định ba lượt chuyển sang trạng thái rõ ràng yêu cầu **Thử lại API**, giữ nguồn và thứ tự. Lỗi mạng/timeout/HTTP tạm thời tiếp tục phục hồi theo backoff; 429/Retry-After và lỗi quyền vẫn được tôn trọng.
- Giữ một worker, source nguyên văn, revision và thứ tự. Không cắt/ghép nguồn, bỏ câu, thêm worker, đổi model/key hoặc sửa bộ đọc Teams. Không chỉnh Google translator hay thuật toán Tekla.

## Kiểm tra

- Build Release x64, C# 7.3/.NET Framework 4.8.
- `tests/MeetingTranslation/Run-Bottleneck.ps1 -Runtime <thư mục build>`: 20 kiểm tra, gồm hai pivot 3 giây không bị hủy, chữ đầu sau 12 giây, sinh chữ kéo dài, hủy body, heartbeat không che treo, EOF/DONE thiếu stop, output bị cắt tăng token, chỉ một rescue, giữ câu khi quality lỗi ba lần.
- Hồi quy sẵn có chạy lại trên build-v2: StrictTests 26, VerbatimTests 18, FinalReview 18 (bao gồm 200 câu FIFO và sửa revision trong lúc HTTP chạy). Tổng 82 kiểm tra offline.
- API thật trên bản trung gian streaming: cả ba câu hoàn thành đúng thứ tự trong 82,356 giây, pending=0; bản này chưa tách 5 giây mỗi pivot. So với bản cũ không hoàn thành câu đầu trong 75 giây, vòng hủy/gửi lại đã được tháo; chưa đủ để khẳng định tốc độ đáp ứng họp trực tiếp.
- Bản cuối `build-v2`: câu đầu Riva ja-en 3,614 giây + en-vi 3,308 giây; total từ lúc nhận 7,726 giây. Câu hai vẫn bị NVIDIA chậm: thời gian xử lý 83,150 giây, có timeout và retry. Câu ba xử lý 4,774 giây. Cả ba hoàn tất đúng thứ tự sau 95,652 giây, pending=0. Không lấy riêng câu nhanh để kết luận dịch vụ đã ổn định.
- Kiểm riêng hai pivot Riva của câu thứ hai với ngân sách quan sát 35 giây vẫn chưa hoàn tất (`riva-long.log`). Vì vậy chưa có bằng chứng rằng chỉ tăng hạn Riva lên vài giây nữa sẽ giải quyết được câu này.
- Sau khi người dùng đóng TTSK, Update-Portable -BuildOnly -NoPause đã cập nhật và xác minh 40 file runtime. Build bàn giao: `.codex-artifacts/portable-update/20261009-232002-cb7291fd/build`. Manifest SHA-256 được đối chiếu độc lập; EXE portable khớp chính xác bản build-v2 đã kiểm thử: `D484C6277DBAA26C87DA82F097E15557C266611C37C620FDF92808184CF19244`. Không commit/push/phát hành.
- Artifact/log tại `.codex-artifacts/meeting-bottleneck/`; kiểm API thật dùng nguồn phát lại, không phải toàn bộ cuộc họp live. Tình trạng NVIDIA thay đổi giữa các lần đo, không dùng các mẫu này như benchmark kiểm soát.

## Giới hạn

Tuần tự nghiêm ngặt và giữ mọi câu đồng nghĩa câu sau vẫn phải chờ nếu máy chủ mất hàng chục giây hoặc unavailable. Code không loại bỏ được hàng chờ/lỗi HTTP phía nhà cung cấp. Bản sửa ngăn tự hủy quá sớm, gọi trùng và thử lại chất lượng vô hạn; không cam kết mọi câu dưới vài giây. Backlog chưa được lưu bền qua đóng ứng dụng.

NVIDIA mô tả SSE và delta/finish_reason tại [NIM Quickstart](https://docs.nvidia.com/nim/large-language-models/latest/get-started/quickstart.html); model Riva tại [model card chính thức](https://build.nvidia.com/nvidia/riva-translate-4b-instruct-v2/modelcard).
