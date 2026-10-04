# Rà soát ảnh hưởng Slot 08 — 2026-10-04

> Đính chính sau đối chiếu 3D: đối tượng Connection trỏ tới trong `[C2-e-34]` là reference màu xám chưa dùng. Phần ghi nhận giữ nó ở lượt rà đầu dưới đây là kết quả lịch sử, không phải kết quả mong muốn cuối. Đã triển khai phân biệt màu gốc + mặt tiếp giáp 3D, mơ hồ giữ/hiện cả nhóm và tiếp tục theo yêu cầu người dùng; xem phần cập nhật cuối tài liệu và `SLOT08_OVERLAPPING_REFERENCE_ANALYSIS_2026-10-04.md`.

## Kết luận có phạm vi

Không thể nói mọi đầu ra giống hoàn toàn bản cũ: hai sửa chủ đích thay đổi visibility của plate/support đủ bằng chứng, và bổ sung số hiện lại/cảnh báo vào thông báo. Với dữ liệu nằm ngoài các trường hợp này, so sánh dưới đây chưa phát hiện thay đổi lõi Resolve, mạng bolt/weld, primary/dummy hoặc logic Check DIM. Không thay thuật toán Fit hay thuật toán DIM khác.

Phát hiện và đã sửa hai điểm ngoài ý định trong phiên bổ sung Connection:

1. Catch của nhánh mới từng thêm `KEEP unresolved reference support` khi đọc lỗi dù chưa có bằng chứng. Test gây lỗi ở đầu đọc đã thất bại trên portable trước lượt rà và đạt sau sửa. Hiện tại chỉ thêm cảnh báo, không đổi Keep/proof.
2. Nhánh mới từng chạy trước RetainLocalDummies, có thể bỏ qua quy tắc cũ hoặc ghi đè lý do giữ đối tượng đã được nhận. Hiện tại thứ tự là Resolve → RetainLocalDummies → RetainConnectionReferenceSupports. Nhánh cuối chỉ xét node chưa có Keep. Gom bằng chứng cục bộ đến hết lần đọc thành công mới công bố; không để lại proof dở dang nếu đọc ném exception.

## Đường gọi và dữ liệu tiêu thụ

| Đường gọi | Đánh giá |
| --- | --- |
| MainForm Slot 08 qua reflection | Cùng entry point/LastRunSucceeded/Apply return count. Có thêm giữ support và hiện part theo phạm vi đã nêu. LastRunMessage/Report có thêm số và lý do. |
| Phím tắt/nút Fit → RunFitView → RunNeighborCleanupAfterFit | Nhận cùng thay đổi visibility khi cleanup được phép chạy. Không đổi active-drawing guard, gating Fit hoặc cách tính restriction box. |
| PHU_DimensionCheck.Analyze → PHU_NeighborCleanup.Analyze | Chỉ dùng Nodes, Edges và các entry Keep có reason đúng `KEEP primary assembly`; không dùng số Restore hoặc cảnh báo mới. Nhánh support không sửa những dữ liệu đó. Dummy vẫn là dummy, không biến support thành chân DIM được phép. |
| PHU_DimensionBatch/Revision dùng kết quả Check DIM | Không thêm điểm gọi hoặc thay source thuật toán; phần quan hệ đầu vào Check DIM được đối chiếu độc lập. |
| Các engine DIM/Section/Arrange khác | Không có caller mới và không sửa source. Lệnh tiếp theo vốn đọc visibility có thể nhận trạng thái đầu vào khác sau cleanup; không đồng nghĩa công thức DIM bị sửa. |

MainForm/Program có thay đổi sẵn từ công việc đa phiên, được giữ nguyên trong lượt này. Diff MainForm không chạm routing Slot 08, Fit hoặc các engine DIM. Source thay đổi để xử lý lỗi trong lượt rà chỉ nằm ở PHU_Slot08_NeighborCleanup.cs.

## Kiểm chứng

