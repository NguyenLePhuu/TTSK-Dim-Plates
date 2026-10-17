# Cải thiện realtime có kiểm chứng — 2026-10-10

## Hai luồng dùng chung những gì?

| Thành phần | NVIDIA / Riva | Google |
| --- | --- | --- |
| Đọc Teams, nhận người nói, tracker và settling | Dùng chung | Dùng chung |
| Sequence/revision, hàng đợi, pause/resume, giữ nguồn khi lỗi | Coordinator chung | Coordinator chung |
| Công bố từng dòng, xử lý sửa câu, lịch sử/subtitle | Dùng chung | Dùng chung |
| Dịch ngôn ngữ | Riva Nhật → Anh → Việt; quality guard, Nemotron cứu/hiệu đính khi cần; 2 worker | Gemini Nhật → Việt trực tiếp; 1 worker; Flash Lite 3.5 gom lô, pacing 4,1 giây |
| Giảm chờ máy chủ | NVIDIA trực tiếp đã nhận SSE, theo dõi tiến độ; chỉ công bố output hoàn tất | Gom câu trong lúc chờ RPM; tái dùng bản dịch nguyên văn trong phiên từ lần nâng cấp này |

Hai nhánh không dùng chung engine/prompt và không tự đổi nhà cung cấp giữa phiên. Việc Google tốt hơn ở một cuộc họp không chứng minh Google luôn hơn ở mọi loại câu; có khác biệt do model và đường dịch qua/không qua tiếng Anh. Giữ lựa chọn người dùng, không sửa prompt, model hoặc credential trong lần nâng cấp này.

## Hai vấn đề đã xử lý

### 1. Tránh mất lượt gửi cho câu đã cũ

Trước đây TranslateQueuedAsync chờ cổng RPM cấp lượt rồi mới đọc lại snapshot. Nếu Teams sửa câu đầu trong lúc chờ, request không được gửi nhưng lượt 4,1 giây đã bị giữ; câu mới lại phải chờ chu kỳ kế tiếp.

GoogleModelRequestPacer.WaitAsync nay kiểm stillNeeded trong khi chờ. Câu đổi hoặc pause làm waiter cũ kết thúc trước khi giữ lượt; FIFO lấy revision mới và tiếp tục chờ đúng thời điểm đã có. Callback không hủy HTTP đã gửi. Sau khi có lượt vẫn chốt lại snapshot để gom các câu mới đã ổn định. Nhịp 4,1 giây và giới hạn rolling 15 RPM giữ nguyên.

### 2. Trả ngay câu lặp nguyên văn, giảm nguồn gửi Google

GoogleSessionTranslationMemory chỉ nằm trong RAM của một translator (riêng session/model/key):

- Tối đa **64 nguồn**, hết hạn **120 giây từ lúc nhận bản dịch** bằng Stopwatch; dùng lại không gia hạn.
- Khớp StringComparer.Ordinal từng ký tự. Không trim/chỉnh nguồn, không so gần nghĩa. M20 khác M22; khác khoảng trắng hoặc phủ định là nguồn khác.
- Chỉ lưu khi response hoàn chỉnh, STOP, JSON/id/Unicode hợp lệ. Lô bị lỗi không ghi cả phần hợp lệ vào bộ nhớ. Đóng phiên/Dispose xóa bộ nhớ; không ghi phụ đề ra đĩa, không chia sẻ bản dịch qua key/provider/session khác.
- Nếu câu đầu có trong bộ nhớ, trả ngay qua FIFO cũ, không chờ RPM và không gọi HTTP. Các lượt nói lặp vẫn có Sequence/thẻ riêng.
- Trong lô mới, chỉ gửi nguồn chưa có bản dịch. Nguồn trùng chính xác trong cùng lô chỉ gửi một lần, sau đó ánh xạ đủ mọi vị trí ban đầu. Ví dụ A/B/A trả đủ ba dòng A/B/A; không xóa lượt thứ ba.
- Giữ giới hạn lô 8 caption/4.000 ký tự, dù một phần đã có cache. Lô còn một nguồn mới dùng payload đơn cũ; nhiều nguồn mới dùng JSON đã kiểm chứng. Khi sửa câu/mất kết nối, pending và buffered vẫn đi qua xử lý cũ.
- Tooltip timing ghi đường dịch/tái dùng và số HTTP thực tế, tránh hiểu nhầm bản từ bộ nhớ là một request mới.

