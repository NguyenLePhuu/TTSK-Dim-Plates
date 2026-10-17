# Dịch tuần tự nghiêm ngặt — 09/10/2026

## Hợp đồng xử lý

Theo yêu cầu mới, hoàn thành câu hiện tại rồi mới gửi câu tiếp theo. Nguồn Teams được giữ nguyên; không bỏ câu để giảm số đo độ trễ. Phạm vi hoàn thiện NVIDIA trước, không sửa translator/model/key Google.

- Một worker duy nhất. Retry giữ quyền xử lý câu đầu trong cùng vòng lặp; không chuyển câu lỗi xuống danh sách để gửi câu sau.
- Cổng WaitForFirstCaptionAsync giữ cả thứ tự trước lúc enqueue: câu sau có dấu chấm có thể ổn định sau 400 ms, sớm hơn câu trước còn cụt cần 800 ms. Câu sau phải đợi câu trước được xác nhận.
- Teams sửa câu: chỉ công bố revision hiện tại. Revision cũ không vào lịch sử/context; câu đầy đủ mới vẫn đứng trước lượt tiếp theo.
- Lỗi mạng/tạm thời tự thử lại câu hiện tại, tôn trọng Retry-After. Lỗi cấu hình/quyền giữ nguồn và đợi người dùng sửa rồi Thử lại API; không liên tục gửi lại key sai.
- NVIDIA giữ đường Riva ja-en → en-vi và ngân sách/circuit đã có. Nếu cần NVIDIA dịch trực tiếp, prompt cho phép dịch gần nghĩa câu cụt/lẫn ngôn ngữ. Output hoàn chỉnh nhưng chưa đạt guard được nhận với nhãn Gần nghĩa. HTTP lỗi, output rỗng hoặc bị cắt không được giả thành bản dịch thành công.
- Preview CÂU ĐANG DỊCH bám CaptionProcessing, không hiển thị câu mới nhất đang xếp hàng. Không tạo thẻ Chờ dịch lại. Nút Thử lại API có thể đánh thức retry của chính câu đang xử lý.
- Dừng/Tiếp tục giữ phiên và nguồn trong RAM; đóng cửa sổ hủy tác vụ. Chưa có lưu bền hàng chờ qua lần đóng ứng dụng.

## Kiểm tra

Build Release x64 và cập nhật 40 file runtime bằng scripts/Update-Portable.ps1 -BuildOnly -NoPause. Build: .codex-artifacts/portable-update/20261009-142511-508b480a/build.

54 kiểm tra đạt:
- StrictTests: 26. Chuỗi 20 câu, lỗi đầu chuỗi, 401 + retry thủ công, revision, không vượt câu chưa ổn định, pause/resume, hủy khi đóng, bản gần nghĩa.
- VerbatimTests: 18. Toàn bộ ký tự nguồn, câu lặp khác lượt, revision và JSON NVIDIA không bị cắt.
- PauseUiTests: 10. Kiểm WinForms desktop riêng, dark/light, 880×620 và 1040×780; không thao tác cửa sổ người dùng.

API NVIDIA thật với 3 câu từ ảnh: `ね。`, `そういう you`, `という you're 泊まりますか`. Kết quả lần lượt sau 7.246 s, 9.676 s, 10.471 s kể từ tiếp nhận; cả chuỗi 10.504 s. Đủ thứ tự 1→2→3, pending=0, failures=0. Câu thứ ba được đánh dấu gần nghĩa. Câu thứ hai còn giữ từ English `you`; không tuyên bố chất lượng ngữ nghĩa tuyệt đối. Đây là phát lại nguồn cố định qua API thật, không phải kiểm cả cuộc họp Teams trực tiếp dài.

Logs/harness: .codex-artifacts/meeting-strict. Source backup: backup/meeting-strict-20261009-142004.

## Giới hạn thực tế

Tuần tự và không bỏ câu đồng nghĩa câu sau phải chờ khi dịch vụ lỗi/chậm. Nếu tốc độ nhận kéo dài lớn hơn tốc độ API, backlog vẫn tăng. Không thể cam kết mọi câu dưới 10 giây khi API unavailable/rate limited, và không dùng nội dung tự đoán để che lỗi kết nối. Không thay đổi thuật toán DIM/Tekla hoặc phần nhập key.