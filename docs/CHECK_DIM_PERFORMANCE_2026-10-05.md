# Tối ưu Check DIM, giữ nguyên quy tắc

Đo bằng probe x64 trên Tekla 2025, active drawing `[C2-PC2-7]`, ngày 2026-10-05.
Chỉ đọc; không đổi bản vẽ, model, selection, work plane hoặc file trong model.

## Việc lặp đã giảm

- Một part xuất hiện trong nhiều view trước đây bị SelectModelObject/GetSolid/
  GetReferenceLine/GetBolts lặp lại. Nay mỗi lần Analyze có cache part và feature
  global riêng. Chỉ cache lượt đọc thành công; lỗi giữ prefix và cảnh báo, không
  cache lỗi. Không có cache static, cache file hoặc cache xuyên bản vẽ/lần chạy.
- Mỗi view nhận Feature/Point mới qua ProjectFeatures. ConstrainReferences sửa
  Allowed riêng trên bản sao; không làm bẩn snapshot global hoặc view khác.
- ID view, ID DIM và tên view được tái sử dụng thay vì gọi lại ở mỗi chân.
- Check DIM cần Nodes/Edges, primary, dummy và nhãn từ Slot08; không đọc quyết
  định giữ/ẩn/hiện của neighbor. AnalyzeDimensionConnections chạy cùng phần
  dựng graph/Resolve nhưng bỏ RetainLocalDummies, RetainConnectionReferenceSupports,
  RetainPlateContactReferenceSupports và lập Restore/Reason. Các nhánh này chỉ
  sửa Keep/proof/visibility, không tạo Nodes/Edges hay đổi primary/dummy.
- Slot08.Analyze và BuildConnections vẫn chạy đầy đủ graph + visibility như cũ.
  Giữ tên điểm gọi/reflection, không thay thuật toán dọn neighbor.

Giữ nguyên Classify, ValidateChain, ConstrainReferences, CheckConnectedMemberSpans,
CheckNotchPairs, phép đo/so sánh/dung sai, xử lý thiếu dữ liệu, đọc giá trị hiển thị,
đối chiếu active drawing cuối lượt. Không chạy API đa luồng hoặc bỏ kiểm hình học.

## Số đo

Thời gian riêng `Analyze()`, không gồm mở form, biên dịch probe hay xuất XML:

| Lượt trong tiến trình | Trước (ms) | Sau (ms) |
| --- | ---: | ---: |
| 0 | 5334.883 | 4334.881 |
| 1 | 4781.429 | 3438.975 |
| 2 | 4714.698 | 3690.718 |
| 3 | 5179.074 | 3410.667 |
| 4 | 4462.059 | 3584.704 |

Trung vị 4781 → 3585 ms, giảm khoảng 25%; lượt đầu giảm khoảng 19%.
Đây là số đo trên một bản vẽ; không cam kết cùng tỷ lệ cho mọi bản vẽ/máy.
Đo riêng graph đầy đủ ~1684 ms, graph cho DIM ~1236 ms; replay Classify ~218 ms.
Phân loại chân chiếm phần nhỏ nên không viết lại các quy tắc đang đúng để tiết kiệm ít CPU.

## Kiểm chứng tương đương

- XML trước/sau khớp chính xác: 49.949 feature trong 5 view, cờ, tọa độ 3D,
  53 chuỗi / 135 chân, verdict, reason, nhãn và vị trí hiển thị.
- Report khớp sau khi bỏ timestamp/số đo benchmark: evidence, owner, residual,
  quan hệ graph, cảnh báo và loại DIM bỏ qua.
- 5 lượt mỗi bản build có cùng report; cache không mang sang lượt tiếp theo.
- Graph DIM và graph Slot08 đầy đủ cùng Nodes/Edges/primary/dummy/label/connected
  trên mẫu (466 node), kiểm bằng probe sau tối ưu.
- Hồi quy geometry cũ và kiểm mới về chuyển hệ tọa độ, copy cờ/điểm, không rò
  Allowed giữa view, dữ liệu không hữu hạn giữ prefix và báo lỗi.
- Không live-test ghi bản vẽ. Chưa benchmark trên bản vẽ khác hoặc SinglePart;
  SinglePart dùng cùng cache hình học, không đi qua graph assembly.

## Chạy lại

```powershell
pwsh -NoProfile -File tests/DimensionCheck/Run-ReadOnly.ps1 -Benchmark -Name perf-after
pwsh -NoProfile -File tests/DimensionCheck/Run-ReadOnly.ps1 -Benchmark -Name perf-before -ProductPath '<baseline EXE>'
pwsh -NoProfile -File tests/DimensionCheck/Compare-Audits.ps1 -Before '<before.xml>' -After '<after.xml>'
```

Baseline EXE và DLL phải cùng bộ; probe nhận ProductPath rõ ràng. Tất cả output
ở `.codex-artifacts/dimension-check`. Chạy tuần tự, giữ nguyên active drawing/model
khi so sánh. Snapshot/cache không phải phụ thuộc sản phẩm và có thể tạo lại.

Build Release x64 bằng `scripts/Update-Portable.ps1 -BuildOnly -NoPause`, đối chiếu
40 runtime với build stage bằng SHA-256; không dùng manifest phát hành cũ để kiểm
build local. Không commit/push/phát hành.
