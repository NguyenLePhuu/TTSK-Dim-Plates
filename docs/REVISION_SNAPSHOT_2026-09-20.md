# Sửa nhận diện snapshot sau Create drawing — 20/09/2026

## Nguyên nhân đã kiểm chứng

Đọc trực tiếp bản vẽ đang mở `[BmC-117]` bằng Drawing API và dữ liệu DPM,
không chạy Create drawing, đổi màu, lưu bản vẽ hoặc thay work plane trong quá trình audit.
Baseline SHA-256: `5AC73FA75D268A0C30BBB9A2A805540703C25E0DB44105CB61A77A375094BBDF`.

Snapshot có cụm 75–75 bên phải. `ReadWitnessFeet` lấy đầu **nét dóng đã có
khoảng hở hiển thị** rồi `SameChainFeet` so như chân đo thật với sai số 0,3 mm giấy.
Sau Create drawing, ID và hình học trình bày của DIM được tạo lại:

| Bằng chứng trên view đứng, đơn vị mm giấy | Snapshot | Sau Create drawing |
|---|---:|---:|
| Biên phải cấu kiện | 255,3 | 255,5 |
| Đầu nét dóng tại hàng trên | 258,3000186 | 257,5 |
| Đường dim bên phải | 274,3 | 275,5 |
| Các trạm đo theo phương Y | −7,5; 0; 7,5 | −7,5; 0; 7,5 |
| Số hiển thị | 75; 75 | 75; 75 |

Sai khác đầu nét dóng phía trên khoảng 0,8000186 vượt ngưỡng 0,3; cụm cũ bị
loại khỏi ứng viên rồi bị kết luận sai là DIM mới. Không phải thiếu cụm trong snapshot.
Cụm 75–75 ở giữa có vùng nét dóng tách biệt và không có trong snapshot;
người dùng xác nhận cụm này phải đỏ.

## Thay đổi

- Giữ nguồn gốc hình học nét dóng riêng. Khi dựng lại DIM, cho phép đối chiếu
  các trạm ngoài trùng nhau và vùng nét dóng cùng chiều tại **cả hai đầu**.
  Mỗi đầu phải chồng lấp ít nhất 65% chiều dài lớn hơn của hai nét dóng từ
  đầu nét tới đường DIM. Đây là bằng chứng về hình học trình bày, không phải
  khẳng định đã đọc được associativity lịch sử. Không tăng ngưỡng chân đo
  0,3 và không ghép chỉ vì hai cụm cùng hiển thị 75–75.
- Dùng hai trạm mũi tên để xác định phương đo, thay vì góc quay chữ.
- Ghép chuỗi theo các đoạn nối tiếp, tránh chia chuỗi do sai số rất nhỏ
  ở cao độ đường DIM và thứ tự liệt kê. Không tự nối nhánh/đoạn trùng.
- Các ứng viên cạnh tranh vẫn phải được giải quyết duy nhất; ưu tiên topology
  không bị bước chọn đường gần nhất đảo ngược.
- Dữ liệu không hữu hạn, view không có đối chiếu, thiếu nét dóng, đọc thiếu
  cụm, baseline thiếu đoạn hoặc tỷ lệ giấy không tương thích chuyển sang
  cần kiểm tra. Không coi lỗi đọc là bằng chứng DIM mới.
- `Apply` chặn tự động ghi đỏ đối với đối tượng cần kiểm tra/không đổi;
  chọn màu thủ công vẫn đi qua luồng riêng.
- F5 khôi phục màu gốc đã lưu cho DIM hiện được chứng minh không đổi nhưng
  từng bị Revision tô đỏ, khi ID và chữ ký lưu khớp. DIM không có bản ghi
  màu gốc phù hợp không tự bị đổi. Native writer kiểm tra màu vẫn là Red
  trước khi khôi phục, tránh ghi đè thay đổi màu xen giữa audit và ghi.

Đã lần theo luồng đóng băng/hash/cache baseline → DPM → Drawing API → ghép
view/chuỗi/đoạn → phân loại → UI → native writer/kiểm tra lại/rollback,
và luồng Create drawing thay DIM, khôi phục vị trí view. Không đổi thuật toán
tạo DIM các slot, Arrange DIM, mark hay bộ ghi thuộc tính ngoài màu chữ.