Không thay prompt hoặc cố sửa nghĩa/chính tả từ model. Bộ nhớ tái dùng một bản dịch đã qua kiểm giao thức, không phải sự bảo đảm ngữ nghĩa hoàn hảo. Những nguồn gần giống, bản chưa hoàn chỉnh hoặc hết TTL vẫn phải dịch lại.

## Kiểm chứng

- Source sản phẩm chỉ sửa `MeetingTranslation/MeetingTranslationService.cs`; không sửa tracker, UIA, UI history/subtitle, prompt NVIDIA/Google hoặc đường DIM/Tekla.
- Release x64/C# 7.3/.NET Framework 4.8: `.codex-artifacts/portable-update/20261010-155019-4f651f8b/`; cập nhật portable bằng BuildOnly.
- GoogleBatchTests **77 kiểm tra offline đạt**: thêm TTL/capacity/eviction/Dispose, phân biệt số/khoảng trắng, không cache lô lỗi, phiên độc lập, lô trộn nguồn mới/cũ/lặp, pause/revision khi chờ quota, HTTP và telemetry. Toàn bộ bộ kiểm batch/RPM trước đó chạy lại.
- Mô phỏng 4 lượt lặp sau lô đầu: đủ 4 dòng mới sau **693 ms**, gồm thời gian settling/polling Teams giả lập; không có HTTP mới. Đây không phải số đo cuộc họp thực.
- Sửa câu trong lúc chờ: câu mới được gửi/hoàn tất khoảng **4142 ms** sau lượt trước; không mất thêm 4,1 giây cho câu cũ.
- Pacing thời gian thật qua HTTP handler giả: các dispatch **3 / 4116 / 8229 ms**. Kiểm 1.000 reservation ở mọi cửa sổ 60 giây ≤15; 5 process dùng cùng ledger vẫn chỉ một process lấy lượt.
- OrderedPipeline **44 kiểm tra đạt**, gồm NVIDIA, revision sớm/muộn, buffer, pause, 429 và burst 200 câu. Bottleneck **20 kiểm tra đạt** trên cùng build. Tổng **141 kiểm tra offline**, kết quả lưu trong các thư mục test tương ứng.
- Google thật: **8 kiểm tra đạt**; 8 câu nguồn (7 nguồn duy nhất) qua một HTTP khoảng **2219 ms**, gọi lại cùng 8 nguồn trong cùng translator **0 HTTP, dưới 1 ms theo đồng hồ test**. Coordinator thật + caption giả lập trả đủ 8 nguồn đúng thứ tự trong **3427 ms** gồm quota/settling. Bản dịch có M20, 12mm và phủ định; key/model trước/sau không đổi.
- Log API thật: `.codex-artifacts/google-batch/tests/live-realtime.log`; offline: `offline.log`. Không ghi key vào log. Câu dùng trong test là mẫu kỹ thuật, không phải nội dung cuộc họp thật.

Số đo là mẫu trong điều kiện kiểm thử, không cam kết độ trễ cho mọi câu/model/mạng. Câu mới vẫn chịu thời gian settling, giới hạn 15 RPM và độ trễ API; không thể bảo đảm tất cả câu mới dịch tức thì. Hạn mức ngày 500 trong ảnh trước vẫn áp dụng. Chưa live-test cuộc họp Teams trong lần sửa này. Không ghi folder model Tekla, không commit/push/phát hành.

## Các hướng chưa bật vì cần đánh giá riêng

- Phát chữ Google khi JSON còn đang stream có thể giảm cảm giác chờ nhưng chưa qua STOP/đủ id và có thể phải thu hồi bản dịch; hiện giữ output hoàn chỉnh để tránh dòng sai nhấp nháy.
- Thay prompt/ngữ cảnh hoặc từ điển chuyên ngành cần corpus Nhật–Việt và so sánh chất lượng; không gộp vào thay đổi điều phối khi người dùng đang hài lòng với bản dịch.
- Không tăng worker Google hoặc gọi hai nhà cung cấp cùng lúc: chưa có bằng chứng cải thiện trong nhịp 15 RPM, nhưng tăng nguy cơ dùng quota thừa và sửa lại câu đang đọc.
