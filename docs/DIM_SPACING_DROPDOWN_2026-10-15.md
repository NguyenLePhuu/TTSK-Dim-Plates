# Dropdown Phạm vi — Dimension Spacing

- Chỉ thay lớp `BorderComboBox` trong `MainForm.cs`. Ô chọn bỏ vạch chia và tam giác đặc, thay bằng chevron nét mảnh đổi hướng khi mở; viền nhẹ, có trạng thái hover/focus. Popup cùng chiều rộng với ô chọn, hàng thoáng, nền hover/đang chọn và dấu ✓, theo theme xanh sáng/cam tối.
- `ScopeDropDownMenu` dùng `ToolStripDropDown` với Flow TopDown để giữ khoảng đệm và kích thước hàng. Không dùng layout cột icon/check mặc định của ContextMenuStrip vì WinForms tự tính lại padding. Khung DWM yêu cầu bo góc/bóng và viền theo theme; Windows không hỗ trợ dùng viền đơn, không cắt Region răng cưa. `ScopeMenuRenderer` vẽ chữ, nền hàng và dấu chọn theo DPI.
- Giữ nguyên bốn giá trị: **Toàn bộ bản vẽ / Front / Top / Bottom**, SelectedIndex/Text/SelectedIndexChanged, nút Áp dụng và toàn bộ `RunDimSpacing`/`PHU_DimSpacingNormalize`. Chọn phạm vi chỉ đổi giá trị, không tự gọi API áp dụng spacing. Code Check Dim/Combo của lượt trước không thay đổi.
- Enter/Space/F4 mở menu; Up/Down khi đóng giữ hành vi chọn cũ. Trong popup, mũi tên chuyển hàng, Enter chọn, Esc đóng không đổi giá trị. Chỉ mở khi nhấn/thả chuột trái trong ô; đóng khi ẩn, disable, resize hoặc đổi theme. Bấm lại ô đang mở không tự mở lại sau AutoClose; popup dispose cùng control.

## Kiểm chứng

- Đối chiếu source trước/sau: mọi dòng ngoài lớp BorderComboBox giữ nguyên; SHA-256 các file C# sản phẩm khác khớp snapshot đầu lượt sửa.
- Probe `.codex-artifacts/spacing-dropdown/Preview.cs` nạp **control thật từ portable EXE** bằng reflection, chạy trên desktop thử riêng và không chuyển input desktop. Kiểm 108 điều kiện ở DeviceDpi 96; sau khi bổ sung kiểm bàn phím trong popup và sát mép màn hình, đạt **124 điều kiện ở DPI hệ thống 120 (125%)**, cả sáng/tối.
- Kiểm hit-test, mở bằng chuột, chiều rộng/hai mép popup, đủ bốn hàng, DWM corner preference, chọn chính xác từng giá trị và event không lặp, dấu chọn sau mở lại, Enter/Space/F4, Up/Down, Esc, ẩn/disable/resize/theme/dispose và popup nằm trong working area khi mở sát đáy màn hình.
- Ảnh client-render sáng/tối, mở/đóng/hover nằm trong `.codex-artifacts/spacing-dropdown/` và `native-dpi/`. Ảnh dùng WM_PRINTCLIENT để không lẫn khung non-client do DrawToBitmap vẽ; ảnh client không chứa bóng/góc compositor DWM. Corner preference đã xác minh trực tiếp qua DwmGetWindowAttribute. Cấu hình DPI bổ sung chỉ thuộc probe, không đổi cấu hình sản phẩm.
- Release x64/.NET Framework 4.8/C# 7.3 BuildOnly thành công: `20261015-012245-fe50bf5a`; 40 runtime đối chiếu với output build, cập nhật/kiểm đủ 41 dòng `portable/SHA256.csv`. Code Map đã cập nhật và Check.
- Không gọi API Tekla, không sửa bản vẽ hoặc ghi model, không commit/push/phát hành. Probe/ảnh là dữ liệu kiểm tạm, không phải phụ thuộc sản phẩm.
