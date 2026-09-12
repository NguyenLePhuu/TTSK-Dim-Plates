# Gói chia sẻ Google Drive / OneDrive

Chạy từ thư mục gốc dự án bằng PowerShell. Script chỉ đóng gói bản build đã có; không tự cập nhật portable, commit, push hoặc upload.

```powershell
dotnet msbuild "TTSK Dim Plates\TTSK Dim Plates\TTSK Dim Plates.csproj" /t:Rebuild /p:Configuration=Release /p:Platform=x64 /m /nologo /v:minimal
# Chỉ tiếp tục nếu build thành công.
& .\distribution\Build-CloudPackage.ps1 -AllowUnsigned
```

`-AllowUnsigned` tạo bản chẩn đoán, chưa phải bản phát hành đã ký. Mặc định script yêu cầu chữ ký Authenticode hợp lệ. Khi có chứng thư code signing tin cậy, ký EXE trong output Release bằng SHA-256 và timestamp trước, rồi chạy script không có `-AllowUnsigned`. Không rebuild sau khi ký. Có thể truyền `-BuildDirectory` cho thư mục build/ký riêng. Chữ ký hợp lệ không bảo đảm cloud sẽ bỏ cảnh báo.

Kết quả nằm trong `.codex-artifacts/cloud-release/<mã-lần-chạy>/`: ZIP, SHA-256 ZIP, nhật ký quét thư mục và ZIP, `validation.json`. Bên trong ZIP có `SHA256.csv` cho runtime và README; manifest không tự chứa hash của chính nó. Script lấy dependency và nội dung runtime từ csproj, kiểm tra tệp bắt buộc, hash bản sao, rồi quét Defender. Khi bất kỳ bước nào lỗi, không chia sẻ sản phẩm của lần chạy đó.

Chỉ chia sẻ ZIP runtime đã kiểm tra; giải nén toàn bộ và chạy `TTSK Dim Plates.exe`. Không cần chạy BAT/PowerShell trên máy người dùng. Không gửi nguyên checkout chứa Git, backup EXE, bản Debug, PDB, log, shortcut hoặc cấu hình cá nhân. Macro `Phu_Macro_GridVisibility.cs`, từ điển và hình ảnh vẫn được giữ vì ứng dụng cần chúng.

Nếu cloud vẫn chặn, lưu tên cảnh báo, SHA-256, liên kết file và thời điểm; yêu cầu dịch vụ kiểm tra nhận nhầm đối với đúng file đó. Không thay đuôi, đặt mật khẩu ZIP hoặc tắt antivirus để vượt kiểm tra.

- Microsoft: https://www.microsoft.com/en-us/wdsi/filesubmission
- OneDrive for Business / SharePoint: https://learn.microsoft.com/en-us/troubleshoot/sharepoint/security/false-positive-malware-detections
- Google Drive, giới hạn quét 100 MB: https://support.google.com/drive/answer/141702
- Chữ ký và reputation: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation

Nếu PowerShell bị chặn bởi chính sách của tổ chức, dùng quy trình ký/phê duyệt script của tổ chức; không đổi chính sách hệ thống.

## Phân biệt kênh Cloud Package và GitHub Release

- **Kênh GitHub Release (Tự động cập nhật cho người dùng cuối):**
  - Đóng gói và phát hành tự động qua GitHub Actions từ nhánh `main`.
  - Tệp asset chính: `TTSK-Dim-Plates-Portable.zip` và `TTSK-Dim-Plates-Portable.zip.sha256`.
  - Kiểm tra tính toàn vẹn bằng SHA-256 đối chiếu từng file runtime theo manifest. Client tự động nhận diện và cập nhật in-app mà không cần quyền Admin hay PowerShell.
- **Kênh Cloud Package (Chia sẻ Google Drive / OneDrive):**
  - Sử dụng `Build-CloudPackage.ps1` để quét Defender và kiểm tra chữ ký Authenticode trước khi tải lên các dịch vụ lưu trữ đám mây của doanh nghiệp.
  - Phục vụ môi trường kiểm soát nghiêm ngặt theo chính sách riêng của tổ chức.
