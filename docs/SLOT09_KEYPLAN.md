# Slot 09 — Keyplan

## Cách dùng

**Tự áp dụng** là công tắc trượt có ghi nhớ. Trạng thái bật/tắt được ghi vào `%LOCALAPPDATA%\TTSK\Keyplan\keyplan.cfg`, được đọc lại mỗi lần mở Slot 09, kể cả sau khi đóng/mở chương trình. Nếu chưa có cấu hình mới, đọc cấu hình cũ cạnh chương trình và chuyển lựa chọn sang LocalAppData; không ghi ngược vào vị trí cũ. Chưa có cấu hình thì mặc định tắt. Khi đã bật, chỉ cần mở Slot 09 để tự kiểm tra rồi cập nhật **Coordinate system = Local, Around X/Y/Z**. Kiểm tra lại hoặc đổi phương án/hướng/lưới cũng tính và áp dụng lại nếu cần. Chỉ thao tác bật/tắt của người dùng mới đổi cấu hình; đổi bản vẽ hoặc gặp lỗi không làm mất lựa chọn này. Không chạy nền hay tự lặp lại sau lỗi. Các nút và việc đóng form tạm khóa trong khi thao tác đang chạy.

## Tổ chức mã và file đầu ra

Thuật toán được gộp vào **PHU_Keyplan.cs**, gồm sáu vùng Geometry, Shape, Reader, Properties, Apply và Preferences. **PHU_KeyplanForm.cs** giữ giao diện; **MainForm.Keyplan.cs** giữ entry point/dispatcher. Sáu file thuật toán cũ đã được gộp và loại khỏi project, không còn bản sao song song.

Luồng production không ghi macro `.cs`, preset, log, dữ liệu đo hay file tạm vào folder model. Model API chỉ đọc. Tự áp dụng vẫn cập nhật thuộc tính bản vẽ qua Tekla theo yêu cầu; các thay đổi bản vẽ được Tekla quản lý, không phải file phụ do Slot 09 xuất ra. Cấu hình duy nhất do ứng dụng tự lưu nằm ở LocalAppData. Các bộ đo phát triển trong `tests/Keyplan` không được compile hay phân phối vào portable.

Mỗi lượt chỉ mở **một** Assembly drawing properties: đọc thuộc tính gốc → tính góc → điền đủ Local/X/Y/Z → **Modify → Apply → OK**. Sau Apply, công cụ nạp lại dữ liệu gốc của bản vẽ vào chính hộp thoại đang mở và đối chiếu trước khi OK; không mở/đóng cửa sổ riêng cho từng trường. Khi góc đã đúng thì không Modify thêm. Thông báo **Đã áp dụng Local · X / Y / Z** khác với **Các view đúng keyplan**. Thiếu view hoặc view chưa khớp vẫn được cảnh báo. Không lưu preset hay gọi Insert/Delete view trực tiếp.

Tekla chỉ nhận lựa chọn combobox sau `CBN_SELENDOK`; thay số hiển thị bằng `CB_SETCURSEL`/`CBN_SELCHANGE` chưa đủ. Luồng mới gửi sự kiện xác nhận này cho Local/X/Y và đọc lại giá trị đã lưu thực tế. Theo yêu cầu người dùng, khi Tự áp dụng đang bật, công cụ tự xác nhận Yes cho việc tái tạo bản vẽ; không hiện thêm hộp hỏi của Keyplan. Hộp xác nhận tương ứng của Tekla được xử lý tự động trong khi chờ Modify hoàn tất. Hết thời gian chờ hoặc gặp hộp thoại lạ thì giữ cửa sổ để kiểm tra, không gửi lặp Modify. Đây là thay đổi bản vẽ, không có phép ghi model 3D.

**Góc gắn với đúng view thực tế.** Với cột có hai view tiêu chuẩn, dùng đúng loại Front/Top/Back/Bottom và thứ tự trái/phải trên giấy để dò góc. Không bỏ Bottom rồi thay bằng Top giả định. Với 0/1 view, vẫn có thể gợi ý nhưng ghi rõ view còn thiếu; khi đã có nhiều view mà vai trò không xác định thì chỉ đối chiếu từng view, không xuất một bộ góc chung giả định. Không đóng form khi thiếu view.

