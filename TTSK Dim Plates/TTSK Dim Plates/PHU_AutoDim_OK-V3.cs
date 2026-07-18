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
// Auto load tiêu chuẩn GEO_standard_Part ( Chua biết thành công không )
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

        // KHOẢNG HỞ CHÂN DIM TẠI TÂM LỖ
        // V21: Không dùng hằng số cố định nữa.
        // Khoảng hở chân DIM lấy theo phi lỗ / BoltSize thật của từng lỗ.

        // Nếu khoảng cách giữa 2 nhóm lỗ theo phương ngang > 300
        // thì dim dọc sẽ tách thành cụm trái và cụm phải.
        private const double HOLE_GROUP_SPLIT_DISTANCE = 300.0;

        // CHỈNH KHUNG VIEW TÍM Ở ĐÂY
        // 10  = khung sát hơn
        // 20 = vừa
        // 50~100 = rộng hơn
        private const double VIEW_PADDING = 20.0;

        // AUTO DIM GÓC VÁT
        // Tự dim thêm chiều ngang + chiều dọc của từng cạnh vát.
        private const bool AUTO_DIM_CHAMFER = true;

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

        // AUTO DIM ANGLE CHO CHAMFER XUYEN HET MAT DAY / THIN VIEW.
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

        // Có chọn 2 view sau khi chạy xong hay không.
        // true  = chọn 2 view để bạn kéo thủ công
        // false = không chọn
        private const bool SELECT_VIEWS_AFTER_RUN = true;

        // AUTO ARRANGE VIEW - TEST V4
        // Chỉ move Drawing View, không đụng model 3D.
        private const bool AUTO_ARRANGE_VIEW_GAP = true;
        private const double VIEW_VERTICAL_GAP_AFTER_RUN = 15.0;

        // SAFE LOAD VIEW STANDARD - V2
        // KHÔNG dùng cách cũ: view.Attributes = new View.ViewAttributes(file).
        // Cách cũ load toàn bộ tiêu chuẩn, có thể kéo theo Cut area / Shortening / Size nguy hiểm.
        // Cách mới: đọc tiêu chuẩn vào object tạm, chỉ copy các property an toàn sang view hiện tại.
        private const bool SAFE_LOAD_VIEW_STANDARD_V2 = true;
        private const string SAFE_VIEW_STANDARD_FILE = "()_Geo_Standard_Part";

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

        #endregion

        #region 01 - MAIN RUN FLOW
        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
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
            // Không còn kiểu vừa load attribute + scale + resize boundary + move trong cùng 1 nhịp.
            List<View> processedViews = GetMainPartViews(drawing, spDrawing);

            // BƯỚC 1: Xóa DIM cũ trước, commit cho Tekla xử lý xong.
            DeleteAllDimensions(drawing);
            SafeCommitAndWait(drawing, 250);

            // BƯỚC 2: Load tiêu chuẩn view theo cách mới an toàn.
            // Không gán thẳng toàn bộ ViewAttributes từ file tiêu chuẩn.
            // Chỉ copy các setting an toàn, bỏ qua Cut area / RestrictionBox / Shortening / Scale / Depth.
            if (SAFE_LOAD_VIEW_STANDARD_V2)
            {
                SelectProcessedViews(dh, processedViews);
                SafeCommitAndWait(drawing, 150);

                foreach (View view in processedViews)
                {
                    if (view == null) continue;

                    ApplyViewStandardSafeV2(view, SAFE_VIEW_STANDARD_FILE);
                }

                SafeCommitAndWait(drawing, 350);
            }

            // BƯỚC 3: Scale theo chiều dài thanh + khổ giấy, chạy trước DIM.
            // Không đo DIM thật; chỉ cộng dự phòng 200mm cho DIM dọc.
            if (AUTO_SCALE_BY_PART_LENGTH)
            {
                foreach (View view in processedViews)
                {
                    if (view == null) continue;
                    ApplyAutoScaleByPartLength(model, drawing, part, view);
                }

                SafeCommitAndWait(drawing, 350);
            }

            // BƯỚC 4: Tạo DIM + move mark.
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

            // BƯỚC 4B: Tự động sửa Bolt Mark lỗi tiếng Nhật sang HOLE mark chuẩn.
            AutoFixBadJapaneseBoltMarks(drawing);
            SafeCommitAndWait(drawing, 80);

            // BƯỚC 5: Auto arrange bằng KHUNG XANH sau khi Tekla đã update DIM/mark.
            // Khung xanh giữ gap có tính cả DIM, tránh chồng dim lên mặt view.
            if (AUTO_ARRANGE_VIEW_GAP)
            {
                ArrangeProcessedViewsVerticalGap(processedViews, VIEW_VERTICAL_GAP_AFTER_RUN);
                SafeCommitAndWait(drawing, 250);
            }

            // BƯỚC 6: Gom cụm view vào giữa vùng giấy hữu dụng bằng KHUNG TÍM RestrictionBox.
            // Chỉ tính các view đã xử lý, không tính khung tên / bảng vật tư / object khác trên sheet.
            CenterProcessedViewsBySheetSize(drawing, processedViews);
            SafeCommitAndWait(drawing, 250);

            // BƯỚC 7: Sau khi move về giữa, arrange lại bằng KHUNG XANH để lấy lại gap 15 có tính cả DIM.
            // Bổ sung arrange cuối kiểu ÉP CƯỠNG BỨC và CHIA ĐỀU 2 VIEW.
            // Chỉ chỉnh vị trí view theo Y để gap TOP/FRONT = 15, không center lại, không đụng DIM/mark/scale.
            if (AUTO_ARRANGE_VIEW_GAP)
            {
                ForceFinalEqualArrangeTopFrontGap15(processedViews, VIEW_VERTICAL_GAP_AFTER_RUN);
                SafeCommitAndWait(drawing, 250);
            }

            // BƯỚC 8: Cập nhật Title 3 theo scale view cuối cùng.
            // Chỉ ghi giá trị hiển thị scale, không đụng DIM / view / model.
            UpdateDrawingTitle3ScaleFromViews(drawing, processedViews);
            SafeCommitAndWait(drawing, 150);

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
            catch
            {
            }

            try
            {
                if (milliseconds > 0)
                    System.Threading.Thread.Sleep(milliseconds);
            }
            catch
            {
            }
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
            catch
            {
            }

            return result;
        }

        private static double GetPlateThickness(ModelPart part)
        {
            string profile = "";
            part.GetReportProperty("PROFILE", ref profile);

            string p = profile.ToUpper().Replace("PL", "").Replace(" ", "").Replace(",", ".");
            string[] tokens = p.Split(new char[] { '*', 'X', '-' }, StringSplitOptions.RemoveEmptyEntries);

            double min = 999999.0;

            foreach (string token in tokens)
            {
                double value;
                if (double.TryParse(token, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    if (value > 0 && value < min)
                        min = value;
                }
            }

            if (min < 999999.0)
                return min;

            return 0.0;
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
            }
            catch
            {
            }
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
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static int CreateDimsBySectionPolygon(
            Model model,
            Drawing drawing,
            SinglePartDrawing spDrawing,
            ModelPart part,
            double thickness,
            List<View> processedViews)
        {
            int count = 0;

            ContainerView sheet = drawing.GetSheet();
            DrawingObjectEnumerator views = sheet.GetAllViews();

            while (views.MoveNext())
            {
                View view = views.Current as View;
                if (view == null) continue;

                if (!ViewContainsMainPart(view, spDrawing))
                    continue;

                // Load attribute và scale đã được chạy riêng ở Run(), có Commit + Wait từng bước.
                // Không chạy lại ở đây để tránh Tekla regenerate view nhiều lần trong cùng một nhịp.
                count += CreateDimsInOneView(model, part, view, thickness);

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
                if (dp == null) continue;

                if (dp.ModelIdentifier.ID == spDrawing.PartIdentifier.ID)
                    return true;
            }

            return false;
        }

        private static int CreateDimsInOneView(
            Model model,
            ModelPart part,
            View view,
            double realThickness)
        {
            int count = 0;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                TransformationPlane viewPlane =
                    new TransformationPlane(view.DisplayCoordinateSystem);

                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(viewPlane);

                Solid solid = part.GetSolid();

                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;

                double solidHeight = Math.Abs(max.Y - min.Y);

                bool thinView =
                    solidHeight <= realThickness + 5.0 ||
                    solidHeight <= 25.0;

                StraightDimensionSetHandler handler =
                    new StraightDimensionSetHandler();

                if (thinView)
                {
                    PointList lengthDim = new PointList();
                    lengthDim.Add(new Point(min.X, max.Y, 0));
                    lengthDim.Add(new Point(max.X, max.Y, 0));

                    if (handler.CreateDimensionSet(view, lengthDim, new Vector(0, 1, 0), TOP_LENGTH_DIM_OFFSET) != null)
                        count++;

                    PointList thickDim = new PointList();
                    thickDim.Add(new Point(min.X, min.Y, 0));
                    thickDim.Add(new Point(min.X, max.Y, 0));

                    if (handler.CreateDimensionSet(view, thickDim, new Vector(-1, 0, 0), TOP_THICKNESS_DIM_OFFSET) != null)
                        count++;

                    double thinRadiusThickness =
                        GetThinRadiusReferenceThickness(part, realThickness);

                    if ((AUTO_DIM_THIN_VIEW_FILLET_RADIUS ||
                         AUTO_DIM_THIN_VIEW_CHAMFER_ANGLE) &&
                        thinRadiusThickness > 0.0 &&
                        solidHeight <= thinRadiusThickness + 5.0)
                    {
                        List<List<Point>> thinBoundaries =
                            GetThinViewIntersectionPolygons(solid, min, max);

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

                List<Point> polygon =
                    GetLargestIntersectionPolygon(
                        solid.IntersectAllFaces(planeP1, planeP2, planeP3)
                    );

                if (polygon.Count < 2)
                {
                    ResizeViewBoundary(view, min, max);
                    return count;
                }

                double minX, maxX, minY, maxY;
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

                double totalMinX, totalMaxX, totalMinY, totalMaxY;
                GetMinMax(totalPolygon, out totalMinX, out totalMaxX, out totalMinY, out totalMaxY);

                // FIX BO GÓC / RADIUS:
                // Giữ nguyên logic tầng DIM và vị trí DIM tổng như cũ,
                // nhưng nguồn điểm cho DIM tổng đã là projected solid theo hệ tọa độ view.
                Point leftForLength = GetStraightVerticalEdgePointForTotalDim(totalPolygon, totalMinX, true);
                Point rightForLength = GetStraightVerticalEdgePointForTotalDim(totalPolygon, totalMaxX, true);

                if (leftForLength == null)
                    leftForLength = HighestPointNearX(totalPolygon, totalMinX);

                if (rightForLength == null)
                    rightForLength = HighestPointNearX(totalPolygon, totalMaxX);

                // FIX BO GÓC / RADIUS:
                // DIM dọc tổng vẫn giữ cách đặt cũ ở phía trái,
                // nhưng nguồn điểm cho DIM tổng đã là projected solid theo hệ tọa độ view.
                Point bottomForHeight = GetStraightHorizontalEdgePointForTotalDim(totalPolygon, totalMinY, true);
                Point topForHeight = GetStraightHorizontalEdgePointForTotalDim(totalPolygon, totalMaxY, true);

                if (bottomForHeight == null)
                    bottomForHeight = LeftMostPointNearY(totalPolygon, totalMinY);

                if (topForHeight == null)
                    topForHeight = LeftMostPointNearY(totalPolygon, totalMaxY);

                // CLEAN PA6 - TẦNG DIM ĐỘC LẬP 4 HƯỚNG:
                // Không đổi thuật toán tạo chân DIM tổng.
                // Mỗi hướng Top / Bottom / Left / Right có bộ tầng riêng.
                // Một DIM chiếm một tầng; DIM tổng tự ra tầng ngoài cùng của hướng đó.
                bool chamferTop;
                bool chamferBottom;
                bool chamferLeft;
                bool chamferRight;
                GetChamferInfluenceSides(
                    polygon,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    out chamferTop,
                    out chamferBottom,
                    out chamferLeft,
                    out chamferRight
                );

                int topTier = 1;
                int bottomTier = 1;
                int leftTier = 1;
                int rightTier = 1;

                // Chamfer/rãnh ngoài nếu có thì chiếm tầng đầu của đúng hướng đó.
                // Chỉ dùng để quản lý tầng; không bù offset theo chamfer.
                if (AUTO_DIM_CHAMFER && realThickness > 16.0)
                {
                    if (chamferTop) topTier++;
                    if (chamferBottom) bottomTier++;
                    if (chamferLeft) leftTier++;
                    if (chamferRight) rightTier++;
                }

                bool hasHoleDims = false;
                bool holeLeftDim = false;
                bool holeRightDim = false;

                if (realThickness > 16.0)
                {
                    List<Point> holesForTier = GetBoltHoleCenters(part, minX, maxX, minY, maxY);
                    if (holesForTier.Count > 0)
                    {
                        hasHoleDims = true;
                        List<List<Point>> groupsForTier = SplitHoleGroupsByX(holesForTier);

                        if (groupsForTier.Count <= 1)
                        {
                            holeRightDim = true;
                        }
                        else
                        {
                            holeLeftDim = true;
                            holeRightDim = true;
                        }
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

                    if (holeLeftDim)
                    {
                        holeLeftOffset = GetCleanDimOffsetByTier(leftTier);
                        leftTier++;
                    }

                    if (holeRightDim)
                    {
                        holeRightOffset = GetCleanDimOffsetByTier(rightTier);
                        rightTier++;
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

                if (CreateDim(handler, view, leftForLength, rightForLength, new Vector(0, 1, 0), totalHorizontalDimOffset))
                    count++;

                if (CreateDim(handler, view, bottomForHeight, topForHeight, new Vector(-1, 0, 0), totalVerticalDimOffset))
                    count++;

                if (realThickness > 16.0)
                {
                    count += CreateHoleCenterDims(
                        handler,
                        view,
                        part,
                        polygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        holeBottomOffset,
                        holeLeftOffset,
                        holeRightOffset,
                        leftForLength,
                        rightForLength,
                        bottomForHeight,
                        topForHeight
                    );
                }

                // CHAMFER DIM:
                // Chỉ dim chamfer khi thickness > 16.
                // Nếu t <= 16 thì bỏ qua chamfer dim.
                if (AUTO_DIM_CHAMFER && realThickness > 16.0)
                {
                    count += CreateChamferDims(handler, view, polygon, minX, maxX, minY, maxY);
                }

                if (AUTO_MOVE_PART_MARK_NAME)
                {
                    AutoMovePartMarkNameV3(view, part, minX, maxX, minY, maxY);
                }

                // View boundary cũng dùng bao ngoài projected solid để không bị hụt khi tấm vát theo chiều dày.
                Point boundaryMin = new Point(totalMinX, totalMinY, min.Z);
                Point boundaryMax = new Point(totalMaxX, totalMaxY, max.Z);
                ResizeViewBoundary(view, boundaryMin, boundaryMax);
            }
            catch
            {
            }
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

                    try { ok = view.Modify(); }
                    catch { ok = false; }

                    // Nếu Tekla không nhận modify, rollback box cũ để tránh hỏng view.
                    if (!ok && oldMin != null && oldMax != null)
                    {
                        try
                        {
                            view.RestrictionBox = new AABB(oldMin, oldMax);
                            view.Modify();
                        }
                        catch
                        {
                        }
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
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsValidBoundaryBox(Point min, Point max)
        {
            if (min == null || max == null)
                return false;

            if (double.IsNaN(min.X) || double.IsNaN(min.Y) || double.IsNaN(min.Z) ||
                double.IsNaN(max.X) || double.IsNaN(max.Y) || double.IsNaN(max.Z))
                return false;

            if (double.IsInfinity(min.X) || double.IsInfinity(min.Y) || double.IsInfinity(min.Z) ||
                double.IsInfinity(max.X) || double.IsInfinity(max.Y) || double.IsInfinity(max.Z))
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
            double maxY)
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

                // PA13 - CHAMFER DÙNG 4 BIÊN NGOÀI LÀM CHUẨN:
                // - Không dùng rule đường chéo / hoặc \.
                // - Không bù +dx/+dy.
                // - Không dùng hệ bù chamfer / bounding box riêng.
                // - Chỉ riêng DIM chamfer được chiếu chân về biên ngoài cùng hướng:
                //      Top    -> Y = maxY
                //      Bottom -> Y = minY
                //      Left   -> X = minX
                //      Right  -> X = maxX
                // Mục tiêu: 2 DIM chamfer ngang/dọc cùng ăn theo một hệ biên ngoài,
                // tránh tình trạng một bên đúng tầng, một bên bị hụt.
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

                    // DIM NGANG CỦA GÓC VÁT:
                    // Chỉ đo theo X của 2 đầu chamfer, nhưng Y của chân DIM được đưa về biên ngoài.
                    if (dx >= CHAMFER_MIN_SIZE)
                    {
                        double y = topSide ? maxY : minY;
                        Point h1 = new Point(a.X, y, 0);
                        Point h2 = new Point(b.X, y, 0);

                        if (CreateDim(handler, view, h1, h2, horizontalDimDirection, chamferOffset))
                            count++;
                    }

                    // DIM DỌC CỦA GÓC VÁT:
                    // Chỉ đo theo Y của 2 đầu chamfer, nhưng X của chân DIM được đưa về biên ngoài.
                    if (dy >= CHAMFER_MIN_SIZE)
                    {
                        double x = rightSide ? maxX : minX;
                        Point v1 = new Point(x, a.Y, 0);
                        Point v2 = new Point(x, b.Y, 0);

                        if (CreateDim(handler, view, v1, v2, verticalDimDirection, chamferOffset))
                            count++;
                    }
                }
            }
            catch
            {
            }

            return count;
        }



        private static bool IsValidChamferSegment(
            Point a,
            Point b,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (a == null || b == null)
                return false;

            double dx = Math.Abs(a.X - b.X);
            double dy = Math.Abs(a.Y - b.Y);

            // Phải là cạnh xiên nhỏ.
            if (dx < CHAMFER_MIN_SIZE ||
                dy < CHAMFER_MIN_SIZE ||
                dx >= CHAMFER_MAX_SIZE ||
                dy >= CHAMFER_MAX_SIZE)
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
            out bool right)
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
            catch
            {
            }
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

            result.Sort(delegate (Point p1, Point p2)
            {
                double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
                double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);

                return a1.CompareTo(a2);
            });

            return result;
        }

        #endregion

        #region 05 - HOLE DIM LOGIC
        private static int CreateHoleCenterDims(
            StraightDimensionSetHandler handler,
            View view,
            ModelPart part,
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
            Point topForHeight)
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

            List<Point> holes = GetBoltHoleCenters(part, minX, maxX, minY, maxY);

            if (holes.Count == 0)
                return count;

            // Tách nhóm lỗ trái/phải theo khoảng cách X lớn nhất.
            List<List<Point>> groups = SplitHoleGroupsByX(holes);

            // DIM NGANG LỖ:
            // PA11: offset tầng được cấp từ bộ quản lý tầng 4 hướng,
            // không dùng hệ tầng cũ 63/125/190.
            holes.Sort(delegate (Point a, Point b)
            {
                return a.X.CompareTo(b.X);
            });

            PointList xDim = new PointList();

            // Với plate vạt góc:
            // Điểm chân dim mép trái/phải phải bám vào đúng điểm vuông của mép thanh,
            // không lấy cứng (minX, minY) vì điểm này có thể nằm ngoài plate.
            // DIM NGANG LỖ:
            // Phải lấy theo mép TRÁI/PHẢI ngoài cùng của plate.
            // Không lấy mép dưới, vì plate vạt góc sẽ làm dim bị ngắn sai như 137.5.
            Point leftBottomEdgeForXDim = GetSidePointNearBottom(polygon, minX, true);
            Point rightBottomEdgeForXDim = GetSidePointNearBottom(polygon, maxX, false);

            xDim.Add(leftBottomEdgeForXDim);

            foreach (Point h in holes)
            {
                // DIM NGANG LỖ:
                // Kích thước vẫn lấy đúng X tâm lỗ.
                // Chỉ dịch chân dim xuống theo phi lỗ / BoltSize thật để không đè tâm lỗ.
                double holeGap = GetHoleCenterDimGapByPhi(part, h);
                xDim.Add(new Point(h.X, h.Y - holeGap, 0));
            }

            xDim.Add(rightBottomEdgeForXDim);

            // PA11: Không đảo PointList nữa.
            // Tekla thường lấy chân đầu của chính DIM làm gốc distance,
            // nên giữ nguyên thứ tự chân DIM và chỉ quy đổi distance về neo A/B/C/D.

            double bottomHoleDistance = GetBottomDistanceByAnchor4Direction(
                xDim,
                leftForLength,
                rightForLength,
                bottomForHeight,
                topForHeight,
                bottomHoleDimOffset
            );

            StraightDimensionSet dimX =
                handler.CreateDimensionSet(
                    view,
                    xDim,
                    new Vector(0, -1, 0),
                    bottomHoleDistance
                );

            if (dimX != null)
                count++;

            // DIM DỌC LỖ:
            // Quy tắc tầng:
            // - Nếu chỉ có 1 cụm lỗ: đặt bên phải, tầng 2 = 126 ~ 125
            // - Nếu có 2 cụm lỗ:
            //     + cụm trái: tầng 1 = 63
            //     + cụm phải: tầng 2 = 126 ~ 125
            // Sau này nếu có nhiều tầng hơn:
            //     tầng 3 = 189 ~ 190
            //     tầng 4 = 252 ...
            if (groups.Count == 1)
            {
                count += CreateOneSideHoleYDim(
                    handler,
                    view,
                    polygon,
                    part,
                    groups[0],
                    maxX,
                    minY,
                    maxY,
                    new Vector(1, 0, 0),
                    rightHoleDimOffset,
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight
                );
            }
            else
            {
                // Cụm trái: tầng 1 = 63
                count += CreateOneSideHoleYDim(
                    handler,
                    view,
                    polygon,
                    part,
                    groups[0],
                    minX,
                    minY,
                    maxY,
                    new Vector(-1, 0, 0),
                    leftHoleDimOffset,
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight
                );

                // Cụm phải: tầng 2 = 126 ~ 125
                count += CreateOneSideHoleYDim(
                    handler,
                    view,
                    polygon,
                    part,
                    groups[groups.Count - 1],
                    maxX,
                    minY,
                    maxY,
                    new Vector(1, 0, 0),
                    rightHoleDimOffset,
                    leftForLength,
                    rightForLength,
                    bottomForHeight,
                    topForHeight
                );
            }

            return count;
        }

        private static double GetCleanDimOffsetByTier(int tier)
        {
            // PA6: Tầng DIM chính dùng hệ 100mm ổn định theo file nền hiện tại.
            // Tầng 1 = 100, tầng 2 = 200, tầng 3 = 300...
            // Hàm này chỉ quản lý khoảng cách tầng, không bù chamfer/notch/bounding box.
            if (tier <= 1)
                return NORMAL_NO_CHAMFER_DIM_OFFSET;

            return NORMAL_NO_CHAMFER_DIM_OFFSET * tier;
        }

        private static List<List<Point>> SplitHoleGroupsByX(List<Point> holes)
        {
            List<Point> sorted = new List<Point>();

            foreach (Point h in holes)
                sorted.Add(h);

            sorted.Sort(delegate (Point a, Point b)
            {
                return a.X.CompareTo(b.X);
            });

            List<List<Point>> groups = new List<List<Point>>();

            if (sorted.Count == 0)
                return groups;

            double maxGap = 0.0;
            int splitIndex = -1;

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                double gap = Math.Abs(sorted[i + 1].X - sorted[i].X);

                if (gap > maxGap)
                {
                    maxGap = gap;
                    splitIndex = i;
                }
            }

            if (splitIndex >= 0 && maxGap > HOLE_GROUP_SPLIT_DISTANCE)
            {
                List<Point> left = new List<Point>();
                List<Point> right = new List<Point>();

                for (int i = 0; i <= splitIndex; i++)
                    left.Add(sorted[i]);

                for (int i = splitIndex + 1; i < sorted.Count; i++)
                    right.Add(sorted[i]);

                groups.Add(left);
                groups.Add(right);
            }
            else
            {
                groups.Add(sorted);
            }

            return groups;
        }

        private static int CreateOneSideHoleYDim(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            ModelPart part,
            List<Point> holes,
            double edgeX,
            double minY,
            double maxY,
            Vector direction,
            double offset,
            Point leftForLength,
            Point rightForLength,
            Point bottomForHeight,
            Point topForHeight)
        {
            if (holes == null || holes.Count == 0)
                return 0;

            holes.Sort(delegate (Point a, Point b)
            {
                return a.Y.CompareTo(b.Y);
            });

            PointList yDim = new PointList();

            bool isLeftSide = direction.X < 0;

            // Với plate vạt góc:
            // Chân dim ở mép thanh phải bám vào điểm vuông của cạnh trái/phải,
            // không lấy cứng (edgeX, minY/maxY) vì điểm đó có thể nằm ngoài plate.
            // DIM DỌC:
            // Với plate vạt góc, không lấy cạnh đứng ngoài cùng vì đó có thể là cạnh nhỏ sau vạt.
            // Phải lấy điểm ở MÉP DƯỚI / MÉP TRÊN thật của plate, rồi chọn trái/phải.
            Point sideBottomPoint = GetHorizontalEdgePoint(polygon, minY, isLeftSide, true);
            Point sideTopPoint = GetHorizontalEdgePoint(polygon, maxY, isLeftSide, false);

            yDim.Add(sideBottomPoint);

            foreach (Point h in holes)
            {
                // DIM DỌC LỖ:
                // Kích thước vẫn lấy đúng Y tâm lỗ.
                // Chỉ dịch chân dim ra ngoài theo phi lỗ / BoltSize thật để không đè tâm lỗ.
                double holeGap = GetHoleCenterDimGapByPhi(part, h);
                double shiftedX = h.X;

                if (direction.X < 0)
                    shiftedX = h.X - holeGap;
                else if (direction.X > 0)
                    shiftedX = h.X + holeGap;

                yDim.Add(new Point(shiftedX, h.Y, 0));
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

            StraightDimensionSet dimY =
                handler.CreateDimensionSet(
                    view,
                    yDim,
                    direction,
                    yDimDistance
                );

            if (dimY != null)
                return 1;

            return 0;
        }



        private static Point GetHorizontalEdgePoint(
            List<Point> polygon,
            double edgeY,
            bool leftSide,
            bool bottom)
        {
            if (polygon == null || polygon.Count == 0)
                return null;

            // Tìm Y thật của mép trên/dưới gần nhất.
            // bottom=true  -> mép dưới
            // bottom=false -> mép trên
            double bestY = edgeY;
            double bestDy = 999999999.0;

            foreach (Point p in polygon)
            {
                double dy = Math.Abs(p.Y - edgeY);

                if (dy < bestDy)
                {
                    bestDy = dy;
                    bestY = p.Y;
                }
            }

            List<Point> candidates = new List<Point>();

            foreach (Point p in polygon)
            {
                if (Math.Abs(p.Y - bestY) <= TOL + 0.0)
                    candidates.Add(p);
            }

            if (candidates.Count == 0)
                return null;

            Point result = null;

            foreach (Point p in candidates)
            {
                if (result == null)
                {
                    result = p;
                    continue;
                }

                if (leftSide)
                {
                    if (p.X < result.X)
                        result = p;
                }
                else
                {
                    if (p.X > result.X)
                        result = p;
                }
            }

            return new Point(result.X, edgeY, 0);
        }

        private static Point GetSidePointNearBottom(
            List<Point> polygon,
            double edgeX,
            bool leftSide)
        {
            Point p = GetSidePointByX(polygon, edgeX, leftSide, true);

            if (p != null)
                return p;

            return new Point(edgeX, 0, 0);
        }

        private static Point GetSidePointNearTop(
            List<Point> polygon,
            double edgeX,
            bool leftSide)
        {
            Point p = GetSidePointByX(polygon, edgeX, leftSide, false);

            if (p != null)
                return p;

            return new Point(edgeX, 0, 0);
        }

        private static Point GetSidePointByX(
            List<Point> polygon,
            double edgeX,
            bool leftSide,
            bool bottom)
        {
            if (polygon == null || polygon.Count == 0)
                return null;

            // Tìm các vertex nằm sát cạnh trái/phải thật của plate.
            // Với plate vạt góc, các điểm này chính là "điểm vuông" ở đầu cạnh đứng.
            double bestX = edgeX;
            double bestDx = 999999999.0;

            foreach (Point p in polygon)
            {
                double dx = Math.Abs(p.X - edgeX);

                if (dx < bestDx)
                {
                    bestDx = dx;
                    bestX = p.X;
                }
            }

            List<Point> candidates = new List<Point>();

            foreach (Point p in polygon)
            {
                if (Math.Abs(p.X - bestX) <= TOL + 0.0)
                    candidates.Add(p);
            }

            if (candidates.Count == 0)
            {
                Point best = null;
                double score = 999999999.0;

                foreach (Point p in polygon)
                {
                    double s = Math.Abs(p.X - edgeX);

                    if (best == null || s < score)
                    {
                        best = p;
                        score = s;
                    }
                }

                if (best != null)
                    return new Point(edgeX, best.Y, 0);

                return null;
            }

            Point result = null;

            foreach (Point p in candidates)
            {
                if (result == null)
                {
                    result = p;
                    continue;
                }

                if (bottom)
                {
                    if (p.Y < result.Y)
                        result = p;
                }
                else
                {
                    if (p.Y > result.Y)
                        result = p;
                }
            }

            return new Point(edgeX, result.Y, 0);
        }

        private static double GetHoleCenterDimGapByPhi(ModelPart part, Point holeCenter)
        {
            // V21: Khoảng hở chân DIM lỗ lấy theo phi lỗ / BoltSize thật.
            // Không dùng hằng số cố định 22mm nữa.
            try
            {
                if (part == null || holeCenter == null)
                    return 0.0;

                ModelObjectEnumerator bolts = part.GetBolts();

                while (bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null) continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null) continue;

                        if (Math.Abs(p.X - holeCenter.X) <= 1.0 &&
                            Math.Abs(p.Y - holeCenter.Y) <= 1.0)
                        {
                            double d = GetBoltGroupPhiForDimGap(bg);
                            if (d > 0.0)
                                return d;
                        }
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetBoltGroupPhiForDimGap(ModelBoltGroup bg)
        {
            if (bg == null)
                return 0.0;

            // PA6: Ưu tiên PHI LỖ thật trước.
            // Chỉ khi không lấy được phi lỗ mới fallback về M/BoltSize.
            double v = GetReportDouble(bg, "HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleSize");
            if (v > 0.0 && v < 500.0) return v;

            // Một số môi trường Tekla trả phi qua DIAMETER/Diameter.
            v = GetReportDouble(bg, "DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "Diameter");
            if (v > 0.0 && v < 500.0) return v;

            // Fallback cuối cùng mới lấy theo M/BoltSize.
            v = GetReportDouble(bg, "BOLT_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_SIZE");
            if (v > 0.0 && v < 500.0) return v;

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

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

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
                if (double.TryParse(
                    value.ToString().Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result))
                    return result;
            }
            catch
            {
            }

            return 0.0;
        }

        private static List<Point> GetBoltHoleCenters(
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            List<Point> result = new List<Point>();

            try
            {
                ModelObjectEnumerator bolts = part.GetBolts();

                while (bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null) continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null) continue;

                        if (p.X >= minX - 5.0 &&
                            p.X <= maxX + 5.0 &&
                            p.Y >= minY - 5.0 &&
                            p.Y <= maxY + 5.0)
                        {
                            AddUniquePoint(result, new Point(p.X, p.Y, 0), 1.0);
                        }
                    }
                }
            }
            catch
            {
            }

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
                if (Math.Abs(q.X - p.X) <= tol &&
                    Math.Abs(q.Y - p.Y) <= tol)
                    return;
            }

            list.Add(p);
        }

        #endregion

        #region 05A - THIN VIEW FILLET / CHAMFER FEATURE BASE

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
            double fallbackThickness)
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
            catch
            {
            }

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
                if (Double.TryParse(
                    numericToken,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value) && value > 0.0)
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
            Point max)
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
                    ArrayList intersectionPolygons = solid.Intersect(
                        planeP1,
                        planeP2,
                        planeP3
                    );
