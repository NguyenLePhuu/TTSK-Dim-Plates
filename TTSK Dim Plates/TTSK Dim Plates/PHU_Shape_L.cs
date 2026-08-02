#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Drawing.UI;

using ModelPart = Tekla.Structures.Model.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;

namespace Tekla.Technology.Akit.UserScript
{
    public class ShapeLScript
    {
        private const double TOL = 1.0;
        private const double VIEW_PADDING = 20.0;

        // DIM TIER SPACING BY DRAWING SCALE:
        // Tất cả view của drawing dùng chung một scale.
        // Hệ tầng được khởi tạo một lần sau Auto Scale và trước khi tạo DIM.
        private const double DIM_TIER_SCALE_5_BASE = 50.0;
        private const double DIM_TIER_SCALE_5_STEP = 50.0;
        private const double DIM_TIER_SCALE_5_MIDDLE = 66.6666667;

        private const double DIM_TIER_SCALE_10_BASE = 100.0;
        private const double DIM_TIER_SCALE_10_STEP = 100.0;
        private const double DIM_TIER_SCALE_10_MIDDLE = 133.3333333;

        private const double DIM_TIER_SCALE_15_BASE = 150.0;
        private const double DIM_TIER_SCALE_15_STEP = 150.0;
        private const double DIM_TIER_SCALE_15_MIDDLE = 200.0;

        private const double DIM_TIER_SCALE_20_BASE = 200.0;
        private const double DIM_TIER_SCALE_20_STEP = 200.0;
        private const double DIM_TIER_SCALE_20_MIDDLE = 266.6666667;

        private const double DIM_TIER_SCALE_30_BASE = 300.0;
        private const double DIM_TIER_SCALE_30_STEP = 300.0;
        private const double DIM_TIER_SCALE_30_MIDDLE = 400.0;

        // SCALE KHOẢNG CÁCH PHỤ THEO CHIỀU DÀI DẦM:
        // Không dùng cho hệ tầng DIM; giữ nguyên cho khoảng hở Section hiện có.
        private const double SHORT_BEAM_DIM_SCALE_LIMIT = 2000.0;
        private const double SHORT_BEAM_DIM_SCALE = 1.0 / 2.0;

        // Không dùng hằng số cố định cho chân DIM lỗ.
        // Giá trị hở chân DIM phải lấy theo phi lỗ thật đọc được từ Tekla/model.
        private const double MIN_VALID_HOLE_DIM_GAP = 1.0;

        // L-SHAPE HOLE FACE CLASSIFICATION:
        // Face names belong to the drawing views, not to a fixed part-local Y/Z axis.
        // Direction is the primary signal; nearest solid face is used only for tied directions.
        private const double L_HOLE_DIRECTION_TIE_TOL = 0.02;
        private const double L_HOLE_CATALOG_DUP_TOL = 0.5;

        private const double TOP_FLANGE_DEPTH_TOL = 3.0;


        // TOP VIEW - GIỚI HẠN HÌNH CHIẾU THEO COORDINATE:
        // Chỉ dùng cho biên dạng chiếu TOP để dim tổng/chamfer/rãnh.
        // Không dùng độ sâu cố định 20mm nữa.
        // Rule mới: Có thể tủy chỉnh mặt trên xuống gần hết chiều sâu dầm,
        // Có thể tùy chỉnh vùng XXmm sâu đáy để tránh rãnh/cạnh đáy bị dim nhầm ở TOP.
        private const double TOP_PROJECTED_BOTTOM_EXCLUDE = 0.0;
        private const double FRONT_END_HOLE_ZONE = 300.0;

        private const bool ENABLE_TOP_VIEW_CHAMFER_DIM = true;

        // TOP VIEW: thiết kế hiện tại không DIM rãnh/notch ở mặt Top.
        // Chamfer ngoài vẫn giữ nguyên. Front/Bottom vẫn DIM rãnh/notch như cũ.
        private const bool ENABLE_TOP_VIEW_NOTCH_DIM = false;

        // TOP VIEW CHAMFER DIM
        // Chân DIM chamfer bắt đúng 2 đầu cạnh xiên thật.
        // Offset DIM chamfer = kích thước chamfer + 200.
        private const double CHAMFER_MIN_SIZE = 5.0;
        private const double CHAMFER_MAX_SIZE = 250.0;
        private const double CHAMFER_MIN_RATIO = 0.20;
        private const double CHAMFER_MAX_RATIO = 5.00;
        private const double CHAMFER_DIM_EXTRA_OFFSET = 200.0;
        private const double NOTCH_MIN_SIZE = 5.0;
        private const double NOTCH_MAX_SIZE = 250.0;
        // Chỉ chặn các DIM rãnh quá nhỏ do fillet sinh ra.
        // Không dùng ratio để tránh lọc mất rãnh thật.
        private const double NOTCH_MIN_DIM_TO_CREATE = 15.0;

        // CHECK TOP/BOTTOM HOLES
        // So sánh lỗ cánh trên và cánh dưới. Nếu khác thì báo popup, không đánh dấu trên drawing.
        private const bool ENABLE_TOP_BOTTOM_HOLE_CHECK = false;
        private const double TOP_BOTTOM_HOLE_POSITION_TOL = 2.0;
        private const double TOP_BOTTOM_HOLE_SIZE_TOL = 1.0;

        // AUTO SCALE THEO KHỔ GIẤY + CHIỀU DÀI THANH
        // Chạy trước khi tạo DIM.
        // A3: trừ margin 20mm. A1/khổ khác: trừ margin 30mm.
        // Thép L dùng reserve 300mm để chừa vùng DIM dọc.
        private const bool AUTO_SCALE_BY_PART_LENGTH = true;
        private const double AUTO_SCALE_RESERVE = 200.0;
        private const double A3_SHEET_WIDTH = 420.0;
        private const double A3_SHEET_HEIGHT = 297.0;
        private const double A1_SHEET_WIDTH = 841.0;
        private const double A1_SHEET_HEIGHT = 594.0;
        private const double A3_SHEET_MARGIN = 20.0;
        private const double DEFAULT_SHEET_MARGIN = 30.0;
        private const double SHEET_SIZE_TOLERANCE = 2.0;

        // Check lỗ Top//bottom có khác nhau ko
        public static int TopBottomHoleCheckResult = 0;
        private static double LastAppliedAutoScale = 0.0;
        private static double CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
        private static double CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
        private static double CurrentMiddleVerticalDimOffset =
            DIM_TIER_SCALE_15_MIDDLE;
        private static int LastTopMaxDimTier = 1;
        private static int LastFrontMaxDimTier = 1;

        // SELECTED MAIN PART MODE:
        // Không pick: giữ nguyên flow tự nhận MainPart gốc.
        // Có pick Drawing.Part: dùng part được pick và lọc bolt/lỗ đúng part đó.
        private static bool UseSelectedMainPartMode = false;
        private static ModelPart SelectedMainPartForBoltFilter = null;
        private static ModelPart CurrentLShapeHolePartForLocalClassify = null;
        private static View CurrentLShapeTopViewForHoleClassify = null;
        private static View CurrentLShapeFrontViewForHoleClassify = null;
        private static bool CurrentLShapeHoleCatalogInitialized = false;
        private static readonly List<LShapeHoleRecord> CurrentLShapeHoleCatalog =
            new List<LShapeHoleRecord>();

        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
            TopBottomHoleCheckResult = 0;
            LastTopMaxDimTier = 1;
            LastFrontMaxDimTier = 1;
            LastAppliedAutoScale = 0.0;
            CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
            CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
            CurrentMiddleVerticalDimOffset =
                DIM_TIER_SCALE_15_MIDDLE;
            UseSelectedMainPartMode = false;
            SelectedMainPartForBoltFilter = null;
            CurrentLShapeHolePartForLocalClassify = null;
            CurrentLShapeTopViewForHoleClassify = null;
            CurrentLShapeFrontViewForHoleClassify = null;
            CurrentLShapeHoleCatalogInitialized = false;
            CurrentLShapeHoleCatalog.Clear();
            DrawingHandler dh = new DrawingHandler();
            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null) return;

            bool isSinglePartDrawing = drawing is SinglePartDrawing;

            Model model = new Model();
            if (!model.GetConnectionStatus()) return;

            // HỖ TRỢ CẢ SINGLE PART + ASSEMBLY DRAWING:
            // Không pick: giữ nguyên cách tự nhận MainPart gốc.
            // Có pick Drawing.Part: dùng đúng part được pick và bật lọc bolt/lỗ theo part đó.
            DrawingPart selectedDrawingPart = GetSelectedDrawingPart(dh);
            ModelPart selectedModelPart = null;
            if (selectedDrawingPart != null && selectedDrawingPart.ModelIdentifier != null)
                selectedModelPart = TrySelectModelPart(model, selectedDrawingPart.ModelIdentifier);

            ModelPart part = null;
            if (selectedModelPart != null)
            {
                part = selectedModelPart;
                UseSelectedMainPartMode = true;
                SelectedMainPartForBoltFilter = part;
            }
            else
            {
                part = GetMainPartFromDrawing(model, drawing);
            }

            if (part == null) return;

            CurrentLShapeHolePartForLocalClassify = part;

            List<View> views = GetMainPartViews(drawing, part.Identifier);
            if (views.Count == 0) return;

            // CHỌN MẶT THEO VIEWTYPE GIỐNG FILE TẠO MARK:
            // Không đoán Top/Front/Section bằng vị trí Origin.Y hay view nhỏ nhất nữa.
            // Dump đã xác nhận mặt cắt thật là ViewType == SectionView.
            View topViewByType = FindViewByViewType(views, "TopView", "Top");
            View frontViewByType = FindViewByViewType(views, "FrontView", "Front");
            View sectionView = FindViewByViewType(views, "SectionView", "Section");

            if (topViewByType == null || frontViewByType == null)
                return;

            if (IsSameView(frontViewByType, topViewByType) || IsSameView(frontViewByType, sectionView))
                return;

            InitializeLShapeHoleCatalog(
                model,
                part,
                topViewByType,
                frontViewByType
            );

            // BƯỚC 0: EXACT VIEW THEO VIEWTYPE.
            // Single-part: chỉ chuyển Exact cho SectionView thật.
            // Không dùng FindSmallestViewByRestrictionBox nữa để tránh lấy nhầm Top/Front.
            if (isSinglePartDrawing && sectionView != null)
            {
                ApplyExactRepresentationToView(sectionView);
                CommitAndWait(drawing, 250);
            }

            List<View> dimViews = BuildDimViewsByViewTypeSafe(
                views,
                sectionView,
                topViewByType,
                frontViewByType
            );

            if (dimViews.Count == 0)
                return;

            View topView = topViewByType;

            // BƯỚC 1: Xóa DIM cũ trước giống code plate chuẩn.
            // Chỉ xóa DIM cũ, không đụng thuật toán tạo DIM shape phía dưới.
            DeleteAllDimensions(drawing);
            CommitAndWait(drawing, 250);

            // BƯỚC 2: Auto scale chỉ áp dụng cho Single Part Drawing.
            // Assembly Drawing giữ nguyên scale do người dùng thiết lập.
            bool hasManualScale =
                TTSK_AutoDim_Plates.ManualDrawingScaleOverride.HasOverride;
            if (hasManualScale ||
                (AUTO_SCALE_BY_PART_LENGTH && isSinglePartDrawing))
            {
                ApplyAutoScaleByPartLength(drawing, model, part, topView, views);
                CommitAndWait(drawing, 500);
            }

            VerifyManualScaleApplied(views);
            InitializeCurrentDimTierSpacing(topView);

            TopBoundary boundary;
            CreateDimsForTopView(model, part, topView, out boundary);
            CommitAndWait(drawing, 250);

            if (boundary.IsValid)
            {
                ResizeViewBoundaryKeepDepth(
                    topView,
                    boundary.MinX,
                    boundary.MaxX,
                    boundary.MinY,
                    boundary.MaxY
                );
            }
            else
            {
                ResizeViewBoundaryKeepDepthBySolid(topView, model, part);
            }

            CommitAndWait(drawing, 250);

            View frontView = null;
            TopBoundary frontBoundary = new TopBoundary();

            // FRONT VIEW: ưu tiên ViewType == FrontView giống file tạo Mark.
            // Không lấy "view thứ 2 theo Origin.Y" nữa.
            frontView = frontViewByType;
            if (frontView == null || IsSameView(frontView, topView) || IsSameView(frontView, sectionView))
                return;

            if (frontView != null)
            {
                CreateDimsForFrontView(model, part, frontView, out frontBoundary);
                CommitAndWait(drawing, 250);

                if (frontBoundary.IsValid)
                {
                    ResizeViewBoundaryKeepDepth(
                        frontView,
                        frontBoundary.MinX,
                        frontBoundary.MaxX,
                        frontBoundary.MinY,
                        frontBoundary.MaxY
                    );
                }
                else
                {
                    ResizeViewBoundaryKeepDepthBySolid(frontView, model, part);
                }

                CommitAndWait(drawing, 250);
            }

            AlignMainViewsByGeometry(topView, boundary, frontView, frontBoundary);
            const double finalGreenBoxGap = 15.0;
            ArrangeSectionViewRightOfFront(
                sectionView,
                frontView,
                frontBoundary,
                boundary,
                finalGreenBoxGap);
            CenterViewGroupOnSheet(drawing, topView, boundary, frontView, frontBoundary, sectionView);

            // ARRANGE CUỐI MỚI: dùng KHUNG XANH để ép gap 15 có tính cả DIM/mark.
            // THÉP L chỉ xử lý Top / Front. Không đụng thuật toán DIM/center/align khác.
            ForceFinalEqualArrangeShapeTopFrontGap15(
                topView,
                frontView,
                finalGreenBoxGap);
            CommitAndWait(drawing, 250);

            // Sau khi Top/Front bung gap 15, đưa mặt cắt bám lại theo Front.
            ArrangeSectionViewRightOfFront(
                sectionView,
                frontView,
                frontBoundary,
                boundary,
                finalGreenBoxGap);
            CommitAndWait(drawing, 250);

            UpdateDrawingTitle3Scale(drawing, topView);
            CommitAndWait(drawing, 250);

            // THÉP L: chỉ có TOP + FRONT. Không chạy Bottom View.
            // View mặt cắt nhỏ chỉ dùng Exact/hiển thị, không dim.

            if (ENABLE_TOP_BOTTOM_HOLE_CHECK)
            {
                CheckTopBottomHolesAndMark(model, part, topView);
                CommitAndWait(drawing, 250);
            }