Loại view do Tekla tự sinh có thể thay đổi. Sau Modify, công cụ đọc lại toàn bộ view mới và kiểm tra theo ánh xạ đã dùng khi tính góc: loại view, chiều grid, chiều lên, hệ Local và vị trí trái/phải. Loại view đổi hoặc hướng không khớp sẽ báo chưa đạt, không tự đổi định nghĩa mục tiêu để nhận kết quả mới là đúng. Nếu chỉ vị trí đổi, báo cần sắp lại view. Hai thẻ tham chiếu hiển thị cả tên view (ví dụ FRONT/BOTTOM).

Mở Assembly drawing, bấm **09 · Keyplan**. Slot 09 này dùng cho Assembly; Single-part không cần xoay keyplan và không đi qua luồng này. Mặc định **Phương án: Tự nhận**, lấy trục StartPoint–EndPoint trong global với Beam/Column; với main part khác, đọc solid 3D của part được liên kết trực tiếp và lấy phương dài duy nhất bằng PCA hình học. Thành phần đứng trội hơn độ dài hình chiếu ngang chọn phương án Cột; ngược lại chọn Dầm. Không dùng tên/loại BEAM/COLUMN để thay thế hình học. Sau khi nhận Dầm mới chọn X/Y theo thành phần trội trong hệ grid (sau hiệu chỉnh xoay). Đảo đầu cấu kiện không đổi phương án.

Bấm Dầm/Cột để giữ phương án chọn tay; bấm X/Y để giữ hướng chọn tay. **Tự nhận** khôi phục cả nhận phương án và hướng tự động, còn **Hướng: Tự động** chỉ khôi phục hướng dầm. Kiểm tra lại giữ lựa chọn thủ công; sang bản vẽ khác trả về tự nhận. Cột luôn dùng view trái cho X, view phải cho Y và vô hiệu hóa hướng dầm. Cấu kiện ngang/đứng trội đúng bằng nhau (45°, dung sai vector 1e-6) cần chọn phương án tay.

- Chỉ **✓ Đúng trục theo keyplan** khi thuộc tính gốc là **Local**, cả ba góc thuộc miền cho phép, hướng chiếu và vị trí hai view đúng keyplan, đồng thời hai view thực tế khớp với chính thuộc tính gốc đó. Không hiển thị góc khi đạt.
- Bản vẽ đang **Model**, dù hai view đã đúng chiều trục: **không tick**. Hiển thị yêu cầu đổi sang Local và bộ X/Y/Z hợp lệ nếu có nghiệm.
- Trục đúng nhưng vị trí view sai: báo sắp lại view, không gợi ý quay.
- Trục sai và có nghiệm: hiển thị lớn **X / Y / Z** để nhập trong Assembly drawing properties → View creation → Attributes, **Coordinate system: Local**. Đây là góc tuyệt đối, không cộng vào góc cũ. Tạo lại view theo thuộc tính mới khi áp dụng trong Tekla.
- Dò đủ 32 tổ hợp mà không có nghiệm: **Không có bộ góc phù hợp**, không chọn kết quả gần đúng.
- Thiếu dữ liệu hoặc hình học có nhiều phương dài tương đương: báo lý do, không hiển thị số cũ và không tự áp dụng một hướng đoán.

Không cần khai báo góc hiện tại. Nút **Hiệu chỉnh** mở danh sách chọn lưới (có mục Tự động) hoặc đổi chiều chuẩn keyplan 0/90/180/270°. Tự chọn lưới chỉ xét grid mặt bằng có đủ hai họ nhãn hợp lệ; grid phụ thiếu nhãn và grid cao độ không chặn grid chính. Nếu có nhiều grid hợp lệ, ưu tiên phạm vi chứa cả hai đầu cấu kiện và độ thẳng hàng với trục dọc. Trong các grid cùng phù hợp, chỉ ưu tiên grid lớn hơn khi nó chứa đầy đủ cùng các mặt phẳng trục và nhãn của các ứng viên còn lại. Bản sao grid tương đương không gây chặn. Grid xung đột trục/nhãn cần chọn tay; diện tích lớn hơn đơn thuần không đủ chứng minh đó là grid chính.

Dòng tham chiếu của dầm ghi **Top · Trục đứng (trái → phải)** và **Trục ngang gần dầm**, chiếu theo hướng X/Y đang chọn và góc hiệu chỉnh; nhãn lấy quanh trung điểm dầm trong tọa độ global. Trục ngang chỉ ghi trục gần nhất và vị trí dưới/trên/trùng; nếu cách đều thì ghi cả hai, không tự chọn một phía. Khi cấu kiện ngoài phạm vi lưới, không dựng thêm một khoảng trục giả. Nhãn là chuẩn keyplan mục tiêu để đối chiếu, không phải lời khẳng định view hiện tại đã đúng.

