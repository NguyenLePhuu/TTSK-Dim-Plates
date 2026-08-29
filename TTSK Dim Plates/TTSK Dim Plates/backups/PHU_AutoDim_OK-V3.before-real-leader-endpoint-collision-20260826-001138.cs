#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Drawing.UI;

using ModelPart = Tekla.Structures.Model.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;

// ========================================================================================
// PHU_AutoDim_OK-V3 - CLEAN / MODULED VERSION
// Ghi chú: Copy file này vào: TeklaStructuresModels --> Tên dự án --> macros --> drawings
// **************** Chức năng ****************
// Auto xóa DIM cũ
// Auto DIM plate chính (t<16 đường bao, >16 Dim lỗ ...)
// Auto resize khung view chứa VL
// Auto sắp xếp 2 view cách nhau
// Auto select view sau khi chạy
// ========================================================================================

namespace Tekla.Technology.Akit.UserScript
{
    public class Script
    {
        #region 00 - CONFIG CONSTANTS
        private const double TOL = 0.0;

        // KHÔNG CÓ CHAMFER:
        // Tất cả dim thường dùng khoảng cách cố định 100mm tính từ mép tấm.
        // Không bù trừ theo chân dim.
        private const double NORMAL_NO_CHAMFER_DIM_OFFSET = 100.0;
        private static double CurrentDimTierUnit = NORMAL_NO_CHAMFER_DIM_OFFSET;

        private const double TOP_LENGTH_DIM_OFFSET = NORMAL_NO_CHAMFER_DIM_OFFSET;
        private const double TOP_THICKNESS_DIM_OFFSET = NORMAL_NO_CHAMFER_DIM_OFFSET;
        private const double BOTTOM_LENGTH_DIM_OFFSET = NORMAL_NO_CHAMFER_DIM_OFFSET;
        private const double BOTTOM_HEIGHT_DIM_OFFSET = NORMAL_NO_CHAMFER_DIM_OFFSET;

        // LOGIC TẦNG DIM CỐ ĐỊNH TỪ MÉP THANH:
        // Tầng 1 = 63  : dùng cho DIM chamfer
        // Tầng 2 = 125 : dùng cho DIM tổng + DIM lỗ
        // Tầng 3 = 190
        // Tầng 4 = 255
        private const double DIM_TIER_1 = 63.0;
        private const double DIM_TIER_2 = 125.0;
        private const double DIM_TIER_3 = 190.0;
        private const double DIM_TIER_4 = 255.0;

        // PLATE HOLE FAMILY - port đúng nguyên tắc đang dùng ổn định trong Shape L:
        // tách family kỹ thuật trước, sau đó mới gom hàng/cột theo hình học.
        private const double HOLE_FAMILY_SIZE_TOL = 1.0;
        private const double HOLE_ROW_COLUMN_TOL = 3.0;
        private const double HOLE_CLUSTER_SPLIT_GAP = 200.0;
        private const double HOLE_END_ZONE = 300.0;

        // Dung sai cục bộ duy nhất cho việc nhận hai đầu thuộc cùng một mép plate.
        // Không tăng TOL toàn cục vì TOL còn tham gia nhận dạng chamfer/feature.
        // 0.1 mm chỉ hấp thụ sai số chiếu Solid -> view, không thay đổi hình học DIM.
        private const double PLATE_EDGE_FOOT_ALIGNMENT_TOL = 0.1;

        // CHỈNH KHUNG VIEW TÍM Ở ĐÂY
        // 10  = khung sát hơn
        // 20 = vừa
        // 50~100 = rộng hơn
        private const double VIEW_PADDING = 20.0;

        // AUTO DIM GÓC VÁT
        // Tự dim thêm chiều ngang + chiều dọc của từng cạnh vát.
        private const bool AUTO_DIM_CHAMFER = true;

        // BASE DIM MẶT TẤM:
        // Chỉ plate có bề dày > 16mm mới tạo DIM chi tiết trên mặt tấm
        // (lỗ và chamfer của đường bao tấm). Tấm mỏng chỉ DIM đường bao tổng.
        // Không áp dụng ngưỡng này cho vát theo chiều dày ở view chiếu độ dày
        // đã xác nhận hoặc Section A-A.
        private const double PLATE_FACE_DETAIL_DIM_THICKNESS_THRESHOLD = 16.0;
        private const double PLATE_THICKNESS_PROJECTION_SPAN_TOL = 5.0;

        // Tầng dim góc vát:
        // 1 = 63
        // 2 = 126
        // 3 = 189 ...
        // Nên để tầng 1 để dim góc vát nằm gần plate,
        // còn dim tổng/dim lỗ nằm tầng ngoài hơn.
        private const int CHAMFER_DIM_TIER = 1;
        private const double CHAMFER_MIN_SIZE = 5.0;

        // Chỉ xem là chamfer nếu cả chiều ngang và chiều dọc đều nhỏ hơn 80mm.
        // Các cạnh xiên lớn hơn sẽ không dim chamfer và không đẩy tầng DIM tổng/lỗ.
        private const double CHAMFER_MAX_SIZE = 80.0;

        // Lọc cung tròn/polycurve:
        // Cung tròn thường bị chia thành nhiều đoạn nhỏ rất bẹt, ví dụ 22.5 x 2.1.
        // Chamfer thật thường có tỷ lệ ngang/dọc tương đối cân bằng.
        private const double CHAMFER_MIN_RATIO = 0.35;
        private const double CHAMFER_MAX_RATIO = 2.85;

        // AUTO DIM R CHO MẶT ĐỘ DÀY / THIN VIEW.
        // Fillet chỉ được xác nhận từ Solid Edge có Type = CURVED_SURFACE.
        // Cạnh xiên thẳng (chamfer) là NORMAL và sẽ đi nhánh riêng khi bổ sung sau.
        private const bool AUTO_DIM_THIN_VIEW_FILLET_RADIUS = true;
        private const double THIN_FILLET_ENDPOINT_MATCH_TOL = 1.0;
        private const double THIN_FILLET_MIN_CHORD = 1.0;
        private const double THIN_FILLET_MIN_SAGITTA_CHORD_RATIO = 0.02;
        private const double THIN_FILLET_MAX_PATH_CHORD_RATIO = 2.20;
        private const double THIN_FILLET_MIN_SWEEP_DEG = 5.0;
        private const double THIN_FILLET_MAX_SWEEP_DEG = 175.0;
        private const double THIN_FILLET_TANGENT_SIN_TOL = 0.17364817766693033; // sin(10°)
        private const double THIN_FILLET_RADIUS_RESIDUAL_MIN_TOL = 0.25;
        private const double THIN_FILLET_RADIUS_RESIDUAL_RATIO = 0.005;

        // Distance lấy đúng theo 4 DIM mẫu thủ công.
        private const double THIN_FILLET_DISTANCE_RIGHT_TOP = -7.59750429393739;
        private const double THIN_FILLET_DISTANCE_RIGHT_BOTTOM = -3.24176895884622;
        private const double THIN_FILLET_DISTANCE_LEFT_TOP = -2.5701706264229;
        private const double THIN_FILLET_DISTANCE_LEFT_BOTTOM = -1.78082545781903;

        // AUTO DIM ANGLE CHO CHAMFER TOÀN PHẦN HOẶC CHỈ VÁT MỘT PHẦN ĐỘ DÀY.
        // Distance va chieu dai tia dung duoc lay dung theo 4 file dump mau.
        private const bool AUTO_DIM_THIN_VIEW_CHAMFER_ANGLE = true;
        private const double THIN_CHAMFER_EDGE_TOL = 1.0;
        private const double THIN_CHAMFER_MIN_RUN = 5.0;
        private const double THIN_CHAMFER_MAX_RUN = 80.0;
        private const double THIN_CHAMFER_DISTANCE_LEFT_TOP = 4.08722821479631;
        private const double THIN_CHAMFER_DISTANCE_RIGHT_TOP = 4.08722821648578;
        private const double THIN_CHAMFER_DISTANCE_LEFT_BOTTOM = 4.08722821648984;
        private const double THIN_CHAMFER_DISTANCE_RIGHT_BOTTOM = 4.3717073415651;
        private const double THIN_CHAMFER_RAY_LENGTH_LEFT_TOP = 36.0;
        private const double THIN_CHAMFER_RAY_LENGTH_RIGHT_TOP = 45.0;
        private const double THIN_CHAMFER_RAY_LENGTH_LEFT_BOTTOM = 29.0;
        private const double THIN_CHAMFER_RAY_LENGTH_RIGHT_BOTTOM = 29.0;

        // SECTION A-A: chỉ phân rã DIM bề dày khi trục X của SectionView đúng là
        // bề dày thật của plate. Dung sai 5mm đồng nhất với nhận dạng thin view.
        private const double SECTION_THICKNESS_SPAN_TOL = 5.0;
        private const double SECTION_CHAMFER_MIN_REFERENCE_RAY = 10.0;

        // Có chọn 2 view sau khi chạy xong hay không.
        // true  = chọn 2 view để bạn kéo thủ công
        // false = không chọn
        private const bool SELECT_VIEWS_AFTER_RUN = true;

        // AUTO ARRANGE VIEW - TEST V4
        // Chỉ move Drawing View, không đụng model 3D.
        private const bool AUTO_ARRANGE_VIEW_GAP = true;
        private const double VIEW_VERTICAL_GAP_AFTER_RUN = 15.0;

        // AUTO SCALE THEO CHIỀU DÀI THANH + KHỔ GIẤY
        // Scale chạy trước DIM. Không đo DIM thật, chỉ dự phòng 200mm cho DIM dọc.
        // A3: trừ lề 20mm. A1: trừ lề 30mm. Scale cho phép: 1:5, 1:10, 1:15, 1:20, 1:30.
        private const bool AUTO_SCALE_BY_PART_LENGTH = true;
        private const double AUTO_SCALE_DIM_VERTICAL_RESERVE = 50.0;
        private const double AUTO_SCALE_A3_MARGIN_TOTAL = 20.0;
        private const double AUTO_SCALE_A1_MARGIN_TOTAL = 30.0;
        private const double AUTO_SCALE_DEFAULT_MARGIN_TOTAL = 20.0;
        private const double A3_SHEET_WIDTH = 420.0;
        private const double A3_SHEET_HEIGHT = 297.0;
        private const double A1_SHEET_WIDTH = 841.0;
        private const double A1_SHEET_HEIGHT = 594.0;
        private const double SHEET_SIZE_TOLERANCE = 2.0;

        // AUTO PART MARK NAME V3
        // Chỉ xử lý mark tên thanh. Không đụng mark lỗ.
        private const bool AUTO_MOVE_PART_MARK_NAME = true;

        // Khoảng hở từ mép trên thanh đến đáy khung mark tên.
        private const double PART_MARK_GAP_FROM_PLATE = 15.0;

        // Nếu có nhiều part mark thì lệch nhẹ để tránh chồng nhau.
        private const double PART_MARK_STAGGER = 18.0;

        // Nếu chiều rộng miếng nhỏ hơn giá trị này thì mark tên đặt phía dưới.
        // Mục tiêu: tránh mark tên nằm phía trên làm chật/đè DIM với các miếng hẹp.
        private const double PART_MARK_BELOW_IF_WIDTH_LESS_THAN = 180.0;

        // AUTO HOLE MARK AESTHETIC LEADER + COLLISION AVOIDANCE.
        // Tất cả khoảng hở dưới đây là paper-space; khi chạy sẽ nhân với view scale.
        private const bool AUTO_ARRANGE_HOLE_MARKS_AESTHETIC = true;
        private const double HOLE_MARK_BOUNDARY_GAP_PAPER = 2.0;
        private const double HOLE_MARK_SEARCH_STEP_PAPER = 2.0;
        private const double HOLE_MARK_MAX_EXTRA_SEARCH_PAPER = 40.0;
        private const double HOLE_MARK_DIM_CLEARANCE_PAPER = 1.5;
        private const double HOLE_MARK_OTHER_MARK_CLEARANCE_PAPER = 2.0;
        private const double HOLE_MARK_ANCHOR_MATCH_PAPER = 4.0;
        private const double HOLE_MARK_OTHER_HOLE_CLEARANCE_PAPER = 1.5;
        private const double HOLE_MARK_TARGET_ANGLE_DEG = 45.0;
        private const double HOLE_MARK_IDEAL_ANGLE_TOL_DEG = 7.5;
        private const double HOLE_MARK_PREFERRED_ANGLE_TOL_DEG = 15.0;
        private const double HOLE_MARK_OBLIQUE_ANGLE_TOL_DEG = 25.0;
        private const double HOLE_MARK_ANGLE_PENALTY_PAPER_PER_DEG = 1.25;
        private const double HOLE_MARK_DIM_CONFLICT_PENALTY_PAPER = 10.0;

        #endregion

        #region 01 - MAIN RUN FLOW
        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
            CurrentDimTierUnit = NORMAL_NO_CHAMFER_DIM_OFFSET;

            DrawingHandler dh = new DrawingHandler();
            Drawing drawing = dh.GetActiveDrawing();

            if (drawing == null)
            {
                return;
            }

            SinglePartDrawing spDrawing = drawing as SinglePartDrawing;
            if (spDrawing == null)
            {
                return;
            }

            Model model = new Model();
            if (!model.GetConnectionStatus())
            {
                return;
            }

            ModelObject mo = model.SelectModelObject(spDrawing.PartIdentifier);
            ModelPart part = mo as ModelPart;

            if (part == null)
            {
                return;
            }

            double thickness = GetPlateThickness(part);

            // LẤY DANH SÁCH VIEW 1 LẦN, RỒI CHẠY TỪNG BƯỚC RIÊNG BIỆT.
            // Các bước scale, tạo DIM, resize và move view được xử lý tuần tự.
            List<View> processedViews = GetMainPartViews(drawing, spDrawing);

            // Giữ tham chiếu semantic ngay từ đầu giống flow Shape H.
            // Không nhận TOP/FRONT bằng chiều cao khung xanh vì DIM/mark có thể làm
            // khung đổi kích thước và khiến hai view bị đảo vai trò khi chạy GAP.
            View topViewForArrange = FindSinglePlateViewByViewType(
                processedViews,
                "TopView",
                "Top"
            );
            View frontViewForArrange = FindSinglePlateViewByViewType(
                processedViews,
                "FrontView",
                "Front"
            );

            // BƯỚC 1: Xóa DIM cũ trước, commit cho Tekla xử lý xong.
            DeleteAllDimensions(drawing);
            SafeCommitAndWait(drawing, 250);

            // BƯỚC 2: Scale theo chiều dài thanh + khổ giấy, chạy trước DIM.
            // Không đo DIM thật; chỉ cộng dự phòng 200mm cho DIM dọc.
            if (AUTO_SCALE_BY_PART_LENGTH)
            {
                foreach (View view in processedViews)
                {
                    if (view == null)
                        continue;
                    ApplyAutoScaleByPartLength(model, drawing, part, view);
                }

                SafeCommitAndWait(drawing, 350);
            }

            InitializeCurrentDimTierSpacing(processedViews);

            // BƯỚC 3: Tạo DIM + move mark.
            // LƯU Ý: Không tự set RestrictionBox thủ công nữa để tránh văng khung tím/cut area.
            int created = CreateDimsBySectionPolygon(
                model,
                drawing,
                spDrawing,
                part,
                thickness,
                processedViews
            );

            SafeCommitAndWait(drawing, 80);

            // BƯỚC 3B: Tự động sửa Bolt Mark lỗi tiếng Nhật sang HOLE mark chuẩn.
            AutoFixBadJapaneseBoltMarks(drawing);
            SafeCommitAndWait(drawing, 80);

            // BƯỚC 3C: Chọn vị trí hole mark theo phân tầng thẩm mỹ gần 45°,
            // sau đó mới tối ưu chiều dài. Hộp chữ không được đè DIM/mark khác;
            // leader chạm DIM chỉ là fallback có phạt trong bản vẽ chật.
            if (AUTO_ARRANGE_HOLE_MARKS_AESTHETIC)
            {
                AutoArrangeHoleMarksAesthetic(model, drawing, part, processedViews);
                SafeCommitAndWait(drawing, 120);
            }

            // BƯỚC 4: Auto arrange bằng KHUNG XANH sau khi Tekla đã update DIM/mark.
            // Khung xanh giữ gap có tính cả DIM, tránh chồng dim lên mặt view.
            if (AUTO_ARRANGE_VIEW_GAP)
            {
                ArrangeProcessedViewsVerticalGap(
                    topViewForArrange,
                    frontViewForArrange,
                    VIEW_VERTICAL_GAP_AFTER_RUN
                );
                SafeCommitAndWait(drawing, 250);
            }

            // BƯỚC 5: Gom cụm view vào giữa vùng giấy hữu dụng bằng KHUNG TÍM RestrictionBox.
            // Chỉ tính các view đã xử lý, không tính khung tên / bảng vật tư / object khác trên sheet.
            CenterProcessedViewsBySheetSize(drawing, processedViews);
            SafeCommitAndWait(drawing, 250);

            // BƯỚC 6: Sau khi move về giữa, arrange lại bằng KHUNG XANH để lấy lại gap 15 có tính cả DIM.
            // Bổ sung arrange cuối kiểu ÉP CƯỠNG BỨC và CHIA ĐỀU 2 VIEW.
            // Chỉ chỉnh vị trí view theo Y để gap TOP/FRONT = 15, không center lại, không đụng DIM/mark/scale.
            if (AUTO_ARRANGE_VIEW_GAP)
            {
                ForceFinalEqualArrangeTopFrontGap15(
                    topViewForArrange,
                    frontViewForArrange,
                    VIEW_VERTICAL_GAP_AFTER_RUN
                );
                SafeCommitAndWait(drawing, 250);
            }

            // BƯỚC 7: Cập nhật Title 3 theo scale view cuối cùng.
            // Chỉ ghi giá trị hiển thị scale, không đụng DIM / view / model.
            UpdateDrawingTitle3ScaleFromViews(drawing, processedViews);
            SafeCommitAndWait(drawing, 150);

            // BƯỚC 8 - MUTATION CUỐI CÙNG: mọi move view và Update Title 3 phía
            // trên đều có thể làm Tekla regenerate LeaderLine mark. Vì vậy phải
            // đặt lại MARK lỗ sau toàn bộ các Commit khác, rồi không được commit
            // bất kỳ thay đổi drawing nào nữa trong Run().
            if (AUTO_ARRANGE_HOLE_MARKS_AESTHETIC)
            {
                AutoArrangeHoleMarksAesthetic(model, drawing, part, processedViews);
                SafeCommitAndWait(drawing, 120);
            }

            if (SELECT_VIEWS_AFTER_RUN)
            {
                SelectProcessedViews(dh, processedViews);
            }
        }

        private static void SafeCommitAndWait(Drawing drawing, int milliseconds)
        {
            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch { }

            try
            {
                if (milliseconds > 0)
                    System.Threading.Thread.Sleep(milliseconds);
            }
            catch { }
        }

        #endregion

