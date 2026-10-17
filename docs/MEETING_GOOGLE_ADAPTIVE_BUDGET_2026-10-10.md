# Google: nhịp gửi linh hoạt và ngân sách cuộc họp dài

## Thay đổi hiện hành

Chỉ sửa MeetingTranslationService.cs. Giữ nguyên prompt dịch, reader/tracker Teams, kiểm revision, FIFO, memory 64 câu/120 giây, bộ kiểm JSON/Unicode, cơ chế thu nhỏ lô lỗi và nhánh NVIDIA.

### Gửi sớm khi có ngân sách

GoogleModelRequestPacer không còn bắt mọi cặp request cách nhau 4,1 giây:

- Tối đa **2 lượt dự phòng**, bù lại **1 lượt/4,1 giây**. Sau khoảng im lặng, câu tiếp theo có thể gửi sớm. Không tích lũy vô hạn để bắn hàng chục request một lúc.
- Tối thiểu **1,1 giây** giữa hai lần cấp lượt. Sổ thời điểm **60,1 giây** đồng thời chặn cứng **15 lượt**; nhờ vậy nhịp dự phòng không vượt 15 trong mọi cửa sổ 60 giây.
- Tất cả request của đúng model gemini-3.5-flash-lite trong TTSK đi qua cổng này, gồm retry và kiểm tra key/model. Cache hit không giữ lượt. Revision cũ/pause khi chờ không giữ lượt trống.
- Timestamp plain `.txt` cũ vẫn được ghi để portable cũ đang chạy cùng Windows user/session có thể tôn trọng nhịp. Ledger `.budget.json` mới lưu thời điểm gần đây, số lượt dự phòng, cooldown và ngân sách ngày. Nếu bản cũ ghi timestamp mới, bản mới dựng lịch sử 15 lượt bảo thủ để tránh nới RPM khi hai phiên bản cùng chạy.
- Named mutex và thay file nguyên tử dùng như trước; không lưu key hoặc phụ đề, không ghi vào model Tekla. Clock lùi không cấp lại lượt đã dùng. 429/Retry-After vẫn giữ cooldown.

### Gom nhiều hơn khi đã có hàng chờ

Trần gom tăng **8 → 16 caption**, vẫn tối đa **4.000 ký tự nguồn** và không cắt một câu để vừa lô. Không chờ đủ 16. Snapshot sau khi có quyền gửi chỉ lấy các câu đã ổn định, theo thứ tự, dừng trước câu chưa sẵn sàng hoặc đã buffered.

Lô gửi chỉ chứa nguồn mới duy nhất, không gửi lại câu đã có trong memory. Mỗi nguồn có id riêng; ánh xạ ngược đủ mọi lượt nói, kể cả câu lặp. Chỉ công bố sau STOP và kiểm đủ id/Unicode. Nếu lô lỗi, giữ toàn bộ nguồn, sửa head rồi giảm trần lô như trước. Không trình bày chữ nháp chưa kiểm như bản dịch hoàn chỉnh.

### Theo dõi ngân sách ngày

- Theo mức trong ảnh người dùng, ledger chặn ở **500 lần cấp lượt/ngày**. Reset theo **nửa đêm Pacific**, có xử lý DST của Windows; không reset theo múi giờ Nhật hay lúc mở lại ứng dụng.
- Báo trạng thái tại 400, 450 và 480 lượt. Khi đủ 500, giữ hàng đợi và đợi ngày quota tiếp theo; người dùng vẫn có thể pause/đóng. Không gọi tiếp rồi retry liên tục vào giới hạn ngày.
- Đây là bộ đếm **các lần cấp lượt mà bản mới đã ghi nhận**, không phải truy vấn Usage của Google. Có thể bao gồm một số lượt đã giữ nhưng HTTP chưa gửi do hủy/sửa câu ngay sau cấp lượt. Không biết lượng đã dùng trước khi cài bản này, lượng của ứng dụng/máy khác, hoặc toàn bộ lượt portable cũ gửi khi bản mới không chạy. Google vẫn có thể báo quota sớm hơn.
- Bộ đếm dùng chung model cho các key trong cùng Windows user/session, bảo thủ nếu người dùng dùng key từ nhiều project. Ledger hỏng tự dựng lại nhưng khóa ngân sách đến lần reset ngày tiếp theo; không dùng lỗi cache để mở lại 500 lượt mới. Xóa mọi ledger thủ công làm mất lịch sử cục bộ, không làm tăng quota Google.