- Baseline source: Git `80b831e2ee1f804497069715087bce33ed6c777f`, file Slot 08 trước hai sửa. Tool tạo bản đổi namespace trong cache để gọi cùng dữ liệu; không checkout/reset working tree.
- 2.000 đồ thị sinh với seed cố định: so exact Keep/reason và warnings của Resolve cũ/mới, ComponentTouchesPrimaryConnection, primary membership và Check DIM ResolveConnected sau phần giữ support. Tất cả đạt.
- So nội dung chuỗi macro batch ẩn cũ/mới bằng reflection: bằng nhau. Bộ phân loại reference/dummy vẫn cho kết quả cũ trên ca kiểm.
- Test lỗi đọc bổ sung: giữ nguyên Keep, không có proof hiện lại và có cảnh báo. Test dummy đã Keep: không đọc lại API, không ghi đè reason, không tạo restore proof. Đạt.
- 47 hồi quy plate/Connection trước đó chạy lại và đạt.
- Đọc bản vẽ `[C2-e-34]` bằng hai planner cũ/mới, cùng phiên Tekla: Main/ViewCount, danh sách item/visibility, Nodes/Edges, primary/dummy và tập connected cho Check DIM giống nhau. Mọi Keep/reason cũ còn nguyên. Chỉ dầm `109028105` ở view 1120 và 1223 đổi từ target ẩn sang giữ; không có restore trên trạng thái hiện tại.
- Chạy full Check DIM.Analyze trong hai tiến trình dùng EXE trước thay đổi và EXE mới: báo cáo giống nhau khi loại dòng CapturedUtc, toàn bộ **7.294 feature** giống nhau về owner/kind/điểm A-B tọa độ round-trip/Allowed/Infinite/ConnectedPart/ExternalMemberReference. Bản vẽ hiện tại có **0 chuỗi DIM**: đây là kiểm dữ liệu hình học/quan hệ/cảnh báo, chưa phải thử phân loại chân DIM thật.
- Read-only planner cũ được đọc lại cuối phép so để kiểm bản vẽ, Keep và trạng thái ẩn không thay đổi trong lúc kiểm.
- Build Release x64/portable: 40 file runtime khớp SHA-256; EXE staging và portable khớp `828B211E4CE41C73AC39DC9365746D26FD8E25D43044A3F40532156C0F03F17C` tại lần kiểm này.

## Giới hạn và hành vi cần biết

- Chưa gọi Apply trên bản vẽ thật. Native show/hide, commit và rollback được rà source/build nhưng chưa kiểm thực thi đầy đủ. Không ghi model, thay selection/work plane, đổi/mở bản vẽ khác hoặc chạy DIM.
- ShowInDrawingView là thay đổi có chủ đích. Không lưu nguồn gốc thao tác ẩn, nên part thỏa điều kiện có thể được hiện lại dù trước đó người dùng ẩn thủ công. Không thể cam kết bảo toàn mọi lựa chọn ẩn thủ công và đồng thời tự sửa trạng thái ẩn sai.
- Kết quả bổ sung giữ support lan theo chính ModelIdentifier đó trong các target view (Keep hiện tại dùng chung theo model). Việc hiện lại bị giới hạn từng view bởi bằng chứng primary + plate đang hiện.
- Lỗi đọc nhánh Connection bổ sung chỉ cảnh báo và giữ quyết định cũ; có thể chưa sửa được support trên lần chạy lỗi đó. Không biến lỗi thành bằng chứng giữ/hiện mới.
- Có thêm lệnh đọc component cho dummy chưa được quy tắc cũ giữ. Một mẫu timing tuần tự không phải benchmark; chưa kiểm tải lớn/nhiều model.
- Không khẳng định bao phủ mọi kiểu Connection hoặc mọi bản vẽ. Đặc biệt hướng input đảo, component tùy biến thay vì native Connection và cả plate/support đang ẩn đều không được mở rộng suy đoán trong sửa này.

## Chạy lại

```powershell
pwsh -NoProfile -File tests/NeighborCleanup/Run-ImpactReview.ps1
pwsh -NoProfile -File tests/NeighborCleanup/Run-ImpactReview.ps1 -Live -TeklaPid <PID>
pwsh -NoProfile -File tests/NeighborCleanup/Run-ReadOnly.ps1 -Offline -TeklaBinPath <Tekla bin>
```

Cache `.codex-artifacts/neighbor-impact` tự sinh lại. Chế độ full Check DIM của ImpactReview.exe nhận `<product.exe> <Tekla bin> <PID> <start ticks> dimcheck`; muốn so EXE khác phải cung cấp chính EXE đó. Các log/backup không phải phụ thuộc sản phẩm.

## Cập nhật cuối: reference chồng nhau và live-test được cho phép

Source sản phẩm vẫn chỉ sửa Slot 08. Dữ liệu hình học/quan hệ Check DIM, macro batch và thuật toán Fit nền giữ nguyên. Phần mới đọc thêm permanent color và solid trong cache từng Analyze, chỉ cho nhánh Connection supplement. Không dùng Class cố định hoặc ID mẫu trong sản phẩm. Mơ hồ giữ/hiện nhóm và cảnh báo; không dừng batch vì mơ hồ.