            SelectViews(dh, views);
        }

        private struct TopBoundary
        {
            public bool IsValid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private struct ChamferInfluence
        {
            public bool Left;
            public bool Right;
            public bool Top;
            public bool Bottom;
            public bool Any;
        }

        private struct ChamferEdgeAnchors
        {
            public Point TopLeft;
            public Point TopRight;
            public Point BottomLeft;
            public Point BottomRight;

            // Dùng riêng cho DIM tổng:
            // Khi có chamfer ở góc, điểm trên mép trên/dưới có thể không còn là điểm ngoài cùng thật.
            // DIM tổng phải bắt vào điểm ngoài cùng thật của dầm:
            // - DIM tổng ngang: LeftMost -> RightMost
            // - DIM tổng dọc  : BottomMost -> TopMost
            public Point LeftMost;
            public Point RightMost;
            public Point BottomMost;
            public Point TopMost;
        }

        private sealed class DimOffsetAnchor4
        {
            public Point A;
            public Point B;
            public Point C;
            public Point D;
            public bool IsValid;
        }

        private class LDimTierManager
        {
            private int TopNext = 1;
            private int BottomNext = 1;
            private int LeftNext = 1;
            private int RightNext = 1;

            public int ReserveTop()
            {
                int tier = TopNext;
                TopNext++;
                return tier;
            }

            public int ReserveBottom()
            {
                int tier = BottomNext;
                BottomNext++;
                return tier;
            }

            public int ReserveLeft()
            {
                int tier = LeftNext;
                LeftNext++;
                return tier;
            }

            public int ReserveRight()
            {
                int tier = RightNext;
                RightNext++;
                return tier;
            }

            public int ReserveHorizontal(bool topSide)
            {
                return topSide ? ReserveTop() : ReserveBottom();
            }

            public int ReserveVertical(bool leftSide)
            {
                return leftSide ? ReserveLeft() : ReserveRight();
            }
        }

        private class HoleCheckInfo
        {
            public double X;
            public double Y;
            public double Diameter;
            public double SlotX;
            public double SlotY;
            public string HoleType;
            public bool Matched;

            public Point ToPoint()
            {
                return new Point(X, Y, 0);
            }
        }

        private static void CheckTopBottomHolesAndMark(
            Model model,
            ModelPart part,
            View topView)
        {
            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                if (model == null || part == null || topView == null)
                    return;

                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(topView.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                Point solidMin = solid.MinimumPoint;
                Point solidMax = solid.MaximumPoint;

                double flangeThickness = GetFlangeThicknessFromProfile(part);
                if (flangeThickness <= 0.0)
                    flangeThickness = 20.0;

                // TOP PROJECTED POLYGON: dùng cho DIM tổng/chamfer/rãnh và DIM NGANG lỗ.
                List<Point> topPolygon = GetTopFacePolygon(solid, solidMin, solidMax);

                // TOP SECTION POLYGON: dùng riêng cho DIM DỌC lỗ, giữ đúng thuật toán file gốc của bạn.
                // Không dùng polygon hình chiếu ở đây để tránh bắt nhầm rãnh/cạnh đáy khi dò theo đường X của lỗ.
                List<Point> topHoleVerticalPolygon = GetTopSectionFacePolygon(solid, solidMin, solidMax);
                if (topHoleVerticalPolygon == null || topHoleVerticalPolygon.Count < 2)
                    topHoleVerticalPolygon = topPolygon;

                double holeVerticalMinX, holeVerticalMaxX, holeVerticalMinY, holeVerticalMaxY;
                GetMinMax(topHoleVerticalPolygon, out holeVerticalMinX, out holeVerticalMaxX, out holeVerticalMinY, out holeVerticalMaxY);

                double minX, maxX, minY, maxY;
                if (topPolygon.Count >= 2)
                    GetMinMax(topPolygon, out minX, out maxX, out minY, out maxY);
                else
                {
                    minX = solidMin.X;
                    maxX = solidMax.X;
                    minY = solidMin.Y;
                    maxY = solidMax.Y;
                }

                List<HoleCheckInfo> topHoles =
                    GetTopBottomCheckHolesFromView(
                        model,
                        topView,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        solidMax.Z - flangeThickness - TOP_FLANGE_DEPTH_TOL,
                        solidMax.Z + TOP_FLANGE_DEPTH_TOL
                    );

                List<HoleCheckInfo> bottomHoles =
                    GetTopBottomCheckHolesFromView(
                        model,
                        topView,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        solidMin.Z - TOP_FLANGE_DEPTH_TOL,
                        solidMin.Z + flangeThickness + TOP_FLANGE_DEPTH_TOL
                    );

                TopBottomHoleCheckResult = AreTopBottomHolesDifferent(topHoles, bottomHoles)
                    ? 1
                    : 0;
            }
            catch
            {
            }
            finally
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static List<HoleCheckInfo> GetTopBottomCheckHolesFromView(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double zMin,
            double zMax)
        {
            List<HoleCheckInfo> result = new List<HoleCheckInfo>();

            try
            {
                DrawingObjectEnumerator boltObjects =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));

                while (boltObjects.MoveNext())
                {
                    DrawingObject drawingBolt = boltObjects.Current as DrawingObject;
                    if (drawingBolt == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(drawingBolt);
                    if (id == null)
                        continue;

                    ModelObject modelObject = model.SelectModelObject(id);
                    ModelBoltGroup bg = modelObject as ModelBoltGroup;
                    if (bg == null)
                        continue;

                    if (UseSelectedMainPartMode && !IsBoltBelongsToSelectedMainPart(bg, SelectedMainPartForBoltFilter))
                        continue;

                    double holeDiameter = GetHoleDiameterFromBoltGroup(bg);
                    if (holeDiameter <= MIN_VALID_HOLE_DIM_GAP)
                        holeDiameter = GetHoleDiameterFromDrawingBolt(drawingBolt);

                    double slotX = GetHoleSlotX(bg);
                    double slotY = GetHoleSlotY(bg);
                    string holeType = GetHoleTypeText(bg);

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;


                        if (p.X < minX - 10.0 ||
                            p.X > maxX + 10.0 ||
                            p.Y < minY - 10.0 ||
                            p.Y > maxY + 10.0)
                            continue;

                        if (p.Z < zMin || p.Z > zMax)
                            continue;

                        HoleCheckInfo h = new HoleCheckInfo();
                        h.X = p.X;
                        h.Y = p.Y;
                        h.Diameter = holeDiameter;
                        h.SlotX = slotX;
                        h.SlotY = slotY;
                        h.HoleType = holeType;
                        h.Matched = false;

                        AddUniqueHoleCheckInfo(result, h, TOP_BOTTOM_HOLE_POSITION_TOL);
                    }
                }
            }
            catch
            {
            }

            result.Sort(delegate (HoleCheckInfo a, HoleCheckInfo b)
            {
                int c = a.X.CompareTo(b.X);
                if (c != 0) return c;
                return a.Y.CompareTo(b.Y);
            });

            return result;
        }

        private static void AddUniqueHoleCheckInfo(
            List<HoleCheckInfo> list,
            HoleCheckInfo h,
            double tol)
        {
            if (list == null || h == null)
                return;

            foreach (HoleCheckInfo q in list)
            {
                if (q == null)
                    continue;

                if (Math.Abs(q.X - h.X) <= tol &&
                    Math.Abs(q.Y - h.Y) <= tol)
                {
                    if (h.Diameter > q.Diameter)
                        q.Diameter = h.Diameter;
                    if (h.SlotX > q.SlotX)
                        q.SlotX = h.SlotX;
                    if (h.SlotY > q.SlotY)
                        q.SlotY = h.SlotY;
                    if (!string.IsNullOrEmpty(h.HoleType))
                        q.HoleType = h.HoleType;
                    return;
                }
            }

            list.Add(h);
        }

        private static bool AreTopBottomHolesDifferent(
            List<HoleCheckInfo> topHoles,
            List<HoleCheckInfo> bottomHoles)
        {
            try
            {
                if (topHoles == null) topHoles = new List<HoleCheckInfo>();
                if (bottomHoles == null) bottomHoles = new List<HoleCheckInfo>();

                foreach (HoleCheckInfo top in topHoles)
                    top.Matched = false;

                foreach (HoleCheckInfo bottom in bottomHoles)
                    bottom.Matched = false;

                foreach (HoleCheckInfo top in topHoles)
                {
                    HoleCheckInfo bottom = FindMatchingHole(top, bottomHoles);
                    if (bottom != null)
                    {
                        top.Matched = true;
                        bottom.Matched = true;
                    }
                }

                foreach (HoleCheckInfo top in topHoles)
                {
                    if (!top.Matched)
                        return true;
                }

                foreach (HoleCheckInfo bottom in bottomHoles)
                {
                    if (!bottom.Matched)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }


        private static HoleCheckInfo FindMatchingHole(
            HoleCheckInfo target,
            List<HoleCheckInfo> candidates)
        {
            if (target == null || candidates == null)
                return null;

            HoleCheckInfo best = null;
            double bestDist = 999999999.0;

            foreach (HoleCheckInfo h in candidates)
            {
                if (h == null || h.Matched)
                    continue;

                double dx = h.X - target.X;
                double dy = h.Y - target.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist > TOP_BOTTOM_HOLE_POSITION_TOL)
                    continue;

                if (Math.Abs(h.Diameter - target.Diameter) > TOP_BOTTOM_HOLE_SIZE_TOL)
                    continue;

                if (Math.Abs(h.SlotX - target.SlotX) > TOP_BOTTOM_HOLE_SIZE_TOL)
                    continue;

                if (Math.Abs(h.SlotY - target.SlotY) > TOP_BOTTOM_HOLE_SIZE_TOL)
                    continue;

                if (!SameText(h.HoleType, target.HoleType))
                    continue;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = h;
                }
            }

            return best;
        }

        private static bool SameText(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) a = "";
            if (string.IsNullOrEmpty(b)) b = "";

            return string.Equals(
                a.Trim(),
                b.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
        }


        private static double GetHoleSlotX(ModelBoltGroup bg)
        {
            double v = GetReportDouble(bg, "SLOTTED_HOLE_X");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "LONG_HOLE_X");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "SlottedHoleX");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "SlotX");
            if (v > 0.0 && v < 500.0) return v;

            return 0.0;
        }

        private static double GetHoleSlotY(ModelBoltGroup bg)
        {
            double v = GetReportDouble(bg, "SLOTTED_HOLE_Y");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "LONG_HOLE_Y");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "SlottedHoleY");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "SlotY");
            if (v > 0.0 && v < 500.0) return v;

            return 0.0;
        }

        private static string GetHoleTypeText(ModelBoltGroup bg)
        {
            string value = GetReportString(bg, "HOLE_TYPE");
            if (!string.IsNullOrEmpty(value)) return value;

            value = GetReportString(bg, "BOLT_HOLE_TYPE");
            if (!string.IsNullOrEmpty(value)) return value;

            value = GetStringPropertyByReflection(bg, "HoleType");
            if (!string.IsNullOrEmpty(value)) return value;

            value = GetStringPropertyByReflection(bg, "BoltHoleType");
            if (!string.IsNullOrEmpty(value)) return value;

            return "";
        }

        private static string GetReportString(ModelBoltGroup bg, string propertyName)
        {
            try
            {
                string text = "";
                bg.GetReportProperty(propertyName, ref text);
                if (text == null)
                    return "";
                return text.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string GetStringPropertyByReflection(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return "";

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return "";

                object value = prop.GetValue(obj, null);
                if (value == null)
                    return "";

                return value.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }

        private static int CreateDimsForTopView(
            Model model,
            ModelPart part,
            View view,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();
            int count = 0;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                Point solidMin = solid.MinimumPoint;
                Point solidMax = solid.MaximumPoint;

                double flangeThickness = GetFlangeThicknessFromProfile(part);

                List<Point> topPolygon = GetTopFacePolygon(solid, solidMin, solidMax);

                // TOP SECTION POLYGON: dùng riêng cho DIM DỌC lỗ, giữ đúng thuật toán file gốc của bạn.
                // Không dùng polygon hình chiếu ở đây để tránh bắt nhầm rãnh/cạnh đáy khi dò theo đường X của lỗ.
                List<Point> topHoleVerticalPolygon = GetTopSectionFacePolygon(solid, solidMin, solidMax);
                if (topHoleVerticalPolygon == null || topHoleVerticalPolygon.Count < 2)
                    topHoleVerticalPolygon = topPolygon;

                double holeVerticalMinX, holeVerticalMaxX, holeVerticalMinY, holeVerticalMaxY;
                GetMinMax(topHoleVerticalPolygon, out holeVerticalMinX, out holeVerticalMaxX, out holeVerticalMinY, out holeVerticalMaxY);

                double minX, maxX, minY, maxY;

                if (topPolygon.Count >= 2)
                {
                    GetMinMax(topPolygon, out minX, out maxX, out minY, out maxY);
                }
                else
                {
                    minX = solidMin.X;
                    maxX = solidMax.X;
                    minY = solidMin.Y;
                    maxY = solidMax.Y;
                }

                boundary.IsValid = true;
                boundary.MinX = minX;
                boundary.MaxX = maxX;
                boundary.MinY = minY;
                boundary.MaxY = maxY;

                double beamLength = Math.Abs(maxX - minX);

                StraightDimensionSetHandler handler =
                    new StraightDimensionSetHandler();

                LDimTierManager tierManager = new LDimTierManager();

                bool chamferDimCreated = false;
                ChamferInfluence chamferInfluence = new ChamferInfluence();
                ChamferEdgeAnchors edgeAnchors = BuildChamferEdgeAnchors(topPolygon, minX, maxX, minY, maxY);
                DimOffsetAnchor4 offsetAnchors =
                    BuildDimOffsetAnchor4(edgeAnchors);

                if (ENABLE_TOP_VIEW_CHAMFER_DIM)
                {
                    int chamferCount = CreateTopViewChamferDims(
                        handler,
                        view,
                        topPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        beamLength,
                        out chamferInfluence
                    );

                    chamferDimCreated = chamferCount > 0;
                    if (chamferDimCreated)
                        chamferInfluence.Any = true;

                    count += chamferCount;
                }

                // TOP VIEW - CHAMFER TIER RULE:
                // Chamfer dùng tầng 0 riêng.
                // Nếu phía nào có chamfer/notch thì phía đó giữ tầng 1,
                // các DIM lỗ/tổng cùng phía bắt đầu từ tầng 2.
                bool topChamferTierReserved = false;
                bool bottomChamferTierReserved = false;

                ChamferInfluence notchInfluence = new ChamferInfluence();
                int notchCount = 0;

                // TOP VIEW: không DIM rãnh/notch để tránh bắt nhầm rãnh mặt Front chiếu lên Top.
                // Chamfer ngoài vẫn giữ nguyên vì đã chạy ở CreateTopViewChamferDims phía trên.
                if (ENABLE_TOP_VIEW_NOTCH_DIM)
                {
                    notchCount = CreateAxisAlignedNotchDims(
                        handler,
                        view,
                        topPolygon,
                        offsetAnchors,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        beamLength,
                        out notchInfluence
                    );

                    if (notchCount > 0)
                    {
                        MergeInfluence(ref chamferInfluence, notchInfluence);
                        count += notchCount;
                    }
                }

                // Sau khi đã gộp chamfer + notch, mới xác định phía nào bị chiếm tầng 1.
                // Chỉ phía bị ảnh hưởng mới nhảy tầng, các phía khác giữ tầng như cũ.
                topChamferTierReserved = chamferInfluence.Top;
                bottomChamferTierReserved = chamferInfluence.Bottom;

                // THÉP L - TOP VIEW:
                // Chỉ nhận lỗ từ cạnh ĐỎ đi lên đúng 1 độ dày cánh.
                // Không dùng lỗ đơn; toàn bộ lỗ đi qua thuật toán chain cụm bên dưới.
                double lLegThickness = GetLThicknessFromProfile(part);
                if (lLegThickness <= 0.0)
                    lLegThickness = flangeThickness;
                if (lLegThickness <= 0.0)
                    lLegThickness = 6.0;

                count += CreateLTopEndNotchDims(
                    handler,
                    view,
                    topPolygon,
                    offsetAnchors,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    beamLength,
                    lLegThickness,
                    tierManager,
                    false
                );

                count += CreateLTopEndNotchDims(
                    handler,
                    view,
                    topPolygon,
                    offsetAnchors,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    beamLength,
                    lLegThickness,
                    tierManager,
                    true
                );

                List<Point> topFlangeHoles =
                    GetVisibleLTopBoltCentersFromRedStrip(
                        model,
                        view,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        solidMin.Z,
                        solidMax.Z,
                        lLegThickness
                    );

                bool holeDimCreated = false;
                bool horizontalHoleDimCreated = false;
                bool verticalHoleDimCreated = false;
                int topHoleTierCount = 0;

                if (topFlangeHoles.Count > 0)
                {
                    int holeCount = 0;

                    holeCount += CreateLHoleChainDimsEdgeToEdge(
                        handler,
                        view,
                        topFlangeHoles,
                        offsetAnchors,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        beamLength,
                        tierManager,
                        out topHoleTierCount,
                        out horizontalHoleDimCreated,
                        out verticalHoleDimCreated
                    );

                    holeDimCreated = holeCount > 0;
                    count += holeCount;
                }

                // THÉP L - TẦNG ĐỘC LẬP 4 HƯỚNG:
                // Top / Bottom / Left / Right có bộ đếm riêng.
                // DIM lỗ đã chiếm tầng qua tierManager trong CreateLHoleChainDimsEdgeToEdge.
                // DIM tổng lấy tầng kế tiếp của đúng hướng, không dùng chung tầng với DIM lỗ.
                int topHorizontalTier = tierManager.ReserveTop();
                int leftVerticalTier = tierManager.ReserveLeft();

                LastTopMaxDimTier = Math.Max(topHorizontalTier, leftVerticalTier);

                count += CreateTopViewTotalDims(
                    handler,
                    view,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    edgeAnchors,
                    offsetAnchors,
                    GetSteelDimOffsetByTier(topHorizontalTier),
                    GetSteelDimOffsetByTier(leftVerticalTier)
                );
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


        private static void VerifyManualScaleApplied(List<View> views)
        {
            double manualScale;
            if (!TTSK_AutoDim_Plates.ManualDrawingScaleOverride.TryGet(
                    out manualScale))
                return;

            bool viewFound = false;
            if (views != null)
            {
                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    viewFound = true;
                    double actualScale = TryGetViewScale(view);
                    if (actualScale <= 0.0 ||
                        Math.Abs(actualScale - manualScale) > 0.001)
                    {
                        throw new InvalidOperationException(
                            "Không áp dụng được manual scale cho toàn bộ target view.");
                    }
                }
            }

            if (!viewFound)
                throw new InvalidOperationException("Không tìm thấy target view để áp dụng manual scale.");
        }

        private static void InitializeCurrentDimTierSpacing(View referenceView)
        {
            CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
            CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
            CurrentMiddleVerticalDimOffset =
                DIM_TIER_SCALE_15_MIDDLE;

            try
            {
                double rawScale = TryGetViewScale(referenceView);

                if (double.IsNaN(rawScale) ||
                    double.IsInfinity(rawScale) ||
                    rawScale <= 0.0)
                    return;

                int scale = Convert.ToInt32(Math.Round(rawScale));

                switch (scale)
                {
                    case 5:
                        CurrentDimTierBase = DIM_TIER_SCALE_5_BASE;
                        CurrentDimTierStep = DIM_TIER_SCALE_5_STEP;
                        CurrentMiddleVerticalDimOffset =
                            DIM_TIER_SCALE_5_MIDDLE;
                        break;

                    case 10:
                        CurrentDimTierBase = DIM_TIER_SCALE_10_BASE;
                        CurrentDimTierStep = DIM_TIER_SCALE_10_STEP;
                        CurrentMiddleVerticalDimOffset =
                            DIM_TIER_SCALE_10_MIDDLE;
                        break;

                    case 15:
                        CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
                        CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
                        CurrentMiddleVerticalDimOffset =
                            DIM_TIER_SCALE_15_MIDDLE;
                        break;

                    case 20:
                        CurrentDimTierBase = DIM_TIER_SCALE_20_BASE;
                        CurrentDimTierStep = DIM_TIER_SCALE_20_STEP;
                        CurrentMiddleVerticalDimOffset =
                            DIM_TIER_SCALE_20_MIDDLE;
                        break;

                    case 30:
                        CurrentDimTierBase = DIM_TIER_SCALE_30_BASE;
                        CurrentDimTierStep = DIM_TIER_SCALE_30_STEP;
                        CurrentMiddleVerticalDimOffset =
                            DIM_TIER_SCALE_30_MIDDLE;
                        break;
                }
            }
            catch
            {
            }
        }

        private static double GetDimScaleByBeamLength(double beamLength)
        {
            // Chỉ scale dầm ngắn dưới 2000mm.
            // Các dầm từ 2000mm trở lên giữ nguyên 100% như code hiện tại.
            if (beamLength > 0.0 && beamLength < SHORT_BEAM_DIM_SCALE_LIMIT)
                return SHORT_BEAM_DIM_SCALE;

            return 1.0;
        }

        private static double GetSteelDimOffsetByTier(int tier)
        {
            int safeTier = Math.Max(0, tier);
            double offset =
                CurrentDimTierBase +
                safeTier * CurrentDimTierStep;

            if (double.IsNaN(offset) ||
                double.IsInfinity(offset) ||
                offset <= 0.0)
            {
                return DIM_TIER_SCALE_15_BASE +
                       safeTier * DIM_TIER_SCALE_15_STEP;
            }

            return offset;
        }



        private static DimOffsetAnchor4 BuildDimOffsetAnchor4(
            ChamferEdgeAnchors edgeAnchors)
        {
            DimOffsetAnchor4 anchors = new DimOffsetAnchor4();

            anchors.IsValid =
                IsValidDimOffsetAnchorPoint(edgeAnchors.LeftMost) &&
                IsValidDimOffsetAnchorPoint(edgeAnchors.RightMost) &&
                IsValidDimOffsetAnchorPoint(edgeAnchors.BottomMost) &&
                IsValidDimOffsetAnchorPoint(edgeAnchors.TopMost);

            if (anchors.IsValid)
            {
                anchors.A = Clone2D(edgeAnchors.LeftMost);
                anchors.B = Clone2D(edgeAnchors.RightMost);
                anchors.C = Clone2D(edgeAnchors.BottomMost);
                anchors.D = Clone2D(edgeAnchors.TopMost);
            }

            return anchors;
        }

        private static bool IsValidDimOffsetAnchorPoint(Point point)
        {
            return point != null &&
                   !double.IsNaN(point.X) &&
                   !double.IsInfinity(point.X) &&
                   !double.IsNaN(point.Y) &&
                   !double.IsInfinity(point.Y);
        }

        private static Point GetFirstDimFoot(PointList dimPoints)
        {
            if (dimPoints == null || dimPoints.Count == 0)
                return null;

            foreach (object obj in dimPoints)
            {
                Point point = obj as Point;
                if (point != null)
                    return point;
            }

            return null;
        }

        private static double ResolveDimDistanceByAnchor4(
            PointList dimPoints,
            Vector direction,
            DimOffsetAnchor4 anchors,
            double tierOffset)
        {
            try
            {
                if (direction == null ||
                    anchors == null ||
                    !anchors.IsValid)
                    return tierOffset;

                Point firstFoot = GetFirstDimFoot(dimPoints);
                if (!IsValidDimOffsetAnchorPoint(firstFoot))
                    return tierOffset;

                double minX = Math.Min(
                    Math.Min(anchors.A.X, anchors.B.X),
                    Math.Min(anchors.C.X, anchors.D.X));
                double maxX = Math.Max(
                    Math.Max(anchors.A.X, anchors.B.X),
                    Math.Max(anchors.C.X, anchors.D.X));
                double minY = Math.Min(
                    Math.Min(anchors.A.Y, anchors.B.Y),
                    Math.Min(anchors.C.Y, anchors.D.Y));
                double maxY = Math.Max(
                    Math.Max(anchors.A.Y, anchors.B.Y),
                    Math.Max(anchors.C.Y, anchors.D.Y));

                double distance;

                if (Math.Abs(direction.Y) >= Math.Abs(direction.X))
                {
                    if (direction.Y > 0.0)
                        distance = (maxY + tierOffset) - firstFoot.Y;
                    else if (direction.Y < 0.0)
                        distance = firstFoot.Y - (minY - tierOffset);
                    else
                        return tierOffset;
                }
                else
                {
                    if (direction.X > 0.0)
                        distance = (maxX + tierOffset) - firstFoot.X;
                    else if (direction.X < 0.0)
                        distance = firstFoot.X - (minX - tierOffset);
                    else
                        return tierOffset;
                }

                if (double.IsNaN(distance) ||
                    double.IsInfinity(distance) ||
                    distance <= 1.0)
                    return tierOffset;

                return distance;
            }
            catch
            {
                return tierOffset;
            }
        }

        private static double ResolveDimDistanceByAnchor4(
            Point firstDimPoint,
            Point secondDimPoint,
            Vector direction,
            DimOffsetAnchor4 anchors,
            double tierOffset)
        {
            PointList dimPoints = new PointList();

            if (firstDimPoint != null)
                dimPoints.Add(Clone2D(firstDimPoint));
            if (secondDimPoint != null)
                dimPoints.Add(Clone2D(secondDimPoint));

            return ResolveDimDistanceByAnchor4(
                dimPoints,
                direction,
                anchors,
                tierOffset);
        }

        private static bool CreateEdgeAnchoredDim(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double tierOffset,
            DimOffsetAnchor4 anchors)
        {
            double distance = ResolveDimDistanceByAnchor4(
                p1,
                p2,
                direction,
                anchors,
                tierOffset);

            return CreateDim(
                handler,
                view,
                p1,
                p2,
                direction,
                distance);
        }

        private static bool CreateEdgeAnchoredNotchDimBySize(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double tierOffset,
            DimOffsetAnchor4 anchors,
            double measuredSize)
        {
            double distance = ResolveDimDistanceByAnchor4(
                p1,
                p2,
                direction,
                anchors,
                tierOffset);

            return CreateNotchDimBySize(
                handler,
                view,
                p1,
                p2,
                direction,
                distance,
                measuredSize);
        }


        private static ChamferEdgeAnchors BuildChamferEdgeAnchors(
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            // Tên hàm giữ lại để không phá các chỗ gọi cũ.
            // Logic mới: không chỉ xử lý chamfer xiên, mà lấy đúng điểm THẬT nằm trên mép dầm.
            // DIM mép nào thì điểm bắt phải nằm trên đúng mép đó:
            // - mép trên  : điểm có Y = maxY
            // - mép dưới  : điểm có Y = minY
            // Nếu có chamfer/rãnh/notch làm mất góc ảo, hàm sẽ tự lấy điểm đầu thật của đoạn mép.
            ChamferEdgeAnchors anchors = new ChamferEdgeAnchors();

            anchors.TopLeft = new Point(minX, maxY, 0);
            anchors.TopRight = new Point(maxX, maxY, 0);
            anchors.BottomLeft = new Point(minX, minY, 0);
            anchors.BottomRight = new Point(maxX, minY, 0);

            anchors.LeftMost = new Point(minX, maxY, 0);
            anchors.RightMost = new Point(maxX, maxY, 0);
            anchors.BottomMost = new Point(minX, minY, 0);
            anchors.TopMost = new Point(minX, maxY, 0);

            try
            {
                if (polygon == null || polygon.Count < 2)
                    return anchors;

                double edgeTol = Math.Max(2.0, TOL + 1.0);

                bool hasTopLeft = false;
                bool hasTopRight = false;
                bool hasBottomLeft = false;
                bool hasBottomRight = false;

                bool hasLeftMost = false;
                bool hasRightMost = false;
                bool hasBottomMost = false;
                bool hasTopMost = false;

                foreach (Point p in polygon)
                {
                    if (p == null)
                        continue;

                    // Điểm ngoài cùng thật của dầm, dùng cho DIM tổng.
                    // Không lấy theo bounding box ảo của góc chamfer.
                    if (!hasLeftMost ||
                        p.X < anchors.LeftMost.X - edgeTol ||
                        (Math.Abs(p.X - anchors.LeftMost.X) <= edgeTol && p.Y > anchors.LeftMost.Y))
                    {
                        anchors.LeftMost = Clone2D(p);
                        hasLeftMost = true;
                    }

                    if (!hasRightMost ||
                        p.X > anchors.RightMost.X + edgeTol ||
                        (Math.Abs(p.X - anchors.RightMost.X) <= edgeTol && p.Y > anchors.RightMost.Y))
                    {
                        anchors.RightMost = Clone2D(p);
                        hasRightMost = true;
                    }

                    if (!hasBottomMost ||
                        p.Y < anchors.BottomMost.Y - edgeTol ||
                        (Math.Abs(p.Y - anchors.BottomMost.Y) <= edgeTol && p.X < anchors.BottomMost.X))
                    {
                        anchors.BottomMost = Clone2D(p);
                        hasBottomMost = true;
                    }

                    if (!hasTopMost ||
                        p.Y > anchors.TopMost.Y + edgeTol ||
                        (Math.Abs(p.Y - anchors.TopMost.Y) <= edgeTol && p.X < anchors.TopMost.X))
                    {
                        anchors.TopMost = Clone2D(p);
                        hasTopMost = true;
                    }

                    if (Math.Abs(p.Y - maxY) <= edgeTol)
                    {
                        if (!hasTopLeft || p.X < anchors.TopLeft.X)
                        {
                            anchors.TopLeft = Clone2D(p);
                            hasTopLeft = true;
                        }

                        if (!hasTopRight || p.X > anchors.TopRight.X)
                        {
                            anchors.TopRight = Clone2D(p);
                            hasTopRight = true;
                        }
                    }

                    if (Math.Abs(p.Y - minY) <= edgeTol)
                    {
                        if (!hasBottomLeft || p.X < anchors.BottomLeft.X)
                        {
                            anchors.BottomLeft = Clone2D(p);
                            hasBottomLeft = true;
                        }

                        if (!hasBottomRight || p.X > anchors.BottomRight.X)
                        {
                            anchors.BottomRight = Clone2D(p);
                            hasBottomRight = true;
                        }
                    }
                }
            }
            catch
            {
            }

            return anchors;
        }


        private static Point Clone2D(Point p)
        {
            if (p == null)
                return new Point(0, 0, 0);

            return new Point(p.X, p.Y, 0);
        }


        private static void MergeInfluence(ref ChamferInfluence target, ChamferInfluence source)
        {
            target.Left = target.Left || source.Left;
            target.Right = target.Right || source.Right;
            target.Top = target.Top || source.Top;
            target.Bottom = target.Bottom || source.Bottom;
            target.Any = target.Any || source.Any;
        }

        private static bool CreateNotchDimBySize(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double distance,
            double measuredSize)
        {
            // Fillet/bo góc thường sinh ra các cạnh rất nhỏ như 7.1, 5.8...
            // Chỉ bỏ các DIM quá nhỏ, giữ nguyên thuật toán nhận rãnh V3.
            if (measuredSize < NOTCH_MIN_DIM_TO_CREATE)
                return false;

            return CreateDim(handler, view, p1, p2, direction, distance);
        }

        private static int CreateLTopEndNotchDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength,
            double legThickness,
            LDimTierManager tierManager,
            bool leftSide)
        {
            int count = 0;

            try
            {
                if (handler == null || view == null || polygon == null || polygon.Count < 2)
                    return count;

                if (tierManager == null)
                    tierManager = new LDimTierManager();

                if (legThickness <= 0.0)
                    legThickness = 6.0;

                Point notchTop;
                Point chamferTop;
                Point chamferEdge;
                Point bottomEdge;

                if (!TryGetLTopEndNotchGeometry(
                    polygon,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    legThickness,
                    leftSide,
                    out notchTop,
                    out chamferTop,
                    out chamferEdge,
                    out bottomEdge))
                    return count;

                double tier0Offset = GetSteelDimOffsetByTier(0);

                if (CreateEdgeAnchoredDim(
                    handler,
                    view,
                    chamferTop,
                    chamferEdge,
                    new Vector(0, 1, 0),
                    tier0Offset,
                    offsetAnchors))
                    count++;

                if (CreateEdgeAnchoredDim(
                    handler,
                    view,
                    notchTop,
                    chamferEdge,
                    new Vector(0, 1, 0),
                    GetSteelDimOffsetByTier(tierManager.ReserveTop()),
                    offsetAnchors))
                    count++;

                PointList verticalChain = new PointList();
                verticalChain.Add(Clone2D(bottomEdge));
                verticalChain.Add(Clone2D(chamferTop));
                verticalChain.Add(Clone2D(notchTop));

                Vector verticalDirection =
                    leftSide ? new Vector(-1, 0, 0) : new Vector(1, 0, 0);
                double verticalTierOffset = GetSteelDimOffsetByTier(
                    leftSide ? tierManager.ReserveLeft() : tierManager.ReserveRight());
                double verticalDistance = ResolveDimDistanceByAnchor4(
                    verticalChain,
                    verticalDirection,
                    offsetAnchors,
                    verticalTierOffset);

                if (handler.CreateDimensionSet(
                    view,
                    verticalChain,
                    verticalDirection,
                    verticalDistance) != null)
                    count++;

                if (CreateEdgeAnchoredDim(
                    handler,
                    view,
                    chamferEdge,
                    chamferTop,
                    leftSide ? new Vector(-1, 0, 0) : new Vector(1, 0, 0),
                    tier0Offset,
                    offsetAnchors))
                    count++;
            }
            catch
            {
            }

            return count;
        }

        private static bool TryGetLTopEndNotchGeometry(
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double legThickness,
            bool leftSide,
            out Point notchTop,
            out Point chamferTop,
            out Point chamferEdge,
            out Point bottomEdge)
        {
            notchTop = null;
            chamferTop = null;
            chamferEdge = null;
            bottomEdge = null;

            try
            {
                if (polygon == null || polygon.Count < 4)
                    return false;

                double edgeTol = Math.Max(1.0, TOL);
                double thicknessY = maxY - legThickness;
                double edgeX = leftSide ? minX : maxX;
                double zoneMinX = leftSide ? minX : maxX - NOTCH_MAX_SIZE;
                double zoneMaxX = leftSide ? minX + NOTCH_MAX_SIZE : maxX;

                foreach (Point p in polygon)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X > zoneMinX + edgeTol &&
                        p.X < zoneMaxX - edgeTol &&
                        (notchTop == null ||
                         (leftSide ? p.X < notchTop.X : p.X > notchTop.X)))
                    {
                        notchTop = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - thicknessY) <= edgeTol &&
                        p.X > zoneMinX + edgeTol &&
                        p.X < zoneMaxX - edgeTol &&
                        (chamferTop == null ||
                         (leftSide ? p.X < chamferTop.X : p.X > chamferTop.X)))
                    {
                        chamferTop = Clone2D(p);
                    }

                    if (Math.Abs(p.X - edgeX) <= edgeTol &&
                        p.Y < thicknessY - edgeTol &&
                        p.Y > minY + edgeTol &&
                        (chamferEdge == null || p.Y > chamferEdge.Y))
                    {
                        chamferEdge = Clone2D(p);
                    }

                    if (Math.Abs(p.X - edgeX) <= edgeTol &&
                        Math.Abs(p.Y - minY) <= edgeTol)
                    {
                        bottomEdge = Clone2D(p);
                    }
                }

                if (notchTop == null || chamferTop == null ||
                    chamferEdge == null || bottomEdge == null)
                    return false;

                double longSize = Math.Abs(edgeX - notchTop.X);
                double chamferWidth = Math.Abs(edgeX - chamferTop.X);
                double chamferHeight = Math.Abs(chamferTop.Y - chamferEdge.Y);
                double thickness = Math.Abs(maxY - chamferTop.Y);

                if (longSize < NOTCH_MIN_DIM_TO_CREATE || longSize > NOTCH_MAX_SIZE)
                    return false;

                if (chamferWidth < NOTCH_MIN_SIZE || chamferWidth > NOTCH_MAX_SIZE ||
                    chamferHeight < NOTCH_MIN_SIZE || chamferHeight > NOTCH_MAX_SIZE)
                    return false;

                if (Math.Abs(thickness - legThickness) > Math.Max(2.0, TOL + 1.0))
                    return false;

                double inwardDirection = leftSide ? 1.0 : -1.0;
                if ((notchTop.X - chamferTop.X) * inwardDirection <= edgeTol ||
                    chamferEdge.Y >= chamferTop.Y - edgeTol)
                    return false;

                double chamferRatio = chamferWidth / chamferHeight;
                if (chamferRatio < CHAMFER_MIN_RATIO || chamferRatio > CHAMFER_MAX_RATIO)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetLFrontEndNotchGeometry(
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double legThickness,
            bool leftSide,
            out Point notchTop,
            out Point thicknessEdge,
            out Point bottomEdge)
        {
            notchTop = null;
            thicknessEdge = null;
            bottomEdge = null;

            try
            {
                if (polygon == null || polygon.Count < 3)
                    return false;

                double edgeTol = Math.Max(1.0, TOL);
                double thicknessY = minY + legThickness;
                double edgeX = leftSide ? minX : maxX;
                double zoneMinX = leftSide ? minX : maxX - NOTCH_MAX_SIZE;
                double zoneMaxX = leftSide ? minX + NOTCH_MAX_SIZE : maxX;

                foreach (Point p in polygon)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X > zoneMinX + edgeTol &&
                        p.X < zoneMaxX - edgeTol &&
                        (notchTop == null ||
                         (leftSide ? p.X < notchTop.X : p.X > notchTop.X)))
                    {
                        notchTop = Clone2D(p);
                    }

                    if (Math.Abs(p.X - edgeX) <= edgeTol &&
                        Math.Abs(p.Y - thicknessY) <= edgeTol)
                    {
                        thicknessEdge = Clone2D(p);
                    }

                    if (Math.Abs(p.X - edgeX) <= edgeTol &&
                        Math.Abs(p.Y - minY) <= edgeTol)
                    {
                        bottomEdge = Clone2D(p);
                    }
                }

                if (notchTop == null || thicknessEdge == null || bottomEdge == null)
                    return false;

                double horizontalSize = Math.Abs(edgeX - notchTop.X);
                double verticalSize = Math.Abs(maxY - thicknessEdge.Y);

                if (horizontalSize < NOTCH_MIN_DIM_TO_CREATE || horizontalSize > NOTCH_MAX_SIZE ||
                    verticalSize < NOTCH_MIN_DIM_TO_CREATE || verticalSize > NOTCH_MAX_SIZE)
                    return false;

                if (Math.Abs((thicknessEdge.Y - minY) - legThickness) > Math.Max(2.0, TOL + 1.0))
                    return false;

                return IsPolygonBoundarySegment(polygon, notchTop, thicknessEdge, edgeTol);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPolygonBoundarySegment(
            List<Point> polygon,
            Point segmentPoint1,
            Point segmentPoint2,
            double tol)
        {
            if (polygon == null || polygon.Count < 2 ||
                segmentPoint1 == null || segmentPoint2 == null)
                return false;

            List<Point> sorted = SortPolygonPointsClockwise(polygon);
            for (int i = 0; i < sorted.Count; i++)
            {
                Point a = sorted[i];
                Point b = sorted[(i + 1) % sorted.Count];

                if (IsSameSegment2D(a, b, segmentPoint1, segmentPoint2, tol))
                    return true;
            }

            return false;
        }

        private static bool IsSameSegment2D(
            Point a,
            Point b,
            Point segmentPoint1,
            Point segmentPoint2,
            double tol)
        {
            if (a == null || b == null || segmentPoint1 == null || segmentPoint2 == null)
                return false;

            bool sameDirection =
                Distance2D(a, segmentPoint1) <= tol &&
                Distance2D(b, segmentPoint2) <= tol;

            bool reverseDirection =
                Distance2D(a, segmentPoint2) <= tol &&
                Distance2D(b, segmentPoint1) <= tol;

            return sameDirection || reverseDirection;
        }

        private static int CreateLFrontEndNotchDims(
            StraightDimensionSetHandler handler,
            View view,
            Point notchTop,
            Point thicknessEdge,
            DimOffsetAnchor4 offsetAnchors,
            Point bottomEdge,
            double beamLength,
            LDimTierManager tierManager,
            bool leftSide)
        {
            int count = 0;

            try
            {
                if (handler == null || view == null || notchTop == null ||
                    thicknessEdge == null || bottomEdge == null)
                    return count;

                if (tierManager == null)
                    tierManager = new LDimTierManager();

                if (CreateEdgeAnchoredDim(
                    handler,
                    view,
                    notchTop,
                    thicknessEdge,
                    new Vector(0, 1, 0),
                    GetSteelDimOffsetByTier(tierManager.ReserveTop()),
                    offsetAnchors))
                    count++;

                PointList verticalChain = new PointList();
                verticalChain.Add(Clone2D(bottomEdge));
                verticalChain.Add(Clone2D(thicknessEdge));
                verticalChain.Add(Clone2D(notchTop));

                Vector verticalDirection =
                    leftSide ? new Vector(-1, 0, 0) : new Vector(1, 0, 0);
                double verticalTierOffset = GetSteelDimOffsetByTier(
                    leftSide ? tierManager.ReserveLeft() : tierManager.ReserveRight());
                double verticalDistance = ResolveDimDistanceByAnchor4(
                    verticalChain,
                    verticalDirection,
                    offsetAnchors,
                    verticalTierOffset);

                if (handler.CreateDimensionSet(
                    view,
                    verticalChain,
                    verticalDirection,
                    verticalDistance) != null)
                    count++;
            }
            catch
            {
            }

            return count;
        }

        private static int CreateAxisAlignedNotchDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength,
            out ChamferInfluence influence)
        {
            // RÃNH / NOTCH - V3
            // Không dựa vào bounding box ảo và không cần điểm rãnh trùng mép dầm nguyên vẹn.
            // Thuật toán:
            // 1. Lấy các điểm polygon thật nằm lõm vào gần từng mép ngoài.
            // 2. Nếu có ít nhất 2 điểm lõm tạo thành bề rộng + chiều sâu hợp lý => xem là rãnh.
            // 3. DIM ngang + dọc rãnh đặt theo neo A/B/C/D của DIM tổng.
            // 4. Chỉ trả influence cạnh bị rãnh để dim tổng liên quan tự đẩy tầng.
            influence = new ChamferInfluence();
            int count = 0;

            try
            {
                if (polygon == null || polygon.Count < 4)
                    return count;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double minWidth = NOTCH_MIN_SIZE;
                double minDepth = NOTCH_MIN_SIZE;

                // =========================
                // RÃNH MỞ Ở MÉP DƯỚI
                // =========================
                List<Point> bottomInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.Y > minY + edgeTol &&
                        p.Y <= minY + NOTCH_MAX_SIZE &&
                        p.X > minX + edgeTol &&
                        p.X < maxX - edgeTol)
                    {
                        bottomInner.Add(Clone2D(p));
                    }
                }

                if (bottomInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(bottomInner, out x1, out x2, out y1, out y2);

                    double width = Math.Abs(x2 - x1);
                    double depth = Math.Abs(y2 - minY);

                    if (width >= minWidth && depth >= minDepth &&
                        width <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        Point outerLeft = FindEdgePointNearestX(polygon, x1, minY, true, edgeTol);
                        Point outerRight = FindEdgePointNearestX(polygon, x2, minY, true, edgeTol);
                        Point innerLeft = FindExtremePointOnHorizontalBand(bottomInner, y2, true, edgeTol, minX, maxX, minY, maxY);
                        Point innerRight = FindExtremePointOnHorizontalBand(bottomInner, y2, false, edgeTol, minX, maxX, minY, maxY);

                        if (innerLeft == null) innerLeft = FindNearestPoint(bottomInner, x1, y2);
                        if (innerRight == null) innerRight = FindNearestPoint(bottomInner, x2, y2);

                        if (outerLeft != null && outerRight != null && innerLeft != null && innerRight != null)
                        {
                            // DIM dọc chiều sâu rãnh:
                            // Chọn phía gần MÉP NGOÀI CÙNG của thanh hơn.
                            // Tránh luôn lấy phía trái rồi bắt nhầm vào điểm fillet ở trong rãnh.
                            double notchMidX = (x1 + x2) / 2.0;
                            bool useRightSideForDepth = Math.Abs(maxX - notchMidX) < Math.Abs(notchMidX - minX);

                            // FIX THEO YÊU CẦU:
                            // DIM chiều sâu rãnh không bắt ở endpoint bên trong/fillet nữa.
                            // Chân DIM được đưa ra hẳn mép ngoài cùng của dầm:
                            // - rãnh gần đầu phải -> dùng X = maxX
                            // - rãnh gần đầu trái  -> dùng X = minX
                            Point depthOuter;
                            Point depthInner;

                            if (!TryGetBottomNotchDepthSegment(
                                polygon,
                                useRightSideForDepth,
                                x1,
                                x2,
                                y2,
                                minY,
                                edgeTol,
                                out depthOuter,
                                out depthInner))
                            {
                                double outerBeamX = useRightSideForDepth ? maxX : minX;
                                depthOuter = new Point(outerBeamX, minY, 0);
                                depthInner = new Point(outerBeamX, y2, 0);
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                depth))
                                count++;

                            // DIM ngang bề rộng rãnh.
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, -1, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                width))
                                count++;

                            influence.Bottom = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP TRÊN
                // =========================
                List<Point> topInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.Y < maxY - edgeTol &&
                        p.Y >= maxY - NOTCH_MAX_SIZE &&
                        p.X > minX + edgeTol &&
                        p.X < maxX - edgeTol)
                    {
                        topInner.Add(Clone2D(p));
                    }
                }

                if (topInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(topInner, out x1, out x2, out y1, out y2);

                    double width = Math.Abs(x2 - x1);
                    double depth = Math.Abs(maxY - y1);

                    if (width >= minWidth && depth >= minDepth &&
                        width <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        Point outerLeft = FindEdgePointNearestX(polygon, x1, maxY, true, edgeTol);
                        Point outerRight = FindEdgePointNearestX(polygon, x2, maxY, true, edgeTol);
                        Point innerLeft = FindExtremePointOnHorizontalBand(topInner, y1, true, edgeTol, minX, maxX, minY, maxY);
                        Point innerRight = FindExtremePointOnHorizontalBand(topInner, y1, false, edgeTol, minX, maxX, minY, maxY);

                        if (innerLeft == null) innerLeft = FindNearestPoint(topInner, x1, y1);
                        if (innerRight == null) innerRight = FindNearestPoint(topInner, x2, y1);

                        if (outerLeft != null && outerRight != null && innerLeft != null && innerRight != null)
                        {
                            double notchMidX = (x1 + x2) / 2.0;
                            bool useRightSideForDepth = Math.Abs(maxX - notchMidX) < Math.Abs(notchMidX - minX);

                            // FIX THEO YÊU CẦU:
                            // DIM chiều sâu rãnh mép trên cũng đưa chân DIM ra mép ngoài cùng của dầm.
                            Point depthOuter;
                            Point depthInner;

                            if (!TryGetTopNotchDepthSegment(
                                polygon,
                                useRightSideForDepth,
                                x1,
                                x2,
                                y1,
                                maxY,
                                edgeTol,
                                out depthOuter,
                                out depthInner))
                            {
                                double outerBeamX = useRightSideForDepth ? maxX : minX;
                                depthOuter = new Point(outerBeamX, maxY, 0);
                                depthInner = new Point(outerBeamX, y1, 0);
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, 1, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                width))
                                count++;

                            influence.Top = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP TRÁI
                // =========================
                List<Point> leftInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.X > minX + edgeTol &&
                        p.X <= minX + NOTCH_MAX_SIZE &&
                        p.Y > minY + edgeTol &&
                        p.Y < maxY - edgeTol)
                    {
                        leftInner.Add(Clone2D(p));
                    }
                }

                if (leftInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(leftInner, out x1, out x2, out y1, out y2);

                    double depth = Math.Abs(x2 - minX);
                    double height = Math.Abs(y2 - y1);

                    if (height >= minWidth && depth >= minDepth &&
                        height <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        Point outerBottom = FindEdgePointNearestY(polygon, y1, minX, true, edgeTol);
                        Point outerTop = FindEdgePointNearestY(polygon, y2, minX, true, edgeTol);
                        Point innerBottom = FindExtremePointOnVerticalBand(leftInner, x2, true, edgeTol, minX, maxX, minY, maxY);
                        Point innerTop = FindExtremePointOnVerticalBand(leftInner, x2, false, edgeTol, minX, maxX, minY, maxY);

                        if (innerBottom == null) innerBottom = FindNearestPoint(leftInner, x2, y1);
                        if (innerTop == null) innerTop = FindNearestPoint(leftInner, x2, y2);

                        if (outerBottom != null && outerTop != null && innerBottom != null && innerTop != null)
                        {
                            double notchMidY = (y1 + y2) / 2.0;
                            bool useTopSideForDepth = Math.Abs(maxY - notchMidY) < Math.Abs(notchMidY - minY);

                            // FIX THEO YÊU CẦU:
                            // Rãnh mở ở mép trái: đưa chân DIM chiều sâu ra mép ngoài cùng theo Y.
                            Point depthOuter;
                            Point depthInner;

                            if (!TryGetLeftNotchDepthSegment(
                                polygon,
                                useTopSideForDepth,
                                x2,
                                y1,
                                y2,
                                minX,
                                edgeTol,
                                out depthOuter,
                                out depthInner))
                            {
                                double outerBeamY = useTopSideForDepth ? maxY : minY;
                                depthOuter = new Point(minX, outerBeamY, 0);
                                depthInner = new Point(x2, outerBeamY, 0);
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(-1, 0, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                height))
                                count++;

                            influence.Left = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP PHẢI
                // =========================
                List<Point> rightInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.X < maxX - edgeTol &&
                        p.X >= maxX - NOTCH_MAX_SIZE &&
                        p.Y > minY + edgeTol &&
                        p.Y < maxY - edgeTol)
                    {
                        rightInner.Add(Clone2D(p));
                    }
                }

                if (rightInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(rightInner, out x1, out x2, out y1, out y2);

                    double depth = Math.Abs(maxX - x1);
                    double height = Math.Abs(y2 - y1);

                    if (height >= minWidth && depth >= minDepth &&
                        height <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        Point outerBottom = FindEdgePointNearestY(polygon, y1, maxX, true, edgeTol);
                        Point outerTop = FindEdgePointNearestY(polygon, y2, maxX, true, edgeTol);
                        Point innerBottom = FindExtremePointOnVerticalBand(rightInner, x1, true, edgeTol, minX, maxX, minY, maxY);
                        Point innerTop = FindExtremePointOnVerticalBand(rightInner, x1, false, edgeTol, minX, maxX, minY, maxY);

                        if (innerBottom == null) innerBottom = FindNearestPoint(rightInner, x1, y1);
                        if (innerTop == null) innerTop = FindNearestPoint(rightInner, x1, y2);

                        if (outerBottom != null && outerTop != null && innerBottom != null && innerTop != null)
                        {
                            double notchMidY = (y1 + y2) / 2.0;
                            bool useTopSideForDepth = Math.Abs(maxY - notchMidY) < Math.Abs(notchMidY - minY);

                            // FIX THEO YÊU CẦU:
                            // Rãnh mở ở mép phải: đưa chân DIM chiều sâu ra mép ngoài cùng theo Y.
                            double outerBeamY = useTopSideForDepth ? maxY : minY;
                            Point depthOuter = new Point(maxX, outerBeamY, 0);
                            Point depthInner = new Point(x1, outerBeamY, 0);

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(1, 0, 0),
                                CHAMFER_DIM_EXTRA_OFFSET,
                                offsetAnchors,
                                height))
                                count++;

                            influence.Right = true;
                            influence.Any = true;
                        }
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateFrontAxisAlignedNotchDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            List<Point> projectedFootPoints,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength,
            out ChamferInfluence influence)
        {
            // FRONT VIEW - RÃNH / NOTCH - theo tầng 0
            // Không dựa vào bounding box ảo và không cần điểm rãnh trùng mép dầm nguyên vẹn.
            // Thuật toán:
            // 1. Lấy các điểm polygon thật nằm lõm vào gần từng mép ngoài.
            // 2. Nếu có ít nhất 2 điểm lõm tạo thành bề rộng + chiều sâu hợp lý => xem là rãnh.
            // 3. DIM ngang + dọc rãnh mặt Front đặt ở tầng 0 theo neo A/B/C/D của DIM tổng.
            // 4. Chỉ trả influence cạnh bị rãnh để dim tổng liên quan tự đẩy tầng.
            influence = new ChamferInfluence();
            int count = 0;

            try
            {
                if (polygon == null || polygon.Count < 4)
                    return count;

                // Chỉ dùng cho chân DIM rãnh Front.
                // Nếu điểm chiếu theo view không đủ thì fallback về polygon cũ.
                List<Point> footPolygon =
                    (projectedFootPoints != null && projectedFootPoints.Count >= 2)
                    ? projectedFootPoints
                    : polygon;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double minWidth = NOTCH_MIN_SIZE;
                double minDepth = NOTCH_MIN_SIZE;

                // =========================
                // RÃNH MỞ Ở MÉP DƯỚI
                // =========================
                List<Point> bottomInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.Y > minY + edgeTol &&
                        p.Y <= minY + NOTCH_MAX_SIZE &&
                        p.X > minX + edgeTol &&
                        p.X < maxX - edgeTol)
                    {
                        bottomInner.Add(Clone2D(p));
                    }
                }

                if (bottomInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(bottomInner, out x1, out x2, out y1, out y2);

                    double width = Math.Abs(x2 - x1);
                    double depth = Math.Abs(y2 - minY);

                    if (width >= minWidth && depth >= minDepth &&
                        width <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        // DIM DỌC RÃNH FRONT - BẮT BUỘC RIÊNG, KHÔNG PHỤ THUỘC DIM RÃNH CŨ.
                        // FIX CHÂN DIM: không dựng điểm bằng tọa độ giả (dimX, minY/y2) nữa,
                        // vì có thể làm chân DIM nằm ngoài không trung.
                        // Chỉ lấy điểm thật trên polygon/điểm chiếu trực diện; nếu không tìm được thì bỏ qua DIM phụ này.
                        {
                            double notchMidX2 = (x1 + x2) / 2.0;
                            bool useRightSideForDepth2 = Math.Abs(maxX - notchMidX2) < Math.Abs(notchMidX2 - minX);
                            double dimX = useRightSideForDepth2 ? x2 : x1;

                            Point dimOuter = FindEdgePointNearestX(footPolygon, dimX, minY, false, edgeTol);
                            Point dimInner = FindProjectedPointNearestXY(footPolygon, dimX, y2, edgeTol + 2.0);
                            if (dimInner == null)
                                dimInner = FindNearestPoint(bottomInner, dimX, y2);

                            if (dimOuter != null && dimInner != null &&
                                CreateEdgeAnchoredDim(
                                    handler,
                                    view,
                                    Clone2D(dimOuter),
                                    Clone2D(dimInner),
                                    useRightSideForDepth2 ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                    GetSteelDimOffsetByTier(1),
                                    offsetAnchors))
                                count++;
                        }

                        Point outerLeft = FindEdgePointNearestX(footPolygon, x1, minY, true, edgeTol);
                        Point outerRight = FindEdgePointNearestX(footPolygon, x2, minY, true, edgeTol);
                        Point innerLeft = FindProjectedPointNearestXY(footPolygon, x1, y2, NOTCH_MAX_SIZE);
                        Point innerRight = FindProjectedPointNearestXY(footPolygon, x2, y2, NOTCH_MAX_SIZE);

                        if (innerLeft == null) innerLeft = FindNearestPoint(bottomInner, x1, y2);
                        if (innerRight == null) innerRight = FindNearestPoint(bottomInner, x2, y2);

                        if (outerLeft != null && outerRight != null && innerLeft != null && innerRight != null)
                        {
                            // DIM dọc chiều sâu rãnh:
                            // Chọn phía gần MÉP NGOÀI CÙNG của thanh hơn.
                            // Tránh luôn lấy phía trái rồi bắt nhầm vào điểm fillet ở trong rãnh.
                            double notchMidX = (x1 + x2) / 2.0;
                            bool useRightSideForDepth = Math.Abs(maxX - notchMidX) < Math.Abs(notchMidX - minX);

                            // FIX THEO YÊU CẦU:
                            // DIM chiều sâu rãnh không bắt ở endpoint bên trong/fillet nữa.
                            // Chân DIM được đưa ra hẳn mép ngoài cùng của dầm:
                            // - rãnh gần đầu phải -> dùng X = maxX
                            // - rãnh gần đầu trái  -> dùng X = minX
                            double outerBeamX = useRightSideForDepth ? maxX : minX;
                            Point depthOuter = new Point(outerBeamX, minY, 0);
                            Point depthInner = new Point(outerBeamX, y2, 0);

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                depth))
                                count++;

                            // DIM ngang bề rộng rãnh.
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, -1, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                width))
                                count++;

                            influence.Bottom = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP TRÊN
                // =========================
                List<Point> topInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.Y < maxY - edgeTol &&
                        p.Y >= maxY - NOTCH_MAX_SIZE &&
                        p.X > minX + edgeTol &&
                        p.X < maxX - edgeTol)
                    {
                        topInner.Add(Clone2D(p));
                    }
                }

                if (topInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(topInner, out x1, out x2, out y1, out y2);

                    double width = Math.Abs(x2 - x1);
                    double depth = Math.Abs(maxY - y1);

                    if (width >= minWidth && depth >= minDepth &&
                        width <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        // DIM DỌC RÃNH FRONT - BẮT BUỘC RIÊNG, KHÔNG PHỤ THUỘC DIM RÃNH CŨ.
                        // FIX CHÂN DIM: chỉ lấy điểm thật trên polygon/điểm chiếu trực diện.
                        {
                            double notchMidX2 = (x1 + x2) / 2.0;
                            bool useRightSideForDepth2 = Math.Abs(maxX - notchMidX2) < Math.Abs(notchMidX2 - minX);
                            double dimX = useRightSideForDepth2 ? x2 : x1;

                            Point dimOuter = FindEdgePointNearestX(footPolygon, dimX, maxY, false, edgeTol);
                            Point dimInner = FindProjectedPointNearestXY(footPolygon, dimX, y1, edgeTol + 2.0);
                            if (dimInner == null)
                                dimInner = FindNearestPoint(topInner, dimX, y1);

                            if (dimOuter != null && dimInner != null &&
                                CreateEdgeAnchoredDim(
                                    handler,
                                    view,
                                    Clone2D(dimOuter),
                                    Clone2D(dimInner),
                                    useRightSideForDepth2 ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                    GetSteelDimOffsetByTier(1),
                                    offsetAnchors))
                                count++;
                        }

                        Point outerLeft = FindEdgePointNearestX(footPolygon, x1, maxY, true, edgeTol);
                        Point outerRight = FindEdgePointNearestX(footPolygon, x2, maxY, true, edgeTol);
                        Point innerLeft = FindProjectedPointNearestXY(footPolygon, x1, y1, NOTCH_MAX_SIZE);
                        Point innerRight = FindProjectedPointNearestXY(footPolygon, x2, y1, NOTCH_MAX_SIZE);

                        if (innerLeft == null) innerLeft = FindNearestPoint(topInner, x1, y1);
                        if (innerRight == null) innerRight = FindNearestPoint(topInner, x2, y1);

                        if (outerLeft != null && outerRight != null && innerLeft != null && innerRight != null)
                        {
                            double notchMidX = (x1 + x2) / 2.0;
                            bool useRightSideForDepth = Math.Abs(maxX - notchMidX) < Math.Abs(notchMidX - minX);

                            // FIX THEO YÊU CẦU:
                            // DIM chiều sâu rãnh mép trên cũng đưa chân DIM ra mép ngoài cùng của dầm.
                            double outerBeamX = useRightSideForDepth ? maxX : minX;
                            Point depthOuter = new Point(outerBeamX, maxY, 0);
                            Point depthInner = new Point(outerBeamX, y1, 0);

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, 1, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                width))
                                count++;

                            influence.Top = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP TRÁI
                // =========================
                List<Point> leftInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.X > minX + edgeTol &&
                        p.X <= minX + NOTCH_MAX_SIZE &&
                        p.Y > minY + edgeTol &&
                        p.Y < maxY - edgeTol)
                    {
                        leftInner.Add(Clone2D(p));
                    }
                }

                if (leftInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(leftInner, out x1, out x2, out y1, out y2);

                    double depth = Math.Abs(x2 - minX);
                    double height = Math.Abs(y2 - y1);

                    if (height >= minWidth && depth >= minDepth &&
                        height <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        // DIM DỌC RÃNH FRONT - FORCED MIN/MAX TRÊN THÀNH ĐỨNG RÃNH.
                        // Ưu tiên lấy min/max từ polyline chiếu theo view.
                        // Nếu không tìm được do polyline/fillet bị nhiễu, vẫn tạo DIM bằng chính box rãnh đang xét:
                        // rãnh mép trái dùng thành trong X = x2, Y = y1 -> y2.
                        // Chỉ thêm DIM dọc rãnh, không đụng DIM khác.
                        {
                            Point dimBottom;
                            Point dimTop;

                            bool gotMinMax = TryGetProjectedFrontNotchVerticalMinMax(
                                footPolygon,
                                false,
                                x1,
                                x2,
                                y1,
                                y2,
                                minX,
                                maxX,
                                minY,
                                maxY,
                                out dimBottom,
                                out dimTop);

                            if (!gotMinMax || dimBottom == null || dimTop == null)
                            {
                                dimBottom = new Point(x2, y1, 0);
                                dimTop = new Point(x2, y2, 0);
                            }

                            if (CreateEdgeAnchoredDim(
                                handler,
                                view,
                                Clone2D(dimBottom),
                                Clone2D(dimTop),
                                new Vector(-1, 0, 0),
                                GetSteelDimOffsetByTier(1),
                                offsetAnchors))
                                count++;
                        }

                        Point outerBottom = FindEdgePointNearestY(footPolygon, y1, minX, true, edgeTol);
                        Point outerTop = FindEdgePointNearestY(footPolygon, y2, minX, true, edgeTol);
                        Point innerBottom;
                        Point innerTop;
                        if (!TryGetProjectedFrontNotchVerticalMinMax(
                            footPolygon,
                            false,
                            x1,
                            x2,
                            y1,
                            y2,
                            minX,
                            maxX,
                            minY,
                            maxY,
                            out innerBottom,
                            out innerTop))
                        {
                            if (!TryGetInnerVerticalNotchMinMax(leftInner, false, out innerBottom, out innerTop))
                            {
                                innerBottom = FindProjectedPointNearestXY(footPolygon, x2, y1, NOTCH_MAX_SIZE);
                                innerTop = FindProjectedPointNearestXY(footPolygon, x2, y2, NOTCH_MAX_SIZE);

                                if (innerBottom == null) innerBottom = FindNearestPoint(leftInner, x2, y1);
                                if (innerTop == null) innerTop = FindNearestPoint(leftInner, x2, y2);
                            }
                        }

                        if (outerBottom != null && outerTop != null && innerBottom != null && innerTop != null)
                        {
                            double notchMidY = (y1 + y2) / 2.0;
                            bool useTopSideForDepth = Math.Abs(maxY - notchMidY) < Math.Abs(notchMidY - minY);

                            // FIX THEO YÊU CẦU:
                            // Rãnh mở ở mép trái: đưa chân DIM chiều sâu ra mép ngoài cùng theo Y.
                            double outerBeamY = useTopSideForDepth ? maxY : minY;
                            Point depthOuter = new Point(minX, outerBeamY, 0);
                            Point depthInner = new Point(x2, outerBeamY, 0);

                            // FRONT NOTCH - DIM NGANG RÃNH: giữ điểm MIN đang đúng,
                            // chỉ đổi điểm MAX theo neo DIM tổng/line ngoài cùng giống DIM dọc.
                            // Rãnh mép trái: MAX theo hướng ngoài là điểm trên line X = minX,
                            // cùng Y với điểm MIN hiện tại. Không lấy midpoint/fillet.
                            Point horizontalMaxPointLeft;
                            if (TryFindFrontNotchHorizontalMaxFromOuterAnchorLine(
                                footPolygon,
                                false,
                                depthInner,
                                useTopSideForDepth,
                                y1,
                                y2,
                                minX,
                                maxX,
                                minY,
                                maxY,
                                out horizontalMaxPointLeft))
                            {
                                depthOuter = Clone2D(horizontalMaxPointLeft);
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(-1, 0, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                height))
                                count++;

                            influence.Left = true;
                            influence.Any = true;
                        }
                    }
                }

                // =========================
                // RÃNH MỞ Ở MÉP PHẢI
                // =========================
                List<Point> rightInner = new List<Point>();
                foreach (Point p in polygon)
                {
                    if (p == null) continue;

                    if (p.X < maxX - edgeTol &&
                        p.X >= maxX - NOTCH_MAX_SIZE &&
                        p.Y > minY + edgeTol &&
                        p.Y < maxY - edgeTol)
                    {
                        rightInner.Add(Clone2D(p));
                    }
                }

                if (rightInner.Count >= 2)
                {
                    double x1, x2, y1, y2;
                    GetMinMax(rightInner, out x1, out x2, out y1, out y2);

                    double depth = Math.Abs(maxX - x1);
                    double height = Math.Abs(y2 - y1);

                    if (height >= minWidth && depth >= minDepth &&
                        height <= NOTCH_MAX_SIZE && depth <= NOTCH_MAX_SIZE)
                    {
                        // DIM DỌC RÃNH FRONT - FORCED MIN/MAX TRÊN THÀNH ĐỨNG RÃNH.
                        // Ưu tiên lấy min/max từ polyline chiếu theo view.
                        // Nếu không tìm được do polyline/fillet bị nhiễu, vẫn tạo DIM bằng chính box rãnh đang xét:
                        // rãnh mép phải dùng thành trong X = x1, Y = y1 -> y2.
                        // Chỉ thêm DIM dọc rãnh, không đụng DIM khác.
                        {
                            Point dimBottom;
                            Point dimTop;

                            bool gotMinMax = TryGetProjectedFrontNotchVerticalMinMax(
                                footPolygon,
                                true,
                                x1,
                                x2,
                                y1,
                                y2,
                                minX,
                                maxX,
                                minY,
                                maxY,
                                out dimBottom,
                                out dimTop);

                            if (!gotMinMax || dimBottom == null || dimTop == null)
                            {
                                dimBottom = new Point(x1, y1, 0);
                                dimTop = new Point(x1, y2, 0);
                            }

                            if (CreateEdgeAnchoredDim(
                                handler,
                                view,
                                Clone2D(dimBottom),
                                Clone2D(dimTop),
                                new Vector(1, 0, 0),
                                GetSteelDimOffsetByTier(1),
                                offsetAnchors))
                                count++;
                        }

                        Point outerBottom = FindEdgePointNearestY(footPolygon, y1, maxX, true, edgeTol);
                        Point outerTop = FindEdgePointNearestY(footPolygon, y2, maxX, true, edgeTol);
                        Point innerBottom;
                        Point innerTop;
                        if (!TryGetProjectedFrontNotchVerticalMinMax(
                            footPolygon,
                            true,
                            x1,
                            x2,
                            y1,
                            y2,
                            minX,
                            maxX,
                            minY,
                            maxY,
                            out innerBottom,
                            out innerTop))
                        {
                            if (!TryGetInnerVerticalNotchMinMax(rightInner, true, out innerBottom, out innerTop))
                            {
                                innerBottom = FindProjectedPointNearestXY(footPolygon, x1, y1, NOTCH_MAX_SIZE);
                                innerTop = FindProjectedPointNearestXY(footPolygon, x1, y2, NOTCH_MAX_SIZE);

                                if (innerBottom == null) innerBottom = FindNearestPoint(rightInner, x1, y1);
                                if (innerTop == null) innerTop = FindNearestPoint(rightInner, x1, y2);
                            }
                        }

                        if (outerBottom != null && outerTop != null && innerBottom != null && innerTop != null)
                        {
                            double notchMidY = (y1 + y2) / 2.0;
                            bool useTopSideForDepth = Math.Abs(maxY - notchMidY) < Math.Abs(notchMidY - minY);

                            // FRONT NOTCH - BẮT ĐÚNG CẶP CHÂN DIM TRÊN CÙNG 1 CẠNH RÃNH:
                            // Không ghép 2 điểm từ 2 thuật toán khác nhau nữa.
                            // Dò trực tiếp cạnh ngang thật của rãnh: một đầu ở mép ngoài, một đầu ở đáy rãnh.
                            Point depthOuter;
                            Point depthInner;

                            if (!TryGetRightNotchDepthSegment(
                                footPolygon,
                                useTopSideForDepth,
                                x1,
                                y1,
                                y2,
                                maxX,
                                edgeTol,
                                out depthOuter,
                                out depthInner))
                            {
                                double outerBeamY = useTopSideForDepth ? maxY : minY;
                                depthOuter = new Point(maxX, outerBeamY, 0);
                                depthInner = new Point(x1, outerBeamY, 0);
                            }

                            // FRONT NOTCH - DIM NGANG RÃNH: giữ điểm MIN đang đúng,
                            // chỉ đổi điểm MAX theo neo DIM tổng/line ngoài cùng giống DIM dọc.
                            // Rãnh mép phải: MAX theo hướng ngoài là điểm trên line X = maxX,
                            // cùng Y với điểm MIN hiện tại. Không lấy midpoint/fillet.
                            Point horizontalMaxPointRight;
                            if (TryFindFrontNotchHorizontalMaxFromOuterAnchorLine(
                                footPolygon,
                                true,
                                depthInner,
                                useTopSideForDepth,
                                y1,
                                y2,
                                minX,
                                maxX,
                                minY,
                                maxY,
                                out horizontalMaxPointRight))
                            {
                                depthOuter = Clone2D(horizontalMaxPointRight);
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                depth))
                                count++;

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(1, 0, 0),
                                GetSteelDimOffsetByTier(0),
                                offsetAnchors,
                                height))
                                count++;

                            influence.Right = true;
                            influence.Any = true;
                        }
                    }
                }
            }
            catch
            {
            }

            return count;
        }



        private static bool TryGetBottomNotchDepthSegment(
            List<Point> pts,
            bool useRightSide,
            double x1,
            double x2,
            double innerY,
            double outerY,
            double tol,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;
            double targetX = useRightSide ? x2 : x1;
            return TryGetVerticalNotchDepthSegment(pts, targetX, innerY, outerY, tol, true, out depthOuter, out depthInner);
        }

        private static bool TryGetTopNotchDepthSegment(
            List<Point> pts,
            bool useRightSide,
            double x1,
            double x2,
            double innerY,
            double outerY,
            double tol,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;
            double targetX = useRightSide ? x2 : x1;
            return TryGetVerticalNotchDepthSegment(pts, targetX, innerY, outerY, tol, false, out depthOuter, out depthInner);
        }

        private static bool TryGetLeftNotchDepthSegment(
            List<Point> pts,
            bool useTopSide,
            double innerX,
            double y1,
            double y2,
            double outerX,
            double tol,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;
            double targetY = useTopSide ? y2 : y1;
            return TryGetHorizontalNotchDepthSegment(pts, targetY, innerX, outerX, tol, true, out depthOuter, out depthInner);
        }

        private static bool TryGetRightNotchDepthSegment(
            List<Point> pts,
            bool useTopSide,
            double innerX,
            double y1,
            double y2,
            double outerX,
            double tol,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;
            double targetY = useTopSide ? y2 : y1;
            return TryGetHorizontalNotchDepthSegment(pts, targetY, innerX, outerX, tol, false, out depthOuter, out depthInner);
        }

        private static bool TryGetVerticalNotchDepthSegment(
            List<Point> pts,
            double targetX,
            double innerY,
            double outerY,
            double tol,
            bool outerIsLower,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;

            try
            {
                if (pts == null || pts.Count < 2)
                    return false;

                List<Point> sorted = SortPolygonPointsClockwise(pts);
                if (sorted == null || sorted.Count < 2)
                    return false;

                double bandTol = Math.Max(2.0, tol);
                double bestScore = 999999999.0;
                Point bestOuter = null;
                Point bestInner = null;

                for (int i = 0; i < sorted.Count; i++)
                {
                    Point a = sorted[i];
                    Point b = sorted[(i + 1) % sorted.Count];
                    if (a == null || b == null)
                        continue;

                    if (Math.Abs(a.X - b.X) > bandTol)
                        continue;

                    bool aOuter = Math.Abs(a.Y - outerY) <= bandTol;
                    bool bOuter = Math.Abs(b.Y - outerY) <= bandTol;
                    bool aInner = Math.Abs(a.Y - innerY) <= bandTol;
                    bool bInner = Math.Abs(b.Y - innerY) <= bandTol;

                    Point outer = null;
                    Point inner = null;

                    if (aOuter && bInner)
                    {
                        outer = a;
                        inner = b;
                    }
                    else if (bOuter && aInner)
                    {
                        outer = b;
                        inner = a;
                    }
                    else
                    {
                        continue;
                    }

                    if (outerIsLower && outer.Y > inner.Y + bandTol)
                        continue;
                    if (!outerIsLower && outer.Y < inner.Y - bandTol)
                        continue;

                    double score = Math.Abs(outer.X - targetX) + Math.Abs(inner.X - targetX);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestOuter = outer;
                        bestInner = inner;
                    }
                }

                if (bestOuter == null || bestInner == null)
                    return false;

                depthOuter = Clone2D(bestOuter);
                depthInner = Clone2D(bestInner);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetHorizontalNotchDepthSegment(
            List<Point> pts,
            double targetY,
            double innerX,
            double outerX,
            double tol,
            bool outerIsLeft,
            out Point depthOuter,
            out Point depthInner)
        {
            depthOuter = null;
            depthInner = null;

            try
            {
                if (pts == null || pts.Count < 2)
                    return false;

                List<Point> sorted = SortPolygonPointsClockwise(pts);
                if (sorted == null || sorted.Count < 2)
                    return false;

                double bandTol = Math.Max(2.0, tol);
                double bestScore = 999999999.0;
                Point bestOuter = null;
                Point bestInner = null;

                for (int i = 0; i < sorted.Count; i++)
                {
                    Point a = sorted[i];
                    Point b = sorted[(i + 1) % sorted.Count];
                    if (a == null || b == null)
                        continue;

                    if (Math.Abs(a.Y - b.Y) > bandTol)
                        continue;

                    bool aOuter = Math.Abs(a.X - outerX) <= bandTol;
                    bool bOuter = Math.Abs(b.X - outerX) <= bandTol;
                    bool aInner = Math.Abs(a.X - innerX) <= bandTol;
                    bool bInner = Math.Abs(b.X - innerX) <= bandTol;

                    Point outer = null;
                    Point inner = null;

                    if (aOuter && bInner)
                    {
                        outer = a;
                        inner = b;
                    }
                    else if (bOuter && aInner)
                    {
                        outer = b;
                        inner = a;
                    }
                    else
                    {
                        continue;
                    }

                    if (outerIsLeft && outer.X > inner.X + bandTol)
                        continue;
                    if (!outerIsLeft && outer.X < inner.X - bandTol)
                        continue;

                    double score = Math.Abs(outer.Y - targetY) + Math.Abs(inner.Y - targetY);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestOuter = outer;
                        bestInner = inner;
                    }
                }

                if (bestOuter == null || bestInner == null)
                    return false;

                depthOuter = Clone2D(bestOuter);
                depthInner = Clone2D(bestInner);
                return true;
            }
            catch
            {
                return false;
            }
        }



        private static List<Point> GetProjectedSolidPointsForTopDepth(
            Solid solid,
            double topZ,
            double visibleDepth)
        {
            // TOP VIEW - PROJECTED BOUNDARY CÓ GIỚI HẠN ĐỘ SÂU Z.
            // Khác với hàm Front đang lấy full solid, hàm này chỉ lấy điểm nằm gần mặt trên.
            // Mục tiêu: Top nhìn thẳng xuống nhưng chỉ thấy xuống khoảng TOP_PROJECTED_BOTTOM_EXCLUDE,
            // không bắt rãnh/cạnh ở đáy dưới rồi tạo DIM sai cho mặt trên.
            List<Point> result = new List<Point>();

            try
            {
                if (solid == null)
                    return result;

                double depth = visibleDepth;
                if (depth < 0.0)
                    depth = 0.0;

                double zMin = topZ - depth;
                double zMax = topZ + TOP_FLANGE_DEPTH_TOL;

                CollectRealSolidPointsForTopDepth(solid, result, 0, zMin, zMax);
            }
            catch
            {
            }

            return result;
        }

        private static void CollectRealSolidPointsForTopDepth(
            object obj,
            List<Point> result,
            int depth,
            double zMin,
            double zMax)
        {
            if (obj == null || result == null || depth > 8)
                return;

            Point directPoint = obj as Point;
            if (directPoint != null)
            {
                // Chỉ lấy điểm nằm trong vùng mặt trên được phép nhìn thấy.
                // Khi add vào list vẫn ép Z=0 vì DIM dùng tọa độ XY trên drawing.
                if (directPoint.Z >= zMin && directPoint.Z <= zMax)
                    AddUniquePoint(result, new Point(directPoint.X, directPoint.Y, 0), 0.5);
                return;
            }

            TryCollectTopDepthFromEnumeratorMethod(obj, result, depth, "GetFaceEnumerator", zMin, zMax);
            TryCollectTopDepthFromEnumeratorMethod(obj, result, depth, "GetLoopEnumerator", zMin, zMax);
            TryCollectTopDepthFromEnumeratorMethod(obj, result, depth, "GetVertexEnumerator", zMin, zMax);
            TryCollectTopDepthFromEnumeratorMethod(obj, result, depth, "GetEdgeEnumerator", zMin, zMax);
            TryCollectTopDepthFromEnumeratorMethod(obj, result, depth, "GetPointEnumerator", zMin, zMax);

            TryCollectTopDepthPointProperty(obj, result, "Point", zMin, zMax);
            TryCollectTopDepthPointProperty(obj, result, "Position", zMin, zMax);
            TryCollectTopDepthPointProperty(obj, result, "StartPoint", zMin, zMax);
            TryCollectTopDepthPointProperty(obj, result, "EndPoint", zMin, zMax);

            IEnumerable enumerable = obj as IEnumerable;
            if (enumerable != null && !(obj is string))
            {
                foreach (object item in enumerable)
                    CollectRealSolidPointsForTopDepth(item, result, depth + 1, zMin, zMax);
            }
        }

        private static void TryCollectTopDepthFromEnumeratorMethod(
            object obj,
            List<Point> result,
            int depth,
            string methodName,
            double zMin,
            double zMax)
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

                object enumeratorObj = method.Invoke(obj, null);
                IEnumerator enumerator = enumeratorObj as IEnumerator;
                if (enumerator == null)
                    return;

                while (enumerator.MoveNext())
                    CollectRealSolidPointsForTopDepth(enumerator.Current, result, depth + 1, zMin, zMax);
            }
            catch
            {
            }
        }

        private static void TryCollectTopDepthPointProperty(
            object obj,
            List<Point> result,
            string propertyName,
            double zMin,
            double zMax)
        {
            try
            {
                if (obj == null || result == null)
                    return;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return;

                object value = prop.GetValue(obj, null);
                Point p = value as Point;
                if (p == null)
                    return;

                if (p.Z >= zMin && p.Z <= zMax)
                    AddUniquePoint(result, new Point(p.X, p.Y, 0), 0.5);
            }
            catch
            {
            }
        }

        private static List<Point> GetProjectedSolidPointsForFrontNotchDims(Solid solid)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (solid == null)
                    return result;

                // Solid đã được lấy sau khi SetCurrentTransformationPlane(view.DisplayCoordinateSystem),
                // nên điểm thu được đã nằm trong hệ tọa độ chiếu trực diện của view.
                // Đây là cách lấy nguồn điểm giống Plate V20/V21: lấy điểm thật của solid,
                // không lấy MinimumPoint/MaximumPoint bounding box.
                CollectRealSolidPointsForFrontNotchDims(solid, result, 0);
            }
            catch
            {
            }

            return result;
        }

        private static void CollectRealSolidPointsForFrontNotchDims(
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

            TryCollectFrontNotchFromEnumeratorMethod(obj, result, depth, "GetFaceEnumerator");
            TryCollectFrontNotchFromEnumeratorMethod(obj, result, depth, "GetLoopEnumerator");
            TryCollectFrontNotchFromEnumeratorMethod(obj, result, depth, "GetVertexEnumerator");
            TryCollectFrontNotchFromEnumeratorMethod(obj, result, depth, "GetEdgeEnumerator");
            TryCollectFrontNotchFromEnumeratorMethod(obj, result, depth, "GetPointEnumerator");

            TryCollectFrontNotchPointProperty(obj, result, "Point");
            TryCollectFrontNotchPointProperty(obj, result, "Position");
            TryCollectFrontNotchPointProperty(obj, result, "StartPoint");
            TryCollectFrontNotchPointProperty(obj, result, "EndPoint");

            IEnumerable enumerable = obj as IEnumerable;
            if (enumerable != null && !(obj is string))
            {
                foreach (object item in enumerable)
                    CollectRealSolidPointsForFrontNotchDims(item, result, depth + 1);
            }
        }

        private static void TryCollectFrontNotchFromEnumeratorMethod(
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
                    CollectRealSolidPointsForFrontNotchDims(current, result, depth + 1);
                }
            }
            catch
            {
            }
        }

        private static void TryCollectFrontNotchPointProperty(
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

        private static Point FindProjectedPointNearestXY(
            List<Point> pts,
            double targetX,
            double targetY,
            double maxDistance)
        {
            try
            {
                if (pts == null || pts.Count == 0)
                    return null;

                Point best = null;
                double bestDist = 999999999.0;
                double maxDist2 = maxDistance > 0.0 ? maxDistance * maxDistance : 999999999.0;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    double dx = p.X - targetX;
                    double dy = p.Y - targetY;
                    double d = dx * dx + dy * dy;

                    if (d > maxDist2)
                        continue;

                    if (best == null || d < bestDist)
                    {
                        best = p;
                        bestDist = d;
                    }
                }

                return best == null ? null : Clone2D(best);
            }
            catch
            {
                return null;
            }
        }



        private static bool TryGetProjectedFrontNotchLongestVerticalSegmentNoFillet(
            List<Point> projectedPoints,
            bool rightSide,
            double x1,
            double x2,
            double y1,
            double y2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point bottomPoint,
            out Point topPoint)
        {
            bottomPoint = null;
            topPoint = null;

            try
            {
                if (projectedPoints == null || projectedPoints.Count < 2)
                    return false;

                double targetInnerX = rightSide ? x1 : x2;
                double yLow = Math.Min(y1, y2);
                double yHigh = Math.Max(y1, y2);

                double xTol = Math.Max(2.0, TOL + 1.0);
                double yTol = Math.Max(3.0, TOL + 2.0);
                double lowerSearch = Math.Max(30.0, NOTCH_MIN_DIM_TO_CREATE + 5.0);
                double minStraightLength = Math.Max(20.0, NOTCH_MIN_DIM_TO_CREATE);

                // Sắp xếp thành polyline bao ngoài để xét từng đoạn kề nhau.
                // Điểm fillet thường tạo nhiều đoạn ngắn -> bị loại bởi minStraightLength.
                List<Point> pts = SortPolygonPointsClockwise(projectedPoints);
                if (pts == null || pts.Count < 2)
                    return false;

                Point bestA = null;
                Point bestB = null;
                double bestXScore = 999999999.0;
                double bestLength = -1.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    // Chỉ nhận đoạn đứng thật.
                    if (Math.Abs(a.X - b.X) > xTol)
                        continue;

                    double length = Math.Abs(a.Y - b.Y);
                    if (length < minStraightLength)
                        continue;

                    double segX = (a.X + b.X) / 2.0;
                    double segMinY = Math.Min(a.Y, b.Y);
                    double segMaxY = Math.Max(a.Y, b.Y);

                    // Khóa đúng vùng rãnh đang xét, không lấy cạnh/góc khác.
                    if (segX < Math.Min(x1, x2) - xTol || segX > Math.Max(x1, x2) + xTol)
                        continue;

                    if (segMaxY < yLow - lowerSearch || segMinY > yHigh + yTol)
                        continue;

                    // Đoạn phải giao với khoảng chiều cao rãnh.
                    if (segMaxY < yLow - yTol || segMinY > yHigh + yTol)
                        continue;

                    double xScore = Math.Abs(segX - targetInnerX);

                    // Ưu tiên đoạn đứng gần thành rãnh nhất; nếu bằng nhau lấy đoạn dài hơn.
                    if (xScore < bestXScore - TOL ||
                        (Math.Abs(xScore - bestXScore) <= TOL && length > bestLength + TOL))
                    {
                        bestXScore = xScore;
                        bestLength = length;
                        bestA = a;
                        bestB = b;
                    }
                }

                if (bestA == null || bestB == null)
                    return false;

                if (bestA.Y <= bestB.Y)
                {
                    bottomPoint = Clone2D(bestA);
                    topPoint = Clone2D(bestB);
                }
                else
                {
                    bottomPoint = Clone2D(bestB);
                    topPoint = Clone2D(bestA);
                }

                return true;
            }
            catch
            {
                bottomPoint = null;
                topPoint = null;
                return false;
            }
        }

        private static bool TryGetProjectedFrontNotchVerticalMinMax(
            List<Point> projectedPoints,
            bool rightSide,
            double x1,
            double x2,
            double y1,
            double y2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point bottomPoint,
            out Point topPoint)
        {
            bottomPoint = null;
            topPoint = null;

            try
            {
                if (projectedPoints == null || projectedPoints.Count < 2)
                    return false;

                // Rãnh mép trái: thành trong nằm gần x2.
                // Rãnh mép phải: thành trong nằm gần x1.
                double targetInnerX = rightSide ? x1 : x2;
                double yLow = Math.Min(y1, y2);
                double yHigh = Math.Max(y1, y2);

                double searchX = Math.Max(4.0, TOL + 3.0);
                double searchY = Math.Max(4.0, TOL + 3.0);

                // FRONT NOTCH - FIX CHÂN DƯỚI:
                // Với rãnh mặt Front, polygon/fillet đôi khi làm y1 nằm trên đường mép dày phía trên,
                // trong khi chân DIM đúng phải nằm ở mép thực ngay bên dưới.
                // Mở thêm vùng tìm theo phía dưới một đoạn nhỏ để bắt được mép thực đó,
                // nhưng vẫn khóa theo X thành rãnh để không nhảy sang rãnh/cạnh khác.
                double lowerRealEdgeSearch = Math.Max(30.0, NOTCH_MIN_DIM_TO_CREATE + 5.0);

                double minSpan = Math.Max(NOTCH_MIN_DIM_TO_CREATE, NOTCH_MIN_SIZE);

                // FRONT NOTCH - NO FILLET SNAP:
                // Ưu tiên bắt 2 đầu của ĐOẠN ĐỨNG THẬT đủ dài trong polyline chiếu.
                // Không lấy vertex rời/điểm gấp khúc nhỏ do fillet sinh ra.
                Point segBottom;
                Point segTop;
                if (TryGetProjectedFrontNotchLongestVerticalSegmentNoFillet(
                    projectedPoints,
                    rightSide,
                    x1,
                    x2,
                    y1,
                    y2,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    out segBottom,
                    out segTop))
                {
                    bottomPoint = Clone2D(segBottom);
                    topPoint = Clone2D(segTop);
                    return true;
                }

                List<Point> candidates = new List<Point>();

                foreach (Point p in projectedPoints)
                {
                    if (p == null)
                        continue;

                    // Khóa đúng vùng rãnh đang xét, không cho nhảy qua rãnh/góc khác.
                    if (p.Y < yLow - lowerRealEdgeSearch || p.Y > yHigh + searchY)
                        continue;

                    if (Math.Abs(p.X - targetInnerX) <= searchX)
                        candidates.Add(Clone2D(p));
                }

                // Nếu projected solid không có đúng X target, mở nhẹ vùng quanh hộp rãnh,
                // nhưng vẫn chỉ xét bên trong notch box để tránh bắt fillet/góc khác.
                if (candidates.Count < 2)
                {
                    double boxMinX = Math.Min(x1, x2) - searchX;
                    double boxMaxX = Math.Max(x1, x2) + searchX;

                    foreach (Point p in projectedPoints)
                    {
                        if (p == null)
                            continue;

                        if (p.X < boxMinX || p.X > boxMaxX)
                            continue;

                        if (p.Y < yLow - lowerRealEdgeSearch || p.Y > yHigh + searchY)
                            continue;

                        candidates.Add(Clone2D(p));
                    }
                }

                if (candidates.Count < 2)
                    return false;

                // FRONT NOTCH - ENDPOINT PRIORITY:
                // Không chọn điểm nằm giữa/vertex nhỏ trên fillet nữa.
                // Tạo các cặp ENDPOINT theo cùng X, ưu tiên cặp có khoảng Y lớn nhất
                // và nằm gần thành rãnh targetInnerX nhất. Các điểm giữa chỉ là nhiễu,
                // không được dùng làm chân DIM.
                Point bestBottom = null;
                Point bestTop = null;
                double bestSpan = -1.0;
                double bestXScore = 999999999.0;

                for (int i = 0; i < candidates.Count; i++)
                {
                    Point a = candidates[i];
                    if (a == null)
                        continue;

                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        Point b = candidates[j];
                        if (b == null)
                            continue;

                        // Hai chân DIM dọc phải gần cùng một trục X.
                        if (Math.Abs(a.X - b.X) > searchX)
                            continue;

                        double span = Math.Abs(a.Y - b.Y);
                        if (span < minSpan)
                            continue;

                        double pairX = (a.X + b.X) / 2.0;
                        double xScore = Math.Abs(pairX - targetInnerX);

                        // Ưu tiên đúng thành rãnh trước; nếu cùng thành thì lấy cặp endpoint xa nhất.
                        if (xScore < bestXScore - TOL ||
                            (Math.Abs(xScore - bestXScore) <= TOL && span > bestSpan + TOL))
                        {
                            bestXScore = xScore;
                            bestSpan = span;

                            if (a.Y <= b.Y)
                            {
                                bestBottom = a;
                                bestTop = b;
                            }
                            else
                            {
                                bestBottom = b;
                                bestTop = a;
                            }
                        }
                    }
                }

                if (bestBottom == null || bestTop == null)
                    return false;

                bottomPoint = Clone2D(bestBottom);
                topPoint = Clone2D(bestTop);

                // FRONT NOTCH - MAX TỪ NEO DIM TỔNG:
                // Điểm MIN hiện đã ổn. Điểm MAX không lấy từ chuỗi fillet nữa.
                // Dùng mép ngoài của dầm đang làm neo DIM tổng, quét xuống theo phương Y,
                // lấy giao điểm giữa line đứng ngoài cùng và line ngang của rãnh.
                Point anchorMaxPoint;
                if (TryFindFrontNotchMaxFromOuterAnchorLine(
                    projectedPoints,
                    rightSide,
                    x1,
                    x2,
                    y1,
                    y2,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    out anchorMaxPoint))
                {
                    topPoint = Clone2D(anchorMaxPoint);
                }

                return true;
            }
            catch
            {
                bottomPoint = null;
                topPoint = null;
                return false;
            }
        }

        private static bool TryFindFrontNotchHorizontalMaxFromOuterAnchorLine(
            List<Point> projectedPoints,
            bool rightSide,
            Point minPoint,
            bool useTopSideForDepth,
            double y1,
            double y2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point maxPoint)
        {
            // FRONT NOTCH - DIM NGANG RÃNH:
            // Giữ nguyên điểm MIN đang đúng. Chỉ thay toàn bộ cách tìm điểm MAX.
            // Logic mới theo yêu cầu:
            // 1. Lấy line đứng ngoài cùng thật đang neo DIM tổng: mép phải dùng X ngoài phải, mép trái dùng X ngoài trái.
            // 2. Nếu rãnh nằm phía dưới: từ neo trên quét xuống theo line đứng đó, điểm dừng đầu tiên ở cao độ bậc rãnh là MAX.
            // 3. Nếu rãnh nằm phía trên: quét ngược từ neo dưới lên theo line đứng đó, điểm dừng ở cao độ bậc rãnh là MAX.
            // 4. Không ép Y của MAX bằng Y điểm MIN nữa, vì đó là nguyên nhân chân MAX tụt xuống sai.
            maxPoint = null;

            try
            {
                if (minPoint == null)
                    return false;

                if (projectedPoints == null || projectedPoints.Count < 2)
                    return false;

                double xTol = Math.Max(3.0, TOL + 2.0);
                double yTol = Math.Max(6.0, TOL + 5.0);
                double edgeSearchTol = Math.Max(35.0, NOTCH_MAX_SIZE * 0.35);
                double minLineSpan = Math.Max(20.0, NOTCH_MIN_DIM_TO_CREATE);

                double expectedOuterX = rightSide ? maxX : minX;

                // Với rãnh dưới, MAX cần nằm ở đầu dưới của line đứng ngoài: thường là Y cao hơn của rãnh.
                // Với rãnh trên, MAX cần nằm ở đầu trên của line đứng ngoài: thường là Y thấp hơn của rãnh.
                double targetNotchY = useTopSideForDepth
                    ? Math.Min(y1, y2)
                    : Math.Max(y1, y2);

                double bestX = 0.0;
                double bestY = 0.0;
                double bestScore = 999999999.0;
                bool foundBest = false;

                // Gom các điểm có cùng X gần mép ngoài để nhận diện line đứng neo DIM tổng.
                foreach (Point seed in projectedPoints)
                {
                    if (seed == null)
                        continue;

                    if (Math.Abs(seed.X - expectedOuterX) > edgeSearchTol)
                        continue;

                    double candidateX = seed.X;
                    double lineMinY = 999999999.0;
                    double lineMaxY = -999999999.0;
                    int count = 0;

                    foreach (Point p in projectedPoints)
                    {
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - candidateX) > xTol)
                            continue;

                        if (p.Y < minY - yTol || p.Y > maxY + yTol)
                            continue;

                        count++;
                        if (p.Y < lineMinY) lineMinY = p.Y;
                        if (p.Y > lineMaxY) lineMaxY = p.Y;
                    }

                    if (count < 2)
                        continue;

                    double span = Math.Abs(lineMaxY - lineMinY);
                    if (span < minLineSpan)
                        continue;

                    double candidateY;
                    bool validAnchorLine;

                    if (!useTopSideForDepth)
                    {
                        // Rãnh phía dưới: line đứng ngoài phải neo lên mép trên, rồi quét xuống tới chân dưới của line.
                        candidateY = lineMinY;
                        validAnchorLine =
                            lineMaxY >= maxY - Math.Max(20.0, yTol * 2.0) &&
                            candidateY >= Math.Min(y1, y2) - Math.Max(25.0, yTol * 2.0) &&
                            candidateY <= Math.Max(y1, y2) + Math.Max(25.0, yTol * 2.0);
                    }
                    else
                    {
                        // Rãnh phía trên: line đứng ngoài phải neo xuống mép dưới, rồi quét lên tới chân trên của line.
                        candidateY = lineMaxY;
                        validAnchorLine =
                            lineMinY <= minY + Math.Max(20.0, yTol * 2.0) &&
                            candidateY >= Math.Min(y1, y2) - Math.Max(25.0, yTol * 2.0) &&
                            candidateY <= Math.Max(y1, y2) + Math.Max(25.0, yTol * 2.0);
                    }

                    if (!validAnchorLine)
                        continue;

                    // Ưu tiên line ngoài cùng thật, sau đó ưu tiên điểm dừng gần cao độ bậc rãnh.
                    double outsideScore = rightSide
                        ? Math.Abs(maxX - candidateX)
                        : Math.Abs(candidateX - minX);

                    double notchScore = Math.Abs(candidateY - targetNotchY);
                    double score = outsideScore * 0.25 + notchScore - span * 0.001;

                    if (!foundBest || score < bestScore)
                    {
                        foundBest = true;
                        bestScore = score;
                        bestX = candidateX;
                        bestY = candidateY;
                    }
                }

                if (!foundBest)
                    return false;

                // Quan trọng: KHÔNG kéo bestY về minPoint.Y.
                // MAX phải là điểm cuối line đứng ngoài khi quét từ neo tổng xuống/lên.
                maxPoint = new Point(bestX, bestY, 0);
                return true;
            }
            catch
            {
                maxPoint = null;
                return false;
            }
        }

        private static bool TryFindFrontNotchMaxFromOuterAnchorLine(
            List<Point> projectedPoints,
            bool rightSide,
            double x1,
            double x2,
            double y1,
            double y2,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out Point maxPoint)
        {
            // Lấy điểm MAX của DIM dọc rãnh Front bằng giao của:
            // - line đứng ngoài cùng của dầm: X = maxX hoặc minX
            // - line ngang của bậc rãnh: Y = y2 (điểm cao hơn của rãnh)
            // Điều kiện: phải có line đứng ngoài cùng thật đủ dài trong projected polyline.
            // Không dùng midpoint/vertex lẻ trên fillet.
            maxPoint = null;

            try
            {
                if (projectedPoints == null || projectedPoints.Count < 2)
                    return false;

                double outerX = rightSide ? maxX : minX;
                double notchTopY = Math.Max(y1, y2);
                double notchBottomY = Math.Min(y1, y2);

                double xTol = Math.Max(3.0, TOL + 2.0);
                double yTol = Math.Max(3.0, TOL + 2.0);
                double minLineSpan = Math.Max(20.0, NOTCH_MIN_DIM_TO_CREATE);

                // Xác nhận có đoạn/chuỗi điểm đứng thật ở mép ngoài từ phía trên đi xuống rãnh.
                double foundMinY = 999999999.0;
                double foundMaxY = -999999999.0;
                int foundCount = 0;

                foreach (Point p in projectedPoints)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.X - outerX) > xTol)
                        continue;

                    // Chỉ xét vùng từ bậc rãnh lên tới mép ngoài phía trên.
                    // Không xét xuống sâu dưới rãnh để tránh bắt các đường khác.
                    if (p.Y < notchTopY - yTol || p.Y > maxY + yTol)
                        continue;

                    foundCount++;
                    if (p.Y < foundMinY) foundMinY = p.Y;
                    if (p.Y > foundMaxY) foundMaxY = p.Y;
                }

                if (foundCount < 2)
                    return false;

                if (Math.Abs(foundMaxY - foundMinY) < minLineSpan)
                    return false;

                // Điểm cần DIM là endpoint dưới của line đứng ngoài cùng,
                // tức giao với line ngang của rãnh, không phải các điểm fillet.
                // Dùng tọa độ toán học của 2 line chính, không chọn vertex fillet.
                maxPoint = new Point(outerX, notchTopY, 0);
                return true;
            }
            catch
            {
                maxPoint = null;
                return false;
            }
        }

        private static bool TryGetInnerVerticalNotchMinMax(
            List<Point> innerPoints,
            bool takeMinX,
            out Point bottomPoint,
            out Point topPoint)
        {
            // FRONT NOTCH - MIN/MAX THÀNH ĐỨNG RÃNH - V2:
            // Chỉ sửa hàm chọn 2 chân DIM dọc của rãnh mép trái/phải.
            // Ưu tiên bắt đúng 2 đầu của ĐOẠN ĐỨNG THẬT dài nhất trong cụm rãnh.
            // Không lấy điểm fillet/gấp khúc nhỏ nếu không tạo thành đoạn đứng dài.
            bottomPoint = null;
            topPoint = null;

            try
            {
                if (innerPoints == null || innerPoints.Count < 2)
                    return false;

                double bandTol = Math.Max(2.0, TOL + 1.0);
                double minSpan = Math.Max(NOTCH_MIN_DIM_TO_CREATE, NOTCH_MIN_SIZE);

                // BƯỚC 1: Dò theo thứ tự polygon để tìm đoạn đứng thật.
                // Đây là điểm khác bản trước: không gom điểm rời rạc trước,
                // mà ưu tiên cặp điểm liên tiếp tạo thành cạnh đứng thật của rãnh.
                List<Point> ordered = SortPolygonPointsClockwise(innerPoints);

                Point bestA = null;
                Point bestB = null;
                double bestSpan = -1.0;
                double bestXScore = 999999999.0;

                if (ordered != null && ordered.Count >= 2)
                {
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        Point a = ordered[i];
                        Point b = ordered[(i + 1) % ordered.Count];

                        if (a == null || b == null)
                            continue;

                        // Cạnh đứng thật: 2 đầu gần cùng X.
                        if (Math.Abs(a.X - b.X) > bandTol)
                            continue;

                        double span = Math.Abs(a.Y - b.Y);
                        if (span < minSpan)
                            continue;

                        // Với rãnh bên phải: thành trong thường là X nhỏ nhất.
                        // Với rãnh bên trái : thành trong thường là X lớn nhất.
                        double xMid = (a.X + b.X) / 2.0;
                        double sideScore = 0.0;

                        foreach (Point p in innerPoints)
                        {
                            if (p == null)
                                continue;

                            if (takeMinX)
                                sideScore = Math.Max(sideScore, xMid - p.X); // càng gần minX càng tốt
                            else
                                sideScore = Math.Max(sideScore, p.X - xMid); // càng gần maxX càng tốt
                        }

                        if (span > bestSpan + TOL ||
                            (Math.Abs(span - bestSpan) <= TOL && sideScore < bestXScore))
                        {
                            bestSpan = span;
                            bestXScore = sideScore;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }

                if (bestA != null && bestB != null)
                {
                    if (bestA.Y <= bestB.Y)
                    {
                        bottomPoint = Clone2D(bestA);
                        topPoint = Clone2D(bestB);
                    }
                    else
                    {
                        bottomPoint = Clone2D(bestB);
                        topPoint = Clone2D(bestA);
                    }
                    return true;
                }

                // BƯỚC 2 fallback: nếu Tekla không trả đúng cạnh liên tiếp do fillet/polycurve,
                // mới gom theo X và lấy span Y dài nhất. Vẫn giữ ngưỡng để tránh lấy đoạn fillet nhỏ.
                Point bestMin = null;
                Point bestMax = null;
                double fallbackBestSpan = -1.0;
                double fallbackBestSideScore = 999999999.0;

                foreach (Point seed in innerPoints)
                {
                    if (seed == null)
                        continue;

                    Point groupMin = null;
                    Point groupMax = null;
                    double groupMinY = 999999999.0;
                    double groupMaxY = -999999999.0;
                    int groupCount = 0;
                    double groupX = seed.X;

                    foreach (Point p in innerPoints)
                    {
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - seed.X) > bandTol)
                            continue;

                        groupCount++;

                        if (p.Y < groupMinY)
                        {
                            groupMinY = p.Y;
                            groupMin = p;
                        }

                        if (p.Y > groupMaxY)
                        {
                            groupMaxY = p.Y;
                            groupMax = p;
                        }
                    }

                    if (groupCount < 2 || groupMin == null || groupMax == null)
                        continue;

                    double span = Math.Abs(groupMaxY - groupMinY);
                    if (span < minSpan)
                        continue;

                    double sideScore = 0.0;
                    foreach (Point p in innerPoints)
                    {
                        if (p == null)
                            continue;

                        if (takeMinX)
                            sideScore = Math.Max(sideScore, groupX - p.X);
                        else
                            sideScore = Math.Max(sideScore, p.X - groupX);
                    }

                    if (span > fallbackBestSpan + TOL ||
                        (Math.Abs(span - fallbackBestSpan) <= TOL && sideScore < fallbackBestSideScore))
                    {
                        fallbackBestSpan = span;
                        fallbackBestSideScore = sideScore;
                        bestMin = groupMin;
                        bestMax = groupMax;
                    }
                }

                if (bestMin == null || bestMax == null)
                    return false;

                bottomPoint = Clone2D(bestMin);
                topPoint = Clone2D(bestMax);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Point FindExtremePointOnHorizontalBand(
            List<Point> pts,
            double targetY,
            bool takeMinX,
            double tol,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            // FIX SNAP ENDPOINT:
            // Không dùng midpoint/điểm giữa cung fillet.
            // Với rãnh có fillet, polygon thường có nhiều điểm nhỏ trên cung.
            // Hàm này chỉ lấy endpoint của ĐOẠN THẲNG NGANG dài nhất trong cụm rãnh.
            // Như vậy DIM rãnh sẽ bám endpoint của đoạn line thật, không bám mid point của cung.
            if (pts == null || pts.Count == 0)
                return null;

            double bandTol = Math.Max(2.0, tol);
            double minSpan = Math.Max(NOTCH_MIN_DIM_TO_CREATE, NOTCH_MIN_SIZE);

            Point bestMin = null;
            Point bestMax = null;
            double bestSpan = -1.0;
            double bestOuterScore = 999999999.0;

            foreach (Point seed in pts)
            {
                if (seed == null)
                    continue;

                Point minP = null;
                Point maxP = null;
                double lineMinX = 999999999.0;
                double lineMaxX = -999999999.0;
                int sameLineCount = 0;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.Y - seed.Y) > bandTol)
                        continue;

                    sameLineCount++;

                    if (p.X < lineMinX)
                    {
                        lineMinX = p.X;
                        minP = p;
                    }

                    if (p.X > lineMaxX)
                    {
                        lineMaxX = p.X;
                        maxP = p;
                    }
                }

                if (sameLineCount < 2 || minP == null || maxP == null)
                    continue;

                double span = Math.Abs(lineMaxX - lineMinX);
                if (span < minSpan)
                    continue;

                // Ưu tiên đoạn thẳng dài nhất; nếu gần bằng nhau thì ưu tiên endpoint gần mép ngoài cùng của thanh.
                Point candidateEndpoint = takeMinX ? minP : maxP;
                double outerScore = GetDistanceToOuterEdge(candidateEndpoint, minX, maxX, minY, maxY);

                if (span > bestSpan + TOL ||
                    (Math.Abs(span - bestSpan) <= TOL && outerScore < bestOuterScore))
                {
                    bestSpan = span;
                    bestOuterScore = outerScore;
                    bestMin = minP;
                    bestMax = maxP;
                }
            }

            if (bestSpan >= minSpan)
                return takeMinX ? Clone2D(bestMin) : Clone2D(bestMax);

            // Fallback rất nhẹ: nếu không tìm được đoạn line dài, mới dùng điểm gần targetY.
            // Giữ fallback để không làm mất DIM rãnh đã từng nhận được.
            Point fallback = null;
            foreach (Point p in pts)
            {
                if (p == null)
                    continue;

                if (Math.Abs(p.Y - targetY) > bandTol)
                    continue;

                if (fallback == null)
                {
                    fallback = p;
                    continue;
                }

                if (takeMinX)
                {
                    if (p.X < fallback.X)
                        fallback = p;
                }
                else
                {
                    if (p.X > fallback.X)
                        fallback = p;
                }
            }

            return fallback == null ? null : Clone2D(fallback);
        }

        private static Point FindExtremePointOnVerticalBand(
            List<Point> pts,
            double targetX,
            bool takeMinY,
            double tol,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            // FIX SNAP ENDPOINT:
            // Không dùng midpoint/điểm giữa cung fillet.
            // Chỉ lấy endpoint của ĐOẠN THẲNG DỌC dài nhất trong cụm rãnh.
            if (pts == null || pts.Count == 0)
                return null;

            double bandTol = Math.Max(2.0, tol);
            double minSpan = Math.Max(NOTCH_MIN_DIM_TO_CREATE, NOTCH_MIN_SIZE);

            Point bestMin = null;
            Point bestMax = null;
            double bestSpan = -1.0;
            double bestOuterScore = 999999999.0;

            foreach (Point seed in pts)
            {
                if (seed == null)
                    continue;

                Point minP = null;
                Point maxP = null;
                double lineMinY = 999999999.0;
                double lineMaxY = -999999999.0;
                int sameLineCount = 0;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.X - seed.X) > bandTol)
                        continue;

                    sameLineCount++;

                    if (p.Y < lineMinY)
                    {
                        lineMinY = p.Y;
                        minP = p;
                    }

                    if (p.Y > lineMaxY)
                    {
                        lineMaxY = p.Y;
                        maxP = p;
                    }
                }

                if (sameLineCount < 2 || minP == null || maxP == null)
                    continue;

                double span = Math.Abs(lineMaxY - lineMinY);
                if (span < minSpan)
                    continue;

                // Ưu tiên đoạn thẳng dài nhất; nếu gần bằng nhau thì ưu tiên endpoint gần mép ngoài cùng của thanh.
                Point candidateEndpoint = takeMinY ? minP : maxP;
                double outerScore = GetDistanceToOuterEdge(candidateEndpoint, minX, maxX, minY, maxY);

                if (span > bestSpan + TOL ||
                    (Math.Abs(span - bestSpan) <= TOL && outerScore < bestOuterScore))
                {
                    bestSpan = span;
                    bestOuterScore = outerScore;
                    bestMin = minP;
                    bestMax = maxP;
                }
            }

            if (bestSpan >= minSpan)
                return takeMinY ? Clone2D(bestMin) : Clone2D(bestMax);

            // Fallback nhẹ để không làm mất rãnh nếu polygon thiếu đoạn thẳng rõ.
            Point fallback = null;
            foreach (Point p in pts)
            {
                if (p == null)
                    continue;

                if (Math.Abs(p.X - targetX) > bandTol)
                    continue;

                if (fallback == null)
                {
                    fallback = p;
                    continue;
                }

                if (takeMinY)
                {
                    if (p.Y < fallback.Y)
                        fallback = p;
                }
                else
                {
                    if (p.Y > fallback.Y)
                        fallback = p;
                }
            }

            return fallback == null ? null : Clone2D(fallback);
        }

        private static double GetDistanceToOuterEdge(
            Point p,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (p == null)
                return 999999999.0;

            double dxMin = Math.Abs(p.X - minX);
            double dxMax = Math.Abs(p.X - maxX);
            double dyMin = Math.Abs(p.Y - minY);
            double dyMax = Math.Abs(p.Y - maxY);

            return Math.Min(Math.Min(dxMin, dxMax), Math.Min(dyMin, dyMax));
        }


        private static Point FindNearestPoint(List<Point> pts, double x, double y)
        {
            if (pts == null || pts.Count == 0)
                return null;

            Point best = null;
            double bestDist = 999999999.0;

            foreach (Point p in pts)
            {
                if (p == null)
                    continue;

                double dx = p.X - x;
                double dy = p.Y - y;
                double d = dx * dx + dy * dy;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best == null ? null : Clone2D(best);
        }

        private static Point FindEdgePointNearestX(
            List<Point> polygon,
            double targetX,
            double edgeY,
            bool allowFallback,
            double edgeTol)
        {
            Point best = null;
            double bestDist = 999999999.0;

            try
            {
                if (polygon != null)
                {
                    foreach (Point p in polygon)
                    {
                        if (p == null)
                            continue;

                        if (Math.Abs(p.Y - edgeY) <= edgeTol)
                        {
                            double d = Math.Abs(p.X - targetX);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                best = p;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            if (best != null)
                return Clone2D(best);

            if (allowFallback)
                return new Point(targetX, edgeY, 0);

            return null;
        }

        private static Point FindEdgePointNearestY(
            List<Point> polygon,
            double targetY,
            double edgeX,
            bool allowFallback,
            double edgeTol)
        {
            Point best = null;
            double bestDist = 999999999.0;

            try
            {
                if (polygon != null)
                {
                    foreach (Point p in polygon)
                    {
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - edgeX) <= edgeTol)
                        {
                            double d = Math.Abs(p.Y - targetY);
                            if (d < bestDist)
                            {
                                bestDist = d;
                                best = p;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            if (best != null)
                return Clone2D(best);

            if (allowFallback)
                return new Point(edgeX, targetY, 0);

            return null;
        }

        private static int CreateTopViewChamferDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength,
            out ChamferInfluence influence,
            Point ignoredSegmentPoint1 = null,
            Point ignoredSegmentPoint2 = null,
            Point ignoredSegmentPoint3 = null,
            Point ignoredSegmentPoint4 = null)
        {
            influence = new ChamferInfluence();
            int count = 0;

            try
            {
                if (polygon == null || polygon.Count < 3)
                    return count;

                List<Point> pts = SortPolygonPointsClockwise(polygon);

                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (IsSameSegment2D(
                        a,
                        b,
                        ignoredSegmentPoint1,
                        ignoredSegmentPoint2,
                        Math.Max(1.0, TOL)))
                        continue;

                    if (IsSameSegment2D(
                        a,
                        b,
                        ignoredSegmentPoint3,
                        ignoredSegmentPoint4,
                        Math.Max(1.0, TOL)))
                        continue;

                    if (!IsValidTopViewChamferSegment(a, b, minX, maxX, minY, maxY))
                        continue;

                    double dx = Math.Abs(a.X - b.X);
                    double dy = Math.Abs(a.Y - b.Y);

                    double midX = (a.X + b.X) / 2.0;
                    double midY = (a.Y + b.Y) / 2.0;

                    bool topSide = midY >= centerY;
                    bool rightSide = midX >= centerX;

                    bool nearLeftEnd = Math.Abs(midX - minX) <= CHAMFER_MAX_SIZE;
                    bool nearRightEnd = Math.Abs(midX - maxX) <= CHAMFER_MAX_SIZE;
                    bool nearBottomEdge = Math.Abs(midY - minY) <= CHAMFER_MAX_SIZE;
                    bool nearTopEdge = Math.Abs(midY - maxY) <= CHAMFER_MAX_SIZE;

                    // FIX TẦNG THEO TỪNG HƯỚNG RIÊNG BIỆT:
                    // Không dùng nearTop/nearBottom theo CHAMFER_MAX_SIZE để set influence,
                    // vì với bản thép mỏng, một chamfer phía trên vẫn có thể "nearBottom".
                    // Dùng vị trí thật theo tâm view để xác định đúng hướng chamfer chiếm tầng.
                    if (rightSide)
                        influence.Right = true;
                    else
                        influence.Left = true;

                    if (topSide)
                        influence.Top = true;
                    else
                        influence.Bottom = true;

                    Vector horizontalDirection = topSide
                        ? new Vector(0, 1, 0)
                        : new Vector(0, -1, 0);

                    Vector verticalDirection = rightSide
                        ? new Vector(1, 0, 0)
                        : new Vector(-1, 0, 0);

                    // Chân DIM chamfer bắt đúng 2 điểm thật của cạnh xiên.
                    Point p1 = new Point(a.X, a.Y, 0);
                    Point p2 = new Point(b.X, b.Y, 0);

                    // CHAMFER TẦNG 0:
                    // Quy tắc mới: không bù theo bounding box và không cộng khoảng hụt.
                    // Tekla sẽ đặt DIM theo offset từ chính các chân DIM thật; chân ngoài cùng thật
                    // của cạnh chamfer là điểm neo tầng. Tầng 0 chỉ dùng cho chamfer.
                    double chamferTierOffset = GetSteelDimOffsetByTier(0);
                    double horizontalChamferOffset = GetChamferHorizontalOffsetFromOuter(
                        p1,
                        p2,
                        topSide,
                        minY,
                        maxY,
                        chamferTierOffset
                    );
                    double verticalChamferOffset = GetChamferVerticalOffsetFromOuter(
                        p1,
                        p2,
                        rightSide,
                        minX,
                        maxX,
                        chamferTierOffset
                    );

                    if (dx >= CHAMFER_MIN_SIZE)
                    {
                        // CHỈ SỬA DIM CHAMFER NGANG:
                        // Khi chamfer bị hụt, điểm neo tầng của DIM ngang phải là chân ngoài cùng thật:
                        // - Chamfer phía trên: dùng chân có Y cao hơn làm điểm đầu/neo.
                        // - Chamfer phía dưới: dùng chân có Y thấp hơn làm điểm đầu/neo.
                        // Không đổi offset, không đổi DIM chamfer dọc.
                        Point horizontalP1 = p1;
                        Point horizontalP2 = p2;

                        if (topSide)
                        {
                            if (p2.Y > p1.Y)
                            {
                                horizontalP1 = p2;
                                horizontalP2 = p1;
                            }
                        }
                        else
                        {
                            if (p2.Y < p1.Y)
                            {
                                horizontalP1 = p2;
                                horizontalP2 = p1;
                            }
                        }

                        if (CreateDim(
                            handler,
                            view,
                            horizontalP1,
                            horizontalP2,
                            horizontalDirection,
                            horizontalChamferOffset))
                        {
                            count++;
                        }
                    }

                    if (dy >= CHAMFER_MIN_SIZE)
                    {
                        if (CreateDim(
                            handler,
                            view,
                            p1,
                            p2,
                            verticalDirection,
                            verticalChamferOffset))
                        {
                            count++;
                        }
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static double GetChamferHorizontalOffsetFromOuter(
            Point p1,
            Point p2,
            bool topSide,
            double minY,
            double maxY,
            double tierOffset)
        {
            // Quy tắc mới khi DIM chamfer ở TOP VIEW:
            // Không lấy maxY/minY bounding box và không cộng bù khoảng hụt.
            // DIM dùng chính 2 chân thật của cạnh chamfer; chân ngoài cùng thật
            // trong 2 chân đó là điểm neo tầng. Vì vậy offset truyền vào Tekla
            // chỉ là offset tầng thuần.
            return tierOffset;
        }

        private static double GetChamferVerticalOffsetFromOuter(
            Point p1,
            Point p2,
            bool rightSide,
            double minX,
            double maxX,
            double tierOffset)
        {
            // Quy tắc mới khi DIM chamfer ở TOP VIEW:
            // Không lấy maxX/minX bounding box và không cộng bù khoảng hụt.
            // DIM dùng chính 2 chân thật của cạnh chamfer; chân ngoài cùng thật
            // trong 2 chân đó là điểm neo tầng. Vì vậy offset truyền vào Tekla
            // chỉ là offset tầng thuần.
            return tierOffset;
        }

        private static bool IsValidTopViewChamferSegment(
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

            if (dx < CHAMFER_MIN_SIZE ||
                dy < CHAMFER_MIN_SIZE ||
                dx > CHAMFER_MAX_SIZE ||
                dy > CHAMFER_MAX_SIZE)
                return false;

            double ratio = dx / dy;

            if (ratio < CHAMFER_MIN_RATIO || ratio > CHAMFER_MAX_RATIO)
                return false;

            double midX = (a.X + b.X) / 2.0;
            double midY = (a.Y + b.Y) / 2.0;

            // Chỉ nhận cạnh xiên nằm gần góc ngoài của view.
            // Điều kiện cũ chỉ xét midpoint gần mép nên rãnh/slot mặt Front chiếu lên Top
            // vẫn có thể bị hiểu nhầm là chamfer. Điều kiện mới bắt buộc cạnh xiên phải
            // thật sự nối 1 mép X ngoài với 1 mép Y ngoài của cùng một góc.
            bool nearLeftEnd = Math.Abs(midX - minX) <= CHAMFER_MAX_SIZE;
            bool nearRightEnd = Math.Abs(midX - maxX) <= CHAMFER_MAX_SIZE;
            bool nearBottomEdge = Math.Abs(midY - minY) <= CHAMFER_MAX_SIZE;
            bool nearTopEdge = Math.Abs(midY - maxY) <= CHAMFER_MAX_SIZE;

            if (!(nearLeftEnd || nearRightEnd))
                return false;

            if (!(nearBottomEdge || nearTopEdge))
                return false;

            double edgeTol = Math.Max(2.0, TOL + 1.0);

            bool aOnLeft = Math.Abs(a.X - minX) <= edgeTol;
            bool aOnRight = Math.Abs(a.X - maxX) <= edgeTol;
            bool aOnBottom = Math.Abs(a.Y - minY) <= edgeTol;
            bool aOnTop = Math.Abs(a.Y - maxY) <= edgeTol;

            bool bOnLeft = Math.Abs(b.X - minX) <= edgeTol;
            bool bOnRight = Math.Abs(b.X - maxX) <= edgeTol;
            bool bOnBottom = Math.Abs(b.Y - minY) <= edgeTol;
            bool bOnTop = Math.Abs(b.Y - maxY) <= edgeTol;

            bool topLeftChamfer =
                (aOnLeft && bOnTop) || (bOnLeft && aOnTop);

            bool topRightChamfer =
                (aOnRight && bOnTop) || (bOnRight && aOnTop);

            bool bottomLeftChamfer =
                (aOnLeft && bOnBottom) || (bOnLeft && aOnBottom);

            bool bottomRightChamfer =
                (aOnRight && bOnBottom) || (bOnRight && aOnBottom);

            if (!(topLeftChamfer || topRightChamfer || bottomLeftChamfer || bottomRightChamfer))
                return false;

            return true;
        }





        private static int CreateTopViewTotalDims(
            StraightDimensionSetHandler handler,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double horizontalTotalOffset,
            double verticalTotalOffset)
        {
            int count = 0;

            PointList lengthPts = new PointList();
            // DIM tổng ngang phải bắt vào điểm ngoài cùng thật của dầm.
            // Nếu góc bị chamfer, không lấy điểm trên mép bị cắt lưng chừng nữa.
            lengthPts.Add(Clone2D(edgeAnchors.LeftMost));
            lengthPts.Add(Clone2D(edgeAnchors.RightMost));

            // Quy tắc mới khi DIM tổng TOP gặp chamfer/rãnh:
            // Không dùng bounding box và không cộng bù rãnh. Offset tầng lấy theo
            // cực trị A/B/C/D rồi quy đổi từ chân đầu thật của PointList.
            Vector lengthDirection = new Vector(0, 1, 0);
            double realUpperTotalOffset = ResolveDimDistanceByAnchor4(
                lengthPts,
                lengthDirection,
                offsetAnchors,
                horizontalTotalOffset);

            if (handler.CreateDimensionSet(
                view,
                lengthPts,
                lengthDirection,
                realUpperTotalOffset) != null)
                count++;

            PointList heightPts = new PointList();
            // DIM tổng dọc phải bắt vào điểm thấp/cao ngoài cùng thật của dầm.
            heightPts.Add(Clone2D(edgeAnchors.TopMost));
            heightPts.Add(Clone2D(edgeAnchors.BottomMost));

            // DIM tổng dọc bên trái: nếu chamfer ảnh hưởng cạnh trái thì đã được cộng tầng ở trên.
            // Quy tắc mới khi DIM tổng dọc TOP gặp chamfer/rãnh:
            // Không dùng bounding box và không cộng bù rãnh. Offset tầng lấy theo
            // cực trị A/B/C/D rồi quy đổi từ chân đầu thật của PointList.
            Vector heightDirection = new Vector(-1, 0, 0);
            double realLeftTotalOffset = ResolveDimDistanceByAnchor4(
                heightPts,
                heightDirection,
                offsetAnchors,
                verticalTotalOffset);

            if (handler.CreateDimensionSet(
                view,
                heightPts,
                heightDirection,
                realLeftTotalOffset) != null)
                count++;

            return count;
        }


        private static Point FindRealContourPointOnVerticalLine(
            List<Point> polygon,
            double x,
            bool takeBottom,
            double minY,
            double maxY)
        {
            // Dùng cho chân DIM lỗ TOP VIEW khi cạnh dầm có chamfer/rãnh.
            // Thay vì dùng điểm ảo (x, minY/maxY), hàm này tìm giao điểm thật
            // giữa đường thẳng đứng X = x và contour polygon của dầm.
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return new Point(x, takeBottom ? minY : maxY, 0);

                Point hit = FindVerticalContourIntersectionInPointOrder(
                    polygon,
                    x,
                    takeBottom,
                    minY,
                    maxY
                );

                if (hit != null)
                    return hit;

                List<Point> sorted = SortPolygonPointsClockwise(polygon);
                hit = FindVerticalContourIntersectionInPointOrder(
                    sorted,
                    x,
                    takeBottom,
                    minY,
                    maxY
                );

                if (hit != null)
                    return hit;
            }
            catch
            {
            }

            return new Point(x, takeBottom ? minY : maxY, 0);
        }

        private static Point FindVerticalContourIntersectionInPointOrder(
            List<Point> pts,
            double x,
            bool takeBottom,
            double minY,
            double maxY)
        {
            try
            {
                if (pts == null || pts.Count < 2)
                    return null;

                double bestY = takeBottom ? 999999999.0 : -999999999.0;
                bool found = false;
                double tol = Math.Max(1.0, TOL);

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    double ax = a.X;
                    double bx = b.X;
                    double ay = a.Y;
                    double by = b.Y;

                    double segMinX = Math.Min(ax, bx) - tol;
                    double segMaxX = Math.Max(ax, bx) + tol;

                    if (x < segMinX || x > segMaxX)
                        continue;

                    double y;

                    if (Math.Abs(bx - ax) <= tol)
                    {
                        if (Math.Abs(x - ax) > tol)
                            continue;

                        // Segment đứng nằm ngay đường DIM.
                        // Lấy đầu ngoài cùng theo hướng cần tìm.
                        y = takeBottom ? Math.Min(ay, by) : Math.Max(ay, by);
                    }
                    else
                    {
                        double t = (x - ax) / (bx - ax);

                        if (t < -0.01 || t > 1.01)
                            continue;

                        if (t < 0.0) t = 0.0;
                        if (t > 1.0) t = 1.0;

                        y = ay + t * (by - ay);
                    }

                    if (y < minY - 5.0 || y > maxY + 5.0)
                        continue;

                    if (takeBottom)
                    {
                        if (!found || y < bestY)
                        {
                            bestY = y;
                            found = true;
                        }
                    }
                    else
                    {
                        if (!found || y > bestY)
                        {
                            bestY = y;
                            found = true;
                        }
                    }
                }

                if (!found)
                    return null;

                return new Point(x, bestY, 0);
            }
            catch
            {
                return null;
            }
        }


        private static double GetHoleDimGap(Point h)
        {
            try
            {
                // Point.Z chỉ mang phi lỗ thật để đẩy chân DIM ra khỏi tâm lỗ.
                if (h != null && h.Z > MIN_VALID_HOLE_DIM_GAP && h.Z < 200.0)
                    return h.Z;
            }
            catch
            {
            }

            return 0.0;
        }


        private static double GetTopBottomRealHoleDiameterFromBoltGroup(ModelBoltGroup bg)
        {
            try
            {
                double direct = ReadFirstValidHoleDiameter(bg);
                if (direct > MIN_VALID_HOLE_DIM_GAP)
                    return direct;

                double fromModel = GetModelHoleDiameterFallback(bg);
                if (fromModel > MIN_VALID_HOLE_DIM_GAP)
                    return fromModel;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetHoleDiameterFromBoltGroup(ModelBoltGroup bg)
        {
            try
            {
                double direct = ReadFirstValidHoleDiameter(bg);
                if (direct > MIN_VALID_HOLE_DIM_GAP)
                    return direct;

                double fromModel = GetModelHoleDiameterFallback(bg);
                if (fromModel > MIN_VALID_HOLE_DIM_GAP)
                    return fromModel;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double ReadFirstValidHoleDiameter(ModelBoltGroup bg)
        {
            try
            {
                if (bg == null)
                    return 0.0;

                string[] holeNames = new string[]
                {
                    "HOLE_DIAMETER",
                    "BOLT_HOLE_DIAMETER",
                    "BOLT_HOLE_SIZE",
                    "HOLE_SIZE",
                    "DIAMETER",
                    "HOLE.DIAMETER",
                    "HOLE_DIAMETER_1",
                    "HOLE_DIAMETER_2",
                    "HOLE_DIAMETER_3",
                    "HOLE_DIAMETER_4"
                };

                foreach (string name in holeNames)
                {
                    double d = GetReportDouble(bg, name);
                    if (d > MIN_VALID_HOLE_DIM_GAP && d < 200.0)
                        return d;
                }

                string[] propNames = new string[]
                {
                    "HoleDiameter",
                    "HoleSize",
                    "BoltHoleDiameter",
                    "BoltHoleSize",
                    "Hole1",
                    "Hole2",
                    "Hole3",
                    "Hole4"
                };

                foreach (string propName in propNames)
                {
                    double d = GetDoublePropertyByReflection(bg, propName);
                    if (d > MIN_VALID_HOLE_DIM_GAP && d < 200.0)
                        return d;
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetModelHoleDiameterFallback(ModelBoltGroup bg)
        {
            try
            {
                if (bg == null)
                    return 0.0;

                double boltSize = 0.0;

                try
                {
                    boltSize = bg.BoltSize;
                }
                catch
                {
                }

                if (boltSize <= MIN_VALID_HOLE_DIM_GAP || boltSize >= 200.0)
                    boltSize = GetReportDouble(bg, "BOLT_SIZE");

                if (boltSize <= MIN_VALID_HOLE_DIM_GAP || boltSize >= 200.0)
                    boltSize = GetDoublePropertyByReflection(bg, "BoltSize");

                if (boltSize <= MIN_VALID_HOLE_DIM_GAP || boltSize >= 200.0)
                    return 0.0;

                double tolerance = 0.0;

                try
                {
                    tolerance = bg.Tolerance;
                }
                catch
                {
                }

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetReportDouble(bg, "TOLERANCE");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetReportDouble(bg, "BOLT_HOLE_TOLERANCE");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetReportDouble(bg, "HOLE_TOLERANCE");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetReportDouble(bg, "CLEARANCE");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetDoublePropertyByReflection(bg, "Tolerance");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetDoublePropertyByReflection(bg, "HoleTolerance");

                if (tolerance <= 0.0 || tolerance >= 50.0)
                    tolerance = GetDoublePropertyByReflection(bg, "Clearance");

                if (tolerance < 0.0 || tolerance >= 50.0)
                    tolerance = 0.0;

                double diameter = boltSize + Math.Max(0.0, tolerance);
                if (diameter > MIN_VALID_HOLE_DIM_GAP && diameter < 200.0)
                    return diameter;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetHoleDiameterFromDrawingBolt(DrawingObject drawingBolt)
        {
            try
            {
                if (drawingBolt == null)
                    return 0.0;

                // Fallback hình học: thử đọc bounding box của bolt/center mark trong drawing.
                // Không dùng hằng số 18/22. Nếu lấy được box hợp lý thì dùng kích thước nhỏ hơn.
                object box = TryInvokeNoArg(drawingBolt, "GetAxisAlignedBoundingBox");
                if (box == null) box = TryInvokeNoArg(drawingBolt, "GetObjectAlignedBoundingBox");
                if (box == null) box = TryInvokeNoArg(drawingBolt, "GetBoundingBox");

                if (box == null)
                    return 0.0;

                Point minP = TryGetPointProperty(box, "MinPoint");
                Point maxP = TryGetPointProperty(box, "MaxPoint");

                if (minP == null || maxP == null)
                    return 0.0;

                double w = Math.Abs(maxP.X - minP.X);
                double h = Math.Abs(maxP.Y - minP.Y);
                double d = Math.Min(w, h);

                if (d > MIN_VALID_HOLE_DIM_GAP && d < 200.0)
                    return d;
            }
            catch
            {
            }

            return 0.0;
        }

        private static object TryInvokeNoArg(object obj, string methodName)
        {
            try
            {
                if (obj == null)
                    return null;

                MethodInfo mi = obj.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (mi == null)
                    return null;

                return mi.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static Point TryGetPointProperty(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                PropertyInfo pi = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (pi == null || !pi.CanRead)
                    return null;

                return pi.GetValue(obj, null) as Point;
            }
            catch
            {
                return null;
            }
        }

        private static double GetReportDouble(ModelBoltGroup bg, string propertyName)
        {
            try
            {
                string text = "";
                bg.GetReportProperty(propertyName, ref text);

                if (string.IsNullOrEmpty(text))
                    return -999999.0;

                text = text
                    .Replace("M", "")
                    .Replace("Ø", "")
                    .Replace("Φ", "")
                    .Replace(" ", "")
                    .Replace(",", ".");

                double value;
                if (double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return -999999.0;
        }

        private static double GetDoublePropertyByReflection(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return -999999.0;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return -999999.0;

                object value = prop.GetValue(obj, null);

                if (value == null)
                    return -999999.0;

                if (value is double)
                    return (double)value;

                if (value is int)
                    return Convert.ToDouble(value);

                double d;
                string text = value.ToString().Replace(",", ".");

                if (double.TryParse(
                    text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out d))
                {
                    return d;
                }
            }
            catch
            {
            }

            return -999999.0;
        }

        private static Identifier TryGetModelIdentifier(DrawingObject drawingObject)
        {
            try
            {
                if (drawingObject == null)
                    return null;

                PropertyInfo prop =
                    drawingObject.GetType().GetProperty(
                        "ModelIdentifier",
                        BindingFlags.Public | BindingFlags.Instance
                    );

                if (prop == null || !prop.CanRead)
                    return null;

                object value = prop.GetValue(drawingObject, null);
                return value as Identifier;
            }
            catch
            {
                return null;
            }
        }

        private static double GetFlangeThicknessFromProfile(ModelPart part)
        {
            try
            {
                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);

                if (string.IsNullOrEmpty(profile))
                    return 0.0;

                string p = profile.ToUpper()
                    .Replace("BH", "")
                    .Replace("H", "")
                    .Replace("I", "")
                    .Replace("PL", "")
                    .Replace(" ", "")
                    .Replace(",", ".");

                string[] tokens = p.Split(
                    new char[] { '*', 'X', 'x', '-' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                List<double> values = new List<double>();

                foreach (string token in tokens)
                {
                    double v;
                    if (double.TryParse(
                        token,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out v))
                    {
                        if (v > 0)
                            values.Add(v);
                    }
                }

                if (values.Count >= 4)
                    return values[values.Count - 1];

                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<Point> GetTopSectionFacePolygon(Solid solid, Point min, Point max)
        {
            // TOP VIEW - BIÊN DẠNG MẶT CẮT CŨ, dùng riêng cho DIM DỌC lỗ.
            // Đây là thuật toán file bạn đang báo đúng: lấy mặt cắt gần mặt trên theo Z,
            // rồi FindRealContourPointOnVerticalLine() dò mép thật theo X của lỗ.
            List<Point> best = new List<Point>();

            try
            {
                double[] zPlanes = new double[]
                {
                    max.Z - 0.5,
                    max.Z - 1.0,
                    max.Z - 2.0,
                    max.Z - 5.0,
                    max.Z - 10.0
                };

                double bestScore = -1.0;

                foreach (double z in zPlanes)
                {
                    Point p1 = new Point(min.X - 1000, min.Y - 1000, z);
                    Point p2 = new Point(max.X + 1000, min.Y - 1000, z);
                    Point p3 = new Point(min.X - 1000, max.Y + 1000, z);

                    List<Point> poly =
                        GetLargestIntersectionPolygon(
                            solid.IntersectAllFaces(p1, p2, p3)
                        );

                    if (poly.Count < 2)
                        continue;

                    double sx1, sx2, sy1, sy2;
                    GetMinMax(poly, out sx1, out sx2, out sy1, out sy2);

                    double width = Math.Abs(sx2 - sx1);
                    double height = Math.Abs(sy2 - sy1);

                    if (width < 100.0)
                        continue;

                    if (height < 20.0)
                        continue;

                    double score = width * height;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = poly;
                    }
                }
            }
            catch
            {
            }

            return best;
        }

        private static List<Point> GetTopFacePolygon(Solid solid, Point min, Point max)
        {
            // TOP VIEW - NGUỒN BIÊN DẠNG MỚI THEO YÊU CẦU:
            // Không ưu tiên lấy mặt cắt ngang theo Z nữa.
            // Sau khi SetCurrentTransformationPlane(view.DisplayCoordinateSystem),
            // lấy toàn bộ điểm thật của solid rồi chiếu xuống hệ tọa độ nhìn thẳng của view,
            // cách này giống nguồn điểm Front đang dùng cho biên dạng/rãnh.
            // Mục tiêu: kích thước tổng/chamfer/rãnh Top lấy theo biên dạng nhìn thẳng xuống,
            // tránh lỗi do mặt cắt top face bắt sai ở fillet/chamfer/rãnh.
            List<Point> projected = new List<Point>();

            try
            {
                double topProjectedVisibleDepth =
                    Math.Max(0.0, (max.Z - min.Z) - TOP_PROJECTED_BOTTOM_EXCLUDE);

                projected = GetProjectedSolidPointsForTopDepth(
                    solid,
                    max.Z,
                    topProjectedVisibleDepth
                );

                if (projected != null && projected.Count >= 2)
                {
                    double px1, px2, py1, py2;
                    GetMinMax(projected, out px1, out px2, out py1, out py2);

                    double projectedWidth = Math.Abs(px2 - px1);
                    double projectedHeight = Math.Abs(py2 - py1);

                    // Safe guard giống logic cũ: chỉ nhận nếu biên dạng đủ hợp lý.
                    if (projectedWidth >= 100.0 && projectedHeight >= 20.0)
                        return projected;
                }
            }
            catch
            {
            }

            // FALLBACK AN TOÀN:
            // Nếu vì Tekla/API không lấy được điểm chiếu solid thì quay lại cách mặt cắt cũ,
            // để không làm chết tool trên các profile lạ.
            List<Point> best = new List<Point>();

            try
            {
                double[] zPlanes = new double[]
                {
                    max.Z - 0.5,
                    max.Z - 1.0,
                    max.Z - 2.0,
                    max.Z - 5.0,
                    max.Z - 10.0
                };

                double bestScore = -1.0;

                foreach (double z in zPlanes)
                {
                    Point p1 = new Point(min.X - 1000, min.Y - 1000, z);
                    Point p2 = new Point(max.X + 1000, min.Y - 1000, z);
                    Point p3 = new Point(min.X - 1000, max.Y + 1000, z);

                    List<Point> poly =
                        GetLargestIntersectionPolygon(
                            solid.IntersectAllFaces(p1, p2, p3)
                        );

                    if (poly.Count < 2)
                        continue;

                    double minX, maxX, minY, maxY;
                    GetMinMax(poly, out minX, out maxX, out minY, out maxY);

                    double width = Math.Abs(maxX - minX);
                    double height = Math.Abs(maxY - minY);

                    if (width < 100.0)
                        continue;

                    if (height < 20.0)
                        continue;

                    double score = width * height;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = poly;
                    }
                }
            }
            catch
            {
            }

            return best;
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

        private static List<Point> GetLargestIntersectionPolygon(IEnumerator en)
        {
            List<List<Point>> all = new List<List<Point>>();

            try
            {
                while (en.MoveNext())
                    CollectPointLists(en.Current, all, 0);
            }
            catch
            {
            }

            List<Point> best = new List<Point>();
            double bestScore = -1.0;

            foreach (List<Point> list in all)
            {
                if (list.Count < 2)
                    continue;

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

        private static void CollectPointLists(
            object obj,
            List<List<Point>> result,
            int depth)
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


        private static void ApplyExactRepresentationToView(View view)
        {
            try
            {
                if (view == null)
                    return;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(DrawingPart));

                while (parts.MoveNext())
                {
                    DrawingPart dp = parts.Current as DrawingPart;
                    if (dp == null)
                        continue;

                    SetDrawingPartRepresentationExact(dp);
                }
            }
            catch
            {
            }
        }


        private static View FindViewByViewType(
            List<View> views,
            string exactViewTypeName,
            string fallbackText)
        {
            try
            {
                if (views == null)
                    return null;

                foreach (View view in views)
                {
                    if (ViewTypeMatches(view, exactViewTypeName, fallbackText))
                        return view;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ViewTypeMatches(
            View view,
            string exactViewTypeName,
            string fallbackText)
        {
            try
            {
                if (view == null)
                    return false;

                string text = "";

                try
                {
                    text = view.ViewType.ToString();
                }
                catch
                {
                    text = "";
                }

                if (!string.IsNullOrEmpty(exactViewTypeName) &&
                    string.Equals(text, exactViewTypeName, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Fallback mềm cho vài môi trường Tekla trả chuỗi khác nhau,
                // nhưng vẫn chỉ đọc thuộc tính ViewType, không dựa vào vị trí view trên giấy.
                if (!string.IsNullOrEmpty(fallbackText) &&
                    text.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool IsSameView(View a, View b)
        {
            if (a == null || b == null)
                return false;

            return System.Object.ReferenceEquals(a, b);
        }

        private static List<View> BuildDimViewsByViewTypeSafe(
            List<View> views,
            View exactView,
            View topView,
            View frontView)
        {
            List<View> result = new List<View>();

            try
            {
                AddUniqueDimView(result, topView, exactView);
                AddUniqueDimView(result, frontView, exactView);

                // Fallback giữ thứ tự Y cũ cho các trường hợp Tekla không trả được ViewType,
                // nhưng Top/Front đã nhận bằng ViewType sẽ luôn được ưu tiên trước.
                if (views != null)
                {
                    foreach (View view in views)
                        AddUniqueDimView(result, view, exactView);
                }
            }
            catch
            {
            }

            return result;
        }

        private static void AddUniqueDimView(
            List<View> result,
            View view,
            View exactView)
        {
            if (result == null || view == null)
                return;

            if (IsSameView(view, exactView))
                return;

            foreach (View existing in result)
            {
                if (IsSameView(existing, view))
                    return;
            }

            result.Add(view);
        }

        private static View FindFirstDimViewExcept(
            List<View> dimViews,
            View exceptView)
        {
            try
            {
                if (dimViews == null)
                    return null;

                foreach (View view in dimViews)
                {
                    if (view == null)
                        continue;

                    if (IsSameView(view, exceptView))
                        continue;

                    return view;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void SetDrawingPartRepresentationExact(DrawingPart dp)
        {
            try
            {
                if (dp == null)
                    return;

                object attrs = null;

                try
                {
                    attrs = dp.Attributes;
                }
                catch
                {
                    attrs = null;
                }

                // Tekla thường lưu Representation bằng mã số.
                // Theo thứ tự trong bảng: Outline = 0, Exact = 1.
                // Ưu tiên set số 1 trước, nếu từng môi trường dùng enum/string thì fallback bên dưới.
                if (attrs != null)
                {
                    TrySetRepresentationExact(attrs);

                    try
                    {
                        PropertyInfo attrProp = dp.GetType().GetProperty(
                            "Attributes",
                            BindingFlags.Public | BindingFlags.Instance
                        );

                        if (attrProp != null && attrProp.CanWrite)
                            attrProp.SetValue(dp, attrs, null);
                    }
                    catch
                    {
                    }
                }

                // Fallback: nếu object DrawingPart có property Representation trực tiếp.
                TrySetRepresentationExact(dp);

                try { dp.Modify(); }
                catch { }
            }
            catch
            {
            }
        }

        private static bool TrySetRepresentationExact(object obj)
        {
            bool changed = false;

            try
            {
                if (obj == null)
                    return false;

                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo prop in props)
                {
                    if (prop == null || !prop.CanWrite)
                        continue;

                    string name = prop.Name.ToUpper();

                    if (name.IndexOf("REPRESENTATION") < 0)
                        continue;

                    if (TrySetExactValueByPropertyType(obj, prop))
                        changed = true;
                }
            }
            catch
            {
            }

            return changed;
        }

        private static bool TrySetExactValueByPropertyType(
            object obj,
            PropertyInfo prop)
        {
            try
            {
                if (obj == null || prop == null || !prop.CanWrite)
                    return false;

                Type t = prop.PropertyType;

                if (t == typeof(int))
                {
                    prop.SetValue(obj, 1, null);
                    return true;
                }

                if (t == typeof(short))
                {
                    prop.SetValue(obj, (short)1, null);
                    return true;
                }

                if (t == typeof(long))
                {
                    prop.SetValue(obj, (long)1, null);
                    return true;
                }

                if (t == typeof(double))
                {
                    prop.SetValue(obj, 1.0, null);
                    return true;
                }

                if (t == typeof(float))
                {
                    prop.SetValue(obj, 1.0f, null);
                    return true;
                }

                if (t.IsEnum)
                {
                    try
                    {
                        object enumValue = Enum.Parse(t, "Exact", true);
                        prop.SetValue(obj, enumValue, null);
                        return true;
                    }
                    catch
                    {
                    }

                    try
                    {
                        object enumValue = Enum.ToObject(t, 1);
                        prop.SetValue(obj, enumValue, null);
                        return true;
                    }
                    catch
                    {
                    }
                }

                if (t == typeof(string))
                {
                    prop.SetValue(obj, "Exact", null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        // =====================================================================================
        // AUTO SCALE THEO KHỔ GIẤY + CHIỀU DÀI THANH - CHẠY TRƯỚC KHI DIM
        // -------------------------------------------------------------------------------------
        // Quy tắc:
        // - Lấy khổ giấy đang áp dụng từ drawing.Layout.SheetSize.
        // - A3 420x297: trừ margin 20mm.
        // - A1 841x594 hoặc khổ khác: trừ margin 30mm.
        // - Thép L: requiredLength = chiều dài dầm theo topView + 300mm reserve.
        // - Chọn scale nhỏ nhất trong {5,10,15,20,30} sao cho không vượt vùng giấy hữu dụng.
        // =====================================================================================
        private static void ApplyAutoScaleByPartLength(
            Drawing drawing,
            Model model,
            ModelPart part,
            View referenceView,
            List<View> views)
        {
            try
            {
                if (drawing == null || model == null || part == null || referenceView == null || views == null)
                    return;

                double scale;
                if (TTSK_AutoDim_Plates.ManualDrawingScaleOverride.TryGet(
                        out scale))
                {
                    LastAppliedAutoScale = scale;

                    foreach (View v in views)
                    {
                        if (v != null)
                            SetViewScale(v, scale);
                    }

                    return;
                }

                double sheetWidth;
                double sheetHeight;

                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                double paperLength = Math.Max(sheetWidth, sheetHeight);
                double margin = GetAutoScaleSheetMargin(sheetWidth, sheetHeight);
                double usablePaperLength = paperLength - margin;

                if (usablePaperLength <= 1.0)
                    return;

                double beamLength = GetBeamLengthInView(model, part, referenceView);
                if (beamLength <= 1.0)
                    return;

                double requiredLength = beamLength + AUTO_SCALE_RESERVE;
                scale = ChooseAutoScaleByRequiredLength(requiredLength, usablePaperLength);
                LastAppliedAutoScale = scale;

                foreach (View v in views)
                {
                    if (v == null)
                        continue;

                    SetViewScale(v, scale);
                }
            }
            catch
            {
            }
        }

        private static double GetBeamLengthInView(
            Model model,
            ModelPart part,
            View view)
        {
            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return 0.0;

                return Math.Abs(solid.MaximumPoint.X - solid.MinimumPoint.X);
            }
            catch
            {
                return 0.0;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static double GetAutoScaleSheetMargin(double width, double height)
        {
            if (IsSheetSize(width, height, A3_SHEET_WIDTH, A3_SHEET_HEIGHT))
                return A3_SHEET_MARGIN;

            // A1 và các khổ giấy khác dùng margin giống A1.
            return DEFAULT_SHEET_MARGIN;
        }

        private static double ChooseAutoScaleByRequiredLength(
            double requiredLength,
            double usablePaperLength)
        {
            if (requiredLength <= 0.0 || usablePaperLength <= 0.0)
                return 30.0;

            double requiredScale = requiredLength / usablePaperLength;
            double[] allowedScales = new double[] { 5.0, 10.0, 15.0, 20.0, 30.0 };

            foreach (double scale in allowedScales)
            {
                if (scale >= requiredScale)
                    return scale;
            }

            return 30.0;
        }

        private static bool IsSheetSize(
            double width,
            double height,
            double targetWidth,
            double targetHeight)
        {
            return
                (Math.Abs(width - targetWidth) <= SHEET_SIZE_TOLERANCE &&
                 Math.Abs(height - targetHeight) <= SHEET_SIZE_TOLERANCE) ||
                (Math.Abs(width - targetHeight) <= SHEET_SIZE_TOLERANCE &&
                 Math.Abs(height - targetWidth) <= SHEET_SIZE_TOLERANCE);
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

                    // Một số Tekla cần gán Attributes lại mới nhận Modify.
                    try
                    {
                        PropertyInfo attrProp = view.GetType().GetProperty(
                            "Attributes",
                            BindingFlags.Public | BindingFlags.Instance
                        );

                        if (attrProp != null && attrProp.CanWrite)
                            attrProp.SetValue(view, attrs, null);
                    }
                    catch
                    {
                    }
                }

                // Nếu View có Scale trực tiếp thì set luôn.
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
                    if (!prop.CanWrite && !prop.CanRead)
                        continue;

                    string name = prop.Name.ToUpper();

                    if (name.IndexOf("SCALE") < 0)
                        continue;

                    try
                    {
                        Type t = prop.PropertyType;

                        if (prop.CanWrite && t == typeof(double))
                        {
                            prop.SetValue(obj, scale, null);
                        }
                        else if (prop.CanWrite && t == typeof(int))
                        {
                            prop.SetValue(obj, Convert.ToInt32(scale), null);
                        }
                        else if (prop.CanWrite && t == typeof(float))
                        {
                            prop.SetValue(obj, Convert.ToSingle(scale), null);
                        }
                        else
                        {
                            object scaleObj = null;

                            if (prop.CanRead)
                                scaleObj = prop.GetValue(obj, null);

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

                Type t = prop.PropertyType;

                if (t == typeof(double))
                    prop.SetValue(obj, Convert.ToDouble(value), null);
                else if (t == typeof(float))
                    prop.SetValue(obj, Convert.ToSingle(value), null);
                else if (t == typeof(int))
                    prop.SetValue(obj, Convert.ToInt32(value), null);
                else
                    prop.SetValue(obj, value, null);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AlignMainViewsByGeometry(
            View topView,
            TopBoundary topBoundary,
            View frontView,
            TopBoundary frontBoundary)
        {
            try
            {
                if (topView == null || frontView == null)
                    return;

                double topLeft;
                double topBottom;
                double frontLeft;
                double frontTop;

                if (!TryGetGeometryLeftEdge(topView, topBoundary, out topLeft))
                    return;

                if (!TryGetGeometryBottomEdge(topView, topBoundary, out topBottom))
                    return;

                if (!TryGetGeometryLeftEdge(frontView, frontBoundary, out frontLeft))
                    return;

                if (!TryGetGeometryTopEdge(frontView, frontBoundary, out frontTop))
                    return;

                double scale = GetCurrentDrawingScale(topView);
                if (scale <= 0.0)
                    scale = 1.0;

                double beamLength = 0.0;
                if (topBoundary.IsValid)
                    beamLength = Math.Abs(topBoundary.MaxX - topBoundary.MinX);
                if (beamLength <= 0.0 && frontBoundary.IsValid)
                    beamLength = Math.Abs(frontBoundary.MaxX - frontBoundary.MinX);

                double shortScale = GetDimScaleByBeamLength(beamLength);
                double gap =
                    (
                        GetSteelDimOffsetByTier(LastTopMaxDimTier)
                        + GetSteelDimOffsetByTier(LastFrontMaxDimTier)
                    ) * 0.8;

                Point topOrigin = topView.Origin;
                Point frontOrigin = frontView.Origin;
                if (topOrigin == null || frontOrigin == null)
                    return;

                // Canh trái theo mép hình học: Front.Left = Top.Left
                double targetSheetLeft = topOrigin.X + topLeft / scale;
                double currentSheetLeft = frontOrigin.X + frontLeft / scale;
                double deltaX = targetSheetLeft - currentSheetLeft;

                // Tự arrange dọc: Front nằm dưới Top, khoảng hở theo hình học = 400 hoặc 200 nếu dầm ngắn.
                double targetSheetFrontTop = topOrigin.Y + topBottom / scale - gap / scale;
                double currentSheetFrontTop = frontOrigin.Y + frontTop / scale;
                double deltaY = targetSheetFrontTop - currentSheetFrontTop;

                if (Math.Abs(deltaX) <= 0.01 && Math.Abs(deltaY) <= 0.01)
                    return;

                Point newOrigin = new Point(
                    frontOrigin.X + deltaX,
                    frontOrigin.Y + deltaY,
                    frontOrigin.Z
                );

                if (TrySetViewOrigin(frontView, newOrigin))
                {
                    try { frontView.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private static void ArrangeSectionViewRightOfFront(
            View sectionView,
            View frontView,
            TopBoundary frontBoundary,
            TopBoundary topBoundary,
            double greenBoxGap)
        {
            try
            {
                if (sectionView == null || frontView == null)
                    return;

                ViewPaperBoxForGreenArrange frontGreenBox;
                ViewPaperBoxForGreenArrange sectionGreenBox;
                if (TryGetViewGreenPaperBoxForShapeL(frontView, out frontGreenBox) &&
                    TryGetViewGreenPaperBoxForShapeL(sectionView, out sectionGreenBox))
                {
                    if (greenBoxGap < 0.0)
                        greenBoxGap = 0.0;

                    Point greenFrontOrigin = frontView.Origin;
                    Point greenSectionOrigin = sectionView.Origin;
                    if (greenFrontOrigin == null || greenSectionOrigin == null)
                        return;

                    double greenDeltaX =
                        frontGreenBox.MaxX + greenBoxGap - sectionGreenBox.MinX;
                    double greenDeltaY =
                        greenFrontOrigin.Y - greenSectionOrigin.Y;

                    if (Math.Abs(greenDeltaX) > 0.01 ||
                        Math.Abs(greenDeltaY) > 0.01)
                    {
                        MoveViewBySheetDelta(
                            sectionView,
                            greenDeltaX,
                            greenDeltaY);
                    }

                    return;
                }

                double frontRight;
                double frontBottom;
                double sectionLeft;
                double sectionBottom;

                if (!TryGetGeometryRightEdge(frontView, frontBoundary, out frontRight))
                    return;

                if (!TryGetGeometryBottomEdge(frontView, frontBoundary, out frontBottom))
                    return;

                TopBoundary sectionBoundary = new TopBoundary();

                // SECTION A-A / EXACT VIEW:
                // Không dùng nhánh DrawingPart bounding box riêng nữa vì dễ lệch hệ tọa độ so với Front.
                // Dùng cùng kiểu mép hình học đang align Top/Front: ưu tiên biên dạng view thật sau khi Tekla tạo Exact.
                if (!TryGetExactSectionGeometryBoundary(sectionView, out sectionBoundary))
                    TryGetDrawingPartGeometryBoundary(sectionView, out sectionBoundary);

                if (!TryGetGeometryLeftEdge(sectionView, sectionBoundary, out sectionLeft))
                    return;

                if (!TryGetGeometryBottomEdge(sectionView, sectionBoundary, out sectionBottom))
                    return;

                double scale = GetCurrentDrawingScale(frontView);
                if (scale <= 0.0)
                    scale = 1.0;

                double beamLength = 0.0;
                if (topBoundary.IsValid)
                    beamLength = Math.Abs(topBoundary.MaxX - topBoundary.MinX);
                if (beamLength <= 0.0 && frontBoundary.IsValid)
                    beamLength = Math.Abs(frontBoundary.MaxX - frontBoundary.MinX);

                double shortScale = GetDimScaleByBeamLength(beamLength);
                double gap = GetSteelDimOffsetByTier(LastFrontMaxDimTier)
                    + 100.0 * shortScale;

                Point frontOrigin = frontView.Origin;
                Point sectionOrigin = sectionView.Origin;
                if (frontOrigin == null || sectionOrigin == null)
                    return;

                double targetSheetLeft = frontOrigin.X + frontRight / scale + gap / scale;
                double currentSheetLeft = sectionOrigin.X + sectionLeft / scale;
                double deltaX = targetSheetLeft - currentSheetLeft;

                // SECTION A-A: theo yêu cầu test mới, không canh Y theo mép hình học nữa.
                // Giữ X theo mép hình học + gap, nhưng canh thẳng hàng theo Origin.Y của Front.
                double deltaY = frontOrigin.Y - sectionOrigin.Y;

                if (Math.Abs(deltaX) <= 0.01 && Math.Abs(deltaY) <= 0.01)
                    return;

                Point newOrigin = new Point(
                    sectionOrigin.X + deltaX,
                    sectionOrigin.Y + deltaY,
                    sectionOrigin.Z
                );

                if (TrySetViewOrigin(sectionView, newOrigin))
                {
                    try { sectionView.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private static bool TryGetExactSectionGeometryBoundary(
            View view,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();

            try
            {
                if (view == null || view.RestrictionBox == null)
                    return false;

                Point minP = view.RestrictionBox.MinPoint;
                Point maxP = view.RestrictionBox.MaxPoint;

                if (minP == null || maxP == null)
                    return false;

                double minX = Math.Min(minP.X, maxP.X) + VIEW_PADDING;
                double maxX = Math.Max(minP.X, maxP.X) - VIEW_PADDING;
                double minY = Math.Min(minP.Y, maxP.Y) + VIEW_PADDING;
                double maxY = Math.Max(minP.Y, maxP.Y) - VIEW_PADDING;

                if (maxX <= minX + 0.1 || maxY <= minY + 0.1)
                {
                    minX = Math.Min(minP.X, maxP.X);
                    maxX = Math.Max(minP.X, maxP.X);
                    minY = Math.Min(minP.Y, maxP.Y);
                    maxY = Math.Max(minP.Y, maxP.Y);
                }

                if (maxX <= minX + 0.1 || maxY <= minY + 0.1)
                    return false;

                boundary.IsValid = true;
                boundary.MinX = minX;
                boundary.MaxX = maxX;
                boundary.MinY = minY;
                boundary.MaxY = maxY;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDrawingPartGeometryBoundary(
            View view,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();

            try
            {
                if (view == null)
                    return false;

                DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                if (parts == null)
                    return false;

                bool found = false;
                double minX = 999999999.0;
                double maxX = -999999999.0;
                double minY = 999999999.0;
                double maxY = -999999999.0;

                while (parts.MoveNext())
                {
                    DrawingObject partObj = parts.Current as DrawingObject;
                    if (partObj == null)
                        continue;

                    object box = TryInvokeNoArg(partObj, "GetAxisAlignedBoundingBox");
                    if (box == null) box = TryInvokeNoArg(partObj, "GetObjectAlignedBoundingBox");
                    if (box == null) box = TryInvokeNoArg(partObj, "GetBoundingBox");
                    if (box == null)
                        continue;

                    Point minP = TryGetPointProperty(box, "MinPoint");
                    Point maxP = TryGetPointProperty(box, "MaxPoint");
                    if (minP == null || maxP == null)
                        continue;

                    double x1 = Math.Min(minP.X, maxP.X);
                    double x2 = Math.Max(minP.X, maxP.X);
                    double y1 = Math.Min(minP.Y, maxP.Y);
                    double y2 = Math.Max(minP.Y, maxP.Y);

                    // Chỉ lấy hình học part, bỏ qua title/label nên mặt cắt A-A canh đúng profile.
                    if (x2 <= x1 + 0.1 || y2 <= y1 + 0.1)
                        continue;

                    if (x1 < minX) minX = x1;
                    if (x2 > maxX) maxX = x2;
                    if (y1 < minY) minY = y1;
                    if (y2 > maxY) maxY = y2;
                    found = true;
                }

                if (!found)
                    return false;

                boundary.IsValid = true;
                boundary.MinX = minX;
                boundary.MaxX = maxX;
                boundary.MinY = minY;
                boundary.MaxY = maxY;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetGeometryTopEdge(
            View view,
            TopBoundary boundary,
            out double topEdge)
        {
            topEdge = 0.0;

            try
            {
                if (boundary.IsValid)
                {
                    topEdge = boundary.MaxY;
                    return true;
                }

                TopBoundary partBoundary;
                if (TryGetDrawingPartGeometryBoundary(view, out partBoundary))
                {
                    topEdge = partBoundary.MaxY;
                    return true;
                }

                if (view != null &&
                    view.RestrictionBox != null &&
                    view.RestrictionBox.MaxPoint != null)
                {
                    topEdge = view.RestrictionBox.MaxPoint.Y - VIEW_PADDING;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetGeometryRightEdge(
            View view,
            TopBoundary boundary,
            out double rightEdge)
        {
            rightEdge = 0.0;

            try
            {
                if (boundary.IsValid)
                {
                    rightEdge = boundary.MaxX;
                    return true;
                }

                TopBoundary partBoundary;
                if (TryGetDrawingPartGeometryBoundary(view, out partBoundary))
                {
                    rightEdge = partBoundary.MaxX;
                    return true;
                }

                if (view != null &&
                    view.RestrictionBox != null &&
                    view.RestrictionBox.MaxPoint != null)
                {
                    rightEdge = view.RestrictionBox.MaxPoint.X - VIEW_PADDING;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetGeometryBottomEdge(
            View view,
            TopBoundary boundary,
            out double bottomEdge)
        {
            bottomEdge = 0.0;

            try
            {
                if (boundary.IsValid)
                {
                    bottomEdge = boundary.MinY;
                    return true;
                }

                TopBoundary partBoundary;
                if (TryGetDrawingPartGeometryBoundary(view, out partBoundary))
                {
                    bottomEdge = partBoundary.MinY;
                    return true;
                }

                if (view != null &&
                    view.RestrictionBox != null &&
                    view.RestrictionBox.MinPoint != null)
                {
                    bottomEdge = view.RestrictionBox.MinPoint.Y + VIEW_PADDING;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetGeometryLeftEdge(
            View view,
            TopBoundary boundary,
            out double leftEdge)
        {
            leftEdge = 0.0;

            try
            {
                if (boundary.IsValid)
                {
                    leftEdge = boundary.MinX;
                    return true;
                }

                TopBoundary partBoundary;
                if (TryGetDrawingPartGeometryBoundary(view, out partBoundary))
                {
                    leftEdge = partBoundary.MinX;
                    return true;
                }

                if (view != null &&
                    view.RestrictionBox != null &&
                    view.RestrictionBox.MinPoint != null)
                {
                    leftEdge = view.RestrictionBox.MinPoint.X + VIEW_PADDING;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetViewOrigin(View view, Point origin)
        {
            try
            {
                if (view == null || origin == null)
                    return false;

                PropertyInfo prop = view.GetType().GetProperty(
                    "Origin",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanWrite)
                    return false;

                prop.SetValue(view, origin, null);
                return true;
            }
            catch
            {
                return false;
            }
        }



        private static void ForceFinalEqualArrangeShapeTopFrontGap15(
            View topView,
            View frontView,
            double gap)
        {
            try
            {
                List<View> stackViews = new List<View>();
                AddUniqueViewForGreenArrange(stackViews, topView);
                AddUniqueViewForGreenArrange(stackViews, frontView);

                if (stackViews.Count < 2)
                    return;

                List<ViewPaperBoxForGreenArrange> boxes = new List<ViewPaperBoxForGreenArrange>();
                foreach (View v in stackViews)
                {
                    ViewPaperBoxForGreenArrange b;
                    // ARRANGE dùng khung xanh/view frame để gap có tính cả DIM/mark.
                    if (TryGetViewGreenPaperBoxForShapeL(v, out b))
                    {
                        if (b != null && b.Width > 1.0 && b.Height > 1.0)
                            boxes.Add(b);
                    }
                }

                if (boxes.Count < 2)
                    return;

                boxes.Sort(delegate (ViewPaperBoxForGreenArrange a, ViewPaperBoxForGreenArrange b)
                {
                    return b.CenterY.CompareTo(a.CenterY);
                });

                double totalHeight = 0.0;
                foreach (ViewPaperBoxForGreenArrange b in boxes)
                    totalHeight += b.Height;

                double currentMinY = double.MaxValue;
                double currentMaxY = double.MinValue;
                foreach (ViewPaperBoxForGreenArrange b in boxes)
                {
                    if (b.MinY < currentMinY) currentMinY = b.MinY;
                    if (b.MaxY > currentMaxY) currentMaxY = b.MaxY;
                }

                double currentCenter = (currentMinY + currentMaxY) * 0.5;
                double totalStackHeight = totalHeight + gap * (boxes.Count - 1);
                double cursorMaxY = currentCenter + totalStackHeight * 0.5;

                foreach (ViewPaperBoxForGreenArrange b in boxes)
                {
                    double desiredMaxY = cursorMaxY;
                    double desiredMinY = desiredMaxY - b.Height;
                    double desiredCenterY = (desiredMinY + desiredMaxY) * 0.5;
                    double currentCenterY = (b.MinY + b.MaxY) * 0.5;
                    double dy = desiredCenterY - currentCenterY;

                    if (Math.Abs(dy) > 300.0)
                        return;

                    MoveViewBySheetDelta(b.View, 0.0, dy);
                    cursorMaxY = desiredMinY - gap;
                }
            }
            catch
            {
            }
        }

        private static void AddUniqueViewForGreenArrange(List<View> views, View view)
        {
            try
            {
                if (views == null || view == null)
                    return;

                foreach (View v in views)
                {
                    if (System.Object.ReferenceEquals(v, view))
                        return;
                }

                views.Add(view);
            }
            catch
            {
            }
        }

        private class ViewPaperBoxForGreenArrange
        {
            public View View;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double Width;
            public double Height;
            public double CenterY;
        }

        private static bool TryGetViewGreenPaperBoxForShapeL(
            View view,
            out ViewPaperBoxForGreenArrange box)
        {
            box = null;

            try
            {
                if (view == null)
                    return false;

                AABB bb = view.GetAxisAlignedBoundingBox();

                if (bb == null || bb.MinPoint == null || bb.MaxPoint == null)
                    return false;

                Point min = bb.MinPoint;
                Point max = bb.MaxPoint;

                box = new ViewPaperBoxForGreenArrange();
                box.View = view;
                box.MinX = Math.Min(min.X, max.X);
                box.MaxX = Math.Max(min.X, max.X);
                box.MinY = Math.Min(min.Y, max.Y);
                box.MaxY = Math.Max(min.Y, max.Y);
                box.Width = Math.Abs(box.MaxX - box.MinX);
                box.Height = Math.Abs(box.MaxY - box.MinY);
                box.CenterY = (box.MinY + box.MaxY) * 0.5;

                return box.Width > 0.5 && box.Height > 0.5;
            }
            catch
            {
                return false;
            }
        }

        private static void CenterViewGroupOnSheet(
            Drawing drawing,
            View topView,
            TopBoundary topBoundary,
            View frontView,
            TopBoundary frontBoundary,
            View sectionView)
        {
            try
            {
                if (drawing == null || topView == null)
                    return;

                double sheetWidth;
                double sheetHeight;
                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                double margin = GetAutoScaleSheetMargin(sheetWidth, sheetHeight);
                double sheetCenterX = sheetWidth * 0.5;
                double sheetCenterY = sheetHeight * 0.5;

                // Nếu Tekla trả ngược khổ giấy đứng/ngang thì vẫn lấy tâm theo khổ đang áp dụng.
                // Margin chỉ dùng để chặn trường hợp tâm vùng hữu dụng bị sai khi cần mở rộng sau này.
                double usableMinX = margin * 0.5;
                double usableMaxX = sheetWidth - margin * 0.5;
                double usableMinY = margin * 0.5;
                double usableMaxY = sheetHeight - margin * 0.5;

                if (usableMaxX > usableMinX && usableMaxY > usableMinY)
                {
                    sheetCenterX = (usableMinX + usableMaxX) * 0.5;
                    sheetCenterY = (usableMinY + usableMaxY) * 0.5;
                }

                double clusterMinX = 999999999.0;
                double clusterMaxX = -999999999.0;
                double clusterMinY = 999999999.0;
                double clusterMaxY = -999999999.0;

                bool hasAny = false;

                AddViewSheetBoundsToCluster(
                    topView,
                    topBoundary,
                    ref clusterMinX,
                    ref clusterMaxX,
                    ref clusterMinY,
                    ref clusterMaxY,
                    ref hasAny
                );

                AddViewSheetBoundsToCluster(
                    frontView,
                    frontBoundary,
                    ref clusterMinX,
                    ref clusterMaxX,
                    ref clusterMinY,
                    ref clusterMaxY,
                    ref hasAny
                );

                if (sectionView != null)
                {
                    TopBoundary sectionBoundary = new TopBoundary();
                    if (!TryGetExactSectionGeometryBoundary(sectionView, out sectionBoundary))
                        TryGetDrawingPartGeometryBoundary(sectionView, out sectionBoundary);

                    AddViewSheetBoundsToCluster(
                        sectionView,
                        sectionBoundary,
                        ref clusterMinX,
                        ref clusterMaxX,
                        ref clusterMinY,
                        ref clusterMaxY,
                        ref hasAny
                    );
                }

                if (!hasAny)
                    return;

                double clusterCenterX = (clusterMinX + clusterMaxX) * 0.5;
                double clusterCenterY = (clusterMinY + clusterMaxY) * 0.5;

                double dx = sheetCenterX - clusterCenterX;
                double dy = sheetCenterY - clusterCenterY;

                if (Math.Abs(dx) <= 0.01 && Math.Abs(dy) <= 0.01)
                    return;

                MoveViewBySheetDelta(topView, dx, dy);
                MoveViewBySheetDelta(frontView, dx, dy);
                MoveViewBySheetDelta(sectionView, dx, dy);
            }
            catch
            {
            }
        }

        private static void AddViewSheetBoundsToCluster(
            View view,
            TopBoundary boundary,
            ref double clusterMinX,
            ref double clusterMaxX,
            ref double clusterMinY,
            ref double clusterMaxY,
            ref bool hasAny)
        {
            try
            {
                double minX;
                double maxX;
                double minY;
                double maxY;

                if (!TryGetViewSheetBounds(view, boundary, out minX, out maxX, out minY, out maxY))
                    return;

                if (minX < clusterMinX) clusterMinX = minX;
                if (maxX > clusterMaxX) clusterMaxX = maxX;
                if (minY < clusterMinY) clusterMinY = minY;
                if (maxY > clusterMaxY) clusterMaxY = maxY;

                hasAny = true;
            }
            catch
            {
            }
        }

        private static bool TryGetViewSheetBounds(
            View view,
            TopBoundary boundary,
            out double minSheetX,
            out double maxSheetX,
            out double minSheetY,
            out double maxSheetY)
        {
            minSheetX = 0.0;
            maxSheetX = 0.0;
            minSheetY = 0.0;
            maxSheetY = 0.0;

            try
            {
                if (view == null || view.Origin == null)
                    return false;

                double scale = GetCurrentDrawingScale(view);
                if (scale <= 0.0)
                    scale = 1.0;

                double minX = 0.0;
                double maxX = 0.0;
                double minY = 0.0;
                double maxY = 0.0;
                bool ok = false;

                // Ưu tiên RestrictionBox vì đây là khung view sau khi resize.
                // Nếu không có thì fallback về mép hình học đã dùng để align.
                try
                {
                    if (view.RestrictionBox != null &&
                        view.RestrictionBox.MinPoint != null &&
                        view.RestrictionBox.MaxPoint != null)
                    {
                        minX = Math.Min(view.RestrictionBox.MinPoint.X, view.RestrictionBox.MaxPoint.X);
                        maxX = Math.Max(view.RestrictionBox.MinPoint.X, view.RestrictionBox.MaxPoint.X);
                        minY = Math.Min(view.RestrictionBox.MinPoint.Y, view.RestrictionBox.MaxPoint.Y);
                        maxY = Math.Max(view.RestrictionBox.MinPoint.Y, view.RestrictionBox.MaxPoint.Y);
                        ok = true;
                    }
                }
                catch
                {
                    ok = false;
                }

                if (!ok)
                {
                    double left;
                    double right;
                    double bottom;
                    double top;

                    if (!TryGetGeometryLeftEdge(view, boundary, out left))
                        return false;
                    if (!TryGetGeometryRightEdge(view, boundary, out right))
                        return false;
                    if (!TryGetGeometryBottomEdge(view, boundary, out bottom))
                        return false;
                    if (!TryGetGeometryTopEdge(view, boundary, out top))
                        return false;

                    minX = Math.Min(left, right) - VIEW_PADDING;
                    maxX = Math.Max(left, right) + VIEW_PADDING;
                    minY = Math.Min(bottom, top) - VIEW_PADDING;
                    maxY = Math.Max(bottom, top) + VIEW_PADDING;
                }

                Point origin = view.Origin;

                minSheetX = origin.X + minX / scale;
                maxSheetX = origin.X + maxX / scale;
                minSheetY = origin.Y + minY / scale;
                maxSheetY = origin.Y + maxY / scale;

                if (maxSheetX < minSheetX)
                {
                    double t = minSheetX;
                    minSheetX = maxSheetX;
                    maxSheetX = t;
                }

                if (maxSheetY < minSheetY)
                {
                    double t = minSheetY;
                    minSheetY = maxSheetY;
                    maxSheetY = t;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MoveViewBySheetDelta(View view, double dx, double dy)
        {
            try
            {
                if (view == null || view.Origin == null)
                    return;

                Point origin = view.Origin;
                Point newOrigin = new Point(
                    origin.X + dx,
                    origin.Y + dy,
                    origin.Z
                );

                if (TrySetViewOrigin(view, newOrigin))
                {
                    try { view.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private static void UpdateDrawingTitle3Scale(Drawing drawing, View referenceView)
        {
            try
            {
                if (drawing == null)
                    return;

                double scale = GetCurrentDrawingScale(referenceView);
                if (scale <= 0.0)
                    return;

                string scaleText = "1:" + Convert.ToInt32(Math.Round(scale)).ToString();

                bool changed = false;

                object attrs = null;
                PropertyInfo attrProp = null;

                try
                {
                    attrProp = drawing.GetType().GetProperty(
                        "Attributes",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                    if (attrProp != null && attrProp.GetIndexParameters().Length == 0 && attrProp.CanRead)
                        attrs = attrProp.GetValue(drawing, null);
                }
                catch
                {
                    attrs = null;
                    attrProp = null;
                }

                if (attrs != null)
                {
                    changed = SetTitle3Text(attrs, scaleText) || changed;

                    try
                    {
                        if (attrProp != null && attrProp.CanWrite)
                            attrProp.SetValue(drawing, attrs, null);
                    }
                    catch
                    {
                    }
                }

                changed = SetTitle3Text(drawing, scaleText) || changed;

                if (changed)
                {
                    try { drawing.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private static bool SetTitle3Text(object obj, string scaleText)
        {
            bool changed = false;

            try
            {
                if (obj == null || string.IsNullOrEmpty(scaleText))
                    return false;

                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo prop in props)
                {
                    if (prop == null || !prop.CanWrite)
                        continue;

                    string name = prop.Name.ToUpper();
                    if (name.IndexOf("TITLE") < 0 || name.IndexOf("3") < 0)
                        continue;

                    if (prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(obj, scaleText, null);
                        changed = true;
                    }
                }
            }
            catch
            {
            }

            return changed;
        }

        private static double GetCurrentDrawingScale(View referenceView)
        {
            try
            {
                if (LastAppliedAutoScale > 0.0)
                    return LastAppliedAutoScale;

                double scale = TryGetViewScale(referenceView);
                if (scale > 0.0)
                    return scale;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double TryGetViewScale(View view)
        {
            try
            {
                if (view == null)
                    return 0.0;

                double scale = TryGetScaleFromObject(view);
                if (scale > 0.0)
                    return scale;

                object attrs = null;
                try { attrs = view.Attributes; }
                catch { attrs = null; }

                scale = TryGetScaleFromObject(attrs);
                if (scale > 0.0)
                    return scale;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double TryGetScaleFromObject(object obj)
        {
            try
            {
                if (obj == null)
                    return 0.0;

                PropertyInfo[] props = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );

                foreach (PropertyInfo prop in props)
                {
                    if (prop == null || !prop.CanRead)
                        continue;

                    string name = prop.Name.ToUpper();
                    if (name.IndexOf("SCALE") < 0)
                        continue;

                    object value = prop.GetValue(obj, null);
                    double direct;
                    if (TryConvertScaleValue(value, out direct) && direct > 0.0)
                        return direct;

                    if (value != null)
                    {
                        object denominator = TryGetObjectProperty(value, "Denominator");
                        double den;
                        if (TryConvertScaleValue(denominator, out den) && den > 0.0)
                            return den;

                        object y = TryGetObjectProperty(value, "Y");
                        double yy;
                        if (TryConvertScaleValue(y, out yy) && yy > 0.0)
                            return yy;
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static bool TryConvertScaleValue(object value, out double result)
        {
            result = 0.0;

            try
            {
                if (value == null)
                    return false;

                if (value is double || value is float || value is int || value is short || value is long)
                {
                    result = Convert.ToDouble(value);
                    return result > 0.0;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void ResizeViewBoundaryKeepDepth(
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (UseSelectedMainPartMode)
                return;

            try
            {
                AABB oldBox = view.RestrictionBox;

                if (oldBox == null ||
                    oldBox.MinPoint == null ||
                    oldBox.MaxPoint == null)
                    return;

                Point newMin = new Point(
                    minX - VIEW_PADDING,
                    minY - VIEW_PADDING,
                    oldBox.MinPoint.Z
                );

                Point newMax = new Point(
                    maxX + VIEW_PADDING,
                    maxY + VIEW_PADDING,
                    oldBox.MaxPoint.Z
                );

                view.RestrictionBox = new AABB(newMin, newMax);
                view.Modify();
            }
            catch
            {
            }
        }

        // =====================================================================================
        // FRONT VIEW DIM LOGIC V2
        // -------------------------------------------------------------------------------------
        // - DIM tổng FRONT luôn tầng 1.
        // - Tất cả DIM lỗ FRONT dùng offset 150.
        // - Chân DIM lỗ không dùng cố định 22, mà dùng đúng phi lỗ đọc từ bolt group.
        // - DIM dọc cụm lỗ mép: chain từ mép flange dưới tới mép flange trên,
        //   chân DIM tại lỗ dịch ngang khỏi tâm lỗ đúng bằng phi lỗ.
        // - DIM ngang cụm lỗ mép: bắt đúng tâm lỗ đầu tiên theo phương X,
        //   nhưng chân DIM dịch dọc khỏi tâm lỗ đúng bằng phi lỗ.
        // =====================================================================================
        // THÉP C - FRONT VIEW
        // - Top/Bottom giữ nguyên theo thuật toán Shape hiện tại.
        // - Front View không dùng thuật toán lỗ mép của I/H.
        // - Front View dùng nguồn coordinate trực tiếp theo view, không dùng mặt cắt giữa.
        // - Lỗ Front luôn chạy theo logic chain của Top View.
        // =====================================================================================
        private static int CreateDimsForFrontView(
            Model model,
            ModelPart part,
            View view,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();
            int count = 0;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;

                double minX = min.X;
                double maxX = max.X;
                double minY = min.Y;
                double maxY = max.Y;

                // THÉP C - FRONT dùng coordinate trực tiếp theo view.
                // Không dùng GetFrontWebFacePolygon() vì hàm đó cắt mặt giữa theo Z cho I/H.
                List<Point> frontPolygon = GetProjectedSolidPointsForFrontNotchDims(solid);
                if (frontPolygon == null || frontPolygon.Count < 2)
                    frontPolygon = GetTopFacePolygon(solid, min, max);

                if (frontPolygon != null && frontPolygon.Count >= 2)
                {
                    GetMinMax(frontPolygon, out minX, out maxX, out minY, out maxY);
                }
                else
                {
                    frontPolygon = new List<Point>();
                    frontPolygon.Add(new Point(minX, minY, 0));
                    frontPolygon.Add(new Point(maxX, minY, 0));
                    frontPolygon.Add(new Point(maxX, maxY, 0));
                    frontPolygon.Add(new Point(minX, maxY, 0));
                }

                List<Point> frontProjectedSolidPoints = frontPolygon;

                ChamferEdgeAnchors frontEdgeAnchors = BuildChamferEdgeAnchors(frontPolygon, minX, maxX, minY, maxY);
                DimOffsetAnchor4 frontOffsetAnchors =
                    BuildDimOffsetAnchor4(frontEdgeAnchors);

                boundary.IsValid = true;
                boundary.MinX = minX;
                boundary.MaxX = maxX;
                boundary.MinY = minY;
                boundary.MaxY = maxY;

                double beamLength = Math.Abs(maxX - minX);

                // THÉP L - FRONT VIEW:
                // Chỉ nhận lỗ từ cạnh XANH đi vào đúng 1 độ dày cánh đứng.
                // Không dùng mặt cắt. Không dùng lỗ đơn; toàn bộ đi theo chain cụm.
                double lLegThickness = GetLThicknessFromProfile(part);
                if (lLegThickness <= 0.0)
                    lLegThickness = 6.0;

                // THÉP L - FRONT VIEW:
                // Coordinate trực tiếp nhưng GIỚI HẠN VÙNG THẤY LỖ theo 1 độ dày.
                // Chuẩn: từ cạnh XANH đi vào đúng 1 độ dày cánh đứng.
                // Không lấy toàn bộ visible bolts nữa để tránh bắt nhầm mặt Top.
                List<Point> frontHoles =
                    GetVisibleLFrontBoltCentersFromGreenStrip(
                        model,
                        view,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        max.Z,
                        lLegThickness
                    );

                // Một số view L có chiều sâu bị đảo theo DisplayCoordinateSystem.
                // Vẫn giữ đúng nguyên tắc "1 độ dày từ cạnh xanh", chỉ thử cạnh đối diện nếu cạnh chuẩn không có lỗ.
                if (frontHoles.Count == 0)
                {
                    frontHoles =
                        GetVisibleLFrontBoltCentersFromGreenStrip(
                            model,
                            view,
                            minX,
                            maxX,
                            minY,
                            maxY,
                            min.Z,
                            lLegThickness
                        );
                }

                StraightDimensionSetHandler handler =
                    new StraightDimensionSetHandler();

                LDimTierManager tierManager = new LDimTierManager();

                Point frontNotchTop;
                Point frontNotchThicknessEdge;
                Point frontNotchBottomEdge;
                bool hasFrontRightNotch = TryGetLFrontEndNotchGeometry(
                    frontPolygon,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    lLegThickness,
                    false,
                    out frontNotchTop,
                    out frontNotchThicknessEdge,
                    out frontNotchBottomEdge
                );

                Point frontLeftNotchTop;
                Point frontLeftNotchThicknessEdge;
                Point frontLeftNotchBottomEdge;
                bool hasFrontLeftNotch = TryGetLFrontEndNotchGeometry(
                    frontPolygon,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    lLegThickness,
                    true,
                    out frontLeftNotchTop,
                    out frontLeftNotchThicknessEdge,
                    out frontLeftNotchBottomEdge
                );

                ChamferInfluence frontChamferInfluence = new ChamferInfluence();
                int frontChamferCount = 0;

                if (ENABLE_TOP_VIEW_CHAMFER_DIM)
                {
                    frontChamferCount = CreateTopViewChamferDims(
                        handler,
                        view,
                        frontPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        beamLength,
                        out frontChamferInfluence,
                        hasFrontRightNotch ? frontNotchTop : null,
                        hasFrontRightNotch ? frontNotchThicknessEdge : null,
                        hasFrontLeftNotch ? frontLeftNotchTop : null,
                        hasFrontLeftNotch ? frontLeftNotchThicknessEdge : null
                    );

                    if (frontChamferCount > 0)
                        frontChamferInfluence.Any = true;

                    count += frontChamferCount;
                }

                ChamferInfluence frontNotchInfluence = new ChamferInfluence();
                int frontNotchCount = 0;

                if (frontNotchCount > 0)
                {
                    MergeInfluence(ref frontChamferInfluence, frontNotchInfluence);
                    count += frontNotchCount;
                }

                if (hasFrontRightNotch)
                {
                    count += CreateLFrontEndNotchDims(
                        handler,
                        view,
                        frontNotchTop,
                        frontNotchThicknessEdge,
                        frontOffsetAnchors,
                        frontNotchBottomEdge,
                        beamLength,
                        tierManager,
                        false
                    );
                }

                if (hasFrontLeftNotch)
                {
                    count += CreateLFrontEndNotchDims(
                        handler,
                        view,
                        frontLeftNotchTop,
                        frontLeftNotchThicknessEdge,
                        frontOffsetAnchors,
                        frontLeftNotchBottomEdge,
                        beamLength,
                        tierManager,
                        true
                    );
                }

                bool holeDimCreated = false;
                bool horizontalHoleDimCreated = false;
                bool verticalHoleDimCreated = false;
                int frontHoleTierCount = 0;

                if (frontHoles != null && frontHoles.Count > 0)
                {
                    // THÉP L - FRONT: toàn bộ lỗ dùng chain cụm, không dùng lỗ đơn.
                    int holeCount = CreateLHoleChainDimsEdgeToEdge(
                        handler,
                        view,
                        frontHoles,
                        frontOffsetAnchors,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        beamLength,
                        tierManager,
                        out frontHoleTierCount,
                        out horizontalHoleDimCreated,
                        out verticalHoleDimCreated
                    );

                    holeDimCreated = holeCount > 0;
                    count += holeCount;
                }

                // THÉP L - TẦNG ĐỘC LẬP 4 HƯỚNG:
                // Top / Bottom / Left / Right có bộ đếm riêng cho từng view.
                // DIM lỗ đã chiếm tầng qua tierManager trong CreateLHoleChainDimsEdgeToEdge.
                // DIM tổng lấy tầng kế tiếp của đúng hướng, không dùng chung tầng với DIM lỗ.
                int frontHorizontalTotalTier = tierManager.ReserveTop();
                int frontVerticalTotalTier = tierManager.ReserveLeft();

                LastFrontMaxDimTier = Math.Max(frontHorizontalTotalTier, frontVerticalTotalTier);

                count += CreateFrontTotalDims(
                    handler,
                    view,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    frontEdgeAnchors,
                    frontOffsetAnchors,
                    GetSteelDimOffsetByTier(frontHorizontalTotalTier),
                    GetSteelDimOffsetByTier(frontVerticalTotalTier)
                );
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


        // THÉP L - HOLE CHAIN DIM
        // Tất cả lỗ dùng chain mép - lỗ - mép, không dùng lỗ đơn / mép gần nhất.
        private static int CreateLHoleChainDimsEdgeToEdge(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength,
            LDimTierManager tierManager,
            out int tierCount,
            out bool horizontalHoleDimCreated,
            out bool verticalHoleDimCreated)
        {
            tierCount = 0;
            horizontalHoleDimCreated = false;
            verticalHoleDimCreated = false;
            int count = 0;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                if (tierManager == null)
                    tierManager = new LDimTierManager();

                List<Point> unique = new List<Point>();
                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;
                    if (h.X < minX - 10.0 || h.X > maxX + 10.0 ||
                        h.Y < minY - 10.0 || h.Y > maxY + 10.0)
                        continue;
                    AddUniquePoint(unique, new Point(h.X, h.Y, h.Z), 1.0);
                }

                if (unique.Count == 0)
                    return count;

                unique.Sort(delegate (Point a, Point b)
                {
                    int c = a.Y.CompareTo(b.Y);
                    if (c != 0) return c;
                    return a.X.CompareTo(b.X);
                });

                // Gom các lỗ cùng hàng Y để tạo DIM ngang dạng: mép trái - lỗ... - mép phải.
                // THÉP L - DIM NGANG LỖ:
                // Nếu một cụm lỗ theo phương dọc có nhiều lỗ cùng X, chỉ lấy 1 lỗ đại diện
                // để tránh tạo 2 DIM ngang cùng tầng cho lỗ trên/lỗ dưới của cùng một cụm.
                List<Point> horizontalRepresentativeHoles =
                    BuildLHorizontalRepresentativeHoles(unique);
                List<List<Point>> rows = GroupPointsByY(horizontalRepresentativeHoles, 3.0);
                foreach (List<Point> row in rows)
                {
                    if (row == null || row.Count == 0)
                        continue;

                    row.Sort(delegate (Point a, Point b) { return a.X.CompareTo(b.X); });

                    // THÉP L - DIM NGANG LỖ:
                    // Luôn đặt lỗ ở tầng 1 và DIM tổng sẽ nằm tầng 2.
                    // Hai chân mép ngoài cùng bắt vào mép thực phía trên của tấm (Y = maxY).
                    // Chân DIM tại lỗ hở khỏi tâm lỗ đúng bằng phi lỗ theo hướng đặt DIM lên trên.
                    double horizontalEdgeY = maxY;

                    PointList pts = new PointList();
                    pts.Add(new Point(minX, horizontalEdgeY, 0));
                    foreach (Point h in row)
                    {
                        double gap = GetHoleDimGap(h);
                        pts.Add(new Point(h.X, h.Y + gap, 0));
                    }
                    pts.Add(new Point(maxX, horizontalEdgeY, 0));

                    Vector horizontalDirection = new Vector(0, 1, 0);
                    double horizontalTierOffset =
                        GetSteelDimOffsetByTier(tierManager.ReserveTop());
                    double horizontalDistance = ResolveDimDistanceByAnchor4(
                        pts,
                        horizontalDirection,
                        offsetAnchors,
                        horizontalTierOffset);

                    if (handler.CreateDimensionSet(
                        view,
                        pts,
                        horizontalDirection,
                        horizontalDistance) != null)
                    {
                        count++;
                        horizontalHoleDimCreated = true;
                    }
                }

                // Gom các lỗ cùng cột X để tạo DIM dọc dạng: mép dưới - lỗ... - mép trên.
                List<List<Point>> cols = GroupPointsByX(unique, 3.0);

                // THÉP L - DIM DỌC LỖ - RULE MỚI:
                // 1) Cụm lỗ gần mép trái/phải: giữ logic hiện tại, DIM ra mép gần nhất.
                // 2) Cụm lỗ nằm bên trong: DIM dọc về mép trên/dưới tại vị trí cột lỗ,
                //    đường DIM offset ra 200 theo hướng bên trái.
                // 3) Nếu các cụm có cùng kích thước dọc và tổng chiều dài thanh < 500,
                //    chỉ DIM 2 cụm ngoài cùng để tránh lặp kích thước giống nhau.
                cols = FilterLVerticalColumnsForShortSamePattern(cols, minX, maxX, minY, maxY, beamLength);

                foreach (List<Point> col in cols)
                {
                    if (col == null || col.Count == 0)
                        continue;

                    col.Sort(delegate (Point a, Point b) { return a.Y.CompareTo(b.Y); });

                    double colX = col[0].X;
                    double distToLeft = Math.Abs(colX - minX);
                    double distToRight = Math.Abs(maxX - colX);

                    bool nearLeftEdge = distToLeft <= FRONT_END_HOLE_ZONE;
                    bool nearRightEdge = distToRight <= FRONT_END_HOLE_ZONE;
                    bool isNearOuterEdge = nearLeftEdge || nearRightEdge;

                    bool useLeftEdge = distToLeft <= distToRight;
                    double verticalEdgeX;
                    Vector verticalDirection;
                    double verticalOffset;

                    if (isNearOuterEdge)
                    {
                        // Lỗ gần mép: DIM ra mép gần nhất như bản đang chạy ổn.
                        verticalEdgeX = useLeftEdge ? minX : maxX;
                        verticalDirection = useLeftEdge ? new Vector(-1, 0, 0) : new Vector(1, 0, 0);
                        verticalOffset = GetSteelDimOffsetByTier(
                            useLeftEdge ? tierManager.ReserveLeft() : tierManager.ReserveRight());
                    }
                    else
                    {
                        // Lỗ bên trong: không kéo về mép trái/phải của thanh nữa.
                        // Chân mép đặt thẳng theo cột lỗ, đường DIM theo middle offset của scale.
                        verticalEdgeX = colX;
                        useLeftEdge = true;
                        verticalDirection = new Vector(-1, 0, 0);
                        verticalOffset = CurrentMiddleVerticalDimOffset;
                    }

                    PointList pts = new PointList();
                    pts.Add(new Point(verticalEdgeX, minY, 0));
                    foreach (Point h in col)
                    {
                        double gap = GetHoleDimGap(h);
                        double holeFootX = useLeftEdge ? h.X - gap : h.X + gap;
                        pts.Add(new Point(holeFootX, h.Y, 0));
                    }
                    pts.Add(new Point(verticalEdgeX, maxY, 0));

                    double resolvedVerticalOffset = isNearOuterEdge
                        ? ResolveDimDistanceByAnchor4(
                            pts,
                            verticalDirection,
                            offsetAnchors,
                            verticalOffset)
                        : verticalOffset;

                    if (handler.CreateDimensionSet(
                        view,
                        pts,
                        verticalDirection,
                        resolvedVerticalOffset) != null)
                    {
                        count++;
                        verticalHoleDimCreated = true;
                    }
                }

                // Quy tắc tầng thép L: có DIM lỗ thì lỗ ở tầng 1, DIM tổng ở tầng 2.
                tierCount = (horizontalHoleDimCreated || verticalHoleDimCreated) ? 1 : 0;
                if (tierCount <= 0 && count > 0)
                    tierCount = 1;
            }
            catch
            {
            }

            return count;
        }

        private static List<Point> BuildLHorizontalRepresentativeHoles(
            List<Point> unique)
        {
            // THÉP L - DIM NGANG LỖ:
            // Một cụm lỗ theo phương dọc có thể có 2 lỗ cùng X nhưng khác Y.
            // DIM ngang chỉ cần 1 lỗ đại diện của cụm, chọn lỗ dưới cùng như Shape H.
            // Các cụm chỉ có 1 lỗ vẫn giữ nguyên.
            List<Point> result = new List<Point>();

            try
            {
                if (unique == null || unique.Count == 0)
                    return result;

                List<List<Point>> cols = GroupPointsByX(unique, 3.0);
                foreach (List<Point> col in cols)
                {
                    if (col == null || col.Count == 0)
                        continue;

                    // Rule đang áp dụng: trong mỗi cột X chọn lỗ có Y thấp nhất.
                    Point best = null;
                    foreach (Point p in col)
                    {
                        if (p == null)
                            continue;

                        if (best == null || p.Y < best.Y)
                            best = p;
                    }

                    if (best != null)
                        AddUniquePoint(result, new Point(best.X, best.Y, best.Z), 1.0);
                }

                return result;
            }
            catch
            {
                return unique;
            }
        }

        private static List<List<Point>> FilterLVerticalColumnsForShortSamePattern(
            List<List<Point>> cols,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double beamLength)
        {
            // THÉP L - DIM DỌC LỖ:
            // Nếu một đầu thanh có nhiều cột lỗ gần mép, chỉ giữ cột ngoài cùng đại diện:
            // - đầu trái  : giữ cột gần minX nhất
            // - đầu phải  : giữ cột gần maxX nhất
            // Các cột ở giữa vẫn giữ rule cũ: cột gần nhau quá thì chỉ giữ một cột đại diện.
            try
            {
                if (cols == null || cols.Count <= 1)
                    return cols;

                cols.Sort(delegate (List<Point> a, List<Point> b)
                {
                    double ax = GetColumnAverageX(a);
                    double bx = GetColumnAverageX(b);
                    return ax.CompareTo(bx);
                });

                List<Point> leftCol = null;
                List<Point> rightCol = null;
                double leftBestDist = 999999999.0;
                double rightBestDist = 999999999.0;

                List<List<Point>> middleCols = new List<List<Point>>();

                foreach (List<Point> col in cols)
                {
                    if (col == null || col.Count == 0)
                        continue;

                    double x = GetColumnAverageX(col);
                    double distToLeft = Math.Abs(x - minX);
                    double distToRight = Math.Abs(maxX - x);

                    bool nearLeftEdge = distToLeft <= FRONT_END_HOLE_ZONE;
                    bool nearRightEdge = distToRight <= FRONT_END_HOLE_ZONE;

                    if (nearLeftEdge && distToLeft <= distToRight)
                    {
                        if (leftCol == null || distToLeft < leftBestDist)
                        {
                            leftCol = col;
                            leftBestDist = distToLeft;
                        }
                        continue;
                    }

                    if (nearRightEdge && distToRight < distToLeft)
                    {
                        if (rightCol == null || distToRight < rightBestDist)
                        {
                            rightCol = col;
                            rightBestDist = distToRight;
                        }
                        continue;
                    }

                    middleCols.Add(col);
                }

                List<List<Point>> filteredMiddle = new List<List<Point>>();
                double lastKeptX = -999999999.0;
                bool hasKept = false;

                foreach (List<Point> col in middleCols)
                {
                    if (col == null || col.Count == 0)
                        continue;

                    double x = GetColumnAverageX(col);

                    if (!hasKept)
                    {
                        filteredMiddle.Add(col);
                        lastKeptX = x;
                        hasKept = true;
                        continue;
                    }

                    if (Math.Abs(x - lastKeptX) < 200.0)
                    {
                        // Cột này gần cột bên trái đã giữ -> bỏ, không DIM lặp lại.
                        continue;
                    }

                    filteredMiddle.Add(col);
                    lastKeptX = x;
                }

                List<List<Point>> filtered = new List<List<Point>>();

                if (leftCol != null)
                    filtered.Add(leftCol);

                foreach (List<Point> col in filteredMiddle)
                {
                    if (col != null && col.Count > 0 && col != leftCol && col != rightCol)
                        filtered.Add(col);
                }

                if (rightCol != null && rightCol != leftCol)
                    filtered.Add(rightCol);

                filtered.Sort(delegate (List<Point> a, List<Point> b)
                {
                    double ax = GetColumnAverageX(a);
                    double bx = GetColumnAverageX(b);
                    return ax.CompareTo(bx);
                });

                if (filtered.Count == 0)
                    return cols;

                return filtered;
            }
            catch
            {
                return cols;
            }
        }


        private static double GetColumnAverageX(List<Point> col)
        {
            try
            {
                if (col == null || col.Count == 0)
                    return 0.0;

                double sum = 0.0;
                int count = 0;

                foreach (Point p in col)
                {
                    if (p == null)
                        continue;

                    sum += p.X;
                    count++;
                }

                if (count <= 0)
                    return 0.0;

                return sum / count;
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<List<Point>> GroupPointsByY(List<Point> points, double tol)
        {
            List<List<Point>> groups = new List<List<Point>>();
            if (points == null)
                return groups;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                List<Point> found = null;
                foreach (List<Point> g in groups)
                {
                    if (g.Count > 0 && Math.Abs(g[0].Y - p.Y) <= tol)
                    {
                        found = g;
                        break;
                    }
                }

                if (found == null)
                {
                    found = new List<Point>();
                    groups.Add(found);
                }

                found.Add(p);
            }

            return groups;
        }

        private static List<List<Point>> GroupPointsByX(List<Point> points, double tol)
        {
            List<List<Point>> groups = new List<List<Point>>();
            if (points == null)
                return groups;

            foreach (Point p in points)
            {
                if (p == null)
                    continue;

                List<Point> found = null;
                foreach (List<Point> g in groups)
                {
                    if (g.Count > 0 && Math.Abs(g[0].X - p.X) <= tol)
                    {
                        found = g;
                        break;
                    }
                }

                if (found == null)
                {
                    found = new List<Point>();
                    groups.Add(found);
                }

                found.Add(p);
            }

            return groups;
        }

        // THÉP L - HOLE CATALOG CLASSIFICATION
        // Đọc BoltGroup một lần trong global CS, gán mặt theo hướng xuyên lỗ + normal Top/Front view.
        // Point.Z sau khi trả về cho thuật toán DIM cũ chỉ mang phi lỗ thật.
        private enum LShapeHoleFace
        {
            Top,
            Front
        }

        private class LShapeHoleRecord
        {
            public int BoltGroupId;
            public Point ModelPoint;
            public LShapeHoleFace Face;
            public double TopHoleDiameter;
            public double FrontHoleDiameter;
            public double SlotX;
            public double SlotY;
            public string HoleType;
        }

        private class LShapeHoleCandidate
        {
            public Point Point;
            public double HoleDiameter;
            public double SlotX;
            public double SlotY;
            public string HoleType;
            public LShapeHoleFace Face;
            public int BoltGroupId;
        }

        private static void InitializeLShapeHoleCatalog(
            Model model,
            ModelPart part,
            View topView,
            View frontView)
        {
            CurrentLShapeHoleCatalog.Clear();
            CurrentLShapeTopViewForHoleClassify = topView;
            CurrentLShapeFrontViewForHoleClassify = frontView;
            CurrentLShapeHoleCatalogInitialized = false;

            if (model == null || part == null || topView == null || frontView == null)
                return;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane()
                );

                ModelPart globalPart =
                    model.SelectModelObject(part.Identifier) as ModelPart;
                if (globalPart == null)
                    globalPart = part;

                Vector topNormal;
                Vector frontNormal;
                if (!TryGetLShapeViewNormal(topView, out topNormal) ||
                    !TryGetLShapeViewNormal(frontView, out frontNormal))
                    return;

                Solid solid = globalPart.GetSolid();
                double topMin;
                double topMax;
                double frontMin;
                double frontMax;

                if (!TryGetLShapeSolidProjectionRange(solid, topNormal, out topMin, out topMax))
                    return;
                if (!TryGetLShapeSolidProjectionRange(solid, frontNormal, out frontMin, out frontMax))
                    return;

                HashSet<int> addedBoltGroupIds = new HashSet<int>();

                try
                {
                    ModelObjectEnumerator bolts = globalPart.GetBolts();
                    while (bolts.MoveNext())
                    {
                        ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                        AddLShapeBoltGroupToCatalog(
                            bg,
                            globalPart,
                            topNormal,
                            frontNormal,
                            topMin,
                            topMax,
                            frontMin,
                            frontMax,
                            addedBoltGroupIds
                        );
                    }
                }
                catch
                {
                }

                AddLShapeDrawingBoltGroupsToCatalog(
                    model,
                    topView,
                    globalPart,
                    topNormal,
                    frontNormal,
                    topMin,
                    topMax,
                    frontMin,
                    frontMax,
                    addedBoltGroupIds
                );

                AddLShapeDrawingBoltGroupsToCatalog(
                    model,
                    frontView,
                    globalPart,
                    topNormal,
                    frontNormal,
                    topMin,
                    topMax,
                    frontMin,
                    frontMax,
                    addedBoltGroupIds
                );
            }
            catch
            {
            }
            finally
            {
                CurrentLShapeHoleCatalogInitialized = true;
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static void AddLShapeDrawingBoltGroupsToCatalog(
            Model model,
            View view,
            ModelPart mainPart,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax,
            HashSet<int> addedBoltGroupIds)
        {
            try
            {
                if (model == null || view == null || mainPart == null)
                    return;

                DrawingObjectEnumerator boltObjects =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));

                while (boltObjects.MoveNext())
                {
                    DrawingObject drawingBolt = boltObjects.Current as DrawingObject;
                    Identifier id = TryGetModelIdentifier(drawingBolt);
                    if (id == null)
                        continue;

                    ModelBoltGroup bg =
                        model.SelectModelObject(id) as ModelBoltGroup;
                    if (bg == null || !LShapeBoltGroupReferencesPart(bg, mainPart))
                        continue;

                    AddLShapeBoltGroupToCatalog(
                        bg,
                        mainPart,
                        topNormal,
                        frontNormal,
                        topMin,
                        topMax,
                        frontMin,
                        frontMax,
                        addedBoltGroupIds
                    );
                }
            }
            catch
            {
            }
        }

        private static void AddLShapeBoltGroupToCatalog(
            ModelBoltGroup bg,
            ModelPart mainPart,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax,
            HashSet<int> addedBoltGroupIds)
        {
            try
            {
                if (bg == null || mainPart == null)
                    return;

                int boltGroupId = (bg.Identifier != null) ? bg.Identifier.ID : 0;
                if (boltGroupId != 0 &&
                    addedBoltGroupIds != null &&
                    addedBoltGroupIds.Contains(boltGroupId))
                    return;

                Vector holeDirection;
                bool hasHoleDirection =
                    TryGetLShapeBoltDirection(bg, mainPart, out holeDirection);

                double topHoleDiameter =
                    GetTopBottomRealHoleDiameterFromBoltGroup(bg);
                double frontHoleDiameter = GetHoleDiameterFromBoltGroup(bg);

                if (topHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                    topHoleDiameter = frontHoleDiameter;
                if (frontHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                    frontHoleDiameter = topHoleDiameter;

                if (topHoleDiameter <= MIN_VALID_HOLE_DIM_GAP ||
                    frontHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                {
                    double modelHoleDiameter = GetModelHoleDiameterFallback(bg);
                    if (topHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                        topHoleDiameter = modelHoleDiameter;
                    if (frontHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                        frontHoleDiameter = modelHoleDiameter;
                }

                double slotX = GetHoleSlotX(bg);
                double slotY = GetHoleSlotY(bg);
                string holeType = GetHoleTypeText(bg);
                bool addedAnyPosition = false;

                foreach (object obj in bg.BoltPositions)
                {
                    Point modelPoint = obj as Point;
                    if (modelPoint == null)
                        continue;

                    Vector direction = holeDirection;
                    if (!hasHoleDirection)
                    {
                        direction = GetLShapePositionFallbackDirection(
                            modelPoint,
                            topNormal,
                            frontNormal,
                            topMin,
                            topMax,
                            frontMin,
                            frontMax
                        );
                    }

                    LShapeHoleRecord record = new LShapeHoleRecord();
                    record.BoltGroupId = boltGroupId;
                    record.ModelPoint = new Point(
                        modelPoint.X,
                        modelPoint.Y,
                        modelPoint.Z
                    );
                    record.Face = ClassifyLShapeHoleFace(
                        modelPoint,
                        direction,
                        topNormal,
                        frontNormal,
                        topMin,
                        topMax,
                        frontMin,
                        frontMax
                    );
                    record.TopHoleDiameter = topHoleDiameter;
                    record.FrontHoleDiameter = frontHoleDiameter;
                    record.SlotX = slotX;
                    record.SlotY = slotY;
                    record.HoleType = holeType;

                    AddUniqueLShapeHoleRecord(record);
                    addedAnyPosition = true;
                }

                if (addedAnyPosition &&
                    boltGroupId != 0 &&
                    addedBoltGroupIds != null)
                    addedBoltGroupIds.Add(boltGroupId);
            }
            catch
            {
            }
        }

        private static void AddUniqueLShapeHoleRecord(LShapeHoleRecord record)
        {
            if (record == null || record.ModelPoint == null)
                return;

            foreach (LShapeHoleRecord existing in CurrentLShapeHoleCatalog)
            {
                if (existing == null || existing.ModelPoint == null)
                    continue;

                if (record.BoltGroupId != 0 &&
                    existing.BoltGroupId != record.BoltGroupId)
                    continue;

                double dx = existing.ModelPoint.X - record.ModelPoint.X;
                double dy = existing.ModelPoint.Y - record.ModelPoint.Y;
                double dz = existing.ModelPoint.Z - record.ModelPoint.Z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= L_HOLE_CATALOG_DUP_TOL)
                    return;
            }

            CurrentLShapeHoleCatalog.Add(record);
        }

        private static LShapeHoleFace ClassifyLShapeHoleFace(
            Point modelPoint,
            Vector holeDirection,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax)
        {
            double topAlignment = Math.Abs(DotLShapeVectors(holeDirection, topNormal));
            double frontAlignment = Math.Abs(DotLShapeVectors(holeDirection, frontNormal));

            if (Math.Abs(topAlignment - frontAlignment) > L_HOLE_DIRECTION_TIE_TOL)
                return topAlignment > frontAlignment
                    ? LShapeHoleFace.Top
                    : LShapeHoleFace.Front;

            double topCoordinate = DotLShapePointVector(modelPoint, topNormal);
            double frontCoordinate = DotLShapePointVector(modelPoint, frontNormal);
            double topDistance = GetLShapeNormalizedOuterSurfaceDistance(
                topCoordinate,
                topMin,
                topMax
            );
            double frontDistance = GetLShapeNormalizedOuterSurfaceDistance(
                frontCoordinate,
                frontMin,
                frontMax
            );

            return topDistance <= frontDistance
                ? LShapeHoleFace.Top
                : LShapeHoleFace.Front;
        }

        private static Vector GetLShapePositionFallbackDirection(
            Point modelPoint,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax)
        {
            double topDistance = GetLShapeNormalizedOuterSurfaceDistance(
                DotLShapePointVector(modelPoint, topNormal),
                topMin,
                topMax
            );
            double frontDistance = GetLShapeNormalizedOuterSurfaceDistance(
                DotLShapePointVector(modelPoint, frontNormal),
                frontMin,
                frontMax
            );

            return topDistance <= frontDistance
                ? new Vector(topNormal.X, topNormal.Y, topNormal.Z)
                : new Vector(frontNormal.X, frontNormal.Y, frontNormal.Z);
        }

        private static bool TryGetLShapeBoltDirection(
            ModelBoltGroup bg,
            ModelPart mainPart,
            out Vector direction)
        {
            direction = new Vector(0, 0, 0);

            try
            {
                CoordinateSystem cs = bg.GetCoordinateSystem();
                if (cs != null && cs.AxisX != null && cs.AxisY != null)
                {
                    Vector normal = CrossLShapeVectors(cs.AxisX, cs.AxisY);
                    if (TryNormalizeLShapeVector(normal, out direction))
                        return true;
                }
            }
            catch
            {
            }

            return TryGetLShapeConnectedPartDirection(bg, mainPart, out direction);
        }

        private static bool TryGetLShapeConnectedPartDirection(
            ModelBoltGroup bg,
            ModelPart mainPart,
            out Vector direction)
        {
            direction = new Vector(0, 0, 0);

            try
            {
                if (bg == null || mainPart == null)
                    return false;

                Point mainCenter;
                if (!TryGetLShapePartSolidCenter(mainPart, out mainCenter))
                    return false;

                List<ModelPart> connectedParts = new List<ModelPart>();
                if (bg.PartToBoltTo != null)
                    connectedParts.Add(bg.PartToBoltTo);
                if (bg.PartToBeBolted != null)
                    connectedParts.Add(bg.PartToBeBolted);

                if (bg.OtherPartsToBolt != null)
                {
                    foreach (object obj in bg.OtherPartsToBolt)
                    {
                        ModelPart otherPart = obj as ModelPart;
                        if (otherPart != null)
                            connectedParts.Add(otherPart);
                    }
                }

                double bestLength = 0.0;
                foreach (ModelPart connectedPart in connectedParts)
                {
                    if (connectedPart == null ||
                        (connectedPart.Identifier != null &&
                         mainPart.Identifier != null &&
                         connectedPart.Identifier.ID == mainPart.Identifier.ID))
                        continue;

                    Point center;
                    if (!TryGetLShapePartSolidCenter(connectedPart, out center))
                        continue;

                    Vector delta = new Vector(
                        center.X - mainCenter.X,
                        center.Y - mainCenter.Y,
                        center.Z - mainCenter.Z
                    );
                    double length = GetLShapeVectorLength(delta);
                    if (length <= bestLength)
                        continue;

                    Vector normalized;
                    if (!TryNormalizeLShapeVector(delta, out normalized))
                        continue;

                    bestLength = length;
                    direction = normalized;
                }

                return bestLength > 0.0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetLShapePartSolidCenter(
            ModelPart part,
            out Point center)
        {
            center = null;

            try
            {
                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return false;

                center = new Point(
                    (solid.MinimumPoint.X + solid.MaximumPoint.X) / 2.0,
                    (solid.MinimumPoint.Y + solid.MaximumPoint.Y) / 2.0,
                    (solid.MinimumPoint.Z + solid.MaximumPoint.Z) / 2.0
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LShapeBoltGroupReferencesPart(
            ModelBoltGroup bg,
            ModelPart part)
        {
            try
            {
                if (bg == null || part == null || part.Identifier == null)
                    return false;

                int partId = part.Identifier.ID;
                if (bg.PartToBoltTo != null &&
                    bg.PartToBoltTo.Identifier != null &&
                    bg.PartToBoltTo.Identifier.ID == partId)
                    return true;

                if (bg.PartToBeBolted != null &&
                    bg.PartToBeBolted.Identifier != null &&
                    bg.PartToBeBolted.Identifier.ID == partId)
                    return true;

                if (bg.OtherPartsToBolt != null)
                {
                    foreach (object obj in bg.OtherPartsToBolt)
                    {
                        ModelPart otherPart = obj as ModelPart;
                        if (otherPart != null &&
                            otherPart.Identifier != null &&
                            otherPart.Identifier.ID == partId)
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetLShapeViewNormal(
            View view,
            out Vector normal)
        {
            normal = new Vector(0, 0, 0);

            try
            {
                CoordinateSystem cs = view.DisplayCoordinateSystem;
                if (cs == null || cs.AxisX == null || cs.AxisY == null)
                    return false;

                return TryNormalizeLShapeVector(
                    CrossLShapeVectors(cs.AxisX, cs.AxisY),
                    out normal
                );
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetLShapeSolidProjectionRange(
            Solid solid,
            Vector axis,
            out double minValue,
            out double maxValue)
        {
            minValue = 0.0;
            maxValue = 0.0;
            bool hasValue = false;
            bool readAllSolidFaces = false;

            try
            {
                if (solid == null || axis == null)
                    return false;

                try
                {
                    Tekla.Structures.Solid.FaceEnumerator faces = solid.GetFaceEnumerator();
                    while (faces.MoveNext())
                    {
                        Tekla.Structures.Solid.Face face = faces.Current;
                        if (face == null)
                            continue;

                        Tekla.Structures.Solid.LoopEnumerator loops = face.GetLoopEnumerator();
                        while (loops.MoveNext())
                        {
                            Tekla.Structures.Solid.Loop loop = loops.Current;
                            if (loop == null)
                                continue;

                            Tekla.Structures.Solid.VertexEnumerator vertices =
                                loop.GetVertexEnumerator();
                            while (vertices.MoveNext())
                            {
                                Point point = vertices.Current;
                                if (point == null)
                                    continue;

                                double value = DotLShapePointVector(point, axis);
                                if (!hasValue)
                                {
                                    minValue = value;
                                    maxValue = value;
                                    hasValue = true;
                                }
                                else
                                {
                                    if (value < minValue) minValue = value;
                                    if (value > maxValue) maxValue = value;
                                }
                            }
                        }
                    }
                    readAllSolidFaces = true;
                }
                catch
                {
                }

                if (readAllSolidFaces &&
                    hasValue &&
                    Math.Abs(maxValue - minValue) > 0.001)
                    return true;

                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;
                if (min == null || max == null)
                    return false;

                double[] xs = new double[] { min.X, max.X };
                double[] ys = new double[] { min.Y, max.Y };
                double[] zs = new double[] { min.Z, max.Z };
                foreach (double x in xs)
                {
                    foreach (double y in ys)
                    {
                        foreach (double z in zs)
                        {
                            double value = DotLShapePointVector(
                                new Point(x, y, z),
                                axis
                            );
                            if (!hasValue)
                            {
                                minValue = value;
                                maxValue = value;
                                hasValue = true;
                            }
                            else
                            {
                                if (value < minValue) minValue = value;
                                if (value > maxValue) maxValue = value;
                            }
                        }
                    }
                }

                return hasValue && Math.Abs(maxValue - minValue) > 0.001;
            }
            catch
            {
                return false;
            }
        }

        private static double DotLShapePointVector(Point point, Vector vector)
        {
            if (point == null || vector == null)
                return 0.0;

            return point.X * vector.X + point.Y * vector.Y + point.Z * vector.Z;
        }

        private static double DotLShapeVectors(Vector a, Vector b)
        {
            if (a == null || b == null)
                return 0.0;

            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        private static Vector CrossLShapeVectors(Vector a, Vector b)
        {
            if (a == null || b == null)
                return new Vector(0, 0, 0);

            return new Vector(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
        }

        private static bool TryNormalizeLShapeVector(
            Vector input,
            out Vector output)
        {
            output = new Vector(0, 0, 0);

            try
            {
                if (input == null)
                    return false;

                double length = GetLShapeVectorLength(input);
                if (length <= 0.000001)
                    return false;

                output = new Vector(
                    input.X / length,
                    input.Y / length,
                    input.Z / length
                );
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double GetLShapeVectorLength(Vector vector)
        {
            if (vector == null)
                return 0.0;

            return Math.Sqrt(
                vector.X * vector.X +
                vector.Y * vector.Y +
                vector.Z * vector.Z
            );
        }

        private static double GetLShapeNormalizedOuterSurfaceDistance(
            double value,
            double minValue,
            double maxValue)
        {
            double span = Math.Abs(maxValue - minValue);
            if (span <= 0.000001)
                return 999999999.0;

            double distanceToMin = Math.Abs(value - minValue) / span;
            double distanceToMax = Math.Abs(maxValue - value) / span;
            return Math.Min(distanceToMin, distanceToMax);
        }

        private static List<Point> GetVisibleLTopBoltCentersFromRedStrip(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double bottomZ,
            double topZ,
            double legThickness)
        {
            return GetVisibleLClassifiedBoltCentersFromView(
                model,
                view,
                minX,
                maxX,
                minY,
                maxY,
                LShapeHoleFace.Top
            );
        }

        private static List<Point> GetVisibleLFrontBoltCentersFromGreenStrip(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double greenEdgeZ,
            double legThickness)
        {
            return GetVisibleLClassifiedBoltCentersFromView(
                model,
                view,
                minX,
                maxX,
                minY,
                maxY,
                LShapeHoleFace.Front
            );
        }

        private static List<Point> GetVisibleLClassifiedBoltCentersFromView(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            LShapeHoleFace targetFace)
        {
            List<Point> result = new List<Point>();

            try
            {
                List<LShapeHoleCandidate> holes = GetLShapeHoleCandidatesInCurrentPlane(
                    model,
                    view,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    targetFace
                );

                foreach (LShapeHoleCandidate item in holes)
                {
                    if (item == null || item.Point == null)
                        continue;

                    AddUniquePoint(result, new Point(item.Point.X, item.Point.Y, item.HoleDiameter), 1.0);
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<LShapeHoleCandidate> GetLShapeHoleCandidatesInCurrentPlane(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            LShapeHoleFace targetFace)
        {
            List<LShapeHoleCandidate> result = new List<LShapeHoleCandidate>();

            try
            {
                if (model == null || view == null)
                    return result;

                if ((!CurrentLShapeHoleCatalogInitialized || CurrentLShapeHoleCatalog.Count == 0) &&
                    CurrentLShapeHolePartForLocalClassify != null &&
                    CurrentLShapeTopViewForHoleClassify != null &&
                    CurrentLShapeFrontViewForHoleClassify != null)
                {
                    InitializeLShapeHoleCatalog(
                        model,
                        CurrentLShapeHolePartForLocalClassify,
                        CurrentLShapeTopViewForHoleClassify,
                        CurrentLShapeFrontViewForHoleClassify
                    );
                }

                double spanX = Math.Abs(maxX - minX);
                double spanY = Math.Abs(maxY - minY);
                double xyTol = Math.Max(TOL * 5.0, Math.Min(spanX, spanY) * 0.02);

                Matrix toView = MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);

                foreach (LShapeHoleRecord record in CurrentLShapeHoleCatalog)
                {
                    if (record == null || record.ModelPoint == null)
                        continue;

                    if (record.Face != targetFace)
                        continue;

                    Point viewPoint = toView.Transform(record.ModelPoint);
                    if (viewPoint == null)
                        continue;

                    if (viewPoint.X < minX - xyTol ||
                        viewPoint.X > maxX + xyTol ||
                        viewPoint.Y < minY - xyTol ||
                        viewPoint.Y > maxY + xyTol)
                        continue;

                    double holeDiameter =
                        record.Face == LShapeHoleFace.Front
                        ? record.FrontHoleDiameter
                        : record.TopHoleDiameter;

                    if (holeDiameter <= MIN_VALID_HOLE_DIM_GAP)
                        holeDiameter = Math.Max(record.TopHoleDiameter, record.FrontHoleDiameter);

                    LShapeHoleCandidate item = new LShapeHoleCandidate();
                    item.Point = new Point(viewPoint.X, viewPoint.Y, viewPoint.Z);
                    item.HoleDiameter = holeDiameter;
                    item.SlotX = record.SlotX;
                    item.SlotY = record.SlotY;
                    item.HoleType = record.HoleType;
                    item.Face = record.Face;
                    item.BoltGroupId = record.BoltGroupId;

                    result.Add(item);
                }
            }
            catch
            {
            }

            return result;
        }

        private static double GetLThicknessFromProfile(ModelPart part)
        {
            try
            {
                if (part == null)
                    return 0.0;

                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);

                if (string.IsNullOrEmpty(profile))
                    return 0.0;

                string p = profile.ToUpper()
                    .Replace("ANGLE", "")
                    .Replace("L", "")
                    .Replace("PL", "")
                    .Replace(" ", "")
                    .Replace(",", ".");

                string[] tokens = p.Split(
                    new char[] { '*', 'X', 'x', '-' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                List<double> values = new List<double>();

                foreach (string token in tokens)
                {
                    double v;
                    if (double.TryParse(
                        token,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out v))
                    {
                        if (v > 0.0)
                            values.Add(v);
                    }
                }

                // L65*65*6 / L-65x65x6: độ dày là số cuối.
                if (values.Count >= 3)
                    return values[values.Count - 1];

                if (values.Count > 0)
                    return values[values.Count - 1];
            }
            catch
            {
            }

            return 0.0;
        }

        // THÉP C - FRONT HOLE FILTER
        // Chỉ dim lỗ nằm trên bụng C. Không dùng mặt cắt.
        // Vùng hợp lệ được lấy từ mép ngoài bụng C đi vào đúng 1 web thickness.



        private static int CreateFrontTotalDims(
            StraightDimensionSetHandler handler,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double horizontalTotalOffset,
            double verticalTotalOffset)
        {
            int count = 0;

            PointList lengthPts = new PointList();
            // FRONT tổng ngang: dùng điểm ngoài cùng thật của dầm, tránh bắt vào endpoint chamfer bị cắt.
            lengthPts.Add(Clone2D(edgeAnchors.LeftMost));
            lengthPts.Add(Clone2D(edgeAnchors.RightMost));

            // FRONT - thuật toán điểm neo:
            // Không dùng bounding box và không cộng bù khi gặp chamfer/rãnh.
            // Offset tầng lấy theo cực trị A/B/C/D và chân đầu thật của PointList.
            Vector lengthDirection = new Vector(0, 1, 0);
            double realUpperTotalOffset = ResolveDimDistanceByAnchor4(
                lengthPts,
                lengthDirection,
                offsetAnchors,
                horizontalTotalOffset);

            if (handler.CreateDimensionSet(
                view,
                lengthPts,
                lengthDirection,
                realUpperTotalOffset) != null)
                count++;

            PointList heightPts = new PointList();
            // FRONT tổng dọc: dùng điểm thấp/cao ngoài cùng thật của dầm.
            heightPts.Add(Clone2D(edgeAnchors.TopMost));
            heightPts.Add(Clone2D(edgeAnchors.BottomMost));

            // FRONT - thuật toán điểm neo:
            // Không dùng bounding box và không cộng bù khi gặp chamfer/rãnh.
            // Offset tầng lấy theo cực trị A/B/C/D và chân đầu thật của PointList.
            Vector heightDirection = new Vector(-1, 0, 0);
            double realLeftTotalOffset = ResolveDimDistanceByAnchor4(
                heightPts,
                heightDirection,
                offsetAnchors,
                verticalTotalOffset);

            if (handler.CreateDimensionSet(
                view,
                heightPts,
                heightDirection,
                realLeftTotalOffset) != null)
                count++;

            return count;
        }




        private static List<Point> GetFrontWebFacePolygon(Solid solid, Point min, Point max)
        {
            List<Point> best = new List<Point>();

            try
            {
                double midZ = (min.Z + max.Z) / 2.0;

                double[] zPlanes = new double[]
                {
                    midZ,
                    midZ - 1.0,
                    midZ + 1.0,
                    midZ - 2.0,
                    midZ + 2.0
                };

                double bestScore = -1.0;

                foreach (double z in zPlanes)
                {
                    Point p1 = new Point(min.X - 1000, min.Y - 1000, z);
                    Point p2 = new Point(max.X + 1000, min.Y - 1000, z);
                    Point p3 = new Point(min.X - 1000, max.Y + 1000, z);

                    List<Point> poly =
                        GetLargestIntersectionPolygon(
                            solid.IntersectAllFaces(p1, p2, p3)
                        );

                    if (poly.Count < 2)
                        continue;

                    double minX, maxX, minY, maxY;
                    GetMinMax(poly, out minX, out maxX, out minY, out maxY);

                    double width = Math.Abs(maxX - minX);
                    double height = Math.Abs(maxY - minY);
                    double score = width * height;

                    if (width < 100.0 || height < 20.0)
                        continue;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = poly;
                    }
                }
            }
            catch
            {
            }

            return best;
        }



        private static void ResizeViewBoundaryKeepDepthBySolid(
            View view,
            Model model,
            ModelPart part)
        {
            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;

                ResizeViewBoundaryKeepDepth(
                    view,
                    min.X,
                    max.X,
                    min.Y,
                    max.Y
                );
            }
            catch
            {
            }
            finally
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static ModelPart GetMainPartFromDrawing(
            Model model,
            Drawing drawing)
        {
            try
            {
                if (model == null || drawing == null)
                    return null;

                // SINGLE PART DRAWING: giữ đúng cách cũ.
                SinglePartDrawing spDrawing = drawing as SinglePartDrawing;
                if (spDrawing != null)
                {
                    ModelPart spPart = TrySelectModelPart(model, spDrawing.PartIdentifier);
                    if (spPart != null)
                        return spPart;
                }

                // ASSEMBLY DRAWING / fallback:
                // Không dùng Modify, không đụng model 3D.
                // Quét Drawing.Part trong các view, chọn part chính theo kích thước solid lớn nhất.
                return FindLargestModelPartFromDrawingViews(model, drawing);
            }
            catch
            {
                return null;
            }
        }

        private static DrawingPart GetSelectedDrawingPart(DrawingHandler dh)
        {
            try
            {
                if (dh == null)
                    return null;

                DrawingObjectEnumerator selected =
                    dh.GetDrawingObjectSelector().GetSelected();

                while (selected != null && selected.MoveNext())
                {
                    DrawingPart dp = selected.Current as DrawingPart;
                    if (dp != null && dp.ModelIdentifier != null)
                        return dp;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsBoltBelongsToSelectedMainPart(
            ModelBoltGroup bg,
            ModelPart mainPart)
        {
            try
            {
                if (bg == null || mainPart == null || mainPart.Identifier == null)
                    return false;

                ModelPart p1 = GetModelPartPropertyByReflection(bg, "PartToBeBolted");
                if (IsSameModelPart(p1, mainPart))
                    return true;

                ModelPart p2 = GetModelPartPropertyByReflection(bg, "PartToBoltTo");
                if (IsSameModelPart(p2, mainPart))
                    return true;

                ModelPart p3 = GetModelPartPropertyByReflection(bg, "Father");
                if (IsSameModelPart(p3, mainPart))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static ModelPart GetModelPartPropertyByReflection(
            object obj,
            string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propertyName))
                    return null;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return null;

                object value = prop.GetValue(obj, null);
                return value as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSameModelPart(ModelPart a, ModelPart b)
        {
            try
            {
                if (a == null || b == null || a.Identifier == null || b.Identifier == null)
                    return false;

                return a.Identifier.ID == b.Identifier.ID;
            }
            catch
            {
                return false;
            }
        }

        private static ModelPart TrySelectModelPart(
            Model model,
            Identifier identifier)
        {
            try
            {
                if (model == null || identifier == null)
                    return null;

                ModelObject mo = model.SelectModelObject(identifier);
                return mo as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static ModelPart FindLargestModelPartFromDrawingViews(
            Model model,
            Drawing drawing)
        {
            ModelPart bestPart = null;
            double bestScore = -1.0;
            List<int> usedIds = new List<int>();

            try
            {
                if (model == null || drawing == null)
                    return null;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null)
                        continue;

                    DrawingObjectEnumerator parts =
                        view.GetAllObjects(typeof(DrawingPart));

                    while (parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        int id = dp.ModelIdentifier.ID;
                        if (usedIds.Contains(id))
                            continue;

                        usedIds.Add(id);

                        ModelPart part = TrySelectModelPart(model, dp.ModelIdentifier);
                        if (part == null)
                            continue;

                        double score = GetPartSolidBoxScore(part);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPart = part;
                        }
                    }
                }
            }
            catch
            {
            }

            return bestPart;
        }

        private static double GetPartSolidBoxScore(ModelPart part)
        {
            try
            {
                if (part == null)
                    return 0.0;

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return 0.0;

                double dx = Math.Abs(solid.MaximumPoint.X - solid.MinimumPoint.X);
                double dy = Math.Abs(solid.MaximumPoint.Y - solid.MinimumPoint.Y);
                double dz = Math.Abs(solid.MaximumPoint.Z - solid.MinimumPoint.Z);

                if (dx < 1.0) dx = 1.0;
                if (dy < 1.0) dy = 1.0;
                if (dz < 1.0) dz = 1.0;

                return dx * dy * dz;
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<View> GetMainPartViews(
            Drawing drawing,
            Identifier mainPartIdentifier)
        {
            List<View> result = new List<View>();

            try
            {
                ContainerView sheet = drawing.GetSheet();
                DrawingObjectEnumerator views = sheet.GetAllViews();

                while (views.MoveNext())
                {
                    View view = views.Current as View;

                    if (view == null)
                        continue;

                    if (ViewContainsMainPart(view, mainPartIdentifier))
                        result.Add(view);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool ViewContainsMainPart(
            View view,
            Identifier mainPartIdentifier)
        {
            try
            {
                if (view == null || mainPartIdentifier == null)
                    return false;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(DrawingPart));

                while (parts.MoveNext())
                {
                    DrawingPart dp = parts.Current as DrawingPart;

                    if (dp == null || dp.ModelIdentifier == null)
                        continue;

                    if (dp.ModelIdentifier.ID == mainPartIdentifier.ID)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void DeleteAllDimensions(Drawing drawing)
        {
            // FIX DELETE DIM SHAPE:
            // Không dùng GetAllObjects() không filter nữa, vì một số drawing có object
            // LeaderLinePlacing bị lỗi deserialize làm macro dừng trước khi xóa DIM.
            // Chỉ quét đúng các loại DIM cần xóa, giữ nguyên AngleDimension.
            try
            {
                if (drawing == null)
                    return;

                ContainerView sheet = null;

                try
                {
                    sheet = drawing.GetSheet();
                }
                catch
                {
                    sheet = null;
                }

                Type[] dimTypes = new Type[]
                {
                    typeof(StraightDimensionSet),
                    typeof(StraightDimension),
                    typeof(CurvedDimensionSetRadial),
                    typeof(CurvedDimensionSetOrthogonal),
                    typeof(RadiusDimension)
                };

                // Xóa DIM ở sheet theo từng loại object, tránh Tekla phải deserialize toàn bộ object.
                if (sheet != null)
                {
                    foreach (Type dimType in dimTypes)
                    {
                        try
                        {
                            DrawingObjectEnumerator objects = sheet.GetAllObjects(dimType);
                            DeleteObjectsFromEnumerator(objects);
                        }
                        catch
                        {
                        }
                    }

                    // Quét thêm từng view để bắt các DIM nằm trong view nếu sheet filter bỏ sót.
                    try
                    {
                        DrawingObjectEnumerator views = sheet.GetAllViews();

                        while (true)
                        {
                            bool hasNext = false;

                            try
                            {
                                hasNext = views.MoveNext();
                            }
                            catch
                            {
                                break;
                            }

                            if (!hasNext)
                                break;

                            View view = views.Current as View;
                            if (view == null)
                                continue;

                            foreach (Type dimType in dimTypes)
                            {
                                try
                                {
                                    DrawingObjectEnumerator viewObjects = view.GetAllObjects(dimType);
                                    DeleteObjectsFromEnumerator(viewObjects);
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
            }
            catch
            {
            }
        }

        private static void DeleteObjectsFromEnumerator(DrawingObjectEnumerator objects)
        {
            try
            {
                if (objects == null)
                    return;

                while (true)
                {
                    bool hasNext = false;

                    try
                    {
                        hasNext = objects.MoveNext();
                    }
                    catch
                    {
                        break;
                    }

                    if (!hasNext)
                        break;

                    DrawingObject obj = objects.Current as DrawingObject;
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



        private static void SelectViews(
            DrawingHandler dh,
            List<View> views)
        {
            try
            {
                DrawingObjectSelector selector =
                    dh.GetDrawingObjectSelector();

                DrawingObjectEnumerator.AutoFetch = true;

                ArrayList selected = new ArrayList();

                foreach (View view in views)
                {
                    if (view != null)
                        selected.Add(view);
                }

                selector.SelectObjects(selected, false);
            }
            catch
            {
            }
        }

        private static void CommitAndWait(
            Drawing drawing,
            int ms)
        {
            try
            {
                drawing.CommitChanges();
            }
            catch
            {
            }

            try
            {
                System.Threading.Thread.Sleep(ms);
            }
            catch
            {
            }
        }

        private static void AddUniquePoint(
            List<Point> list,
            Point p,
            double tol)
        {
            foreach (Point q in list)
            {
                if (Math.Abs(q.X - p.X) <= tol &&
                    Math.Abs(q.Y - p.Y) <= tol)
                {
                    // Nếu cùng 1 lỗ nhưng lần sau đọc được phi lỗ tốt hơn,
                    // cập nhật lại Z để chân DIM dùng đúng phi lỗ.
                    try
                    {
                        if (p.Z > q.Z)
                            q.Z = p.Z;
                    }
                    catch
                    {
                    }

                    return;
                }
            }

            list.Add(p);
        }
    }
}