#pragma warning restore 618
                    CollectPointLists(intersectionPolygons, result, 0);
                }
                catch
                {
                }

                IEnumerator intersections =
                    solid.IntersectAllFaces(planeP1, planeP2, planeP3);

                while (intersections != null && intersections.MoveNext())
                    CollectPointLists(intersections.Current, result, 0);
            }
            catch
            {
            }

            return result;
        }

        private static int CreateThinViewBoundaryFeatureDims(
            View view,
            Solid solid,
            List<List<Point>> boundaries,
            Point min,
            Point max)
        {
            int count = 0;

            try
            {
                List<ThinBoundaryFeature> features =
                    CollectThinViewBoundaryFeatures(solid, boundaries, min, max);

                foreach (ThinBoundaryFeature feature in features)
                {
                    if (feature == null)
                        continue;

                    if (feature.Kind == ThinBoundaryFeatureKind.FilletArc &&
                        AUTO_DIM_THIN_VIEW_FILLET_RADIUS)
                    {
                        double distance = GetThinFilletDimensionDistance(
                            feature.IsLeftSide,
                            feature.IsTopSide
                        );

                        if (CreateThinViewRadiusDimByReflection(
                            view,
                            feature.ArcPoint1,
                            feature.ArcPoint2,
                            feature.ArcPoint3,
                            distance))
                        {
                            count++;
                        }
                    }

                    if (feature.Kind == ThinBoundaryFeatureKind.ChamferLine &&
                        AUTO_DIM_THIN_VIEW_CHAMFER_ANGLE &&
                        CreateThinViewChamferAngleDimension(view, feature))
                    {
                        count++;
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static List<ThinBoundaryFeature> CollectThinViewBoundaryFeatures(
            Solid solid,
            List<List<Point>> boundaries,
            Point min,
            Point max)
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
                    Tekla.Structures.Solid.EdgeEnumerator edges =
                        solid.GetEdgeEnumerator();

                    while (edges != null && edges.MoveNext())
                    {
                        Tekla.Structures.Solid.Edge edge =
                            edges.Current as Tekla.Structures.Solid.Edge;

                        if (!IsCurvedSolidEdge(edge))
                            continue;

                        curvedEdges.Add(edge);
                    }
                }
                catch
                {
                }

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
                        if (!TryBuildThinFilletFeature(
                            orderedBoundary,
                            start2D,
                            end2D,
                            min.X,
                            max.X,
                            min.Y,
                            max.Y,
                            out feature))
                        {
                            continue;
                        }

                        AddUniqueThinBoundaryFeature(result, feature);
                    }

                    // Fallback cho polycurve Tekla tách thành nhiều cạnh NORMAL:
                    // chỉ nhận khi cả chuỗi biên cùng nằm trên một đường tròn.
                    // Chamfer thẳng vẫn bị loại bởi path >= 3, sagitta và circle residual.
                    ThinBoundaryFeature leftBoundaryFeature;
                    if (!HasThinFilletFeatureOnSide(result, true) &&
                        TryBuildThinFilletFeatureFromBoundarySide(
                        orderedBoundary,
                        true,
                        min.X,
                        max.X,
                        min.Y,
                        max.Y,
                        out leftBoundaryFeature))
                    {
                        AddUniqueThinBoundaryFeature(result, leftBoundaryFeature);
                    }

                    ThinBoundaryFeature rightBoundaryFeature;
                    if (!HasThinFilletFeatureOnSide(result, false) &&
                        TryBuildThinFilletFeatureFromBoundarySide(
                        orderedBoundary,
                        false,
                        min.X,
                        max.X,
                        min.Y,
                        max.Y,
                        out rightBoundaryFeature))
                    {
                        AddUniqueThinBoundaryFeature(result, rightBoundaryFeature);
                    }
                }

                // Fallback cuối cho trường hợp API chỉ trả hai endpoint hoặc chia cung ra
                // nhiều list: lấy 5 điểm silhouette trực tiếp từ Solid ở mặt cắt giữa.
                ThinBoundaryFeature leftSampledFeature;
                if (!HasThinFilletFeatureOnSide(result, true) &&
                    TryBuildThinFilletFeatureFromSolidSideSamples(
                    solid,
                    true,
                    min,
                    max,
                    out leftSampledFeature))
                {
                    AddUniqueThinBoundaryFeature(result, leftSampledFeature);
                }

                ThinBoundaryFeature rightSampledFeature;
                if (!HasThinFilletFeatureOnSide(result, false) &&
                    TryBuildThinFilletFeatureFromSolidSideSamples(
                    solid,
                    false,
                    min,
                    max,
                    out rightSampledFeature))
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
                        if (TryBuildThinChamferFeature(
                            orderedBoundary,
                            i,
                            curvedEdges,
                            result,
                            min.X,
                            max.X,
                            min.Y,
                            max.Y,
                            out chamferFeature))
                        {
                            AddUniqueThinBoundaryFeature(result, chamferFeature);
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool HasThinFilletFeatureOnSide(
            List<ThinBoundaryFeature> features,
            bool isLeftSide)
        {
            if (features == null)
                return false;

            foreach (ThinBoundaryFeature feature in features)
            {
                if (feature != null &&
                    feature.Kind == ThinBoundaryFeatureKind.FilletArc &&
                    feature.IsLeftSide == isLeftSide)
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
            out ThinBoundaryFeature feature)
        {
            feature = null;

            if (boundary == null || boundary.Count < 3 ||
                segmentIndex < 0 || segmentIndex >= boundary.Count)
            {
                return false;
            }

            int count = boundary.Count;
            Point a = boundary[segmentIndex];
            Point b = boundary[(segmentIndex + 1) % count];

            if (a == null || b == null)
                return false;

            double dx = Math.Abs(a.X - b.X);
            double height = Math.Abs(maxY - minY);

            if (dx < THIN_CHAMFER_MIN_RUN || dx >= THIN_CHAMFER_MAX_RUN ||
                height <= THIN_CHAMFER_EDGE_TOL * 2.0)
            {
                return false;
            }

            bool aOnMinY = Math.Abs(a.Y - minY) <= THIN_CHAMFER_EDGE_TOL;
            bool aOnMaxY = Math.Abs(a.Y - maxY) <= THIN_CHAMFER_EDGE_TOL;
            bool bOnMinY = Math.Abs(b.Y - minY) <= THIN_CHAMFER_EDGE_TOL;
            bool bOnMaxY = Math.Abs(b.Y - maxY) <= THIN_CHAMFER_EDGE_TOL;

            if (!((aOnMinY && bOnMaxY) || (aOnMaxY && bOnMinY)))
                return false;

            double segmentMinX = Math.Min(a.X, b.X);
            double segmentMaxX = Math.Max(a.X, b.X);
            bool isLeftSide = Math.Abs(segmentMinX - minX) <= THIN_CHAMFER_EDGE_TOL;
            bool isRightSide = Math.Abs(segmentMaxX - maxX) <= THIN_CHAMFER_EDGE_TOL;

            if (isLeftSide == isRightSide)
                return false;

            bool originIsA = isLeftSide ? a.X <= b.X : a.X >= b.X;
            Point origin = originIsA ? a : b;
            Point other = originIsA ? b : a;

            if (Math.Abs(origin.X - (isLeftSide ? minX : maxX)) >
                THIN_CHAMFER_EDGE_TOL)
            {
                return false;
            }

            Point beforeA = boundary[(segmentIndex - 1 + count) % count];
            Point afterB = boundary[(segmentIndex + 2) % count];
            Point originNeighbor = originIsA ? beforeA : afterB;
            Point otherNeighbor = originIsA ? afterB : beforeA;

            if (!IsThinChamferHorizontalBodyNeighbor(
                    origin,
                    originNeighbor,
                    isLeftSide) ||
                !IsThinChamferHorizontalBodyNeighbor(
                    other,
                    otherNeighbor,
                    isLeftSide))
            {
                return false;
            }

            if (MatchesThinChamferCurvedEdge(a, b, curvedEdges) ||
                HasThinFilletFeatureOnSide(existingFeatures, isLeftSide))
            {
                return false;
            }

            feature = new ThinBoundaryFeature();
            feature.Kind = ThinBoundaryFeatureKind.ChamferLine;
            feature.IsLeftSide = isLeftSide;
            feature.IsTopSide = Math.Abs(origin.Y - minY) <= THIN_CHAMFER_EDGE_TOL;
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
            bool isLeftSide)
        {
            if (endpoint == null || neighbor == null ||
                Math.Abs(endpoint.Y - neighbor.Y) > THIN_CHAMFER_EDGE_TOL)
            {
                return false;
            }

            return isLeftSide
                ? neighbor.X > endpoint.X + 0.05
                : neighbor.X < endpoint.X - 0.05;
        }

        private static bool MatchesThinChamferCurvedEdge(
            Point a,
            Point b,
            List<Tekla.Structures.Solid.Edge> curvedEdges)
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
                        Distance2D(a, start) <= THIN_FILLET_ENDPOINT_MATCH_TOL &&
                        Distance2D(b, end) <= THIN_FILLET_ENDPOINT_MATCH_TOL;
                    bool reverse =
                        Distance2D(a, end) <= THIN_FILLET_ENDPOINT_MATCH_TOL &&
                        Distance2D(b, start) <= THIN_FILLET_ENDPOINT_MATCH_TOL;

                    if (direct || reverse)
                        return true;
                }
                catch
                {
                }
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
            out ThinBoundaryFeature feature)
        {
            feature = null;

            if (boundary == null || boundary.Count < 3)
                return false;

            double yBand = Math.Max(0.25, Math.Abs(maxY - minY) * 0.05);
            int topIndex = FindThinBoundaryOuterPointIndex(
                boundary,
                isLeftSide,
                maxY,
                yBand
            );
            int bottomIndex = FindThinBoundaryOuterPointIndex(
                boundary,
                isLeftSide,
                minY,
                yBand
            );

            if (topIndex < 0 || bottomIndex < 0 || topIndex == bottomIndex)
                return false;

            ThinBoundaryFeature candidate;
            if (!TryBuildThinFilletFeature(
                boundary,
                boundary[topIndex],
                boundary[bottomIndex],
                minX,
                maxX,
                minY,
                maxY,
                out candidate))
            {
                return false;
            }

            if (candidate == null ||
                candidate.ClaimedBoundaryPath == null ||
                candidate.ClaimedBoundaryPath.Count < 4)
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
            double yBand)
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

                if (bestIndex < 0 ||
                    (isLeftSide && point.X < bestX) ||
                    (!isLeftSide && point.X > bestX))
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
            out ThinBoundaryFeature feature)
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

                if (!TryGetThinSolidSideIntersectionPoint(
                    solid,
                    isLeftSide,
                    min.X,
                    max.X,
                    y,
                    midZ,
                    out silhouettePoint))
                {
                    return false;
                }

                sampledPath.Add(silhouettePoint);
            }

            if (!TryEvaluateThinFilletPath(
                sampledPath,
                min.X,
                max.X,
                min.Y,
                max.Y,
                out feature))
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
            out Point result)
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

                if (best == null ||
                    (isLeftSide && point.X < best.X) ||
                    (!isLeftSide && point.X > best.X))
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
                return edgeType.IndexOf(
                    "CURVED_SURFACE",
                    StringComparison.OrdinalIgnoreCase) >= 0;
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
                if (p == null ||
                    Double.IsNaN(p.X) || Double.IsInfinity(p.X) ||
                    Double.IsNaN(p.Y) || Double.IsInfinity(p.Y))
                {
                    continue;
                }

                Point copy = new Point(p.X, p.Y, 0);
                if (result.Count == 0 ||
                    Distance2D(result[result.Count - 1], copy) > 0.05)
                {
                    result.Add(copy);
                }
            }

            if (result.Count > 1 &&
                Distance2D(result[0], result[result.Count - 1]) <= 0.05)
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
            out ThinBoundaryFeature feature)
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

            endIndex = FindNearestThinBoundaryPointIndex(
                boundary,
                curvedEnd,
                out endDistance
            );

            if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
                return false;

            if (startDistance > THIN_FILLET_ENDPOINT_MATCH_TOL ||
                endDistance > THIN_FILLET_ENDPOINT_MATCH_TOL)
            {
                return false;
            }

            List<Point> forwardPath =
                BuildThinBoundaryPath(boundary, startIndex, endIndex, 1);

            List<Point> backwardPath =
                BuildThinBoundaryPath(boundary, startIndex, endIndex, -1);

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
                feature = forwardFeature.BoundaryPathLength <= backwardFeature.BoundaryPathLength
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

        private static Point GetThinBoundaryDimensionSegmentMidpoint(
            ThinBoundaryFeature feature)
        {
            if (feature == null ||
                feature.ArcPoint2 == null ||
                feature.ClaimedBoundaryPath == null ||
                feature.ClaimedBoundaryPath.Count < 4)
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

                if (halfPathLength > walkedLength + 0.05 &&
                    halfPathLength < nextWalkedLength - 0.05)
                {
                    if (i < 2 || i > path.Count - 2)
                        return null;

                    bool touchesCurrentMiddle =
                        Distance2D(feature.ArcPoint2, segmentStart) <= 0.05 ||
                        Distance2D(feature.ArcPoint2, segmentEnd) <= 0.05;

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
            out double bestDistance)
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
            int direction)
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
            out ThinBoundaryFeature feature)
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

            if (pathLength <= chord ||
                pathLength > chord * THIN_FILLET_MAX_PATH_CHORD_RATIO)
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

            if (arcMiddle == null ||
                maxSagitta / chord < THIN_FILLET_MIN_SAGITTA_CHORD_RATIO)
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

            double sweepDeg = GetArcSweepDegreesThroughPoint(
                first,
                arcMiddle,
                last,
                center
            );
            if (sweepDeg < THIN_FILLET_MIN_SWEEP_DEG ||
                sweepDeg > THIN_FILLET_MAX_SWEEP_DEG)
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

            if (Math.Abs(topEndpoint.Y - maxY) > edgeTol ||
                Math.Abs(bottomEndpoint.Y - minY) > edgeTol)
            {
                return false;
            }

            double topTangentError = Math.Abs(topEndpoint.X - center.X) / radius;
            double bottomTangentError = Math.Abs(bottomEndpoint.X - center.X) / radius;

            bool isTopSide = topTangentError <= bottomTangentError;
            double selectedTangentError = isTopSide
                ? topTangentError
                : bottomTangentError;

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

            NormalizeThinFilletArcPointOrder(
                ref arc1,
                arc2,
                ref arc3,
                isLeftSide,
                isTopSide
            );

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

        private static double DistancePointToInfiniteLine2D(
            Point p,
            Point lineStart,
            Point lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= 0.000001)
                return 0.0;

            double cross =
                dx * (lineStart.Y - p.Y) -
                (lineStart.X - p.X) * dy;

            return Math.Abs(cross) / length;
        }

        private static bool TryFitCircleThroughThreePoints(
            Point p1,
            Point p2,
            Point p3,
            out Point center,
            out double radius)
        {
            center = null;
            radius = 0.0;

            double d = 2.0 * (
                p1.X * (p2.Y - p3.Y) +
                p2.X * (p3.Y - p1.Y) +
                p3.X * (p1.Y - p2.Y)
            );

            if (Math.Abs(d) <= 0.000001)
                return false;

            double p1Sq = p1.X * p1.X + p1.Y * p1.Y;
            double p2Sq = p2.X * p2.X + p2.Y * p2.Y;
            double p3Sq = p3.X * p3.X + p3.Y * p3.Y;

            double centerX = (
                p1Sq * (p2.Y - p3.Y) +
                p2Sq * (p3.Y - p1.Y) +
                p3Sq * (p1.Y - p2.Y)
            ) / d;

            double centerY = (
                p1Sq * (p3.X - p2.X) +
                p2Sq * (p1.X - p3.X) +
                p3Sq * (p2.X - p1.X)
            ) / d;

            if (Double.IsNaN(centerX) || Double.IsInfinity(centerX) ||
                Double.IsNaN(centerY) || Double.IsInfinity(centerY))
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
            Point center)
        {
            double startAngle = Math.Atan2(
                start.Y - center.Y,
                start.X - center.X
            );
            double middleAngle = Math.Atan2(
                middle.Y - center.Y,
                middle.X - center.X
            );
            double endAngle = Math.Atan2(
                end.Y - center.Y,
                end.X - center.X
            );

            double ccwStartToEnd = NormalizePositiveAngle(endAngle - startAngle);
            double ccwStartToMiddle = NormalizePositiveAngle(middleAngle - startAngle);
            double sweepRadians = ccwStartToMiddle <= ccwStartToEnd + 0.000001
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
            bool isTopSide)
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
                (arc2.X - arc1.X) * (arc3.Y - arc1.Y) -
                (arc2.Y - arc1.Y) * (arc3.X - arc1.X);

            // Bốn DIM mẫu đều dùng thứ tự clockwise (cross < 0).
            if (cross > 0.0)
            {
                Point temp = arc1;
                arc1 = arc3;
                arc3 = temp;
            }
        }

        private static double GetThinFilletDimensionDistance(
            bool isLeftSide,
            bool isTopSide)
        {
            if (isLeftSide)
            {
                return isTopSide
                    ? THIN_FILLET_DISTANCE_LEFT_TOP
                    : THIN_FILLET_DISTANCE_LEFT_BOTTOM;
            }

            return isTopSide
                ? THIN_FILLET_DISTANCE_RIGHT_TOP
                : THIN_FILLET_DISTANCE_RIGHT_BOTTOM;
        }

        private static double GetThinChamferDimensionDistance(
            bool isLeftSide,
            bool isTopSide)
        {
            if (isLeftSide)
            {
                return isTopSide
                    ? THIN_CHAMFER_DISTANCE_LEFT_TOP
                    : THIN_CHAMFER_DISTANCE_LEFT_BOTTOM;
            }

            return isTopSide
                ? THIN_CHAMFER_DISTANCE_RIGHT_TOP
                : THIN_CHAMFER_DISTANCE_RIGHT_BOTTOM;
        }

        private static double GetThinChamferVerticalRayLength(
            bool isLeftSide,
            bool isTopSide)
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
            ThinBoundaryFeature candidate)
        {
            if (features == null || candidate == null)
                return;

            foreach (ThinBoundaryFeature existing in features)
            {
                if (existing == null || existing.Kind != candidate.Kind)
                    continue;

                if (existing.IsLeftSide != candidate.IsLeftSide ||
                    existing.IsTopSide != candidate.IsTopSide)
                {
                    continue;
                }

                if (candidate.Kind == ThinBoundaryFeatureKind.ChamferLine)
                    return;

                if (existing.Center != null && candidate.Center != null &&
                    Distance2D(existing.Center, candidate.Center) <= 1.0 &&
                    Math.Abs(existing.Radius - candidate.Radius) <= 0.5)
                {
                    return;
                }
            }

            features.Add(candidate);
        }

        private static bool CreateThinViewChamferAngleDimension(
            View view,
            ThinBoundaryFeature feature)
        {
            try
            {
                if (view == null)
                    return false;

                Point origin;
                Point chamferAnchor;
                Point perpendicularPoint;
                if (!TryGetThinChamferAnglePoints(
                    feature,
                    out origin,
                    out chamferAnchor,
                    out perpendicularPoint))
                    return false;

                if (HasMatchingThinChamferAngleDimension(
                    view,
                    origin,
                    chamferAnchor,
                    perpendicularPoint))
                {
                    return true;
                }

                AngleDimensionAttributes attributes = new AngleDimensionAttributes();
                attributes.Type = AngleTypes.AngleOnSide;
                attributes.TransparentBackground = false;
                if (attributes.Text != null)
                {
                    attributes.Text.TextPlacing =
                        DimensionSetBaseAttributes.DimensionTextPlacings.AboveDimensionLine;
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
            out Point perpendicularPoint)
        {
            origin = null;
            chamferAnchor = null;
            perpendicularPoint = null;

            if (feature == null ||
                feature.ChamferOrigin == null ||
                feature.ChamferOtherPoint == null)
            {
                return false;
            }

            origin = new Point(
                feature.ChamferOrigin.X,
                feature.ChamferOrigin.Y,
                0
            );
            chamferAnchor = new Point(
                feature.ChamferOtherPoint.X,
                feature.ChamferOtherPoint.Y,
                0
            );

            double rayLength = Math.Max(
                GetThinChamferVerticalRayLength(
                    feature.IsLeftSide,
                    feature.IsTopSide),
                Math.Abs(chamferAnchor.Y - origin.Y) + 1.0
            );
            perpendicularPoint = new Point(
                origin.X,
                origin.Y + (feature.IsTopSide ? rayLength : -rayLength),
                0
            );

            double cross =
                (chamferAnchor.X - origin.X) * (perpendicularPoint.Y - origin.Y) -
                (chamferAnchor.Y - origin.Y) * (perpendicularPoint.X - origin.X);

            return Math.Abs(cross) > 0.000001;
        }

        private static bool HasMatchingThinChamferAngleDimension(
            View view,
            Point origin,
            Point point1,
            Point point2)
        {
            try
            {
                if (view == null || origin == null || point1 == null || point2 == null)
                    return false;

                DrawingObjectEnumerator objects =
                    view.GetAllObjects(typeof(AngleDimension));

                while (objects != null && objects.MoveNext())
                {
                    AngleDimension existing = objects.Current as AngleDimension;
                    if (existing == null ||
                        existing.Origin == null ||
                        existing.Point1 == null ||
                        existing.Point2 == null ||
                        Distance2D(existing.Origin, origin) > 0.5)
                    {
                        continue;
                    }

                    bool direct =
                        Distance2D(existing.Point1, point1) <= 0.5 &&
                        Distance2D(existing.Point2, point2) <= 0.5;
                    bool reverse =
                        Distance2D(existing.Point1, point2) <= 0.5 &&
                        Distance2D(existing.Point2, point1) <= 0.5;

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
            catch
            {
            }

            return false;
        }

        private static bool CreateThinViewRadiusDimByReflection(
            View view,
            Point arc1,
            Point arc2,
            Point arc3,
            double distance)
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

                        if (parameters[0].ParameterType.IsAssignableFrom(view.GetType()) &&
                            parameters[1].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            parameters[2].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            parameters[3].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            parameters[4].ParameterType == typeof(double))
                        {
                            args = new object[] { view, arc1, arc2, arc3, distance };
                        }
                        else if (parameters[0].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 parameters[1].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 parameters[2].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 parameters[3].ParameterType == typeof(double) &&
                                 parameters[4].ParameterType.IsAssignableFrom(view.GetType()))
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
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

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
            catch
            {
            }

            return result;
        }

        private static void CollectRealSolidPointsForTotalDims(
            object obj,
            List<Point> result,
            int depth)
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
            string methodName)
        {
            try
            {
                if (obj == null || result == null || string.IsNullOrEmpty(methodName))
                    return;

                MethodInfo method = obj.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (method == null || method.GetParameters().Length != 0)
                    return;

                object enumerator = method.Invoke(obj, null);
                if (enumerator == null)
                    return;

                MethodInfo moveNext = enumerator.GetType().GetMethod(
                    "MoveNext",
                    BindingFlags.Public | BindingFlags.Instance
                );

                PropertyInfo currentProp = enumerator.GetType().GetProperty(
                    "Current",
                    BindingFlags.Public | BindingFlags.Instance
                );

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
            catch
            {
            }
        }

        private static void TryCollectPointProperty(
            object obj,
            List<Point> result,
            string propertyName)
        {
            try
            {
                if (obj == null || result == null || string.IsNullOrEmpty(propertyName))
                    return;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return;

                if (prop.PropertyType != typeof(Point))
                    return;

                Point p = prop.GetValue(obj, null) as Point;
                if (p == null)
                    return;

                AddUniquePoint(result, new Point(p.X, p.Y, 0), 0.5);
            }
            catch
            {
            }
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
                if (list.Count < 2) continue;

                double minX, maxX, minY, maxY;
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
            out double maxY)
        {
            minX = 999999999.0;
            maxX = -999999999.0;
            minY = 999999999.0;
            maxY = -999999999.0;

            foreach (Point p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        private static Point HighestPointNearX(List<Point> pts, double x)
        {
            Point best = null;
            double bestDx = 999999999.0;

            foreach (Point p in pts)
            {
                double dx = Math.Abs(p.X - x);

                if (best == null ||
                    dx < bestDx - TOL ||
                    (Math.Abs(dx - bestDx) <= TOL && p.Y > best.Y))
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

                if (best == null ||
                    dy < bestDy - TOL ||
                    (Math.Abs(dy - bestDy) <= TOL && p.X < best.X))
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
            bool preferTop)
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
                    if (Math.Abs(a.X - edgeX) > sideTol ||
                        Math.Abs(b.X - edgeX) > sideTol)
                        continue;

                    double length = Math.Abs(a.Y - b.Y);

                    if (length < minStraightLength)
                        continue;

                    double score = Math.Abs(a.X - edgeX) + Math.Abs(b.X - edgeX);

                    if (length > bestLength + TOL ||
                        (Math.Abs(length - bestLength) <= TOL && score < bestScore))
                    {
                        bestLength = length;
                        bestScore = score;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestA == null || bestB == null)
                    return null;

                double y = preferTop
                    ? Math.Max(bestA.Y, bestB.Y)
                    : Math.Min(bestA.Y, bestB.Y);

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
            bool preferLeft)
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
                    if (Math.Abs(a.Y - edgeY) > sideTol ||
                        Math.Abs(b.Y - edgeY) > sideTol)
                        continue;

                    double length = Math.Abs(a.X - b.X);

                    if (length < minStraightLength)
                        continue;

                    double score = Math.Abs(a.Y - edgeY) + Math.Abs(b.Y - edgeY);

                    if (length > bestLength + TOL ||
                        (Math.Abs(length - bestLength) <= TOL && score < bestScore))
                    {
                        bestLength = length;
                        bestScore = score;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestA == null || bestB == null)
                    return null;

                double x = preferLeft
                    ? Math.Min(bestA.X, bestB.X)
                    : Math.Max(bestA.X, bestB.X);

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
            double distance)
        {
            if (p1 == null || p2 == null)
                return false;

            if (Distance2D(p1, p2) < 1.0)
                return false;

            PointList list = new PointList();
            list.Add(new Point(p1.X, p1.Y, 0));
            list.Add(new Point(p2.X, p2.Y, 0));

            StraightDimensionSet dim =
                handler.CreateDimensionSet(view, list, direction, distance);

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
            double tierOffset)
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
            double tierOffset)
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
            double tierOffset)
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
            double tierOffset)
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
            double tierOffset)
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
                        anchor = GetHighestYPoint(leftForLength, rightForLength, bottomForHeight, topForHeight);
                        if (anchor == null) return tierOffset;

                        distance = (anchor.Y + tierOffset) - firstFoot.Y;
                    }
                    else
                    {
                        anchor = GetLowestYPoint(leftForLength, rightForLength, bottomForHeight, topForHeight);
                        if (anchor == null) return tierOffset;

                        distance = firstFoot.Y - (anchor.Y - tierOffset);
                    }
                }
                else
                {
                    if (direction.X < 0)
                    {
                        anchor = GetLowestXPoint(leftForLength, rightForLength, bottomForHeight, topForHeight);
                        if (anchor == null) return tierOffset;

                        distance = firstFoot.X - (anchor.X - tierOffset);
                    }
                    else
                    {
                        anchor = GetHighestXPoint(leftForLength, rightForLength, bottomForHeight, topForHeight);
                        if (anchor == null) return tierOffset;

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
            double tierOffset)
        {
            PointList list = new PointList();
            if (leftForLength != null) list.Add(leftForLength);
            if (rightForLength != null) list.Add(rightForLength);

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
            double tierOffset)
        {
            PointList list = new PointList();
            if (leftForLength != null) list.Add(leftForLength);
            if (rightForLength != null) list.Add(rightForLength);

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
            double tierOffset)
        {
            PointList list = new PointList();
            if (bottomForHeight != null) list.Add(bottomForHeight);
            if (topForHeight != null) list.Add(topForHeight);

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
            double tierOffset)
        {
            PointList list = new PointList();
            if (bottomForHeight != null) list.Add(bottomForHeight);
            if (topForHeight != null) list.Add(topForHeight);

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
            double tierOffset)
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
            double tierOffset)
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

                if (best == null ||
                    p.Y > best.Y + TOL ||
                    (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X))
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

                if (best == null ||
                    p.Y < best.Y - TOL ||
                    (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X))
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

                if (best == null ||
                    p.X < best.X - TOL ||
                    (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y))
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

                if (best == null ||
                    p.X > best.X + TOL ||
                    (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y))
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
            Point topForHeight)
        {
            if (SamePoint2D(leftForLength, bottomForHeight)) return leftForLength;
            if (SamePoint2D(leftForLength, topForHeight)) return leftForLength;
            if (SamePoint2D(rightForLength, bottomForHeight)) return rightForLength;
            if (SamePoint2D(rightForLength, topForHeight)) return rightForLength;

            return null;
        }

        private static bool SamePoint2D(Point a, Point b)
        {
            if (a == null || b == null)
                return false;

            return Math.Abs(a.X - b.X) <= TOL &&
                   Math.Abs(a.Y - b.Y) <= TOL;
        }

        private static Point GetHigherPoint(Point a, Point b)
        {
            if (a == null) return b;
            if (b == null) return a;

            if (a.Y > b.Y + TOL)
                return a;

            if (b.Y > a.Y + TOL)
                return b;

            return a.X <= b.X ? a : b;
        }

        private static double GetHigherY(Point a, Point b)
        {
            double y = -999999999.0;

            if (a != null && a.Y > y) y = a.Y;
            if (b != null && b.Y > y) y = b.Y;

            if (y < -999999990.0)
                return 0.0;

            return y;
        }

        private static double GetLowerX(Point a, Point b)
        {
            double x = 999999999.0;

            if (a != null && a.X < x) x = a.X;
            if (b != null && b.X < x) x = b.X;

            if (x > 999999990.0)
                return 0.0;

            return x;
        }


        #endregion

        #region 07 - PART MARK AUTO MOVE
        private static void AutoMovePartMarkNameV3(
            View view,
            ModelPart part,
            double minX,
            double maxX,
            double minY,
            double maxY)
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

                for (int i = 0; i < partMarks.Count; i++)
                {
                    // 1) Chỉnh mark name thành Inside part horizontal trước
                    SetPartMarkInsideHorizontal(partMarks[i]);

                    // 2) Sau đó mới di chuyển mark về giữa phía trên thanh
                    MovePartMarkBoxToCenterTop(
                        partMarks[i],
                        i,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );
                }
            }
            catch
            {
            }
        }


        private static void SetPartMarkInsideHorizontal(MarkBase mark)
        {
            if (mark == null)
                return;

            try
            {
                Mark realMark = mark as Mark;

                object attrs = null;
                PropertyInfo attrProp = null;

                if (realMark != null)
                {
                    attrs = realMark.Attributes;
                }
                else
                {
                    attrProp = mark.GetType().GetProperty(
                        "Attributes",
                        BindingFlags.Public | BindingFlags.Instance
                    );

                    if (attrProp != null && attrProp.CanRead)
                        attrs = attrProp.GetValue(mark, null);
                }

                if (attrs == null)
                    return;

                // PA17 - FORCE LEADER LINE OPTION BY INDEX:
                // Tekla UI tính option từ 0:
                // 0 = Leader line
                // 1 = Along line or leader line
                // 2 = Along line
                // 3 = Inside part horizontal
                // Vì thuộc tính cũ đang ưu tiên option đỏ, ép trực tiếp index = 3,
                // đồng thời vẫn gán object InsidePartHorizontalPlacingType cho API chính.
                ForcePartMarkLeaderLineInsideHorizontal(attrs);

                if (realMark != null)
                {
                    Mark.MarkAttributes realAttrs = attrs as Mark.MarkAttributes;
                    if (realAttrs != null)
                        realMark.Attributes = realAttrs;

                    realMark.Modify();
                }
                else
                {
                    try
                    {
                        if (attrProp == null)
                        {
                            attrProp = mark.GetType().GetProperty(
                                "Attributes",
                                BindingFlags.Public | BindingFlags.Instance
                            );
                        }

                        if (attrProp != null && attrProp.CanWrite)
                            attrProp.SetValue(mark, attrs, null);
                    }
                    catch
                    {
                    }

                    mark.Modify();
                }
            }
            catch
            {
            }
        }

        private static void ForcePartMarkLeaderLineInsideHorizontal(object attrs)
        {
            if (attrs == null)
                return;

            try
            {
                InsidePartHorizontalPlacingType inside = new InsidePartHorizontalPlacingType();

                // API chính nếu Tekla nhận object placing type.
                SetPropertyIfAssignable(attrs, "PreferredPlacing", inside);
                SetPropertyIfAssignable(attrs, "Placing", inside);
                SetPropertyIfAssignable(attrs, "PlacingType", inside);
                SetPropertyIfAssignable(attrs, "LeaderLinePlacing", inside);
                SetPropertyIfAssignable(attrs, "PreferredLeaderLinePlacing", inside);
                SetPropertyIfAssignable(attrs, "MarkPlacing", inside);

                // Ép option UI bằng index 3 cho các property liên quan Leader line / placing.
                ForceIndex3OnLeaderLineProperties(attrs, 0);
            }
            catch
            {
            }
        }

        private static void ForceIndex3OnLeaderLineProperties(object obj, int depth)
        {
            if (obj == null || depth > 3)
                return;

            try
            {
                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo prop in props)
                {
                    try
                    {
                        if (prop == null || string.IsNullOrEmpty(prop.Name))
                            continue;

                        string name = prop.Name;
                        bool isTargetName =
                            name.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Placing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Preferred", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!isTargetName)
                            continue;

                        if (prop.CanWrite)
                        {
                            Type t = prop.PropertyType;

                            // Property dạng int / short / byte: set trực tiếp index 3.
                            if (t == typeof(int))
                            {
                                prop.SetValue(obj, 3, null);
                                continue;
                            }

                            if (t == typeof(short))
                            {
                                prop.SetValue(obj, (short)3, null);
                                continue;
                            }

                            if (t == typeof(byte))
                            {
                                prop.SetValue(obj, (byte)3, null);
                                continue;
                            }

                            // Property enum: lấy value có thứ tự index 3 nếu có.
                            if (t.IsEnum)
                            {
                                Array values = Enum.GetValues(t);
                                if (values != null && values.Length > 3)
                                {
                                    prop.SetValue(obj, values.GetValue(3), null);
                                    continue;
                                }
                            }
                        }

                        // Một số thuộc tính leader nằm trong object con.
                        if (prop.CanRead && depth < 3)
                        {
                            object child = prop.GetValue(obj, null);
                            if (child == null)
                                continue;

                            Type childType = child.GetType();
                            if (childType == typeof(string) || childType.IsValueType)
                                continue;

                            ForceIndex3OnLeaderLineProperties(child, depth + 1);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void SetPropertyIfAssignable(object obj, string propertyName, object value)
        {
            try
            {
                if (obj == null || value == null || string.IsNullOrEmpty(propertyName))
                    return;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanWrite)
                    return;

                if (!prop.PropertyType.IsAssignableFrom(value.GetType()))
                    return;

                prop.SetValue(obj, value, null);
            }
            catch
            {
            }
        }

        private static void MovePartMarkBoxToCenterTop(
            MarkBase mark,
            int index,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (mark == null)
                return;

            try
            {
                double centerX = (minX + maxX) / 2.0;
                double plateWidth = Math.Abs(maxX - minX);

                // GIỮ NGUYÊN LOGIC MARK CŨ CHO MIẾNG RỘNG.
                // Chỉ đổi vị trí mark xuống dưới khi chiều rộng miếng < 180.
                bool placeBelow = plateWidth > 0.0 && plateWidth < PART_MARK_BELOW_IF_WIDTH_LESS_THAN;

                // Nếu mark phía trên: đáy khung mark cách mép trên 15mm như logic cũ.
                // Nếu mark phía dưới: đỉnh khung mark cách mép dưới 15mm.
                double targetAnchorY = placeBelow
                    ? minY - PART_MARK_GAP_FROM_PLATE - index * PART_MARK_STAGGER
                    : maxY + PART_MARK_GAP_FROM_PLATE + index * PART_MARK_STAGGER;

                // Lấy khung bao thật của mark.
                Point boxMin;
                Point boxMax;

                if (TryGetObjectBox(mark, out boxMin, out boxMax))
                {
                    double currentCenterX = (boxMin.X + boxMax.X) / 2.0;

                    // Mark trên: canh theo đáy box.
                    // Mark dưới: canh theo đỉnh box để bảo đảm khoảng hở tới mép dưới đúng 15mm.
                    double currentAnchorY = placeBelow ? boxMax.Y : boxMin.Y;

                    Vector move = new Vector(
                        centerX - currentCenterX,
                        targetAnchorY - currentAnchorY,
                        0
                    );

                    if (TryMoveObjectRelative(mark, move))
                    {
                        try { mark.Modify(); }
                        catch { }

                        return;
                    }
                }

                // Fallback nếu Tekla không cho lấy box:
                // với miếng hẹp đặt insertion point ở giữa phía dưới, còn lại giữ phía trên như cũ.
                Point target = new Point(centerX, targetAnchorY, 0);
                MoveMarkInsertionOnly(mark, target);
            }
            catch
            {
            }
        }

        private static bool IsPartNameMarkV3(
            MarkBase mark,
            List<Point> holes,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (mark == null)
                return false;

            string typeName = mark.GetType().FullName.ToUpper();

            if (typeName.Contains("BOLTMARK") ||
                typeName.Contains("HOLEMARK") ||
                typeName.Contains("WELDMARK") ||
                typeName.Contains("CONNECTIONMARK"))
                return false;

            if (typeName.Contains("PARTMARK"))
                return true;

            string text = GetObjectTextByReflection(mark).ToUpper();

            if (text.Contains("アンカー") ||
                text.Contains("ルーズ") ||
                text.Contains("HOLE") ||
                text.Contains("BOLT") ||
                text.Contains("Ø") ||
                text.Contains("Φ") ||
                text.Contains("M20") ||
                text.Contains("M 20"))
                return false;

            if (text.Contains("PL") ||
                text.Contains("BP") ||
                text.Contains("*"))
                return true;

            Point p = SafeGetInsertionPoint(mark);

            if (p != null)
            {
                bool nearTop =
                    p.X >= minX - 100.0 &&
                    p.X <= maxX + 100.0 &&
                    p.Y >= maxY - 40.0 &&
                    p.Y <= maxY + 120.0;

                if (nearTop && !IsPointNearAnyHole(p, holes, 120.0))
                    return true;
            }

            return false;
        }

        private static MarkBase FindMostLikelyPartMarkV3(
            List<MarkBase> marks,
            List<Point> holes,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            MarkBase best = null;
            double bestScore = 999999999.0;

            Point target = new Point(
                (minX + maxX) / 2.0,
                maxY + PART_MARK_GAP_FROM_PLATE,
                0
            );

            foreach (MarkBase mark in marks)
            {
                if (mark == null)
                    continue;

                string typeName = mark.GetType().FullName.ToUpper();

                if (typeName.Contains("BOLTMARK") ||
                    typeName.Contains("HOLEMARK") ||
                    typeName.Contains("WELDMARK"))
                    continue;

                Point boxMin;
                Point boxMax;
                Point p = null;

                if (TryGetObjectBox(mark, out boxMin, out boxMax))
                {
                    p = new Point(
                        (boxMin.X + boxMax.X) / 2.0,
                        (boxMin.Y + boxMax.Y) / 2.0,
                        0
                    );
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

        private static bool TryGetObjectBox(
            DrawingObject obj,
            out Point min,
            out Point max)
        {
            min = null;
            max = null;

            try
            {
                MethodInfo method = obj.GetType().GetMethod(
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
            catch
            {
            }

            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    "BoundingBox",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop != null && prop.CanRead)
                {
                    object box = prop.GetValue(obj, null);

                    if (TryExtractBoxMinMax(box, out min, out max))
                        return true;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    "RestrictionBox",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop != null && prop.CanRead)
                {
                    object box = prop.GetValue(obj, null);

                    if (TryExtractBoxMinMax(box, out min, out max))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryExtractBoxMinMax(
            object box,
            out Point min,
            out Point max)
        {
            min = null;
            max = null;

            if (box == null)
                return false;

            try
            {
                PropertyInfo minProp = box.GetType().GetProperty(
                    "MinPoint",
                    BindingFlags.Public | BindingFlags.Instance
                );

                PropertyInfo maxProp = box.GetType().GetProperty(
                    "MaxPoint",
                    BindingFlags.Public | BindingFlags.Instance
                );

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
            catch
            {
            }

            return false;
        }

        private static bool TryMoveObjectRelative(
            DrawingObject obj,
            Vector move)
        {
            try
            {
                MethodInfo method = obj.GetType().GetMethod(
                    "MoveObjectRelative",
                    BindingFlags.Public | BindingFlags.Instance
                );

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

        private static void MoveMarkInsertionOnly(
            MarkBase mark,
            Point target)
        {
            if (mark == null || target == null)
                return;

            try
            {
                // Không set IsFixed.
                mark.InsertionPoint = target;
                mark.Modify();
            }
            catch
            {
                try
                {
                    TrySetPointProperty(mark, "InsertionPoint", target);
                    TrySetPointProperty(mark, "Position", target);
                    TrySetPointProperty(mark, "Origin", target);
                    mark.Modify();
                }
                catch
                {
                }
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

        private static bool IsPointNearAnyHole(
            Point p,
            List<Point> holes,
            double distance)
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

        private static bool TrySetPointProperty(
            object obj,
            string propertyName,
            Point value)
        {
            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

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

        private static Point TryGetPointProperty(
            object obj,
            string propertyName)
        {
            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

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

        private static void CollectStringsByReflection(
            object obj,
            List<string> texts,
            int depth)
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

            PropertyInfo[] props = t.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance
            );

            foreach (PropertyInfo prop in props)
            {
                if (!prop.CanRead)
                    continue;

                string name = prop.Name.ToUpper();

                if (!(name.Contains("TEXT") ||
                      name.Contains("CONTENT") ||
                      name.Contains("VALUE") ||
                      name.Contains("STRING") ||
                      name.Contains("TAG")))
                    continue;

                try
                {
                    object value = prop.GetValue(obj, null);
                    CollectStringsByReflection(value, texts, depth + 1);
                }
                catch
                {
                }
            }
        }



        #endregion

        #region 08 - SAFE VIEW STANDARD LOAD
        private static void ApplySafeFitByPartsAndExtension(View view, double extension)
        {
            try
            {
                if (view == null)
                    return;

                object attrs = null;
                try { attrs = view.Attributes; }
                catch { attrs = null; }

                if (attrs != null)
                {
                    ForceSafeViewSizeSettings(attrs, extension);
                }

                // Một số phiên bản Tekla expose property trực tiếp ở View.
                ForceSafeViewSizeSettings(view, extension);

                try { view.Modify(); }
                catch { }
            }
            catch
            {
            }
        }

        private static void ForceSafeViewSizeSettings(object obj, double extension)
        {
            if (obj == null)
                return;

            try
            {
                // Tắt mọi setting shortening / cut parts nguy hiểm trong view attribute.
                object shortening = TryGetObjectProperty(obj, "Shortening");
                if (shortening != null)
                {
                    TrySetBoolPropertyIfExists(shortening, "Enable", false);
                    TrySetBoolPropertyIfExists(shortening, "Enabled", false);
                    TrySetBoolPropertyIfExists(shortening, "CutParts", false);
                    TrySetBoolPropertyIfExists(shortening, "CutSkewParts", false);
                    TrySetDoublePropertyIfExists(shortening, "MinimumCutPartLength", 0.0);
                    TrySetDoublePropertyIfExists(shortening, "Space", 0.0);
                    TrySetDoublePropertyIfExists(shortening, "Distance", 0.0);
                }

                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo prop in props)
                {
                    if (!prop.CanWrite)
                        continue;

                    string name = prop.Name.ToUpper();

                    // View extension for neighbor parts = 20.
                    if (name.IndexOf("EXTENSION") >= 0 ||
                        name.IndexOf("MARGIN") >= 0 ||
                        name.IndexOf("PADDING") >= 0)
                    {
                        TrySetNumericProperty(prop, obj, extension);
                        continue;
                    }

                    // Cố gắng set Size = Fit by parts nếu API expose enum/int/string.
                    if ((name.IndexOf("SIZE") >= 0 || name.IndexOf("BOUND") >= 0) &&
                        prop.PropertyType.IsEnum)
                    {
                        TrySetEnumByName(prop, obj, new string[] { "FITBYPARTS", "FIT_BY_PARTS", "BYPARTS", "PARTS" });
                        continue;
                    }

                    // Một số API dùng bool kiểu FitByParts.
                    if ((name.IndexOf("FIT") >= 0 && name.IndexOf("PART") >= 0) ||
                        (name.IndexOf("BYPART") >= 0))
                    {
                        TrySetBoolPropertyIfExists(obj, prop.Name, true);
                        continue;
                    }

                    // Tắt các property cut/shortening nếu nằm trực tiếp trong attributes.
                    if (name.IndexOf("SHORTEN") >= 0 || name.IndexOf("CUTPART") >= 0)
                    {
                        if (prop.PropertyType == typeof(bool))
                            TrySetBoolPropertyIfExists(obj, prop.Name, false);
                    }
                }
            }
            catch
            {
            }
        }

        private static void TrySetNumericProperty(PropertyInfo prop, object obj, double value)
        {
            try
            {
                if (prop == null || obj == null || !prop.CanWrite)
                    return;

                Type t = prop.PropertyType;

                if (t == typeof(double))
                    prop.SetValue(obj, value, null);
                else if (t == typeof(float))
                    prop.SetValue(obj, Convert.ToSingle(value), null);
                else if (t == typeof(int))
                    prop.SetValue(obj, Convert.ToInt32(value), null);
            }
            catch
            {
            }
        }

        private static void TrySetEnumByName(PropertyInfo prop, object obj, string[] preferredNames)
        {
            try
            {
                if (prop == null || obj == null || preferredNames == null)
                    return;

                if (!prop.PropertyType.IsEnum)
                    return;

                Array values = Enum.GetValues(prop.PropertyType);

                foreach (object v in values)
                {
                    string enumName = v.ToString().ToUpper().Replace(" ", "").Replace("_", "");

                    foreach (string preferred in preferredNames)
                    {
                        string p = preferred.ToUpper().Replace(" ", "").Replace("_", "");

                        if (enumName.IndexOf(p) >= 0)
                        {
                            prop.SetValue(obj, v, null);
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool TrySetBoolPropertyIfExists(object obj, string propertyName, bool value)
        {
            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(bool))
                    return false;

                prop.SetValue(obj, value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetDoublePropertyIfExists(object obj, string propertyName, double value)
        {
            try
            {
                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanWrite)
                    return false;

                if (prop.PropertyType == typeof(double))
                {
                    prop.SetValue(obj, value, null);
                    return true;
                }

                if (prop.PropertyType == typeof(float))
                {
                    prop.SetValue(obj, Convert.ToSingle(value), null);
                    return true;
                }

                if (prop.PropertyType == typeof(int))
                {
                    prop.SetValue(obj, Convert.ToInt32(value), null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool ApplyViewStandardSafeV2(View view, string attributeFile)
        {
            if (view == null || string.IsNullOrEmpty(attributeFile))
                return false;

            try
            {
                // CÁCH MỚI:
                // 1) Load tiêu chuẩn vào object tạm.
                // 2) Lấy attributes hiện tại của view.
                // 3) Chỉ copy các property an toàn từ tiêu chuẩn sang attributes hiện tại.
                // 4) Không copy Cut area / RestrictionBox / Shortening / Scale / Depth.
                View.ViewAttributes currentAttributes = null;

                try { currentAttributes = view.Attributes; }
                catch { currentAttributes = null; }

                if (currentAttributes == null)
                    return false;

                View.ViewAttributes standardAttributes =
                    new View.ViewAttributes(attributeFile);

                if (standardAttributes == null)
                    return false;

                CopySafeViewStandardProperties(standardAttributes, currentAttributes);

                // Ép các setting an toàn sau khi copy:
                // - Tắt shortening / cut parts nếu property tồn tại.
                // - Giữ Fit by parts / extension 20 nếu property tồn tại.
                ForceSafeViewSizeSettings(currentAttributes, VIEW_PADDING);

                view.Attributes = currentAttributes;
                ForceSafeViewSizeSettings(view, VIEW_PADDING);

                try { return view.Modify(); }
                catch { return false; }
            }
            catch
            {
                return false;
            }
        }

        private static void CopySafeViewStandardProperties(object source, object target)
        {
            if (source == null || target == null)
                return;

            try
            {
                PropertyInfo[] sourceProps = source.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo sp in sourceProps)
                {
                    if (sp == null || !sp.CanRead)
                        continue;

                    string propName = sp.Name;

                    if (IsDangerousViewStandardProperty(propName))
                        continue;

                    PropertyInfo tp = target.GetType().GetProperty(
                        propName,
                        BindingFlags.Public | BindingFlags.Instance
                    );

                    if (tp == null || !tp.CanWrite)
                        continue;

                    if (tp.PropertyType != sp.PropertyType)
                        continue;

                    if (!IsSafeSimplePropertyType(tp.PropertyType))
                        continue;

                    try
                    {
                        object value = sp.GetValue(source, null);
                        if (value == null)
                            continue;

                        tp.SetValue(target, value, null);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsSafeSimplePropertyType(Type t)
        {
            if (t == null)
                return false;

            if (t.IsEnum)
                return true;

            if (t == typeof(bool) ||
                t == typeof(int) ||
                t == typeof(double) ||
                t == typeof(float) ||
                t == typeof(string))
                return true;

            return false;
        }

        private static bool IsDangerousViewStandardProperty(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return true;

            string name = propertyName.ToUpper();

            // Những property này có thể làm Tekla rebuild cut area / crop / depth / scale.
            // Không copy từ file tiêu chuẩn để tránh lỗi Illegal length of cut area.
            if (name.IndexOf("RESTRICTION") >= 0) return true;
            if (name.IndexOf("BOUND") >= 0) return true;
            if (name.IndexOf("BOX") >= 0) return true;
            if (name.IndexOf("CUT") >= 0) return true;
            if (name.IndexOf("SHORTEN") >= 0) return true;
            if (name.IndexOf("DEPTH") >= 0) return true;
            if (name.IndexOf("CLIP") >= 0) return true;
            if (name.IndexOf("SCALE") >= 0) return true;
            if (name.IndexOf("SIZE") >= 0) return true;
            if (name.IndexOf("EXTENSION") >= 0) return true;
            if (name.IndexOf("MARGIN") >= 0) return true;
            if (name.IndexOf("PADDING") >= 0) return true;
            if (name.IndexOf("ORIGIN") >= 0) return true;
            if (name.IndexOf("PLANE") >= 0) return true;
            if (name.IndexOf("COORDINATE") >= 0) return true;
            if (name.IndexOf("PLAC") >= 0) return true;
            if (name.IndexOf("MIN") == 0 || name.IndexOf("MAX") == 0) return true;

            return false;
        }

        private static object TryGetObjectProperty(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

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

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

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


        #endregion

        #region 09 - SCALE / ARRANGE / SELECT VIEW
        private static void ApplyAutoScaleByPartLength(
            Model model,
            Drawing drawing,
            ModelPart part,
            View view)
        {
            TransformationPlane oldPlane = null;

            try
            {
                if (model == null || drawing == null || part == null || view == null)
                    return;

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

                TransformationPlane viewPlane = new TransformationPlane(view.DisplayCoordinateSystem);
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
            catch
            {
            }
            finally
            {
                try
                {
                    if (model != null && oldPlane != null)
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static double GetAutoScaleMarginTotal(double sheetWidth, double sheetHeight)
        {
            if (IsSheetSize(sheetWidth, sheetHeight, A1_SHEET_WIDTH, A1_SHEET_HEIGHT, SHEET_SIZE_TOLERANCE))
                return AUTO_SCALE_A1_MARGIN_TOTAL;

            if (IsSheetSize(sheetWidth, sheetHeight, A3_SHEET_WIDTH, A3_SHEET_HEIGHT, SHEET_SIZE_TOLERANCE))
                return AUTO_SCALE_A3_MARGIN_TOTAL;

            return AUTO_SCALE_DEFAULT_MARGIN_TOTAL;
        }

        private static bool IsSheetSize(
            double sheetWidth,
            double sheetHeight,
            double targetWidth,
            double targetHeight,
            double tolerance)
        {
            return
                (Math.Abs(sheetWidth - targetWidth) <= tolerance &&
                 Math.Abs(sheetHeight - targetHeight) <= tolerance) ||
                (Math.Abs(sheetWidth - targetHeight) <= tolerance &&
                 Math.Abs(sheetHeight - targetWidth) <= tolerance);
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
            out double height)
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

                try { attrs = view.Attributes; }
                catch { attrs = null; }

                if (attrs != null)
                {
                    SetScaleProperties(attrs, scale);
                }

                // Nếu Tekla có property Scale trực tiếp trên View thì set luôn.
                SetScaleProperties(view, scale);

                try { view.Modify(); }
                catch { }
            }
            catch
            {
            }
        }

        private static void SetScaleProperties(object obj, double scale)
        {
            if (obj == null)
                return;

            try
            {
                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

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
                                TrySetObjectProperty(scaleObj, "Denominator", Convert.ToInt32(scale));
                                TrySetObjectProperty(scaleObj, "Numerator", 1);
                                TrySetObjectProperty(scaleObj, "X", 1.0);
                                TrySetObjectProperty(scaleObj, "Y", scale);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }



        private static void UpdateDrawingTitle3ScaleFromViews(
            Drawing drawing,
            List<View> views)
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
                    try { drawing.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
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
                try { attrs = view.Attributes; }
                catch { attrs = null; }

                scale = GetScaleNumberFromObject(attrs);
                if (scale > 0.0)
                    return scale;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetScaleNumberFromObject(object obj)
        {
            if (obj == null)
                return 0.0;

            try
            {
                PropertyInfo[] props = obj.GetType().GetProperties(
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
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double ConvertScaleValueToNumber(object value)
        {
            if (value == null)
                return 0.0;

            try
            {
                if (value is int ||
                    value is double ||
                    value is float ||
                    value is decimal)
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
                if (double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out numberText))
                {
                    if (numberText > 0.0)
                        return numberText;
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static string FormatScaleForTitle3(double scale)
        {
            try
            {
                double rounded = Math.Round(scale, 3);

                if (Math.Abs(rounded - Math.Round(rounded)) < 0.001)
                    return "1:" + ((int)Math.Round(rounded)).ToString();

                return "1:" + rounded.ToString(
                    "0.###",
                    System.Globalization.CultureInfo.InvariantCulture
                );
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

            string[] propNames = new string[]
            {
                "Title3",
                "TITLE3",
                "TitleThree",
                "DrawingTitle3"
            };

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
            string value)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                    return false;

                PropertyInfo prop = obj.GetType().GetProperty(
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
                try { current = prop.GetValue(obj, null); }
                catch { current = null; }

                if (current != null)
                {
                    if (TrySetObjectPropertyFlexible(current, "Text", value))
                        return true;

                    if (TrySetObjectPropertyFlexible(current, "Value", value))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }



        private static void CenterProcessedViewsBySheetSize(
            Drawing drawing,
            List<View> views)
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

                if (minX == double.MaxValue || maxX == double.MinValue ||
                    minY == double.MaxValue || maxY == double.MinValue)
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

                        v.Origin = new Point(
                            oldOrigin.X + dx,
                            oldOrigin.Y + dy,
                            oldOrigin.Z
                        );

                        v.Modify();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void ArrangeProcessedViewsVerticalGap(
            List<View> views,
            double gap)
        {
            try
            {
                if (views == null || views.Count < 2)
                    return;

                List<ViewPaperBox> boxes = new List<ViewPaperBox>();

                foreach (View v in views)
                {
                    if (v == null)
                        continue;

                    try { v.Select(); }
                    catch { }

                    // Đảm bảo view có thể được đặt thủ công.
                    TrySetFixedViewPlacing(v, true);

                    ViewPaperBox b;
                    // ARRANGE dùng khung xanh/view frame để giữ khoảng hở có tính cả DIM như OK-V3.
                    // MOVE CENTER vẫn dùng khung tím ở CenterProcessedViewsBySheetSize().
                    if (TryGetViewPaperBox(v, out b))
                        boxes.Add(b);
                }

                if (boxes.Count < 2)
                    return;

                // YÊU CẦU MỚI:
                // Mặt độ dày / view mỏng phải luôn nằm phía trên view chính.
                // Nhận diện view mỏng bằng chiều cao khung nhỏ nhất.
                boxes.Sort(delegate (ViewPaperBox a, ViewPaperBox b)
                {
                    return a.Height.CompareTo(b.Height);
                });

                ViewPaperBox thicknessView = boxes[0];
                ViewPaperBox mainView = boxes[boxes.Count - 1];

                if (thicknessView == null || mainView == null)
                    return;

                if (thicknessView.View == null || mainView.View == null)
                    return;

                if (thicknessView.Height <= 1.0 || mainView.Height <= 1.0)
                    return;

                // Trong paper coordinate của Tekla, so sánh bằng box thực tế:
                // Đưa đáy của view mỏng lên cách đỉnh của view chính đúng gap.
                // Công thức này không phụ thuộc view hiện đang nằm trên hay dưới.
                double desiredThicknessMinY = mainView.MaxY + gap;
                double moveY = desiredThicknessMinY - thicknessView.MinY;

                if (Math.Abs(moveY) < 0.1)
                    return;

                // Bảo vệ move view: nếu tính toán ra bước nhảy quá lớn thì không move.
                // Tránh view bị văng lung tung trên sheet.
                if (Math.Abs(moveY) > 300.0)
                    return;

                MoveViewByOriginOnly(thicknessView.View, 0.0, moveY);

                try { thicknessView.View.Select(); }
                catch { }

                thicknessView.View.Modify();
            }
            catch
            {
            }
        }


        private static void ForceFinalEqualArrangeTopFrontGap15(
            List<View> views,
            double gap)
        {
            try
            {
                if (views == null || views.Count < 2)
                    return;

                List<ViewPaperBox> boxes = new List<ViewPaperBox>();

                foreach (View v in views)
                {
                    if (v == null)
                        continue;

                    try { v.Select(); }
                    catch { }

                    TrySetFixedViewPlacing(v, true);

                    ViewPaperBox b;
                    // ARRANGE CUỐI dùng khung xanh/view frame để khoảng hở 15 tính cả vùng DIM.
                    // Không dùng khung tím ở đây vì tím quá sát plate, dễ làm DIM top chồng vào front.
                    if (TryGetViewPaperBox(v, out b))
                    {
                        if (b != null && b.Width > 1.0 && b.Height > 1.0)
                            boxes.Add(b);
                    }
                }

                if (boxes.Count < 2)
                    return;

                // TOP thường là view mỏng nhất; FRONT là view cao nhất.
                boxes.Sort(delegate (ViewPaperBox a, ViewPaperBox b)
                {
                    return a.Height.CompareTo(b.Height);
                });

                ViewPaperBox topView = boxes[0];
                ViewPaperBox frontView = boxes[boxes.Count - 1];

                if (topView == null || frontView == null)
                    return;

                if (topView.View == null || frontView.View == null)
                    return;

                if (topView.View == frontView.View)
                    return;

                // Ép TOP nằm trên FRONT, mép dưới TOP cách mép trên FRONT đúng gap.
                double currentGap = topView.MinY - frontView.MaxY;
                double delta = gap - currentGap;

                if (Math.Abs(delta) < 0.1)
                    return;

                // Chia đều: TOP đi lên 1/2, FRONT đi xuống 1/2.
                // Nếu TOP/FRONT đang dính sát/đè nhau thì delta dương, 2 view sẽ tách ra.
                double half = delta * 0.5;

                if (Math.Abs(half) > 300.0)
                    return;

                MoveViewByOriginOnly(topView.View, 0.0, half);
                MoveViewByOriginOnly(frontView.View, 0.0, -half);

                try { topView.View.Modify(); }
                catch { }

                try { frontView.View.Modify(); }
                catch { }
            }
            catch
            {
            }
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

        private static bool TryGetViewPurplePaperBox(
            View view,
            out ViewPaperBox box)
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
                try { rb = view.RestrictionBox; }
                catch { rb = null; }

                if (rb == null || rb.MinPoint == null || rb.MaxPoint == null)
                    return false;

                Point origin = null;
                try { origin = view.Origin; }
                catch { origin = null; }

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
        private static bool TryGetViewPaperBox(
            View view,
            out ViewPaperBox box)
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

        private static void MoveViewByOriginOnly(
            View view,
            double dx,
            double dy)
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

                view.Origin = new Point(
                    oldOrigin.X + dx,
                    oldOrigin.Y + dy,
                    oldOrigin.Z
                );

                view.Modify();
                return;
            }
            catch
            {
            }

            try
            {
                Vector move = new Vector(dx, dy, 0);

                // Nếu môi trường Tekla expose MoveObjectRelative cho View thì dùng fallback này.
                MethodInfo m = view.GetType().GetMethod(
                    "MoveObjectRelative",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (m != null)
                {
                    m.Invoke(view, new object[] { move });
                    view.Modify();
                }
            }
            catch
            {
            }
        }

        private static void TrySetFixedViewPlacing(View view, bool fixedPlacing)
        {
            try
            {
                if (view == null || view.Attributes == null)
                    return;

                PropertyInfo p = view.Attributes.GetType().GetProperty(
                    "FixedViewPlacing",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                {
                    p.SetValue(view.Attributes, fixedPlacing, null);
                    view.Modify();
                }
            }
            catch
            {
            }
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
            catch
            {
            }
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
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsBadJapaneseBoltMarkForAutoFix(Mark mark)
        {
            try
            {
                string text = GetMarkTextForAutoFix(mark);

                if (string.IsNullOrEmpty(text))
                    return false;

                return text.Contains(PHU_BAD_BOLT_MARK_TEXT_1) ||
                       text.Contains(PHU_BAD_BOLT_MARK_TEXT_2);
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
            catch
            {
            }

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
                    "GR_BOLT_NUMBER"));

            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());
            AddElementForceForAutoFix(content, MakeTextElementForAutoFix("-"));
            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());
            AddElementForceForAutoFix(content, MakeTextElementForAutoFix("φ"));
            AddElementForceForAutoFix(content, CreateSpaceElementForAutoFix());

            AddElementForceForAutoFix(
                content,
                CreateLengthPropertyElementFromDumpForAutoFix(
                    "HOLE.DIAMETER",
                    "GR_HOLE_DIAMETER"));

            mark.Attributes.Content = content;
        }

        private static object CreatePropertyElementFromDumpForAutoFix(
            string elementTypeName,
            string name,
            string propertyTypeEnumName)
        {
            Type elementType = FindTeklaDrawingTypeForAutoFix(elementTypeName);

            if (elementType == null)
                return MakeTextElementForAutoFix(name);

            object obj = null;

            try
            {
                obj = Activator.CreateInstance(elementType, true);
            }
            catch
            {
            }

            if (obj == null)
            {
                try
                {
                    ConstructorInfo ci = elementType.GetConstructor(new Type[] { typeof(string) });

                    if (ci != null)
                        obj = ci.Invoke(new object[] { name });
                }
                catch
                {
                }
            }

            if (obj == null)
                return MakeTextElementForAutoFix(name);

            ForcePropertyElementFieldsForAutoFix(obj, name, propertyTypeEnumName);
            SetElementFontForAutoFix(obj);

            return obj;
        }

        private static object CreateLengthPropertyElementFromDumpForAutoFix(
            string name,
            string propertyTypeEnumName)
        {
            Type elementType = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.LengthPropertyElement");

            if (elementType == null)
            {
                return CreatePropertyElementFromDumpForAutoFix(
                    "Tekla.Structures.Drawing.PropertyElement",
                    name,
                    propertyTypeEnumName);
            }

            object obj = null;

            try
            {
                obj = Activator.CreateInstance(elementType, true);
            }
            catch
            {
            }

            if (obj == null)
            {
                try
                {
                    ConstructorInfo ci = elementType.GetConstructor(new Type[] { typeof(string) });

                    if (ci != null)
                        obj = ci.Invoke(new object[] { name });
                }
                catch
                {
                }
            }

            if (obj == null)
            {
                return CreatePropertyElementFromDumpForAutoFix(
                    "Tekla.Structures.Drawing.PropertyElement",
                    name,
                    propertyTypeEnumName);
            }

            ForcePropertyElementFieldsForAutoFix(obj, name, propertyTypeEnumName);
            ForceLengthUnitAutomaticPrecision0ForAutoFix(obj);
            SetElementFontForAutoFix(obj);

            return obj;
        }

        private static void ForcePropertyElementFieldsForAutoFix(
            object obj,
            string name,
            string propertyTypeEnumName)
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
                "Tekla.Structures.Drawing.PropertyElement+PropertyElementType");

            if (propElementType == null)
                return null;

            object propTypeObj = null;

            try
            {
                propTypeObj = Activator.CreateInstance(propElementType, true);
            }
            catch
            {
            }

            if (propTypeObj == null)
                return null;

            Type enumType = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.PropertyElement+PropertyElementType+PropertyTypes");

            if (enumType == null)
                return null;

            object enumValue = null;

            try
            {
                enumValue = Enum.Parse(enumType, enumName, true);
            }
            catch
            {
            }

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
                        "Tekla.Structures.Drawing.UnitAttributes");

                    if (unitType != null)
                    {
                        try
                        {
                            unit = Activator.CreateInstance(unitType, true);
                        }
                        catch
                        {
                        }
                    }
                }

                if (unit == null)
                    return;

                SetEnumPropertyOrFieldForAutoFix(
                    unit,
                    "_Unit",
                    "Unit",
                    "Tekla.Structures.Drawing.Units",
                    "Automatic");

                SetEnumPropertyOrFieldForAutoFix(
                    unit,
                    "_Format",
                    "Format",
                    "Tekla.Structures.Drawing.FormatTypes",
                    "Automatic");

                TrySetFieldForAutoFix(unit, "_Precision", 0);
                TrySetPropertyForAutoFix(unit, "Precision", 0);

                TrySetFieldForAutoFix(lengthElement, "_Unit", unit);
                TrySetPropertyForAutoFix(lengthElement, "Unit", unit);
            }
            catch
            {
            }
        }

        private static void SetEnumPropertyOrFieldForAutoFix(
            object obj,
            string fieldName,
            string propName,
            string enumTypeName,
            string enumName)
        {
            Type enumType = FindTeklaDrawingTypeForAutoFix(enumTypeName);

            if (enumType == null)
                return;

            object enumValue = null;

            try
            {
                enumValue = Enum.Parse(enumType, enumName, true);
            }
            catch
            {
            }

            if (enumValue == null)
                return;

            TrySetFieldForAutoFix(obj, fieldName, enumValue);
            TrySetPropertyForAutoFix(obj, propName, enumValue);
        }

        private static object CreateSpaceElementForAutoFix()
        {
            Type t = FindTeklaDrawingTypeForAutoFix(
                "Tekla.Structures.Drawing.SpaceElement");

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
            catch
            {
            }

            try
            {
                MethodInfo[] methods = content.GetType().GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

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
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static Type FindTeklaDrawingTypeForAutoFix(string fullName)
        {
            try
            {
                Type t = Type.GetType(fullName + ", Tekla.Structures.Drawing");

                if (t != null)
                    return t;
            }
            catch
            {
            }

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
            catch
            {
            }

            return null;
        }

        private static void SetBadBoltMarkStyleForAutoFix(Mark mark)
        {
            if (mark == null || mark.Attributes == null)
                return;

            dynamic a = mark.Attributes;

            TrySetForAutoFix(delegate { a.Font.Name = PHU_BOLT_MARK_FONT_NAME; });
            TrySetForAutoFix(delegate { a.Font.FontName = PHU_BOLT_MARK_FONT_NAME; });
            TrySetForAutoFix(delegate { a.Font.Height = PHU_BOLT_MARK_FONT_HEIGHT; });
            TrySetForAutoFix(delegate { a.Font.Color = DrawingColors.Black; });

            TrySetForAutoFix(delegate { a.Frame.Type = FrameTypes.Line; });
            TrySetForAutoFix(delegate { a.Frame.Color = DrawingColors.Black; });

            TrySetForAutoFix(delegate { a.Transparent = false; });
            TrySetForAutoFix(delegate { a.TransparentBackground = false; });
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

                PropertyInfo p = obj.GetType().GetProperty(
                    propName,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

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

                PropertyInfo p = obj.GetType().GetProperty(
                    propName,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

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
            catch
            {
            }
        }

        private static void TrySetFieldForAutoFix(object obj, string fieldName, object value)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(fieldName))
                    return;

                FieldInfo f = obj.GetType().GetField(
                    fieldName,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

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
            catch
            {
            }
        }

        private static void TrySetForAutoFix(Action action)
        {
            try
            {
                if (action != null)
                    action();
            }
            catch
            {
            }
        }

        #endregion


    }
}