Dark mode dùng copper trên nền tối; light mode dùng nền trắng và xanh. Ba thẻ X/Y/Z bằng nhau, số lớn, bỏ khung viền lồng bên ngoài. Dấu nhắc **Đổi Model → Local** đặt ngay trên góc quay; bỏ đoạn hướng dẫn thường lệ dưới số. Khi đã Local chỉ ghi LOCAL. Đường dẫn thuộc tính đầy đủ có trong tooltip.

Hai thẻ tham chiếu cân nhau: cột ghi **VIEW TRÁI · X / VIEW PHẢI · Y**; dầm ghi **TOP · TRỤC ĐỨNG / TOP · TRỤC NGANG**, kèm nhãn trục lớn và biểu tượng phương trục. Dữ liệu thẻ lấy từ cùng bộ tính nhãn keyplan, không phân tích ngược chuỗi giao diện. Cảnh báo sắp view/tái tạo view, thiếu dữ liệu và vô nghiệm vẫn được hiển thị khi cần. Không log hay scrollbar mặc định.

Nút thường dùng gradient 12° theo `MainForm.ApplyMainPresentationTheme`: light trắng → RGB(239,244,251), dark RGB(29,34,42) → RGB(22,26,33), cùng màu viền/chữ MainForm. Light mode dùng xanh đậm RGB(30,58,138) cho số góc, biểu tượng tham chiếu và nút Kiểm tra lại; lựa chọn nền xanh nhạt. Nhãn đổi Model → Local dùng nền xanh nhạt và chữ xanh, không dùng chữ cam trong light mode.

Thuộc tính gốc được đọc từ hộp **Assembly drawing properties của đối tượng đang mở**, không đọc file preset mang cùng tên. Công cụ tạm bỏ chọn rồi khôi phục các đối tượng đã chọn, mở hộp thuộc tính riêng, đọc hai lần và đóng bằng Cancel. Luồng đọc không gọi Get/Apply/Modify/Save/OK; chỉ luồng công tắc Tự áp dụng được gọi Modify. Nếu hộp thuộc tính đã mở, công cụ yêu cầu đóng trước để tránh đọc hoặc ghi đè thay đổi chưa áp dụng của người dùng. Không đọc được thuộc tính thì không tick và không xuất góc.

## Keyplan và quy tắc hình học

Đọc rectangular grid thật và chuyển từ work plane hiện tại sang global. Grid có thể xoay tùy ý; không dùng global X/Y làm keyplan. Khoảng cách X/Y được cộng dồn, hỗ trợ `n*distance`; tầng Z theo cao độ tuyệt đối. Không sắp nhãn theo tên.

Kx là X1→X2, Ky là Y1→Y2, Kz hướng lên.

| Chế độ | View | Trái → phải | Dưới → trên |
|---|---|---|---|
| Dầm X | Front dưới | Kx | Kz |
| Dầm X | Top trên | Kx | Ky |
| Dầm Y | Front dưới | Ky | Kz |
| Dầm Y | Top trên | Ky | −Kx |
| Cột | View trái | Kx | Kz |
| Cột | View phải | Ky | Kz |

Kiểm tra hình chiếu hai khoảng trục trên giấy: phải giữ đúng chiều phải và chiều lên, không xoay lệch trên giấy quá dung sai. Phía nhìn phải là phía trội duy nhất theo keyplan; trường hợp nghiêng đúng 45° giữa hai phía được báo không xác định. Độ xoay tiết diện làm co hình chiếu nhưng vẫn giữ đúng thứ tự trục không bị loại chỉ vì hai hệ trục 3D khác nhau. Việc view đã được tạo đúng từ thuộc tính gốc vẫn dùng so sánh hệ 3D đầy đủ.

Vị trí trên giấy theo tâm khung view từ `GetAxisAlignedBoundingBox`. Chỉ dùng view chứa đúng main part của assembly. Thiếu/trùng view, grid mơ hồ, reflected/unfolded/undeformed và grid không nằm ngang không được tự kết luận đúng. Quy ước hình học X/Y không đổi; chế độ Tự động chọn một trong hai quy ước theo hình chiếu trục StartPoint–EndPoint của dầm lên grid. Dầm đứng hoặc có hai thành phần bằng nhau (45°, dung sai vector 1e-6) yêu cầu chọn X/Y. Chọn tự động không nới điều kiện khớp trục hoặc lấy góc gần đúng khi cả 32 bộ đều không đạt.