Rà sâu nhánh lỗi đã loại bỏ giữ toàn bộ reference cùng profile/material khi một đối tượng đọc lỗi. Ứng viên được giới hạn cùng view có nguồn + plate, trục song song và vùng interface. Không dùng kết quả hình học chưa đủ để chọn một ứng viên. Thành viên đọc lỗi được xử lý riêng. Hai reference cùng màu rõ cũng là mơ hồ, không ưu tiên Connection source một cách tuyệt đối.

Trước live-test: 82 hồi quy và 2.000 đồ thị đối chiếu đều đạt. Check DIM report/features giữa EXE baseline và mới giống nhau (trừ CapturedUtc), 7.294 feature. Mô phỏng màu mơ hồ trên hình học thật giữ/hiện cả hai support ở Top/Front và giữ nguyên các target ngoài nhóm.

Người dùng sau đó cho phép ghi bản hiện tại và bản trước. Snapshot lưu ngoài model ở `.codex-artifacts/neighbor-live/20261004-171910`:

- `[C2-e-34]`: Apply thật ẩn 6 part, hiện 1 support. `130973315` màu đỏ hiện cả hai view, `109028105` màu xám bị ẩn. So toàn bộ item với kế hoạch, danh sách object ID/type, vị trí/scale/restriction box view, DIM signature và Check DIM report: không đổi ngoài visibility dự kiến. Apply lần hai trả 0, lưu thành công.
- `[C2-e-73]`: bản mở lại có 3 target view; Apply ẩn 17 part, hiện 2 plate. Công cụ đầu bị gián đoạn sau khi Apply xong; không suy kết quả từ exit code. Đã dùng snapshot trước đó để tiếp tục đối chiếu toàn bộ visibility, object/layout/DIM và Check DIM: đạt. Apply lần hai trả 0 và lưu thành công.

Live-test này kiểm ảnh hưởng thực tế trên hai mẫu đã cho phép; không chứng minh mọi representation/model/drawing khác. Cảnh báo về tự hiện part từng bị ẩn thủ công vẫn áp dụng theo yêu cầu giữ/hiện nhóm mơ hồ. Không đổi representation/màu/geometry 3D hoặc work plane; công cụ chỉ ghi visibility bản vẽ và lưu/mở đúng hai bản để kiểm.

### Xác minh hoàn tất sau live-test

- Đã lưu `[C2-e-73]` và mở lại `[C2-e-34]`, xác minh trạng thái sau reopen: thanh đỏ hiện cả hai view, thanh xám ẩn cả hai view. Kết quả lưu bền vững, không chỉ là trạng thái trong tiến trình probe.
- Live-test riêng nhánh mơ hồ (`.codex-artifacts/neighbor-live/20261004-172426`): chỉ thay color cache trong RAM của kế hoạch, không đổi màu model. Apply hiện cả hai support trong cả hai view, không ẩn part nào; đối chiếu mọi part ngoài cặp, object/layout/DIM signature và Check DIM report: giữ nguyên. Sau đó Analyze với màu thật + Apply ẩn lại đúng thanh xám trong hai view, khôi phục toàn bộ visibility về baseline đã kiểm, chạy lần hai trả 0 và lưu. Không có force-stop vì mơ hồ.
- Đã kiểm thêm trên snapshot hình học thật: khi cả hai reference đều có màu rõ, giữ cả hai thay vì ưu tiên Connection. Thêm ứng viên cùng profile/material và cùng view nhưng tịnh tiến ra xa: không bị giữ/hiện theo nhóm mơ hồ. ID nhân tạo chỉ nằm trong test, không insert model/drawing.
- Portable cuối từ Release x64: 40 file runtime được xác minh; EXE SHA-256 `319750765B8211E695940DE62A678755AC0174F1C8CB3B9B1EA0A48E84654D13`. Source map phải chạy Generate rồi Check sau cập nhật tài liệu.

Kết luận giới hạn: trên hai bản đã chạy và các ca kiểm, không phát hiện thay đổi liên kết/visibility ngoài kế hoạch; graph nền, Check DIM và bố trí view bảo toàn. Có thay đổi chủ đích ở visibility và thông báo Slot 08; vẫn không tuyên bố tất cả cấu tạo/model khác đều đã được kiểm.