        #region 02 - AUTO DIM MAIN FLOW
        private static List<View> GetMainPartViews(Drawing drawing, SinglePartDrawing spDrawing)
        {
            List<View> result = new List<View>();

            try
            {
                if (drawing == null || spDrawing == null)
                    return result;

                ContainerView sheet = drawing.GetSheet();
                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null)
                        continue;

                    if (!ViewContainsMainPart(view, spDrawing))
                        continue;

                    if (!result.Contains(view))
                        result.Add(view);
                }
            }
            catch { }

            return result;
        }

        private static bool IsSectionView(View view)
        {
            try
            {
                if (view == null)
                    return false;

                return IsSectionViewTypeText(view.ViewType.ToString());
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSectionViewTypeText(string viewType)
        {
            if (String.IsNullOrEmpty(viewType))
                return false;

            return String.Equals(viewType, "SectionView", StringComparison.OrdinalIgnoreCase)
                || viewType.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static View FindSinglePlateViewByViewType(
            List<View> views,
            string exactViewTypeName,
            string fallbackText
        )
        {
            try
            {
                if (views == null)
                    return null;

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    string viewTypeText = "";
                    try
                    {
                        viewTypeText = view.ViewType.ToString();
                    }
                    catch
                    {
                        viewTypeText = "";
                    }

                    if (
                        SinglePlateViewTypeMatchesForArrange(
                            viewTypeText,
                            exactViewTypeName,
                            fallbackText
                        )
                    )
                        return view;
                }
            }
            catch { }

            return null;
        }

        private static bool SinglePlateViewTypeMatchesForArrange(
            string viewTypeText,
            string exactViewTypeName,
            string fallbackText
        )
        {
            if (String.IsNullOrEmpty(viewTypeText))
                return false;

            if (
                !String.IsNullOrEmpty(exactViewTypeName)
                && String.Equals(
                    viewTypeText,
                    exactViewTypeName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;

            // Fallback mềm giống Shape H: vẫn chỉ đọc ViewType, tuyệt đối không
            // quay lại đoán bằng chiều cao khung hoặc vị trí Y hiện tại.
            return !String.IsNullOrEmpty(fallbackText)
                && viewTypeText.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double GetPlateThickness(ModelPart part)
        {
            string profile = "";
            part.GetReportProperty("PROFILE", ref profile);

            string p = profile.ToUpper().Replace("PL", "").Replace(" ", "").Replace(",", ".");
            string[] tokens = p.Split(
                new char[] { '*', 'X', '-' },
                StringSplitOptions.RemoveEmptyEntries
            );

            double min = 999999.0;

            foreach (string token in tokens)
            {
                double value;
                if (
                    double.TryParse(
                        token,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value
                    )
                )
                {
                    if (value > 0 && value < min)
                        min = value;
                }
            }

            if (min < 999999.0)
                return min;

            return 0.0;
        }

        private static bool ShouldCreatePlateFaceDetailDimensions(double realThickness)
        {
            // PROFILE không đọc được trả về 0; mặc định an toàn là chỉ DIM đường bao.
            return realThickness > PLATE_FACE_DETAIL_DIM_THICKNESS_THRESHOLD;
        }

        private static bool ShouldCreateContourChamferDimensions(
            double realThickness,
            bool sectionThicknessAcrossX
        )
        {
            // Vát trên mặt tấm tuân theo ngưỡng T. Vát theo chiều dày ở A-A là
            // một feature gia công khác và vẫn phải DIM dù plate mỏng.
            return AUTO_DIM_CHAMFER
                && (
                    sectionThicknessAcrossX
                    || ShouldCreatePlateFaceDetailDimensions(realThickness)
                );
        }

        private static bool IsPlateThicknessProjectionView(
            double projectedSpanY,
            double projectedDepthZ,
            double realThickness
        )
        {
            if (
                realThickness <= 0.0
                || projectedSpanY <= 0.0
                || projectedDepthZ <= 0.0
            )
            {
                return false;
            }

            double yThicknessError = Math.Abs(projectedSpanY - realThickness);
            double zThicknessError = Math.Abs(projectedDepthZ - realThickness);

            // Trong hệ tọa độ display của view:
            // - view độ dày thật: span Y bám theo T, còn chiều mặt tấm nằm ở depth Z;
            // - view mặt tấm: depth Z mới bám theo T.
            // Điều kiện cũ "height <= 25" có thể nhận nhầm một tấm mặt hẹp là view
            // độ dày và làm DIM chamfer dù T < 16. Trường hợp hai hướng cùng mơ hồ
            // được fail-closed về nhánh mặt tấm để chỉ giữ DIM đường bao.
            return yThicknessError <= PLATE_THICKNESS_PROJECTION_SPAN_TOL
                && zThicknessError
                    > yThicknessError + PLATE_EDGE_FOOT_ALIGNMENT_TOL;
        }

        private static void DeleteAllDimensions(Drawing drawing)
        {
            // SAFE FIX:
            // Không quét toàn bộ object bằng GetAllObjects() nữa.
            // Sau khi chạy Shape, một số mark/leader object có thể làm Tekla lỗi deserialize
            // LeaderLinePlacing khi enumerator quét tất cả object.
            // Chỉ quét đúng các loại Dimension cần xóa để tránh đụng mark/leader.
            try
            {
                if (drawing == null)
                    return;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return;

                DeleteDrawingObjectsByTypeSafe(sheet, typeof(StraightDimensionSet));
                DeleteDrawingObjectsByTypeSafe(sheet, typeof(StraightDimension));
                DeleteDrawingObjectsByTypeSafe(sheet, typeof(CurvedDimensionSetRadial));
                DeleteDrawingObjectsByTypeSafe(sheet, typeof(CurvedDimensionSetOrthogonal));
                DeleteDrawingObjectsByTypeSafe(sheet, typeof(RadiusDimension));
                DeleteDrawingObjectsByTypeSafe(sheet, typeof(AngleDimension));
            }
            catch { }
        }

        private static void DeleteDrawingObjectsByTypeSafe(ContainerView sheet, Type objectType)
        {
            try
            {
                if (sheet == null || objectType == null)
                    return;

                DrawingObjectEnumerator objects = null;

                try
                {
                    objects = sheet.GetAllObjects(objectType);
                }
                catch
                {
                    return;
                }

                if (objects == null)
                    return;

                while (true)
                {
                    bool moved = false;

                    try
                    {
                        moved = objects.MoveNext();
                    }
                    catch
                    {
                        break;
                    }

                    if (!moved)
                        break;

                    DrawingObject obj = null;

                    try
                    {
                        obj = objects.Current as DrawingObject;
                    }
                    catch
                    {
                        obj = null;
                    }

                    if (obj == null)
                        continue;

                    try
                    {
                        obj.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static int CreateDimsBySectionPolygon(
            Model model,
            Drawing drawing,
            SinglePartDrawing spDrawing,
            ModelPart part,
            double thickness,
            List<View> processedViews
        )
        {
            int count = 0;

            ContainerView sheet = drawing.GetSheet();
            DrawingObjectEnumerator views = sheet.GetAllViews();

            while (views.MoveNext())
            {
                View view = views.Current as View;
                if (view == null)
                    continue;

                if (!ViewContainsMainPart(view, spDrawing))
                    continue;

                // Load attribute và scale đã được chạy riêng ở Run(), có Commit + Wait từng bước.
                // Không chạy lại ở đây để tránh Tekla regenerate view nhiều lần trong cùng một nhịp.
                count += CreateDimsInOneView(model, drawing, part, view, thickness);

                if (!processedViews.Contains(view))
                    processedViews.Add(view);
            }

            return count;
        }

        private static bool ViewContainsMainPart(View view, SinglePartDrawing spDrawing)
        {
            DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));

            while (parts.MoveNext())
            {
                DrawingPart dp = parts.Current as DrawingPart;
                if (dp == null)
                    continue;

                if (dp.ModelIdentifier.ID == spDrawing.PartIdentifier.ID)
                    return true;
            }

            return false;
        }

        private static int CreateDimsInOneView(
            Model model,
            Drawing drawing,
            ModelPart part,
            View view,
            double realThickness
        )
        {
            int count = 0;
            bool createPlateFaceDetailDimensions =
                ShouldCreatePlateFaceDetailDimensions(realThickness);

            TransformationPlane oldPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();

            try
            {
                TransformationPlane viewPlane = new TransformationPlane(
                    view.DisplayCoordinateSystem
                );

                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(viewPlane);

                Solid solid = part.GetSolid();

                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;

                double solidHeight = Math.Abs(max.Y - min.Y);
                double solidDepth = Math.Abs(max.Z - min.Z);

                bool thicknessProjectionView = IsPlateThicknessProjectionView(
                    solidHeight,
                    solidDepth,
                    realThickness
                );

                StraightDimensionSetHandler handler = new StraightDimensionSetHandler();

                if (thicknessProjectionView)
                {
                    // Không dựng chân DIM từ MinimumPoint/MaximumPoint: với plate vát,
                    // bốn tổ hợp (minX,minY)... là góc của bounding box và có thể không
                    // hề thuộc plate. Silhouette dưới đây chỉ gồm vertex thật của Solid.
                    List<List<Point>> thinBoundaries = GetThinViewIntersectionPolygons(
                        solid,
                        min,
                        max
                    );
                    List<Point> thinOuterContour = BuildExactProjectedOuterContour(
                        solid,
                        thinBoundaries
                    );

                    // Intersect ở giữa Z có thể không đi qua phần vát cục bộ. Đưa
                    // silhouette thật vào đầu danh sách để bộ nhận diện angle luôn thấy
                    // cạnh vát không xuyên hết độ dày.
                    if (thinOuterContour != null && thinOuterContour.Count >= 3)
                        thinBoundaries.Insert(0, thinOuterContour);

                    count += CreateThinViewExactPlateDims(handler, view, thinOuterContour);

                    double thinRadiusThickness = GetThinRadiusReferenceThickness(
                        part,
                        realThickness
                    );

                    if (
                        (AUTO_DIM_THIN_VIEW_FILLET_RADIUS || AUTO_DIM_THIN_VIEW_CHAMFER_ANGLE)
                        && thinRadiusThickness > 0.0
                        && IsPlateThicknessProjectionView(
                            solidHeight,
                            solidDepth,
                            thinRadiusThickness
                        )
                    )
                    {
                        count += CreateThinViewBoundaryFeatureDims(
                            view,
                            solid,
                            thinBoundaries,
                            min,
                            max
                        );
                    }

                    ResizeViewBoundary(view, min, max);

                    return count;
                }

                double midZ = (min.Z + max.Z) / 2.0;

                Point planeP1 = new Point(min.X - 1000, min.Y - 1000, midZ);
                Point planeP2 = new Point(max.X + 1000, min.Y - 1000, midZ);
                Point planeP3 = new Point(min.X - 1000, max.Y + 1000, midZ);

                List<Point> polygon = GetLargestIntersectionPolygon(
                    solid.IntersectAllFaces(planeP1, planeP2, planeP3)
                );

                if (polygon.Count < 2)
                {
                    ResizeViewBoundary(view, min, max);
                    return count;
                }

                double minX,
                    maxX,
                    minY,
                    maxY;
                GetMinMax(polygon, out minX, out maxX, out minY, out maxY);

                // TOTAL DIM VIEW PROJECTION - V20:
                // Chỉ đổi NGUỒN BIÊN cho DIM tổng + view boundary.
                // Không dùng polygon mặt cắt giữa chiều dày để tính DIM tổng nữa,
                // vì tấm bevel/vát xuyên chiều dày có thể làm lát cắt giữa bị hụt kích thước.
                //
                // KHÔNG đổi thuật toán chân DIM:
                // - vẫn dùng GetStraightVerticalEdgePointForTotalDim()
                // - vẫn fallback HighestPointNearX()
                // - vẫn dùng GetStraightHorizontalEdgePointForTotalDim()
                // - vẫn fallback LeftMostPointNearY()
                //
                // Các thuật toán khác như DIM lỗ, chamfer, mark vẫn dùng polygon cũ bên dưới.
                List<Point> totalPolygon = GetProjectedSolidPointsForTotalDims(solid);

                if (totalPolygon == null || totalPolygon.Count < 2)
                    totalPolygon = polygon;

                // Outer contour phải được tạo từ điểm Solid thật. Convex hull loại các
                // vòng lỗ và vertex mặt trong nhưng vẫn giữ nguyên endpoint thật của
                // cạnh vát; không sinh bốn góc ảo của bounding box.
                List<Point> exactOuterContour = BuildConvexHull2D(totalPolygon);
                if (exactOuterContour == null || exactOuterContour.Count < 3)
                    exactOuterContour = polygon;

                double totalMinX,
                    totalMaxX,
                    totalMinY,
                    totalMaxY;
                GetMinMax(
                    exactOuterContour,
                    out totalMinX,
                    out totalMaxX,
                    out totalMinY,
                    out totalMaxY
                );

                // FIX BO GÓC / RADIUS:
                // Giữ nguyên logic tầng DIM và vị trí DIM tổng như cũ,
                // nhưng nguồn điểm cho DIM tổng đã là projected solid theo hệ tọa độ view.
                Point leftForLength = GetStraightVerticalEdgePointForTotalDim(
                    exactOuterContour,
                    totalMinX,
                    true
                );
                Point rightForLength = GetStraightVerticalEdgePointForTotalDim(
                    exactOuterContour,
                    totalMaxX,
                    true
                );

                if (leftForLength == null)
                    leftForLength = HighestPointNearX(exactOuterContour, totalMinX);

                if (rightForLength == null)
                    rightForLength = HighestPointNearX(exactOuterContour, totalMaxX);

                // FIX BO GÓC / RADIUS:
                // DIM dọc tổng vẫn giữ cách đặt cũ ở phía trái,
                // nhưng nguồn điểm cho DIM tổng đã là projected solid theo hệ tọa độ view.
                Point bottomForHeight = GetStraightHorizontalEdgePointForTotalDim(
                    exactOuterContour,
                    totalMinY,
                    true
                );
                Point topForHeight = GetStraightHorizontalEdgePointForTotalDim(
                    exactOuterContour,
                    totalMaxY,
                    true
                );

                if (bottomForHeight == null)
                    bottomForHeight = LeftMostPointNearY(exactOuterContour, totalMinY);

                if (topForHeight == null)
                    topForHeight = LeftMostPointNearY(exactOuterContour, totalMaxY);

                // CLEAN PA6 - TẦNG DIM ĐỘC LẬP 4 HƯỚNG:
                // Không đổi thuật toán tạo chân DIM tổng.
                // Mỗi hướng Top / Bottom / Left / Right có bộ tầng riêng.
                // Một DIM chiếm một tầng; DIM tổng tự ra tầng ngoài cùng của hướng đó.
                bool chamferTop;
                bool chamferBottom;
                bool chamferLeft;
                bool chamferRight;
                GetChamferInfluenceSides(
                    exactOuterContour,
                    totalMinX,
                    totalMaxX,
                    totalMinY,
                    totalMaxY,
                    out chamferTop,
                    out chamferBottom,
                    out chamferLeft,
                    out chamferRight
                );

                bool sectionThicknessAcrossX = IsSectionThicknessAcrossX(
                    IsSectionView(view),
                    Math.Abs(totalMaxX - totalMinX),
                    Math.Abs(totalMaxY - totalMinY),
                    realThickness
                );
                bool createContourChamferDimensions =
                    ShouldCreateContourChamferDimensions(
                        realThickness,
                        sectionThicknessAcrossX
                    );

                // A-A đứng: DIM ngang phía trên chính là bề dày plate. Nếu cạnh trên
                // có vát, không được ghi bề dày tổng qua hai mép ảo; thay bằng phần
                // thẳng còn lại giữa các vát. Trường hợp cạnh trên thẳng giữ nguyên.
                bool replaceSectionTotalThicknessWithLand =
                    sectionThicknessAcrossX && chamferTop;

                int topTier = 1;
                int bottomTier = 1;
                int leftTier = 1;
                int rightTier = 1;

                // Chamfer/rãnh ngoài nếu có thì chiếm tầng đầu của đúng hướng đó.
                // Chỉ dùng để quản lý tầng; không bù offset theo chamfer.
                if (createContourChamferDimensions)
                {
                    if (chamferTop)
                        topTier++;
                    if (chamferBottom)
                        bottomTier++;
                    if (chamferLeft)
                        leftTier++;
                    if (chamferRight)
                        rightTier++;
                }

                bool hasHoleDims = false;
                int holeLeftDimCount = 0;
                int holeRightDimCount = 0;
                List<List<PlateHoleCandidate>> holeFamiliesForView =
                    new List<List<PlateHoleCandidate>>();

                if (createPlateFaceDetailDimensions)
                {
                    holeFamiliesForView = GetPlateHoleFamilies(
                        part,
                        totalMinX,
                        totalMaxX,
                        totalMinY,
                        totalMaxY
                    );
                    if (holeFamiliesForView.Count > 0)
                    {
                        hasHoleDims = true;
                        GetPlateHoleExternalClusterCounts(
                            holeFamiliesForView,
                            totalMinX,
                            totalMaxX,
                            out holeLeftDimCount,
                            out holeRightDimCount
                        );
                    }
                }

                double holeBottomOffset = 0.0;
                double holeLeftOffset = 0.0;
                double holeRightOffset = 0.0;

                if (hasHoleDims)
                {
                    // DIM ngang lỗ đặt phía dưới.
                    holeBottomOffset = GetCleanDimOffsetByTier(bottomTier);
                    bottomTier++;

                    if (holeLeftDimCount > 0)
                    {
                        holeLeftOffset = GetCleanDimOffsetByTier(leftTier);
                        leftTier += holeLeftDimCount;
                    }

                    if (holeRightDimCount > 0)
                    {
                        holeRightOffset = GetCleanDimOffsetByTier(rightTier);
                        rightTier += holeRightDimCount;
                    }
                }

                // DIM tổng luôn là tầng ngoài cùng của hướng đang đặt.
                double totalHorizontalTierOffset = GetCleanDimOffsetByTier(topTier);
                topTier++;

                double totalVerticalTierOffset = GetCleanDimOffsetByTier(leftTier);
                leftTier++;

                double totalHorizontalDimOffset = GetTotalTopDistanceByTotalFeetAnchor(
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight,
                    totalHorizontalTierOffset
                );

                double totalVerticalDimOffset = GetTotalLeftDistanceByTotalFeetAnchor(
                    bottomForHeight,
                    topForHeight,
                    leftForLength,
                    rightForLength,
                    totalVerticalTierOffset
                );

                if (replaceSectionTotalThicknessWithLand)
                {
                    Point sectionLandLeft;
                    Point sectionLandRight;
                    if (
                        TryFindStraightHorizontalLandOnOuterSide(
                            exactOuterContour,
                            true,
                            out sectionLandLeft,
                            out sectionLandRight
                        )
                    )
                    {
                        PointList sectionLandPoints = new PointList();
                        sectionLandPoints.Add(sectionLandLeft);
                        sectionLandPoints.Add(sectionLandRight);

                        double sectionLandOffset = GetDistanceFromFirstFootToAnchorTarget(
                            sectionLandPoints,
                            new Vector(0, 1, 0),
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight,
                            totalHorizontalTierOffset
                        );

                        if (
                            CreateDim(
                                handler,
                                view,
                                sectionLandLeft,
                                sectionLandRight,
                                new Vector(0, 1, 0),
                                sectionLandOffset
                            )
                        )
                        {
                            count++;
                        }
                    }
                }
                else if (
                    CreateDim(
                        handler,
                        view,
                        leftForLength,
                        rightForLength,
                        new Vector(0, 1, 0),
                        totalHorizontalDimOffset
                    )
                )
                {
                    count++;
                }

                if (
                    CreateDim(
                        handler,
                        view,
                        bottomForHeight,
                        topForHeight,
                        new Vector(-1, 0, 0),
                        totalVerticalDimOffset
                    )
                )
                    count++;

                if (createPlateFaceDetailDimensions)
                {
                    count += CreateHoleCenterDims(
                        handler,
                        view,
                        holeFamiliesForView,
                        exactOuterContour,
                        totalMinX,
                        totalMaxX,
                        totalMinY,
                        totalMaxY,
                        holeBottomOffset,
                        holeLeftOffset,
                        holeRightOffset,
                        leftForLength,
                        rightForLength,
                        bottomForHeight,
                        topForHeight
                    );
                }

                // DIM hai thành phần X/Y của cạnh vát từ chính hai endpoint thật.
                if (createContourChamferDimensions)
                {
                    count += CreateChamferDims(
                        handler,
                        view,
                        exactOuterContour,
                        totalMinX,
                        totalMaxX,
                        totalMinY,
                        totalMaxY
                    );

                    if (sectionThicknessAcrossX)
                    {
                        count += CreateSectionThicknessChamferAngleDims(
                            view,
                            exactOuterContour,
                            totalMinX,
                            totalMaxX,
                            totalMinY,
                            totalMaxY
                        );
                    }
                }

                if (AUTO_MOVE_PART_MARK_NAME)
                {
                    AutoMovePartMarkNameV3(
                        drawing,
                        view,
                        part,
                        totalMinX,
                        totalMaxX,
                        totalMinY,
                        totalMaxY
                    );
                }

                // View boundary cũng dùng bao ngoài projected solid để không bị hụt khi tấm vát theo chiều dày.
                Point boundaryMin = new Point(totalMinX, totalMinY, min.Z);
                Point boundaryMax = new Point(totalMaxX, totalMaxY, max.Z);
                ResizeViewBoundary(view, boundaryMin, boundaryMax);
            }
            catch { }
            finally
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }

            return count;
        }

        #endregion

        #region 03 - VIEW BOUNDARY SAFETY
        private static void ResizeViewBoundary(View view, Point min, Point max)
        {
            // BẢN SAFE: vẫn mở khung tím + VIEW_PADDING, nhưng không load/cut theo tiêu chuẩn.
            // Chỉ set RestrictionBox khi box hợp lệ, depth hợp lệ, kích thước không bất thường.
            try
            {
                if (view == null || min == null || max == null)
                    return;

                if (!IsValidBoundaryBox(min, max))
                    return;

                AABB oldBox = null;
                Point oldMin = null;
                Point oldMax = null;

                try
                {
                    oldBox = view.RestrictionBox;
                    if (oldBox != null && oldBox.MinPoint != null && oldBox.MaxPoint != null)
                    {
                        oldMin = new Point(oldBox.MinPoint.X, oldBox.MinPoint.Y, oldBox.MinPoint.Z);
                        oldMax = new Point(oldBox.MaxPoint.X, oldBox.MaxPoint.Y, oldBox.MaxPoint.Z);
                    }
                }
                catch
                {
                    oldBox = null;
                }

                double zMin = -100.0;
                double zMax = 100.0;

                if (oldMin != null && oldMax != null)
                {
                    zMin = Math.Min(oldMin.Z, oldMax.Z);
                    zMax = Math.Max(oldMin.Z, oldMax.Z);

                    // Nếu depth cũ đang lỗi hoặc quá mỏng thì dùng depth an toàn.
                    if (zMax <= zMin + 5.0)
                    {
                        zMin = -100.0;
                        zMax = 100.0;
                    }
                }

                Point newMin = new Point(min.X - VIEW_PADDING, min.Y - VIEW_PADDING, zMin);
                Point newMax = new Point(max.X + VIEW_PADDING, max.Y + VIEW_PADDING, zMax);

                if (!IsValidBoundaryBox(newMin, newMax))
                    return;

                // Chặn khung tím văng quá lớn bất thường.
                double w = Math.Abs(newMax.X - newMin.X);
                double h = Math.Abs(newMax.Y - newMin.Y);
                if (w > 3000.0 || h > 3000.0)
                    return;

                try
                {
                    view.RestrictionBox = new AABB(newMin, newMax);
                    bool ok = false;

                    try
                    {
                        ok = view.Modify();
                    }
                    catch
                    {
                        ok = false;
                    }

                    // Nếu Tekla không nhận modify, rollback box cũ để tránh hỏng view.
                    if (!ok && oldMin != null && oldMax != null)
                    {
                        try
                        {
                            view.RestrictionBox = new AABB(oldMin, oldMax);
                            view.Modify();
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Rollback nếu set box gây exception.
                    if (oldMin != null && oldMax != null)
                    {
                        try
                        {
                            view.RestrictionBox = new AABB(oldMin, oldMax);
                            view.Modify();
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsValidBoundaryBox(Point min, Point max)
        {
            if (min == null || max == null)
                return false;

            if (
                double.IsNaN(min.X)
                || double.IsNaN(min.Y)
                || double.IsNaN(min.Z)
                || double.IsNaN(max.X)
                || double.IsNaN(max.Y)
                || double.IsNaN(max.Z)
            )
                return false;

            if (
                double.IsInfinity(min.X)
                || double.IsInfinity(min.Y)
                || double.IsInfinity(min.Z)
                || double.IsInfinity(max.X)
                || double.IsInfinity(max.Y)
                || double.IsInfinity(max.Z)
            )
                return false;

            if (max.X <= min.X + 1.0)
                return false;

            if (max.Y <= min.Y + 1.0)
                return false;

            return true;
        }

        #endregion

        #region 04 - CHAMFER DIM LOGIC
        private static int CreateChamferDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            int count = 0;

            try
            {
                if (polygon == null || polygon.Count < 3)
                    return count;

                List<Point> pts = SortPolygonPointsClockwise(polygon);

                if (pts.Count < 3)
                    return count;

                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                // Offset chỉ quyết định tầng hiển thị. Chân DIM luôn giữ nguyên hai
                // endpoint của cạnh vát; không chiếu sang bốn biên bounding box.
                double chamferOffset = GetCleanDimOffsetByTier(CHAMFER_DIM_TIER);

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    double dx = Math.Abs(a.X - b.X);
                    double dy = Math.Abs(a.Y - b.Y);

                    // Chỉ nhận chamfer thật ở góc ngoài plate.
                    // Bỏ qua cung tròn/polycurve bị chia thành nhiều đoạn nhỏ.
                    if (!IsValidChamferSegment(a, b, minX, maxX, minY, maxY))
                        continue;

                    double midX = (a.X + b.X) / 2.0;
                    double midY = (a.Y + b.Y) / 2.0;

                    bool topSide = midY >= centerY;
                    bool rightSide = midX >= centerX;

                    Vector horizontalDimDirection = topSide
                        ? new Vector(0, 1, 0)
                        : new Vector(0, -1, 0);

                    Vector verticalDimDirection = rightSide
                        ? new Vector(1, 0, 0)
                        : new Vector(-1, 0, 0);

                    // Contour có thể chạy thuận hoặc ngược nên a/b không mang ý nghĩa kỹ thuật.
                    // Mỗi trục đo phải chuẩn hóa lại đúng thứ tự mép trong -> mép ngoài.
                    if (dx >= CHAMFER_MIN_SIZE)
                    {
                        Point horizontalInner;
                        Point horizontalOuter;
                        OrderChamferFeetInnerToOuter(
                            a,
                            b,
                            true,
                            rightSide,
                            topSide,
                            out horizontalInner,
                            out horizontalOuter
                        );
                        if (
                            CreateDim(
                                handler,
                                view,
                                horizontalInner,
                                horizontalOuter,
                                horizontalDimDirection,
                                chamferOffset
                            )
                        )
                        {
                            count++;
                        }
                    }

                    if (dy >= CHAMFER_MIN_SIZE)
                    {
                        Point verticalInner;
                        Point verticalOuter;
                        OrderChamferFeetInnerToOuter(
                            a,
                            b,
                            false,
                            rightSide,
                            topSide,
                            out verticalInner,
                            out verticalOuter
                        );
                        if (
                            CreateDim(
                                handler,
                                view,
                                verticalInner,
                                verticalOuter,
                                verticalDimDirection,
                                chamferOffset
                            )
                        )
                        {
                            count++;
                        }
                    }
                }
            }
            catch { }

            return count;
        }

        private static void OrderChamferFeetInnerToOuter(
            Point first,
            Point second,
            bool horizontalMeasurement,
            bool rightSide,
            bool topSide,
            out Point inner,
            out Point outer
        )
        {
            inner = first;
            outer = second;
            if (first == null || second == null)
                return;

            bool firstIsInner;
            if (horizontalMeasurement)
            {
                // Bên trái: X lớn hơn là mép trong; bên phải: X nhỏ hơn là mép trong.
                firstIsInner = rightSide ? first.X <= second.X : first.X >= second.X;
            }
            else
            {
                // Phía trên: Y nhỏ hơn là mép trong; phía dưới: Y lớn hơn là mép trong.
                // Với vát không hết độ dày, đây chính là chân trên/dưới của đoạn land đứng.
                firstIsInner = topSide ? first.Y <= second.Y : first.Y >= second.Y;
            }

            if (!firstIsInner)
            {
                inner = second;
                outer = first;
            }
        }

        private static bool IsValidChamferSegment(
            Point a,
            Point b,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            if (a == null || b == null)
                return false;

            double dx = Math.Abs(a.X - b.X);
            double dy = Math.Abs(a.Y - b.Y);

            // Phải là cạnh xiên nhỏ.
            if (
                dx < CHAMFER_MIN_SIZE
                || dy < CHAMFER_MIN_SIZE
                || dx >= CHAMFER_MAX_SIZE
                || dy >= CHAMFER_MAX_SIZE
            )
                return false;

            // Lọc cung tròn/polycurve:
            // Đoạn cung thường rất bẹt, tỷ lệ dx/dy quá lớn hoặc quá nhỏ.
            double ratio = dx / dy;

            if (ratio < CHAMFER_MIN_RATIO || ratio > CHAMFER_MAX_RATIO)
                return false;

            double midX = (a.X + b.X) / 2.0;
            double midY = (a.Y + b.Y) / 2.0;

            bool nearLeft = Math.Abs(midX - minX) <= CHAMFER_MAX_SIZE;
            bool nearRight = Math.Abs(midX - maxX) <= CHAMFER_MAX_SIZE;
            bool nearBottom = Math.Abs(midY - minY) <= CHAMFER_MAX_SIZE;
            bool nearTop = Math.Abs(midY - maxY) <= CHAMFER_MAX_SIZE;

            // Chamfer thật phải nằm gần góc ngoài:
            // vừa gần trái/phải, vừa gần trên/dưới.
            if (!(nearLeft || nearRight))
                return false;

            if (!(nearBottom || nearTop))
                return false;

            return true;
        }

        private static void GetChamferInfluenceSides(
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out bool top,
            out bool bottom,
            out bool left,
            out bool right
        )
        {
            top = false;
            bottom = false;
            left = false;
            right = false;

            try
            {
                if (polygon == null || polygon.Count < 3)
                    return;

                List<Point> pts = SortPolygonPointsClockwise(polygon);
                if (pts == null || pts.Count < 3)
                    return;

                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (!IsValidChamferSegment(a, b, minX, maxX, minY, maxY))
                        continue;

                    double midX = (a.X + b.X) / 2.0;
                    double midY = (a.Y + b.Y) / 2.0;

                    if (midY >= centerY)
                        top = true;
                    else
                        bottom = true;

                    if (midX >= centerX)
                        right = true;
                    else
                        left = true;
                }
            }
            catch { }
        }

        private static bool IsSectionThicknessAcrossX(
            bool isSectionView,
            double spanX,
            double spanY,
            double realThickness
        )
        {
            if (
                !isSectionView
                || realThickness <= 0.0
                || spanX <= 0.0
                || spanY <= 0.0
            )
            {
                return false;
            }

            // Chỉ nhận Section đứng, nơi X là bề dày và Y là chiều dài plate.
            // Nhờ vậy view mặt chính có chamfer góc vẫn giữ DIM tổng chiều rộng cũ.
            return Math.Abs(spanX - realThickness) <= SECTION_THICKNESS_SPAN_TOL
                && spanY > spanX + PLATE_EDGE_FOOT_ALIGNMENT_TOL;
        }

        private static bool TryFindStraightHorizontalLandOnOuterSide(
            List<Point> polygon,
            bool topSide,
            out Point left,
            out Point right
        )
        {
            left = null;
            right = null;

            if (polygon == null || polygon.Count < 2)
                return false;

            List<Point> points = SortPolygonPointsClockwise(polygon);
            if (points == null || points.Count < 2)
                return false;

            double minX;
            double maxX;
            double minY;
            double maxY;
            GetMinMax(points, out minX, out maxX, out minY, out maxY);
            double targetY = topSide ? maxY : minY;
            double bestLength = -1.0;
            double bestCenterX = 0.0;

            for (int i = 0; i < points.Count; i++)
            {
                Point a = points[i];
                Point b = points[(i + 1) % points.Count];
                if (a == null || b == null)
                    continue;

                if (
                    Math.Abs(a.Y - b.Y) > PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    || Math.Abs(a.Y - targetY) > PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    || Math.Abs(b.Y - targetY) > PLATE_EDGE_FOOT_ALIGNMENT_TOL
                )
                {
                    continue;
                }

                double length = Math.Abs(a.X - b.X);
                if (length < 1.0)
                    continue;

                double centerX = (a.X + b.X) * 0.5;
                bool better = length > bestLength + PLATE_EDGE_FOOT_ALIGNMENT_TOL;
                if (
                    Math.Abs(length - bestLength) <= PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    && (left == null || centerX < bestCenterX)
                )
                {
                    better = true;
                }

                if (!better)
                    continue;

                bestLength = length;
                bestCenterX = centerX;
                Point first = a.X <= b.X ? a : b;
                Point second = a.X <= b.X ? b : a;
                left = new Point(first.X, first.Y, 0);
                right = new Point(second.X, second.Y, 0);
            }

            return left != null && right != null;
        }

        private static int CreateSectionThicknessChamferAngleDims(
            View view,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            int count = 0;

            try
            {
                if (view == null || polygon == null || polygon.Count < 3)
                    return count;

                List<Point> points = SortPolygonPointsClockwise(polygon);
                for (int i = 0; i < points.Count; i++)
                {
                    Point origin;
                    Point point1;
                    Point point2;
                    if (
                        !TryGetSectionThicknessChamferAnglePoints(
                            points[i],
                            points[(i + 1) % points.Count],
                            minX,
                            maxX,
                            minY,
                            maxY,
                            out origin,
                            out point1,
                            out point2
                        )
                    )
                    {
                        continue;
                    }

                    AngleDimensionAttributes attributes = new AngleDimensionAttributes();
                    attributes.Type = AngleTypes.AngleOnSide;
                    attributes.TransparentBackground = false;
                    if (attributes.Text != null)
                    {
                        attributes.Text.TextPlacing = DimensionSetBaseAttributes
                            .DimensionTextPlacings
                            .AboveDimensionLine;
                    }

                    // Approved A-A dùng origin tại đầu trong của chamfer; distance=0
                    // để bán kính cung được quyết định bởi hai tia hình học thật.
                    AngleDimension dimension = new AngleDimension(
                        view,
                        origin,
                        point1,
                        point2,
                        0.0,
                        attributes
                    );

                    if (dimension.Insert())
                        count++;
                }
            }
            catch { }

            return count;
        }

        private static bool TryGetSectionThicknessChamferAnglePoints(
            Point first,
            Point second,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point origin,
            out Point point1,
            out Point point2
        )
        {
            origin = null;
            point1 = null;
            point2 = null;

            if (!IsValidChamferSegment(first, second, minX, maxX, minY, maxY))
                return false;

            double centerX = (minX + maxX) * 0.5;
            double centerY = (minY + maxY) * 0.5;
            bool rightSide = (first.X + second.X) * 0.5 >= centerX;
            bool topSide = (first.Y + second.Y) * 0.5 >= centerY;

            Point inner;
            Point outer;
            OrderChamferFeetInnerToOuter(
                first,
                second,
                true,
                rightSide,
                topSide,
                out inner,
                out outer
            );

            double targetFaceY = topSide ? maxY : minY;
            double targetSideX = rightSide ? maxX : minX;
            if (
                inner == null
                || outer == null
                || Math.Abs(inner.Y - targetFaceY) > THIN_CHAMFER_EDGE_TOL
                || Math.Abs(outer.X - targetSideX) > THIN_CHAMFER_EDGE_TOL
            )
            {
                return false;
            }

            origin = new Point(inner.X, inner.Y, 0);
            Point chamferPoint = new Point(outer.X, outer.Y, 0);
            double rayLength = Math.Max(
                SECTION_CHAMFER_MIN_REFERENCE_RAY,
                Distance2D(inner, outer)
            );
            Point outwardReference = new Point(
                origin.X + (rightSide ? rayLength : -rayLength),
                origin.Y,
                0
            );

            // Thứ tự tia theo đúng DIM A-A được duyệt để luôn lấy góc nhọn:
            // trái = chamfer -> outward, phải = outward -> chamfer.
            if (rightSide)
            {
                point1 = outwardReference;
                point2 = chamferPoint;
            }
            else
            {
                point1 = chamferPoint;
                point2 = outwardReference;
            }

            double cross =
                (point1.X - origin.X) * (point2.Y - origin.Y)
                - (point1.Y - origin.Y) * (point2.X - origin.X);

            return Math.Abs(cross) > 0.000001;
        }

        private static double GetHoleDimOffsetByPolygon(List<Point> polygon)
        {
            // CLEAN PA4:
            // DIM lỗ dùng đúng tầng hiện tại.
            // Không đẩy tầng theo chamfer, notch, chân hụt hay bounding box.
            return NORMAL_NO_CHAMFER_DIM_OFFSET;
        }

        private static double GetTotalDimOffset(List<Point> polygon)
        {
            // CLEAN PA4:
            // DIM tổng dùng đúng tầng hiện tại.
            // Không bù tầng khi gặp chamfer / rãnh / biên dạng hụt.
            return NORMAL_NO_CHAMFER_DIM_OFFSET;
        }

        private static List<Point> SortPolygonPointsClockwise(List<Point> polygon)
        {
            List<Point> result = new List<Point>();

            if (polygon == null)
                return result;

            double cx = 0.0;
            double cy = 0.0;
            int n = 0;

            foreach (Point p in polygon)
            {
                if (p == null)
                    continue;

                cx += p.X;
                cy += p.Y;
                n++;
            }

            if (n == 0)
                return result;

            cx = cx / n;
            cy = cy / n;

            foreach (Point p in polygon)
            {
                if (p != null)
                    result.Add(new Point(p.X, p.Y, p.Z));
            }

            result.Sort(
                delegate(Point p1, Point p2)
                {
                    double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
                    double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);

                    return a1.CompareTo(a2);
                }
            );

            return result;
        }

        #endregion

        #region 05 - HOLE DIM LOGIC

        private sealed class PlateHoleCandidate
        {
            public Point Point;
            public double HoleDiameter;
            public double BoltSize;
            public double SlotX;
            public double SlotY;
            public string HoleType;
        }

        private static List<List<PlateHoleCandidate>> GetPlateHoleFamilies(
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            List<PlateHoleCandidate> candidates = new List<PlateHoleCandidate>();

            try
            {
                if (part == null)
                    return new List<List<PlateHoleCandidate>>();

                ModelObjectEnumerator bolts = part.GetBolts();
                while (bolts.MoveNext())
                {
                    ModelBoltGroup group = bolts.Current as ModelBoltGroup;
                    if (group == null)
                        continue;

                    double holeDiameter = GetBoltGroupPhiForDimGap(group);
                    double boltSize = GetPlateBoltSize(group);
                    double slotX = GetPlateHoleSlotX(group);
                    double slotY = GetPlateHoleSlotY(group);
                    string holeType = GetPlateHoleTypeText(group);

                    foreach (object item in group.BoltPositions)
                    {
                        Point point = item as Point;
                        if (
                            point == null
                            || point.X < minX - 5.0
                            || point.X > maxX + 5.0
                            || point.Y < minY - 5.0
                            || point.Y > maxY + 5.0
                        )
                        {
                            continue;
                        }

                        PlateHoleCandidate candidate = new PlateHoleCandidate();
                        candidate.Point = new Point(point.X, point.Y, 0);
                        candidate.HoleDiameter = holeDiameter;
                        candidate.BoltSize = boltSize;
                        candidate.SlotX = slotX;
                        candidate.SlotY = slotY;
                        candidate.HoleType = holeType;
                        candidates.Add(candidate);
                    }
                }
            }
            catch { }

            return GroupPlateHoleCandidatesByFamily(candidates);
        }

        private static List<List<PlateHoleCandidate>> GroupPlateHoleCandidatesByFamily(
            List<PlateHoleCandidate> candidates
        )
        {
            List<List<PlateHoleCandidate>> families = new List<List<PlateHoleCandidate>>();
            if (candidates == null || candidates.Count == 0)
                return families;

            candidates.Sort(
                delegate(PlateHoleCandidate first, PlateHoleCandidate second)
                {
                    int xCompare = first.Point.X.CompareTo(second.Point.X);
                    if (xCompare != 0)
                        return xCompare;
                    return first.Point.Y.CompareTo(second.Point.Y);
                }
            );

            foreach (PlateHoleCandidate candidate in candidates)
            {
                List<PlateHoleCandidate> matched = null;
                foreach (List<PlateHoleCandidate> family in families)
                {
                    if (family.Count > 0 && AreSamePlateHoleFamily(family[0], candidate))
                    {
                        matched = family;
                        break;
                    }
                }

                if (matched == null)
                {
                    matched = new List<PlateHoleCandidate>();
                    families.Add(matched);
                }

                matched.Add(candidate);
            }

            families.Sort(
                delegate(List<PlateHoleCandidate> first, List<PlateHoleCandidate> second)
                {
                    return GetPlateHoleFamilyMinimumX(first)
                        .CompareTo(GetPlateHoleFamilyMinimumX(second));
                }
            );

            return families;
        }

        private static bool AreSamePlateHoleFamily(
            PlateHoleCandidate first,
            PlateHoleCandidate second
        )
        {
            if (first == null || second == null)
                return false;

            return AreSamePlateOptionalHoleSize(first.HoleDiameter, second.HoleDiameter)
                && AreSamePlateOptionalHoleSize(first.BoltSize, second.BoltSize)
                && AreSamePlateOptionalHoleSize(first.SlotX, second.SlotX)
                && AreSamePlateOptionalHoleSize(first.SlotY, second.SlotY)
                && String.Equals(
                    (first.HoleType ?? String.Empty).Trim(),
                    (second.HoleType ?? String.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool AreSamePlateOptionalHoleSize(double first, double second)
        {
            bool firstValid = first > 1.0 && first < 500.0;
            bool secondValid = second > 1.0 && second < 500.0;
            if (firstValid != secondValid)
                return false;

            return Math.Abs(first - second) <= HOLE_FAMILY_SIZE_TOL;
        }

        private static double GetPlateHoleFamilyMinimumX(List<PlateHoleCandidate> family)
        {
            double minX = Double.MaxValue;
            if (family != null)
            {
                foreach (PlateHoleCandidate item in family)
                {
                    if (item != null && item.Point != null && item.Point.X < minX)
                        minX = item.Point.X;
                }
            }
            return minX;
        }

        private static List<Point> ConvertPlateHoleFamilyToPoints(List<PlateHoleCandidate> family)
        {
            List<Point> result = new List<Point>();
            if (family == null)
                return result;

            foreach (PlateHoleCandidate item in family)
            {
                if (item == null || item.Point == null)
                    continue;
                AddUniquePoint(
                    result,
                    // Giống Shape H: Z chỉ mang theo phi lỗ để tính khoảng hở
                    // trình bày của mọi chân DIM lỗ; X/Y vẫn là tâm lỗ thật.
                    new Point(item.Point.X, item.Point.Y, item.HoleDiameter),
                    1.0
                );
            }
            return result;
        }

        private static List<Point> BuildPlateHorizontalRepresentativeHoles(List<Point> holes)
        {
            List<Point> result = new List<Point>();
            List<List<Point>> columns = GroupPlateHolePointsByX(holes, HOLE_ROW_COLUMN_TOL);

            foreach (List<Point> column in columns)
            {
                Point lowest = null;
                foreach (Point point in column)
                {
                    if (point != null && (lowest == null || point.Y < lowest.Y))
                        lowest = point;
                }
                if (lowest != null)
                {
                    // Giữ phi lỗ trong Z để writer DIM ngang có thể tạo chân
                    // presentation hở khỏi tâm giống Shape H/Shape L.
                    AddUniquePoint(result, new Point(lowest.X, lowest.Y, lowest.Z), 1.0);
                }
            }

            return result;
        }

        private static List<List<Point>> GroupPlateHolePointsByY(
            List<Point> points,
            double tolerance
        )
        {
            List<List<Point>> groups = new List<List<Point>>();
            if (points == null)
                return groups;

            List<Point> sorted = new List<Point>(points);
            sorted.Sort(
                delegate(Point first, Point second)
                {
                    int yCompare = first.Y.CompareTo(second.Y);
                    if (yCompare != 0)
                        return yCompare;
                    return first.X.CompareTo(second.X);
                }
            );

            foreach (Point point in sorted)
            {
                List<Point> matched = null;
                foreach (List<Point> group in groups)
                {
                    if (group.Count > 0 && Math.Abs(group[0].Y - point.Y) <= tolerance)
                    {
                        matched = group;
                        break;
                    }
                }

                if (matched == null)
                {
                    matched = new List<Point>();
                    groups.Add(matched);
                }
                matched.Add(point);
            }
            return groups;
        }

        private static List<List<Point>> GroupPlateHolePointsByX(
            List<Point> points,
            double tolerance
        )
        {
            List<List<Point>> groups = new List<List<Point>>();
            if (points == null)
                return groups;

            List<Point> sorted = new List<Point>(points);
            sorted.Sort(
                delegate(Point first, Point second)
                {
                    int xCompare = first.X.CompareTo(second.X);
                    if (xCompare != 0)
                        return xCompare;
                    return first.Y.CompareTo(second.Y);
                }
            );

            foreach (Point point in sorted)
            {
                List<Point> matched = null;
                foreach (List<Point> group in groups)
                {
                    if (group.Count > 0 && Math.Abs(group[0].X - point.X) <= tolerance)
                    {
                        matched = group;
                        break;
                    }
                }

                if (matched == null)
                {
                    matched = new List<Point>();
                    groups.Add(matched);
                }
                matched.Add(point);
            }
            return groups;
        }

        private static List<List<Point>> SplitPlateHoleFamilyIntoXClusters(
            List<Point> holes,
            double splitGap
        )
        {
            List<List<Point>> result = new List<List<Point>>();
            if (holes == null || holes.Count == 0)
                return result;

            List<List<Point>> columns = GroupPlateHolePointsByX(holes, HOLE_ROW_COLUMN_TOL);
            columns.Sort(
                delegate(List<Point> first, List<Point> second)
                {
                    return GetAverageX(first).CompareTo(GetAverageX(second));
                }
            );

            List<Point> current = new List<Point>();
            double previousX = Double.NaN;
            foreach (List<Point> column in columns)
            {
                double currentX = GetAverageX(column);
                if (
                    current.Count > 0
                    && !Double.IsNaN(previousX)
                    && currentX - previousX >= splitGap
                )
                {
                    result.Add(current);
                    current = new List<Point>();
                }

                current.AddRange(column);
                previousX = currentX;
            }

            if (current.Count > 0)
                result.Add(current);
            return result;
        }

        private static bool TryBuildPlateWideTwoRowSideLayout(
            List<Point> familyPoints,
            double minX,
            double maxX,
            out List<Point> leftRowFeet,
            out List<Point> rightRowFeet,
            out List<List<Point>> centerLineRows
        )
        {
            // Trường hợp đặc biệt theo bố cục PHU_Shape mà user đã duyệt:
            // một family tạo thành đúng hai hàng Y giống hệt nhau, trải qua nhiều
            // cụm X và có cột ngoài cùng gần cả hai mép plate. Không tạo DIM Y
            // lặp lại ở từng cột; chỉ giữ một chain bên trái, một chain bên phải
            // và nối tâm ngoài cùng của từng hàng bằng center line.
            leftRowFeet = new List<Point>();
            rightRowFeet = new List<Point>();
            centerLineRows = new List<List<Point>>();

            if (familyPoints == null || familyPoints.Count == 0)
                return false;

            List<List<Point>> rows = GroupPlateHolePointsByY(familyPoints, HOLE_ROW_COLUMN_TOL);
            List<List<Point>> columns = GroupPlateHolePointsByX(familyPoints, HOLE_ROW_COLUMN_TOL);

            // Hai chân giữa trong chain Y tương ứng đúng hai hàng lỗ.
            // Ít nhất bốn cột để nhánh này không thay đổi các cụm 2x2 thông thường.
            if (rows.Count != 2 || columns.Count < 4)
                return false;

            List<List<Point>> legacyClusters = SplitPlateHoleFamilyIntoXClusters(
                familyPoints,
                HOLE_CLUSTER_SPLIT_GAP
            );

            // Chỉ can thiệp đúng lỗi nhiều DIM Y ở giữa. Nếu flow cũ chỉ tạo
            // một/hai cụm thì giữ nguyên toàn bộ cách xử lý hiện tại.
            if (legacyClusters.Count < 3)
                return false;

            columns.Sort(
                delegate(List<Point> first, List<Point> second)
                {
                    return GetAverageX(first).CompareTo(GetAverageX(second));
                }
            );
            rows.Sort(
                delegate(List<Point> first, List<Point> second)
                {
                    return GetAverageY(first).CompareTo(GetAverageY(second));
                }
            );

            // Xác nhận đây là lưới đầy đủ, không phải các lỗ rời vô tình cùng Y.
            // Mỗi hàng phải chứa đúng một tâm tại từng cột X của family.
            foreach (List<Point> row in rows)
            {
                List<List<Point>> rowColumns = GroupPlateHolePointsByX(row, HOLE_ROW_COLUMN_TOL);
                rowColumns.Sort(
                    delegate(List<Point> first, List<Point> second)
                    {
                        return GetAverageX(first).CompareTo(GetAverageX(second));
                    }
                );

                if (rowColumns.Count != columns.Count)
                    return false;

                for (int i = 0; i < columns.Count; i++)
                {
                    if (
                        Math.Abs(GetAverageX(rowColumns[i]) - GetAverageX(columns[i]))
                        > HOLE_ROW_COLUMN_TOL
                    )
                    {
                        return false;
                    }
                }
            }

            double firstColumnX = GetAverageX(columns[0]);
            double lastColumnX = GetAverageX(columns[columns.Count - 1]);
            bool nearLeftEdge = Math.Abs(firstColumnX - minX) <= HOLE_END_ZONE;
            bool nearRightEdge = Math.Abs(maxX - lastColumnX) <= HOLE_END_ZONE;

            if (!nearLeftEdge || !nearRightEdge)
                return false;

            foreach (List<Point> row in rows)
            {
                Point left = null;
                Point right = null;

                foreach (Point point in row)
                {
                    if (point == null)
                        continue;

                    if (left == null || point.X < left.X)
                        left = point;
                    if (right == null || point.X > right.X)
                        right = point;
                }

                if (left == null || right == null || Distance2D(left, right) <= HOLE_ROW_COLUMN_TOL)
                {
                    return false;
                }

                // Giữ phi lỗ trong Z tới writer. Center line phía dưới vẫn dùng
                // X/Y tâm thật và không bị dịch theo chân DIM presentation.
                leftRowFeet.Add(new Point(left.X, left.Y, left.Z));
                rightRowFeet.Add(new Point(right.X, right.Y, right.Z));

                List<Point> centerLineRow = new List<Point>();
                centerLineRow.Add(new Point(left.X, left.Y, 0));
                centerLineRow.Add(new Point(right.X, right.Y, 0));
                centerLineRows.Add(centerLineRow);
            }

            return leftRowFeet.Count == 2 && rightRowFeet.Count == 2 && centerLineRows.Count == 2;
        }

        private static double GetAverageY(List<Point> points)
        {
            if (points == null || points.Count == 0)
                return 0.0;

            double sum = 0.0;
            int count = 0;
            foreach (Point point in points)
            {
                if (point == null)
                    continue;
                sum += point.Y;
                count++;
            }
            return count > 0 ? sum / count : 0.0;
        }

        private static double GetAverageX(List<Point> points)
        {
            if (points == null || points.Count == 0)
                return 0.0;

            double sum = 0.0;
            int count = 0;
            foreach (Point point in points)
            {
                if (point == null)
                    continue;
                sum += point.X;
                count++;
            }
            return count > 0 ? sum / count : 0.0;
        }

        private static bool TryResolvePlateHoleExternalSide(
            List<Point> cluster,
            double minX,
            double maxX,
            out bool dimOnRight
        )
        {
            dimOnRight = false;
            if (cluster == null || cluster.Count == 0)
                return false;

            // Giữ nguyên rule Shape L đang chạy ổn định: trước hết phân loại theo
            // X trung bình của cụm. Đây vẫn là rule chính cho mọi cụm thông thường.
            double averageX = GetAverageX(cluster);
            double centerDistanceToLeft = Math.Abs(averageX - minX);
            double centerDistanceToRight = Math.Abs(maxX - averageX);
            bool centerNearLeft = centerDistanceToLeft <= HOLE_END_ZONE;
            bool centerNearRight = centerDistanceToRight <= HOLE_END_ZONE;

            if (centerNearLeft || centerNearRight)
            {
                dimOnRight =
                    centerNearRight
                    && (!centerNearLeft || centerDistanceToRight < centerDistanceToLeft);
                return true;
            }

            // Fallback hẹp cho đúng trường hợp user chỉ định: family có X trung bình
            // nằm giữa pallet nhưng dãy X trải rộng và lỗ ngoài cùng thực sự sát mép.
            // Nếu không có lỗ ngoài cùng gần mép thì trả false để giữ nguyên nhánh nội bộ.
            double clusterMinX = Double.MaxValue;
            double clusterMaxX = -Double.MaxValue;
            foreach (Point point in cluster)
            {
                if (point == null)
                    continue;
                if (point.X < clusterMinX)
                    clusterMinX = point.X;
                if (point.X > clusterMaxX)
                    clusterMaxX = point.X;
            }

            if (clusterMinX == Double.MaxValue || clusterMaxX == -Double.MaxValue)
                return false;

            double edgeDistanceToLeft = Math.Abs(clusterMinX - minX);
            double edgeDistanceToRight = Math.Abs(maxX - clusterMaxX);
            bool outerHoleNearLeft = edgeDistanceToLeft <= HOLE_END_ZONE;
            bool outerHoleNearRight = edgeDistanceToRight <= HOLE_END_ZONE;

            if (!(outerHoleNearLeft || outerHoleNearRight))
                return false;

            // Khi đồng thời gần hai mép, chọn mép có khoảng hở nhỏ hơn; hòa thì ưu tiên
            // trái để kết quả ổn định và đúng bố cục mẫu user đã duyệt.
            dimOnRight =
                outerHoleNearRight
                && (!outerHoleNearLeft || edgeDistanceToRight < edgeDistanceToLeft);
            return true;
        }

        private static double GetPlateHolePresentationGap(Point hole)
        {
            if (hole != null && hole.Z > 1.0 && hole.Z < 500.0)
                return hole.Z;
            return 0.0;
        }

        private static Point CreatePlateHolePresentationFoot(
            Point hole,
            double directionX,
            double directionY
        )
        {
            if (hole == null)
                return null;

            double gap = GetPlateHolePresentationGap(hole);
            double length = Math.Sqrt(directionX * directionX + directionY * directionY);

            if (gap <= 0.0 || length <= 0.000001)
                return new Point(hole.X, hole.Y, 0);

            // Dịch chân đúng bằng phi lỗ theo hướng đặt DIM. Vì dịch vuông góc
            // trục đo nên trị số chuỗi X/Y vẫn giữ nguyên như khi lấy tại tâm.
            return new Point(
                hole.X + gap * directionX / length,
                hole.Y + gap * directionY / length,
                0
            );
        }

        private static List<Point> SelectPlateVerticalRowFeet(List<Point> cluster, bool dimOnRight)
        {
            List<Point> result = new List<Point>();
            List<List<Point>> rows = GroupPlateHolePointsByY(cluster, HOLE_ROW_COLUMN_TOL);

            foreach (List<Point> row in rows)
            {
                Point selected = null;
                foreach (Point point in row)
                {
                    if (point == null)
                        continue;
                    if (
                        selected == null
                        || (dimOnRight && point.X > selected.X)
                        || (!dimOnRight && point.X < selected.X)
                    )
                    {
                        selected = point;
                    }
                }
                if (selected != null)
                {
                    result.Add(new Point(selected.X, selected.Y, selected.Z));
                }
            }

            result.Sort(
                delegate(Point first, Point second)
                {
                    return first.Y.CompareTo(second.Y);
                }
            );
            return result;
        }

        private static List<List<Point>> BuildPlateHoleCenterLineSegments(List<Point> holes)
        {
            List<List<Point>> result = new List<List<Point>>();
            if (holes == null || holes.Count < 2)
                return result;

            // Line ngang: tâm trái ngoài cùng -> tâm phải ngoài cùng của từng hàng.
            List<List<Point>> rows = GroupPlateHolePointsByY(holes, HOLE_ROW_COLUMN_TOL);
            foreach (List<Point> row in rows)
            {
                Point first = null;
                Point last = null;
                if (row != null)
                {
                    foreach (Point hole in row)
                    {
                        if (hole == null)
                            continue;
                        if (first == null || hole.X < first.X)
                            first = hole;
                        if (last == null || hole.X > last.X)
                            last = hole;
                    }
                }

                if (first != null && last != null && Distance2D(first, last) > HOLE_ROW_COLUMN_TOL)
                {
                    List<Point> segment = new List<Point>();
                    segment.Add(new Point(first.X, first.Y, 0));
                    segment.Add(new Point(last.X, last.Y, 0));
                    result.Add(segment);
                }
            }

            // Line dọc: tâm dưới ngoài cùng -> tâm trên ngoài cùng của từng cột.
            List<List<Point>> columns = GroupPlateHolePointsByX(holes, HOLE_ROW_COLUMN_TOL);
            foreach (List<Point> column in columns)
            {
                Point first = null;
                Point last = null;
                if (column != null)
                {
                    foreach (Point hole in column)
                    {
                        if (hole == null)
                            continue;
                        if (first == null || hole.Y < first.Y)
                            first = hole;
                        if (last == null || hole.Y > last.Y)
                            last = hole;
                    }
                }

                if (first != null && last != null && Distance2D(first, last) > HOLE_ROW_COLUMN_TOL)
                {
                    List<Point> segment = new List<Point>();
                    segment.Add(new Point(first.X, first.Y, 0));
                    segment.Add(new Point(last.X, last.Y, 0));
                    result.Add(segment);
                }
            }

            return result;
        }

        private static void CreatePlateHoleRowAndColumnCenterLines(View view, List<Point> holes)
        {
            CreatePlateHoleRowCenterLines(view, BuildPlateHoleCenterLineSegments(holes));
        }

        private static void CreatePlateHoleRowCenterLines(
            View view,
            List<List<Point>> centerLineRows
        )
        {
            if (view == null || centerLineRows == null)
                return;

            foreach (List<Point> row in centerLineRows)
            {
                try
                {
                    if (
                        row == null
                        || row.Count != 2
                        || row[0] == null
                        || row[1] == null
                        || Distance2D(row[0], row[1]) <= HOLE_ROW_COLUMN_TOL
                    )
                    {
                        continue;
                    }

                    Point startPoint = new Point(row[0].X, row[0].Y, 0);
                    Point endPoint = new Point(row[1].X, row[1].Y, 0);

                    if (HasEquivalentPlateHoleCenterLine(view, startPoint, endPoint))
                    {
                        continue;
                    }

                    // Dùng đúng line attributes đang được PHU_Shape sử dụng cho
                    // dãy lỗ cùng Y; không tự dựng thêm một kiểu line mới.
                    TTSK_AutoDim_Plates.PHU_LineDistance.InsertLineWithLineDistanceAttributes(
                        view,
                        startPoint,
                        endPoint
                    );
                }
                catch { }
            }
        }

        private static bool HasEquivalentPlateHoleCenterLine(
            View view,
            Point startPoint,
            Point endPoint
        )
        {
            try
            {
                if (view == null || startPoint == null || endPoint == null)
                    return false;

                DrawingObjectEnumerator objects = view.GetAllObjects(
                    typeof(Tekla.Structures.Drawing.Line)
                );

                while (objects.MoveNext())
                {
                    Tekla.Structures.Drawing.Line line =
                        objects.Current as Tekla.Structures.Drawing.Line;
                    if (line == null)
                        continue;

                    Point existingStart;
                    Point existingEnd;
                    if (!TryGetPlateDrawingLinePoints(line, out existingStart, out existingEnd))
                    {
                        continue;
                    }

                    bool sameDirection =
                        Distance2D(existingStart, startPoint) <= HOLE_ROW_COLUMN_TOL
                        && Distance2D(existingEnd, endPoint) <= HOLE_ROW_COLUMN_TOL;
                    bool reverseDirection =
                        Distance2D(existingStart, endPoint) <= HOLE_ROW_COLUMN_TOL
                        && Distance2D(existingEnd, startPoint) <= HOLE_ROW_COLUMN_TOL;

                    if (sameDirection || reverseDirection)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryGetPlateDrawingLinePoints(
            Tekla.Structures.Drawing.Line line,
            out Point startPoint,
            out Point endPoint
        )
        {
            startPoint = null;
            endPoint = null;

            if (line == null)
                return false;

            string[] startNames = new string[]
            {
                "StartPoint",
                "Start",
                "Point1",
                "FirstPoint",
                "P1"
            };
            string[] endNames = new string[] { "EndPoint", "End", "Point2", "SecondPoint", "P2" };

            for (int i = 0; i < startNames.Length; i++)
            {
                startPoint = TryGetPointProperty(line, startNames[i]);
                endPoint = TryGetPointProperty(line, endNames[i]);

                if (startPoint != null && endPoint != null)
                    return true;
            }

            return false;
        }

        private static void GetPlateHoleExternalClusterCounts(
            List<List<PlateHoleCandidate>> families,
            double minX,
            double maxX,
            out int leftCount,
            out int rightCount
        )
        {
            leftCount = 0;
            rightCount = 0;

            if (families == null)
                return;

            foreach (List<PlateHoleCandidate> family in families)
            {
                List<Point> familyPoints = ConvertPlateHoleFamilyToPoints(family);
                List<Point> leftRowFeet;
                List<Point> rightRowFeet;
                List<List<Point>> centerLineRows;

                if (
                    TryBuildPlateWideTwoRowSideLayout(
                        familyPoints,
                        minX,
                        maxX,
                        out leftRowFeet,
                        out rightRowFeet,
                        out centerLineRows
                    )
                )
                {
                    // Bộ quản lý tầng phải biết trước sẽ có đúng một DIM lỗ
                    // ở mỗi phía, để DIM tổng dọc tiếp tục nằm ngoài cùng.
                    leftCount++;
                    rightCount++;
                    continue;
                }

                List<List<Point>> clusters = SplitPlateHoleFamilyIntoXClusters(
                    familyPoints,
                    HOLE_CLUSTER_SPLIT_GAP
                );

                foreach (List<Point> cluster in clusters)
                {
                    bool dimOnRight;
                    if (!TryResolvePlateHoleExternalSide(cluster, minX, maxX, out dimOnRight))
                    {
                        continue;
                    }

                    if (dimOnRight)
                        rightCount++;
                    else
                        leftCount++;
                }
            }
        }

        private static int CreateInternalPlateHoleYDim(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> contour,
            List<Point> cluster,
            List<Point> rowFeet,
            double minY,
            double maxY
        )
        {
            if (
                handler == null
                || view == null
                || cluster == null
                || cluster.Count == 0
                || rowFeet == null
                || rowFeet.Count == 0
            )
            {
                return 0;
            }

            Point baselineFoot = CreatePlateHolePresentationFoot(rowFeet[0], -1.0, 0.0);
            double baselineX = baselineFoot.X;
            double bottomY = GetBoundaryExtremeYAtX(contour, baselineX, false, minY);
            PointList points = new PointList();
            points.Add(new Point(baselineX, bottomY, 0));
            foreach (Point rowFoot in rowFeet)
            {
                points.Add(CreatePlateHolePresentationFoot(rowFoot, -1.0, 0.0));
            }

            if (rowFeet.Count > 1)
            {
                double topY = GetBoundaryExtremeYAtX(contour, baselineX, true, maxY);
                points.Add(new Point(baselineX, topY, 0));
            }

            // Trước đây rowFeet lấy cột xa phía DIM nên phải cộng cả bề rộng cụm
            // để kéo đường DIM trở ngược ra ngoài. Nay chân đã lấy ngay cột gần
            // phía DIM; giữ phần bù cũ sẽ đẩy DIM xa thừa đúng một bề rộng cụm.
            double distance = CurrentDimTierUnit * 0.5;
            return handler.CreateDimensionSet(view, points, new Vector(-1, 0, 0), distance) != null
                ? 1
                : 0;
        }

        private static double GetBoundaryExtremeYAtX(
            List<Point> contour,
            double x,
            bool highest,
            double fallback
        )
        {
            if (contour == null || contour.Count < 2)
                return fallback;

            double best = highest ? -Double.MaxValue : Double.MaxValue;
            bool found = false;

            for (int i = 0; i < contour.Count; i++)
            {
                Point a = contour[i];
                Point b = contour[(i + 1) % contour.Count];
                if (
                    a == null
                    || b == null
                    || x < Math.Min(a.X, b.X) - 0.1
                    || x > Math.Max(a.X, b.X) + 0.1
                )
                {
                    continue;
                }

                if (Math.Abs(a.X - b.X) <= 0.1)
                {
                    double candidate = highest ? Math.Max(a.Y, b.Y) : Math.Min(a.Y, b.Y);
                    best = highest ? Math.Max(best, candidate) : Math.Min(best, candidate);
                    found = true;
                    continue;
                }

                double ratio = (x - a.X) / (b.X - a.X);
                double y = a.Y + ratio * (b.Y - a.Y);
                best = highest ? Math.Max(best, y) : Math.Min(best, y);
                found = true;
            }

            return found ? best : fallback;
        }

        private static double GetPlateBoltSize(ModelBoltGroup group)
        {
            if (group == null)
                return 0.0;

            double value = 0.0;
            try
            {
                value = group.BoltSize;
            }
            catch { }
            if (value > 1.0 && value < 200.0)
                return value;

            value = GetReportDouble(group, "BOLT_SIZE");
            if (value > 1.0 && value < 200.0)
                return value;
            value = GetReportDouble(group, "BOLT_DIAMETER");
            if (value > 1.0 && value < 200.0)
                return value;
            return GetDoublePropertyByReflection(group, "BoltSize");
        }

        private static double GetPlateHoleSlotX(ModelBoltGroup group)
        {
            double value = GetReportDouble(group, "SLOTTED_HOLE_X");
            if (value > 0.0 && value < 500.0)
                return value;
            value = GetReportDouble(group, "LONG_HOLE_X");
            if (value > 0.0 && value < 500.0)
                return value;
            value = GetDoublePropertyByReflection(group, "SlottedHoleX");
            if (value > 0.0 && value < 500.0)
                return value;
            return GetDoublePropertyByReflection(group, "SlotX");
        }

        private static double GetPlateHoleSlotY(ModelBoltGroup group)
        {
            double value = GetReportDouble(group, "SLOTTED_HOLE_Y");
            if (value > 0.0 && value < 500.0)
                return value;
            value = GetReportDouble(group, "LONG_HOLE_Y");
            if (value > 0.0 && value < 500.0)
                return value;
            value = GetDoublePropertyByReflection(group, "SlottedHoleY");
            if (value > 0.0 && value < 500.0)
                return value;
            return GetDoublePropertyByReflection(group, "SlotY");
        }

        private static string GetPlateHoleTypeText(ModelBoltGroup group)
        {
            string value = GetPlateReportString(group, "HOLE_TYPE");
            if (!String.IsNullOrEmpty(value))
                return value;
            value = GetPlateReportString(group, "BOLT_HOLE_TYPE");
            if (!String.IsNullOrEmpty(value))
                return value;
            value = GetStringPropertyByReflection(group, "HoleType");
            if (!String.IsNullOrEmpty(value))
                return value;
            return GetStringPropertyByReflection(group, "BoltHoleType");
        }

        private static string GetPlateReportString(ModelBoltGroup group, string propertyName)
        {
            try
            {
                string value = String.Empty;
                group.GetReportProperty(propertyName, ref value);
                return (value ?? String.Empty).Trim();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static string GetStringPropertyByReflection(object target, string propertyName)
        {
            try
            {
                if (target == null)
                    return String.Empty;
                PropertyInfo property = target
                    .GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanRead)
                    return String.Empty;
                object value = property.GetValue(target, null);
                return value == null ? String.Empty : value.ToString().Trim();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static int CreateHoleCenterDims(
            StraightDimensionSetHandler handler,
            View view,
            List<List<PlateHoleCandidate>> families,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double bottomHoleDimOffset,
            double leftHoleDimOffset,
            double rightHoleDimOffset,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight
        )
        {
            int count = 0;

            // PA6: Offset tầng của DIM lỗ được cấp từ bộ quản lý tầng 4 hướng.
            // Không tự bù tầng theo chamfer/bounding box trong hàm lỗ nữa.
            if (bottomHoleDimOffset <= 0.0)
                bottomHoleDimOffset = GetCleanDimOffsetByTier(1);
            if (leftHoleDimOffset <= 0.0)
                leftHoleDimOffset = GetCleanDimOffsetByTier(1);
            if (rightHoleDimOffset <= 0.0)
                rightHoleDimOffset = GetCleanDimOffsetByTier(1);

            if (families == null || families.Count == 0)
                return count;

            Point leftBottomEdgeForXDim = GetSidePointNearBottom(polygon, minX, true);
            Point rightBottomEdgeForXDim = GetSidePointNearBottom(polygon, maxX, false);
            int horizontalTierIndex = 0;
            int leftTierIndex = 0;
            int rightTierIndex = 0;

            foreach (List<PlateHoleCandidate> family in families)
            {
                List<Point> familyPoints = ConvertPlateHoleFamilyToPoints(family);
                if (familyPoints.Count == 0)
                    continue;

                // DIM chỉ neo tới lỗ gần phía đặt. Hai loại center line độc lập
                // biểu diễn liên hệ toàn bộ dãy tâm theo hàng Y và cột X.
                CreatePlateHoleRowAndColumnCenterLines(view, familyPoints);

                // Shape L: một cột X chỉ lấy lỗ thấp nhất làm đại diện cho DIM ngang,
                // sau đó mới gom theo hàng Y. Mỗi family kỹ thuật có chain riêng.
                List<Point> horizontalRepresentatives = BuildPlateHorizontalRepresentativeHoles(
                    familyPoints
                );
                List<List<Point>> rows = GroupPlateHolePointsByY(
                    horizontalRepresentatives,
                    HOLE_ROW_COLUMN_TOL
                );

                foreach (List<Point> row in rows)
                {
                    if (row == null || row.Count == 0)
                        continue;

                    row.Sort(
                        delegate(Point a, Point b)
                        {
                            return a.X.CompareTo(b.X);
                        }
                    );
                    // Không tạo DIM nếu không tìm được chân thật trên contour.
                    // Tuyệt đối không dựng góc bounding box làm fallback.
                    if (leftBottomEdgeForXDim == null || rightBottomEdgeForXDim == null)
                    {
                        continue;
                    }

                    PointList xDim = new PointList();
                    xDim.Add(leftBottomEdgeForXDim);
                    foreach (Point hole in row)
                    {
                        // DIM ngang đặt phía dưới: chân presentation tại lỗ dịch
                        // xuống đúng bằng phi lỗ; X tâm và trị số chuỗi không đổi.
                        xDim.Add(CreatePlateHolePresentationFoot(hole, 0.0, -1.0));
                    }
                    xDim.Add(rightBottomEdgeForXDim);

                    double tierOffset =
                        bottomHoleDimOffset + horizontalTierIndex * CurrentDimTierUnit;
                    double distance = GetBottomDistanceByAnchor4Direction(
                        xDim,
                        leftForLength,
                        rightForLength,
                        bottomForHeight,
                        topForHeight,
                        tierOffset
                    );

                    if (
                        handler.CreateDimensionSet(view, xDim, new Vector(0, -1, 0), distance)
                        != null
                    )
                    {
                        count++;
                        horizontalTierIndex++;
                    }
                }

                List<Point> leftWideRowFeet;
                List<Point> rightWideRowFeet;
                List<List<Point>> wideCenterLineRows;

                if (
                    TryBuildPlateWideTwoRowSideLayout(
                        familyPoints,
                        minX,
                        maxX,
                        out leftWideRowFeet,
                        out rightWideRowFeet,
                        out wideCenterLineRows
                    )
                )
                {
                    // Mẫu 4 cột x 2 hàng trải rộng:
                    // - chỉ một chain Y ở mép trái, lấy tâm cột trái ngoài cùng;
                    // - chỉ một chain Y ở mép phải, lấy tâm cột phải ngoài cùng;
                    // - center line hàng/cột đã được tạo riêng phía trên.
                    // Không đi tiếp vào SplitPlateHoleFamilyIntoXClusters vì đó
                    // chính là nguyên nhân sinh các chain 85-330-85 ở giữa.
                    double leftSideOffset = leftHoleDimOffset + leftTierIndex * CurrentDimTierUnit;
                    leftTierIndex++;
                    count += CreateOneSideHoleYDim(
                        handler,
                        view,
                        polygon,
                        leftWideRowFeet,
                        minY,
                        maxY,
                        new Vector(-1, 0, 0),
                        leftSideOffset,
                        leftForLength,
                        rightForLength,
                        bottomForHeight,
                        topForHeight
                    );

                    double rightSideOffset =
                        rightHoleDimOffset + rightTierIndex * CurrentDimTierUnit;
                    rightTierIndex++;
                    count += CreateOneSideHoleYDim(
                        handler,
                        view,
                        polygon,
                        rightWideRowFeet,
                        minY,
                        maxY,
                        new Vector(1, 0, 0),
                        rightSideOffset,
                        leftForLength,
                        rightForLength,
                        bottomForHeight,
                        topForHeight
                    );

                    continue;
                }

                // Các family còn lại tiếp tục tách cụm X nguyên vẹn như Shape L.
                List<List<Point>> xClusters = SplitPlateHoleFamilyIntoXClusters(
                    familyPoints,
                    HOLE_CLUSTER_SPLIT_GAP
                );

                foreach (List<Point> cluster in xClusters)
                {
                    if (cluster == null || cluster.Count == 0)
                        continue;

                    bool dimOnRight;
                    bool isExternal = TryResolvePlateHoleExternalSide(
                        cluster,
                        minX,
                        maxX,
                        out dimOnRight
                    );

                    List<Point> rowFeet = SelectPlateVerticalRowFeet(cluster, dimOnRight);
                    if (rowFeet.Count == 0)
                        continue;

                    if (isExternal)
                    {
                        double sideOffset;
                        Vector sideDirection;

                        if (dimOnRight)
                        {
                            sideOffset = rightHoleDimOffset + rightTierIndex * CurrentDimTierUnit;
                            rightTierIndex++;
                            sideDirection = new Vector(1, 0, 0);
                        }
                        else
                        {
                            sideOffset = leftHoleDimOffset + leftTierIndex * CurrentDimTierUnit;
                            leftTierIndex++;
                            sideDirection = new Vector(-1, 0, 0);
                        }

                        count += CreateOneSideHoleYDim(
                            handler,
                            view,
                            polygon,
                            rowFeet,
                            minY,
                            maxY,
                            sideDirection,
                            sideOffset,
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight
                        );
                    }
                    else
                    {
                        count += CreateInternalPlateHoleYDim(
                            handler,
                            view,
                            polygon,
                            cluster,
                            rowFeet,
                            minY,
                            maxY
                        );
                    }
                }
            }

            return count;
        }

        private static double GetCleanDimOffsetByTier(int tier)
        {
            // PA6: Tầng DIM chính dùng hệ 100mm ổn định theo file nền hiện tại.
            // Tầng 1 = 100, tầng 2 = 200, tầng 3 = 300...
            // Hàm này chỉ quản lý khoảng cách tầng, không bù chamfer/notch/bounding box.
            if (tier <= 1)
                return CurrentDimTierUnit;

            return CurrentDimTierUnit * tier;
        }

        private static void InitializeCurrentDimTierSpacing(List<View> views)
        {
            CurrentDimTierUnit = NORMAL_NO_CHAMFER_DIM_OFFSET;

            double manualScale;
            if (!TTSK_AutoDim_Plates.ManualDrawingScaleOverride.TryGet(out manualScale))
                return;

            bool actualScaleFound = false;
            if (views != null)
            {
                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    double actualScale = GetViewScaleNumberForTitle3(view);
                    if (actualScale <= 0.0)
                        continue;

                    actualScaleFound = true;
                    if (Math.Abs(actualScale - manualScale) > 0.001)
                    {
                        throw new InvalidOperationException(
                            "Không áp dụng được manual scale cho toàn bộ target view."
                        );
                    }
                }
            }

            if (!actualScaleFound)
            {
                throw new InvalidOperationException(
                    "Không đọc lại được manual scale sau CommitChanges."
                );
            }

            CurrentDimTierUnit = manualScale * 10.0;
        }

        private static int CreateOneSideHoleYDim(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            List<Point> holes,
            double minY,
            double maxY,
            Vector direction,
            double offset,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight
        )
        {
            if (holes == null || holes.Count == 0)
                return 0;

            holes.Sort(
                delegate(Point a, Point b)
                {
                    return a.Y.CompareTo(b.Y);
                }
            );

            PointList yDim = new PointList();

            bool isLeftSide = direction.X < 0;

            // Trái/phải chỉ do vị trí cụm lỗ quyết định ở flow phía trên.
            // Sau khi đã chọn phía, tuyệt đối không tìm lại "mép gần nhất" trên
            // contour: cạnh vát/notch gần lỗ có thể thắng và làm hai chân neo lệch phía.
            //
            // Hai chân DIM tổng dọc C/D đã đúng: dùng nguyên hai chân đó ở bên trái.
            // Bên phải lấy endpoint đối diện của chính hai cạnh ngang C/D, bằng cùng
            // resolver DIM tổng. Không dùng X của DIM tổng ngang/bounding-box.
            Point sideBottomPoint;
            Point sideTopPoint;
            bool resolvedFromTotalDimensions =
                TryResolvePlateHoleSideFeetFromTotalVerticalDimension(
                    polygon,
                    minY,
                    maxY,
                    isLeftSide,
                    bottomForHeight,
                    topForHeight,
                    out sideBottomPoint,
                    out sideTopPoint
                );

            // Fallback chỉ dành cho dữ liệu cũ/malformed thiếu chân DIM tổng.
            // DIM lỗ vẫn là bắt buộc: không bỏ DIM chỉ vì resolver phụ thất bại.
            if (!resolvedFromTotalDimensions)
            {
                sideBottomPoint = GetHorizontalEdgePoint(polygon, minY, isLeftSide, true);
                sideTopPoint = GetHorizontalEdgePoint(polygon, maxY, isLeftSide, false);
            }

            if (sideBottomPoint == null || sideTopPoint == null)
            {
                double emergencyX = holes[0].X;
                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;
                    if (isLeftSide && hole.X < emergencyX)
                        emergencyX = hole.X;
                    if (!isLeftSide && hole.X > emergencyX)
                        emergencyX = hole.X;
                }
                sideBottomPoint = new Point(emergencyX, minY, 0);
                sideTopPoint = new Point(emergencyX, maxY, 0);
            }

            yDim.Add(sideBottomPoint);

            foreach (Point h in holes)
            {
                // DIM dọc: chân presentation dịch khỏi tâm đúng bằng phi lỗ
                // về cùng phía với đường DIM; Y tâm và trị số chuỗi không đổi.
                yDim.Add(CreatePlateHolePresentationFoot(h, direction.X, direction.Y));
            }

            yDim.Add(sideTopPoint);

            // PA11: Không đảo PointList.
            // Distance được tính từ chân đầu của DIM tới vị trí tầng theo neo A/B/C/D.

            double yDimDistance = offset;
            if (direction.X < 0)
            {
                yDimDistance = GetLeftDistanceByAnchor4Direction(
                    yDim,
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight,
                    offset
                );
            }
            else if (direction.X > 0)
            {
                yDimDistance = GetRightDistanceByAnchor4Direction(
                    yDim,
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight,
                    offset
                );
            }

            StraightDimensionSet dimY = handler.CreateDimensionSet(
                view,
                yDim,
                direction,
                yDimDistance
            );

            if (dimY != null)
                return 1;

            return 0;
        }

        private static bool TryResolvePlateHoleSideFeetFromTotalVerticalDimension(
            List<Point> polygon,
            double minY,
            double maxY,
            bool isLeftSide,
            Point bottomForHeight,
            Point topForHeight,
            out Point sideBottomPoint,
            out Point sideTopPoint
        )
        {
            sideBottomPoint = null;
            sideTopPoint = null;

            if (bottomForHeight == null || topForHeight == null)
                return false;

            if (isLeftSide)
            {
                // Không resolve lại: đây chính là hai chân của DIM tổng dọc.
                sideBottomPoint = new Point(bottomForHeight.X, bottomForHeight.Y, 0);
                sideTopPoint = new Point(topForHeight.X, topForHeight.Y, 0);
                return true;
            }

            // Cặp phải là hai endpoint còn lại trên đúng các mép ngang đã tạo C/D.
            // Dùng Y của C/D làm nguồn chính; minY/maxY chỉ là fallback dữ liệu cũ.
            sideBottomPoint = GetStraightHorizontalEdgePointForTotalDim(
                polygon,
                bottomForHeight == null ? minY : bottomForHeight.Y,
                false
            );
            sideTopPoint = GetStraightHorizontalEdgePointForTotalDim(
                polygon,
                topForHeight == null ? maxY : topForHeight.Y,
                false
            );
            return sideBottomPoint != null && sideTopPoint != null;
        }

        private static Point GetHorizontalEdgePoint(
            List<Point> polygon,
            double edgeY,
            bool leftSide,
            bool bottom
        )
        {
            if (polygon == null || polygon.Count == 0)
                return null;

            // ROOT FIX:
            // Flow cũ lấy bestY từ đúng một vertex rồi lọc bằng TOL=0. Hai đầu của
            // cùng một mép sau phép chiếu có thể lệch vài phần nghìn mm, vì vậy chỉ
            // còn đầu trái hoặc đầu phải trong candidates. Kết quả là DIM bên phải
            // có chân trên bám phải nhưng chân dưới lại bám trái (hoặc ngược lại).
            //
            // Flow mới làm việc trực tiếp trong hệ tọa độ view: xác định Y biên thật,
            // lấy mọi vertex/giao điểm contour trên dải 0.1 mm đó, rồi phía DIM quyết
            // định cực trị X. Dung sai chỉ nhận "cùng mép"; không quyết định trái/phải.
            double targetY = bottom ? Double.MaxValue : -Double.MaxValue;
            foreach (Point point in polygon)
            {
                if (point == null)
                    continue;

                targetY = bottom ? Math.Min(targetY, point.Y) : Math.Max(targetY, point.Y);
            }

            if (targetY == Double.MaxValue || targetY == -Double.MaxValue)
                targetY = edgeY;

            List<Point> candidates = new List<Point>();
            foreach (Point point in polygon)
            {
                if (point != null && Math.Abs(point.Y - targetY) <= PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                {
                    AddUniquePoint(
                        candidates,
                        new Point(point.X, point.Y, 0),
                        PLATE_EDGE_FOOT_ALIGNMENT_TOL * 0.25
                    );
                }
            }

            // Fallback hình học cho contour không có vertex đúng ngay dải biên:
            // chiếu đường ngang Y=targetY và lấy giao điểm với từng segment thật.
            if (candidates.Count == 0)
            {
                AddPlateContourIntersectionsAtY(polygon, targetY, candidates);
            }

            if (candidates.Count == 0)
                return null;

            Point result = null;
            foreach (Point point in candidates)
            {
                if (point == null)
                    continue;

                if (
                    result == null
                    || (leftSide && point.X < result.X)
                    || (!leftSide && point.X > result.X)
                )
                {
                    result = point;
                }
            }

            // Nếu dải dung sai vẫn chỉ thấy đầu ở nửa đối diện, không bỏ DIM.
            // Fallback hẹp: giới hạn vertex vào đúng nửa trái/phải rồi lấy điểm
            // có Y gần biên nhất. Đây là phương án cuối, sau dải mép và giao contour.
            double minContourX = Double.MaxValue;
            double maxContourX = -Double.MaxValue;
            foreach (Point point in polygon)
            {
                if (point == null)
                    continue;
                if (point.X < minContourX)
                    minContourX = point.X;
                if (point.X > maxContourX)
                    maxContourX = point.X;
            }

            if (result != null && minContourX < Double.MaxValue && maxContourX > -Double.MaxValue)
            {
                double centerX = (minContourX + maxContourX) * 0.5;
                if (
                    (leftSide && result.X > centerX + PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                    || (!leftSide && result.X < centerX - PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                )
                {
                    Point sideFallback = null;
                    double bestDistanceToEdge = Double.MaxValue;
                    foreach (Point point in polygon)
                    {
                        if (point == null)
                            continue;

                        bool onRequestedHalf = leftSide
                            ? point.X <= centerX + PLATE_EDGE_FOOT_ALIGNMENT_TOL
                            : point.X >= centerX - PLATE_EDGE_FOOT_ALIGNMENT_TOL;
                        if (!onRequestedHalf)
                            continue;

                        double distanceToEdge = Math.Abs(point.Y - targetY);
                        if (
                            sideFallback == null
                            || distanceToEdge < bestDistanceToEdge - PLATE_EDGE_FOOT_ALIGNMENT_TOL
                            || (
                                Math.Abs(distanceToEdge - bestDistanceToEdge)
                                    <= PLATE_EDGE_FOOT_ALIGNMENT_TOL
                                && (
                                    (leftSide && point.X < sideFallback.X)
                                    || (!leftSide && point.X > sideFallback.X)
                                )
                            )
                        )
                        {
                            sideFallback = point;
                            bestDistanceToEdge = distanceToEdge;
                        }
                    }

                    if (sideFallback != null)
                        result = sideFallback;
                }
            }

            return result == null ? null : new Point(result.X, result.Y, 0);
        }

        private static Point GetSidePointNearBottom(
            List<Point> polygon,
            double edgeX,
            bool leftSide
        )
        {
            return GetSidePointByX(polygon, edgeX, leftSide, true);
        }

        private static Point GetSidePointByX(
            List<Point> polygon,
            double edgeX,
            bool leftSide,
            bool bottom
        )
        {
            if (polygon == null || polygon.Count == 0)
                return null;

            // Đối xứng với resolver Y ở trên: nhận cạnh trái/phải bằng một dải X
            // cục bộ rồi chọn đầu dưới/trên. Không dùng equality tuyệt đối TOL=0.
            double targetX = leftSide ? Double.MaxValue : -Double.MaxValue;
            foreach (Point point in polygon)
            {
                if (point == null)
                    continue;

                targetX = leftSide ? Math.Min(targetX, point.X) : Math.Max(targetX, point.X);
            }

            if (targetX == Double.MaxValue || targetX == -Double.MaxValue)
                targetX = edgeX;

            List<Point> candidates = new List<Point>();
            foreach (Point point in polygon)
            {
                if (point != null && Math.Abs(point.X - targetX) <= PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                {
                    AddUniquePoint(
                        candidates,
                        new Point(point.X, point.Y, 0),
                        PLATE_EDGE_FOOT_ALIGNMENT_TOL * 0.25
                    );
                }
            }

            if (candidates.Count == 0)
            {
                AddPlateContourIntersectionsAtX(polygon, targetX, candidates);
            }

            if (candidates.Count == 0)
                return null;

            Point result = null;
            foreach (Point point in candidates)
            {
                if (point == null)
                    continue;

                if (
                    result == null
                    || (bottom && point.Y < result.Y)
                    || (!bottom && point.Y > result.Y)
                )
                {
                    result = point;
                }
            }

            double minContourY = Double.MaxValue;
            double maxContourY = -Double.MaxValue;
            foreach (Point point in polygon)
            {
                if (point == null)
                    continue;
                if (point.Y < minContourY)
                    minContourY = point.Y;
                if (point.Y > maxContourY)
                    maxContourY = point.Y;
            }

            if (result != null && minContourY < Double.MaxValue && maxContourY > -Double.MaxValue)
            {
                double centerY = (minContourY + maxContourY) * 0.5;
                if (
                    (bottom && result.Y > centerY + PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                    || (!bottom && result.Y < centerY - PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                )
                {
                    Point edgeFallback = null;
                    double bestDistanceToEdge = Double.MaxValue;
                    foreach (Point point in polygon)
                    {
                        if (point == null)
                            continue;

                        bool onRequestedHalf = bottom
                            ? point.Y <= centerY + PLATE_EDGE_FOOT_ALIGNMENT_TOL
                            : point.Y >= centerY - PLATE_EDGE_FOOT_ALIGNMENT_TOL;
                        if (!onRequestedHalf)
                            continue;

                        double distanceToEdge = Math.Abs(point.X - targetX);
                        if (
                            edgeFallback == null
                            || distanceToEdge < bestDistanceToEdge - PLATE_EDGE_FOOT_ALIGNMENT_TOL
                            || (
                                Math.Abs(distanceToEdge - bestDistanceToEdge)
                                    <= PLATE_EDGE_FOOT_ALIGNMENT_TOL
                                && (
                                    (bottom && point.Y < edgeFallback.Y)
                                    || (!bottom && point.Y > edgeFallback.Y)
                                )
                            )
                        )
                        {
                            edgeFallback = point;
                            bestDistanceToEdge = distanceToEdge;
                        }
                    }

                    if (edgeFallback != null)
                        result = edgeFallback;
                }
            }

            return result == null ? null : new Point(result.X, result.Y, 0);
        }

        private static void AddPlateContourIntersectionsAtY(
            List<Point> polygon,
            double targetY,
            List<Point> result
        )
        {
            if (polygon == null || polygon.Count < 2 || result == null)
                return;

            for (int i = 0; i < polygon.Count; i++)
            {
                Point first = polygon[i];
                Point second = polygon[(i + 1) % polygon.Count];
                if (first == null || second == null)
                    continue;

                double deltaY = second.Y - first.Y;
                if (Math.Abs(deltaY) <= PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                {
                    if (
                        Math.Min(Math.Abs(first.Y - targetY), Math.Abs(second.Y - targetY))
                        <= PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    )
                    {
                        AddUniquePoint(result, new Point(first.X, first.Y, 0), 0.025);
                        AddUniquePoint(result, new Point(second.X, second.Y, 0), 0.025);
                    }
                    continue;
                }

                if (
                    targetY < Math.Min(first.Y, second.Y) - PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    || targetY > Math.Max(first.Y, second.Y) + PLATE_EDGE_FOOT_ALIGNMENT_TOL
                )
                {
                    continue;
                }

                double ratio = (targetY - first.Y) / deltaY;
                if (ratio < -0.001 || ratio > 1.001)
                    continue;

                ratio = Math.Max(0.0, Math.Min(1.0, ratio));
                AddUniquePoint(
                    result,
                    new Point(first.X + ratio * (second.X - first.X), targetY, 0),
                    0.025
                );
            }
        }

        private static void AddPlateContourIntersectionsAtX(
            List<Point> polygon,
            double targetX,
            List<Point> result
        )
        {
            if (polygon == null || polygon.Count < 2 || result == null)
                return;

            for (int i = 0; i < polygon.Count; i++)
            {
                Point first = polygon[i];
                Point second = polygon[(i + 1) % polygon.Count];
                if (first == null || second == null)
                    continue;

                double deltaX = second.X - first.X;
                if (Math.Abs(deltaX) <= PLATE_EDGE_FOOT_ALIGNMENT_TOL)
                {
                    if (
                        Math.Min(Math.Abs(first.X - targetX), Math.Abs(second.X - targetX))
                        <= PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    )
                    {
                        AddUniquePoint(result, new Point(first.X, first.Y, 0), 0.025);
                        AddUniquePoint(result, new Point(second.X, second.Y, 0), 0.025);
                    }
                    continue;
                }

                if (
                    targetX < Math.Min(first.X, second.X) - PLATE_EDGE_FOOT_ALIGNMENT_TOL
                    || targetX > Math.Max(first.X, second.X) + PLATE_EDGE_FOOT_ALIGNMENT_TOL
                )
                {
                    continue;
                }

                double ratio = (targetX - first.X) / deltaX;
                if (ratio < -0.001 || ratio > 1.001)
                    continue;

                ratio = Math.Max(0.0, Math.Min(1.0, ratio));
                AddUniquePoint(
                    result,
                    new Point(targetX, first.Y + ratio * (second.Y - first.Y), 0),
                    0.025
                );
            }
        }

        private static double GetBoltGroupPhiForDimGap(ModelBoltGroup bg)
        {
            if (bg == null)
                return 0.0;

            // PA6: Ưu tiên PHI LỖ thật trước.
            // Chỉ khi không lấy được phi lỗ mới fallback về M/BoltSize.
            double v = GetReportDouble(bg, "HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetReportDouble(bg, "BOLT_HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetReportDouble(bg, "HOLE_SIZE");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetDoublePropertyByReflection(bg, "HoleDiameter");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetDoublePropertyByReflection(bg, "HoleSize");
            if (v > 0.0 && v < 500.0)
                return v;

            // Một số môi trường Tekla trả phi qua DIAMETER/Diameter.
            v = GetReportDouble(bg, "DIAMETER");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetDoublePropertyByReflection(bg, "Diameter");
            if (v > 0.0 && v < 500.0)
                return v;

            // Fallback cuối cùng mới lấy theo M/BoltSize.
            v = GetReportDouble(bg, "BOLT_DIAMETER");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetDoublePropertyByReflection(bg, "BoltSize");
            if (v > 0.0 && v < 500.0)
                return v;

            v = GetReportDouble(bg, "BOLT_SIZE");
            if (v > 0.0 && v < 500.0)
                return v;

            return 0.0;
        }

        private static double GetReportDouble(ModelBoltGroup bg, string propertyName)
        {
            try
            {
                if (bg == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                double value = 0.0;
                bg.GetReportProperty(propertyName, ref value);
                return value;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetDoublePropertyByReflection(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return 0.0;

                object value = prop.GetValue(obj, null);
                if (value == null)
                    return 0.0;

                if (value is double)
                    return (double)value;

                if (value is int)
                    return Convert.ToDouble((int)value);

                if (value is float)
                    return Convert.ToDouble((float)value);

                double result;
                if (
                    double.TryParse(
                        value.ToString().Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out result
                    )
                )
                    return result;
            }
            catch { }

            return 0.0;
        }

        private static List<Point> GetBoltHoleCenters(
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            List<Point> result = new List<Point>();

            try
            {
                ModelObjectEnumerator bolts = part.GetBolts();

                while (bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null)
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (
                            p.X >= minX - 5.0
                            && p.X <= maxX + 5.0
                            && p.Y >= minY - 5.0
                            && p.Y <= maxY + 5.0
                        )
                        {
                            AddUniquePoint(result, new Point(p.X, p.Y, 0), 1.0);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private static void AddUnique(List<double> list, double value, double tol)
        {
            foreach (double v in list)
            {
                if (Math.Abs(v - value) <= tol)
                    return;
            }

            list.Add(value);
        }

        private static void AddUniquePoint(List<Point> list, Point p, double tol)
        {
            foreach (Point q in list)
            {
                if (Math.Abs(q.X - p.X) <= tol && Math.Abs(q.Y - p.Y) <= tol)
                    return;
            }

            list.Add(p);
        }

        #endregion

        #region 05A - THIN VIEW FILLET / CHAMFER FEATURE BASE

        private static List<Point> BuildExactProjectedOuterContour(
            Solid solid,
            List<List<Point>> boundaries
        )
        {
            List<Point> projected = GetProjectedSolidPointsForTotalDims(solid);
            List<Point> hull = BuildConvexHull2D(projected);

            if (hull.Count >= 3)
                return hull;

            List<Point> best = new List<Point>();
            double bestArea = 0.0;

            if (boundaries != null)
            {
                foreach (List<Point> boundary in boundaries)
                {
                    List<Point> normalized = NormalizeThinBoundaryPolygon(boundary);
                    double area = Math.Abs(GetSignedPolygonArea2(normalized));

                    if (normalized.Count >= 3 && area > bestArea)
                    {
                        best = normalized;
                        bestArea = area;
                    }
                }
            }

            return best;
        }

        private static List<Point> BuildConvexHull2D(List<Point> points)
        {
            List<Point> unique = new List<Point>();

            if (points == null)
                return unique;

            foreach (Point point in points)
            {
                if (
                    point == null
                    || Double.IsNaN(point.X)
                    || Double.IsInfinity(point.X)
                    || Double.IsNaN(point.Y)
                    || Double.IsInfinity(point.Y)
                )
                {
                    continue;
                }

                AddUniquePoint(unique, new Point(point.X, point.Y, 0), 0.05);
            }

            unique.Sort(
                delegate(Point first, Point second)
                {
                    int xCompare = first.X.CompareTo(second.X);
                    if (xCompare != 0)
                        return xCompare;
                    return first.Y.CompareTo(second.Y);
                }
            );

            if (unique.Count <= 2)
                return unique;

            List<Point> lower = new List<Point>();
            foreach (Point point in unique)
            {
                while (
                    lower.Count >= 2
                    && Cross2D(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0.0001
                )
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(point);
            }

            List<Point> upper = new List<Point>();
            for (int i = unique.Count - 1; i >= 0; i--)
            {
                Point point = unique[i];
                while (
                    upper.Count >= 2
                    && Cross2D(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0.0001
                )
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(point);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return SimplifyNearlyCollinearContour(lower, 0.1);
        }

        private static double Cross2D(Point origin, Point first, Point second)
        {
            return (first.X - origin.X) * (second.Y - origin.Y)
                - (first.Y - origin.Y) * (second.X - origin.X);
        }

        private static List<Point> SimplifyNearlyCollinearContour(
            List<Point> contour,
            double tolerance
        )
        {
            List<Point> result = contour == null ? new List<Point>() : new List<Point>(contour);

            bool changed = true;
            int guard = 0;
            while (changed && result.Count > 3 && guard < 1000)
            {
                changed = false;
                guard++;

                for (int i = 0; i < result.Count; i++)
                {
                    Point previous = result[(i - 1 + result.Count) % result.Count];
                    Point current = result[i];
                    Point next = result[(i + 1) % result.Count];
                    double segmentLength = Distance2D(previous, next);
                    if (segmentLength <= 0.05)
                        continue;

                    double perpendicularDistance =
                        Math.Abs(Cross2D(previous, next, current)) / segmentLength;
                    if (perpendicularDistance > tolerance)
                        continue;

                    double dot =
                        (current.X - previous.X) * (next.X - previous.X)
                        + (current.Y - previous.Y) * (next.Y - previous.Y);
                    double squaredLength = segmentLength * segmentLength;
                    if (dot < -0.01 || dot > squaredLength + 0.01)
                        continue;

                    result.RemoveAt(i);
                    changed = true;
                    break;
                }
            }

            return result;
        }

        private static int CreateThinViewExactPlateDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> contour
        )
        {
            int count = 0;

            if (handler == null || view == null || contour == null || contour.Count < 3)
                return count;

            double minX;
            double maxX;
            double minY;
            double maxY;
            GetMinMax(contour, out minX, out maxX, out minY, out maxY);

            double centerX = (minX + maxX) * 0.5;
            double centerY = (minY + maxY) * 0.5;
            bool hasLeftChamfer = false;
            bool hasRightChamfer = false;
            int chamferCount = 0;

            for (int i = 0; i < contour.Count; i++)
            {
                Point a = contour[i];
                Point b = contour[(i + 1) % contour.Count];
                if (!IsValidChamferSegment(a, b, minX, maxX, minY, maxY))
                    continue;

                chamferCount++;
                double midX = (a.X + b.X) * 0.5;
                if (midX <= centerX)
                    hasLeftChamfer = true;
                else
                    hasRightChamfer = true;
            }

            Point leftLengthFoot = FindExactExtremePoint(contour, minX, true);
            Point rightLengthFoot = FindExactExtremePoint(contour, maxX, true);

            if (chamferCount == 0)
            {
                if (
                    CreateDim(
                        handler,
                        view,
                        leftLengthFoot,
                        rightLengthFoot,
                        new Vector(0, 1, 0),
                        GetCleanDimOffsetByTier(1)
                    )
                )
                {
                    count++;
                }

                Point flatBottom;
                Point flatTop;
                if (
                    TryFindMaximumThicknessPair(
                        contour,
                        minY,
                        maxY,
                        false,
                        out flatBottom,
                        out flatTop
                    )
                    && CreateDim(
                        handler,
                        view,
                        flatBottom,
                        flatTop,
                        new Vector(-1, 0, 0),
                        GetCleanDimOffsetByTier(1)
                    )
                )
                {
                    count++;
                }

                return count;
            }

            // Mỗi đầu plate được phân loại độc lập:
            // - đầu có vát: DIM từng run vát + đoạn land thẳng còn lại;
            // - đầu thẳng: mới được DIM bề dày tổng.
            // Không dùng quy tắc cũ "detail bên trái / tổng bên phải" vì hai đầu có
            // thể cùng vát và khi đó DIM tổng không còn đại diện cho một cạnh thẳng.
            double chamferOffset = GetCleanDimOffsetByTier(1);

            for (int i = 0; i < contour.Count; i++)
            {
                Point a = contour[i];
                Point b = contour[(i + 1) % contour.Count];
                if (!IsValidChamferSegment(a, b, minX, maxX, minY, maxY))
                    continue;

                double midX = (a.X + b.X) * 0.5;
                bool isLeft = midX <= centerX;
                bool isTop = ResolveThinChamferTopSideFromInnerEndpoint(a, b, !isLeft, centerY);

                Point horizontalInner;
                Point horizontalOuter;
                OrderChamferFeetInnerToOuter(
                    a,
                    b,
                    true,
                    !isLeft,
                    isTop,
                    out horizontalInner,
                    out horizontalOuter
                );

                if (
                    CreateDim(
                        handler,
                        view,
                        horizontalInner,
                        horizontalOuter,
                        isTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                        chamferOffset
                    )
                )
                {
                    count++;
                }

                Point verticalInner;
                Point verticalOuter;
                OrderChamferFeetInnerToOuter(
                    a,
                    b,
                    false,
                    !isLeft,
                    isTop,
                    out verticalInner,
                    out verticalOuter
                );

                if (
                    CreateDim(
                        handler,
                        view,
                        verticalInner,
                        verticalOuter,
                        isLeft ? new Vector(-1, 0, 0) : new Vector(1, 0, 0),
                        chamferOffset
                    )
                )
                {
                    count++;
                }
            }

            Point landA;
            Point landB;
            if (
                hasLeftChamfer
                && TryFindExtremeVerticalLand(contour, minX, out landA, out landB)
                && CreateDim(
                    handler,
                    view,
                    landA,
                    landB,
                    new Vector(-1, 0, 0),
                    GetCleanDimOffsetByTier(2)
                )
            )
            {
                count++;
            }

            if (
                hasRightChamfer
                && TryFindExtremeVerticalLand(contour, maxX, out landA, out landB)
                && CreateDim(
                    handler,
                    view,
                    landA,
                    landB,
                    new Vector(1, 0, 0),
                    GetCleanDimOffsetByTier(2)
                )
            )
            {
                count++;
            }

            Point overallBottom;
            Point overallTop;
            bool overallOnRight;
            if (
                ShouldCreateThinOverallThickness(
                    hasLeftChamfer,
                    hasRightChamfer,
                    out overallOnRight
                )
                && TryFindMaximumThicknessPair(
                    contour,
                    minY,
                    maxY,
                    overallOnRight,
                    out overallBottom,
                    out overallTop
                )
                && CreateDim(
                    handler,
                    view,
                    overallBottom,
                    overallTop,
                    overallOnRight ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                    GetCleanDimOffsetByTier(2)
                )
            )
            {
                count++;
            }

            if (
                CreateDim(
                    handler,
                    view,
                    leftLengthFoot,
                    rightLengthFoot,
                    new Vector(0, 1, 0),
                    GetCleanDimOffsetByTier(3)
                )
            )
            {
                count++;
            }

            return count;
        }

        private static bool ShouldCreateThinOverallThickness(
            bool hasLeftChamfer,
            bool hasRightChamfer,
            out bool overallOnRight
        )
        {
            // Hai đầu cùng vát: mỗi đầu đã được phân rã bằng chính các endpoint
            // chamfer/land, nên tuyệt đối không thêm bề dày tổng ở đầu nào.
            if (hasLeftChamfer && hasRightChamfer)
            {
                overallOnRight = false;
                return false;
            }

            // Một đầu vát: DIM tổng chỉ được đặt ở đầu thẳng đối diện.
            if (hasLeftChamfer)
            {
                overallOnRight = true;
                return true;
            }

            overallOnRight = false;
            return true;
        }

        private static bool ResolveThinChamferTopSideFromInnerEndpoint(
            Point first,
            Point second,
            bool rightSide,
            double centerY
        )
        {
            if (first == null || second == null)
                return false;

            // Chamfer trái: endpoint có X lớn hơn là mép trong của thân plate.
            // Chamfer phải: endpoint có X nhỏ hơn là mép trong của thân plate.
            // Dùng endpoint này để xác định phía đặt DIM. Không dùng trung điểm:
            // chamfer xuyên hết độ dày luôn có midY == centerY và từng bị ép lên TOP.
            Point innerEndpoint;
            if (Math.Abs(first.X - second.X) <= THIN_CHAMFER_EDGE_TOL)
            {
                // Fail-safe cho dữ liệu suy biến; chamfer hợp lệ bình thường không
                // đi nhánh này vì luôn có horizontal run.
                return (first.Y + second.Y) * 0.5 >= centerY;
            }

            if (rightSide)
            {
                innerEndpoint = first.X <= second.X ? first : second;
            }
            else
            {
                innerEndpoint = first.X >= second.X ? first : second;
            }

            return innerEndpoint.Y >= centerY;
        }

        private static Point FindExactExtremePoint(
            List<Point> contour,
            double targetX,
            bool preferTop
        )
        {
            Point best = null;

            if (contour == null)
                return best;

            foreach (Point point in contour)
            {
                if (point == null || Math.Abs(point.X - targetX) > 0.1)
                    continue;

                if (
                    best == null
                    || (preferTop && point.Y > best.Y)
                    || (!preferTop && point.Y < best.Y)
                )
                {
                    best = point;
                }
            }

            return best == null ? null : new Point(best.X, best.Y, 0);
        }

        private static bool TryFindExtremeVerticalLand(
            List<Point> contour,
            double targetX,
            out Point first,
            out Point second
        )
        {
            first = null;
            second = null;
            double bestLength = 0.0;

            if (contour == null || contour.Count < 2)
                return false;

            for (int i = 0; i < contour.Count; i++)
            {
                Point a = contour[i];
                Point b = contour[(i + 1) % contour.Count];
                if (
                    a == null
                    || b == null
                    || Math.Abs(a.X - targetX) > 0.1
                    || Math.Abs(b.X - targetX) > 0.1
                )
                {
                    continue;
                }

                double length = Math.Abs(a.Y - b.Y);
                if (length > bestLength)
                {
                    bestLength = length;
                    first = new Point(a.X, a.Y, 0);
                    second = new Point(b.X, b.Y, 0);
                }
            }

            return first != null && second != null && bestLength >= 1.0;
        }

        private static bool TryFindMaximumThicknessPair(
            List<Point> contour,
            double minY,
            double maxY,
            bool preferRight,
            out Point bottom,
            out Point top
        )
        {
            bottom = null;
            top = null;
            double bestDeltaX = Double.MaxValue;
            double bestSideX = preferRight ? -Double.MaxValue : Double.MaxValue;

            if (contour == null)
                return false;

            foreach (Point bottomCandidate in contour)
            {
                if (bottomCandidate == null || Math.Abs(bottomCandidate.Y - minY) > 0.1)
                    continue;

                foreach (Point topCandidate in contour)
                {
                    if (topCandidate == null || Math.Abs(topCandidate.Y - maxY) > 0.1)
                        continue;

                    double deltaX = Math.Abs(bottomCandidate.X - topCandidate.X);
                    double sideX = (bottomCandidate.X + topCandidate.X) * 0.5;
                    bool better = deltaX < bestDeltaX - 0.05;

                    if (Math.Abs(deltaX - bestDeltaX) <= 0.05)
                    {
                        better = preferRight ? sideX > bestSideX : sideX < bestSideX;
                    }

                    if (!better)
                        continue;

                    bestDeltaX = deltaX;
                    bestSideX = sideX;
                    bottom = new Point(bottomCandidate.X, bottomCandidate.Y, 0);
                    top = new Point(topCandidate.X, topCandidate.Y, 0);
                }
            }

            return bottom != null && top != null;
        }

        private enum ThinBoundaryFeatureKind
        {
            Unknown = 0,
            FilletArc = 1,
            ChamferLine = 2
        }

        private sealed class ThinBoundaryFeature
        {
            public ThinBoundaryFeatureKind Kind;
            public bool IsLeftSide;
            public bool IsTopSide;
            public Point ArcPoint1;
            public Point ArcPoint2;
            public Point ArcPoint3;
            public Point Center;
            public double Radius;
            public double MaxRadiusResidual;
            public double BoundaryPathLength;
            public Point ChamferOrigin;
            public Point ChamferOtherPoint;

            // Giữ path đã được FilletArc nhận để chamfer sau này không xét lại các segment này.
            public List<Point> ClaimedBoundaryPath = new List<Point>();
        }

        private static double GetThinRadiusReferenceThickness(
            ModelPart part,
            double fallbackThickness
        )
        {
            try
            {
                if (part == null)
                    return fallbackThickness;

                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);

                double parsedThickness = GetMinimumThinRadiusProfileDimension(profile);
                if (parsedThickness > 0.0)
                    return parsedThickness;
            }
            catch { }

            return fallbackThickness;
        }

        private static double GetMinimumThinRadiusProfileDimension(string profile)
        {
            if (String.IsNullOrEmpty(profile))
                return 0.0;

            double minimum = Double.MaxValue;
            string numericToken = "";

            for (int i = 0; i <= profile.Length; i++)
            {
                char c = i < profile.Length ? profile[i] : '\0';

                if (Char.IsDigit(c) || c == '.' || c == ',')
                {
                    numericToken += c == ',' ? '.' : c;
                    continue;
                }

                if (numericToken.Length == 0)
                    continue;

                double value;
                if (
                    Double.TryParse(
                        numericToken,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value
                    )
                    && value > 0.0
                )
                {
                    minimum = Math.Min(minimum, value);
                }

                numericToken = "";
            }

            return minimum < Double.MaxValue ? minimum : 0.0;
        }

        private static List<List<Point>> GetThinViewIntersectionPolygons(
            Solid solid,
            Point min,
            Point max
        )
        {
            List<List<Point>> result = new List<List<Point>>();

            try
            {
                if (solid == null || min == null || max == null)
                    return result;

                double midZ = (min.Z + max.Z) / 2.0;

                Point planeP1 = new Point(min.X - 1000.0, min.Y - 1000.0, midZ);
                Point planeP2 = new Point(max.X + 1000.0, min.Y - 1000.0, midZ);
                Point planeP3 = new Point(min.X - 1000.0, max.Y + 1000.0, midZ);

                // Lấy outer contour đã được Tekla ghép kín trước. IntersectAllFaces phía dưới
                // vẫn được giữ làm nguồn bổ sung cho các solid/cut trả contour theo từng face.
                try
                {
#pragma warning disable 618
                    ArrayList intersectionPolygons = solid.Intersect(planeP1, planeP2, planeP3);
#pragma warning restore 618
                    CollectPointLists(intersectionPolygons, result, 0);
                }
                catch { }

                IEnumerator intersections = solid.IntersectAllFaces(planeP1, planeP2, planeP3);

                while (intersections != null && intersections.MoveNext())
                    CollectPointLists(intersections.Current, result, 0);
            }
            catch { }

            return result;
        }

        private static int CreateThinViewBoundaryFeatureDims(
            View view,
            Solid solid,
            List<List<Point>> boundaries,
            Point min,
            Point max
        )
        {
            int count = 0;

            try
            {
                List<ThinBoundaryFeature> features = CollectThinViewBoundaryFeatures(
                    solid,
                    boundaries,
                    min,
                    max
                );

                foreach (ThinBoundaryFeature feature in features)
                {
                    if (feature == null)
                        continue;

                    if (
                        feature.Kind == ThinBoundaryFeatureKind.FilletArc
                        && AUTO_DIM_THIN_VIEW_FILLET_RADIUS
                    )
                    {
                        double distance = GetThinFilletDimensionDistance(
                            feature.IsLeftSide,
                            feature.IsTopSide
                        );

                        if (
                            CreateThinViewRadiusDimByReflection(
                                view,
                                feature.ArcPoint1,
                                feature.ArcPoint2,
                                feature.ArcPoint3,
                                distance
                            )
                        )
                        {
                            count++;
                        }
                    }

                    if (
                        feature.Kind == ThinBoundaryFeatureKind.ChamferLine
                        && AUTO_DIM_THIN_VIEW_CHAMFER_ANGLE
                        && CreateThinViewChamferAngleDimension(view, feature)
                    )
                    {
                        count++;
                    }
                }
            }
            catch { }

            return count;
        }

        private static List<ThinBoundaryFeature> CollectThinViewBoundaryFeatures(
            Solid solid,
            List<List<Point>> boundaries,
            Point min,
            Point max
        )
        {
            List<ThinBoundaryFeature> result = new List<ThinBoundaryFeature>();

            try
            {
                if (solid == null || boundaries == null || min == null || max == null)
                    return result;

                List<Tekla.Structures.Solid.Edge> curvedEdges =
                    new List<Tekla.Structures.Solid.Edge>();

                try
                {
                    Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();

                    while (edges != null && edges.MoveNext())
                    {
                        Tekla.Structures.Solid.Edge edge =
                            edges.Current as Tekla.Structures.Solid.Edge;

                        if (!IsCurvedSolidEdge(edge))
                            continue;

                        curvedEdges.Add(edge);
                    }
                }
                catch { }

                foreach (List<Point> boundary in boundaries)
                {
                    List<Point> orderedBoundary = NormalizeThinBoundaryPolygon(boundary);
                    if (orderedBoundary.Count < 3)
                        continue;

                    foreach (Tekla.Structures.Solid.Edge edge in curvedEdges)
                    {
                        if (edge == null)
                            continue;

                        Point start = edge.StartPoint;
                        Point end = edge.EndPoint;

                        if (start == null || end == null)
                            continue;

                        Point start2D = new Point(start.X, start.Y, 0);
                        Point end2D = new Point(end.X, end.Y, 0);

                        if (Distance2D(start2D, end2D) < THIN_FILLET_MIN_CHORD)
                            continue;

                        ThinBoundaryFeature feature;
                        if (
                            !TryBuildThinFilletFeature(
                                orderedBoundary,
                                start2D,
                                end2D,
                                min.X,
                                max.X,
                                min.Y,
                                max.Y,
                                out feature
                            )
                        )
                        {
                            continue;
                        }

                        AddUniqueThinBoundaryFeature(result, feature);
                    }

                    // Fallback cho polycurve Tekla tách thành nhiều cạnh NORMAL:
                    // chỉ nhận khi cả chuỗi biên cùng nằm trên một đường tròn.
                    // Chamfer thẳng vẫn bị loại bởi path >= 3, sagitta và circle residual.
                    ThinBoundaryFeature leftBoundaryFeature;
                    if (
                        !HasThinFilletFeatureOnSide(result, true)
                        && TryBuildThinFilletFeatureFromBoundarySide(
                            orderedBoundary,
                            true,
                            min.X,
                            max.X,
                            min.Y,
                            max.Y,
                            out leftBoundaryFeature
                        )
                    )
                    {
                        AddUniqueThinBoundaryFeature(result, leftBoundaryFeature);
                    }

                    ThinBoundaryFeature rightBoundaryFeature;
                    if (
                        !HasThinFilletFeatureOnSide(result, false)
                        && TryBuildThinFilletFeatureFromBoundarySide(
                            orderedBoundary,
                            false,
                            min.X,
                            max.X,
                            min.Y,
                            max.Y,
                            out rightBoundaryFeature
                        )
                    )
                    {
                        AddUniqueThinBoundaryFeature(result, rightBoundaryFeature);
                    }
                }

                // Fallback cuối cho trường hợp API chỉ trả hai endpoint hoặc chia cung ra
                // nhiều list: lấy 5 điểm silhouette trực tiếp từ Solid ở mặt cắt giữa.
                ThinBoundaryFeature leftSampledFeature;
                if (
                    !HasThinFilletFeatureOnSide(result, true)
                    && TryBuildThinFilletFeatureFromSolidSideSamples(
                        solid,
                        true,
                        min,
                        max,
                        out leftSampledFeature
                    )
                )
                {
                    AddUniqueThinBoundaryFeature(result, leftSampledFeature);
                }

                ThinBoundaryFeature rightSampledFeature;
                if (
                    !HasThinFilletFeatureOnSide(result, false)
                    && TryBuildThinFilletFeatureFromSolidSideSamples(
                        solid,
                        false,
                        min,
                        max,
                        out rightSampledFeature
                    )
                )
                {
                    AddUniqueThinBoundaryFeature(result, rightSampledFeature);
                }

                // Chỉ quét chamfer sau khi toàn bộ fillet đã được nhận dạng.
                foreach (List<Point> boundary in boundaries)
                {
                    List<Point> orderedBoundary = NormalizeThinBoundaryPolygon(boundary);
                    if (orderedBoundary.Count < 3)
                        continue;

                    for (int i = 0; i < orderedBoundary.Count; i++)
                    {
                        ThinBoundaryFeature chamferFeature;
                        if (
                            TryBuildThinChamferFeature(
                                orderedBoundary,
                                i,
                                curvedEdges,
                                result,
                                min.X,
                                max.X,
                                min.Y,
                                max.Y,
                                out chamferFeature
                            )
                        )
                        {
                            AddUniqueThinBoundaryFeature(result, chamferFeature);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private static bool HasThinFilletFeatureOnSide(
            List<ThinBoundaryFeature> features,
            bool isLeftSide
        )
        {
            if (features == null)
                return false;

            foreach (ThinBoundaryFeature feature in features)
            {
                if (
                    feature != null
                    && feature.Kind == ThinBoundaryFeatureKind.FilletArc
                    && feature.IsLeftSide == isLeftSide
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildThinChamferFeature(
            List<Point> boundary,
            int segmentIndex,
            List<Tekla.Structures.Solid.Edge> curvedEdges,
            List<ThinBoundaryFeature> existingFeatures,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out ThinBoundaryFeature feature
        )
        {
            feature = null;

            if (
                boundary == null
                || boundary.Count < 3
                || segmentIndex < 0
                || segmentIndex >= boundary.Count
            )
            {
                return false;
            }

            int count = boundary.Count;
            Point a = boundary[segmentIndex];
            Point b = boundary[(segmentIndex + 1) % count];

            if (a == null || b == null)
                return false;

            double dx = Math.Abs(a.X - b.X);
            double dy = Math.Abs(a.Y - b.Y);
            double height = Math.Abs(maxY - minY);

            if (
                dx < THIN_CHAMFER_MIN_RUN
                || dx >= THIN_CHAMFER_MAX_RUN
                || dy <= THIN_CHAMFER_EDGE_TOL
                || dy >= THIN_CHAMFER_MAX_RUN
                || height <= THIN_CHAMFER_EDGE_TOL * 2.0
            )
            {
                return false;
            }

            double segmentMinX = Math.Min(a.X, b.X);
            double segmentMaxX = Math.Max(a.X, b.X);
            bool isLeftSide = Math.Abs(segmentMinX - minX) <= THIN_CHAMFER_EDGE_TOL;
            bool isRightSide = Math.Abs(segmentMaxX - maxX) <= THIN_CHAMFER_EDGE_TOL;

            if (isLeftSide == isRightSide)
                return false;

            bool originIsA = isLeftSide ? a.X <= b.X : a.X >= b.X;
            Point origin = originIsA ? a : b;
            Point other = originIsA ? b : a;

            if (Math.Abs(origin.X - (isLeftSide ? minX : maxX)) > THIN_CHAMFER_EDGE_TOL)
            {
                return false;
            }

            Point beforeA = boundary[(segmentIndex - 1 + count) % count];
            Point afterB = boundary[(segmentIndex + 2) % count];
            Point originNeighbor = originIsA ? beforeA : afterB;
            Point otherNeighbor = originIsA ? afterB : beforeA;

            bool otherOnMinY = Math.Abs(other.Y - minY) <= THIN_CHAMFER_EDGE_TOL;
            bool otherOnMaxY = Math.Abs(other.Y - maxY) <= THIN_CHAMFER_EDGE_TOL;
            if (!(otherOnMinY || otherOnMaxY))
                return false;

            // Full-depth bevel: neighbor tại origin có thể là cạnh ngang của body.
            // Partial-depth bevel: neighbor tại origin là đoạn land đứng còn lại.
            bool validOriginNeighbor =
                IsThinChamferHorizontalBodyNeighbor(origin, originNeighbor, isLeftSide)
                || IsThinChamferVerticalLandNeighbor(
                    origin,
                    originNeighbor,
                    isLeftSide ? minX : maxX
                );

            if (
                !validOriginNeighbor
                || !IsThinChamferHorizontalBodyNeighbor(other, otherNeighbor, isLeftSide)
            )
            {
                return false;
            }

            if (
                MatchesThinChamferCurvedEdge(a, b, curvedEdges)
                || HasThinFilletFeatureOnSide(existingFeatures, isLeftSide)
            )
            {
                return false;
            }

            feature = new ThinBoundaryFeature();
            feature.Kind = ThinBoundaryFeatureKind.ChamferLine;
            feature.IsLeftSide = isLeftSide;
            feature.IsTopSide = otherOnMaxY;
            feature.ChamferOrigin = new Point(origin.X, origin.Y, 0);
            feature.ChamferOtherPoint = new Point(other.X, other.Y, 0);
            feature.BoundaryPathLength = Distance2D(origin, other);
            feature.ClaimedBoundaryPath.Add(new Point(origin.X, origin.Y, 0));
            feature.ClaimedBoundaryPath.Add(new Point(other.X, other.Y, 0));

            return true;
        }

        private static bool IsThinChamferHorizontalBodyNeighbor(
            Point endpoint,
            Point neighbor,
            bool isLeftSide
        )
        {
            if (
                endpoint == null
                || neighbor == null
                || Math.Abs(endpoint.Y - neighbor.Y) > THIN_CHAMFER_EDGE_TOL
            )
            {
                return false;
            }

            return isLeftSide ? neighbor.X > endpoint.X + 0.05 : neighbor.X < endpoint.X - 0.05;
        }

        private static bool IsThinChamferVerticalLandNeighbor(
            Point endpoint,
            Point neighbor,
            double sideX
        )
        {
            if (endpoint == null || neighbor == null)
                return false;

            return Math.Abs(endpoint.X - sideX) <= THIN_CHAMFER_EDGE_TOL
                && Math.Abs(neighbor.X - sideX) <= THIN_CHAMFER_EDGE_TOL
                && Math.Abs(endpoint.Y - neighbor.Y) > 0.05;
        }

        private static bool MatchesThinChamferCurvedEdge(
            Point a,
            Point b,
            List<Tekla.Structures.Solid.Edge> curvedEdges
        )
        {
            if (a == null || b == null || curvedEdges == null)
                return false;

            foreach (Tekla.Structures.Solid.Edge edge in curvedEdges)
            {
                try
                {
                    if (edge == null || edge.StartPoint == null || edge.EndPoint == null)
                        continue;

                    Point start = new Point(edge.StartPoint.X, edge.StartPoint.Y, 0);
                    Point end = new Point(edge.EndPoint.X, edge.EndPoint.Y, 0);

                    bool direct =
                        Distance2D(a, start) <= THIN_FILLET_ENDPOINT_MATCH_TOL
                        && Distance2D(b, end) <= THIN_FILLET_ENDPOINT_MATCH_TOL;
                    bool reverse =
                        Distance2D(a, end) <= THIN_FILLET_ENDPOINT_MATCH_TOL
                        && Distance2D(b, start) <= THIN_FILLET_ENDPOINT_MATCH_TOL;

                    if (direct || reverse)
                        return true;
                }
                catch { }
            }

            return false;
        }

        private static bool TryBuildThinFilletFeatureFromBoundarySide(
            List<Point> boundary,
            bool isLeftSide,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out ThinBoundaryFeature feature
        )
        {
            feature = null;

            if (boundary == null || boundary.Count < 3)
                return false;

            double yBand = Math.Max(0.25, Math.Abs(maxY - minY) * 0.05);
            int topIndex = FindThinBoundaryOuterPointIndex(boundary, isLeftSide, maxY, yBand);
            int bottomIndex = FindThinBoundaryOuterPointIndex(boundary, isLeftSide, minY, yBand);

            if (topIndex < 0 || bottomIndex < 0 || topIndex == bottomIndex)
                return false;

            ThinBoundaryFeature candidate;
            if (
                !TryBuildThinFilletFeature(
                    boundary,
                    boundary[topIndex],
                    boundary[bottomIndex],
                    minX,
                    maxX,
                    minY,
                    maxY,
                    out candidate
                )
            )
            {
                return false;
            }

            if (
                candidate == null
                || candidate.ClaimedBoundaryPath == null
                || candidate.ClaimedBoundaryPath.Count < 4
            )
            {
                return false;
            }

            feature = candidate;
            return true;
        }

        private static int FindThinBoundaryOuterPointIndex(
            List<Point> boundary,
            bool isLeftSide,
            double targetY,
            double yBand
        )
        {
            int bestIndex = -1;
            double bestX = isLeftSide ? Double.MaxValue : Double.MinValue;

            if (boundary == null)
                return bestIndex;

            for (int i = 0; i < boundary.Count; i++)
            {
                Point point = boundary[i];
                if (point == null || Math.Abs(point.Y - targetY) > yBand)
                    continue;

                if (
                    bestIndex < 0
                    || (isLeftSide && point.X < bestX)
                    || (!isLeftSide && point.X > bestX)
                )
                {
                    bestIndex = i;
                    bestX = point.X;
                }
            }

            return bestIndex;
        }

        private static bool TryBuildThinFilletFeatureFromSolidSideSamples(
            Solid solid,
            bool isLeftSide,
            Point min,
            Point max,
            out ThinBoundaryFeature feature
        )
        {
            feature = null;

            if (solid == null || min == null || max == null)
                return false;

            double height = Math.Abs(max.Y - min.Y);
            if (height <= 0.000001)
                return false;

            double midZ = (min.Z + max.Z) / 2.0;
            double[] fractions = { 0.02, 0.25, 0.50, 0.75, 0.98 };
            List<Point> sampledPath = new List<Point>();

            foreach (double fraction in fractions)
            {
                double y = min.Y + (max.Y - min.Y) * fraction;
                Point silhouettePoint;

                if (
                    !TryGetThinSolidSideIntersectionPoint(
                        solid,
                        isLeftSide,
                        min.X,
                        max.X,
                        y,
                        midZ,
                        out silhouettePoint
                    )
                )
                {
                    return false;
                }

                sampledPath.Add(silhouettePoint);
            }

            if (!TryEvaluateThinFilletPath(sampledPath, min.X, max.X, min.Y, max.Y, out feature))
            {
                return false;
            }

            return feature != null && feature.IsLeftSide == isLeftSide;
        }

        private static bool TryGetThinSolidSideIntersectionPoint(
            Solid solid,
            bool isLeftSide,
            double minX,
            double maxX,
            double y,
            double z,
            out Point result
        )
        {
            result = null;

            if (solid == null)
                return false;

            ArrayList intersections;

            try
            {
                double extension = Math.Max(1000.0, Math.Abs(maxX - minX) + 200.0);
                intersections = solid.Intersect(
                    new Point(minX - extension, y, z),
                    new Point(maxX + extension, y, z)
                );
            }
            catch
            {
                return false;
            }

            if (intersections == null || intersections.Count == 0)
                return false;

            Point best = null;

            foreach (object item in intersections)
            {
                Point point = item as Point;
                if (point == null)
                    continue;

                if (
                    best == null
                    || (isLeftSide && point.X < best.X)
                    || (!isLeftSide && point.X > best.X)
                )
                {
                    best = point;
                }
            }

            if (best == null)
                return false;

            result = new Point(best.X, best.Y, 0);
            return true;
        }

        private static bool IsCurvedSolidEdge(Tekla.Structures.Solid.Edge edge)
        {
            try
            {
                if (edge == null)
                    return false;

                string edgeType = edge.Type.ToString();
                return edgeType.IndexOf("CURVED_SURFACE", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static List<Point> NormalizeThinBoundaryPolygon(List<Point> boundary)
        {
            List<Point> result = new List<Point>();

            if (boundary == null)
                return result;

            foreach (Point p in boundary)
            {
                if (
                    p == null
                    || Double.IsNaN(p.X)
                    || Double.IsInfinity(p.X)
                    || Double.IsNaN(p.Y)
                    || Double.IsInfinity(p.Y)
                )
                {
                    continue;
                }

                Point copy = new Point(p.X, p.Y, 0);
                if (result.Count == 0 || Distance2D(result[result.Count - 1], copy) > 0.05)
                {
                    result.Add(copy);
                }
            }

            if (result.Count > 1 && Distance2D(result[0], result[result.Count - 1]) <= 0.05)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (GetSignedPolygonArea2(result) > 0.0)
                result.Reverse();

            return result;
        }

        private static double GetSignedPolygonArea2(List<Point> points)
        {
            double area2 = 0.0;

            if (points == null || points.Count < 3)
                return area2;

            for (int i = 0; i < points.Count; i++)
            {
                Point a = points[i];
                Point b = points[(i + 1) % points.Count];
                area2 += a.X * b.Y - b.X * a.Y;
            }

            return area2;
        }

        private static bool TryBuildThinFilletFeature(
            List<Point> boundary,
            Point curvedStart,
            Point curvedEnd,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out ThinBoundaryFeature feature
        )
        {
            feature = null;

            int startIndex;
            int endIndex;
            double startDistance;
            double endDistance;

            startIndex = FindNearestThinBoundaryPointIndex(
                boundary,
                curvedStart,
                out startDistance
            );

            endIndex = FindNearestThinBoundaryPointIndex(boundary, curvedEnd, out endDistance);

            if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
                return false;

            if (
                startDistance > THIN_FILLET_ENDPOINT_MATCH_TOL
                || endDistance > THIN_FILLET_ENDPOINT_MATCH_TOL
            )
            {
                return false;
            }

            List<Point> forwardPath = BuildThinBoundaryPath(boundary, startIndex, endIndex, 1);

            List<Point> backwardPath = BuildThinBoundaryPath(boundary, startIndex, endIndex, -1);

            ThinBoundaryFeature forwardFeature;
            ThinBoundaryFeature backwardFeature;

            bool forwardOk = TryEvaluateThinFilletPath(
                forwardPath,
                minX,
                maxX,
                minY,
                maxY,
                out forwardFeature
            );

            bool backwardOk = TryEvaluateThinFilletPath(
                backwardPath,
                minX,
                maxX,
                minY,
                maxY,
                out backwardFeature
            );

            if (!forwardOk && !backwardOk)
                return false;

            if (forwardOk && backwardOk)
            {
                feature =
                    forwardFeature.BoundaryPathLength <= backwardFeature.BoundaryPathLength
                        ? forwardFeature
                        : backwardFeature;
            }
            else
            {
                feature = forwardOk ? forwardFeature : backwardFeature;
            }

            Point dimensionMiddle = GetThinBoundaryDimensionSegmentMidpoint(feature);
            if (dimensionMiddle != null)
                feature.ArcPoint2 = dimensionMiddle;

            return feature != null;
        }

        private static Point GetThinBoundaryDimensionSegmentMidpoint(ThinBoundaryFeature feature)
        {
            if (
                feature == null
                || feature.ArcPoint2 == null
                || feature.ClaimedBoundaryPath == null
                || feature.ClaimedBoundaryPath.Count < 4
            )
            {
                return null;
            }

            List<Point> path = feature.ClaimedBoundaryPath;
            double pathLength = 0.0;

            for (int i = 1; i < path.Count; i++)
            {
                if (path[i - 1] == null || path[i] == null)
                    return null;

                pathLength += Distance2D(path[i - 1], path[i]);
            }

            if (pathLength <= 0.05)
                return null;

            double halfPathLength = pathLength / 2.0;
            double walkedLength = 0.0;

            for (int i = 1; i < path.Count; i++)
            {
                Point segmentStart = path[i - 1];
                Point segmentEnd = path[i];
                double segmentLength = Distance2D(segmentStart, segmentEnd);

                if (segmentLength <= 0.05)
                {
                    walkedLength += segmentLength;
                    continue;
                }

                double nextWalkedLength = walkedLength + segmentLength;

                if (
                    halfPathLength > walkedLength + 0.05
                    && halfPathLength < nextWalkedLength - 0.05
                )
                {
                    if (i < 2 || i > path.Count - 2)
                        return null;

                    bool touchesCurrentMiddle =
                        Distance2D(feature.ArcPoint2, segmentStart) <= 0.05
                        || Distance2D(feature.ArcPoint2, segmentEnd) <= 0.05;

                    if (!touchesCurrentMiddle)
                        return null;

                    return new Point(
                        (segmentStart.X + segmentEnd.X) / 2.0,
                        (segmentStart.Y + segmentEnd.Y) / 2.0,
                        0
                    );
                }

                walkedLength = nextWalkedLength;
            }

            return null;
        }

        private static int FindNearestThinBoundaryPointIndex(
            List<Point> boundary,
            Point target,
            out double bestDistance
        )
        {
            int bestIndex = -1;
            bestDistance = Double.MaxValue;

            if (boundary == null || target == null)
                return bestIndex;

            for (int i = 0; i < boundary.Count; i++)
            {
                Point p = boundary[i];
                if (p == null)
                    continue;

                double distance = Distance2D(p, target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<Point> BuildThinBoundaryPath(
            List<Point> boundary,
            int startIndex,
            int endIndex,
            int direction
        )
        {
            List<Point> path = new List<Point>();

            if (boundary == null || boundary.Count == 0)
                return path;

            int count = boundary.Count;
            int index = startIndex;
            int guard = 0;

            while (guard <= count)
            {
                guard++;

                Point p = boundary[index];
                path.Add(new Point(p.X, p.Y, 0));

                if (index == endIndex)
                    break;

                index = (index + direction + count) % count;
            }

            return path;
        }

        private static bool TryEvaluateThinFilletPath(
            List<Point> path,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out ThinBoundaryFeature feature
        )
        {
            feature = null;

            if (path == null || path.Count < 3)
                return false;

            Point first = path[0];
            Point last = path[path.Count - 1];
            double chord = Distance2D(first, last);

            if (chord < THIN_FILLET_MIN_CHORD)
                return false;

            double pathLength = 0.0;
            for (int i = 1; i < path.Count; i++)
                pathLength += Distance2D(path[i - 1], path[i]);

            if (pathLength <= chord || pathLength > chord * THIN_FILLET_MAX_PATH_CHORD_RATIO)
            {
                return false;
            }

            Point arcMiddle = null;
            double maxSagitta = 0.0;

            for (int i = 1; i < path.Count - 1; i++)
            {
                double sagitta = DistancePointToInfiniteLine2D(path[i], first, last);
                if (sagitta > maxSagitta)
                {
                    maxSagitta = sagitta;
                    arcMiddle = path[i];
                }
            }

            if (arcMiddle == null || maxSagitta / chord < THIN_FILLET_MIN_SAGITTA_CHORD_RATIO)
            {
                return false;
            }

            Point center;
            double radius;
            if (!TryFitCircleThroughThreePoints(first, arcMiddle, last, out center, out radius))
                return false;

            if (radius < 0.5 || Double.IsNaN(radius) || Double.IsInfinity(radius))
                return false;

            double residualTolerance = Math.Max(
                THIN_FILLET_RADIUS_RESIDUAL_MIN_TOL,
                radius * THIN_FILLET_RADIUS_RESIDUAL_RATIO
            );

            double maxRadiusResidual = 0.0;

            foreach (Point p in path)
            {
                double residual = Math.Abs(Distance2D(p, center) - radius);
                maxRadiusResidual = Math.Max(maxRadiusResidual, residual);

                if (residual > residualTolerance)
                    return false;
            }

            if (!HasConsistentThinCurveTurn(path))
                return false;

            double sweepDeg = GetArcSweepDegreesThroughPoint(first, arcMiddle, last, center);
            if (sweepDeg < THIN_FILLET_MIN_SWEEP_DEG || sweepDeg > THIN_FILLET_MAX_SWEEP_DEG)
            {
                return false;
            }

            double edgeTol = Math.Max(1.0, Math.Abs(maxY - minY) * 0.20);
            double pathMinX = Double.MaxValue;
            double pathMaxX = Double.MinValue;

            foreach (Point p in path)
            {
                pathMinX = Math.Min(pathMinX, p.X);
                pathMaxX = Math.Max(pathMaxX, p.X);
            }

            bool touchesLeft = Math.Abs(pathMinX - minX) <= edgeTol;
            bool touchesRight = Math.Abs(pathMaxX - maxX) <= edgeTol;

            if (!touchesLeft && !touchesRight)
                return false;

            bool isLeftSide;
            if (touchesLeft && touchesRight)
                isLeftSide = arcMiddle.X <= (minX + maxX) / 2.0;
            else
                isLeftSide = touchesLeft;

            if (isLeftSide)
            {
                if (center.X <= minX)
                    return false;
            }
            else
            {
                if (center.X >= maxX)
                    return false;
            }

            Point topEndpoint = first.Y >= last.Y ? first : last;
            Point bottomEndpoint = first.Y >= last.Y ? last : first;

            if (
                Math.Abs(topEndpoint.Y - maxY) > edgeTol
                || Math.Abs(bottomEndpoint.Y - minY) > edgeTol
            )
            {
                return false;
            }

            double topTangentError = Math.Abs(topEndpoint.X - center.X) / radius;
            double bottomTangentError = Math.Abs(bottomEndpoint.X - center.X) / radius;

            bool isTopSide = topTangentError <= bottomTangentError;
            double selectedTangentError = isTopSide ? topTangentError : bottomTangentError;

            if (selectedTangentError > THIN_FILLET_TANGENT_SIN_TOL)
                return false;

            if (isTopSide)
            {
                if (center.Y >= topEndpoint.Y)
                    return false;
            }
            else
            {
                if (center.Y <= bottomEndpoint.Y)
                    return false;
            }

            Point arc1 = new Point(first.X, first.Y, 0);
            Point arc2 = new Point(arcMiddle.X, arcMiddle.Y, 0);
            Point arc3 = new Point(last.X, last.Y, 0);

            NormalizeThinFilletArcPointOrder(ref arc1, arc2, ref arc3, isLeftSide, isTopSide);

            feature = new ThinBoundaryFeature();
            feature.Kind = ThinBoundaryFeatureKind.FilletArc;
            feature.IsLeftSide = isLeftSide;
            feature.IsTopSide = isTopSide;
            feature.ArcPoint1 = arc1;
            feature.ArcPoint2 = arc2;
            feature.ArcPoint3 = arc3;
            feature.Center = center;
            feature.Radius = radius;
            feature.MaxRadiusResidual = maxRadiusResidual;
            feature.BoundaryPathLength = pathLength;

            foreach (Point p in path)
                feature.ClaimedBoundaryPath.Add(new Point(p.X, p.Y, 0));

            return true;
        }

        private static double DistancePointToInfiniteLine2D(Point p, Point lineStart, Point lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= 0.000001)
                return 0.0;

            double cross = dx * (lineStart.Y - p.Y) - (lineStart.X - p.X) * dy;

            return Math.Abs(cross) / length;
        }

        private static bool TryFitCircleThroughThreePoints(
            Point p1,
            Point p2,
            Point p3,
            out Point center,
            out double radius
        )
        {
            center = null;
            radius = 0.0;

            double d = 2.0 * (p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y));

            if (Math.Abs(d) <= 0.000001)
                return false;

            double p1Sq = p1.X * p1.X + p1.Y * p1.Y;
            double p2Sq = p2.X * p2.X + p2.Y * p2.Y;
            double p3Sq = p3.X * p3.X + p3.Y * p3.Y;

            double centerX =
                (p1Sq * (p2.Y - p3.Y) + p2Sq * (p3.Y - p1.Y) + p3Sq * (p1.Y - p2.Y)) / d;

            double centerY =
                (p1Sq * (p3.X - p2.X) + p2Sq * (p1.X - p3.X) + p3Sq * (p2.X - p1.X)) / d;

            if (
                Double.IsNaN(centerX)
                || Double.IsInfinity(centerX)
                || Double.IsNaN(centerY)
                || Double.IsInfinity(centerY)
            )
            {
                return false;
            }

            center = new Point(centerX, centerY, 0);
            radius = Distance2D(center, p1);

            return radius > 0.0;
        }

        private static bool HasConsistentThinCurveTurn(List<Point> path)
        {
            int turnSign = 0;
            int meaningfulTurns = 0;

            for (int i = 1; i < path.Count - 1; i++)
            {
                double ax = path[i].X - path[i - 1].X;
                double ay = path[i].Y - path[i - 1].Y;
                double bx = path[i + 1].X - path[i].X;
                double by = path[i + 1].Y - path[i].Y;

                double lenA = Math.Sqrt(ax * ax + ay * ay);
                double lenB = Math.Sqrt(bx * bx + by * by);
                if (lenA <= 0.000001 || lenB <= 0.000001)
                    continue;

                double normalizedCross = (ax * by - ay * bx) / (lenA * lenB);
                if (Math.Abs(normalizedCross) <= 0.001)
                    continue;

                int currentSign = normalizedCross > 0.0 ? 1 : -1;
                if (turnSign != 0 && currentSign != turnSign)
                    return false;

                turnSign = currentSign;
                meaningfulTurns++;
            }

            return meaningfulTurns > 0;
        }

        private static double GetArcSweepDegreesThroughPoint(
            Point start,
            Point middle,
            Point end,
            Point center
        )
        {
            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double middleAngle = Math.Atan2(middle.Y - center.Y, middle.X - center.X);
            double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);

            double ccwStartToEnd = NormalizePositiveAngle(endAngle - startAngle);
            double ccwStartToMiddle = NormalizePositiveAngle(middleAngle - startAngle);
            double sweepRadians =
                ccwStartToMiddle <= ccwStartToEnd + 0.000001
                    ? ccwStartToEnd
                    : 2.0 * Math.PI - ccwStartToEnd;

            return sweepRadians * 180.0 / Math.PI;
        }

        private static double NormalizePositiveAngle(double angle)
        {
            double fullTurn = 2.0 * Math.PI;
            angle %= fullTurn;

            if (angle < 0.0)
                angle += fullTurn;

            return angle;
        }

        private static void NormalizeThinFilletArcPointOrder(
            ref Point arc1,
            Point arc2,
            ref Point arc3,
            bool isLeftSide,
            bool isTopSide
        )
        {
            bool swap = false;

            if (Math.Abs(arc1.X - arc3.X) > 0.05)
            {
                if (isTopSide)
                    swap = arc1.X > arc3.X;
                else
                    swap = arc1.X < arc3.X;
            }
            else
            {
                // Fallback cho cung gần bán nguyệt có 2 endpoint cùng X.
                if (isLeftSide)
                    swap = arc1.Y > arc3.Y;
                else
                    swap = arc1.Y < arc3.Y;
            }

            if (swap)
            {
                Point temp = arc1;
                arc1 = arc3;
                arc3 = temp;
            }

            double cross =
                (arc2.X - arc1.X) * (arc3.Y - arc1.Y) - (arc2.Y - arc1.Y) * (arc3.X - arc1.X);

            // Bốn DIM mẫu đều dùng thứ tự clockwise (cross < 0).
            if (cross > 0.0)
            {
                Point temp = arc1;
                arc1 = arc3;
                arc3 = temp;
            }
        }

        private static double GetThinFilletDimensionDistance(bool isLeftSide, bool isTopSide)
        {
            if (isLeftSide)
            {
                return isTopSide ? THIN_FILLET_DISTANCE_LEFT_TOP : THIN_FILLET_DISTANCE_LEFT_BOTTOM;
            }

            return isTopSide ? THIN_FILLET_DISTANCE_RIGHT_TOP : THIN_FILLET_DISTANCE_RIGHT_BOTTOM;
        }

        private static double GetThinChamferDimensionDistance(bool isLeftSide, bool isTopSide)
        {
            if (isLeftSide)
            {
                return isTopSide
                    ? THIN_CHAMFER_DISTANCE_LEFT_TOP
                    : THIN_CHAMFER_DISTANCE_LEFT_BOTTOM;
            }

            return isTopSide ? THIN_CHAMFER_DISTANCE_RIGHT_TOP : THIN_CHAMFER_DISTANCE_RIGHT_BOTTOM;
        }

        private static double GetThinChamferVerticalRayLength(bool isLeftSide, bool isTopSide)
        {
            if (isLeftSide)
            {
                return isTopSide
                    ? THIN_CHAMFER_RAY_LENGTH_LEFT_TOP
                    : THIN_CHAMFER_RAY_LENGTH_LEFT_BOTTOM;
            }

            return isTopSide
                ? THIN_CHAMFER_RAY_LENGTH_RIGHT_TOP
                : THIN_CHAMFER_RAY_LENGTH_RIGHT_BOTTOM;
        }

        private static void AddUniqueThinBoundaryFeature(
            List<ThinBoundaryFeature> features,
            ThinBoundaryFeature candidate
        )
        {
            if (features == null || candidate == null)
                return;

            foreach (ThinBoundaryFeature existing in features)
            {
                if (existing == null || existing.Kind != candidate.Kind)
                    continue;

                if (
                    existing.IsLeftSide != candidate.IsLeftSide
                    || existing.IsTopSide != candidate.IsTopSide
                )
                {
                    continue;
                }

                if (candidate.Kind == ThinBoundaryFeatureKind.ChamferLine)
                    return;

                if (
                    existing.Center != null
                    && candidate.Center != null
                    && Distance2D(existing.Center, candidate.Center) <= 1.0
                    && Math.Abs(existing.Radius - candidate.Radius) <= 0.5
                )
                {
                    return;
                }
            }

            features.Add(candidate);
        }

        private static bool CreateThinViewChamferAngleDimension(
            View view,
            ThinBoundaryFeature feature
        )
        {
            try
            {
                if (view == null)
                    return false;

                Point origin;
                Point chamferAnchor;
                Point perpendicularPoint;
                if (
                    !TryGetThinChamferAnglePoints(
                        feature,
                        out origin,
                        out chamferAnchor,
                        out perpendicularPoint
                    )
                )
                    return false;

                if (
                    HasMatchingThinChamferAngleDimension(
                        view,
                        origin,
                        chamferAnchor,
                        perpendicularPoint
                    )
                )
                {
                    return true;
                }

                AngleDimensionAttributes attributes = new AngleDimensionAttributes();
                attributes.Type = AngleTypes.AngleOnSide;
                attributes.TransparentBackground = false;
                if (attributes.Text != null)
                {
                    attributes.Text.TextPlacing = DimensionSetBaseAttributes
                        .DimensionTextPlacings
                        .AboveDimensionLine;
                }

                double distance = GetThinChamferDimensionDistance(
                    feature.IsLeftSide,
                    feature.IsTopSide
                );

                // Ba điểm logic: đầu chamfer -> điểm vuông góc -> cuối chamfer.
                // API dùng Point1 làm tia neo nên truyền cuối chamfer trước điểm vuông góc.
                AngleDimension dimension = new AngleDimension(
                    view,
                    origin,
                    chamferAnchor,
                    perpendicularPoint,
                    distance,
                    attributes
                );

                return dimension.Insert();
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetThinChamferAnglePoints(
            ThinBoundaryFeature feature,
            out Point origin,
            out Point chamferAnchor,
            out Point perpendicularPoint
        )
        {
            origin = null;
            chamferAnchor = null;
            perpendicularPoint = null;

            if (
                feature == null
                || feature.ChamferOrigin == null
                || feature.ChamferOtherPoint == null
            )
            {
                return false;
            }

            origin = new Point(feature.ChamferOrigin.X, feature.ChamferOrigin.Y, 0);
            chamferAnchor = new Point(feature.ChamferOtherPoint.X, feature.ChamferOtherPoint.Y, 0);

            double rayLength = Math.Max(
                GetThinChamferVerticalRayLength(feature.IsLeftSide, feature.IsTopSide),
                Math.Abs(chamferAnchor.Y - origin.Y) + 1.0
            );
            perpendicularPoint = new Point(
                origin.X,
                origin.Y + (feature.IsTopSide ? rayLength : -rayLength),
                0
            );

            double cross =
                (chamferAnchor.X - origin.X) * (perpendicularPoint.Y - origin.Y)
                - (chamferAnchor.Y - origin.Y) * (perpendicularPoint.X - origin.X);

            return Math.Abs(cross) > 0.000001;
        }

        private static bool HasMatchingThinChamferAngleDimension(
            View view,
            Point origin,
            Point point1,
            Point point2
        )
        {
            try
            {
                if (view == null || origin == null || point1 == null || point2 == null)
                    return false;

                DrawingObjectEnumerator objects = view.GetAllObjects(typeof(AngleDimension));

                while (objects != null && objects.MoveNext())
                {
                    AngleDimension existing = objects.Current as AngleDimension;
                    if (
                        existing == null
                        || existing.Origin == null
                        || existing.Point1 == null
                        || existing.Point2 == null
                        || Distance2D(existing.Origin, origin) > 0.5
                    )
                    {
                        continue;
                    }

                    bool direct =
                        Distance2D(existing.Point1, point1) <= 0.5
                        && Distance2D(existing.Point2, point2) <= 0.5;
                    bool reverse =
                        Distance2D(existing.Point1, point2) <= 0.5
                        && Distance2D(existing.Point2, point1) <= 0.5;

                    if (direct)
                        return true;

                    if (reverse)
                    {
                        // DIM từ phiên bản cũ có cùng hai tia nhưng bị đảo Point1/Point2.
                        // Chỉ xóa đúng DIM trùng hình học này để tạo lại với đầu chamfer làm tia neo.
                        try
                        {
                            if (!existing.Delete())
                                return true;
                        }
                        catch
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool CreateThinViewRadiusDimByReflection(
            View view,
            Point arc1,
            Point arc2,
            Point arc3,
            double distance
        )
        {
            try
            {
                if (view == null || arc1 == null || arc2 == null || arc3 == null)
                    return false;

                Type type = typeof(RadiusDimension);
                ConstructorInfo[] constructors = type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                foreach (ConstructorInfo constructor in constructors)
                {
                    try
                    {
                        ParameterInfo[] parameters = constructor.GetParameters();
                        if (parameters == null || parameters.Length != 5)
                            continue;

                        object[] args = null;

                        if (
                            parameters[0].ParameterType.IsAssignableFrom(view.GetType())
                            && parameters[1].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[2].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[3].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[4].ParameterType == typeof(double)
                        )
                        {
                            args = new object[] { view, arc1, arc2, arc3, distance };
                        }
                        else if (
                            parameters[0].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[1].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[2].ParameterType.IsAssignableFrom(typeof(Point))
                            && parameters[3].ParameterType == typeof(double)
                            && parameters[4].ParameterType.IsAssignableFrom(view.GetType())
                        )
                        {
                            args = new object[] { arc1, arc2, arc3, distance, view };
                        }

                        if (args == null)
                            continue;

                        object dimension = constructor.Invoke(args);
                        DrawingObject drawingObject = dimension as DrawingObject;
                        if (drawingObject == null)
                            continue;

                        return drawingObject.Insert();
                    }
                    catch { }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region 06 - GEOMETRY / DIM HELPERS
        private static List<Point> GetProjectedSolidPointsForTotalDims(Solid solid)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (solid == null)
                    return result;

                // Solid đã được lấy sau khi SetCurrentTransformationPlane(view.DisplayCoordinateSystem),
                // nên điểm thu được đã nằm trong hệ tọa độ view.
                // Không dùng solid.MinimumPoint / MaximumPoint ở đây vì đó là bounding box ảo.
                CollectRealSolidPointsForTotalDims(solid, result, 0);
            }
            catch { }

            return result;
        }

        private static void CollectRealSolidPointsForTotalDims(
            object obj,
            List<Point> result,
            int depth
        )
        {
            if (obj == null || result == null || depth > 8)
                return;

            Point directPoint = obj as Point;
            if (directPoint != null)
            {
                AddUniquePoint(result, new Point(directPoint.X, directPoint.Y, 0), 0.5);
                return;
            }

            // Các enumerator chính của Tekla Solid thường đi theo Solid -> Face -> Loop -> Vertex.
            // Dùng reflection để tránh phụ thuộc cứng tên class Face/Loop/Vertex giữa các bản Tekla.
            TryCollectFromEnumeratorMethod(obj, result, depth, "GetFaceEnumerator");
            TryCollectFromEnumeratorMethod(obj, result, depth, "GetLoopEnumerator");
            TryCollectFromEnumeratorMethod(obj, result, depth, "GetVertexEnumerator");
            TryCollectFromEnumeratorMethod(obj, result, depth, "GetEdgeEnumerator");
            TryCollectFromEnumeratorMethod(obj, result, depth, "GetPointEnumerator");

            // Một số object Vertex/Edge có thể expose Point/StartPoint/EndPoint bằng property.
            // Không lấy MinimumPoint/MaximumPoint vì đó là bounding box, không phải điểm biên thật.
            TryCollectPointProperty(obj, result, "Point");
            TryCollectPointProperty(obj, result, "Position");
            TryCollectPointProperty(obj, result, "StartPoint");
            TryCollectPointProperty(obj, result, "EndPoint");

            IEnumerable enumerable = obj as IEnumerable;
            if (enumerable != null && !(obj is string))
            {
                foreach (object item in enumerable)
                {
                    CollectRealSolidPointsForTotalDims(item, result, depth + 1);
                }
            }
        }

        private static void TryCollectFromEnumeratorMethod(
            object obj,
            List<Point> result,
            int depth,
            string methodName
        )
        {
            try
            {
                if (obj == null || result == null || string.IsNullOrEmpty(methodName))
                    return;

                MethodInfo method = obj.GetType()
                    .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

                if (method == null || method.GetParameters().Length != 0)
                    return;

                object enumerator = method.Invoke(obj, null);
                if (enumerator == null)
                    return;

                MethodInfo moveNext = enumerator
                    .GetType()
                    .GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);

                PropertyInfo currentProp = enumerator
                    .GetType()
                    .GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);

                if (moveNext == null || currentProp == null)
                    return;

                int guard = 0;
                while (guard < 20000)
                {
                    guard++;

                    object moved = moveNext.Invoke(enumerator, null);
                    if (!(moved is bool) || !(bool)moved)
                        break;

                    object current = currentProp.GetValue(enumerator, null);
                    CollectRealSolidPointsForTotalDims(current, result, depth + 1);
                }
            }
            catch { }
        }

        private static void TryCollectPointProperty(
            object obj,
            List<Point> result,
            string propertyName
        )
        {
            try
            {
                if (obj == null || result == null || string.IsNullOrEmpty(propertyName))
                    return;

                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return;

                if (prop.PropertyType != typeof(Point))
                    return;

                Point p = prop.GetValue(obj, null) as Point;
                if (p == null)
                    return;

                AddUniquePoint(result, new Point(p.X, p.Y, 0), 0.5);
            }
            catch { }
        }

        private static List<Point> GetLargestIntersectionPolygon(IEnumerator en)
        {
            List<List<Point>> all = new List<List<Point>>();

            while (en.MoveNext())
            {
                CollectPointLists(en.Current, all, 0);
            }

            List<Point> best = new List<Point>();
            double bestScore = -1.0;

            foreach (List<Point> list in all)
            {
                if (list.Count < 2)
                    continue;

                double minX,
                    maxX,
                    minY,
                    maxY;
                GetMinMax(list, out minX, out maxX, out minY, out maxY);

                double score = Math.Abs(maxX - minX) * Math.Abs(maxY - minY);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = list;
                }
            }

            return best;
        }

        private static void CollectPointLists(object obj, List<List<Point>> result, int depth)
        {
            if (obj == null || depth > 6)
                return;

            Point p = obj as Point;

            if (p != null)
            {
                List<Point> one = new List<Point>();
                one.Add(new Point(p.X, p.Y, p.Z));
                result.Add(one);
                return;
            }

            IEnumerable e = obj as IEnumerable;

            if (e == null || obj is string)
                return;

            List<Point> directPoints = new List<Point>();

            foreach (object item in e)
            {
                Point ip = item as Point;

                if (ip != null)
                    directPoints.Add(new Point(ip.X, ip.Y, ip.Z));
                else
                    CollectPointLists(item, result, depth + 1);
            }

            if (directPoints.Count >= 2)
                result.Add(directPoints);
        }

        private static void GetMinMax(
            List<Point> pts,
            out double minX,
            out double maxX,
            out double minY,
            out double maxY
        )
        {
            minX = 999999999.0;
            maxX = -999999999.0;
            minY = 999999999.0;
            maxY = -999999999.0;

            foreach (Point p in pts)
            {
                if (p.X < minX)
                    minX = p.X;
                if (p.X > maxX)
                    maxX = p.X;
                if (p.Y < minY)
                    minY = p.Y;
                if (p.Y > maxY)
                    maxY = p.Y;
            }
        }

        private static Point HighestPointNearX(List<Point> pts, double x)
        {
            Point best = null;
            double bestDx = 999999999.0;

            foreach (Point p in pts)
            {
                double dx = Math.Abs(p.X - x);

                if (
                    best == null
                    || dx < bestDx - TOL
                    || (Math.Abs(dx - bestDx) <= TOL && p.Y > best.Y)
                )
                {
                    best = p;
                    bestDx = dx;
                }
            }

            return best;
        }

        private static Point LeftMostPointNearY(List<Point> pts, double y)
        {
            Point best = null;
            double bestDy = 999999999.0;

            foreach (Point p in pts)
            {
                double dy = Math.Abs(p.Y - y);

                if (
                    best == null
                    || dy < bestDy - TOL
                    || (Math.Abs(dy - bestDy) <= TOL && p.X < best.X)
                )
                {
                    best = p;
                    bestDy = dy;
                }
            }

            return best;
        }

        // =====================================================================================
        // FIX DIM TỔNG CHO BO GÓC / RADIUS
        // -------------------------------------------------------------------------------------
        // Vấn đề cũ:
        // - HighestPointNearX / LeftMostPointNearY có thể bắt nhầm vào điểm nhỏ trên cung bo góc.
        // - Kích thước tổng có thể đúng, nhưng chân DIM nhìn như dính vào bo góc.
        //
        // Cách sửa:
        // - Không đổi tầng DIM, offset DIM, logic chamfer, logic lỗ, mark, view.
        // - Chỉ đổi cách chọn CHÂN DIM TỔNG.
        // - Ưu tiên lấy điểm nằm trên cạnh thẳng thật:
        //      + DIM ngang tổng: lấy đầu trên của cạnh đứng ngoài cùng trái/phải.
        //      + DIM dọc tổng : lấy đầu trái của cạnh ngang ngoài cùng dưới/trên.
        // - Nếu không tìm được cạnh thẳng rõ ràng thì fallback về logic cũ.
        // =====================================================================================
        private static Point GetStraightVerticalEdgePointForTotalDim(
            List<Point> polygon,
            double edgeX,
            bool preferTop
        )
        {
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return null;

                List<Point> pts = SortPolygonPointsClockwise(polygon);

                if (pts == null || pts.Count < 2)
                    return null;
                //Dung sai ngang
                double sideTol = TOL + 0.05;
                double minStraightLength = 8.0;

                Point bestA = null;
                Point bestB = null;
                double bestLength = -1.0;
                double bestScore = 999999999.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    // Chỉ nhận cạnh đứng thật nằm sát X ngoài cùng.
                    // Các đoạn cung bo thường rất ngắn hoặc không cùng X ổn định.
                    if (Math.Abs(a.X - edgeX) > sideTol || Math.Abs(b.X - edgeX) > sideTol)
                        continue;

                    double length = Math.Abs(a.Y - b.Y);

                    if (length < minStraightLength)
                        continue;

                    double score = Math.Abs(a.X - edgeX) + Math.Abs(b.X - edgeX);

                    if (
                        length > bestLength + TOL
                        || (Math.Abs(length - bestLength) <= TOL && score < bestScore)
                    )
                    {
                        bestLength = length;
                        bestScore = score;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestA == null || bestB == null)
                    return null;

                double y = preferTop ? Math.Max(bestA.Y, bestB.Y) : Math.Min(bestA.Y, bestB.Y);

                // X dùng đúng edgeX ngoài cùng để giá trị DIM tổng không đổi.
                // Y lấy tại đầu cạnh thẳng thật để chân DIM không nằm trên cung bo.
                return new Point(edgeX, y, 0);
            }
            catch
            {
                return null;
            }
        }

        private static Point GetStraightHorizontalEdgePointForTotalDim(
            List<Point> polygon,
            double edgeY,
            bool preferLeft
        )
        {
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return null;

                List<Point> pts = SortPolygonPointsClockwise(polygon);

                if (pts == null || pts.Count < 2)
                    return null;
                //Dung sai doc
                double sideTol = TOL + 0.05;
                double minStraightLength = 8.0;

                Point bestA = null;
                Point bestB = null;
                double bestLength = -1.0;
                double bestScore = 999999999.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    // Chỉ nhận cạnh ngang thật nằm sát Y ngoài cùng.
                    // Các đoạn cung bo thường rất ngắn hoặc không cùng Y ổn định.
                    if (Math.Abs(a.Y - edgeY) > sideTol || Math.Abs(b.Y - edgeY) > sideTol)
                        continue;

                    double length = Math.Abs(a.X - b.X);

                    if (length < minStraightLength)
                        continue;

                    double score = Math.Abs(a.Y - edgeY) + Math.Abs(b.Y - edgeY);

                    if (
                        length > bestLength + TOL
                        || (Math.Abs(length - bestLength) <= TOL && score < bestScore)
                    )
                    {
                        bestLength = length;
                        bestScore = score;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestA == null || bestB == null)
                    return null;

                double x = preferLeft ? Math.Min(bestA.X, bestB.X) : Math.Max(bestA.X, bestB.X);

                // Y dùng đúng edgeY ngoài cùng để giá trị DIM tổng không đổi.
                // X lấy tại đầu cạnh thẳng thật để chân DIM không nằm trên cung bo.
                return new Point(x, edgeY, 0);
            }
            catch
            {
                return null;
            }
        }

        private static bool CreateDim(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double distance
        )
        {
            if (p1 == null || p2 == null)
                return false;

            if (Distance2D(p1, p2) < 1.0)
                return false;

            PointList list = new PointList();
            list.Add(new Point(p1.X, p1.Y, 0));
            list.Add(new Point(p2.X, p2.Y, 0));

            StraightDimensionSet dim = handler.CreateDimensionSet(view, list, direction, distance);

            return dim != null;
        }

        private static double Distance2D(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        // =====================================================================================
        // PA9 - RULE OFFSET DUY NHẤT CHO MỌI DIM THEO 4 CHÂN DIM TỔNG A/B/C/D
        // -------------------------------------------------------------------------------------
        // A/B = 2 chân DIM tổng ngang, C/D = 2 chân DIM tổng dọc.
        // - Hướng trên  : neo = điểm có Y cao nhất trong A/B/C/D.
        // - Hướng dưới : neo = điểm có Y thấp nhất trong A/B/C/D.
        // - Hướng trái : neo = điểm có X nhỏ nhất trong A/B/C/D.
        // - Hướng phải : neo = điểm có X lớn nhất trong A/B/C/D.
        // Các hàm dưới quy đổi từ target tier theo neo sang distance Tekla của DIM hiện tại.
        // Không bù chamfer, không bù bounding box, không dùng so sánh trùng điểm.
        // =====================================================================================
        private static double GetTopDistanceByAnchor4Direction(
            PointList dimPoints,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            return GetDistanceFromFirstFootToAnchorTarget(
                dimPoints,
                new Vector(0, 1, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetBottomDistanceByAnchor4Direction(
            PointList dimPoints,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            return GetDistanceFromFirstFootToAnchorTarget(
                dimPoints,
                new Vector(0, -1, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetLeftDistanceByAnchor4Direction(
            PointList dimPoints,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            return GetDistanceFromFirstFootToAnchorTarget(
                dimPoints,
                new Vector(-1, 0, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetRightDistanceByAnchor4Direction(
            PointList dimPoints,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            return GetDistanceFromFirstFootToAnchorTarget(
                dimPoints,
                new Vector(1, 0, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetDistanceFromFirstFootToAnchorTarget(
            PointList dimPoints,
            Vector direction,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            // PA11 - RULE DUY NHẤT:
            // 1) Neo chỉ lấy từ 4 chân DIM tổng A/B/C/D.
            //    Trên = Y cao nhất, dưới = Y thấp nhất, trái = X nhỏ nhất, phải = X lớn nhất.
            // 2) Không bù chamfer, không bù bounding box, không dùng hệ tầng cũ 63/125/190.
            // 3) Distance Tekla được quy đổi từ CHÂN ĐẦU của DIM hiện tại
            //    về vị trí đường DIM mong muốn theo neo A/B/C/D.
            try
            {
                if (dimPoints == null || dimPoints.Count == 0 || direction == null)
                    return tierOffset;

                Point firstFoot = null;

                foreach (object obj in dimPoints)
                {
                    firstFoot = obj as Point;
                    if (firstFoot != null)
                        break;
                }

                if (firstFoot == null)
                    return tierOffset;

                Point anchor = null;
                double distance = tierOffset;

                if (Math.Abs(direction.Y) >= Math.Abs(direction.X))
                {
                    if (direction.Y > 0)
                    {
                        anchor = GetHighestYPoint(
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight
                        );
                        if (anchor == null)
                            return tierOffset;

                        distance = (anchor.Y + tierOffset) - firstFoot.Y;
                    }
                    else
                    {
                        anchor = GetLowestYPoint(
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight
                        );
                        if (anchor == null)
                            return tierOffset;

                        distance = firstFoot.Y - (anchor.Y - tierOffset);
                    }
                }
                else
                {
                    if (direction.X < 0)
                    {
                        anchor = GetLowestXPoint(
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight
                        );
                        if (anchor == null)
                            return tierOffset;

                        distance = firstFoot.X - (anchor.X - tierOffset);
                    }
                    else
                    {
                        anchor = GetHighestXPoint(
                            leftForLength,
                            rightForLength,
                            bottomForHeight,
                            topForHeight
                        );
                        if (anchor == null)
                            return tierOffset;

                        distance = (anchor.X + tierOffset) - firstFoot.X;
                    }
                }

                if (double.IsNaN(distance) || double.IsInfinity(distance))
                    return tierOffset;

                return distance > 1.0 ? distance : tierOffset;
            }
            catch
            {
                return tierOffset;
            }
        }

        private static double GetPointListHighestY(PointList pts)
        {
            double result = -999999999.0;
            foreach (object obj in pts)
            {
                Point p = obj as Point;
                if (p != null && p.Y > result)
                    result = p.Y;
            }
            return result < -999999990.0 ? 0.0 : result;
        }

        private static double GetPointListLowestY(PointList pts)
        {
            double result = 999999999.0;
            foreach (object obj in pts)
            {
                Point p = obj as Point;
                if (p != null && p.Y < result)
                    result = p.Y;
            }
            return result > 999999990.0 ? 0.0 : result;
        }

        private static double GetPointListLowestX(PointList pts)
        {
            double result = 999999999.0;
            foreach (object obj in pts)
            {
                Point p = obj as Point;
                if (p != null && p.X < result)
                    result = p.X;
            }
            return result > 999999990.0 ? 0.0 : result;
        }

        private static double GetPointListHighestX(PointList pts)
        {
            double result = -999999999.0;
            foreach (object obj in pts)
            {
                Point p = obj as Point;
                if (p != null && p.X > result)
                    result = p.X;
            }
            return result < -999999990.0 ? 0.0 : result;
        }

        private static double GetTotalTopDistanceByTotalFeetAnchor(
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            PointList list = new PointList();
            if (leftForLength != null)
                list.Add(leftForLength);
            if (rightForLength != null)
                list.Add(rightForLength);

            return GetDistanceFromFirstFootToAnchorTarget(
                list,
                new Vector(0, 1, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetTotalBottomDistanceByTotalFeetAnchor(
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            PointList list = new PointList();
            if (leftForLength != null)
                list.Add(leftForLength);
            if (rightForLength != null)
                list.Add(rightForLength);

            return GetDistanceFromFirstFootToAnchorTarget(
                list,
                new Vector(0, -1, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetTotalLeftDistanceByTotalFeetAnchor(
            Point bottomForHeight,
            Point topForHeight,
            Point leftForLength,
            Point rightForLength,
            double tierOffset
        )
        {
            PointList list = new PointList();
            if (bottomForHeight != null)
                list.Add(bottomForHeight);
            if (topForHeight != null)
                list.Add(topForHeight);

            return GetDistanceFromFirstFootToAnchorTarget(
                list,
                new Vector(-1, 0, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetTotalRightDistanceByTotalFeetAnchor(
            Point bottomForHeight,
            Point topForHeight,
            Point leftForLength,
            Point rightForLength,
            double tierOffset
        )
        {
            PointList list = new PointList();
            if (bottomForHeight != null)
                list.Add(bottomForHeight);
            if (topForHeight != null)
                list.Add(topForHeight);

            return GetDistanceFromFirstFootToAnchorTarget(
                list,
                new Vector(1, 0, 0),
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        private static double GetTotalHorizontalDistanceByTotalFeetAnchor(
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight,
            double tierOffset
        )
        {
            return GetTotalTopDistanceByTotalFeetAnchor(
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                tierOffset
            );
        }

        // Giữ wrapper tên cũ để tránh lỗi nếu còn chỗ gọi cũ.
        private static double GetTotalVerticalLeftDistanceByTotalFeetAnchor(
            Point bottomForHeight,
            Point topForHeight,
            Point leftForLength,
            Point rightForLength,
            double tierOffset
        )
        {
            return GetTotalLeftDistanceByTotalFeetAnchor(
                bottomForHeight,
                topForHeight,
                leftForLength,
                rightForLength,
                tierOffset
            );
        }

        private static Point GetHighestYPoint(params Point[] points)
        {
            Point best = null;

            if (points == null)
                return null;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                if (
                    best == null
                    || p.Y > best.Y + TOL
                    || (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X)
                )
                {
                    best = p;
                }
            }

            return best;
        }

        private static Point GetLowestYPoint(params Point[] points)
        {
            Point best = null;

            if (points == null)
                return null;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                if (
                    best == null
                    || p.Y < best.Y - TOL
                    || (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X)
                )
                {
                    best = p;
                }
            }

            return best;
        }

        private static Point GetLowestXPoint(params Point[] points)
        {
            Point best = null;

            if (points == null)
                return null;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                if (
                    best == null
                    || p.X < best.X - TOL
                    || (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y)
                )
                {
                    best = p;
                }
            }

            return best;
        }

        private static Point GetHighestXPoint(params Point[] points)
        {
            Point best = null;

            if (points == null)
                return null;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                if (
                    best == null
                    || p.X > best.X + TOL
                    || (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y)
                )
                {
                    best = p;
                }
            }

            return best;
        }

        private static double GetLowerY(Point a, Point b)
        {
            if (a == null && b == null)
                return 0.0;
            if (a == null)
                return b.Y;
            if (b == null)
                return a.Y;
            return Math.Min(a.Y, b.Y);
        }

        private static double GetHigherX(Point a, Point b)
        {
            if (a == null && b == null)
                return 0.0;
            if (a == null)
                return b.X;
            if (b == null)
                return a.X;
            return Math.Max(a.X, b.X);
        }

        private static Point FindSharedTotalFoot(
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight
        )
        {
            if (SamePoint2D(leftForLength, bottomForHeight))
                return leftForLength;
            if (SamePoint2D(leftForLength, topForHeight))
                return leftForLength;
            if (SamePoint2D(rightForLength, bottomForHeight))
                return rightForLength;
            if (SamePoint2D(rightForLength, topForHeight))
                return rightForLength;

            return null;
        }

        private static bool SamePoint2D(Point a, Point b)
        {
            if (a == null || b == null)
                return false;

            return Math.Abs(a.X - b.X) <= TOL && Math.Abs(a.Y - b.Y) <= TOL;
        }

        private static Point GetHigherPoint(Point a, Point b)
        {
            if (a == null)
                return b;
            if (b == null)
                return a;

            if (a.Y > b.Y + TOL)
                return a;

            if (b.Y > a.Y + TOL)
                return b;

            return a.X <= b.X ? a : b;
        }

        private static double GetHigherY(Point a, Point b)
        {
            double y = -999999999.0;

            if (a != null && a.Y > y)
                y = a.Y;
            if (b != null && b.Y > y)
                y = b.Y;

            if (y < -999999990.0)
                return 0.0;

            return y;
        }

        private static double GetLowerX(Point a, Point b)
        {
            double x = 999999999.0;

            if (a != null && a.X < x)
                x = a.X;
            if (b != null && b.X < x)
                x = b.X;

            if (x > 999999990.0)
                return 0.0;

            return x;
        }

        #endregion

        #region 07 - PART MARK AUTO MOVE
        private static void AutoMovePartMarkNameV3(
            Drawing drawing,
            View view,
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            try
            {
                List<MarkBase> allMarks = new List<MarkBase>();

                DrawingObjectEnumerator objects = view.GetAllObjects();

                while (objects.MoveNext())
                {
                    MarkBase mark = objects.Current as MarkBase;
                    if (mark == null)
                        continue;

                    allMarks.Add(mark);
                }

                if (allMarks.Count == 0)
                    return;

                List<Point> holes = GetBoltHoleCenters(part, minX, maxX, minY, maxY);
                List<MarkBase> partMarks = new List<MarkBase>();

                foreach (MarkBase mark in allMarks)
                {
                    if (IsPartNameMarkV3(mark, holes, minX, maxX, minY, maxY))
                        partMarks.Add(mark);
                }

                if (partMarks.Count == 0)
                {
                    MarkBase fallback = FindMostLikelyPartMarkV3(
                        allMarks,
                        holes,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );

                    if (fallback != null)
                        partMarks.Add(fallback);
                }

                List<MarkBase> preparedMarks = new List<MarkBase>();
                foreach (MarkBase partMark in partMarks)
                {
                    if (TryPreparePartMarkInsideHorizontal(partMark))
                        preparedMarks.Add(partMark);
                }

                if (preparedMarks.Count == 0)
                    return;

                // Tekla chỉ materialize InsidePartHorizontal thành BaseLinePlacing sau
                // Modify + Commit. Nếu move ngay trên object LeaderLine cũ thì chỉ đầu
                // leader chạy về góc plate, còn box mark đứng nguyên.
                SafeCommitAndWait(drawing, 80);

                for (int i = 0; i < preparedMarks.Count; i++)
                {
                    try
                    {
                        preparedMarks[i].Select();
                    }
                    catch { }

                    MovePartMarkBoxToCenterTop(
                        preparedMarks[i],
                        i,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );
                }
            }
            catch { }
        }

        private static bool TryPreparePartMarkInsideHorizontal(MarkBase mark)
        {
            if (mark == null)
                return false;

            try
            {
                Mark realMark = mark as Mark;
                if (realMark == null || realMark.Attributes == null)
                    return false;

                Mark.MarkAttributes attributes = realMark.Attributes;

                // Tekla 2025 lưu lựa chọn UI này bằng PreferredPlacingTypes = 23.
                // Không dùng enum ordinal/index 3: index 3 thực tế là BaseLinePlacingType.
                attributes.PreferredPlacing = new InsidePartHorizontalPlacingType();

                // PreferredPlacing chỉ mô tả rule; sau Commit, Tekla tạo actual
                // BaseLinePlacing tương ứng. Khóa vị trí để bước move sau được giữ lại.
                if (attributes.PlacingAttributes != null)
                    attributes.PlacingAttributes.IsFixed = true;

                realMark.Attributes = attributes;
                return realMark.Modify();
            }
            catch
            {
                return false;
            }
        }

        private static void MovePartMarkBoxToCenterTop(
            MarkBase mark,
            int index,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            if (mark == null)
                return;

            try
            {
                double centerX = (minX + maxX) / 2.0;
                double plateWidth = Math.Abs(maxX - minX);

                // GIỮ NGUYÊN LOGIC MARK CŨ CHO MIẾNG RỘNG.
                // Chỉ đổi vị trí mark xuống dưới khi chiều rộng miếng < 180.
                bool placeBelow =
                    plateWidth > 0.0 && plateWidth < PART_MARK_BELOW_IF_WIDTH_LESS_THAN;

                // Nếu mark phía trên: đáy khung mark cách mép trên 15mm như logic cũ.
                // Nếu mark phía dưới: đỉnh khung mark cách mép dưới 15mm.
                double targetAnchorY = placeBelow
                    ? minY - PART_MARK_GAP_FROM_PLATE - index * PART_MARK_STAGGER
                    : maxY + PART_MARK_GAP_FROM_PLATE + index * PART_MARK_STAGGER;

                // Lấy khung bao thật của mark. InsertionPoint của Mark là tâm khung,
                // vì vậy phải cộng/trừ nửa chiều cao để khoảng hở được đo từ mép box.
                Point boxMin;
                Point boxMax;
                if (!TryGetObjectBox(mark, out boxMin, out boxMax))
                    return;

                double halfBoxHeight = Math.Abs(boxMax.Y - boxMin.Y) * 0.5;

                double targetCenterY = placeBelow
                    ? targetAnchorY - halfBoxHeight
                    : targetAnchorY + halfBoxHeight;
                Point currentCenter = new Point(
                    (boxMin.X + boxMax.X) * 0.5,
                    (boxMin.Y + boxMax.Y) * 0.5,
                    0
                );

                // Chỉ move sau khi read-back xác nhận actual placing đã là baseline.
                // Tuyệt đối không dùng cùng lệnh trên LeaderLinePlacing vì khi đó
                // Tekla chỉ kéo leader point, chính là lỗi không đồng đều giữa các tấm.
                if (!(mark.Placing is BaseLinePlacing))
                    return;

                Vector move = new Vector(
                    centerX - currentCenter.X,
                    targetCenterY - currentCenter.Y,
                    0
                );

                if (TryMoveObjectRelative(mark, move))
                    mark.Modify();
            }
            catch { }
        }

        private static bool IsPartNameMarkV3(
            MarkBase mark,
            List<Point> holes,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            if (mark == null)
                return false;

            string typeName = mark.GetType().FullName.ToUpper();

            if (
                typeName.Contains("BOLTMARK")
                || typeName.Contains("HOLEMARK")
                || typeName.Contains("WELDMARK")
                || typeName.Contains("CONNECTIONMARK")
            )
                return false;

            if (typeName.Contains("PARTMARK"))
                return true;

            // Drawing API trả cả part mark và bolt mark dưới cùng runtime type
            // Tekla.Structures.Drawing.Mark. Vì vậy type name/position không đủ để
            // phân biệt. Ưu tiên nội dung semantic của template mark trước hình học.
            Mark realMark = mark as Mark;
            if (IsPartNameMarkContentV3(realMark))
                return true;

            if (IsBoltOrHoleMarkContentV3(realMark))
                return false;

            string text = realMark != null
                ? GetMarkTextForAutoFix(realMark).ToUpperInvariant()
                : GetObjectTextByReflection(mark).ToUpperInvariant();

            if (
                text.Contains("アンカー")
                || text.Contains("ルーズ")
                || text.Contains("中ﾎﾞﾙﾄ")
                || text.Contains("HOLE")
                || text.Contains("BOLT")
                || text.Contains("Ø")
                || text.Contains("Φ")
                || text.Contains("M20")
                || text.Contains("M 20")
            )
                return false;

            if (text.Contains("PL") || text.Contains("BP") || text.Contains("*"))
                return true;

            Point p = SafeGetInsertionPoint(mark);

            if (p != null)
            {
                bool nearTop =
                    p.X >= minX - 100.0
                    && p.X <= maxX + 100.0
                    && p.Y >= maxY - 40.0
                    && p.Y <= maxY + 120.0;

                if (nearTop && !IsPointNearAnyHole(p, holes, 120.0))
                    return true;
            }

            return false;
        }

        private static bool IsPartNameMarkContentV3(Mark mark)
        {
            HashSet<string> names = GetMarkContentPropertyNamesV3(mark);
            if (names.Count == 0)
                return false;

            bool hasPartPos = names.Contains("PART_POS");
            bool hasProfile = names.Contains("PROFILE");
            bool hasMaterial = names.Contains("MATERIAL");
            bool hasLength = names.Contains("LENGTH");

            // Tiêu chuẩn hiện tại có PART_POS + PROFILE. Nhánh thứ hai giữ tương
            // thích cho template part mark rút gọn nhưng vẫn đòi đủ ba thuộc tính
            // kỹ thuật, không nhận một note chỉ tình cờ chứa chữ PL.
            return (hasPartPos && (hasProfile || hasMaterial || hasLength))
                || (hasProfile && hasMaterial && hasLength);
        }

        private static bool IsBoltOrHoleMarkContentV3(Mark mark)
        {
            HashSet<string> names = GetMarkContentPropertyNamesV3(mark);

            foreach (string name in names)
            {
                if (
                    name.Contains("BOLT")
                    || name.Contains("HOLE")
                    || name.Contains("DIAMETER")
                    || name.Contains("WELD")
                )
                    return true;
            }

            return false;
        }

        private static HashSet<string> GetMarkContentPropertyNamesV3(Mark mark)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (mark != null && mark.Attributes != null)
                    CollectMarkContentPropertyNamesV3(mark.Attributes.Content, names, 0);
            }
            catch { }

            return names;
        }

        private static void CollectMarkContentPropertyNamesV3(
            object content,
            HashSet<string> names,
            int depth
        )
        {
            if (content == null || names == null || depth > 6)
                return;

            try
            {
                object rawName = GetPropertyValueForAutoFix(content, "Name");
                if (rawName != null)
                {
                    string name = rawName.ToString().Trim().ToUpperInvariant();
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }
            catch { }

            try
            {
                object childContent = GetPropertyValueForAutoFix(content, "Content");
                if (childContent != null && !object.ReferenceEquals(childContent, content))
                    CollectMarkContentPropertyNamesV3(childContent, names, depth + 1);
            }
            catch { }

            IEnumerable enumerable = content as IEnumerable;
            if (enumerable == null || content is string)
                return;

            try
            {
                foreach (object item in enumerable)
                    CollectMarkContentPropertyNamesV3(item, names, depth + 1);
            }
            catch { }
        }

        private static MarkBase FindMostLikelyPartMarkV3(
            List<MarkBase> marks,
            List<Point> holes,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            MarkBase best = null;
            double bestScore = 999999999.0;

            Point target = new Point((minX + maxX) / 2.0, maxY + PART_MARK_GAP_FROM_PLATE, 0);

            foreach (MarkBase mark in marks)
            {
                if (mark == null)
                    continue;

                string typeName = mark.GetType().FullName.ToUpper();

                if (
                    typeName.Contains("BOLTMARK")
                    || typeName.Contains("HOLEMARK")
                    || typeName.Contains("WELDMARK")
                )
                    continue;

                Mark realMark = mark as Mark;
                if (IsBoltOrHoleMarkContentV3(realMark))
                    continue;

                Point boxMin;
                Point boxMax;
                Point p = null;

                if (TryGetObjectBox(mark, out boxMin, out boxMax))
                {
                    p = new Point((boxMin.X + boxMax.X) / 2.0, (boxMin.Y + boxMax.Y) / 2.0, 0);
                }
                else
                {
                    p = SafeGetInsertionPoint(mark);
                }

                if (p == null)
                    continue;

                if (IsPointNearAnyHole(p, holes, 120.0))
                    continue;

                double score = Distance2D(p, target);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = mark;
                }
            }

            return best;
        }

        private static bool TryGetObjectBox(DrawingObject obj, out Point min, out Point max)
        {
            min = null;
            max = null;

            try
            {
                MethodInfo method = obj.GetType()
                    .GetMethod(
                        "GetAxisAlignedBoundingBox",
                        BindingFlags.Public | BindingFlags.Instance
                    );

                if (method != null)
                {
                    object box = method.Invoke(obj, null);

                    if (TryExtractBoxMinMax(box, out min, out max))
                        return true;
                }
            }
            catch { }

            try
            {
                PropertyInfo prop = obj.GetType()
                    .GetProperty("BoundingBox", BindingFlags.Public | BindingFlags.Instance);

                if (prop != null && prop.CanRead)
                {
                    object box = prop.GetValue(obj, null);

                    if (TryExtractBoxMinMax(box, out min, out max))
                        return true;
                }
            }
            catch { }

            try
            {
                PropertyInfo prop = obj.GetType()
                    .GetProperty("RestrictionBox", BindingFlags.Public | BindingFlags.Instance);

                if (prop != null && prop.CanRead)
                {
                    object box = prop.GetValue(obj, null);

                    if (TryExtractBoxMinMax(box, out min, out max))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool TryExtractBoxMinMax(object box, out Point min, out Point max)
        {
            min = null;
            max = null;

            if (box == null)
                return false;

            try
            {
                PropertyInfo minProp = box.GetType()
                    .GetProperty("MinPoint", BindingFlags.Public | BindingFlags.Instance);

                PropertyInfo maxProp = box.GetType()
                    .GetProperty("MaxPoint", BindingFlags.Public | BindingFlags.Instance);

                if (minProp != null && maxProp != null)
                {
                    Point pMin = minProp.GetValue(box, null) as Point;
                    Point pMax = maxProp.GetValue(box, null) as Point;

                    if (pMin != null && pMax != null)
                    {
                        min = new Point(pMin.X, pMin.Y, pMin.Z);
                        max = new Point(pMax.X, pMax.Y, pMax.Z);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool TryMoveObjectRelative(DrawingObject obj, Vector move)
        {
            try
            {
                MethodInfo method = obj.GetType()
                    .GetMethod("MoveObjectRelative", BindingFlags.Public | BindingFlags.Instance);

                if (method == null)
                    return false;

                method.Invoke(obj, new object[] { move });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Point SafeGetInsertionPoint(MarkBase mark)
        {
            try
            {
                if (mark == null)
                    return null;

                Point p = mark.InsertionPoint;

                if (p == null)
                    return null;

                return new Point(p.X, p.Y, p.Z);
            }
            catch
            {
                return TryGetPointProperty(mark, "InsertionPoint");
            }
        }

        private static bool IsPointNearAnyHole(Point p, List<Point> holes, double distance)
        {
            if (p == null || holes == null)
                return false;

            foreach (Point h in holes)
            {
                if (h == null)
                    continue;

                if (Distance2D(p, h) <= distance)
                    return true;
            }

            return false;
        }

        private static bool TrySetPointProperty(object obj, string propertyName, Point value)
        {
            try
            {
                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanWrite)
                    return false;

                if (prop.PropertyType != typeof(Point))
                    return false;

                prop.SetValue(obj, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Point TryGetPointProperty(object obj, string propertyName)
        {
            try
            {
                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return null;

                if (prop.PropertyType != typeof(Point))
                    return null;

                Point p = prop.GetValue(obj, null) as Point;

                if (p == null)
                    return null;

                return new Point(p.X, p.Y, p.Z);
            }
            catch
            {
                return null;
            }
        }

        private static string GetObjectTextByReflection(object obj)
        {
            try
            {
                List<string> texts = new List<string>();
                CollectStringsByReflection(obj, texts, 0);

                string result = "";

                foreach (string s in texts)
                    result += " " + s;

                return result;
            }
            catch
            {
                return "";
            }
        }

        private static void CollectStringsByReflection(object obj, List<string> texts, int depth)
        {
            if (obj == null || depth > 4)
                return;

            string s = obj as string;

            if (s != null)
            {
                if (s.Length > 0)
                    texts.Add(s);

                return;
            }

            IEnumerable en = obj as IEnumerable;

            if (en != null && !(obj is string))
            {
                foreach (object item in en)
                    CollectStringsByReflection(item, texts, depth + 1);
            }

            Type t = obj.GetType();

            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo prop in props)
            {
                if (!prop.CanRead)
                    continue;

                string name = prop.Name.ToUpper();

                if (
                    !(
                        name.Contains("TEXT")
                        || name.Contains("CONTENT")
                        || name.Contains("VALUE")
                        || name.Contains("STRING")
                        || name.Contains("TAG")
                    )
                )
                    continue;

                try
                {
                    object value = prop.GetValue(obj, null);
                    CollectStringsByReflection(value, texts, depth + 1);
                }
                catch { }
            }
        }

        #endregion

        #region 07B - HOLE MARK AESTHETIC LEADER / COLLISION AVOIDANCE

        private sealed class HoleMarkSegmentV3
        {
            public Point A;
            public Point B;
        }

        private sealed class HoleMarkRectV3
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private sealed class HoleMarkLayoutItemV3
        {
            public Mark Mark;
            public Point CurrentAnchor;
            public List<Point> Anchors = new List<Point>();
            public List<Point> GroupHoles = new List<Point>();
            public List<HoleMarkSegmentV3> OccupiedLeaders =
                new List<HoleMarkSegmentV3>();
            public double Width;
            public double Height;
            public int RequiredVerticalRank = -1;
            public bool RequireNoDimensionConflicts;
            public bool RequireAcceptableObliqueAngle;
        }

        private sealed class HoleMarkCandidateV3
        {
            public Point Anchor;
            public Point Contact;
            public Point Center;
            public HoleMarkRectV3 Box;
            public int Side;
            public double TangentOffset;
            public double OutwardOffset;
            public double LeaderLength;
            public double AngleDegrees;
            public double AngleDeviation;
            public int DimensionConflictCount;
            public int AngleQualityRank;
            public int VisualTier;
            public int VerticalPreferenceRank;
            public double Score;
        }

        private sealed class HoleMarkLayoutPlanEntryV3
        {
            public HoleMarkLayoutItemV3 Item;
            public HoleMarkCandidateV3 Candidate;
        }

        private sealed class HoleMarkLayoutPlanV3
        {
            public List<HoleMarkLayoutPlanEntryV3> Entries =
                new List<HoleMarkLayoutPlanEntryV3>();
            public double TotalScore;
        }

        private static void AutoArrangeHoleMarksAesthetic(
            Model model,
            Drawing drawing,
            ModelPart part,
            List<View> views
        )
        {
            if (model == null || drawing == null || part == null || views == null)
                return;

            TransformationPlane oldPlane = null;

            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    try
                    {
                        // Tekla 2025 giữ cache Solid theo work plane của lần đọc trước.
                        // Luôn quay về plane gốc rồi select lại Part trước mỗi view;
                        // nếu không, view thứ hai có thể nhận tọa độ global lệch hàng chục mét.
                        if (oldPlane != null)
                        {
                            model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                        }
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                            new TransformationPlane(view.DisplayCoordinateSystem)
                        );

                        ModelPart viewPart = null;
                        try
                        {
                            if (part.Identifier != null)
                            {
                                viewPart = model.SelectModelObject(part.Identifier) as ModelPart;
                            }
                        }
                        catch { }
                        if (viewPart == null)
                            viewPart = part;

                        Solid solid = viewPart.GetSolid();
                        if (
                            solid == null
                            || solid.MinimumPoint == null
                            || solid.MaximumPoint == null
                        )
                        {
                            continue;
                        }

                        double minX = Math.Min(solid.MinimumPoint.X, solid.MaximumPoint.X);
                        double maxX = Math.Max(solid.MinimumPoint.X, solid.MaximumPoint.X);
                        double minY = Math.Min(solid.MinimumPoint.Y, solid.MaximumPoint.Y);
                        double maxY = Math.Max(solid.MinimumPoint.Y, solid.MaximumPoint.Y);

                        List<List<Point>> holeGroups = GetBoltHoleGroupsForMarkLayoutV3(
                            viewPart,
                            minX,
                            maxX,
                            minY,
                            maxY
                        );

                        double scale = GetViewScaleNumberForTitle3(view);
                        if (scale <= 0.0)
                            scale = 5.0;

                        AutoArrangeHoleMarksAestheticInView(
                            drawing,
                            view,
                            holeGroups,
                            minX,
                            maxX,
                            minY,
                            maxY,
                            scale
                        );
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            if (oldPlane != null)
                            {
                                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            finally
            {
                try
                {
                    if (oldPlane != null)
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }
        }

        private static void AutoArrangeHoleMarksAestheticInView(
            Drawing drawing,
            View view,
            List<List<Point>> holeGroups,
            double partMinX,
            double partMaxX,
            double partMinY,
            double partMaxY,
            double scale
        )
        {
            List<Mark> allMarks = new List<Mark>();
            List<Mark> holeMarks = new List<Mark>();

            try
            {
                DrawingObjectEnumerator objects = view.GetAllObjects();
                while (objects != null && objects.MoveNext())
                {
                    Mark mark = objects.Current as Mark;
                    if (mark == null)
                        continue;

                    allMarks.Add(mark);
                    if (IsBoltOrHoleMarkContentV3(mark))
                        holeMarks.Add(mark);
                }
            }
            catch { }

            if (holeMarks.Count == 0)
                return;

            List<HoleMarkLayoutItemV3> items = new List<HoleMarkLayoutItemV3>();
            double anchorMatchTolerance = HOLE_MARK_ANCHOR_MATCH_PAPER * scale;
            foreach (Mark mark in holeMarks)
            {
                try
                {
                    mark.Select();

                    LeaderLinePlacing placing = mark.Placing as LeaderLinePlacing;
                    if (placing == null || placing.StartPoint == null)
                        continue;

                    Point boxMin;
                    Point boxMax;
                    if (!TryGetObjectBox(mark, out boxMin, out boxMax))
                        continue;

                    HoleMarkLayoutItemV3 item = new HoleMarkLayoutItemV3();
                    item.Mark = mark;
                    item.CurrentAnchor = new Point(
                        placing.StartPoint.X,
                        placing.StartPoint.Y,
                        0
                    );
                    item.Width = Math.Abs(boxMax.X - boxMin.X);
                    item.Height = Math.Abs(boxMax.Y - boxMin.Y);
                    // Chỉ move mark. Không đổi lỗ/bolt đang được leader trỏ tới.
                    item.Anchors.Add(
                        new Point(
                            item.CurrentAnchor.X,
                            item.CurrentAnchor.Y,
                            0
                        )
                    );
                    item.GroupHoles = ResolveHoleMarkAnchorCandidatesV3(
                        item.CurrentAnchor,
                        holeGroups,
                        anchorMatchTolerance
                    );
                    if (item.GroupHoles.Count == 0)
                    {
                        item.GroupHoles.Add(
                            new Point(
                                item.CurrentAnchor.X,
                                item.CurrentAnchor.Y,
                                0
                            )
                        );
                    }

                    if (
                        item.Width > 0.1
                        && item.Height > 0.1
                        && item.Anchors.Count > 0
                    )
                    {
                        items.Add(item);
                    }
                }
                catch { }
            }

            if (items.Count == 0)
                return;

            items.Sort(
                delegate(HoleMarkLayoutItemV3 first, HoleMarkLayoutItemV3 second)
                {
                    int x = first.CurrentAnchor.X.CompareTo(second.CurrentAnchor.X);
                    if (x != 0)
                        return x;
                    return second.CurrentAnchor.Y.CompareTo(first.CurrentAnchor.Y);
                }
            );

            List<HoleMarkSegmentV3> dimensionSegments =
                BuildDimensionObstacleSegmentsForHoleMarksV3(view, scale);
            List<HoleMarkRectV3> occupiedMarkBoxes = new List<HoleMarkRectV3>();

            foreach (Mark mark in allMarks)
            {
                if (holeMarks.Contains(mark))
                    continue;

                Point boxMin;
                Point boxMax;
                if (TryGetObjectBox(mark, out boxMin, out boxMax))
                    occupiedMarkBoxes.Add(MakeHoleMarkRectV3(boxMin, boxMax));
            }

            HoleMarkRectV3 partBox = new HoleMarkRectV3();
            partBox.MinX = partMinX;
            partBox.MaxX = partMaxX;
            partBox.MinY = partMinY;
            partBox.MaxY = partMaxY;

            HoleMarkLayoutPlanV3 layoutPlan = BuildCoordinatedHoleMarkLayoutPlanV3(
                items,
                partBox,
                dimensionSegments,
                occupiedMarkBoxes,
                scale
            );
            if (layoutPlan == null || layoutPlan.Entries.Count == 0)
                return;

            bool movedAny = false;
            foreach (HoleMarkLayoutPlanEntryV3 entry in layoutPlan.Entries)
            {
                if (entry == null || entry.Item == null || entry.Candidate == null)
                    continue;

                HoleMarkLayoutItemV3 item = entry.Item;
                HoleMarkCandidateV3 candidate = entry.Candidate;

                try
                {
                    Point currentCenter = GetHoleMarkBoxCenterV3(item.Mark);
                    if (currentCenter == null)
                        continue;

                    Vector move = new Vector(
                        candidate.Center.X - currentCenter.X,
                        candidate.Center.Y - currentCenter.Y,
                        0
                    );
                    if (Math.Abs(move.X) > 0.01 || Math.Abs(move.Y) > 0.01)
                    {
                        if (!TryMoveObjectRelative(item.Mark, move))
                            continue;
                        if (!TryLimitedModifyHoleMarkV3(item.Mark))
                            continue;
                        movedAny = true;
                    }

                }
                catch { }
            }

            if (!movedAny)
                return;

            SafeCommitAndWait(drawing, 80);
        }

        private static HoleMarkLayoutPlanV3 BuildCoordinatedHoleMarkLayoutPlanV3(
            List<HoleMarkLayoutItemV3> items,
            HoleMarkRectV3 partBox,
            List<HoleMarkSegmentV3> dimensionSegments,
            List<HoleMarkRectV3> fixedMarkBoxes,
            double scale
        )
        {
            if (items == null || items.Count == 0)
                return null;

            // Một cụm MARK phải được xét như một bố cục chung, không phải các bài
            // toán độc lập. Ưu tiên một hướng chung (trên hoặc dưới), leader xiên
            // 20..70° và sạch DIM. Nếu không có nghiệm mới lần lượt nới hướng chung,
            // góc xiên rồi xung đột DIM. Với mỗi mode thử cả hai thứ tự anchor và
            // chọn tổng điểm thấp hơn; scoring vẫn ưu tiên phía trên khi hai phương
            // án có cùng chất lượng hình học.
            HoleMarkLayoutPlanV3 plan = PickBetterHoleMarkLayoutPlanV3(
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 0, true, true
                ),
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 2, true, true
                )
            );
            if (plan != null)
                return plan;

            plan = BuildBestHoleMarkLayoutPlanForModeV3(
                items, partBox, dimensionSegments, fixedMarkBoxes, scale, -1, true, true
            );
            if (plan != null)
                return plan;

            plan = PickBetterHoleMarkLayoutPlanV3(
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 0, true, false
                ),
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 2, true, false
                )
            );
            if (plan != null)
                return plan;

            plan = BuildBestHoleMarkLayoutPlanForModeV3(
                items, partBox, dimensionSegments, fixedMarkBoxes, scale, -1, true, false
            );
            if (plan != null)
                return plan;

            plan = PickBetterHoleMarkLayoutPlanV3(
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 0, false, true
                ),
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 2, false, true
                )
            );
            if (plan != null)
                return plan;

            plan = BuildBestHoleMarkLayoutPlanForModeV3(
                items, partBox, dimensionSegments, fixedMarkBoxes, scale, -1, false, true
            );
            if (plan != null)
                return plan;

            plan = PickBetterHoleMarkLayoutPlanV3(
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 0, false, false
                ),
                BuildBestHoleMarkLayoutPlanForModeV3(
                    items, partBox, dimensionSegments, fixedMarkBoxes, scale, 2, false, false
                )
            );
            if (plan != null)
                return plan;

            return BuildBestHoleMarkLayoutPlanForModeV3(
                items, partBox, dimensionSegments, fixedMarkBoxes, scale, -1, false, false
            );
        }

        private static HoleMarkLayoutPlanV3 PickBetterHoleMarkLayoutPlanV3(
            HoleMarkLayoutPlanV3 first,
            HoleMarkLayoutPlanV3 second
        )
        {
            if (first == null)
                return second;
            if (second == null)
                return first;
            return second.TotalScore < first.TotalScore - 0.000001 ? second : first;
        }

        private static HoleMarkLayoutPlanV3 BuildBestHoleMarkLayoutPlanForModeV3(
            List<HoleMarkLayoutItemV3> items,
            HoleMarkRectV3 partBox,
            List<HoleMarkSegmentV3> dimensionSegments,
            List<HoleMarkRectV3> fixedMarkBoxes,
            double scale,
            int requiredVerticalRank,
            bool requireNoDimensionConflicts,
            bool requireAcceptableObliqueAngle
        )
        {
            HoleMarkLayoutPlanV3 forward = TryBuildHoleMarkLayoutPlanInOrderV3(
                items,
                false,
                partBox,
                dimensionSegments,
                fixedMarkBoxes,
                scale,
                requiredVerticalRank,
                requireNoDimensionConflicts,
                requireAcceptableObliqueAngle
            );
            if (items.Count <= 1)
                return forward;

            HoleMarkLayoutPlanV3 reverse = TryBuildHoleMarkLayoutPlanInOrderV3(
                items,
                true,
                partBox,
                dimensionSegments,
                fixedMarkBoxes,
                scale,
                requiredVerticalRank,
                requireNoDimensionConflicts,
                requireAcceptableObliqueAngle
            );
            if (forward == null)
                return reverse;
            if (reverse == null)
                return forward;
            return reverse.TotalScore < forward.TotalScore - 0.000001
                ? reverse
                : forward;
        }

        private static HoleMarkLayoutPlanV3 TryBuildHoleMarkLayoutPlanInOrderV3(
            List<HoleMarkLayoutItemV3> sourceItems,
            bool reverseOrder,
            HoleMarkRectV3 partBox,
            List<HoleMarkSegmentV3> dimensionSegments,
            List<HoleMarkRectV3> fixedMarkBoxes,
            double scale,
            int requiredVerticalRank,
            bool requireNoDimensionConflicts,
            bool requireAcceptableObliqueAngle
        )
        {
            if (sourceItems == null || sourceItems.Count == 0)
                return null;

            List<HoleMarkLayoutItemV3> orderedItems =
                new List<HoleMarkLayoutItemV3>(sourceItems);
            if (reverseOrder)
                orderedItems.Reverse();

            List<HoleMarkRectV3> occupiedBoxes = fixedMarkBoxes == null
                ? new List<HoleMarkRectV3>()
                : new List<HoleMarkRectV3>(fixedMarkBoxes);
            List<HoleMarkSegmentV3> occupiedLeaders =
                new List<HoleMarkSegmentV3>();
            HoleMarkLayoutPlanV3 plan = new HoleMarkLayoutPlanV3();

            try
            {
                foreach (HoleMarkLayoutItemV3 item in orderedItems)
                {
                    if (item == null)
                        return null;

                    item.RequiredVerticalRank = requiredVerticalRank;
                    item.RequireNoDimensionConflicts = requireNoDimensionConflicts;
                    item.RequireAcceptableObliqueAngle = requireAcceptableObliqueAngle;
                    item.OccupiedLeaders = occupiedLeaders;

                    HoleMarkCandidateV3 candidate = FindBestHoleMarkCandidateV3(
                        item,
                        partBox,
                        dimensionSegments,
                        occupiedBoxes,
                        scale
                    );
                    if (candidate == null)
                        return null;

                    HoleMarkLayoutPlanEntryV3 entry = new HoleMarkLayoutPlanEntryV3();
                    entry.Item = item;
                    entry.Candidate = candidate;
                    plan.Entries.Add(entry);
                    plan.TotalScore += candidate.Score;
                    occupiedBoxes.Add(candidate.Box);

                    HoleMarkSegmentV3 leader = new HoleMarkSegmentV3();
                    leader.A = candidate.Anchor;
                    leader.B = candidate.Contact;
                    occupiedLeaders.Add(leader);
                }

                return plan;
            }
            finally
            {
                foreach (HoleMarkLayoutItemV3 item in sourceItems)
                {
                    if (item == null)
                        continue;
                    item.RequiredVerticalRank = -1;
                    item.RequireNoDimensionConflicts = false;
                    item.RequireAcceptableObliqueAngle = false;
                    item.OccupiedLeaders = new List<HoleMarkSegmentV3>();
                }
            }
        }

        private static Point GetHoleMarkBoxCenterV3(Mark mark)
        {
            Point min;
            Point max;
            if (mark == null || !TryGetObjectBox(mark, out min, out max))
                return null;

            return new Point(
                (min.X + max.X) * 0.5,
                (min.Y + max.Y) * 0.5,
                0
            );
        }

        private static bool TryLimitedModifyHoleMarkV3(Mark mark)
        {
            if (mark == null)
                return false;

            try
            {
                MethodInfo method = mark.GetType().GetMethod(
                    "LimitedModify",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (method == null)
                    return false;

                object result = method.Invoke(mark, null);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static List<List<Point>> GetBoltHoleGroupsForMarkLayoutV3(
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            List<List<Point>> groups = new List<List<Point>>();

            try
            {
                ModelObjectEnumerator bolts = part.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup boltGroup = bolts.Current as ModelBoltGroup;
                    if (boltGroup == null)
                        continue;

                    List<Point> points = new List<Point>();
                    foreach (object value in boltGroup.BoltPositions)
                    {
                        Point point = value as Point;
                        if (point == null)
                            continue;

                        if (
                            point.X < minX - 5.0
                            || point.X > maxX + 5.0
                            || point.Y < minY - 5.0
                            || point.Y > maxY + 5.0
                        )
                        {
                            continue;
                        }

                        AddUniquePoint(points, new Point(point.X, point.Y, 0), 1.0);
                    }

                    if (points.Count > 0)
                        groups.Add(points);
                }
            }
            catch { }

            return groups;
        }

        private static List<Point> ResolveHoleMarkAnchorCandidatesV3(
            Point currentAnchor,
            List<List<Point>> groups,
            double matchTolerance
        )
        {
            List<Point> result = new List<Point>();
            if (currentAnchor == null)
                return result;

            List<Point> bestGroup = null;
            double bestDistance = 999999999.0;

            if (groups != null)
            {
                foreach (List<Point> group in groups)
                {
                    if (group == null || group.Count == 0)
                        continue;

                    double groupDistance = 999999999.0;
                    foreach (Point point in group)
                    {
                        double distance = Distance2D(currentAnchor, point);
                        if (distance < groupDistance)
                            groupDistance = distance;
                    }

                    if (groupDistance < bestDistance)
                    {
                        bestDistance = groupDistance;
                        bestGroup = group;
                    }
                }
            }

            if (bestGroup != null && bestDistance <= matchTolerance)
            {
                foreach (Point point in bestGroup)
                    AddUniquePoint(result, new Point(point.X, point.Y, 0), 0.5);
            }
            else
            {
                result.Add(new Point(currentAnchor.X, currentAnchor.Y, 0));
            }

            result.Sort(
                delegate(Point first, Point second)
                {
                    double firstDistance = Distance2D(first, currentAnchor);
                    double secondDistance = Distance2D(second, currentAnchor);
                    int distanceCompare = firstDistance.CompareTo(secondDistance);
                    if (distanceCompare != 0)
                        return distanceCompare;

                    int x = first.X.CompareTo(second.X);
                    if (x != 0)
                        return x;
                    return first.Y.CompareTo(second.Y);
                }
            );

            return result;
        }

        private static List<HoleMarkSegmentV3> BuildDimensionObstacleSegmentsForHoleMarksV3(
            View view,
            double scale
        )
        {
            List<HoleMarkSegmentV3> result = new List<HoleMarkSegmentV3>();

            try
            {
                DrawingObjectEnumerator objects = view.GetAllObjects();
                while (objects != null && objects.MoveNext())
                {
                    StraightDimensionSet set = objects.Current as StraightDimensionSet;
                    if (set == null)
                        continue;

                    List<Point> points = ReadDimensionPointsForHoleMarkV3(set);
                    if (points.Count < 2)
                        continue;

                    Vector up = GetMemberValueForHoleMarkV3(set, "UpDirection") as Vector;
                    object rawDistance = GetMemberValueForHoleMarkV3(set, "Distance");
                    if (up == null || rawDistance == null)
                        continue;

                    double distance;
                    try
                    {
                        distance = Convert.ToDouble(rawDistance);
                    }
                    catch
                    {
                        continue;
                    }

                    double upLength = Math.Sqrt(up.X * up.X + up.Y * up.Y);
                    if (upLength <= 0.0001)
                        continue;

                    double nx = up.X / upLength;
                    double ny = up.Y / upLength;
                    double tx = -ny;
                    double ty = nx;
                    Point first = points[0];
                    Point lineOrigin = new Point(
                        first.X + nx * distance,
                        first.Y + ny * distance,
                        0
                    );

                    double minProjection = 999999999.0;
                    double maxProjection = -999999999.0;
                    foreach (Point point in points)
                    {
                        double projection =
                            (point.X - lineOrigin.X) * tx
                            + (point.Y - lineOrigin.Y) * ty;
                        if (projection < minProjection)
                            minProjection = projection;
                        if (projection > maxProjection)
                            maxProjection = projection;

                        Point onLine = new Point(
                            lineOrigin.X + tx * projection,
                            lineOrigin.Y + ty * projection,
                            0
                        );
                        AddHoleMarkSegmentV3(result, point, onLine);
                    }

                    double lineOverrun = 2.0 * scale;
                    Point lineStart = new Point(
                        lineOrigin.X + tx * (minProjection - lineOverrun),
                        lineOrigin.Y + ty * (minProjection - lineOverrun),
                        0
                    );
                    Point lineEnd = new Point(
                        lineOrigin.X + tx * (maxProjection + lineOverrun),
                        lineOrigin.Y + ty * (maxProjection + lineOverrun),
                        0
                    );
                    AddHoleMarkSegmentV3(result, lineStart, lineEnd);
                }
            }
            catch { }

            return result;
        }

        private static List<Point> ReadDimensionPointsForHoleMarkV3(object dimension)
        {
            List<Point> result = new List<Point>();
            object rawPoints = GetMemberValueForHoleMarkV3(dimension, "DimensionPoints");
            IEnumerable enumerable = rawPoints as IEnumerable;
            if (enumerable == null)
                return result;

            try
            {
                foreach (object value in enumerable)
                {
                    Point point = value as Point;
                    if (point != null)
                        result.Add(new Point(point.X, point.Y, 0));
                }
            }
            catch { }

            return result;
        }

        private static object GetMemberValueForHoleMarkV3(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
                return null;

            try
            {
                PropertyInfo property = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (property != null && property.CanRead)
                    return property.GetValue(obj, null);
            }
            catch { }

            try
            {
                FieldInfo field = obj.GetType().GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (field != null)
                    return field.GetValue(obj);
            }
            catch { }

            return null;
        }

        private static void AddHoleMarkSegmentV3(
            List<HoleMarkSegmentV3> segments,
            Point first,
            Point second
        )
        {
            if (segments == null || first == null || second == null)
                return;

            if (Distance2D(first, second) <= 0.01)
                return;

            HoleMarkSegmentV3 segment = new HoleMarkSegmentV3();
            segment.A = new Point(first.X, first.Y, 0);
            segment.B = new Point(second.X, second.Y, 0);
            segments.Add(segment);
        }

        private static HoleMarkCandidateV3 FindBestHoleMarkCandidateV3(
            HoleMarkLayoutItemV3 item,
            HoleMarkRectV3 partBox,
            List<HoleMarkSegmentV3> dimensionSegments,
            List<HoleMarkRectV3> occupiedMarkBoxes,
            double scale
        )
        {
            if (item == null || item.CurrentAnchor == null || item.Anchors == null)
                return null;

            double boundaryGap = HOLE_MARK_BOUNDARY_GAP_PAPER * scale;
            double step = Math.Max(1.0, HOLE_MARK_SEARCH_STEP_PAPER * scale);
            double fineStep = Math.Max(0.25, 0.1 * scale);
            double maxOutward = HOLE_MARK_MAX_EXTRA_SEARCH_PAPER * scale;
            double maxTangent =
                Math.Abs(partBox.MaxX - partBox.MinX)
                + Math.Abs(partBox.MaxY - partBox.MinY)
                + item.Width
                + item.Height
                + maxOutward;
            double dimensionClearance = HOLE_MARK_DIM_CLEARANCE_PAPER * scale;
            double markClearance = HOLE_MARK_OTHER_MARK_CLEARANCE_PAPER * scale;
            double otherHoleClearance = HOLE_MARK_OTHER_HOLE_CLEARANCE_PAPER * scale;
            double sharedAnchorTolerance = Math.Max(1.0, 0.4 * scale);
            double sharedDimensionFootTolerance = Math.Max(0.1, 0.02 * scale);
            HoleMarkCandidateV3 best = null;

            foreach (Point anchor in item.Anchors)
            {
                if (anchor == null)
                    continue;

                // Side chỉ mô tả cạnh ngoài tấm. Hướng lên/xuống thực tế được
                // chấm riêng theo tâm box để cả mark bên trái/phải vẫn ưu tiên lên.
                for (int side = 0; side < 4; side++)
                {
                    HoleMarkCandidateV3 bestForSide = null;

                    for (double outward = 0.0; outward <= maxOutward + 0.01; outward += step)
                    {
                        int tangentIndex = 0;
                        while (true)
                        {
                            double tangentMagnitude = tangentIndex * step;
                            if (tangentMagnitude > maxTangent + 0.01)
                                break;

                            int variants = tangentIndex == 0 ? 1 : 2;
                            for (int variant = 0; variant < variants; variant++)
                            {
                                double tangent = tangentIndex == 0
                                    ? 0.0
                                    : (variant == 0 ? -tangentMagnitude : tangentMagnitude);
                                HoleMarkCandidateV3 candidate = BuildHoleMarkSideCandidateV3(
                                    anchor,
                                    side,
                                    tangent,
                                    outward,
                                    partBox,
                                    boundaryGap,
                                    item.Width,
                                    item.Height
                                );

                                bestForSide = KeepBetterHoleMarkCandidateV3(
                                    candidate,
                                    item,
                                    partBox,
                                    boundaryGap,
                                    dimensionSegments,
                                    dimensionClearance,
                                    occupiedMarkBoxes,
                                    markClearance,
                                    otherHoleClearance,
                                    sharedAnchorTolerance,
                                    sharedDimensionFootTolerance,
                                    scale,
                                    bestForSide
                                );
                            }

                            tangentIndex++;
                        }

                        // Luôn thử hai tia đúng 45° trước khi xét các góc lân cận.
                        // Lưới chỉ đóng vai trò tìm lối thoát khi không gian bị chật.
                        bestForSide = ConsiderHoleMarkTargetAngleCandidatesV3(
                            anchor,
                            side,
                            outward,
                            item,
                            partBox,
                            boundaryGap,
                            dimensionSegments,
                            dimensionClearance,
                            occupiedMarkBoxes,
                            markClearance,
                            otherHoleClearance,
                            sharedAnchorTolerance,
                            sharedDimensionFootTolerance,
                            scale,
                            bestForSide
                        );

                        // Đi qua đúng endpoint chân DIM nằm trên biên tấm là hợp
                        // lệ: leader tận dụng khoảng hở tại chân dóng, không cắt
                        // phần thân của đường DIM. Giao ở vị trí khác vẫn bị phạt.
                        bestForSide = ConsiderHoleMarkDimensionFootCandidatesV3(
                            anchor,
                            side,
                            outward,
                            item,
                            partBox,
                            boundaryGap,
                            dimensionSegments,
                            dimensionClearance,
                            occupiedMarkBoxes,
                            markClearance,
                            otherHoleClearance,
                            sharedAnchorTolerance,
                            sharedDimensionFootTolerance,
                            scale,
                            bestForSide
                        );

                    }

                    // Lưới thô tìm vùng đúng; lưới mịn 0.1 mm giấy tối ưu
                    // chiều dài mà không làm chi phí tìm kiếm tăng trên toàn bản vẽ.
                    if (bestForSide != null)
                    {
                        double minOutward = Math.Max(0.0, bestForSide.OutwardOffset - step);
                        double maxRefineOutward = Math.Min(
                            maxOutward,
                            bestForSide.OutwardOffset + step
                        );
                        double minTangent = Math.Max(
                            -maxTangent,
                            bestForSide.TangentOffset - step
                        );
                        double maxRefineTangent = Math.Min(
                            maxTangent,
                            bestForSide.TangentOffset + step
                        );

                        for (
                            double outward = minOutward;
                            outward <= maxRefineOutward + 0.001;
                            outward += fineStep
                        )
                        {
                            for (
                                double tangent = minTangent;
                                tangent <= maxRefineTangent + 0.001;
                                tangent += fineStep
                            )
                            {
                                HoleMarkCandidateV3 candidate = BuildHoleMarkSideCandidateV3(
                                    anchor,
                                    side,
                                    tangent,
                                    outward,
                                    partBox,
                                    boundaryGap,
                                    item.Width,
                                    item.Height
                                );

                                bestForSide = KeepBetterHoleMarkCandidateV3(
                                    candidate,
                                    item,
                                    partBox,
                                    boundaryGap,
                                    dimensionSegments,
                                    dimensionClearance,
                                    occupiedMarkBoxes,
                                    markClearance,
                                    otherHoleClearance,
                                    sharedAnchorTolerance,
                                    sharedDimensionFootTolerance,
                                    scale,
                                    bestForSide
                                );
                            }

                            bestForSide = ConsiderHoleMarkDimensionFootCandidatesV3(
                                anchor,
                                side,
                                outward,
                                item,
                                partBox,
                                boundaryGap,
                                dimensionSegments,
                                dimensionClearance,
                                occupiedMarkBoxes,
                                markClearance,
                                otherHoleClearance,
                                sharedAnchorTolerance,
                                sharedDimensionFootTolerance,
                                scale,
                                bestForSide
                            );

                            bestForSide = ConsiderHoleMarkTargetAngleCandidatesV3(
                                anchor,
                                side,
                                outward,
                                item,
                                partBox,
                                boundaryGap,
                                dimensionSegments,
                                dimensionClearance,
                                occupiedMarkBoxes,
                                markClearance,
                                otherHoleClearance,
                                sharedAnchorTolerance,
                                sharedDimensionFootTolerance,
                                scale,
                                bestForSide
                            );
                        }

                        if (best == null || bestForSide.Score < best.Score - 0.000001)
                            best = bestForSide;
                    }
                }
            }

            return best;
        }

        private static HoleMarkCandidateV3 ConsiderHoleMarkTargetAngleCandidatesV3(
            Point anchor,
            int side,
            double outwardOffset,
            HoleMarkLayoutItemV3 item,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            List<HoleMarkSegmentV3> dimensionSegments,
            double dimensionClearance,
            List<HoleMarkRectV3> occupiedMarkBoxes,
            double markClearance,
            double otherHoleClearance,
            double sharedAnchorTolerance,
            double sharedDimensionFootTolerance,
            double scale,
            HoleMarkCandidateV3 currentBest
        )
        {
            if (anchor == null || item == null || partBox == null)
                return currentBest;

            double normalDistance;
            double halfTangentSize;
            if (side == 0)
            {
                normalDistance =
                    partBox.MaxY + boundaryGap + outwardOffset - anchor.Y;
                halfTangentSize = item.Width * 0.5;
            }
            else if (side == 1)
            {
                normalDistance =
                    anchor.X - (partBox.MinX - boundaryGap - outwardOffset);
                halfTangentSize = item.Height * 0.5;
            }
            else if (side == 2)
            {
                normalDistance =
                    partBox.MaxX + boundaryGap + outwardOffset - anchor.X;
                halfTangentSize = item.Height * 0.5;
            }
            else
            {
                normalDistance =
                    anchor.Y - (partBox.MinY - boundaryGap - outwardOffset);
                halfTangentSize = item.Width * 0.5;
            }

            if (normalDistance <= 0.000001)
                return currentBest;

            // tan(45°) = 1: contact lệch theo tiếp tuyến đúng bằng khoảng
            // cách vuông góc từ anchor đến cạnh gần nhất của hộp mark.
            double exactTangentMagnitude = normalDistance + halfTangentSize;
            int[] signs = new int[] { -1, 1 };
            foreach (int sign in signs)
            {
                HoleMarkCandidateV3 candidate = BuildHoleMarkSideCandidateV3(
                    anchor,
                    side,
                    sign * exactTangentMagnitude,
                    outwardOffset,
                    partBox,
                    boundaryGap,
                    item.Width,
                    item.Height
                );
                currentBest = KeepBetterHoleMarkCandidateV3(
                    candidate,
                    item,
                    partBox,
                    boundaryGap,
                    dimensionSegments,
                    dimensionClearance,
                    occupiedMarkBoxes,
                    markClearance,
                    otherHoleClearance,
                    sharedAnchorTolerance,
                    sharedDimensionFootTolerance,
                    scale,
                    currentBest
                );
            }

            return currentBest;
        }

        private static HoleMarkCandidateV3 KeepBetterHoleMarkCandidateV3(
            HoleMarkCandidateV3 candidate,
            HoleMarkLayoutItemV3 item,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            List<HoleMarkSegmentV3> dimensionSegments,
            double dimensionClearance,
            List<HoleMarkRectV3> occupiedMarkBoxes,
            double markClearance,
            double otherHoleClearance,
            double sharedAnchorTolerance,
            double sharedDimensionFootTolerance,
            double scale,
            HoleMarkCandidateV3 currentBest
        )
        {
            if (
                candidate == null
                || !IsHoleMarkCandidateCollisionFreeV3(
                    candidate,
                    partBox,
                    boundaryGap,
                    dimensionSegments,
                    dimensionClearance,
                    occupiedMarkBoxes,
                    markClearance,
                    item.OccupiedLeaders,
                    item.GroupHoles,
                    otherHoleClearance,
                    sharedAnchorTolerance,
                    sharedDimensionFootTolerance
                )
            )
            {
                return currentBest;
            }

            ScoreHoleMarkCandidateV3(candidate, item.CurrentAnchor, scale);
            if (
                item.RequiredVerticalRank >= 0
                && candidate.VerticalPreferenceRank != item.RequiredVerticalRank
            )
            {
                return currentBest;
            }
            if (item.RequireNoDimensionConflicts && candidate.DimensionConflictCount > 0)
                return currentBest;
            if (item.RequireAcceptableObliqueAngle && candidate.AngleQualityRank > 2)
                return currentBest;

            if (currentBest == null || candidate.Score < currentBest.Score - 0.000001)
                return candidate;
            return currentBest;
        }

        private static HoleMarkCandidateV3 ConsiderHoleMarkDimensionFootCandidatesV3(
            Point anchor,
            int side,
            double outwardOffset,
            HoleMarkLayoutItemV3 item,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            List<HoleMarkSegmentV3> dimensionSegments,
            double dimensionClearance,
            List<HoleMarkRectV3> occupiedMarkBoxes,
            double markClearance,
            double otherHoleClearance,
            double sharedAnchorTolerance,
            double sharedDimensionFootTolerance,
            double scale,
            HoleMarkCandidateV3 currentBest
        )
        {
            if (dimensionSegments == null)
                return currentBest;

            foreach (HoleMarkSegmentV3 obstacle in dimensionSegments)
            {
                if (obstacle == null)
                    continue;

                Point[] endpoints = new Point[] { obstacle.A, obstacle.B };
                foreach (Point dimensionFoot in endpoints)
                {
                    HoleMarkCandidateV3 candidate =
                        BuildHoleMarkCandidateThroughDimensionFootV3(
                            anchor,
                            side,
                            outwardOffset,
                            dimensionFoot,
                            partBox,
                            boundaryGap,
                            item.Width,
                            item.Height,
                            sharedDimensionFootTolerance
                        );
                    currentBest = KeepBetterHoleMarkCandidateV3(
                        candidate,
                        item,
                        partBox,
                        boundaryGap,
                        dimensionSegments,
                        dimensionClearance,
                        occupiedMarkBoxes,
                        markClearance,
                        otherHoleClearance,
                        sharedAnchorTolerance,
                        sharedDimensionFootTolerance,
                        scale,
                        currentBest
                    );
                }
            }

            return currentBest;
        }

        private static HoleMarkCandidateV3 BuildHoleMarkSideCandidateV3(
            Point anchor,
            int side,
            double tangentOffset,
            double outwardOffset,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            double width,
            double height
        )
        {
            HoleMarkCandidateV3 candidate = new HoleMarkCandidateV3();
            candidate.Anchor = new Point(anchor.X, anchor.Y, 0);
            candidate.Side = side;
            candidate.TangentOffset = tangentOffset;
            candidate.OutwardOffset = outwardOffset;

            if (side == 0)
            {
                candidate.Center = new Point(
                    anchor.X + tangentOffset,
                    partBox.MaxY + boundaryGap + outwardOffset + height * 0.5,
                    0
                );
            }
            else if (side == 1)
            {
                candidate.Center = new Point(
                    partBox.MinX - boundaryGap - outwardOffset - width * 0.5,
                    anchor.Y + tangentOffset,
                    0
                );
            }
            else if (side == 2)
            {
                candidate.Center = new Point(
                    partBox.MaxX + boundaryGap + outwardOffset + width * 0.5,
                    anchor.Y + tangentOffset,
                    0
                );
            }
            else
            {
                candidate.Center = new Point(
                    anchor.X + tangentOffset,
                    partBox.MinY - boundaryGap - outwardOffset - height * 0.5,
                    0
                );
            }

            candidate.Box = new HoleMarkRectV3();
            candidate.Box.MinX = candidate.Center.X - width * 0.5;
            candidate.Box.MaxX = candidate.Center.X + width * 0.5;
            candidate.Box.MinY = candidate.Center.Y - height * 0.5;
            candidate.Box.MaxY = candidate.Center.Y + height * 0.5;
            candidate.Contact = new Point(
                ClampHoleMarkValueV3(anchor.X, candidate.Box.MinX, candidate.Box.MaxX),
                ClampHoleMarkValueV3(anchor.Y, candidate.Box.MinY, candidate.Box.MaxY),
                0
            );
            candidate.LeaderLength = Distance2D(candidate.Anchor, candidate.Contact);
            return candidate;
        }

        private static HoleMarkCandidateV3 BuildHoleMarkCandidateThroughDimensionFootV3(
            Point anchor,
            int side,
            double outwardOffset,
            Point dimensionFoot,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            double width,
            double height,
            double footTolerance
        )
        {
            if (
                anchor == null
                || dimensionFoot == null
                || partBox == null
                || !IsHoleMarkDimensionFootOnSideV3(
                    dimensionFoot,
                    side,
                    partBox,
                    footTolerance
                )
            )
            {
                return null;
            }

            double contactCoordinate;
            double rayFactor;
            double tangentOffset;

            if (side == 0 || side == 3)
            {
                contactCoordinate = side == 0
                    ? partBox.MaxY + boundaryGap + outwardOffset
                    : partBox.MinY - boundaryGap - outwardOffset;
                double denominator = dimensionFoot.Y - anchor.Y;
                if (Math.Abs(denominator) <= 0.000001)
                    return null;

                rayFactor = (contactCoordinate - anchor.Y) / denominator;
                if (rayFactor < 1.0 - 0.000001)
                    return null;

                double contactX =
                    anchor.X + rayFactor * (dimensionFoot.X - anchor.X);
                double centerX;
                if (contactX < anchor.X - 0.000001)
                    centerX = contactX - width * 0.5;
                else if (contactX > anchor.X + 0.000001)
                    centerX = contactX + width * 0.5;
                else
                    centerX = anchor.X;
                tangentOffset = centerX - anchor.X;
            }
            else
            {
                contactCoordinate = side == 1
                    ? partBox.MinX - boundaryGap - outwardOffset
                    : partBox.MaxX + boundaryGap + outwardOffset;
                double denominator = dimensionFoot.X - anchor.X;
                if (Math.Abs(denominator) <= 0.000001)
                    return null;

                rayFactor = (contactCoordinate - anchor.X) / denominator;
                if (rayFactor < 1.0 - 0.000001)
                    return null;

                double contactY =
                    anchor.Y + rayFactor * (dimensionFoot.Y - anchor.Y);
                double centerY;
                if (contactY < anchor.Y - 0.000001)
                    centerY = contactY - height * 0.5;
                else if (contactY > anchor.Y + 0.000001)
                    centerY = contactY + height * 0.5;
                else
                    centerY = anchor.Y;
                tangentOffset = centerY - anchor.Y;
            }

            HoleMarkCandidateV3 candidate = BuildHoleMarkSideCandidateV3(
                anchor,
                side,
                tangentOffset,
                outwardOffset,
                partBox,
                boundaryGap,
                width,
                height
            );
            if (
                candidate == null
                || HoleMarkPointToSegmentDistanceV3(
                    dimensionFoot,
                    candidate.Anchor,
                    candidate.Contact
                ) > footTolerance
            )
            {
                return null;
            }

            return candidate;
        }

        private static bool IsHoleMarkDimensionFootOnSideV3(
            Point point,
            int side,
            HoleMarkRectV3 partBox,
            double tolerance
        )
        {
            if (point == null || partBox == null)
                return false;

            if (side == 0)
            {
                return Math.Abs(point.Y - partBox.MaxY) <= tolerance
                    && point.X >= partBox.MinX - tolerance
                    && point.X <= partBox.MaxX + tolerance;
            }
            if (side == 1)
            {
                return Math.Abs(point.X - partBox.MinX) <= tolerance
                    && point.Y >= partBox.MinY - tolerance
                    && point.Y <= partBox.MaxY + tolerance;
            }
            if (side == 2)
            {
                return Math.Abs(point.X - partBox.MaxX) <= tolerance
                    && point.Y >= partBox.MinY - tolerance
                    && point.Y <= partBox.MaxY + tolerance;
            }
            return Math.Abs(point.Y - partBox.MinY) <= tolerance
                && point.X >= partBox.MinX - tolerance
                && point.X <= partBox.MaxX + tolerance;
        }

        private static void ScoreHoleMarkCandidateV3(
            HoleMarkCandidateV3 candidate,
            Point currentAnchor,
            double scale
        )
        {
            if (candidate == null)
                return;

            double dx = Math.Abs(candidate.Contact.X - candidate.Anchor.X);
            double dy = Math.Abs(candidate.Contact.Y - candidate.Anchor.Y);
            candidate.AngleDegrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            candidate.AngleDeviation = Math.Abs(
                candidate.AngleDegrees - HOLE_MARK_TARGET_ANGLE_DEG
            );

            // Trong hệ tọa độ drawing/view này, Y lớn hơn hiển thị cao hơn trên
            // tờ bản vẽ. Dùng tâm box (không dùng Side) để mark nằm bên trái
            // hoặc phải tấm nhưng nghiêng lên vẫn được nhận đúng là "phía trên".
            Point verticalReference = candidate.Center ?? candidate.Contact;
            double verticalTolerance = Math.Max(0.5, 0.1 * Math.Max(1.0, scale));
            candidate.VerticalPreferenceRank = 1;
            if (verticalReference != null && candidate.Anchor != null)
            {
                double verticalDelta = verticalReference.Y - candidate.Anchor.Y;
                if (verticalDelta > verticalTolerance)
                    candidate.VerticalPreferenceRank = 0;
                else if (verticalDelta < -verticalTolerance)
                    candidate.VerticalPreferenceRank = 2;
            }

            bool touchesDimension = candidate.DimensionConflictCount > 0;
            const double angleTierTolerance = 0.000001;
            if (
                candidate.AngleDeviation
                <= HOLE_MARK_IDEAL_ANGLE_TOL_DEG + angleTierTolerance
            )
                candidate.AngleQualityRank = 0;
            else if (
                candidate.AngleDeviation
                <= HOLE_MARK_PREFERRED_ANGLE_TOL_DEG + angleTierTolerance
            )
                candidate.AngleQualityRank = 1;
            else if (
                candidate.AngleDeviation
                <= HOLE_MARK_OBLIQUE_ANGLE_TOL_DEG + angleTierTolerance
            )
                candidate.AngleQualityRank = 2;
            else
                candidate.AngleQualityRank = 3;

            // Ưu tiên kỹ thuật mới:
            // 1) Giữ leader xiên chấp nhận được (20..70°); ngang/dọc chỉ fallback.
            // 2) Trong nhóm xiên, né DIM rồi ưu tiên box nằm phía trên anchor.
            // 3) Góc gần 45° và chiều dài chỉ tối ưu sau các điều kiện trên.
            // Nhờ vậy 30° phía trên thắng 45° phía dưới, nhưng một leader ngang
            // phía trên không thể thắng một leader 45° hợp lý ở phía dưới.
            bool hasAcceptableObliqueAngle = candidate.AngleQualityRank <= 2;
            candidate.VisualTier = hasAcceptableObliqueAngle ? 0 : 6;
            if (touchesDimension)
                candidate.VisualTier += 3;
            candidate.VisualTier += candidate.VerticalPreferenceRank;

            // Trong cùng tầng mới cân bằng: gần 45°, số lần chạm DIM và chiều dài.
            double anchorTieBreak = currentAnchor == null
                ? 0.0
                : Distance2D(candidate.Anchor, currentAnchor) * 0.000001;
            double sideTieBreak = candidate.Side * Math.Max(1.0, scale) * 0.000001;
            double positionTieBreak =
                (Math.Abs(candidate.TangentOffset) + candidate.OutwardOffset)
                * 0.000000001;
            candidate.Score = candidate.VisualTier * 1000000000.0
                + candidate.AngleDeviation
                    * Math.Max(1.0, scale)
                    * HOLE_MARK_ANGLE_PENALTY_PAPER_PER_DEG
                + candidate.DimensionConflictCount
                    * Math.Max(1.0, scale)
                    * HOLE_MARK_DIM_CONFLICT_PENALTY_PAPER
                + candidate.LeaderLength
                + anchorTieBreak
                + sideTieBreak
                + positionTieBreak;
        }

        private static double ClampHoleMarkValueV3(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static bool IsHoleMarkCandidateCollisionFreeV3(
            HoleMarkCandidateV3 candidate,
            HoleMarkRectV3 partBox,
            double boundaryGap,
            List<HoleMarkSegmentV3> dimensionSegments,
            double dimensionClearance,
            List<HoleMarkRectV3> occupiedMarkBoxes,
            double markClearance,
            List<HoleMarkSegmentV3> occupiedLeaders,
            List<Point> groupHoles,
            double otherHoleClearance,
            double sharedAnchorTolerance,
            double sharedDimensionFootTolerance
        )
        {
            if (candidate == null || candidate.Box == null)
                return false;

            candidate.DimensionConflictCount = 0;

            // Hộp mark phải nằm hoàn toàn ngoài bounding box tấm + khoảng hở.
            if (HoleMarkRectsOverlapV3(candidate.Box, partBox, boundaryGap))
                return false;

            HoleMarkSegmentV3 leader = new HoleMarkSegmentV3();
            leader.A = candidate.Anchor;
            leader.B = candidate.Contact;

            if (dimensionSegments != null)
            {
                foreach (HoleMarkSegmentV3 obstacle in dimensionSegments)
                {
                    if (HoleMarkSegmentIntersectsRectV3(obstacle, candidate.Box, dimensionClearance))
                        return false;

                    bool sharesAnchor =
                        Distance2D(obstacle.A, candidate.Anchor) <= sharedAnchorTolerance
                        || Distance2D(obstacle.B, candidate.Anchor) <= sharedAnchorTolerance;
                    bool passesSharedDimensionFoot =
                        HoleMarkLeaderPassesSharedDimensionFootV3(
                            leader,
                            obstacle,
                            partBox,
                            sharedDimensionFootTolerance
                        );
                    if (
                        !sharesAnchor
                        && !passesSharedDimensionFoot
                        && HoleMarkSegmentDistanceV3(leader, obstacle) <= dimensionClearance
                    )
                    {
                        // Leader chạm/cắt DIM là soft constraint. Giữ ứng viên để
                        // có thể cứu góc đẹp trong bản vẽ chật, nhưng scoring sẽ
                        // luôn ưu tiên ứng viên cùng chất lượng không chạm DIM.
                        candidate.DimensionConflictCount++;
                    }
                }
            }

            if (occupiedLeaders != null)
            {
                foreach (HoleMarkSegmentV3 occupiedLeader in occupiedLeaders)
                {
                    if (occupiedLeader == null)
                        continue;

                    // Kiểm tra hai chiều: leader mới không được đi qua box cũ
                    // (đã kiểm tra ở trên), và box mới cũng không được đè lên
                    // leader đã chọn trước đó. Hai leader cũng không được cắt nhau.
                    if (
                        HoleMarkSegmentIntersectsRectV3(
                            occupiedLeader,
                            candidate.Box,
                            markClearance
                        )
                        || HoleMarkSegmentDistanceV3(leader, occupiedLeader)
                            <= markClearance
                    )
                    {
                        return false;
                    }
                }
            }

            if (occupiedMarkBoxes != null)
            {
                foreach (HoleMarkRectV3 occupied in occupiedMarkBoxes)
                {
                    if (HoleMarkRectsOverlapV3(candidate.Box, occupied, markClearance))
                        return false;

                    if (HoleMarkSegmentIntersectsRectV3(leader, occupied, markClearance))
                        return false;
                }
            }

            if (groupHoles != null)
            {
                foreach (Point hole in groupHoles)
                {
                    if (Distance2D(hole, candidate.Anchor) <= 0.5)
                        continue;

                    if (HoleMarkPointToSegmentDistanceV3(hole, leader.A, leader.B) <= otherHoleClearance)
                        return false;
                }
            }

            return true;
        }

        private static bool HoleMarkLeaderPassesSharedDimensionFootV3(
            HoleMarkSegmentV3 leader,
            HoleMarkSegmentV3 dimensionSegment,
            HoleMarkRectV3 partBox,
            double tolerance
        )
        {
            if (
                leader == null
                || leader.A == null
                || leader.B == null
                || dimensionSegment == null
                || dimensionSegment.A == null
                || dimensionSegment.B == null
                || partBox == null
            )
            {
                return false;
            }

            return HoleMarkLeaderUsesDimensionEndpointV3(
                    leader,
                    dimensionSegment.A,
                    dimensionSegment.B,
                    partBox,
                    tolerance
                )
                || HoleMarkLeaderUsesDimensionEndpointV3(
                    leader,
                    dimensionSegment.B,
                    dimensionSegment.A,
                    partBox,
                    tolerance
                );
        }

        private static bool HoleMarkLeaderUsesDimensionEndpointV3(
            HoleMarkSegmentV3 leader,
            Point endpoint,
            Point otherEndpoint,
            HoleMarkRectV3 partBox,
            double tolerance
        )
        {
            if (!IsHoleMarkPointOnPartBoundaryV3(endpoint, partBox, tolerance))
                return false;

            if (
                HoleMarkPointToSegmentDistanceV3(endpoint, leader.A, leader.B)
                > tolerance
            )
            {
                return false;
            }

            // Không cho phép leader trùng dọc theo đường dim; chỉ được gặp đúng
            // tại endpoint chung của contour và chân dim.
            return HoleMarkPointToSegmentDistanceV3(
                    otherEndpoint,
                    leader.A,
                    leader.B
                )
                > tolerance * 2.0;
        }

        private static bool IsHoleMarkPointOnPartBoundaryV3(
            Point point,
            HoleMarkRectV3 partBox,
            double tolerance
        )
        {
            if (point == null || partBox == null)
                return false;

            bool onVertical =
                (
                    Math.Abs(point.X - partBox.MinX) <= tolerance
                    || Math.Abs(point.X - partBox.MaxX) <= tolerance
                )
                && point.Y >= partBox.MinY - tolerance
                && point.Y <= partBox.MaxY + tolerance;
            bool onHorizontal =
                (
                    Math.Abs(point.Y - partBox.MinY) <= tolerance
                    || Math.Abs(point.Y - partBox.MaxY) <= tolerance
                )
                && point.X >= partBox.MinX - tolerance
                && point.X <= partBox.MaxX + tolerance;
            return onVertical || onHorizontal;
        }

        private static HoleMarkRectV3 MakeHoleMarkRectV3(Point min, Point max)
        {
            HoleMarkRectV3 result = new HoleMarkRectV3();
            result.MinX = Math.Min(min.X, max.X);
            result.MaxX = Math.Max(min.X, max.X);
            result.MinY = Math.Min(min.Y, max.Y);
            result.MaxY = Math.Max(min.Y, max.Y);
            return result;
        }

        private static bool HoleMarkRectsOverlapV3(
            HoleMarkRectV3 first,
            HoleMarkRectV3 second,
            double clearance
        )
        {
            if (first == null || second == null)
                return false;

            return first.MinX < second.MaxX + clearance
                && first.MaxX > second.MinX - clearance
                && first.MinY < second.MaxY + clearance
                && first.MaxY > second.MinY - clearance;
        }

        private static bool HoleMarkSegmentIntersectsRectV3(
            HoleMarkSegmentV3 segment,
            HoleMarkRectV3 rect,
            double clearance
        )
        {
            if (segment == null || segment.A == null || segment.B == null || rect == null)
                return false;

            double minX = rect.MinX - clearance;
            double maxX = rect.MaxX + clearance;
            double minY = rect.MinY - clearance;
            double maxY = rect.MaxY + clearance;

            if (
                HoleMarkPointInsideRectV3(segment.A, minX, maxX, minY, maxY)
                || HoleMarkPointInsideRectV3(segment.B, minX, maxX, minY, maxY)
            )
            {
                return true;
            }

            Point bottomLeft = new Point(minX, minY, 0);
            Point bottomRight = new Point(maxX, minY, 0);
            Point topRight = new Point(maxX, maxY, 0);
            Point topLeft = new Point(minX, maxY, 0);

            return HoleMarkSegmentsIntersectV3(segment.A, segment.B, bottomLeft, bottomRight)
                || HoleMarkSegmentsIntersectV3(segment.A, segment.B, bottomRight, topRight)
                || HoleMarkSegmentsIntersectV3(segment.A, segment.B, topRight, topLeft)
                || HoleMarkSegmentsIntersectV3(segment.A, segment.B, topLeft, bottomLeft);
        }

        private static bool HoleMarkPointInsideRectV3(
            Point point,
            double minX,
            double maxX,
            double minY,
            double maxY
        )
        {
            return point != null
                && point.X >= minX
                && point.X <= maxX
                && point.Y >= minY
                && point.Y <= maxY;
        }

        private static double HoleMarkSegmentDistanceV3(
            HoleMarkSegmentV3 first,
            HoleMarkSegmentV3 second
        )
        {
            if (
                first == null
                || second == null
                || first.A == null
                || first.B == null
                || second.A == null
                || second.B == null
            )
            {
                return 999999999.0;
            }

            if (HoleMarkSegmentsIntersectV3(first.A, first.B, second.A, second.B))
                return 0.0;

            return Math.Min(
                Math.Min(
                    HoleMarkPointToSegmentDistanceV3(first.A, second.A, second.B),
                    HoleMarkPointToSegmentDistanceV3(first.B, second.A, second.B)
                ),
                Math.Min(
                    HoleMarkPointToSegmentDistanceV3(second.A, first.A, first.B),
                    HoleMarkPointToSegmentDistanceV3(second.B, first.A, first.B)
                )
            );
        }

        private static double HoleMarkPointToSegmentDistanceV3(
            Point point,
            Point start,
            Point end
        )
        {
            if (point == null || start == null || end == null)
                return 999999999.0;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.000001)
                return Distance2D(point, start);

            double t =
                ((point.X - start.X) * dx + (point.Y - start.Y) * dy)
                / lengthSquared;
            if (t < 0.0)
                t = 0.0;
            else if (t > 1.0)
                t = 1.0;

            Point projection = new Point(start.X + t * dx, start.Y + t * dy, 0);
            return Distance2D(point, projection);
        }

        private static bool HoleMarkSegmentsIntersectV3(
            Point a,
            Point b,
            Point c,
            Point d
        )
        {
            double o1 = HoleMarkOrientationV3(a, b, c);
            double o2 = HoleMarkOrientationV3(a, b, d);
            double o3 = HoleMarkOrientationV3(c, d, a);
            double o4 = HoleMarkOrientationV3(c, d, b);
            const double epsilon = 0.0001;

            if (
                ((o1 > epsilon && o2 < -epsilon) || (o1 < -epsilon && o2 > epsilon))
                && ((o3 > epsilon && o4 < -epsilon) || (o3 < -epsilon && o4 > epsilon))
            )
            {
                return true;
            }

            if (Math.Abs(o1) <= epsilon && HoleMarkPointOnSegmentV3(c, a, b, epsilon))
                return true;
            if (Math.Abs(o2) <= epsilon && HoleMarkPointOnSegmentV3(d, a, b, epsilon))
                return true;
            if (Math.Abs(o3) <= epsilon && HoleMarkPointOnSegmentV3(a, c, d, epsilon))
                return true;
            if (Math.Abs(o4) <= epsilon && HoleMarkPointOnSegmentV3(b, c, d, epsilon))
                return true;

            return false;
        }

        private static double HoleMarkOrientationV3(Point a, Point b, Point c)
        {
            return (b.X - a.X) * (c.Y - a.Y)
                - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool HoleMarkPointOnSegmentV3(
            Point point,
            Point start,
            Point end,
            double tolerance
        )
        {
            return point.X >= Math.Min(start.X, end.X) - tolerance
                && point.X <= Math.Max(start.X, end.X) + tolerance
                && point.Y >= Math.Min(start.Y, end.Y) - tolerance
                && point.Y <= Math.Max(start.Y, end.Y) + tolerance;
        }

        #endregion

        #region 08 - SCALE / ARRANGE / SELECT VIEW

        private static object TryGetObjectProperty(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return null;

                return prop.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetObjectProperty(object obj, string propertyName, object value)
        {
            try
            {
                if (obj == null || value == null)
                    return false;

                PropertyInfo prop = obj.GetType()
                    .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanWrite)
                    return false;

                if (prop.PropertyType != value.GetType())
                    return false;

                prop.SetValue(obj, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyAutoScaleByPartLength(
            Model model,
            Drawing drawing,
            ModelPart part,
            View view
        )
        {
            TransformationPlane oldPlane = null;

            try
            {
                if (model == null || drawing == null || part == null || view == null)
                    return;

                double manualScale;
                if (TTSK_AutoDim_Plates.ManualDrawingScaleOverride.TryGet(out manualScale))
                {
                    SetViewScale(view, manualScale);
                    return;
                }

                double sheetWidth;
                double sheetHeight;

                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                double paperLength = Math.Max(sheetWidth, sheetHeight);
                double marginTotal = GetAutoScaleMarginTotal(sheetWidth, sheetHeight);
                double usablePaperLength = paperLength - marginTotal;

                if (usablePaperLength <= 1.0)
                    return;

                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

                TransformationPlane viewPlane = new TransformationPlane(
                    view.DisplayCoordinateSystem
                );
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(viewPlane);

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return;

                double partLength = Math.Abs(solid.MaximumPoint.X - solid.MinimumPoint.X);

                if (partLength <= 1.0)
                    return;

                double requiredModelLength = partLength + AUTO_SCALE_DIM_VERTICAL_RESERVE;
                double requiredScale = requiredModelLength / usablePaperLength;
                double selectedScale = ChooseAllowedViewScale(requiredScale);

                SetViewScale(view, selectedScale);
            }
            catch { }
            finally
            {
                try
                {
                    if (model != null && oldPlane != null)
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }
        }

        private static double GetAutoScaleMarginTotal(double sheetWidth, double sheetHeight)
        {
            if (
                IsSheetSize(
                    sheetWidth,
                    sheetHeight,
                    A1_SHEET_WIDTH,
                    A1_SHEET_HEIGHT,
                    SHEET_SIZE_TOLERANCE
                )
            )
                return AUTO_SCALE_A1_MARGIN_TOTAL;

            if (
                IsSheetSize(
                    sheetWidth,
                    sheetHeight,
                    A3_SHEET_WIDTH,
                    A3_SHEET_HEIGHT,
                    SHEET_SIZE_TOLERANCE
                )
            )
                return AUTO_SCALE_A3_MARGIN_TOTAL;

            return AUTO_SCALE_DEFAULT_MARGIN_TOTAL;
        }

        private static bool IsSheetSize(
            double sheetWidth,
            double sheetHeight,
            double targetWidth,
            double targetHeight,
            double tolerance
        )
        {
            return (
                    Math.Abs(sheetWidth - targetWidth) <= tolerance
                    && Math.Abs(sheetHeight - targetHeight) <= tolerance
                )
                || (
                    Math.Abs(sheetWidth - targetHeight) <= tolerance
                    && Math.Abs(sheetHeight - targetWidth) <= tolerance
                );
        }

        private static double ChooseAllowedViewScale(double requiredScale)
        {
            double[] allowedScales = new double[] { 5.0, 10.0, 15.0, 20.0, 30.0 };

            foreach (double scale in allowedScales)
            {
                if (scale >= requiredScale)
                    return scale;
            }

            return 30.0;
        }

        private static bool TryGetDrawingSheetSize(
            Drawing drawing,
            out double width,
            out double height
        )
        {
            width = 0.0;
            height = 0.0;

            if (drawing == null)
                return false;

            try
            {
                object layout = TryGetObjectProperty(drawing, "Layout");

                if (layout == null)
                    return false;

                object sheetSize = TryGetObjectProperty(layout, "SheetSize");

                if (sheetSize == null)
                    return false;

                object w = TryGetObjectProperty(sheetSize, "Width");
                object h = TryGetObjectProperty(sheetSize, "Height");

                if (w == null || h == null)
                    return false;

                width = Convert.ToDouble(w);
                height = Convert.ToDouble(h);

                return width > 0.0 && height > 0.0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetViewScale(View view, double scale)
        {
            if (view == null)
                return;

            try
            {
                // Ưu tiên set scale trong Attributes.
                object attrs = null;

                try
                {
                    attrs = view.Attributes;
                }
                catch
                {
                    attrs = null;
                }

                if (attrs != null)
                {
                    SetScaleProperties(attrs, scale);
                }

                // Nếu Tekla có property Scale trực tiếp trên View thì set luôn.
                SetScaleProperties(view, scale);

                try
                {
                    view.Modify();
                }
                catch { }
            }
            catch { }
        }

        private static void SetScaleProperties(object obj, double scale)
        {
            if (obj == null)
                return;

            try
            {
                PropertyInfo[] props = obj.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (PropertyInfo prop in props)
                {
                    if (!prop.CanWrite)
                        continue;

                    string name = prop.Name.ToUpper();

                    if (name.IndexOf("SCALE") < 0)
                        continue;

                    try
                    {
                        Type t = prop.PropertyType;

                        if (t == typeof(double))
                        {
                            prop.SetValue(obj, scale, null);
                        }
                        else if (t == typeof(int))
                        {
                            prop.SetValue(obj, Convert.ToInt32(scale), null);
                        }
                        else if (t == typeof(float))
                        {
                            prop.SetValue(obj, Convert.ToSingle(scale), null);
                        }
                        else
                        {
                            // Một số Tekla object scale là class có Numerator/Denominator hoặc X/Y.
                            object scaleObj = prop.GetValue(obj, null);

                            if (scaleObj != null)
                            {
                                TrySetObjectProperty(
                                    scaleObj,
                                    "Denominator",
                                    Convert.ToInt32(scale)
                                );
                                TrySetObjectProperty(scaleObj, "Numerator", 1);
                                TrySetObjectProperty(scaleObj, "X", 1.0);
                                TrySetObjectProperty(scaleObj, "Y", scale);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void UpdateDrawingTitle3ScaleFromViews(Drawing drawing, List<View> views)
        {
            try
            {
                if (drawing == null || views == null || views.Count == 0)
                    return;

                string scaleText = "";

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    double scale = GetViewScaleNumberForTitle3(view);
                    if (scale > 0.0)
                    {
                        scaleText = FormatScaleForTitle3(scale);
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(scaleText))
                    return;

                if (SetDrawingTitle3Text(drawing, scaleText))
                {
                    try
                    {
                        drawing.Modify();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static double GetViewScaleNumberForTitle3(View view)
        {
            try
            {
                if (view == null)
                    return 0.0;

                double scale = GetScaleNumberFromObject(view);
                if (scale > 0.0)
                    return scale;

                object attrs = null;
                try
                {
                    attrs = view.Attributes;
                }
                catch
                {
                    attrs = null;
                }

                scale = GetScaleNumberFromObject(attrs);
                if (scale > 0.0)
                    return scale;
            }
            catch { }

            return 0.0;
        }

        private static double GetScaleNumberFromObject(object obj)
        {
            if (obj == null)
                return 0.0;

            try
            {
                PropertyInfo[] props = obj.GetType()
                    .GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                foreach (PropertyInfo prop in props)
                {
                    if (!prop.CanRead)
                        continue;

                    if (prop.GetIndexParameters().Length > 0)
                        continue;

                    string name = prop.Name;
                    if (name.IndexOf("Scale", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        object value = prop.GetValue(obj, null);
                        double scale = ConvertScaleValueToNumber(value);

                        if (scale > 0.0)
                            return scale;
                    }
                    catch { }
                }
            }
            catch { }

            return 0.0;
        }

        private static double ConvertScaleValueToNumber(object value)
        {
            if (value == null)
                return 0.0;

            try
            {
                if (value is int || value is double || value is float || value is decimal)
                {
                    double number = Convert.ToDouble(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture
                    );

                    if (number > 0.0)
                        return number;
                }

                object denominator = TryGetObjectProperty(value, "Denominator");
                if (denominator != null)
                {
                    double d = Convert.ToDouble(
                        denominator,
                        System.Globalization.CultureInfo.InvariantCulture
                    );

                    if (d > 0.0)
                        return d;
                }

                object y = TryGetObjectProperty(value, "Y");
                if (y != null)
                {
                    double yy = Convert.ToDouble(
                        y,
                        System.Globalization.CultureInfo.InvariantCulture
                    );

                    if (yy > 0.0)
                        return yy;
                }

                string text = value.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return 0.0;

                text = text.Trim();

                int colon = text.IndexOf(":");
                if (colon >= 0 && colon < text.Length - 1)
                    text = text.Substring(colon + 1);

                text = text.Replace(" ", "").Replace(",", ".");

                double numberText = 0.0;
                if (
                    double.TryParse(
                        text,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out numberText
                    )
                )
                {
                    if (numberText > 0.0)
                        return numberText;
                }
            }
            catch { }

            return 0.0;
        }

        private static string FormatScaleForTitle3(double scale)
        {
            try
            {
                double rounded = Math.Round(scale, 3);

                if (Math.Abs(rounded - Math.Round(rounded)) < 0.001)
                    return "1:" + ((int)Math.Round(rounded)).ToString();

                return "1:"
                    + rounded.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }

        private static bool SetDrawingTitle3Text(Drawing drawing, string text)
        {
            if (drawing == null || string.IsNullOrWhiteSpace(text))
                return false;

            string[] propNames = new string[] { "Title3", "TITLE3", "TitleThree", "DrawingTitle3" };

            foreach (string propName in propNames)
            {
                if (TrySetObjectPropertyFlexible(drawing, propName, text))
                    return true;
            }

            return false;
        }

        private static bool TrySetObjectPropertyFlexible(
            object obj,
            string propertyName,
            string value
        )
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                    return false;

                PropertyInfo prop = obj.GetType()
                    .GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                if (prop == null || !prop.CanWrite)
                    return false;

                if (prop.GetIndexParameters().Length > 0)
                    return false;

                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(obj, value, null);
                    return true;
                }

                object current = null;
                try
                {
                    current = prop.GetValue(obj, null);
                }
                catch
                {
                    current = null;
                }

                if (current != null)
                {
                    if (TrySetObjectPropertyFlexible(current, "Text", value))
                        return true;

                    if (TrySetObjectPropertyFlexible(current, "Value", value))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static void CenterProcessedViewsBySheetSize(Drawing drawing, List<View> views)
        {
            try
            {
                if (drawing == null || views == null || views.Count == 0)
                    return;

                double sheetWidth;
                double sheetHeight;

                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                if (sheetWidth <= 1.0 || sheetHeight <= 1.0)
                    return;

                double marginTotal = GetAutoScaleMarginTotal(sheetWidth, sheetHeight);
                double margin = marginTotal * 0.5;

                double usableMinX = margin;
                double usableMaxX = sheetWidth - margin;
                double usableMinY = margin;
                double usableMaxY = sheetHeight - margin;

                if (usableMaxX <= usableMinX + 1.0 || usableMaxY <= usableMinY + 1.0)
                    return;

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                double maxY = double.MinValue;
                int count = 0;

                foreach (View v in views)
                {
                    if (v == null)
                        continue;

                    ViewPaperBox box;
                    // MOVE CENTER: dùng khung tím RestrictionBox làm nguồn, không dùng khung xanh bounding box.
                    if (!TryGetViewPurplePaperBox(v, out box))
                        continue;

                    minX = Math.Min(minX, box.MinX);
                    maxX = Math.Max(maxX, box.MaxX);
                    minY = Math.Min(minY, box.MinY);
                    maxY = Math.Max(maxY, box.MaxY);
                    count++;
                }

                if (count == 0)
                    return;

                if (
                    minX == double.MaxValue
                    || maxX == double.MinValue
                    || minY == double.MaxValue
                    || maxY == double.MinValue
                )
                    return;

                if (maxX <= minX + 1.0 || maxY <= minY + 1.0)
                    return;

                double clusterCenterX = (minX + maxX) * 0.5;
                double clusterCenterY = (minY + maxY) * 0.5;

                double targetCenterX = (usableMinX + usableMaxX) * 0.5;
                double targetCenterY = (usableMinY + usableMaxY) * 0.5;

                double dx = targetCenterX - clusterCenterX;
                double dy = targetCenterY - clusterCenterY;

                if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1)
                    return;

                // Bảo vệ nhẹ: nếu giá trị bất thường lớn hơn khổ giấy nhiều lần thì bỏ qua.
                if (Math.Abs(dx) > sheetWidth * 2.0 || Math.Abs(dy) > sheetHeight * 2.0)
                    return;

                foreach (View v in views)
                {
                    if (v == null)
                        continue;

                    try
                    {
                        TrySetFixedViewPlacing(v, true);

                        Point oldOrigin = v.Origin;
                        if (oldOrigin == null)
                            continue;

                        v.Origin = new Point(oldOrigin.X + dx, oldOrigin.Y + dy, oldOrigin.Z);

                        v.Modify();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void ArrangeProcessedViewsVerticalGap(
            View topView,
            View frontView,
            double gap
        )
        {
            ArrangeSinglePlateTopFrontBySemanticRole(topView, frontView, gap);
        }

        private static void ForceFinalEqualArrangeTopFrontGap15(
            View topView,
            View frontView,
            double gap
        )
        {
            ArrangeSinglePlateTopFrontBySemanticRole(topView, frontView, gap);
        }

        private static void ArrangeSinglePlateTopFrontBySemanticRole(
            View topView,
            View frontView,
            double gap
        )
        {
            try
            {
                if (topView == null || frontView == null)
                    return;

                if (object.ReferenceEquals(topView, frontView))
                    return;

                try
                {
                    topView.Select();
                }
                catch { }
                try
                {
                    frontView.Select();
                }
                catch { }

                TrySetFixedViewPlacing(topView, true);
                TrySetFixedViewPlacing(frontView, true);

                ViewPaperBox topBox;
                ViewPaperBox frontBox;

                // Khung xanh vẫn là nguồn đúng để giữ GAP tính cả DIM/mark.
                // Vai trò TOP/FRONT đã được khóa bằng ViewType, không lấy từ box.
                if (
                    !TryGetViewPaperBox(topView, out topBox)
                    || !TryGetViewPaperBox(frontView, out frontBox)
                )
                    return;

                if (
                    topBox == null
                    || frontBox == null
                    || topBox.Width <= 1.0
                    || topBox.Height <= 1.0
                    || frontBox.Width <= 1.0
                    || frontBox.Height <= 1.0
                )
                    return;

                double[] targetCenters = BuildSinglePlateTopFrontTargetCenters(
                    topBox.MinY,
                    topBox.MaxY,
                    frontBox.MinY,
                    frontBox.MaxY,
                    gap
                );
                if (targetCenters == null || targetCenters.Length != 2)
                    return;

                double topMoveY = targetCenters[0] - (topBox.MinY + topBox.MaxY) * 0.5;
                double frontMoveY = targetCenters[1] - (frontBox.MinY + frontBox.MaxY) * 0.5;

                // Tính xong cả hai bước trước khi move để không bao giờ chỉ move một view.
                // Giữ cùng giới hạn an toàn đã được Shape H kiểm chứng.
                if (Math.Abs(topMoveY) > 300.0 || Math.Abs(frontMoveY) > 300.0)
                    return;

                if (Math.Abs(topMoveY) >= 0.1)
                    MoveViewByOriginOnly(topView, 0.0, topMoveY);
                if (Math.Abs(frontMoveY) >= 0.1)
                    MoveViewByOriginOnly(frontView, 0.0, frontMoveY);

                try
                {
                    topView.Modify();
                }
                catch { }

                try
                {
                    frontView.Modify();
                }
                catch { }
            }
            catch { }
        }

        private static double[] BuildSinglePlateTopFrontTargetCenters(
            double topMinY,
            double topMaxY,
            double frontMinY,
            double frontMaxY,
            double gap
        )
        {
            double topHeight = Math.Abs(topMaxY - topMinY);
            double frontHeight = Math.Abs(frontMaxY - frontMinY);
            if (topHeight <= 1.0 || frontHeight <= 1.0)
                return null;

            if (gap < 0.0)
                gap = 0.0;

            // Dựng lại cả stack quanh tâm cụm hiện tại giống Shape H.
            // Công thức không phụ thuộc TOP/FRONT đang nằm ở đâu hoặc box nào cao hơn.
            double currentMinY = Math.Min(topMinY, frontMinY);
            double currentMaxY = Math.Max(topMaxY, frontMaxY);
            double currentCenterY = (currentMinY + currentMaxY) * 0.5;
            double totalStackHeight = topHeight + gap + frontHeight;

            double topTargetCenterY = currentCenterY + totalStackHeight * 0.5 - topHeight * 0.5;
            double frontTargetCenterY = currentCenterY - totalStackHeight * 0.5 + frontHeight * 0.5;

            return new double[] { topTargetCenterY, frontTargetCenterY };
        }

        private class ViewPaperBox
        {
            public View View;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double Width;
            public double Height;
        }

        private static bool TryGetViewPurplePaperBox(View view, out ViewPaperBox box)
        {
            box = null;

            try
            {
                if (view == null)
                    return false;

                // CHỈ LẤY KHUNG TÍM RestrictionBox.
                // Tuyệt đối không fallback sang GetAxisAlignedBoundingBox vì đó là khung xanh/view frame,
                // khi view xanh to ra sẽ kéo tâm bị lệch như ảnh bạn gửi.
                AABB rb = null;
                try
                {
                    rb = view.RestrictionBox;
                }
                catch
                {
                    rb = null;
                }

                if (rb == null || rb.MinPoint == null || rb.MaxPoint == null)
                    return false;

                Point origin = null;
                try
                {
                    origin = view.Origin;
                }
                catch
                {
                    origin = null;
                }

                if (origin == null)
                    return false;

                double scale = GetViewScaleNumberForTitle3(view);
                if (scale <= 0.0)
                    scale = 1.0;

                // RestrictionBox là tọa độ local/model của view.
                // Quy đổi sang paper coordinate bằng Origin + RestrictionBox / Scale.
                // Không cộng FrameOrigin vì FrameOrigin thuộc khung xanh/frame; cộng vào sẽ làm tâm tím bị lệch theo khung xanh.
                double x1 = origin.X + rb.MinPoint.X / scale;
                double y1 = origin.Y + rb.MinPoint.Y / scale;
                double x2 = origin.X + rb.MaxPoint.X / scale;
                double y2 = origin.Y + rb.MaxPoint.Y / scale;

                box = new ViewPaperBox();
                box.View = view;
                box.MinX = Math.Min(x1, x2);
                box.MaxX = Math.Max(x1, x2);
                box.MinY = Math.Min(y1, y2);
                box.MaxY = Math.Max(y1, y2);
                box.Width = Math.Abs(box.MaxX - box.MinX);
                box.Height = Math.Abs(box.MaxY - box.MinY);

                if (box.Width <= 0.5 || box.Height <= 0.5)
                    return false;

                // Chặn box tím bất thường để không ăn nhầm khung xanh/table.
                if (box.Width > 1000.0 || box.Height > 1000.0)
                    return false;

                return true;
            }
            catch
            {
                box = null;
                return false;
            }
        }

        /* KHUNG XANH: chỉ dùng cho ARRANGE gap, không dùng cho MOVE CENTER */
        private static bool TryGetViewPaperBox(View view, out ViewPaperBox box)
        {
            box = null;

            try
            {
                // GetAxisAlignedBoundingBox() là kích thước/khung view trên paper coordinates.
                AABB bb = view.GetAxisAlignedBoundingBox();

                if (bb == null || bb.MinPoint == null || bb.MaxPoint == null)
                    return false;

                Point min = bb.MinPoint;
                Point max = bb.MaxPoint;

                box = new ViewPaperBox();
                box.View = view;
                box.MinX = Math.Min(min.X, max.X);
                box.MaxX = Math.Max(min.X, max.X);
                box.MinY = Math.Min(min.Y, max.Y);
                box.MaxY = Math.Max(min.Y, max.Y);
                box.Width = Math.Abs(box.MaxX - box.MinX);
                box.Height = Math.Abs(box.MaxY - box.MinY);

                return box.Width > 0.0 && box.Height > 0.0;
            }
            catch
            {
                try
                {
                    Point origin = view.Origin;
                    Vector frame = view.FrameOrigin;

                    double x1 = origin.X + frame.X;
                    double y1 = origin.Y + frame.Y;
                    double x2 = x1 + view.Width;
                    double y2 = y1 + view.Height;

                    box = new ViewPaperBox();
                    box.View = view;
                    box.MinX = Math.Min(x1, x2);
                    box.MaxX = Math.Max(x1, x2);
                    box.MinY = Math.Min(y1, y2);
                    box.MaxY = Math.Max(y1, y2);
                    box.Width = Math.Abs(box.MaxX - box.MinX);
                    box.Height = Math.Abs(box.MaxY - box.MinY);

                    return box.Width > 0.0 && box.Height > 0.0;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static void MoveViewByOriginOnly(View view, double dx, double dy)
        {
            if (view == null)
                return;

            // CHỈ ĐỔI DRAWING VIEW ORIGIN.
            // Không gọi model object, không sửa model 3D.

            try
            {
                view.Select();

                Point oldOrigin = view.Origin;

                if (oldOrigin == null)
                    return;

                if (Math.Abs(dx) > 300.0 || Math.Abs(dy) > 300.0)
                    return;

                view.Origin = new Point(oldOrigin.X + dx, oldOrigin.Y + dy, oldOrigin.Z);

                view.Modify();
                return;
            }
            catch { }

            try
            {
                Vector move = new Vector(dx, dy, 0);

                // Nếu môi trường Tekla expose MoveObjectRelative cho View thì dùng fallback này.
                MethodInfo m = view.GetType()
                    .GetMethod("MoveObjectRelative", BindingFlags.Public | BindingFlags.Instance);

                if (m != null)
                {
                    m.Invoke(view, new object[] { move });
                    view.Modify();
                }
            }
            catch { }
        }

        private static void TrySetFixedViewPlacing(View view, bool fixedPlacing)
        {
            try
            {
                if (view == null || view.Attributes == null)
                    return;

                PropertyInfo p = view
                    .Attributes.GetType()
                    .GetProperty("FixedViewPlacing", BindingFlags.Public | BindingFlags.Instance);

                if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                {
                    p.SetValue(view.Attributes, fixedPlacing, null);
                    view.Modify();
                }
            }
            catch { }
        }

        private static void SelectProcessedViews(DrawingHandler dh, List<View> views)
        {
            try
            {
                if (views == null || views.Count == 0)
                    return;

                ArrayList objectsToSelect = new ArrayList();

                foreach (View view in views)
                {
                    if (view != null)
                        objectsToSelect.Add(view);
                }

                if (objectsToSelect.Count == 0)
                    return;

                DrawingObjectSelector selector = dh.GetDrawingObjectSelector();
                selector.SelectObjects(objectsToSelect, false);
            }
            catch { }
        }
        #endregion

        #region 99 - AUTO FIX BAD JAPANESE BOLT MARK TO HOLE MARK

        private const string PHU_BAD_BOLT_MARK_TEXT_1 = "不要";
        private const string PHU_BAD_BOLT_MARK_TEXT_2 = "消してください";
        private const string PHU_BOLT_MARK_FONT_NAME = "MS UI Gothic";
        private const double PHU_BOLT_MARK_FONT_HEIGHT = 3.5;

        private static void AutoFixBadJapaneseBoltMarks(Drawing drawing)
        {
            try
            {
                if (drawing == null)
                    return;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return;

                int fixedCount = 0;

                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    ViewBase view = views.Current as ViewBase;
                    if (view == null)
                        continue;

                    DrawingObjectEnumerator objects = null;

                    try
                    {
                        objects = view.GetAllObjects();
                    }
                    catch
                    {
                        objects = null;
                    }

                    if (objects == null)
                        continue;

                    while (objects.MoveNext())
                    {
                        Mark mark = objects.Current as Mark;
                        if (mark == null)
                            continue;

                        try
                        {
                            if (!IsBadJapaneseBoltMarkForAutoFix(mark))
                                continue;

                            ReplaceBadBoltMarkContentFromRealDump(mark);
                            SetBadBoltMarkStyleForAutoFix(mark);

                            mark.Modify();
                            fixedCount++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static bool IsBadJapaneseBoltMarkForAutoFix(Mark mark)
        {
            try
            {
                string text = GetMarkTextForAutoFix(mark);

                if (string.IsNullOrEmpty(text))
                    return false;

                return text.Contains(PHU_BAD_BOLT_MARK_TEXT_1)
                    || text.Contains(PHU_BAD_BOLT_MARK_TEXT_2);
            }
            catch
            {
                return false;
            }
        }

        private static string GetMarkTextForAutoFix(Mark mark)
        {
            List<string> texts = new List<string>();

            try
            {
                if (mark != null && mark.Attributes != null)
                    CollectMarkTextsForAutoFix(mark.Attributes.Content, texts);
            }
            catch { }

            return string.Join(" ", texts.ToArray());
        }

        private static void CollectMarkTextsForAutoFix(object contentObj, List<string> output)
        {
            if (contentObj == null || output == null)
                return;

            IEnumerable enumerable = contentObj as IEnumerable;

            if (enumerable == null || contentObj is string)
            {
                string s = contentObj as string;

                if (!string.IsNullOrEmpty(s))
                    output.Add(s);

                return;
            }

            foreach (object item in enumerable)
            {
                if (item == null)
                    continue;

                object textValue = GetPropertyValueForAutoFix(item, "Text");
                if (textValue != null)
                    output.Add(textValue.ToString());

                object valueValue = GetPropertyValueForAutoFix(item, "Value");
                if (valueValue != null)
                    output.Add(valueValue.ToString());

                object stringValue = GetPropertyValueForAutoFix(item, "String");
                if (stringValue != null)
                    output.Add(stringValue.ToString());

                object childContent = GetPropertyValueForAutoFix(item, "Content");
                if (childContent != null)
                    CollectMarkTextsForAutoFix(childContent, output);
            }
        }

        private static void ReplaceBadBoltMarkContentFromRealDump(Mark mark)
        {
            if (mark == null || mark.Attributes == null)
                return;

            ContainerElement content = new ContainerElement();

            // Theo dump mark HOLE chuẩn:
            // ITEM 1: PropertyElement, Name = albl_Number_of_bolts, PropertyType = GR_BOLT_NUMBER
            // ITEM 2: SpaceElement
            // ITEM 3: TextElement "-"
            // ITEM 4: SpaceElement
            // ITEM 5: TextElement "φ"
            // ITEM 6: SpaceElement
            // ITEM 7: LengthPropertyElement, Name = HOLE.DIAMETER, PropertyType = GR_HOLE_DIAMETER, Precision = 0
            AddElementForceForAutoFix(
                content,
                CreatePropertyElementFromDumpForAutoFix(
                    "Tekla.Structures.Drawing.PropertyElement",
                    "albl_Number_of_bolts",
                    "GR_BOLT_NUMBER"
                )
            );

            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());
            AddElementForceForAutoFix(content, MakeTextElementForAutoFix("-"));
            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());
            AddElementForceForAutoFix(content, MakeTextElementForAutoFix("φ"));
            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());

            AddElementForceForAutoFix(
                content,
                CreateLengthPropertyElementFromDumpForAutoFix("HOLE.DIAMETER", "GR_HOLE_DIAMETER")
            );

            mark.Attributes.Content = content;
        }

        private static object CreatePropertyElementFromDumpForAutoFix(
            string elementTypeName,
            string name,
            string propertyTypeEnumName
        )
        {
            Type elementType = FindTeklaDrawingTypeForAutoFix(elementTypeName);

            if (elementType == null)
                return MakeTextElementForAutoFix(name);

            object obj = null;

            try
            {
                obj = Activator.CreateInstance(elementType, true);
            }
            catch { }

            if (obj == null)
            {
                try
                {
                    ConstructorInfo ci = elementType.GetConstructor(new Type[] { typeof(string) });

                    if (ci != null)
                        obj = ci.Invoke(new object[] { name });
                }
                catch { }
            }

            if (obj == null)
                return MakeTextElementForAutoFix(name);

            ForcePropertyElementFieldsForAutoFix(obj, name, propertyTypeEnumName);
            SetElementFontForAutoFix(obj);

            return obj;
        }

        private static object CreateLengthPropertyElementFromDumpForAutoFix(
            string name,
            string propertyTypeEnumName
        )
        {
            Type elementType = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.LengthPropertyElement"
            );

            if (elementType == null)
            {
                return CreatePropertyElementFromDumpForAutoFix(
                    "Tekla.Structures.Drawing.PropertyElement",
                    name,
                    propertyTypeEnumName
                );
            }

            object obj = null;

            try
            {
                obj = Activator.CreateInstance(elementType, true);
            }
            catch { }

            if (obj == null)
            {
                try
                {
                    ConstructorInfo ci = elementType.GetConstructor(new Type[] { typeof(string) });

                    if (ci != null)
                        obj = ci.Invoke(new object[] { name });
                }
                catch { }
            }

            if (obj == null)
            {
                return CreatePropertyElementFromDumpForAutoFix(
                    "Tekla.Structures.Drawing.PropertyElement",
                    name,
                    propertyTypeEnumName
                );
            }

            ForcePropertyElementFieldsForAutoFix(obj, name, propertyTypeEnumName);
            ForceLengthUnitAutomaticPrecision0ForAutoFix(obj);
            SetElementFontForAutoFix(obj);

            return obj;
        }

        private static void ForcePropertyElementFieldsForAutoFix(
            object obj,
            string name,
            string propertyTypeEnumName
        )
        {
            if (obj == null)
                return;

            TrySetFieldForAutoFix(obj, "_Name", name);
            TrySetPropertyForAutoFix(obj, "Name", name);

            // Để rỗng để Tekla tự lấy value thật từ bolt liên kết.
            TrySetFieldForAutoFix(obj, "_Value", "");
            TrySetPropertyForAutoFix(obj, "Value", "");

            object propType = CreatePropertyElementTypeForAutoFix(propertyTypeEnumName);

            if (propType != null)
            {
                TrySetFieldForAutoFix(obj, "_Type", propType);
                TrySetPropertyForAutoFix(obj, "PropertyType", propType);
            }
        }

        private static object CreatePropertyElementTypeForAutoFix(string enumName)
        {
            Type propElementType = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.PropertyElement+PropertyElementType"
            );

            if (propElementType == null)
                return null;

            object propTypeObj = null;

            try
            {
                propTypeObj = Activator.CreateInstance(propElementType, true);
            }
            catch { }

            if (propTypeObj == null)
                return null;

            Type enumType = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.PropertyElement+PropertyElementType+PropertyTypes"
            );

            if (enumType == null)
                return null;

            object enumValue = null;

            try
            {
                enumValue = Enum.Parse(enumType, enumName, true);
            }
            catch { }

            if (enumValue == null)
                return null;

            TrySetFieldForAutoFix(propTypeObj, "_PropertyType", enumValue);
            TrySetPropertyForAutoFix(propTypeObj, "PropertyType", enumValue);

            return propTypeObj;
        }

        private static void ForceLengthUnitAutomaticPrecision0ForAutoFix(object lengthElement)
        {
            try
            {
                object unit = GetPropertyValueForAutoFix(lengthElement, "Unit");

                if (unit == null)
                {
                    Type unitType = FindTeklaDrawingTypeForAutoFix(
                        "Tekla.Structures.Drawing.UnitAttributes"
                    );

                    if (unitType != null)
                    {
                        try
                        {
                            unit = Activator.CreateInstance(unitType, true);
                        }
                        catch { }
                    }
                }

                if (unit == null)
                    return;

                SetEnumPropertyOrFieldForAutoFix(
                    unit,
                    "_Unit",
                    "Unit",
                    "Tekla.Structures.Drawing.Units",
                    "Automatic"
                );

                SetEnumPropertyOrFieldForAutoFix(
                    unit,
                    "_Format",
                    "Format",
                    "Tekla.Structures.Drawing.FormatTypes",
                    "Automatic"
                );

                TrySetFieldForAutoFix(unit, "_Precision", 0);
                TrySetPropertyForAutoFix(unit, "Precision", 0);

                TrySetFieldForAutoFix(lengthElement, "_Unit", unit);
                TrySetPropertyForAutoFix(lengthElement, "Unit", unit);
            }
            catch { }
        }

        private static void SetEnumPropertyOrFieldForAutoFix(
            object obj,
            string fieldName,
            string propName,
            string enumTypeName,
            string enumName
        )
        {
            Type enumType = FindTeklaDrawingTypeForAutoFix(enumTypeName);

            if (enumType == null)
                return;

            object enumValue = null;

            try
            {
                enumValue = Enum.Parse(enumType, enumName, true);
            }
            catch { }

            if (enumValue == null)
                return;

            TrySetFieldForAutoFix(obj, fieldName, enumValue);
            TrySetPropertyForAutoFix(obj, propName, enumValue);
        }

        private static object CreateSpaceElementForAutoFix()
        {
            Type t = FindTeklaDrawingTypeForAutoFix("Tekla.Structures.Drawing.SpaceElement");

            if (t == null)
                return MakeTextElementForAutoFix(" ");

            try
            {
                return Activator.CreateInstance(t, true);
            }
            catch
            {
                return MakeTextElementForAutoFix(" ");
            }
        }

        private static TextElement MakeTextElementForAutoFix(string text)
        {
            TextElement te = new TextElement(text);
            SetElementFontForAutoFix(te);
            return te;
        }

        private static void AddElementForceForAutoFix(ContainerElement content, object element)
        {
            if (content == null || element == null)
                return;

            try
            {
                content.Add((dynamic)element);
                return;
            }
            catch { }

            try
            {
                MethodInfo[] methods = content
                    .GetType()
                    .GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                foreach (MethodInfo m in methods)
                {
                    if (m.Name != "Add")
                        continue;

                    ParameterInfo[] ps = m.GetParameters();

                    if (ps.Length != 1)
                        continue;

                    try
                    {
                        m.Invoke(content, new object[] { element });
                        return;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static Type FindTeklaDrawingTypeForAutoFix(string fullName)
        {
            try
            {
                Type t = Type.GetType(fullName + ", Tekla.Structures.Drawing");

                if (t != null)
                    return t;
            }
            catch { }

            try
            {
                System.Reflection.Assembly asm = typeof(Mark).Assembly;

                Type t = asm.GetType(fullName, false, true);

                if (t != null)
                    return t;

                Type[] all = asm.GetTypes();
                string shortName = fullName.Substring(fullName.LastIndexOf('.') + 1);

                foreach (Type x in all)
                {
                    if (x.FullName == fullName)
                        return x;

                    if (x.Name == shortName)
                        return x;
                }
            }
            catch { }

            return null;
        }

        private static void SetBadBoltMarkStyleForAutoFix(Mark mark)
        {
            if (mark == null || mark.Attributes == null)
                return;

            dynamic a = mark.Attributes;

            TrySetForAutoFix(
                delegate
                {
                    a.Font.Name = PHU_BOLT_MARK_FONT_NAME;
                }
            );
            TrySetForAutoFix(
                delegate
                {
                    a.Font.FontName = PHU_BOLT_MARK_FONT_NAME;
                }
            );
            TrySetForAutoFix(
                delegate
                {
                    a.Font.Height = PHU_BOLT_MARK_FONT_HEIGHT;
                }
            );
            TrySetForAutoFix(
                delegate
                {
                    a.Font.Color = DrawingColors.Black;
                }
            );

            TrySetForAutoFix(
                delegate
                {
                    a.Frame.Type = FrameTypes.Line;
                }
            );
            TrySetForAutoFix(
                delegate
                {
                    a.Frame.Color = DrawingColors.Black;
                }
            );

            TrySetForAutoFix(
                delegate
                {
                    a.Transparent = false;
                }
            );
            TrySetForAutoFix(
                delegate
                {
                    a.TransparentBackground = false;
                }
            );
        }

        private static void SetElementFontForAutoFix(object element)
        {
            if (element == null)
                return;

            object font = GetPropertyValueForAutoFix(element, "Font");

            if (font == null)
                return;

            TrySetPropertyForAutoFix(font, "Name", PHU_BOLT_MARK_FONT_NAME);
            TrySetPropertyForAutoFix(font, "FontName", PHU_BOLT_MARK_FONT_NAME);
            TrySetPropertyForAutoFix(font, "Height", PHU_BOLT_MARK_FONT_HEIGHT);
            TrySetPropertyForAutoFix(font, "Color", DrawingColors.Black);
        }

        private static object GetPropertyValueForAutoFix(object obj, string propName)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propName))
                    return null;

                PropertyInfo p = obj.GetType()
                    .GetProperty(
                        propName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                if (p == null)
                    return null;

                if (p.GetIndexParameters().Length > 0)
                    return null;

                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static void TrySetPropertyForAutoFix(object obj, string propName, object value)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propName))
                    return;

                PropertyInfo p = obj.GetType()
                    .GetProperty(
                        propName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                if (p == null || !p.CanWrite)
                    return;

                if (p.GetIndexParameters().Length > 0)
                    return;

                if (value != null && !p.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    if (p.PropertyType == typeof(string))
                    {
                        p.SetValue(obj, value.ToString(), null);
                        return;
                    }

                    if (p.PropertyType == typeof(int))
                    {
                        int i;

                        if (int.TryParse(value.ToString(), out i))
                        {
                            p.SetValue(obj, i, null);
                            return;
                        }
                    }

                    if (p.PropertyType == typeof(double))
                    {
                        double d;

                        if (double.TryParse(value.ToString(), out d))
                        {
                            p.SetValue(obj, d, null);
                            return;
                        }
                    }

                    return;
                }

                p.SetValue(obj, value, null);
            }
            catch { }
        }

        private static void TrySetFieldForAutoFix(object obj, string fieldName, object value)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(fieldName))
                    return;

                FieldInfo f = obj.GetType()
                    .GetField(
                        fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                if (f == null)
                    return;

                if (value != null && !f.FieldType.IsAssignableFrom(value.GetType()))
                {
                    if (f.FieldType == typeof(string))
                    {
                        f.SetValue(obj, value.ToString());
                        return;
                    }

                    if (f.FieldType == typeof(int))
                    {
                        int i;

                        if (int.TryParse(value.ToString(), out i))
                        {
                            f.SetValue(obj, i);
                            return;
                        }
                    }

                    if (f.FieldType == typeof(double))
                    {
                        double d;

                        if (double.TryParse(value.ToString(), out d))
                        {
                            f.SetValue(obj, d);
                            return;
                        }
                    }

                    return;
                }

                f.SetValue(obj, value);
            }
            catch { }
        }

        private static void TrySetForAutoFix(Action action)
        {
            try
            {
                if (action != null)
                    action();
            }
            catch { }
        }

        #endregion
    }
}