**Tự nhận phương án khác với đặt đứng view của Tekla.** `VerticalColumn` vẫn phản ánh quy tắc đặt đứng đã được kiểm chứng của Tekla, không tự bật chỉ vì giao diện chọn Cột. Nhận một cấu kiện dốc là phương án Cột không chứng minh nó có nghiệm: bộ dò vẫn bắt buộc khớp trục chiếu. Không chuyển sang phương án khác chỉ vì phương án đó tìm được góc dễ hơn.

Kiểm chứng chỉ đọc 2026-09-27 trên `[C-csb20W-13]`: tên main part BEAM, trục thật đứng, hai đầu chênh Z 866 mm, Top nằm trái Front. Tự nhận Cột và trả **Local/270/0/0**, bằng phương án Cột thủ công; cả hai phương án Dầm X/Y vô nghiệm. Không sửa/tái tạo view trong bản vẽ người dùng khi kiểm chứng này.

## Góc tuyệt đối đã kiểm chứng

Miền duy nhất: **X={0,90,180,270}; Y={0,180}; Z={0,90,180,270}**. `Angles` từ chối cả góc âm, 360, góc gần 90, NaN và Infinity.

Gọi B là hệ Local của main part đọc trực tiếp từ model. Quy tắc đo được trên **Tekla 2025 SP7, build 56843**:

`B' = B · Rx(−X) · Ry(−Y) · Rz(−Z)`

Front dùng `(B'.X, B'.Y)`, Top dùng `(B'.X, −B'.Z)`. Các phép quay tạo một hệ 3D chung trước khi chiếu view. Z không phải phép quay độc lập trên từng view giấy.

Với cấu kiện đứng và `XS_USE_VERTICAL_PLACING_FOR_COLUMNS_IN` áp dụng cho assembly: thêm phép quay trên giấy −90° nếu trục dọc hướng lên, +90° nếu trục dọc hướng xuống. Cấu kiện dựng từ trên xuống có thể được API phân loại BEAM; thuật toán nhận biết theo vector thật, không theo tên cấu kiện.

`SearchKeyplan` đánh giá đủ 32 tổ hợp trên cả hai view theo trục chiếu. Chỉ nhận sai lệch trên giấy không vượt dung sai số **0.0001°**. Nhiều nghiệm tương đương được chọn ổn định, ưu tiên ít ô khác 0. Không suy ngược góc từ view hiện tại và không mặc định góc cũ bằng 0. `SearchLocal` được giữ để kiểm thử độc lập độ chính xác của hệ quay 3D.

## Phạm vi và kiểm chứng

Xuất góc và xác nhận cho Assembly có main part Beam/Column hoặc Part khác có solid 3D với một phương dài duy nhất. Dầm giữ quy ước Front/Top; cột tính theo hai loại view tiêu chuẩn thực tế Front/Top/Back/Bottom. Cột cần tùy chọn đặt đứng cho assembly. Assembly thiếu liên kết main part rõ ràng được kiểm từng view và chỉ đưa hướng giấy riêng, không chế tạo một bộ Local/X/Y/Z chung. Phiên bản, ngôn ngữ hộp thuộc tính hoặc tùy chọn chưa kiểm chứng sẽ báo chưa xác minh, không tự nhận đạt.