## Giới hạn không thể xóa bằng điều phối

15 RPM là trần, không phải số request cần dùng hết. 500 RPD vẫn hữu hạn. Nhịp dự phòng giảm chờ ở đoạn nói ngắt quãng; khi nói liên tục, ngân sách phải được bù lại nên vẫn phải gom/chờ. Tăng trần lô giúp xử lý backlog bằng ít HTTP hơn, nhưng không bảo đảm mọi câu mới tức thì trong nhiều giờ. Độ trễ còn có settling Teams, mạng, model và retry. Không tự tăng tier, mua quota, đổi key/project/provider hoặc chuyển sang dịch vụ khác.

## Kiểm chứng trên bản bàn giao

- Release x64/C# 7.3/.NET Framework 4.8; `Update-Portable.ps1 -BuildOnly -NoPause`; build `.codex-artifacts/portable-update/20261010-190909-8b362d4c/`.
- **GoogleBatchTests 121 kiểm tra offline đạt**, gồm 1.000 reservation qua nhiều instance, rolling 15/60 giây, trần 500/ngày, 5 tiến trình, khởi động lại/clock lùi, ledger hỏng, tương thích timestamp cũ, reset Pacific/DST, notice 400/500, cache, revision, retry, pause/close, JSON/Unicode và lô 16.
- Kịch bản 10 câu ngắt quãng tại giây 0,2,12,14,24,26,36,38,48,50: tổng chờ điều phối cũ 10,5 giây; mới 0 giây. Đây là mô phỏng lịch gửi, chưa gồm latency API/settling.
- HTTP handler giả và đồng hồ thật: 3 lần gửi ở **6 / 1126 / 4131 ms**. Lượt thứ hai dùng dự phòng; lượt thứ ba phải đợi bù ngân sách. Không phải 3 request gọi Google thật.
- 41 caption ngắn đã sẵn sàng: **3 HTTP thay vì 6**, trả đủ 41 dòng theo Sequence, không nhầm nguồn hoặc mất pending. Nguồn dài vẫn bị giới hạn 4.000 ký tự/lô, không cắt source.
- Google thật: **8 kiểm tra đạt**, lô 16 caption (15 nguồn duy nhất) trong **2455 ms, 1 HTTP**; dùng lại cùng nguồn **0 HTTP**, dưới 1 ms theo đồng hồ test. Coordinator thật với caption mẫu giả lập hoàn tất 16 dòng trong **2608 ms**; key/model trước/sau giữ nguyên. Đã đọc bản dịch của mẫu M20/12mm/3500mm/24 bulông/150mm/22mm, các câu phủ định và lịch giao hàng. Không cam kết mọi bản dịch đều đúng về nghĩa từ một tập mẫu.
- **OrderedPipeline 44 kiểm tra đạt khi chạy riêng**; một lần chạy song song với các bộ test khác thất bại tại kiểm tra thời điểm NVIDIA 429, lần chạy riêng sau đó đạt trên cùng binary. Không sửa code NVIDIA hoặc nới assertion để làm test qua. Ghi nhận đây là kiểm tra nhạy với lịch chạy, chưa xác định gốc của lần thất bại đầu.
- **Bottleneck 20 kiểm tra đạt** trên cùng binary. Tổng các bộ đạt cuối: **185 kiểm tra offline**, cộng 8 live checks. Không coi lần thất bại đầu là đã được sửa chỉ vì chạy lại đạt.
- Artifacts: `.codex-artifacts/google-batch/tests/offline.log`, `live-adaptive.log`, các log ordered/bottleneck; hash build/portable lưu riêng trong `.codex-artifacts/google-batch/verified-runtime.csv`.

Không thử cuộc họp Teams thật trong lần sửa này; không gọi DIM hoặc ghi model Tekla; không commit/push/phát hành. BuildOnly không sinh lại metadata đóng gói phát hành cũ.

## Tài liệu nguồn

[Google rate limits](https://ai.google.dev/gemini-api/docs/rate-limits): RPM/TPM/RPD được kiểm riêng, áp dụng theo project; RPD reset nửa đêm Pacific; quota/capacity thực tế cần xem AI Studio. Các mô tả nhịp cứng 4,1 giây và trần 8 caption trong tài liệu trước là lịch sử, được thay bằng cơ chế này.