## Kết quả

| Kết quả trên 19 đoạn hiện hành | Trước sửa | Sau sửa |
|---|---:|---:|
| Không đổi | 12 | 14 |
| Thay đổi giá trị thật | 3 | 3 |
| DIM mới | 4 | 2 |

- Bên phải: child 4530/4531, `75 → 75`, không đổi.
- Ở giữa: child 4527/4528, mới, giữ đỏ.
- Hai số `2553 → 2555` và một số `688,2 → 690,2` vẫn là thay đổi.
- Planner màu của form thật, chạy chỉ đọc, chọn đúng 4530/4531 để trả về
  `Black` (màu trắng khi Tekla hiển thị trên nền đen). Chưa áp dụng ghi màu
  trực tiếp trên bản vẽ trong phiên này; chạy F5 bằng portable mới để áp dụng.

216 kiểm tra mới đạt: tái dựng toàn bộ ID, hình học DPM của lỗi thực tế,
cụm mới ở giữa, cạnh tranh snapshot, hướng dóng đối lập, thiếu dữ liệu,
góc chữ độc lập, chuỗi nhiễu/đảo thứ tự, quay/đối xứng/dịch chuyển lớn và
thay đổi tỷ lệ đồng thời hai bộ dữ liệu. Bộ cũ cũng đạt: 90 classifier cases,
506 hardening assertions, 67 Revision assertions, session choices và 60 batch assertions.

Đã build Release x64, xác minh SHA-256 đủ 39 runtime files trong portable,
chạy lại 216 kiểm tra và audit chỉ đọc bằng chính binary portable đã sao chép
vào test host. Build có 30 cảnh báo sẵn có ngoài các file sửa, không có lỗi.
Portable EXE SHA-256: `4007BFBF63C89B2B960E4AAE79AB537CEB766BA5067D1F25511027160BDB6A48`.

Bước dọn output tạm chưa hoàn tất: automatic approval review từ chối lệnh
PowerShell xóa các thư mục build/test đã xác minh nằm trong workspace, với
thông báo `blocked by policy`. Không chuyển sang cơ chế khác để vượt chặn.
Output còn tại `.codex-artifacts/revision-investigation`,
`.codex-artifacts/Slot09Check`,
`.codex-artifacts/portable-update/20260920-233427-45fdcc88`,
`tests/Slot09DimensionCheck/obj`, và các thư mục `bin/Release`,
`bin/x64/Release`, `obj/Release`, `obj/x64/Release` của project chính.
Các thư mục Debug có cấu hình cá nhân và output phát hành tồn tại từ trước
được giữ nguyên. Chưa commit, push hoặc phát hành bản mới.

## Chạy lại kiểm chứng

Chạy từ thư mục repository:

```powershell
dotnet build tests/Slot09DimensionCheck/Slot09DimensionCheck.csproj -c Release -p:Platform=x64
& '.codex-artifacts/Slot09Check/tests/Slot09DimensionCheck.exe' --revision-matching-tests
& '.codex-artifacts/Slot09Check/tests/Slot09DimensionCheck.exe' --revision-matching-audit '<thư mục kết quả tạm>'
```

Fixture `tests/Slot09DimensionCheck/fixtures/revision-create-bmc117.tsv` lưu
hình học đã đọc để tái hiện lỗi mà không cần sửa bản vẽ hoặc phụ thuộc ID mới.
IDs và kích thước mẫu chỉ có trong fixture/tests, không nằm trong quy tắc production.

## Giới hạn xác minh

DPM lưu hình học trình bày, không lưu đầy đủ liên kết chân đo lịch sử.
Các cụm rất gần nhau có hình học giống nhau, DIM bị override, view đổi tỷ lệ,
hoặc dạng DIM chưa hỗ trợ không được xem là đã chứng nhận tuyệt đối.
Không có lời bảo đảm không thể sai trên mọi bản vẽ. Đã xác minh bản vẽ hiện
tại sau Create drawing và các biến thể trong bộ kiểm thử; chưa chạy lại thao
tác Create drawing có ghi dữ liệu trên nhiều bản vẽ thực tế trong phiên này.