- **224 bản vẽ thử, 448 hệ trục view thật**, đủ 32 tổ hợp cho 7 mẫu: dầm X/Y, dầm xoay tiết diện, cột, dầm/cột đảo đầu, dầm xoay mặt bằng 37.25°.
- 224 phép tìm ngược đối chiếu toàn bộ tập nghiệm với dữ liệu Tekla; sai lệch lớn nhất **1.20×10⁻¹⁰°**, thấp hơn dung sai kiểm tra.
- 10.368 phép dò bằng oracle ma trận nguyên độc lập cho sáu cặp view và hai thứ tự, **331.776 ứng viên**, 31.105 assertion; 351 kiểm tra ngữ nghĩa và dữ liệu lỗi.
- `[A-C12-9]`: cặp Front trái / Bottom phải dùng **Local/0/0/0**. Đã đo đủ 32 bộ góc và 64 hệ trục thực tế, sau đó trả về 0/0/0. Front trái có Right=+X, Up=+Z; Bottom phải có Right=+Y, Up=+Z. Grid mục tiêu lần lượt S17A→S18 và SG-1→SF-1. Không có nhãn hay ID mẫu trong thuật toán production.
- Assembly `[C-BP-4]` với main part `ContourPlate / BASE_PLATE` được đọc từ solid 3D, nhận `Local/0/0/0`, chạy 32 bộ góc thực tế và khôi phục bản vẽ sau kiểm tra. Contour bị đảo thứ tự, cấu kiện xoay, tấm vuông có hai phương tương đương và view lệch riêng đều có kiểm tra riêng.
- 84 phép dò theo trục chiếu, đối chiếu bộ đo Tekla bằng phép chiếu hai khoảng trục có nhãn độc lập.
- Đọc đủ 32 bộ góc gốc Local dù file preset cùng tên bị đổi thành Model; giữ nguyên lựa chọn đối tượng sau khi đọc.
- Trọn luồng trên Tekla thật cho dầm và cột: bản vẽ sai → đề xuất góc → tạo lại bằng chính góc đề xuất → Reader trả `Correct`, không xuất góc nữa.
- UI light/dark: mở tự kiểm tra, đổi chế độ tự kiểm tra, tick không có góc, kết quả sai có thẻ XYZ, vô nghiệm xóa số cũ.

Chi tiết và dữ liệu đo: [TEKLA_VALIDATION.md](../tests/Keyplan/TEKLA_VALIDATION.md).

## Ranh giới ghi dữ liệu

Slot 09 luôn chỉ đọc Model API: không Insert/Modify/Delete/CommitChanges đối tượng model, không đổi work plane hay advanced option. Luồng đọc bản vẽ không ghi; chỉ khi người dùng bật Tự áp dụng mới được Modify thuộc tính **bản vẽ** qua hộp native. Không lưu/mở/đóng drawing, không gọi Insert/Delete view trực tiếp; native Modify chỉ được tái tạo khi có xác nhận riêng. Model chuyển đổi trong lúc đọc hoặc bản vẽ có parts đã sửa chưa cập nhật sẽ bị chặn.

Kiểm chứng ghi thật được người dùng cho phép trên [C-csb20W-13]: sửa từ Model/0/0/0 sang Local/270/0/0; kiểm thêm chuẩn keyplan xoay 180° cho Local/90/0/0, sau đó trả về chuẩn 0° và Local/270/0/0. Luồng một hộp thoại chạy đủ Modify/Apply/OK và tự xử lý xác nhận Tekla; mở lại độc lập xác nhận giá trị lưu và hai view đều Correct. Chạy lại không Modify. Trục Local và hai đầu main part trước/sau không đổi. UI light/dark đã kiểm lưu bật/tắt, mở lại tự chạy, lỗi giữ lựa chọn và không tự thử lại. 323 kiểm tra ngữ nghĩa và toàn bộ 448 hệ trục Tekla đo trước đó vẫn qua.

Mẫu người dùng xác nhận `[A-H100-3]`, **hướng Y**: Front/Top có SG-1 bên trái → SF-1 bên phải; Top có S17A dưới → S17 trên; Front có tầng thấp dưới → tầng cao trên. Bản gốc Model/0/0/0 phải đề xuất **Local/0/0/0**, không tick. Hướng X được tính riêng và mẫu này không có nghiệm trong 32 bộ cho phép. Không có tên nhãn hay ID mẫu này trong mã production.

**Hướng X của cùng mẫu**: Top/Front phải có S17 trái → S17A phải; Top có SG-1 dưới → SF-1 trên; Front có 2FL dưới → 3FL trên theo cao độ thật. Quy tắc này vẫn giữ nguyên khi không có nghiệm, không hoán đổi X/Y để lấy một kết quả dễ đạt.

`TeklaExperiment.cs` là bộ thử phát triển riêng, không được compile vào ứng dụng/portable. Mọi phép tạo/sửa trong kiểm chứng được giới hạn bằng đường dẫn bản sao model. Model nguồn và bản vẽ đang làm việc đã được mở lại sau kiểm thử.

Nguồn hãng: [Part orientation](https://support.tekla.com/doc/tekla-structures/2025/dra_setting_part_orientation), [vertical column placing](https://support.tekla.com/doc/tekla-structures/2025/xs_use_vertical_placing_for_columns_in). Thứ tự/dấu quay chi tiết ở trên được xác minh bằng đo thực tế, không chỉ suy từ tài liệu.
