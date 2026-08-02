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
    public class HShapeAutoSectionPrecheckResult
    {
        public int HoleResult = -1;
        public int TopBottomContourResult = -1;
        public bool HasTopBottomDifference = false;
        public string Message = "";
        public View TopView = null;
        public View FrontView = null;
        public View BottomView = null;
        public List<View> SpecialTopSections = new List<View>();
        public List<View> SpecialBottomSections = new List<View>();
        public List<View> ExactSectionViews = new List<View>();
        public bool IsValid = false;
        public bool HasCompleteSingleLayout = false;
        public bool HasCompleteAssemblyLayout = false;
        public bool HasPartialSectionLayout = false;
    }

    public class ShapeScript
    {
        private const double TOL = 1.0;
        private const double VIEW_PADDING = 20.0;

        // CENTER VÙNG VÀNG - ÉP GIỚI HẠN THEO 2 BLOCK TRÊN/DƯỚI.
        // Dùng khi API không đọc được template block nên center vẫn ăn theo margin cũ.
        // Giá trị là % chiều cao giấy, tự chạy cho A1/A3 ngang/dọc.
        private const bool FORCE_CENTER_BY_TOP_BOTTOM_BLOCKS = true;
        private const double CENTER_BOTTOM_BLOCK_HEIGHT_RATIO = 0.18;
        private const double CENTER_TOP_BLOCK_HEIGHT_RATIO = 0.08;
        private const double CENTER_BLOCK_EXTRA_GAP = 5.0;

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
        // Giá trị hở chân DIM Top/Bottom phải lấy theo phi lỗ thật.
        // Nếu model không trả phi lỗ thì thử lấy từ hình học drawing bolt.
        private const double MIN_VALID_HOLE_DIM_GAP = 1.0;

        // DIM lỗ TOP/BOTTOM VIEW:
        // Tất cả chân DIM lỗ dùng đúng phi lỗ thật đọc được từ Tekla.
        // Không còn fallback cố định, không còn rule riêng theo kích thước lỗ.

        private const double TOP_FLANGE_DEPTH_TOL = 3.0;

        // H-SHAPE HOLE FACE CLASSIFICATION:
        // Face names belong to the drawing views, not to a fixed part-local Y/Z axis.
        // Direction is the primary signal; position is used only to split Top and Bottom.
        private const double H_HOLE_DIRECTION_TIE_TOL = 0.02;
        private const double H_HOLE_CENTER_SIDE_TOL = 0.5;

        // TOP VIEW - GIỚI HẠN HÌNH CHIẾU THEO COORDINATE:
        // Chỉ dùng cho biên dạng chiếu TOP để dim tổng/chamfer/rãnh.
        // Không dùng độ sâu cố định 20mm nữa.
        // Rule mới: Có thể tủy chỉnh mặt trên xuống gần hết chiều sâu dầm,
        // Có thể tùy chỉnh vùng XXmm sâu đáy để tránh rãnh/cạnh đáy bị dim nhầm ở TOP.
        private const double TOP_PROJECTED_BOTTOM_EXCLUDE = 0.0;

        private const double FRONT_WEB_DEPTH_TOL = 3.0;
        private const double FRONT_END_HOLE_ZONE = 300.0;

        // FRONT VIEW - CHIA TRƯỜNG HỢP LỖ:
        // - 1 cụm hoặc 2 cụm lỗ ở mép: giữ DIM cũ, không tạo DIM giữa.
        // - Từ 3 cụm trở lên: cụm đầu + cụm cuối giữ DIM mép như cũ,
        //   các cụm nằm giữa mới tạo thêm DIM riêng phía dưới.
        private const double FRONT_HOLE_CLUSTER_SPLIT_GAP = 300.0;

        // TOP/BOTTOM VIEW - RULE LỖ MỚI:
        // Không còn rule đặc biệt theo kích thước lỗ.
        // Phân loại lỗ theo hình học trước, phi/M chỉ dùng làm khoảng hở chân DIM.
        // Nếu cụm/lỗ cách mép trái/phải < 200 thì ưu tiên đẩy DIM về phía mép đó.
        private const double TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE = 200.0;

        // Chỉ dùng khi tách cụm phục vụ DIM dọc. DIM ngang vẫn giữ nguyên chain toàn hàng.
        private const double TOP_BOTTOM_VERTICAL_CLUSTER_MIN_SPLIT_GAP = 300.0;
        private const double TOP_BOTTOM_VERTICAL_CLUSTER_GAP_RATIO = 2.5;

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
        private const bool ENABLE_TOP_BOTTOM_HOLE_CHECK = true;
        private const double TOP_BOTTOM_HOLE_POSITION_TOL = 2.0;
        private const double TOP_BOTTOM_HOLE_SIZE_TOL = 1.0;
        private const double TOP_BOTTOM_CROSS_FACE_SYMMETRY_EDGE_TOL = 5.0;
        private const double TOP_BOTTOM_CONTOUR_COMPARE_TOL = 3.0;

        // AUTO SCALE THEO KHỔ GIẤY + CHIỀU DÀI DẦM
        // Chạy trước khi tạo DIM.
        // Scale chọn theo: (chiều dài dầm + reserve dim) / vùng giấy hữu dụng.
        private const bool AUTO_SCALE_BY_PART_LENGTH = true;
        private const double A3_SHEET_WIDTH = 420.0;
        private const double A3_SHEET_HEIGHT = 297.0;
        private const double A1_SHEET_WIDTH = 841.0;
        private const double A1_SHEET_HEIGHT = 594.0;
        private const double SHEET_SIZE_TOLERANCE = 2.0;
        private const double AUTO_SCALE_DIM_RESERVE = 500.0;
        private const double A3_SCALE_MARGIN_TOTAL = 20.0;
        private const double A1_OR_OTHER_SCALE_MARGIN_TOTAL = 30.0;

        // Check lỗ Top//bottom có khác nhau ko
        public static int TopBottomHoleCheckResult = 0;
        private static double LastAppliedAutoScale = 0.0;
        private static double CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
        private static double CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
        private static double CurrentMiddleVerticalDimOffset =
            DIM_TIER_SCALE_15_MIDDLE;
        private static int LastTopBottomDimTier = 1;
        private static int LastFrontTopDimTier = 1;
        private static int LastFrontBottomDimTier = 1;
        private static int LastFrontRightDimTier = 1;
        private static int LastBottomTopDimTier = 1;
        private static bool EnableNotchRadiusDimensionForCurrentDrawing = false;
        private static bool UseSelectedMainPartMode = false;
        private static ModelPart CurrentHShapeHolePartForLocalClassify = null;
        private static View CurrentHShapeTopViewForHoleClassify = null;
        private static View CurrentHShapeFrontViewForHoleClassify = null;
        private static bool CurrentHShapeHoleCatalogInitialized = false;
        private static bool CurrentHShapeHoleCatalogReadSucceeded = false;
        private static readonly List<HShapeHoleRecord> CurrentHShapeHoleCatalog =
            new List<HShapeHoleRecord>();
        private static bool PendingAutoSectionDimPass = false;
        private static bool PendingAutoSectionSingleLayout = false;
        private static int PendingAutoSectionSelectedPartId = 0;

        public static void PrepareAutoSectionDimPass(
            bool singlePartLayout,
            int selectedAssemblyPartId)
        {
            PendingAutoSectionDimPass = true;
            PendingAutoSectionSingleLayout = singlePartLayout;
            PendingAutoSectionSelectedPartId = selectedAssemblyPartId > 0
                ? selectedAssemblyPartId
                : 0;
        }

        public static HShapeAutoSectionPrecheckResult PrepareAutoSectionPrecheck(
            Drawing drawing,
            Model model,
            ModelPart part)
        {
            HShapeAutoSectionPrecheckResult result =
                new HShapeAutoSectionPrecheckResult();

            TopBottomHoleCheckResult = -1;
            CurrentHShapeHolePartForLocalClassify = null;
            CurrentHShapeTopViewForHoleClassify = null;
            CurrentHShapeFrontViewForHoleClassify = null;
            CurrentHShapeHoleCatalogInitialized = false;
            CurrentHShapeHoleCatalogReadSucceeded = false;
            CurrentHShapeHoleCatalog.Clear();

            try
            {
                if (drawing == null)
                {
                    result.Message = "Khong co drawing de precheck Auto Section.";
                    return result;
                }

                if (model == null || !model.GetConnectionStatus())
                {
                    result.Message = "Khong ket noi duoc model de precheck Auto Section.";
                    return result;
                }

                if (part == null || part.Identifier == null)
                {
                    result.Message = "Khong co part hop le de precheck Auto Section.";
                    return result;
                }

                List<View> views = GetMainPartViews(drawing, part.Identifier);
                if (views.Count == 0)
                {
                    result.Message = "Khong tim thay view chua part de precheck Auto Section.";
                    return result;
                }

                views.Sort(delegate (View a, View b)
                {
                    return b.Origin.Y.CompareTo(a.Origin.Y);
                });

                View topViewByType = FindViewByViewTypeForH(
                    views,
                    "TopView",
                    "Top"
                );
                View frontViewByType = FindViewByViewTypeForH(
                    views,
                    "FrontView",
                    "Front"
                );
                View bottomViewByType = FindViewByViewTypeForH(
                    views,
                    "BottomView",
                    "Bottom"
                );

                ClassifySectionViewsForH(
                    views,
                    frontViewByType,
                    topViewByType,
                    bottomViewByType,
                    result.SpecialTopSections,
                    result.SpecialBottomSections,
                    result.ExactSectionViews
                );

                View resolvedTopView = topViewByType;
                if (resolvedTopView == null && result.SpecialTopSections.Count > 0)
                    resolvedTopView = result.SpecialTopSections[0];

                View resolvedBottomView = bottomViewByType;
                if (resolvedBottomView == null && result.SpecialBottomSections.Count > 0)
                    resolvedBottomView = result.SpecialBottomSections[0];

                result.TopView = resolvedTopView;
                result.FrontView = frontViewByType;
                result.BottomView = resolvedBottomView;

                bool isSinglePartDrawing = drawing is SinglePartDrawing;
                bool isAssemblyDrawing = drawing is AssemblyDrawing;
                bool hasSpecialTop = result.SpecialTopSections.Count > 0;
                bool hasSpecialBottom = result.SpecialBottomSections.Count > 0;
                bool hasAnySpecialSection = hasSpecialTop || hasSpecialBottom;
                bool hasAmbiguousSpecialSection =
                    result.SpecialTopSections.Count > 1 ||
                    result.SpecialBottomSections.Count > 1;

                result.HasCompleteSingleLayout =
                    isSinglePartDrawing &&
                    hasSpecialTop &&
                    frontViewByType != null &&
                    hasSpecialBottom;

                result.HasCompleteAssemblyLayout =
                    isAssemblyDrawing &&
                    topViewByType != null &&
                    frontViewByType != null &&
                    (bottomViewByType != null || hasSpecialBottom);

                bool hasCompleteLayoutForDrawing =
                    isSinglePartDrawing
                        ? result.HasCompleteSingleLayout
                        : isAssemblyDrawing
                            ? result.HasCompleteAssemblyLayout
                            : false;

                result.HasPartialSectionLayout =
                    hasAnySpecialSection &&
                    (!hasCompleteLayoutForDrawing || hasAmbiguousSpecialSection);

                if (resolvedTopView == null)
                {
                    result.Message = "Khong xac dinh duoc Top view de precheck Auto Section.";
                    return result;
                }

                if (frontViewByType == null)
                {
                    result.Message = "Khong xac dinh duoc FrontView bang ViewType de precheck Auto Section.";
                    return result;
                }

                CurrentHShapeHolePartForLocalClassify = part;
                CurrentHShapeTopViewForHoleClassify = resolvedTopView;
                CurrentHShapeFrontViewForHoleClassify = frontViewByType;

                InitializeHShapeHoleCatalog(
                    model,
                    part,
                    resolvedTopView,
                    frontViewByType
                );

                CheckTopBottomHolesAndMark(
                    model,
                    part,
                    resolvedTopView
                );

                result.HoleResult = TopBottomHoleCheckResult;
                bool useContourDifferenceForAutoSection =
                    isSinglePartDrawing;
                result.TopBottomContourResult =
                    useContourDifferenceForAutoSection
                        ? CheckTopBottomFlangeContourDifference(
                            model,
                            part,
                            resolvedTopView
                        )
                        : -1;

                bool holeCheckKnown =
                    result.HoleResult == 0 || result.HoleResult == 1;
                bool contourCheckKnown =
                    result.TopBottomContourResult == 0 ||
                    result.TopBottomContourResult == 1;
                bool contourDifferenceForAutoSection =
                    useContourDifferenceForAutoSection &&
                    result.TopBottomContourResult == 1;

                result.HasTopBottomDifference =
                    result.HoleResult == 1 ||
                    contourDifferenceForAutoSection;

                // Giữ tương thích với flow cũ: nếu kiểm tra lỗ đã rõ thì precheck vẫn hợp lệ
                // ngay cả khi Tekla không trả được contour. Nếu contour đã xác nhận khác,
                // vẫn cho phép cắt B/C dù kiểm tra lỗ chưa xác định được.
                result.IsValid =
                    result.HasTopBottomDifference ||
                    holeCheckKnown;

                if (!result.IsValid)
                {
                    result.Message = "Khong the hoan tat precheck lo va ranh Top/Bottom.";
                }
                else if (result.HoleResult == 1 &&
                         contourDifferenceForAutoSection)
                {
                    result.Message = "Lo va bien dang ranh Top/Bottom khac nhau.";
                }
                else if (result.HoleResult == 1)
                {
                    result.Message = "Lo Top/Bottom khac nhau.";
                }
                else if (contourDifferenceForAutoSection)
                {
                    result.Message = "Bien dang ranh Top/Bottom khac nhau.";
                }
                else if (isAssemblyDrawing)
                {
                    result.Message =
                        "Lo Top/Bottom giong nhau; Assembly khong ap dung rule ranh.";
                }
                else if (!contourCheckKnown)
                {
                    result.Message = "Lo Top/Bottom giong nhau; khong doc duoc bien dang ranh.";
                }
                else
                {
                    result.Message = "Lo va bien dang ranh Top/Bottom giong nhau.";
                }
            }
            catch (Exception ex)
            {
                result.Message = "Precheck Auto Section loi: " + ex.Message;
            }

            return result;
        }

        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
            bool autoSectionDimPass = PendingAutoSectionDimPass;
            bool autoSectionSingleLayout = PendingAutoSectionSingleLayout;
            int autoSectionSelectedPartId = PendingAutoSectionSelectedPartId;
            PendingAutoSectionDimPass = false;
            PendingAutoSectionSingleLayout = false;
            PendingAutoSectionSelectedPartId = 0;

            TopBottomHoleCheckResult = 0;
            LastTopBottomDimTier = 1;
            LastFrontTopDimTier = 1;
            LastFrontBottomDimTier = 1;
            LastFrontRightDimTier = 1;
            LastBottomTopDimTier = 1;
            LastAppliedAutoScale = 0.0;
            CurrentDimTierBase = DIM_TIER_SCALE_15_BASE;
            CurrentDimTierStep = DIM_TIER_SCALE_15_STEP;
            CurrentMiddleVerticalDimOffset =
                DIM_TIER_SCALE_15_MIDDLE;
            EnableNotchRadiusDimensionForCurrentDrawing = false;
            UseSelectedMainPartMode = false;
            CurrentHShapeHolePartForLocalClassify = null;
            CurrentHShapeTopViewForHoleClassify = null;
            CurrentHShapeFrontViewForHoleClassify = null;
            CurrentHShapeHoleCatalogInitialized = false;
            CurrentHShapeHoleCatalogReadSucceeded = false;
            CurrentHShapeHoleCatalog.Clear();
            DrawingHandler dh = new DrawingHandler();
            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null) return;

            bool isSinglePartDrawing = drawing is SinglePartDrawing;
            bool isAssemblyDrawing = drawing is AssemblyDrawing;
            EnableNotchRadiusDimensionForCurrentDrawing = isSinglePartDrawing;

            Model model = new Model();
            if (!model.GetConnectionStatus()) return;

            // HỖ TRỢ CẢ SINGLE PART + ASSEMBLY DRAWING:
            // Chỉ thay phần lấy main part/view, không thay thuật toán DIM phía dưới.
            ModelPart selectedModelPart = null;
            DrawingPart selectedDrawingPart = null;

            if (autoSectionSelectedPartId > 0)
            {
                selectedModelPart = TrySelectModelPart(
                    model,
                    new Identifier(autoSectionSelectedPartId));
            }

            if (selectedModelPart == null)
            {
                selectedDrawingPart = GetSelectedDrawingPart(dh);
                if (selectedDrawingPart != null && selectedDrawingPart.ModelIdentifier != null)
                    selectedModelPart = TrySelectModelPart(model, selectedDrawingPart.ModelIdentifier);
            }

            ModelPart part = selectedModelPart;
            if (part != null &&
                (autoSectionSelectedPartId > 0 || selectedDrawingPart != null))
                UseSelectedMainPartMode = true;

            if (part == null)
                part = GetMainPartFromDrawing(model, drawing);

            if (part == null) return;
            CurrentHShapeHolePartForLocalClassify = part;

            List<View> views = GetMainPartViews(drawing, part.Identifier);
            if (views.Count == 0) return;

            views.Sort(delegate (View a, View b)
            {
                return b.Origin.Y.CompareTo(a.Origin.Y);
            });

            // BƯỚC 0: NHẬN DIỆN VIEW THEO VIEWTYPE + SECTION ĐẶC BIỆT.
            // Chỉ thay phần chọn Top / Front / Bottom / Section Exact.
            // Các thuật toán DIM / MOVE / CENTER / ARRANGE phía dưới giữ nguyên.
            View topViewByType = FindViewByViewTypeForH(views, "TopView", "Top");
            View frontViewByType = FindViewByViewTypeForH(views, "FrontView", "Front");
            View bottomViewByType = FindViewByViewTypeForH(views, "BottomView", "Bottom");

            List<View> specialTopSections = new List<View>();
            List<View> specialBottomSections = new List<View>();
            List<View> exactSectionViews = new List<View>();

            ClassifySectionViewsForH(
                views,
                frontViewByType,
                topViewByType,
                bottomViewByType,
                specialTopSections,
                specialBottomSections,
                exactSectionViews
            );

            if (autoSectionDimPass)
            {
                RecoverAutoSectionDimViewsForH(
                    autoSectionSingleLayout,
                    views,
                    frontViewByType,
                    topViewByType,
                    bottomViewByType,
                    specialTopSections,
                    specialBottomSections,
                    exactSectionViews
                );
            }

            // Nếu Tekla không trả TopView thật vì người dùng cắt mặt Top thủ công dạng SectionView,
            // lấy Section đặc biệt nằm trên Front và có chiều ngang gần bằng Front làm Top.
            if (topViewByType == null && specialTopSections.Count > 0)
                topViewByType = specialTopSections[0];

            // Hướng mới bắt buộc Front phải xác định bằng ViewType để phân biệt Top/Bottom Section đặc biệt.
            if (frontViewByType == null || topViewByType == null)
                return;

            InitializeHShapeHoleCatalog(
                model,
                part,
                topViewByType,
                frontViewByType
            );

            // Tên biến giữ nguyên để không đụng các thuật toán arrange/center phía dưới.
            // Giá trị không còn lấy bằng FindSmallestViewByRestrictionBox nữa.
            // SectionView thường = ViewType SectionView và KHÔNG phải Section đặc biệt Top/Bottom.
            View smallestExactView = null;
            if (isSinglePartDrawing && exactSectionViews.Count > 0)
                smallestExactView = exactSectionViews[0];

            if (isSinglePartDrawing)
            {
                foreach (View exactView in exactSectionViews)
                {
                    if (exactView == null)
                        continue;

                    ApplyExactRepresentationToView(exactView);
                    CommitAndWait(drawing, 250);
                }
            }

            List<View> dimViews = BuildDimViewsByViewTypeForH(
                topViewByType,
                frontViewByType,
                bottomViewByType,
                specialBottomSections
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

            View frontViewForTopBottomNotch =
                (dimViews.Count > 1) ? dimViews[1] : frontViewByType;
            ChamferInfluence frontNotchInfluenceForTopBottom =
                DetectFrontNotchInfluenceOnly(model, part, frontViewForTopBottomNotch);

            TopBoundary boundary;
            CreateDimsForTopView(model, part, topView, frontNotchInfluenceForTopBottom, out boundary);
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

            // FRONT VIEW: view thứ 2 sau khi đã loại view mặt cắt nhỏ Exact.
            // Top view giữ nguyên, front view chạy thêm sau top view.
            if (dimViews.Count > 1)
            {
                frontView = dimViews[1];

                CreateDimsForFrontView(
                    model,
                    part,
                    frontView,
                    isAssemblyDrawing,
                    out frontBoundary
                );
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

            // BOTTOM VIEW: cho phép người dùng tự cắt mặt bottom ra.
            // Không bắt buộc view phải có hướng nhìn ngược Top, vì mặt cắt bottom có thể không phải
            // là Bottom View chuẩn của Tekla. Các view phụ còn lại sau khi bỏ Top / Front / view nhỏ nhất Exact
            // sẽ được xem như bottom candidate và chạy DIM bằng thuật toán TOP.
            List<View> bottomViews = FindManualBottomCandidateViews(dimViews, topView, frontView, smallestExactView);
            List<TopBoundary> bottomBoundaries = new List<TopBoundary>();
            List<int> bottomTopDimTiers = new List<int>();
            foreach (View bottomView in bottomViews)
            {
                TopBoundary bottomBoundary;

                CreateDimsForBottomView(model, part, bottomView, frontNotchInfluenceForTopBottom, out bottomBoundary);
                bottomTopDimTiers.Add(LastBottomTopDimTier);
                CommitAndWait(drawing, 250);

                if (bottomBoundary.IsValid)
                {
                    ResizeViewBoundaryKeepDepth(
                        bottomView,
                        bottomBoundary.MinX,
                        bottomBoundary.MaxX,
                        bottomBoundary.MinY,
                        bottomBoundary.MaxY
                    );
                }
                else
                {
                    ResizeViewBoundaryKeepDepthBySolid(bottomView, model, part);
                }

                CommitAndWait(drawing, 250);
                bottomBoundaries.Add(bottomBoundary);
            }

            AlignMainViewsByGeometry(
                topView,
                boundary,
                frontView,
                frontBoundary,
                LastTopBottomDimTier,
                LastFrontTopDimTier
            );

            for (int i = 0; i < bottomViews.Count; i++)
            {
                View bottomView = bottomViews[i];
                TopBoundary bottomBoundary = (i < bottomBoundaries.Count) ? bottomBoundaries[i] : new TopBoundary();
                int bottomTopDimTier = (i < bottomTopDimTiers.Count) ? bottomTopDimTiers[i] : 1;

                AlignMainViewsByGeometry(
                    frontView,
                    frontBoundary,
                    bottomView,
                    bottomBoundary,
                    LastFrontBottomDimTier,
                    bottomTopDimTier
                );
            }

            const double finalGreenBoxGap = 15.0;
            ArrangeSectionViewRightOfFront(
                smallestExactView,
                frontView,
                frontBoundary,
                boundary,
                finalGreenBoxGap);

            // MOVE CENTER MỚI: dùng KHUNG TÍM RestrictionBox giống file plate OK-V3.
            // Không dùng CenterViewGroupOnSheet() cũ vì hàm cũ lấy biên hình học/tier DIM,
            // dễ làm tâm cụm view lệch sau khi DIM/mark làm khung xanh thay đổi.
            CenterShapeViewsByPurpleBoxOnSheet(drawing, topView, frontView, smallestExactView, bottomViews);
            CommitAndWait(drawing, 250);

            // ARRANGE CUỐI MỚI: dùng KHUNG XANH để ép gap 15 có tính cả DIM/mark.
            // Chỉ xử lý cụm Top / Front / Bottom. Section bên cạnh không tham gia gap dọc.
            ForceFinalEqualArrangeShapeTopFrontBottomGap15(
                topView,
                frontView,
                bottomViews,
                finalGreenBoxGap);
            CommitAndWait(drawing, 250);

            // ALIGN LẠI MẶT CẮT SAU KHI CENTER + GAP 15.
            // Giữ nguyên thuật toán ArrangeSectionViewRightOfFront(), chỉ gọi thêm 1 lần sau cùng
            // để mặt cắt A-A bám lại theo vị trí Front cuối cùng.
            ArrangeSectionViewRightOfFront(
                smallestExactView,
                frontView,
                frontBoundary,
                boundary,
                finalGreenBoxGap);
            CommitAndWait(drawing, 250);

            UpdateDrawingTitle3Scale(drawing, topView);
            CommitAndWait(drawing, 250);

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

        private class TopBottomFrontNotchChain
        {
            public bool HasLeft;
            public bool HasRight;
            public Point LeftOuter;
            public Point LeftInner;
            public Point RightOuter;
            public Point RightInner;
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

            // Chỉ dùng cho chân DIM lỗ Top/Bottom khi rãnh đã được nhận diện chắc chắn.
            // Mặt Front không dùng override này; chân DIM Front giữ theo thuật toán gốc.
            public bool HasLeftNotchHoleAnchor;
            public bool HasRightNotchHoleAnchor;
        }

        private sealed class DimOffsetAnchor4
        {
            public Point A;
            public Point B;
            public Point C;
            public Point D;
            public bool IsValid;
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

        private enum HShapeHoleFace
        {
            Top,
            Bottom,
            Front
        }

        private class HShapeHoleRecord
        {
            public int BoltGroupId;
            public Point ModelPoint;
            public HShapeHoleFace Face;
            public double TopBottomHoleDiameter;
            public double FrontHoleDiameter;
            public double SlotX;
            public double SlotY;
            public string HoleType;
        }

        private class HHoleCandidate
        {
            public Point Point;
            public double HoleDiameter;
            public double SlotX;
            public double SlotY;
            public string HoleType;
            public HShapeHoleFace Face;
            public int BoltGroupId;
        }

        private class HHoleClassification
        {
            public List<HHoleCandidate> TopCandidates = new List<HHoleCandidate>();
            public List<HHoleCandidate> BottomCandidates = new List<HHoleCandidate>();
            public List<HHoleCandidate> FrontCandidates = new List<HHoleCandidate>();
        }

        private class TopBottomHoleGroup
        {
            public List<Point> Holes = new List<Point>();

            // 0 = lỗ đơn, 1 = cụm ngang, 2 = cụm dọc, 3 = cụm 2 chiều.
            public int Type;

            // Dùng riêng cho cụm 2 chiều trái/phải giống nhau:
            // DIM ngang có thể nối chung, nhưng DIM dọc vẫn tách riêng từng cụm.
            public bool HorizontalDimDone;

            // Dùng khi xử lý ưu tiên lỗ đơn trước các cụm khác.
            public bool GroupDimDone;

            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public int XCount;
            public int YCount;
        }

        private static void InitializeHShapeHoleCatalog(
            Model model,
            ModelPart part,
            View topView,
            View frontView)
        {
            CurrentHShapeHoleCatalog.Clear();
            CurrentHShapeTopViewForHoleClassify = topView;
            CurrentHShapeFrontViewForHoleClassify = frontView;
            CurrentHShapeHoleCatalogInitialized = false;
            CurrentHShapeHoleCatalogReadSucceeded = false;

            if (model == null || part == null || topView == null || frontView == null)
                return;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                // BoltPositions and BoltGroup.GetCoordinateSystem() are returned in the
                // transformation plane in which the object was selected. Re-select all
                // model objects in global coordinates, classify once, then only project
                // the stored points into each drawing view.
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane()
                );

                ModelPart globalPart =
                    model.SelectModelObject(part.Identifier) as ModelPart;
                if (globalPart == null)
                    globalPart = part;

                Vector topNormal;
                Vector frontNormal;
                if (!TryGetHShapeViewNormal(topView, out topNormal) ||
                    !TryGetHShapeViewNormal(frontView, out frontNormal))
                    return;

                Solid solid = globalPart.GetSolid();
                double topMin;
                double topMax;
                double frontMin;
                double frontMax;

                if (!TryGetHShapeSolidProjectionRange(solid, topNormal, out topMin, out topMax))
                    return;
                if (!TryGetHShapeSolidProjectionRange(solid, frontNormal, out frontMin, out frontMax))
                    return;

                HashSet<int> addedBoltGroupIds = new HashSet<int>();
                bool modelBoltEnumerationCompleted = false;

                try
                {
                    ModelObjectEnumerator bolts = globalPart.GetBolts();
                    while (bolts.MoveNext())
                    {
                        ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                        AddHShapeBoltGroupToCatalog(
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

                    modelBoltEnumerationCompleted = true;
                }
                catch
                {
                }

                // Keep the old drawing-source coverage as a union source. This catches
                // unusual drawing/model ownership cases while the part-reference check
                // prevents nearby unrelated bolts from entering the catalog.
                AddHShapeDrawingBoltGroupsToCatalog(
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

                AddHShapeDrawingBoltGroupsToCatalog(
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

                CurrentHShapeHoleCatalogReadSucceeded =
                    modelBoltEnumerationCompleted ||
                    CurrentHShapeHoleCatalog.Count > 0;
            }
            catch
            {
            }
            finally
            {
                CurrentHShapeHoleCatalogInitialized = true;
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static void AddHShapeDrawingBoltGroupsToCatalog(
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
                    if (bg == null || !HShapeBoltGroupReferencesPart(bg, mainPart))
                        continue;

                    AddHShapeBoltGroupToCatalog(
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

        private static void AddHShapeBoltGroupToCatalog(
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
                    TryGetHShapeBoltDirection(bg, mainPart, out holeDirection);

                double topBottomHoleDiameter =
                    GetTopBottomRealHoleDiameterFromBoltGroup(bg);
                double frontHoleDiameter = GetHoleDiameterFromBoltGroup(bg);

                if (topBottomHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                    topBottomHoleDiameter = frontHoleDiameter;
                if (frontHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                    frontHoleDiameter = topBottomHoleDiameter;

                if (topBottomHoleDiameter <= MIN_VALID_HOLE_DIM_GAP ||
                    frontHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                {
                    double modelHoleDiameter = bg.BoltSize + Math.Max(0.0, bg.Tolerance);
                    if (topBottomHoleDiameter <= MIN_VALID_HOLE_DIM_GAP)
                        topBottomHoleDiameter = modelHoleDiameter;
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
                        direction = GetHShapePositionFallbackDirection(
                            modelPoint,
                            topNormal,
                            frontNormal,
                            topMin,
                            topMax,
                            frontMin,
                            frontMax
                        );
                    }

                    HShapeHoleRecord record = new HShapeHoleRecord();
                    record.BoltGroupId = boltGroupId;
                    record.ModelPoint = new Point(
                        modelPoint.X,
                        modelPoint.Y,
                        modelPoint.Z
                    );
                    record.Face = ClassifyHShapeHoleFace(
                        bg,
                        modelPoint,
                        direction,
                        topNormal,
                        frontNormal,
                        topMin,
                        topMax,
                        frontMin,
                        frontMax
                    );
                    record.TopBottomHoleDiameter = topBottomHoleDiameter;
                    record.FrontHoleDiameter = frontHoleDiameter;
                    record.SlotX = slotX;
                    record.SlotY = slotY;
                    record.HoleType = holeType;

                    AddUniqueHShapeHoleRecord(record);
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

        private static void AddUniqueHShapeHoleRecord(HShapeHoleRecord record)
        {
            if (record == null || record.ModelPoint == null)
                return;

            foreach (HShapeHoleRecord existing in CurrentHShapeHoleCatalog)
            {
                if (existing == null || existing.ModelPoint == null)
                    continue;

                if (record.BoltGroupId != 0 &&
                    existing.BoltGroupId != record.BoltGroupId)
                    continue;

                double dx = existing.ModelPoint.X - record.ModelPoint.X;
                double dy = existing.ModelPoint.Y - record.ModelPoint.Y;
                double dz = existing.ModelPoint.Z - record.ModelPoint.Z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= 0.5)
                    return;
            }

            CurrentHShapeHoleCatalog.Add(record);
        }

        private static HShapeHoleFace ClassifyHShapeHoleFace(
            ModelBoltGroup bg,
            Point modelPoint,
            Vector holeDirection,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax)
        {
            double topAlignment = Math.Abs(DotHShapeVectors(holeDirection, topNormal));
            double frontAlignment = Math.Abs(DotHShapeVectors(holeDirection, frontNormal));
            bool isFlangeHole;

            if (Math.Abs(topAlignment - frontAlignment) > H_HOLE_DIRECTION_TIE_TOL)
            {
                isFlangeHole = topAlignment > frontAlignment;
            }
            else
            {
                // Oblique/tied directions still receive a face. The closest real outer
                // solid surface is used only as a deterministic fallback.
                double topCoordinate = DotHShapePointVector(modelPoint, topNormal);
                double frontCoordinate = DotHShapePointVector(modelPoint, frontNormal);
                double topDistance = GetHShapeNormalizedOuterSurfaceDistance(
                    topCoordinate,
                    topMin,
                    topMax
                );
                double frontDistance = GetHShapeNormalizedOuterSurfaceDistance(
                    frontCoordinate,
                    frontMin,
                    frontMax
                );
                isFlangeHole = topDistance <= frontDistance;
            }

            if (!isFlangeHole)
                return HShapeHoleFace.Front;

            double topCenter = (topMin + topMax) / 2.0;
            double side = DotHShapePointVector(modelPoint, topNormal) - topCenter;

            if (Math.Abs(side) <= H_HOLE_CENTER_SIDE_TOL)
            {
                try
                {
                    CoordinateSystem boltCs = bg.GetCoordinateSystem();
                    if (boltCs != null && boltCs.Origin != null)
                        side = DotHShapePointVector(boltCs.Origin, topNormal) - topCenter;
                }
                catch
                {
                }
            }

            if (Math.Abs(side) <= H_HOLE_CENTER_SIDE_TOL)
                side = DotHShapeVectors(holeDirection, topNormal);

            return side >= 0.0
                ? HShapeHoleFace.Top
                : HShapeHoleFace.Bottom;
        }

        private static Vector GetHShapePositionFallbackDirection(
            Point modelPoint,
            Vector topNormal,
            Vector frontNormal,
            double topMin,
            double topMax,
            double frontMin,
            double frontMax)
        {
            double topDistance = GetHShapeNormalizedOuterSurfaceDistance(
                DotHShapePointVector(modelPoint, topNormal),
                topMin,
                topMax
            );
            double frontDistance = GetHShapeNormalizedOuterSurfaceDistance(
                DotHShapePointVector(modelPoint, frontNormal),
                frontMin,
                frontMax
            );

            return topDistance <= frontDistance
                ? new Vector(topNormal.X, topNormal.Y, topNormal.Z)
                : new Vector(frontNormal.X, frontNormal.Y, frontNormal.Z);
        }

        private static bool TryGetHShapeBoltDirection(
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
                    Vector normal = CrossHShapeVectors(cs.AxisX, cs.AxisY);
                    if (TryNormalizeHShapeVector(normal, out direction))
                        return true;
                }
            }
            catch
            {
            }

            return TryGetHShapeConnectedPartDirection(bg, mainPart, out direction);
        }

        private static bool TryGetHShapeConnectedPartDirection(
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
                if (!TryGetHShapePartSolidCenter(mainPart, out mainCenter))
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
                    if (!TryGetHShapePartSolidCenter(connectedPart, out center))
                        continue;

                    Vector delta = new Vector(
                        center.X - mainCenter.X,
                        center.Y - mainCenter.Y,
                        center.Z - mainCenter.Z
                    );
                    double length = GetHShapeVectorLength(delta);
                    if (length <= bestLength)
                        continue;

                    Vector normalized;
                    if (!TryNormalizeHShapeVector(delta, out normalized))
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

        private static bool TryGetHShapePartSolidCenter(
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

        private static bool HShapeBoltGroupReferencesPart(
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

        private static bool TryGetHShapeViewNormal(
            View view,
            out Vector normal)
        {
            normal = new Vector(0, 0, 0);

            try
            {
                CoordinateSystem cs = view.DisplayCoordinateSystem;
                if (cs == null || cs.AxisX == null || cs.AxisY == null)
                    return false;

                return TryNormalizeHShapeVector(
                    CrossHShapeVectors(cs.AxisX, cs.AxisY),
                    out normal
                );
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetHShapeSolidProjectionRange(
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

                                double value = DotHShapePointVector(point, axis);
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
                            double value = DotHShapePointVector(
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
            }
            catch
            {
                return false;
            }

            return hasValue && Math.Abs(maxValue - minValue) > 0.001;
        }

        private static double GetHShapeNormalizedOuterSurfaceDistance(
            double coordinate,
            double minValue,
            double maxValue)
        {
            double range = Math.Max(Math.Abs(maxValue - minValue), 1.0);
            return Math.Min(
                Math.Abs(coordinate - minValue),
                Math.Abs(coordinate - maxValue)
            ) / range;
        }

        private static Vector CrossHShapeVectors(Vector a, Vector b)
        {
            if (a == null || b == null)
                return new Vector(0, 0, 0);

            return new Vector(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
        }

        private static double DotHShapeVectors(Vector a, Vector b)
        {
            if (a == null || b == null)
                return 0.0;

            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        private static double DotHShapePointVector(Point point, Vector vector)
        {
            if (point == null || vector == null)
                return 0.0;

            return point.X * vector.X + point.Y * vector.Y + point.Z * vector.Z;
        }

        private static double GetHShapeVectorLength(Vector vector)
        {
            if (vector == null)
                return 0.0;

            return Math.Sqrt(
                vector.X * vector.X +
                vector.Y * vector.Y +
                vector.Z * vector.Z
            );
        }

        private static bool TryNormalizeHShapeVector(
            Vector vector,
            out Vector normalized)
        {
            normalized = new Vector(0, 0, 0);
            double length = GetHShapeVectorLength(vector);
            if (length <= 0.000001)
                return false;

            normalized = new Vector(
                vector.X / length,
                vector.Y / length,
                vector.Z / length
            );
            return true;
        }

        private static bool TryGetHShapePartLocalPoint(
            ModelPart part,
            Point modelPoint,
            out double lx,
            out double ly,
            out double lz)
        {
            lx = 0.0;
            ly = 0.0;
            lz = 0.0;

            try
            {
                if (part == null || modelPoint == null)
                    return false;

                CoordinateSystem cs = part.GetCoordinateSystem();
                if (cs == null || cs.Origin == null || cs.AxisX == null || cs.AxisY == null)
                    return false;

                double axx = cs.AxisX.X;
                double axy = cs.AxisX.Y;
                double axz = cs.AxisX.Z;

                double ayx = cs.AxisY.X;
                double ayy = cs.AxisY.Y;
                double ayz = cs.AxisY.Z;

                NormalizeHShapeVector(ref axx, ref axy, ref axz);
                NormalizeHShapeVector(ref ayx, ref ayy, ref ayz);

                double azx = axy * ayz - axz * ayy;
                double azy = axz * ayx - axx * ayz;
                double azz = axx * ayy - axy * ayx;
                NormalizeHShapeVector(ref azx, ref azy, ref azz);

                double dx = modelPoint.X - cs.Origin.X;
                double dy = modelPoint.Y - cs.Origin.Y;
                double dz = modelPoint.Z - cs.Origin.Z;

                lx = dx * axx + dy * axy + dz * axz;
                ly = dx * ayx + dy * ayy + dz * ayz;
                lz = dx * azx + dy * azy + dz * azz;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void NormalizeHShapeVector(ref double x, ref double y, ref double z)
        {
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len <= 0.000001)
                return;

            x /= len;
            y /= len;
            z /= len;
        }

        private static bool TryGetHShapePartLocalYRange(
            ModelPart part,
            out double minLocalY,
            out double maxLocalY)
        {
            minLocalY = 0.0;
            maxLocalY = 0.0;

            try
            {
                if (part == null)
                    return false;

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return false;

                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;
                bool hasValue = false;

                double[] xs = new double[] { min.X, max.X };
                double[] ys = new double[] { min.Y, max.Y };
                double[] zs = new double[] { min.Z, max.Z };

                foreach (double x in xs)
                {
                    foreach (double y in ys)
                    {
                        foreach (double z in zs)
                        {
                            double lx, ly, lz;
                            if (!TryGetHShapePartLocalPoint(part, new Point(x, y, z), out lx, out ly, out lz))
                                continue;

                            if (!hasValue)
                            {
                                minLocalY = ly;
                                maxLocalY = ly;
                                hasValue = true;
                            }
                            else
                            {
                                if (ly < minLocalY) minLocalY = ly;
                                if (ly > maxLocalY) maxLocalY = ly;
                            }
                        }
                    }
                }

                return hasValue && Math.Abs(maxLocalY - minLocalY) > 1.0;
            }
            catch
            {
                return false;
            }
        }

        private static List<HHoleCandidate> GetHHoleCandidatesInCurrentPlane(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool useTopBottomRealHoleDiameter)
        {
            List<HHoleCandidate> result = new List<HHoleCandidate>();

            try
            {
                if ((!CurrentHShapeHoleCatalogInitialized ||
                     CurrentHShapeHoleCatalog.Count == 0) &&
                    CurrentHShapeHolePartForLocalClassify != null &&
                    CurrentHShapeTopViewForHoleClassify != null &&
                    CurrentHShapeFrontViewForHoleClassify != null)
                {
                    InitializeHShapeHoleCatalog(
                        model,
                        CurrentHShapeHolePartForLocalClassify,
                        CurrentHShapeTopViewForHoleClassify,
                        CurrentHShapeFrontViewForHoleClassify
                    );
                }

                if (view == null || view.DisplayCoordinateSystem == null)
                    return result;

                Tekla.Structures.Geometry3d.Matrix toView =
                    MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                Dictionary<int, double> drawingDiameterFallbacks =
                    GetHShapeDrawingHoleDiameterFallbacks(view);

                foreach (HShapeHoleRecord record in CurrentHShapeHoleCatalog)
                {
                    if (record == null || record.ModelPoint == null)
                        continue;

                    Point p = toView.Transform(record.ModelPoint);
                    if (p == null)
                        continue;

                    if (p.X < minX - 10.0 ||
                        p.X > maxX + 10.0 ||
                        p.Y < minY - 10.0 ||
                        p.Y > maxY + 10.0)
                        continue;

                    double holeDiameter = useTopBottomRealHoleDiameter
                        ? record.TopBottomHoleDiameter
                        : record.FrontHoleDiameter;

                    double drawingDiameter;
                    if (holeDiameter <= MIN_VALID_HOLE_DIM_GAP &&
                        drawingDiameterFallbacks.TryGetValue(
                            record.BoltGroupId,
                            out drawingDiameter))
                    {
                        holeDiameter = drawingDiameter;
                    }

                    HHoleCandidate item = new HHoleCandidate();
                    item.Point = new Point(p.X, p.Y, p.Z);
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

        private static Dictionary<int, double> GetHShapeDrawingHoleDiameterFallbacks(
            View view)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();

            try
            {
                if (view == null)
                    return result;

                DrawingObjectEnumerator boltObjects =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));

                while (boltObjects.MoveNext())
                {
                    DrawingObject drawingBolt = boltObjects.Current as DrawingObject;
                    Identifier id = TryGetModelIdentifier(drawingBolt);
                    if (id == null || result.ContainsKey(id.ID))
                        continue;

                    double diameter = GetHoleDiameterFromDrawingBolt(drawingBolt);
                    if (diameter > MIN_VALID_HOLE_DIM_GAP)
                        result.Add(id.ID, diameter);
                }
            }
            catch
            {
            }

            return result;
        }

        private static HHoleClassification ClassifyHHoleCandidates(
            List<HHoleCandidate> holes)
        {
            HHoleClassification result = new HHoleClassification();

            try
            {
                if (holes == null || holes.Count == 0)
                    return result;

                foreach (HHoleCandidate item in holes)
                {
                    if (item == null || item.Point == null)
                        continue;

                    if (item.Face == HShapeHoleFace.Top)
                    {
                        result.TopCandidates.Add(item);
                    }
                    else if (item.Face == HShapeHoleFace.Bottom)
                    {
                        result.BottomCandidates.Add(item);
                    }
                    else
                    {
                        result.FrontCandidates.Add(item);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<Point> ConvertHHoleCandidatesToDimPoints(List<HHoleCandidate> holes)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (holes == null)
                    return result;

                foreach (HHoleCandidate item in holes)
                {
                    if (item == null || item.Point == null)
                        continue;

                    AddUniquePoint(
                        result,
                        new Point(item.Point.X, item.Point.Y, item.HoleDiameter),
                        1.0
                    );
                }
            }
            catch
            {
            }

            return result;
        }

        private static void CheckTopBottomHolesAndMark(
            Model model,
            ModelPart part,
            View topView)
        {
            TopBottomHoleCheckResult = -1;

            if (model == null || part == null || topView == null ||
                !CurrentHShapeHoleCatalogInitialized ||
                !CurrentHShapeHoleCatalogReadSucceeded)
                return;

            TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(topView.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                Point solidMin = solid.MinimumPoint;
                Point solidMax = solid.MaximumPoint;

                // TOP PROJECTED POLYGON: dùng cho DIM tổng/chamfer/rãnh và DIM NGANG lỗ.
                List<Point> topPolygon = GetTopFacePolygon(solid, solidMin, solidMax);

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

                List<HoleCheckInfo> topHoles;
                if (!TryGetTopBottomCheckHolesFromView(
                    topView,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    HShapeHoleFace.Top,
                    out topHoles))
                    return;

                List<HoleCheckInfo> bottomHoles;
                if (!TryGetTopBottomCheckHolesFromView(
                    topView,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    HShapeHoleFace.Bottom,
                    out bottomHoles))
                    return;

                bool holesDifferent;
                if (!TryAreTopBottomHolesDifferent(
                    topHoles,
                    bottomHoles,
                    minX,
                    maxX,
                    out holesDifferent))
                    return;

                TopBottomHoleCheckResult = holesDifferent ? 1 : 0;
            }
            catch
            {
            }
            finally
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static int CheckTopBottomFlangeContourDifference(
            Model model,
            ModelPart part,
            View topView)
        {
            if (model == null || part == null || topView == null)
                return -1;

            TransformationPlane oldPlane = null;

            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(topView.DisplayCoordinateSystem)
                );

                Solid solid = part.GetSolid();
                if (solid == null)
                    return -1;

                Point solidMin = solid.MinimumPoint;
                Point solidMax = solid.MaximumPoint;
                double totalDepth = Math.Abs(solidMax.Z - solidMin.Z);

                if (totalDepth <= 2.0)
                    return -1;

                double flangeThickness = GetFlangeThicknessFromProfile(part);
                if (flangeThickness <= 1.0)
                    flangeThickness = Math.Min(20.0, totalDepth * 0.20);

                // Không để vùng dò cánh trên/dưới chạm nhau trên profile thấp hoặc lạ.
                flangeThickness = Math.Min(flangeThickness, totalDepth * 0.40);
                if (flangeThickness <= 1.0)
                    return -1;

                double minInset = Math.Min(0.5, flangeThickness * 0.10);
                double maxInset = flangeThickness - minInset;
                if (maxInset <= minInset)
                    return -1;

                List<double> probeDepths = new List<double>();
                double[] fractions = new double[] { 0.15, 0.50, 0.85 };

                foreach (double fraction in fractions)
                {
                    double depth = flangeThickness * fraction;
                    if (depth < minInset) depth = minInset;
                    if (depth > maxInset) depth = maxInset;
                    AddUniqueCoordinate(probeDepths, depth, 0.25);
                }

                int validPairCount = 0;

                foreach (double depth in probeDepths)
                {
                    double topZ = solidMax.Z - depth;
                    double bottomZ = solidMin.Z + depth;

                    if (bottomZ >= topZ - TOP_BOTTOM_CONTOUR_COMPARE_TOL)
                        continue;

                    List<Point> topContour = GetFlangeContourAtZ(
                        solid,
                        solidMin,
                        solidMax,
                        topZ
                    );
                    List<Point> bottomContour = GetFlangeContourAtZ(
                        solid,
                        solidMin,
                        solidMax,
                        bottomZ
                    );

                    bool topValid = topContour != null && topContour.Count >= 3;
                    bool bottomValid = bottomContour != null && bottomContour.Count >= 3;

                    if (topValid != bottomValid)
                        return 1;

                    if (!topValid)
                        continue;

                    validPairCount++;

                    if (AreTopBottomFlangeContoursDifferent(
                        topContour,
                        bottomContour,
                        TOP_BOTTOM_CONTOUR_COMPARE_TOL))
                    {
                        return 1;
                    }
                }

                return validPairCount > 0 ? 0 : -1;
            }
            catch
            {
                return -1;
            }
            finally
            {
                if (oldPlane != null)
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
        }

        private static List<Point> GetFlangeContourAtZ(
            Solid solid,
            Point solidMin,
            Point solidMax,
            double z)
        {
            try
            {
                Point p1 = new Point(solidMin.X - 1000.0, solidMin.Y - 1000.0, z);
                Point p2 = new Point(solidMax.X + 1000.0, solidMin.Y - 1000.0, z);
                Point p3 = new Point(solidMin.X - 1000.0, solidMax.Y + 1000.0, z);

                return GetLargestIntersectionPolygon(
                    solid.IntersectAllFaces(p1, p2, p3)
                );
            }
            catch
            {
                return new List<Point>();
            }
        }

        private static bool AreTopBottomFlangeContoursDifferent(
            List<Point> topContour,
            List<Point> bottomContour,
            double tolerance)
        {
            if (topContour == null || bottomContour == null ||
                topContour.Count < 3 || bottomContour.Count < 3)
            {
                return true;
            }

            double topMinX, topMaxX, topMinY, topMaxY;
            double bottomMinX, bottomMaxX, bottomMinY, bottomMaxY;

            GetMinMax(topContour, out topMinX, out topMaxX, out topMinY, out topMaxY);
            GetMinMax(bottomContour, out bottomMinX, out bottomMaxX, out bottomMinY, out bottomMaxY);

            if (Math.Abs(topMinX - bottomMinX) > tolerance ||
                Math.Abs(topMaxX - bottomMaxX) > tolerance ||
                Math.Abs(topMinY - bottomMinY) > tolerance ||
                Math.Abs(topMaxY - bottomMaxY) > tolerance)
            {
                return true;
            }

            foreach (Point point in topContour)
            {
                if (point != null &&
                    !IsPointNearClosedContour(point, bottomContour, tolerance))
                {
                    return true;
                }
            }

            foreach (Point point in bottomContour)
            {
                if (point != null &&
                    !IsPointNearClosedContour(point, topContour, tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointNearClosedContour(
            Point point,
            List<Point> contour,
            double tolerance)
        {
            if (point == null || contour == null || contour.Count < 2)
                return false;

            for (int i = 0; i < contour.Count; i++)
            {
                Point start = contour[i];
                Point end = contour[(i + 1) % contour.Count];

                if (start == null || end == null)
                    continue;

                if (DistancePointToSegment2D(point, start, end) <= tolerance)
                    return true;
            }

            return false;
        }

        private static double DistancePointToSegment2D(
            Point point,
            Point start,
            Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared <= 0.000001)
                return Distance2D(point, start);

            double t =
                ((point.X - start.X) * dx +
                 (point.Y - start.Y) * dy) /
                lengthSquared;

            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            Point projection = new Point(
                start.X + t * dx,
                start.Y + t * dy,
                0
            );

            return Distance2D(point, projection);
        }

        private static bool TryGetTopBottomCheckHolesFromView(
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY,
            HShapeHoleFace requestedFace,
            out List<HoleCheckInfo> result)
        {
            result = new List<HoleCheckInfo>();

            try
            {
                if (view == null || view.DisplayCoordinateSystem == null ||
                    !CurrentHShapeHoleCatalogInitialized ||
                    !CurrentHShapeHoleCatalogReadSucceeded)
                    return false;

                Tekla.Structures.Geometry3d.Matrix toView =
                    MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                Dictionary<int, double> drawingDiameterFallbacks =
                    GetHShapeDrawingHoleDiameterFallbacks(view);

                foreach (HShapeHoleRecord record in CurrentHShapeHoleCatalog)
                {
                    if (record == null || record.ModelPoint == null ||
                        record.Face != requestedFace)
                        continue;

                    Point point = toView.Transform(record.ModelPoint);
                    if (point == null)
                        return false;

                    if (point.X < minX - 10.0 ||
                        point.X > maxX + 10.0 ||
                        point.Y < minY - 10.0 ||
                        point.Y > maxY + 10.0)
                        continue;

                    double holeDiameter = record.TopBottomHoleDiameter;
                    double drawingDiameter;
                    if (holeDiameter <= MIN_VALID_HOLE_DIM_GAP &&
                        drawingDiameterFallbacks.TryGetValue(
                            record.BoltGroupId,
                            out drawingDiameter))
                    {
                        holeDiameter = drawingDiameter;
                    }

                    HoleCheckInfo h = new HoleCheckInfo();
                    h.X = point.X;
                    h.Y = point.Y;
                    h.Diameter = holeDiameter;
                    h.SlotX = record.SlotX;
                    h.SlotY = record.SlotY;
                    h.HoleType = record.HoleType;
                    h.Matched = false;

                    AddUniqueHoleCheckInfo(result, h, TOP_BOTTOM_HOLE_POSITION_TOL);
                }
            }
            catch
            {
                result = new List<HoleCheckInfo>();
                return false;
            }

            result.Sort(delegate (HoleCheckInfo a, HoleCheckInfo b)
            {
                int c = a.X.CompareTo(b.X);
                if (c != 0) return c;
                return a.Y.CompareTo(b.Y);
            });

            return true;
        }

        private static List<HHoleCandidate> SelectHShapeCheckCandidatesByOldZHint(
            List<HHoleCandidate> topCandidates,
            List<HHoleCandidate> bottomCandidates,
            double targetZ)
        {
            if (topCandidates == null)
                topCandidates = new List<HHoleCandidate>();
            if (bottomCandidates == null)
                bottomCandidates = new List<HHoleCandidate>();

            double topZ = GetAverageHShapeCandidateViewZ(topCandidates);
            double bottomZ = GetAverageHShapeCandidateViewZ(bottomCandidates);

            if (topCandidates.Count == 0)
                return targetZ <= bottomZ ? bottomCandidates : new List<HHoleCandidate>();
            if (bottomCandidates.Count == 0)
                return targetZ >= topZ ? topCandidates : new List<HHoleCandidate>();

            if (Math.Abs(targetZ - topZ) <= Math.Abs(targetZ - bottomZ))
                return topCandidates;

            return bottomCandidates;
        }

        private static double GetAverageHShapeCandidateViewZ(List<HHoleCandidate> holes)
        {
            try
            {
                if (holes == null || holes.Count == 0)
                    return 0.0;

                double sum = 0.0;
                int count = 0;

                foreach (HHoleCandidate item in holes)
                {
                    if (item == null || item.Point == null)
                        continue;

                    sum += item.Point.Z;
                    count++;
                }

                if (count > 0)
                    return sum / count;
            }
            catch
            {
            }

            return 0.0;
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

        private static bool TryAreTopBottomHolesDifferent(
            List<HoleCheckInfo> topHoles,
            List<HoleCheckInfo> bottomHoles,
            double minX,
            double maxX,
            out bool holesDifferent)
        {
            holesDifferent = false;

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

                MatchCrossFaceSymmetricSingleHoles(
                    topHoles,
                    bottomHoles,
                    minX,
                    maxX
                );

                foreach (HoleCheckInfo top in topHoles)
                {
                    if (!top.Matched)
                    {
                        holesDifferent = true;
                        return true;
                    }
                }

                foreach (HoleCheckInfo bottom in bottomHoles)
                {
                    if (!bottom.Matched)
                    {
                        holesDifferent = true;
                        return true;
                    }
                }

                return true;
            }
            catch
            {
                holesDifferent = false;
                return false;
            }
        }

        private static void MatchCrossFaceSymmetricSingleHoles(
            List<HoleCheckInfo> topHoles,
            List<HoleCheckInfo> bottomHoles,
            double minX,
            double maxX)
        {
            if (topHoles == null || bottomHoles == null ||
                Math.Abs(maxX - minX) <= TOP_BOTTOM_HOLE_POSITION_TOL)
                return;

            List<HoleCheckInfo> topSingleHoles =
                GetCrossFaceSymmetrySingleHoles(topHoles, minX, maxX);
            List<HoleCheckInfo> bottomSingleHoles =
                GetCrossFaceSymmetrySingleHoles(bottomHoles, minX, maxX);
            double centerX = (minX + maxX) / 2.0;

            foreach (HoleCheckInfo top in topSingleHoles)
            {
                if (top == null || top.Matched ||
                    Math.Abs(top.X - centerX) <= TOP_BOTTOM_HOLE_POSITION_TOL)
                    continue;

                bool topIsLeft = top.X < centerX;
                double topEdgeDistance = topIsLeft
                    ? Math.Abs(top.X - minX)
                    : Math.Abs(maxX - top.X);
                HoleCheckInfo bestBottom = null;
                double bestEdgeDifference = 999999999.0;

                foreach (HoleCheckInfo bottom in bottomSingleHoles)
                {
                    if (bottom == null || bottom.Matched ||
                        Math.Abs(bottom.X - centerX) <= TOP_BOTTOM_HOLE_POSITION_TOL)
                        continue;

                    bool bottomIsLeft = bottom.X < centerX;
                    if (bottomIsLeft == topIsLeft ||
                        !HaveSameHoleCheckShape(top, bottom))
                        continue;

                    double bottomEdgeDistance = bottomIsLeft
                        ? Math.Abs(bottom.X - minX)
                        : Math.Abs(maxX - bottom.X);
                    double edgeDifference =
                        Math.Abs(topEdgeDistance - bottomEdgeDistance);

                    if (edgeDifference <= TOP_BOTTOM_CROSS_FACE_SYMMETRY_EDGE_TOL &&
                        edgeDifference < bestEdgeDifference)
                    {
                        bestEdgeDifference = edgeDifference;
                        bestBottom = bottom;
                    }
                }

                if (bestBottom != null)
                {
                    top.Matched = true;
                    bestBottom.Matched = true;
                }
            }
        }

        private static List<HoleCheckInfo> GetCrossFaceSymmetrySingleHoles(
            List<HoleCheckInfo> holes,
            double minX,
            double maxX)
        {
            List<HoleCheckInfo> result = new List<HoleCheckInfo>();
            if (holes == null || holes.Count == 0)
                return result;

            List<List<HoleCheckInfo>> shapeGroups =
                new List<List<HoleCheckInfo>>();

            foreach (HoleCheckInfo hole in holes)
            {
                if (hole == null)
                    continue;

                List<HoleCheckInfo> targetGroup = null;
                foreach (List<HoleCheckInfo> shapeGroup in shapeGroups)
                {
                    if (shapeGroup != null && shapeGroup.Count > 0 &&
                        HaveSameHoleCheckShape(shapeGroup[0], hole))
                    {
                        targetGroup = shapeGroup;
                        break;
                    }
                }

                if (targetGroup == null)
                {
                    targetGroup = new List<HoleCheckInfo>();
                    shapeGroups.Add(targetGroup);
                }

                targetGroup.Add(hole);
            }

            foreach (List<HoleCheckInfo> shapeGroup in shapeGroups)
            {
                if (shapeGroup == null || shapeGroup.Count == 0)
                    continue;

                List<Point> points = new List<Point>();
                foreach (HoleCheckInfo hole in shapeGroup)
                {
                    if (hole != null)
                        points.Add(new Point(hole.X, hole.Y, hole.Diameter));
                }

                List<TopBottomHoleGroup> geometryGroups =
                    BuildTopBottomHoleGroupsByGeometry(points, minX, maxX);

                foreach (TopBottomHoleGroup geometryGroup in geometryGroups)
                {
                    if (geometryGroup == null || geometryGroup.Type != 0 ||
                        geometryGroup.Holes == null ||
                        geometryGroup.Holes.Count != 1)
                        continue;

                    Point singlePoint = geometryGroup.Holes[0];
                    foreach (HoleCheckInfo hole in shapeGroup)
                    {
                        if (hole == null || result.Contains(hole))
                            continue;

                        if (Math.Abs(hole.X - singlePoint.X) <= TOP_BOTTOM_HOLE_POSITION_TOL &&
                            Math.Abs(hole.Y - singlePoint.Y) <= TOP_BOTTOM_HOLE_POSITION_TOL)
                        {
                            result.Add(hole);
                            break;
                        }
                    }
                }
            }

            return result;
        }

        private static bool HaveSameHoleCheckShape(
            HoleCheckInfo a,
            HoleCheckInfo b)
        {
            if (a == null || b == null)
                return false;

            return Math.Abs(a.Diameter - b.Diameter) <= TOP_BOTTOM_HOLE_SIZE_TOL &&
                   Math.Abs(a.SlotX - b.SlotX) <= TOP_BOTTOM_HOLE_SIZE_TOL &&
                   Math.Abs(a.SlotY - b.SlotY) <= TOP_BOTTOM_HOLE_SIZE_TOL &&
                   SameText(a.HoleType, b.HoleType);
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
            ChamferInfluence frontNotchInfluence,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();
            int count = 0;
            LastTopBottomDimTier = 1;

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

                bool chamferDimCreated = false;
                ChamferInfluence chamferInfluence = new ChamferInfluence();
                ChamferEdgeAnchors edgeAnchors = BuildChamferEdgeAnchors(topPolygon, minX, maxX, minY, maxY);

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
                bool leftChamferTierReserved = false;
                bool rightChamferTierReserved = false;

                ChamferInfluence notchInfluence = new ChamferInfluence();
                int notchCount = 0;
                int leftNotchTierCount = 0;
                int rightNotchTierCount = 0;

                // TOP VIEW: không DIM rãnh/notch để tránh bắt nhầm rãnh mặt Front chiếu lên Top.
                // Chamfer ngoài vẫn giữ nguyên vì đã chạy ở CreateTopViewChamferDims phía trên.
                TopBottomFrontNotchChain topFrontNotchChain = null;
                if (frontNotchInfluence.Top)
                {
                    TryDetectTopBottomFrontNotchChain(
                        part,
                        solid,
                        topPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        flangeThickness,
                        true,
                        out topFrontNotchChain
                    );
                }

                if (topFrontNotchChain != null)
                {
                    ApplyTopBottomFrontNotchTotalAnchors(ref edgeAnchors, topFrontNotchChain, topPolygon, minX, maxX, minY, maxY);
                    ApplyTopBottomFrontNotchHoleAnchors(ref edgeAnchors, topFrontNotchChain, minY, maxY);
                }

                DimOffsetAnchor4 offsetAnchors =
                    BuildDimOffsetAnchor4(edgeAnchors);

                List<Point> independentSectionFacePolygon =
                    new List<Point>();

                if (topFrontNotchChain == null &&
                    !frontNotchInfluence.Top &&
                    ViewTypeMatchesForH(view, "SectionView", "Section"))
                {
                    independentSectionFacePolygon =
                        GetIndependentSectionFacePolygon(
                            view,
                            solid,
                            solidMin,
                            solidMax);
                }

                // Sau khi đã gộp chamfer + notch, mới xác định phía nào bị chiếm tầng 1.
                // Chỉ phía bị ảnh hưởng mới nhảy tầng, các phía khác giữ tầng như cũ.
                topChamferTierReserved = chamferInfluence.Top;
                bottomChamferTierReserved = chamferInfluence.Bottom;
                leftChamferTierReserved = chamferInfluence.Left;
                rightChamferTierReserved = chamferInfluence.Right;

                List<Point> topFlangeHoles =
                    GetVisibleTopFlangeBoltCentersFromView(
                        model,
                        view,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );

                bool holeDimCreated = false;
                int topHoleTierCount = 0;
                int bottomHoleTierCount = 0;
                int leftHoleTierCount = 0;
                int rightHoleTierCount = 0;

                if (topFlangeHoles.Count > 0)
                {
                    int holeCount = 0;

                    holeCount += CreateTopViewHoleDimsByDiameter(
                        handler,
                        view,
                        topFlangeHoles,
                        topPolygon,
                        topHoleVerticalPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        holeVerticalMinY,
                        holeVerticalMaxY,
                        edgeAnchors,
                        offsetAnchors,
                        beamLength,
                        topChamferTierReserved ? 1 : 0,
                        bottomChamferTierReserved ? 1 : 0,
                        leftChamferTierReserved ? 1 : 0,
                        rightChamferTierReserved ? 1 : 0,
                        out topHoleTierCount,
                        out bottomHoleTierCount,
                        out leftHoleTierCount,
                        out rightHoleTierCount
                    );

                    holeDimCreated = holeCount > 0;
                    count += holeCount;
                }

                // TOP VIEW - QUY TẮC TẦNG DIM LỖ:
                // Mỗi nhóm phi/cụm dim đại diện chiếm đúng 1 tầng.
                // Ví dụ: lỗ đơn đối xứng ưu tiên tầng 1, cụm khác theo thứ tự hình học...
                // DIM tổng luôn nằm ở tầng cuối cùng sau toàn bộ nhóm lỗ.
                int topReservedTier = topChamferTierReserved ? 1 : 0;
                int topHorizontalTier = topReservedTier + (holeDimCreated ? (topHoleTierCount + 1) : 1);

                double notchHorizontalOffset = GetSteelDimOffsetByTier(topHorizontalTier);
                int bottomReservedTier = bottomChamferTierReserved ? 1 : 0;
                int bottomHorizontalNotchTier = bottomReservedTier + bottomHoleTierCount + 1;
                double bottomNotchHorizontalOffset = GetSteelDimOffsetByTier(bottomHorizontalNotchTier);

                bool independentSectionFaceBottomNotchCreated = false;

                if (independentSectionFacePolygon.Count >= 4)
                {
                    double faceMinX;
                    double faceMaxX;
                    double faceMinY;
                    double faceMaxY;
                    GetMinMax(
                        independentSectionFacePolygon,
                        out faceMinX,
                        out faceMaxX,
                        out faceMinY,
                        out faceMaxY);

                    int independentLeftTierCount;
                    int independentRightTierCount;
                    ChamferInfluence sectionFaceNotchInfluence;
                    int sectionFaceNotchCount =
                        CreateIndependentSectionFaceNotchDims(
                            handler,
                            view,
                            independentSectionFacePolygon,
                            offsetAnchors,
                            faceMinX,
                            faceMaxX,
                            faceMinY,
                            faceMaxY,
                            notchHorizontalOffset,
                            bottomNotchHorizontalOffset,
                            (leftChamferTierReserved ? 1 : 0) + leftHoleTierCount + 1,
                            (rightChamferTierReserved ? 1 : 0) + rightHoleTierCount + 1,
                            out sectionFaceNotchInfluence,
                            out independentLeftTierCount,
                            out independentRightTierCount);

                    if (sectionFaceNotchCount > 0)
                    {
                        MergeInfluence(
                            ref chamferInfluence,
                            sectionFaceNotchInfluence);
                        count += sectionFaceNotchCount;
                        leftNotchTierCount += independentLeftTierCount;
                        rightNotchTierCount += independentRightTierCount;

                        if (sectionFaceNotchInfluence.Top)
                            topHorizontalTier++;

                        if (sectionFaceNotchInfluence.Bottom)
                            independentSectionFaceBottomNotchCreated = true;
                    }
                }

                if (topFrontNotchChain != null)
                {
                    notchCount = CreateTopBottomFrontNotchChainDims(
                        handler,
                        view,
                        topFrontNotchChain,
                        topPolygon,
                        offsetAnchors,
                        minX,
                        maxX,
                        notchHorizontalOffset,
                        out notchInfluence
                    );
                }

                if (notchCount == 0 && ENABLE_TOP_VIEW_NOTCH_DIM && frontNotchInfluence.Top)
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
                        notchHorizontalOffset,
                        bottomNotchHorizontalOffset,
                        (leftChamferTierReserved ? 1 : 0) + leftHoleTierCount + 1,
                        (rightChamferTierReserved ? 1 : 0) + rightHoleTierCount + 1,
                        out notchInfluence,
                        out leftNotchTierCount,
                        out rightNotchTierCount
                    );
                }

                if (notchCount > 0)
                {
                    MergeInfluence(ref chamferInfluence, notchInfluence);
                    count += notchCount;

                    if (notchInfluence.Top)
                        topHorizontalTier++;
                }

                // TOP VIEW: Left/Right là hai hệ tầng độc lập.
                // Mỗi chain DIM dọc tạo thành công chỉ tăng đúng một tầng ở phía nó được đặt.
                int leftVerticalTier =
                    (leftChamferTierReserved ? 1 : 0) +
                    leftHoleTierCount +
                    leftNotchTierCount +
                    1;
                int bottomHorizontalMaxTier = bottomReservedTier + bottomHoleTierCount;
                if (notchInfluence.Bottom ||
                    independentSectionFaceBottomNotchCreated)
                    bottomHorizontalMaxTier = Math.Max(bottomHorizontalMaxTier, bottomHorizontalNotchTier);
                LastTopBottomDimTier = Math.Max(1, bottomHorizontalMaxTier);

                double leftTotalVerticalOffset =
                    GetSteelDimOffsetByTier(leftVerticalTier);

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
                    leftTotalVerticalOffset
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

        private static int CreateDimsForBottomView(
            Model model,
            ModelPart part,
            View view,
            ChamferInfluence frontNotchInfluence,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();
            int count = 0;
            LastBottomTopDimTier = 1;

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

                bool chamferDimCreated = false;
                ChamferInfluence chamferInfluence = new ChamferInfluence();
                ChamferEdgeAnchors edgeAnchors = BuildChamferEdgeAnchors(topPolygon, minX, maxX, minY, maxY);

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

                // BOTTOM VIEW - CHAMFER TIER RULE:
                // Chamfer dùng tầng 0 riêng.
                // Nếu phía nào có chamfer/notch thì phía đó giữ tầng 1,
                // các DIM lỗ/tổng cùng phía bắt đầu từ tầng 2.
                bool topChamferTierReserved = false;
                bool bottomChamferTierReserved = false;
                bool leftChamferTierReserved = false;
                bool rightChamferTierReserved = false;

                ChamferInfluence notchInfluence = new ChamferInfluence();
                int notchCount = 0;
                TopBottomFrontNotchChain bottomFrontNotchChain = null;
                if (frontNotchInfluence.Bottom)
                {
                    TryDetectTopBottomFrontNotchChain(
                        part,
                        solid,
                        topPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        flangeThickness,
                        false,
                        out bottomFrontNotchChain
                    );

                }

                if (bottomFrontNotchChain != null)
                {
                    ApplyTopBottomFrontNotchTotalAnchors(ref edgeAnchors, bottomFrontNotchChain, topPolygon, minX, maxX, minY, maxY);
                    ApplyTopBottomFrontNotchHoleAnchors(ref edgeAnchors, bottomFrontNotchChain, minY, maxY);
                }

                DimOffsetAnchor4 offsetAnchors =
                    BuildDimOffsetAnchor4(edgeAnchors);

                List<Point> independentSectionFacePolygon =
                    new List<Point>();

                if (bottomFrontNotchChain == null &&
                    !frontNotchInfluence.Bottom &&
                    ViewTypeMatchesForH(view, "SectionView", "Section"))
                {
                    independentSectionFacePolygon =
                        GetIndependentSectionFacePolygon(
                            view,
                            solid,
                            solidMin,
                            solidMax);
                }

                // Sau khi đã gộp chamfer + notch, mới xác định phía nào bị chiếm tầng 1.
                // Chỉ phía bị ảnh hưởng mới nhảy tầng, các phía khác giữ tầng như cũ.
                topChamferTierReserved = chamferInfluence.Top;
                bottomChamferTierReserved = chamferInfluence.Bottom;
                leftChamferTierReserved = chamferInfluence.Left;
                rightChamferTierReserved = chamferInfluence.Right;

                List<Point> topFlangeHoles =
                    GetVisibleBottomFlangeBoltCentersFromView(
                        model,
                        view,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );

                bool holeDimCreated = false;
                int topHoleTierCount = 0;
                int bottomHoleTierCount = 0;
                int leftHoleTierCount = 0;
                int rightHoleTierCount = 0;

                if (topFlangeHoles.Count > 0)
                {
                    int holeCount = 0;

                    holeCount += CreateTopViewHoleDimsByDiameter(
                        handler,
                        view,
                        topFlangeHoles,
                        topPolygon,
                        topHoleVerticalPolygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        holeVerticalMinY,
                        holeVerticalMaxY,
                        edgeAnchors,
                        offsetAnchors,
                        beamLength,
                        topChamferTierReserved ? 1 : 0,
                        bottomChamferTierReserved ? 1 : 0,
                        leftChamferTierReserved ? 1 : 0,
                        rightChamferTierReserved ? 1 : 0,
                        out topHoleTierCount,
                        out bottomHoleTierCount,
                        out leftHoleTierCount,
                        out rightHoleTierCount
                    );

                    holeDimCreated = holeCount > 0;
                    count += holeCount;
                }

                // BOTTOM VIEW - QUY TẮC TẦNG DIM LỖ:
                // Mỗi nhóm phi/cụm dim đại diện chiếm đúng 1 tầng.
                // Ví dụ: lỗ đơn đối xứng ưu tiên tầng 1, cụm khác theo thứ tự hình học...
                // DIM tổng luôn nằm ở tầng cuối cùng sau toàn bộ nhóm lỗ.
                int topReservedTier = topChamferTierReserved ? 1 : 0;
                int topHorizontalTier = topReservedTier + (holeDimCreated ? (topHoleTierCount + 1) : 1);

                // BOTTOM VIEW: Left/Right là hai hệ tầng độc lập.
                int independentLeftTierCount = 0;
                int independentRightTierCount = 0;
                int bottomReservedTier = bottomChamferTierReserved ? 1 : 0;
                int bottomHorizontalNotchTier =
                    bottomReservedTier + bottomHoleTierCount + 1;

                if (independentSectionFacePolygon.Count >= 4)
                {
                    double faceMinX;
                    double faceMaxX;
                    double faceMinY;
                    double faceMaxY;
                    GetMinMax(
                        independentSectionFacePolygon,
                        out faceMinX,
                        out faceMaxX,
                        out faceMinY,
                        out faceMaxY);

                    ChamferInfluence sectionFaceNotchInfluence;
                    int sectionFaceNotchCount =
                        CreateIndependentSectionFaceNotchDims(
                            handler,
                            view,
                            independentSectionFacePolygon,
                            offsetAnchors,
                            faceMinX,
                            faceMaxX,
                            faceMinY,
                            faceMaxY,
                            GetSteelDimOffsetByTier(topHorizontalTier),
                            GetSteelDimOffsetByTier(bottomHorizontalNotchTier),
                            (leftChamferTierReserved ? 1 : 0) + leftHoleTierCount + 1,
                            (rightChamferTierReserved ? 1 : 0) + rightHoleTierCount + 1,
                            out sectionFaceNotchInfluence,
                            out independentLeftTierCount,
                            out independentRightTierCount);

                    if (sectionFaceNotchCount > 0)
                    {
                        MergeInfluence(
                            ref chamferInfluence,
                            sectionFaceNotchInfluence);
                        count += sectionFaceNotchCount;

                        if (sectionFaceNotchInfluence.Top)
                            topHorizontalTier++;
                    }
                }

                int leftVerticalTier =
                    (leftChamferTierReserved ? 1 : 0) +
                    leftHoleTierCount +
                    independentLeftTierCount +
                    1;
                if (bottomFrontNotchChain != null)
                {
                    notchCount = CreateTopBottomFrontNotchChainDims(
                        handler,
                        view,
                        bottomFrontNotchChain,
                        topPolygon,
                        offsetAnchors,
                        minX,
                        maxX,
                        GetSteelDimOffsetByTier(topHorizontalTier),
                        out notchInfluence
                    );

                    if (notchCount > 0)
                    {
                        MergeInfluence(ref chamferInfluence, notchInfluence);
                        count += notchCount;
                        topHorizontalTier++;
                    }
                }

                LastBottomTopDimTier = Math.Max(1, topHorizontalTier);

                double leftTotalVerticalOffset =
                    GetSteelDimOffsetByTier(leftVerticalTier);

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
                    leftTotalVerticalOffset
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

        private static bool CreateEdgeAnchoredNotchDimBySize(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double tierOffset,
            DimOffsetAnchor4 anchors,
            double measuredSize,
            string attributeName = null)
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
                measuredSize,
                attributeName);
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

        private static List<Point> GetIndependentSectionFacePolygon(
            View view,
            Solid solid,
            Point solidMin,
            Point solidMax)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (view == null ||
                    solid == null ||
                    solidMin == null ||
                    solidMax == null ||
                    view.RestrictionBox == null ||
                    view.RestrictionBox.MinPoint == null ||
                    view.RestrictionBox.MaxPoint == null)
                    return result;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double restrictionMinZ = view.RestrictionBox.MinPoint.Z;
                double restrictionMaxZ = view.RestrictionBox.MaxPoint.Z;
                bool minBoundaryMatches =
                    Math.Abs(restrictionMinZ - solidMin.Z) <= edgeTol;
                bool maxBoundaryMatches =
                    Math.Abs(restrictionMaxZ - solidMax.Z) <= edgeTol;

                if (minBoundaryMatches == maxBoundaryMatches)
                    return result;

                const double insideOffset = 0.5;
                double cutZ = minBoundaryMatches
                    ? solidMin.Z + insideOffset
                    : solidMax.Z - insideOffset;

                if (cutZ <= solidMin.Z ||
                    cutZ >= solidMax.Z ||
                    cutZ < restrictionMinZ - TOL ||
                    cutZ > restrictionMaxZ + TOL)
                    return result;

                Point p1 = new Point(
                    solidMin.X - 1000.0,
                    solidMin.Y - 1000.0,
                    cutZ);
                Point p2 = new Point(
                    solidMax.X + 1000.0,
                    solidMin.Y - 1000.0,
                    cutZ);
                Point p3 = new Point(
                    solidMin.X - 1000.0,
                    solidMax.Y + 1000.0,
                    cutZ);

                List<Point> polygon =
                    GetLargestIntersectionPolygon(
                        solid.IntersectAllFaces(p1, p2, p3));

                if (polygon == null || polygon.Count < 4)
                    return result;

                double minX;
                double maxX;
                double minY;
                double maxY;
                GetMinMax(
                    polygon,
                    out minX,
                    out maxX,
                    out minY,
                    out maxY);

                if (Math.Abs(maxX - minX) < 100.0 ||
                    Math.Abs(maxY - minY) < 20.0)
                    return result;

                return polygon;
            }
            catch
            {
                return result;
            }
        }

        private static int CreateIndependentSectionFaceNotchDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> polygon,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double topHorizontalTierOffset,
            double bottomHorizontalTierOffset,
            int leftVerticalStartTier,
            int rightVerticalStartTier,
            out ChamferInfluence influence,
            out int leftVerticalTierCount,
            out int rightVerticalTierCount)
        {
            influence = new ChamferInfluence();
            leftVerticalTierCount = 0;
            rightVerticalTierCount = 0;
            int count = 0;

            try
            {
                if (handler == null ||
                    view == null ||
                    polygon == null ||
                    polygon.Count < 4)
                    return count;

                for (int sideIndex = 0; sideIndex < 4; sideIndex++)
                {
                    bool leftSide = sideIndex == 0 || sideIndex == 2;
                    bool topSide = sideIndex == 0 || sideIndex == 1;
                    Point outer;
                    Point inner;
                    bool hasRadiusEvidence;

                    if (!TryFindIndependentSectionFaceNotch(
                        polygon,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        leftSide,
                        topSide,
                        out outer,
                        out inner,
                        out hasRadiusEvidence))
                        continue;

                    double width = leftSide
                        ? Math.Abs(inner.X - minX)
                        : Math.Abs(maxX - inner.X);
                    double depth = topSide
                        ? Math.Abs(maxY - outer.Y)
                        : Math.Abs(outer.Y - minY);
                    bool cornerCreated = false;

                    double verticalTierOffset = GetSteelDimOffsetByTier(
                        leftSide
                            ? leftVerticalStartTier + leftVerticalTierCount
                            : rightVerticalStartTier + rightVerticalTierCount);
                    double realVerticalOffset =
                        ResolveDimDistanceByAnchor4(
                            outer,
                            inner,
                            leftSide
                                ? new Vector(-1, 0, 0)
                                : new Vector(1, 0, 0),
                            offsetAnchors,
                            verticalTierOffset);

                    if (CreateNotchDimBySize(
                        handler,
                        view,
                        Clone2D(outer),
                        Clone2D(inner),
                        leftSide
                            ? new Vector(-1, 0, 0)
                            : new Vector(1, 0, 0),
                        realVerticalOffset,
                        depth))
                    {
                        count++;
                        cornerCreated = true;

                        if (leftSide)
                            leftVerticalTierCount++;
                        else
                            rightVerticalTierCount++;
                    }

                    double horizontalTierOffset = topSide
                        ? topHorizontalTierOffset
                        : bottomHorizontalTierOffset;
                    double realHorizontalOffset =
                        ResolveDimDistanceByAnchor4(
                            outer,
                            inner,
                            topSide
                                ? new Vector(0, 1, 0)
                                : new Vector(0, -1, 0),
                            offsetAnchors,
                            horizontalTierOffset);

                    if (CreateNotchDimBySize(
                        handler,
                        view,
                        Clone2D(outer),
                        Clone2D(inner),
                        topSide
                            ? new Vector(0, 1, 0)
                            : new Vector(0, -1, 0),
                        realHorizontalOffset,
                        width,
                        "GEO_\u5207\u308A\u6B20\u304D"))
                    {
                        count++;
                        cornerCreated = true;
                    }

                    if (hasRadiusEvidence &&
                        CreateNotchRadiusDimByOuterInnerClean(
                            view,
                            polygon,
                            outer,
                            inner,
                            leftSide,
                            topSide))
                    {
                        count++;
                        cornerCreated = true;
                    }

                    if (!cornerCreated)
                        continue;

                    if (leftSide)
                        influence.Left = true;
                    else
                        influence.Right = true;

                    if (topSide)
                        influence.Top = true;
                    else
                        influence.Bottom = true;

                    influence.Any = true;
                }
            }
            catch
            {
            }

            return count;
        }

        private static bool TryFindIndependentSectionFaceNotch(
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool leftSide,
            bool topSide,
            out Point outer,
            out Point inner,
            out bool hasRadiusEvidence)
        {
            outer = null;
            inner = null;
            hasRadiusEvidence = false;

            try
            {
                if (polygon == null || polygon.Count < 4)
                    return false;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double minSize =
                    Math.Max(NOTCH_MIN_SIZE, NOTCH_MIN_DIM_TO_CREATE);
                double maxSize = NOTCH_MAX_SIZE;

                foreach (Point p in polygon)
                {
                    if (p == null)
                        continue;

                    bool onSideEdge = leftSide
                        ? Math.Abs(p.X - minX) <= edgeTol
                        : Math.Abs(p.X - maxX) <= edgeTol;
                    bool inVerticalCornerBand = topSide
                        ? p.Y < maxY - edgeTol &&
                          p.Y >= maxY - maxSize
                        : p.Y > minY + edgeTol &&
                          p.Y <= minY + maxSize;

                    if (onSideEdge && inVerticalCornerBand)
                    {
                        if (outer == null ||
                            (topSide && p.Y > outer.Y) ||
                            (!topSide && p.Y < outer.Y))
                            outer = Clone2D(p);
                    }

                    bool onHorizontalEdge = topSide
                        ? Math.Abs(p.Y - maxY) <= edgeTol
                        : Math.Abs(p.Y - minY) <= edgeTol;
                    bool inHorizontalCornerBand = leftSide
                        ? p.X > minX + edgeTol &&
                          p.X <= minX + maxSize
                        : p.X < maxX - edgeTol &&
                          p.X >= maxX - maxSize;

                    if (onHorizontalEdge && inHorizontalCornerBand)
                    {
                        if (inner == null ||
                            (leftSide && p.X > inner.X) ||
                            (!leftSide && p.X < inner.X))
                            inner = Clone2D(p);
                    }
                }

                if (outer == null || inner == null)
                    return false;

                double width = leftSide
                    ? Math.Abs(inner.X - minX)
                    : Math.Abs(maxX - inner.X);
                double depth = topSide
                    ? Math.Abs(maxY - outer.Y)
                    : Math.Abs(outer.Y - minY);
                double faceWidth = Math.Abs(maxX - minX);
                double faceHeight = Math.Abs(maxY - minY);

                if (width < minSize ||
                    depth < minSize ||
                    width > maxSize ||
                    depth > maxSize ||
                    width >= faceWidth - edgeTol ||
                    depth >= faceHeight - edgeTol)
                    return false;

                if (!HasIndependentSectionFaceNotchBoundary(
                    polygon,
                    outer,
                    inner,
                    edgeTol,
                    minSize,
                    out hasRadiusEvidence))
                    return false;

                return true;
            }
            catch
            {
                outer = null;
                inner = null;
                hasRadiusEvidence = false;
                return false;
            }
        }

        private static bool HasIndependentSectionFaceNotchBoundary(
            List<Point> polygon,
            Point outer,
            Point inner,
            double edgeTol,
            double minSize,
            out bool hasRadiusEvidence)
        {
            hasRadiusEvidence = false;

            try
            {
                List<Point> path =
                    GetIndependentSectionFaceCornerPath(
                        polygon,
                        outer,
                        inner,
                        edgeTol);

                if (path.Count < 3)
                    return false;

                double tangentTol = Math.Min(0.5, edgeTol);
                int horizontalTangentIndex = 0;
                while (horizontalTangentIndex + 1 < path.Count &&
                       Math.Abs(
                           path[horizontalTangentIndex + 1].Y -
                           outer.Y) <= tangentTol)
                {
                    horizontalTangentIndex++;
                }

                int verticalTangentIndex = path.Count - 1;
                while (verticalTangentIndex - 1 >= 0 &&
                       Math.Abs(
                           path[verticalTangentIndex - 1].X -
                           inner.X) <= tangentTol)
                {
                    verticalTangentIndex--;
                }

                if (horizontalTangentIndex < 1 ||
                    verticalTangentIndex > path.Count - 2 ||
                    horizontalTangentIndex > verticalTangentIndex)
                    return false;

                Point horizontalTangent =
                    path[horizontalTangentIndex];
                Point verticalTangent =
                    path[verticalTangentIndex];

                if (Math.Abs(horizontalTangent.X - outer.X) < minSize ||
                    Math.Abs(verticalTangent.Y - inner.Y) < minSize)
                    return false;

                if (horizontalTangentIndex == verticalTangentIndex)
                {
                    hasRadiusEvidence = false;
                    return true;
                }

                double radiusX =
                    Math.Abs(inner.X - horizontalTangent.X);
                double radiusY =
                    Math.Abs(verticalTangent.Y - outer.Y);
                double radius = (radiusX + radiusY) * 0.5;
                double radiusMatchTol =
                    Math.Max(0.75, radius * 0.10);

                if (radius <= edgeTol ||
                    Math.Abs(radiusX - radiusY) > radiusMatchTol)
                    return false;

                Point center = new Point(
                    horizontalTangent.X,
                    verticalTangent.Y,
                    0.0);
                int arcInteriorPointCount = 0;
                double circleTol =
                    Math.Max(0.75, radius * 0.08);

                for (int i = horizontalTangentIndex;
                     i <= verticalTangentIndex;
                     i++)
                {
                    Point p = path[i];
                    if (p == null)
                        return false;

                    double dx = p.X - center.X;
                    double dy = p.Y - center.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (Math.Abs(distance - radius) > circleTol)
                        return false;

                    if (i > horizontalTangentIndex &&
                        i < verticalTangentIndex &&
                        Math.Abs(p.Y - outer.Y) > edgeTol &&
                        Math.Abs(p.X - inner.X) > edgeTol)
                    {
                        arcInteriorPointCount++;
                    }
                }

                if (arcInteriorPointCount < 1)
                    return false;

                hasRadiusEvidence = true;
                return true;
            }
            catch
            {
                hasRadiusEvidence = false;
                return false;
            }
        }

        private static List<Point> GetIndependentSectionFaceCornerPath(
            List<Point> polygon,
            Point outer,
            Point inner,
            double edgeTol)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (polygon == null ||
                    polygon.Count < 3 ||
                    outer == null ||
                    inner == null)
                    return result;

                int outerIndex =
                    FindIndependentSectionFacePointIndex(
                        polygon,
                        outer,
                        edgeTol);
                int innerIndex =
                    FindIndependentSectionFacePointIndex(
                        polygon,
                        inner,
                        edgeTol);

                if (outerIndex < 0 ||
                    innerIndex < 0 ||
                    outerIndex == innerIndex)
                    return result;

                List<Point> forward =
                    BuildIndependentSectionFacePath(
                        polygon,
                        outerIndex,
                        innerIndex,
                        1);
                List<Point> backward =
                    BuildIndependentSectionFacePath(
                        polygon,
                        outerIndex,
                        innerIndex,
                        -1);

                bool forwardValid =
                    IsIndependentSectionFaceCornerPath(
                        forward,
                        outer,
                        inner,
                        edgeTol);
                bool backwardValid =
                    IsIndependentSectionFaceCornerPath(
                        backward,
                        outer,
                        inner,
                        edgeTol);

                if (!forwardValid && !backwardValid)
                    return result;

                if (forwardValid && !backwardValid)
                    return forward;

                if (backwardValid && !forwardValid)
                    return backward;

                return GetIndependentSectionFacePathLength(forward) <=
                       GetIndependentSectionFacePathLength(backward)
                    ? forward
                    : backward;
            }
            catch
            {
                return result;
            }
        }

        private static int FindIndependentSectionFacePointIndex(
            List<Point> polygon,
            Point target,
            double tolerance)
        {
            int bestIndex = -1;
            double bestDistance = double.MaxValue;

            if (polygon == null || target == null)
                return bestIndex;

            for (int i = 0; i < polygon.Count; i++)
            {
                Point p = polygon[i];
                if (p == null)
                    continue;

                double dx = p.X - target.X;
                double dy = p.Y - target.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance <= tolerance &&
                    distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<Point> BuildIndependentSectionFacePath(
            List<Point> polygon,
            int startIndex,
            int endIndex,
            int step)
        {
            List<Point> result = new List<Point>();

            if (polygon == null ||
                polygon.Count == 0 ||
                startIndex < 0 ||
                endIndex < 0 ||
                (step != 1 && step != -1))
                return result;

            int index = startIndex;
            for (int guard = 0; guard <= polygon.Count; guard++)
            {
                Point p = polygon[index];
                if (p != null)
                    result.Add(Clone2D(p));

                if (index == endIndex)
                    break;

                index =
                    (index + step + polygon.Count) %
                    polygon.Count;
            }

            return result;
        }

        private static bool IsIndependentSectionFaceCornerPath(
            List<Point> path,
            Point outer,
            Point inner,
            double tolerance)
        {
            if (path == null ||
                path.Count < 3 ||
                outer == null ||
                inner == null)
                return false;

            double minX = Math.Min(outer.X, inner.X) - tolerance;
            double maxX = Math.Max(outer.X, inner.X) + tolerance;
            double minY = Math.Min(outer.Y, inner.Y) - tolerance;
            double maxY = Math.Max(outer.Y, inner.Y) + tolerance;

            foreach (Point p in path)
            {
                if (p == null ||
                    p.X < minX ||
                    p.X > maxX ||
                    p.Y < minY ||
                    p.Y > maxY)
                    return false;
            }

            return true;
        }

        private static double GetIndependentSectionFacePathLength(
            List<Point> path)
        {
            double length = 0.0;

            if (path == null)
                return length;

            for (int i = 1; i < path.Count; i++)
            {
                Point first = path[i - 1];
                Point second = path[i];
                if (first == null || second == null)
                    continue;

                double dx = second.X - first.X;
                double dy = second.Y - first.Y;
                length += Math.Sqrt(dx * dx + dy * dy);
            }

            return length;
        }

        private static int CreateTopBottomFrontNotchChainDims(
            StraightDimensionSetHandler handler,
            View view,
            TopBottomFrontNotchChain chain,
            List<Point> polygon,
            DimOffsetAnchor4 offsetAnchors,
            double minX,
            double maxX,
            double tierOffset,
            out ChamferInfluence influence)
        {
            influence = new ChamferInfluence();
            int count = 0;

            try
            {
                if (chain == null || (!chain.HasLeft && !chain.HasRight))
                    return count;

                PointList pts = new PointList();
                Point firstDimPoint = null;

                if (chain.HasLeft && chain.HasRight)
                {
                    firstDimPoint = Clone2D(chain.LeftOuter);
                    pts.Add(firstDimPoint);
                    pts.Add(Clone2D(chain.LeftInner));
                    pts.Add(Clone2D(chain.RightInner));
                    pts.Add(Clone2D(chain.RightOuter));
                }
                else if (chain.HasLeft)
                {
                    double rightOuterY = chain.LeftInner != null ? chain.LeftInner.Y : chain.LeftOuter.Y;
                    Point rightOuter = FindEdgePointNearestY(polygon, rightOuterY, maxX, true, Math.Max(2.0, TOL + 1.0));
                    if (rightOuter == null)
                        rightOuter = new Point(maxX, rightOuterY, 0);

                    firstDimPoint = Clone2D(chain.LeftOuter);
                    pts.Add(firstDimPoint);
                    pts.Add(Clone2D(chain.LeftInner));
                    pts.Add(Clone2D(rightOuter));
                }
                else if (chain.HasRight)
                {
                    double leftOuterY = chain.RightInner != null ? chain.RightInner.Y : chain.RightOuter.Y;
                    Point leftOuter = FindEdgePointNearestY(polygon, leftOuterY, minX, true, Math.Max(2.0, TOL + 1.0));
                    if (leftOuter == null)
                        leftOuter = new Point(minX, leftOuterY, 0);

                    firstDimPoint = Clone2D(leftOuter);
                    pts.Add(firstDimPoint);
                    pts.Add(Clone2D(chain.RightInner));
                    pts.Add(Clone2D(chain.RightOuter));
                }

                if (pts.Count < 3)
                    return count;

                double realUpperOffset = ResolveDimDistanceByAnchor4(
                    pts,
                    new Vector(0, 1, 0),
                    offsetAnchors,
                    tierOffset
                );

                if (handler.CreateDimensionSet(view, pts, new Vector(0, 1, 0), realUpperOffset) != null)
                {
                    count++;
                    influence.Top = true;
                    influence.Any = true;
                }
            }
            catch
            {
            }

            return count;
        }

        private static bool TryDetectTopBottomFrontNotchChain(
            ModelPart part,
            Solid solid,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double flangeThickness,
            bool detectTopFrontNotch,
            out TopBottomFrontNotchChain chain)
        {
            chain = null;

            try
            {
                List<Point> pts = new List<Point>();

                if (polygon != null)
                {
                    foreach (Point p in polygon)
                    {
                        if (p != null)
                            AddUniquePoint(pts, Clone2D(p), 0.5);
                    }
                }

                List<Point> solidPts = GetProjectedSolidPointsForFrontNotchDims(solid);
                if (solidPts != null)
                {
                    foreach (Point p in solidPts)
                    {
                        if (p != null)
                            AddUniquePoint(pts, Clone2D(p), 0.5);
                    }
                }

                if (pts.Count < 4)
                    return false;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double maxSize = NOTCH_MAX_SIZE;
                double minSize = Math.Max(NOTCH_MIN_SIZE, NOTCH_MIN_DIM_TO_CREATE);

                Point leftOuter = null;
                Point leftInner = null;
                Point rightOuter = null;
                Point rightInner = null;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.X - minX) <= edgeTol &&
                        p.Y < maxY - edgeTol &&
                        p.Y > minY + edgeTol)
                    {
                        if (leftOuter == null || p.Y > leftOuter.Y)
                            leftOuter = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X > minX + edgeTol &&
                        p.X <= minX + maxSize)
                    {
                        if (leftInner == null || p.X > leftInner.X)
                            leftInner = Clone2D(p);
                    }

                    if (Math.Abs(p.X - maxX) <= edgeTol &&
                        p.Y < maxY - edgeTol &&
                        p.Y > minY + edgeTol)
                    {
                        if (rightOuter == null || p.Y > rightOuter.Y)
                            rightOuter = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X < maxX - edgeTol &&
                        p.X >= maxX - maxSize)
                    {
                        if (rightInner == null || p.X < rightInner.X)
                            rightInner = Clone2D(p);
                    }
                }

                bool hasLeft = IsValidTopBottomFrontNotchSide(leftOuter, leftInner, true, minX, maxX, maxY, minSize, maxSize);
                bool hasRight = IsValidTopBottomFrontNotchSide(rightOuter, rightInner, false, minX, maxX, maxY, minSize, maxSize);

                hasLeft = hasLeft && IsPointOnRequestedHShapeFace(part, leftInner, flangeThickness, detectTopFrontNotch);
                hasRight = hasRight && IsPointOnRequestedHShapeFace(part, rightInner, flangeThickness, detectTopFrontNotch);

                if (!hasLeft && !hasRight)
                    return false;

                chain = new TopBottomFrontNotchChain();
                chain.HasLeft = hasLeft;
                chain.HasRight = hasRight;
                chain.LeftOuter = leftOuter;
                chain.LeftInner = leftInner;
                chain.RightOuter = rightOuter;
                chain.RightInner = rightInner;

                return true;
            }
            catch
            {
                chain = null;
                return false;
            }
        }

        private static bool IsValidTopBottomFrontNotchSide(
            Point outer,
            Point inner,
            bool isLeft,
            double minX,
            double maxX,
            double maxY,
            double minSize,
            double maxSize)
        {
            if (outer == null || inner == null)
                return false;

            double width = isLeft ? Math.Abs(inner.X - minX) : Math.Abs(maxX - inner.X);
            double depth = Math.Abs(maxY - outer.Y);

            return width >= minSize &&
                   depth >= minSize &&
                   width <= maxSize &&
                   depth <= maxSize;
        }

        private static bool IsPointOnRequestedHShapeFace(
            ModelPart part,
            Point point,
            double flangeThickness,
            bool wantTopFace)
        {
            try
            {
                if (part == null || point == null)
                    return false;

                double minHeight;
                double maxHeight;
                if (!TryGetHShapePartLocalYRange(part, out minHeight, out maxHeight))
                    return false;

                double height = Math.Abs(maxHeight - minHeight);
                if (height <= 1.0)
                    return false;

                if (flangeThickness <= 0.0)
                    flangeThickness = 20.0;

                double faceBand = Math.Max(height * 0.20, flangeThickness + TOP_FLANGE_DEPTH_TOL);
                double overlapTol = Math.Max(height * 0.04, 5.0);

                double topMin = maxHeight - faceBand - overlapTol;
                double bottomMax = minHeight + faceBand + overlapTol;

                double lx;
                double ly;
                double lz;
                if (!TryGetHShapePartLocalPoint(part, point, out lx, out ly, out lz))
                    return false;

                return wantTopFace
                    ? ly >= topMin
                    : ly <= bottomMax;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyTopBottomFrontNotchTotalAnchors(
            ref ChamferEdgeAnchors edgeAnchors,
            TopBottomFrontNotchChain chain,
            List<Point> polygon,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            try
            {
                if (chain == null || (!chain.HasLeft && !chain.HasRight))
                    return;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                Point leftOuter = null;
                Point rightOuter = null;
                Point inner = null;

                if (chain.HasLeft)
                {
                    leftOuter = Clone2D(chain.LeftOuter);
                    inner = Clone2D(chain.LeftInner);
                }

                if (chain.HasRight)
                {
                    rightOuter = Clone2D(chain.RightOuter);
                    if (inner == null)
                        inner = Clone2D(chain.RightInner);
                }

                if (leftOuter == null && rightOuter != null)
                {
                    double leftOuterY = inner != null ? inner.Y : rightOuter.Y;
                    leftOuter = FindEdgePointNearestY(polygon, leftOuterY, minX, true, edgeTol);
                    if (leftOuter == null)
                        leftOuter = new Point(minX, leftOuterY, 0);
                }

                if (rightOuter == null && leftOuter != null)
                {
                    double rightOuterY = inner != null ? inner.Y : leftOuter.Y;
                    rightOuter = FindEdgePointNearestY(polygon, rightOuterY, maxX, true, edgeTol);
                    if (rightOuter == null)
                        rightOuter = new Point(maxX, rightOuterY, 0);
                }

                if (leftOuter == null || rightOuter == null || inner == null)
                    return;

                double verticalAnchorX = inner.X;
                if (chain.HasRight && !chain.HasLeft)
                    verticalAnchorX = leftOuter.X;

                edgeAnchors.LeftMost = Clone2D(leftOuter);
                edgeAnchors.RightMost = Clone2D(rightOuter);
                edgeAnchors.BottomMost = new Point(verticalAnchorX, minY, 0);
                edgeAnchors.TopMost = new Point(verticalAnchorX, maxY, 0);
            }
            catch
            {
            }
        }

        private static void ApplyTopBottomFrontNotchHoleAnchors(
            ref ChamferEdgeAnchors edgeAnchors,
            TopBottomFrontNotchChain chain,
            double minY,
            double maxY)
        {
            try
            {
                if (chain == null)
                    return;

                if (chain.HasLeft && chain.LeftInner != null)
                {
                    double leftRealEdgeX = chain.LeftInner.X;
                    edgeAnchors.TopLeft = new Point(leftRealEdgeX, maxY, 0);
                    edgeAnchors.BottomLeft = new Point(leftRealEdgeX, minY, 0);
                    edgeAnchors.HasLeftNotchHoleAnchor = true;
                }

                if (chain.HasRight && chain.RightInner != null)
                {
                    double rightRealEdgeX = chain.RightInner.X;
                    edgeAnchors.TopRight = new Point(rightRealEdgeX, maxY, 0);
                    edgeAnchors.BottomRight = new Point(rightRealEdgeX, minY, 0);
                    edgeAnchors.HasRightNotchHoleAnchor = true;
                }
            }
            catch
            {
            }
        }

        private static bool CreateNotchDimBySize(
            StraightDimensionSetHandler handler,
            View view,
            Point p1,
            Point p2,
            Vector direction,
            double distance,
            double measuredSize,
            string attributeName = null)
        {
            // Fillet/bo góc thường sinh ra các cạnh rất nhỏ như 7.1, 5.8...
            // Chỉ bỏ các DIM quá nhỏ, giữ nguyên thuật toán nhận rãnh V3.
            if (measuredSize < NOTCH_MIN_DIM_TO_CREATE)
                return false;

            return CreateDim(handler, view, p1, p2, direction, distance, attributeName);
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
            double topHorizontalTierOffset,
            double bottomHorizontalTierOffset,
            int leftVerticalStartTier,
            int rightVerticalStartTier,
            out ChamferInfluence influence,
            out int leftVerticalTierCount,
            out int rightVerticalTierCount)
        {
            // RÃNH / NOTCH - V3
            // Không dựa vào bounding box ảo và không cần điểm rãnh trùng mép dầm nguyên vẹn.
            // Thuật toán:
            // 1. Lấy các điểm polygon thật nằm lõm vào gần từng mép ngoài.
            // 2. Nếu có ít nhất 2 điểm lõm tạo thành bề rộng + chiều sâu hợp lý => xem là rãnh.
            // 3. DIM ngang + dọc rãnh đặt tầng theo neo A/B/C/D của DIM tổng.
            // 4. Chỉ trả influence cạnh bị rãnh để dim tổng liên quan tự đẩy tầng.
            influence = new ChamferInfluence();
            leftVerticalTierCount = 0;
            rightVerticalTierCount = 0;
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

                            double verticalTierOffset = GetSteelDimOffsetByTier(
                                useRightSideForDepth
                                    ? rightVerticalStartTier + rightVerticalTierCount
                                    : leftVerticalStartTier + leftVerticalTierCount);
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                verticalTierOffset,
                                offsetAnchors,
                                depth))
                            {
                                count++;
                                if (useRightSideForDepth)
                                    rightVerticalTierCount++;
                                else
                                    leftVerticalTierCount++;
                            }

                            // DIM ngang bề rộng rãnh.
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, -1, 0),
                                bottomHorizontalTierOffset,
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

                            double verticalTierOffset = GetSteelDimOffsetByTier(
                                useRightSideForDepth
                                    ? rightVerticalStartTier + rightVerticalTierCount
                                    : leftVerticalStartTier + leftVerticalTierCount);
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useRightSideForDepth ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                                verticalTierOffset,
                                offsetAnchors,
                                depth))
                            {
                                count++;
                                if (useRightSideForDepth)
                                    rightVerticalTierCount++;
                                else
                                    leftVerticalTierCount++;
                            }

                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerLeft),
                                Clone2D(innerRight),
                                new Vector(0, 1, 0),
                                topHorizontalTierOffset,
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

                            bool horizontalDepthDimCreated = CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                useTopSideForDepth ? topHorizontalTierOffset : bottomHorizontalTierOffset,
                                offsetAnchors,
                                depth);
                            if (horizontalDepthDimCreated)
                                count++;

                            double leftTierOffset = GetSteelDimOffsetByTier(
                                leftVerticalStartTier + leftVerticalTierCount);
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(-1, 0, 0),
                                leftTierOffset,
                                offsetAnchors,
                                height))
                            {
                                count++;
                                leftVerticalTierCount++;
                            }

                            influence.Left = true;
                            if (horizontalDepthDimCreated)
                            {
                                if (useTopSideForDepth)
                                    influence.Top = true;
                                else
                                    influence.Bottom = true;
                            }
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

                            bool horizontalDepthDimCreated = CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(depthOuter),
                                Clone2D(depthInner),
                                useTopSideForDepth ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                                useTopSideForDepth ? topHorizontalTierOffset : bottomHorizontalTierOffset,
                                offsetAnchors,
                                depth);
                            if (horizontalDepthDimCreated)
                                count++;

                            double rightTierOffset = GetSteelDimOffsetByTier(
                                rightVerticalStartTier + rightVerticalTierCount);
                            if (CreateEdgeAnchoredNotchDimBySize(
                                handler,
                                view,
                                Clone2D(innerBottom),
                                Clone2D(innerTop),
                                new Vector(1, 0, 0),
                                rightTierOffset,
                                offsetAnchors,
                                height))
                            {
                                count++;
                                rightVerticalTierCount++;
                            }

                            influence.Right = true;
                            if (horizontalDepthDimCreated)
                            {
                                if (useTopSideForDepth)
                                    influence.Top = true;
                                else
                                    influence.Bottom = true;
                            }
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
            // PHU CLEAN FRONT NOTCH DEBUG - RULE CHẮC CHẮN ĐỂ PORT VÀO FILE CHÍNH
            // Mục tiêu: chỉ dim rãnh/notch ở 4 góc theo đúng chân DIM thật.
            // Không dùng lại logic cũ kiểu dựng điểm nhân tạo cùng X / cùng Y như:
            //      (220,75) -> (220,88)
            // vì đó là nguyên nhân bắt nhầm vào thành trong/fillet.
            //
            // Rule mới:
            // 1) Mỗi rãnh góc được nhận bằng 2 điểm thật trên polygon/điểm chiếu:
            //    - outer point: điểm thật nằm trên mép ngoài đứng/trái-phải của dầm.
            //    - inner point: điểm thật nằm trên mép ngoài ngang trên/dưới của dầm.
            // 2) Hai DIM của cùng 1 rãnh dùng CÙNG 1 cặp điểm chéo thật outer -> inner.
            //    Ví dụ dump mong muốn:
            //      TL: (0,75)    -> (220,100)
            //      TR: (6390,75) -> (6170,100)
            // 3) Chỉ tạo khi cả 2 điểm đều là điểm thật tìm được trong polygon/footPolygon.
            //    Không fallback bằng tọa độ giả minX/maxX/minY/maxY.
            // 4) Rãnh trái/phải/trên/dưới đều tạo bằng cùng một nguyên tắc đối xứng.
            influence = new ChamferInfluence();
            int count = 0;

            try
            {
                if (polygon == null || polygon.Count < 4)
                    return count;

                List<Point> pts =
                    (projectedFootPoints != null && projectedFootPoints.Count >= 2)
                    ? projectedFootPoints
                    : polygon;

                if (pts == null || pts.Count < 4)
                    return count;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double maxSize = NOTCH_MAX_SIZE;
                double minSize = Math.Max(NOTCH_MIN_SIZE, NOTCH_MIN_DIM_TO_CREATE);

                // Offset debug cố định theo dump mong muốn:
                // DIM ra trái/phải nằm khoảng 200; DIM ra trên/dưới nằm ngoài hơn một chút.
                // FRONT NOTCH TIER RULE: vertical = tier 0; horizontal = tier 1.
                double sideOffset = GetSteelDimOffsetByTier(0);
                double topBottomOffset = GetSteelDimOffsetByTier(1);

                Point outer;
                Point inner;
                double width;
                double depth;

                // ============================================================
                // TOP LEFT NOTCH
                // outer = điểm thật trên mép trái, gần mép trên nhưng không phải góc ảo.
                // inner = điểm thật trên mép trên, lõm vào từ mép trái.
                // ============================================================
                outer = null;
                inner = null;

                foreach (Point p in pts)
                {
                    if (p == null) continue;

                    if (Math.Abs(p.X - minX) <= edgeTol &&
                        p.Y < maxY - edgeTol &&
                        p.Y >= maxY - maxSize)
                    {
                        if (outer == null || p.Y > outer.Y)
                            outer = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X > minX + edgeTol &&
                        p.X <= minX + maxSize)
                    {
                        if (inner == null || p.X > inner.X)
                            inner = Clone2D(p);
                    }
                }

                if (outer != null && inner != null)
                {
                    width = Math.Abs(inner.X - minX);
                    depth = Math.Abs(maxY - outer.Y);

                    if (width >= minSize && depth >= minSize && width <= maxSize && depth <= maxSize)
                    {
                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(-1, 0, 0), sideOffset, offsetAnchors, Math.Max(width, depth)))
                            count++;

                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(0, 1, 0), topBottomOffset, offsetAnchors, Math.Max(width, depth), "GEO_\u5207\u308A\u6B20\u304D"))
                            count++;

                        if (CreateNotchRadiusDimByOuterInnerClean(view, pts, outer, inner, true, true))
                            count++;

                        influence.Left = true;
                        influence.Top = true;
                        influence.Any = true;
                    }
                }

                // ============================================================
                // TOP RIGHT NOTCH
                // outer = điểm thật trên mép phải, gần mép trên nhưng không phải góc ảo.
                // inner = điểm thật trên mép trên, lõm vào từ mép phải.
                // ============================================================
                outer = null;
                inner = null;

                foreach (Point p in pts)
                {
                    if (p == null) continue;

                    if (Math.Abs(p.X - maxX) <= edgeTol &&
                        p.Y < maxY - edgeTol &&
                        p.Y >= maxY - maxSize)
                    {
                        if (outer == null || p.Y > outer.Y)
                            outer = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - maxY) <= edgeTol &&
                        p.X < maxX - edgeTol &&
                        p.X >= maxX - maxSize)
                    {
                        if (inner == null || p.X < inner.X)
                            inner = Clone2D(p);
                    }
                }

                if (outer != null && inner != null)
                {
                    width = Math.Abs(maxX - inner.X);
                    depth = Math.Abs(maxY - outer.Y);

                    if (width >= minSize && depth >= minSize && width <= maxSize && depth <= maxSize)
                    {
                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(0, 1, 0), topBottomOffset, offsetAnchors, Math.Max(width, depth), "GEO_\u5207\u308A\u6B20\u304D"))
                            count++;

                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(1, 0, 0), sideOffset, offsetAnchors, Math.Max(width, depth)))
                            count++;

                        if (CreateNotchRadiusDimByOuterInnerClean(view, pts, outer, inner, false, true))
                            count++;

                        influence.Right = true;
                        influence.Top = true;
                        influence.Any = true;
                    }
                }

                // ============================================================
                // BOTTOM LEFT NOTCH
                // outer = điểm thật trên mép trái, gần mép dưới nhưng không phải góc ảo.
                // inner = điểm thật trên mép dưới, lõm vào từ mép trái.
                // ============================================================
                outer = null;
                inner = null;

                foreach (Point p in pts)
                {
                    if (p == null) continue;

                    if (Math.Abs(p.X - minX) <= edgeTol &&
                        p.Y > minY + edgeTol &&
                        p.Y <= minY + maxSize)
                    {
                        if (outer == null || p.Y < outer.Y)
                            outer = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - minY) <= edgeTol &&
                        p.X > minX + edgeTol &&
                        p.X <= minX + maxSize)
                    {
                        if (inner == null || p.X > inner.X)
                            inner = Clone2D(p);
                    }
                }

                if (outer != null && inner != null)
                {
                    width = Math.Abs(inner.X - minX);
                    depth = Math.Abs(outer.Y - minY);

                    if (width >= minSize && depth >= minSize && width <= maxSize && depth <= maxSize)
                    {
                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(-1, 0, 0), sideOffset, offsetAnchors, Math.Max(width, depth)))
                            count++;

                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(0, -1, 0), topBottomOffset, offsetAnchors, Math.Max(width, depth), "GEO_\u5207\u308A\u6B20\u304D"))
                            count++;

                        if (CreateNotchRadiusDimByOuterInnerClean(view, pts, outer, inner, true, false))
                            count++;

                        influence.Left = true;
                        influence.Bottom = true;
                        influence.Any = true;
                    }
                }

                // ============================================================
                // BOTTOM RIGHT NOTCH
                // outer = điểm thật trên mép phải, gần mép dưới nhưng không phải góc ảo.
                // inner = điểm thật trên mép dưới, lõm vào từ mép phải.
                // ============================================================
                outer = null;
                inner = null;

                foreach (Point p in pts)
                {
                    if (p == null) continue;

                    if (Math.Abs(p.X - maxX) <= edgeTol &&
                        p.Y > minY + edgeTol &&
                        p.Y <= minY + maxSize)
                    {
                        if (outer == null || p.Y < outer.Y)
                            outer = Clone2D(p);
                    }

                    if (Math.Abs(p.Y - minY) <= edgeTol &&
                        p.X < maxX - edgeTol &&
                        p.X >= maxX - maxSize)
                    {
                        if (inner == null || p.X < inner.X)
                            inner = Clone2D(p);
                    }
                }

                if (outer != null && inner != null)
                {
                    width = Math.Abs(maxX - inner.X);
                    depth = Math.Abs(outer.Y - minY);

                    if (width >= minSize && depth >= minSize && width <= maxSize && depth <= maxSize)
                    {
                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(1, 0, 0), sideOffset, offsetAnchors, Math.Max(width, depth)))
                            count++;

                        if (CreateEdgeAnchoredNotchDimBySize(handler, view, Clone2D(outer), Clone2D(inner), new Vector(0, -1, 0), topBottomOffset, offsetAnchors, Math.Max(width, depth), "GEO_\u5207\u308A\u6B20\u304D"))
                            count++;

                        if (CreateNotchRadiusDimByOuterInnerClean(view, pts, outer, inner, false, false))
                            count++;

                        influence.Right = true;
                        influence.Bottom = true;
                        influence.Any = true;
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static ChamferInfluence DetectFrontNotchInfluenceOnly(
            Model model,
            ModelPart part,
            View view)
        {
            ChamferInfluence influence = new ChamferInfluence();

            if (model == null || part == null || view == null)
                return influence;

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

                List<Point> frontPolygon = GetFrontWebFacePolygon(solid, min, max);
                if (frontPolygon.Count >= 2)
                    GetMinMax(frontPolygon, out minX, out maxX, out minY, out maxY);

                List<Point> frontProjectedSolidPoints = GetProjectedSolidPointsForFrontNotchDims(solid);
                List<Point> frontNotchProfile =
                    (frontProjectedSolidPoints != null && frontProjectedSolidPoints.Count >= 2)
                    ? frontProjectedSolidPoints
                    : frontPolygon;

                return DetectFrontAxisAlignedNotchInfluence(
                    frontNotchProfile,
                    frontProjectedSolidPoints,
                    minX,
                    maxX,
                    minY,
                    maxY
                );
            }
            catch
            {
                return influence;
            }
            finally
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
        }

        private static ChamferInfluence DetectFrontAxisAlignedNotchInfluence(
            List<Point> polygon,
            List<Point> projectedFootPoints,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            ChamferInfluence influence = new ChamferInfluence();

            try
            {
                List<Point> pts =
                    (projectedFootPoints != null && projectedFootPoints.Count >= 2)
                    ? projectedFootPoints
                    : polygon;

                if (pts == null || pts.Count < 4)
                    return influence;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double maxSize = NOTCH_MAX_SIZE;
                double minSize = Math.Max(NOTCH_MIN_SIZE, NOTCH_MIN_DIM_TO_CREATE);

                if (HasFrontCornerNotch(pts, minX, maxX, minY, maxY, edgeTol, minSize, maxSize, true, true))
                {
                    influence.Left = true;
                    influence.Top = true;
                    influence.Any = true;
                }

                if (HasFrontCornerNotch(pts, minX, maxX, minY, maxY, edgeTol, minSize, maxSize, false, true))
                {
                    influence.Right = true;
                    influence.Top = true;
                    influence.Any = true;
                }

                if (HasFrontCornerNotch(pts, minX, maxX, minY, maxY, edgeTol, minSize, maxSize, true, false))
                {
                    influence.Left = true;
                    influence.Bottom = true;
                    influence.Any = true;
                }

                if (HasFrontCornerNotch(pts, minX, maxX, minY, maxY, edgeTol, minSize, maxSize, false, false))
                {
                    influence.Right = true;
                    influence.Bottom = true;
                    influence.Any = true;
                }
            }
            catch
            {
            }

            return influence;
        }

        private static bool HasFrontCornerNotch(
            List<Point> pts,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double edgeTol,
            double minSize,
            double maxSize,
            bool leftSide,
            bool topSide)
        {
            Point outer = null;
            Point inner = null;

            foreach (Point p in pts)
            {
                if (p == null) continue;

                bool onSideEdge = leftSide
                    ? Math.Abs(p.X - minX) <= edgeTol
                    : Math.Abs(p.X - maxX) <= edgeTol;

                bool inVerticalCornerBand = topSide
                    ? p.Y < maxY - edgeTol && p.Y >= maxY - maxSize
                    : p.Y > minY + edgeTol && p.Y <= minY + maxSize;

                if (onSideEdge && inVerticalCornerBand)
                {
                    if (outer == null ||
                        (topSide && p.Y > outer.Y) ||
                        (!topSide && p.Y < outer.Y))
                        outer = Clone2D(p);
                }

                bool onHorizontalEdge = topSide
                    ? Math.Abs(p.Y - maxY) <= edgeTol
                    : Math.Abs(p.Y - minY) <= edgeTol;

                bool inHorizontalCornerBand = leftSide
                    ? p.X > minX + edgeTol && p.X <= minX + maxSize
                    : p.X < maxX - edgeTol && p.X >= maxX - maxSize;

                if (onHorizontalEdge && inHorizontalCornerBand)
                {
                    if (inner == null ||
                        (leftSide && p.X > inner.X) ||
                        (!leftSide && p.X < inner.X))
                        inner = Clone2D(p);
                }
            }

            if (outer == null || inner == null)
                return false;

            double width = leftSide ? Math.Abs(inner.X - minX) : Math.Abs(maxX - inner.X);
            double depth = topSide ? Math.Abs(maxY - outer.Y) : Math.Abs(outer.Y - minY);

            return width >= minSize &&
                   depth >= minSize &&
                   width <= maxSize &&
                   depth <= maxSize;
        }

        private static bool CreateNotchRadiusDimByOuterInnerClean(
            View view,
            List<Point> pts,
            Point outer,
            Point inner,
            bool isLeftSide,
            bool isTopSide)
        {
            if (!EnableNotchRadiusDimensionForCurrentDrawing)
                return false;

            // PHU RADIUS NOTCH CLEAN V2:
            // Sửa lỗi riêng cho TOP-RIGHT và BOTTOM-LEFT theo dump đúng.
            // Không lấy 3 điểm bằng sort gần/angle nữa vì dễ bắt lẫn endpoint thẳng:
            //   TOP-RIGHT sai: (2064,75) -> (2054.761,81.173) -> (2054,100)
            //   BOTTOM-LEFT sai: thứ tự điểm radius bị đảo và bắt điểm không đúng mẫu.
            // Rule mới: dựng 3 ArcPoint chuẩn theo cung R10 tại góc trong của rãnh,
            // đối xứng 4 hướng. Đây là đúng pattern Tekla trong dump radius thủ công.
            try
            {
                if (view == null || outer == null || inner == null)
                    return false;

                double r = 10.0;
                double cX;
                double cY;
                Point arc1;
                Point arc2;
                Point arc3;

                double k1 = 0.9238795325112867; // cos 22.5
                double k2 = 0.7071067811865476; // cos 45
                double k3 = 0.3826834323650898; // sin 22.5

                if (isTopSide)
                {
                    // TOP LEFT / TOP RIGHT:
                    // center nằm lệch 10mm từ góc trong về phía ngoài rãnh.
                    cX = inner.X + (isLeftSide ? -r : r);
                    cY = outer.Y + r;

                    if (isLeftSide)
                    {
                        // Dump đúng TL: (cx+9.239,cy-3.827) -> (cx+7.071,cy-7.071) -> (cx+3.827,cy-9.239)
                        arc1 = new Point(cX + r * k1, cY - r * k3, 0);
                        arc2 = new Point(cX + r * k2, cY - r * k2, 0);
                        arc3 = new Point(cX + r * k3, cY - r * k1, 0);
                        return CreateRadiusDimByReflection(view, arc1, arc2, arc3, 0.991896737251397);
                    }
                    else
                    {
                        // Dump đúng TOP-RIGHT:
                        // ArcPoint1 = inner-side upper arc, ArcPoint2 = mid, ArcPoint3 = outer-side lower arc.
                        arc1 = new Point(cX - r * k3, cY - r * k1, 0);
                        arc2 = new Point(cX - r * k2, cY - r * k2, 0);
                        arc3 = new Point(cX - r * k1, cY - r * k3, 0);
                        return CreateRadiusDimByReflection(view, arc1, arc2, arc3, 3.34232505030355);
                    }
                }
                else
                {
                    // BOTTOM LEFT / BOTTOM RIGHT.
                    cX = inner.X + (isLeftSide ? -r : r);
                    cY = outer.Y - r;

                    if (isLeftSide)
                    {
                        // Dump đúng BOTTOM-LEFT:
                        // ArcPoint1 = (cx,cy+r), ArcPoint2 = (cx+7.071,cy+7.071), ArcPoint3 = (cx+r,cy)
                        arc1 = new Point(cX + r * k3, cY + r * k1, 0);
                        arc2 = new Point(cX + r * k2, cY + r * k2, 0);
                        arc3 = new Point(cX + r * k1, cY + r * k3, 0);
                        return CreateRadiusDimByReflection(view, arc1, arc2, arc3, 8.53306717924072);
                    }
                    else
                    {
                        // BOTTOM-RIGHT đối xứng BOTTOM-LEFT.
                        arc1 = new Point(cX - r * k1, cY + r * k3, 0);
                        arc2 = new Point(cX - r * k2, cY + r * k2, 0);
                        arc3 = new Point(cX - r * k3, cY + r * k1, 0);
                        return CreateRadiusDimByReflection(view, arc1, arc2, arc3, 1.9404629528167);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateRadiusDimByReflection(
            View view,
            Point arc1,
            Point arc2,
            Point arc3,
            double distance)
        {
            // Dùng reflection để tránh lệ thuộc signature constructor RadiusDimension giữa các version Tekla.
            try
            {
                if (view == null || arc1 == null || arc2 == null || arc3 == null)
                    return false;

                Type t = typeof(RadiusDimension);
                ConstructorInfo[] ctors = t.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );

                foreach (ConstructorInfo ctor in ctors)
                {
                    try
                    {
                        ParameterInfo[] ps = ctor.GetParameters();
                        if (ps == null || ps.Length != 5)
                            continue;

                        object[] args = null;

                        if (ps[0].ParameterType.IsAssignableFrom(view.GetType()) &&
                            ps[1].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            ps[2].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            ps[3].ParameterType.IsAssignableFrom(typeof(Point)) &&
                            ps[4].ParameterType == typeof(double))
                        {
                            args = new object[] { view, arc1, arc2, arc3, distance };
                        }
                        else if (ps[0].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 ps[1].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 ps[2].ParameterType.IsAssignableFrom(typeof(Point)) &&
                                 ps[3].ParameterType == typeof(double) &&
                                 ps[4].ParameterType.IsAssignableFrom(view.GetType()))
                        {
                            args = new object[] { arc1, arc2, arc3, distance, view };
                        }

                        if (args == null)
                            continue;

                        object dim = ctor.Invoke(args);
                        DrawingObject dobj = dim as DrawingObject;
                        if (dobj == null)
                            continue;

                        return dobj.Insert();
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
            out ChamferInfluence influence)
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
                        Point verticalP1 = p1;
                        Point verticalP2 = p2;

                        if (rightSide)
                        {
                            if (p2.X > p1.X)
                            {
                                verticalP1 = p2;
                                verticalP2 = p1;
                            }
                        }
                        else
                        {
                            if (p2.X < p1.X)
                            {
                                verticalP1 = p2;
                                verticalP2 = p1;
                            }
                        }

                        if (CreateDim(
                            handler,
                            view,
                            verticalP1,
                            verticalP2,
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

            double realUpperTotalOffset = ResolveDimDistanceByAnchor4(
                lengthPts,
                new Vector(0, 1, 0),
                offsetAnchors,
                horizontalTotalOffset
            );

            if (handler.CreateDimensionSet(
                view,
                lengthPts,
                new Vector(0, 1, 0),
                realUpperTotalOffset) != null)
                count++;

            PointList heightPts = new PointList();
            // DIM tổng dọc phải bắt vào điểm thấp/cao ngoài cùng thật của dầm.
            heightPts.Add(Clone2D(edgeAnchors.TopMost));
            heightPts.Add(Clone2D(edgeAnchors.BottomMost));

            double realLeftTotalOffset = ResolveDimDistanceByAnchor4(
                heightPts,
                new Vector(-1, 0, 0),
                offsetAnchors,
                verticalTotalOffset
            );

            if (handler.CreateDimensionSet(
                view,
                heightPts,
                new Vector(-1, 0, 0),
                realLeftTotalOffset) != null)
                count++;

            return count;
        }


        private static int CreateTopViewHoleDimsByDiameter(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            List<Point> polygon,
            List<Point> verticalPolygon,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double verticalMinY,
            double verticalMaxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double beamLength,
            int reservedHorizontalTierCount,
            int reservedBottomTierCount,
            int reservedLeftTierCount,
            int reservedRightTierCount,
            out int usedTierCount,
            out int usedBottomTierCount,
            out int usedLeftTierCount,
            out int usedRightTierCount)
        {
            // TOP/BOTTOM VIEW - RULE LỖ MỚI THEO HÌNH HỌC:
            // - Không gom theo kích thước lỗ để chọn thuật toán nữa.
            // - Không còn rule đặc biệt theo kích thước lỗ.
            // - Phi lỗ thật chỉ dùng làm khoảng hở chân DIM.
            // - Top và Bottom dùng chung hàm này.
            // FIX4:
            // - Lỗ đơn đối xứng được xử lý ưu tiên trước toàn bộ cụm khác để giữ tầng 1.
            // - Hỗ trợ cả 2 phương lỗ đơn đối xứng: / và \.
            // - Thứ tự DIM dọc lỗ đơn giữ đúng: Lỗ -> Mép cho cả trái/phải.
            int count = 0;
            usedTierCount = 0;
            usedBottomTierCount = 0;
            usedLeftTierCount = 0;
            usedRightTierCount = 0;

            try
            {
                if (holes == null || holes.Count == 0)
                    return count;

                // TOP/BOTTOM - PHU FIX: tách cụm/chain theo giá trị lỗ.
                // Chỉ cần khác phi/M là phải thành cụm chain riêng,
                // không được gom chung dù cùng Y, cùng pattern X/Y.
                List<TopBottomHoleGroup> groups = new List<TopBottomHoleGroup>();
                Dictionary<double, List<Point>> holesByDiameter = GroupTopViewHolesByDiameter(holes);

                foreach (KeyValuePair<double, List<Point>> kv in holesByDiameter)
                {
                    List<TopBottomHoleGroup> diameterGroups =
                        BuildTopBottomHoleGroupsByGeometry(kv.Value, minX, maxX);

                    if (diameterGroups == null)
                        continue;

                    foreach (TopBottomHoleGroup g in diameterGroups)
                    {
                        if (g != null)
                            groups.Add(g);
                    }
                }

                if (groups == null || groups.Count == 0)
                    return count;

                int topUsedTierCount = reservedHorizontalTierCount;
                int bottomUsedTierCount = reservedBottomTierCount;
                int leftUsedTierCount = reservedLeftTierCount;
                int rightUsedTierCount = reservedRightTierCount;
                double middleVerticalDimOffset = CurrentMiddleVerticalDimOffset;

                // ƯU TIÊN 1 - LỖ ĐƠN ĐỐI XỨNG / LỖ ĐƠN:
                // Xử lý trước để không bị cụm 2 chiều/cụm ngang chiếm tầng.
                // Với cặp đối xứng trái/phải, mỗi bên vẫn dùng đúng phía đặt DIM riêng:
                // - bên trái: ngang dưới, dọc bên phải, thứ tự dọc Lỗ -> Mép.
                // - bên phải: ngang trên, dọc bên trái, thứ tự dọc Lỗ -> Mép.
                foreach (TopBottomHoleGroup group in groups)
                {
                    if (group == null || group.Holes == null || group.Holes.Count == 0)
                        continue;

                    if (group.Type != 0)
                        continue;

                    Point h = group.Holes[0];
                    if (h == null)
                        continue;

                    bool isLeftSide = IsHoleGroupOnLeftSide(group, minX, maxX);
                    bool horizontalDimOnTop = ShouldSingleHoleHorizontalDimBeTop(
                        group,
                        groups,
                        minX,
                        maxX,
                        isLeftSide
                    );

                    double horizontalOffset;
                    if (horizontalDimOnTop)
                    {
                        topUsedTierCount++;
                        horizontalOffset = GetSteelDimOffsetByTier(topUsedTierCount);
                    }
                    else
                    {
                        bottomUsedTierCount++;
                        horizontalOffset = GetSteelDimOffsetByTier(bottomUsedTierCount);
                    }

                    bool verticalDimOnRight = isLeftSide;
                    bool usesOuterSideTier = IsTopBottomHoleGroupNearPartEdge(group, minX, maxX);
                    int nextVerticalTier = usesOuterSideTier
                        ? (verticalDimOnRight ? rightUsedTierCount + 1 : leftUsedTierCount + 1)
                        : 0;
                    bool verticalDimCreated;

                    count += CreateTopBottomSingleHoleDims(
                        handler,
                        view,
                        h,
                        verticalPolygon,
                        verticalMinY,
                        verticalMaxY,
                        edgeAnchors,
                        offsetAnchors,
                        isLeftSide,
                        horizontalDimOnTop,
                        horizontalOffset,
                        usesOuterSideTier
                            ? GetSteelDimOffsetByTier(nextVerticalTier)
                            : middleVerticalDimOffset,
                        usesOuterSideTier,
                        out verticalDimCreated
                    );

                    if (verticalDimCreated && usesOuterSideTier)
                    {
                        if (verticalDimOnRight)
                            rightUsedTierCount++;
                        else
                            leftUsedTierCount++;
                    }

                    group.GroupDimDone = true;
                }

                // CỤM 2 CHIỀU TRÁI/PHẢI GIỐNG NHAU:
                // DIM ngang được phép nối chung thành 1 chain.
                // DIM dọc vẫn tách riêng từng cụm, tuyệt đối không gộp chung.
                List<TopBottomHoleGroup> twoDimGroups = GetTwoDimensionalGroups(groups);
                List<TopBottomHoleGroup> horizontalMergePair =
                    FindMergeableTwoDimensionalHorizontalPair(twoDimGroups);

                if (horizontalMergePair != null && horizontalMergePair.Count == 2)
                {
                    List<Point> mergedHorizontalHoles = new List<Point>();

                    foreach (TopBottomHoleGroup g in horizontalMergePair)
                    {
                        if (g == null || g.Holes == null)
                            continue;

                        foreach (Point h in g.Holes)
                        {
                            if (h != null)
                                mergedHorizontalHoles.Add(Clone2DWithDiameter(h));
                        }

                        g.HorizontalDimDone = true;
                    }

                    if (mergedHorizontalHoles.Count > 0)
                    {
                        topUsedTierCount++;
                        count += CreateTopBottomClusterHoleXFullChain(
                            handler,
                            view,
                            mergedHorizontalHoles,
                            edgeAnchors,
                            offsetAnchors,
                            GetSteelDimOffsetByTier(topUsedTierCount)
                        );
                    }
                }

                foreach (TopBottomHoleGroup group in groups)
                {
                    if (group == null || group.Holes == null || group.Holes.Count == 0)
                        continue;

                    if (group.GroupDimDone)
                        continue;

                    if (group.Type == 1)
                    {
                        // Cụm ngang 1 hàng cùng Y:
                        // Ngang: Mép -> lỗ -> lỗ -> mép.
                        // - Gần mép trên: đặt DIM ngang lên trên và chiếm tầng trên.
                        // - Gần mép dưới: đặt DIM ngang xuống dưới và chiếm tầng dưới.
                        // Dọc: chỉ 1 DIM đại diện cho cả hàng, thứ tự Lỗ -> Mép.
                        bool useBottomHorizontalSide = IsTopBottomHorizontalRowNearBottom(group, minY, maxY);

                        if (!HasHigherHorizontalRowWithSameColumns(groups, group))
                        {
                            if (useBottomHorizontalSide)
                            {
                                bottomUsedTierCount++;
                                count += CreateTopBottomClusterHoleXFullChainOnSide(
                                    handler,
                                    view,
                                    group.Holes,
                                    edgeAnchors,
                                    offsetAnchors,
                                    false,
                                    GetSteelDimOffsetByTier(bottomUsedTierCount)
                                );
                            }
                            else
                            {
                                topUsedTierCount++;
                                count += CreateTopBottomClusterHoleXFullChainOnSide(
                                    handler,
                                    view,
                                    group.Holes,
                                    edgeAnchors,
                                    offsetAnchors,
                                    true,
                                    GetSteelDimOffsetByTier(topUsedTierCount)
                                );
                            }
                        }

                        CreateTopBottomHorizontalGroupCenterLine(view, group.Holes);

                        bool verticalDimOnRight = ShouldTopBottomVerticalDimUseRightSide(group, minX, maxX);
                        bool usesOuterSideTier = IsTopBottomHoleGroupNearPartEdge(group, minX, maxX);
                        int nextVerticalTier = usesOuterSideTier
                            ? (verticalDimOnRight ? rightUsedTierCount + 1 : leftUsedTierCount + 1)
                            : 0;
                        int verticalCount = CreateTopBottomHorizontalGroupRepresentativeYDim(
                            handler,
                            view,
                            group.Holes,
                            verticalPolygon,
                            verticalMinY,
                            verticalMaxY,
                            offsetAnchors,
                            usesOuterSideTier
                                ? GetSteelDimOffsetByTier(nextVerticalTier)
                                : middleVerticalDimOffset
                        );

                        count += verticalCount;
                        if (verticalCount > 0 && usesOuterSideTier)
                        {
                            if (verticalDimOnRight)
                                rightUsedTierCount++;
                            else
                                leftUsedTierCount++;
                        }

                        continue;
                    }

                    if (group.Type == 2)
                    {
                        // Cụm dọc:
                        // Ngang: Mép -> lỗ -> mép.
                        // Dọc : Mép -> lỗ -> lỗ -> mép.
                        topUsedTierCount++;
                        count += CreateTopBottomClusterHoleXFullChain(
                            handler,
                            view,
                            group.Holes,
                            edgeAnchors,
                            offsetAnchors,
                            GetSteelDimOffsetByTier(topUsedTierCount)
                        );

                        bool verticalDimOnRight = ShouldTopBottomVerticalDimUseRightSide(group, minX, maxX);
                        bool usesOuterSideTier = IsTopBottomHoleGroupNearPartEdge(group, minX, maxX);
                        int nextVerticalTier = usesOuterSideTier
                            ? (verticalDimOnRight ? rightUsedTierCount + 1 : leftUsedTierCount + 1)
                            : 0;
                        int verticalCount = CreateTopBottomGroupYFullChainRepresentative(
                            handler,
                            view,
                            group.Holes,
                            verticalPolygon,
                            verticalMinY,
                            verticalMaxY,
                            offsetAnchors,
                            usesOuterSideTier
                                ? GetSteelDimOffsetByTier(nextVerticalTier)
                                : middleVerticalDimOffset
                        );

                        count += verticalCount;
                        if (verticalCount > 0 && usesOuterSideTier)
                        {
                            if (verticalDimOnRight)
                                rightUsedTierCount++;
                            else
                                leftUsedTierCount++;
                        }

                        continue;
                    }

                    if (group.Type == 3)
                    {
                        // Cụm 2 chiều:
                        // Chỉ dim 1 hàng đại diện ngang và 1 cột đại diện dọc.
                        // Nếu đã được nối chain ngang chung với cụm đối diện thì không tạo chain ngang riêng nữa.
                        if (!group.HorizontalDimDone)
                        {
                            topUsedTierCount++;
                            count += CreateTopBottomClusterHoleXFullChain(
                                handler,
                                view,
                                group.Holes,
                                edgeAnchors,
                                offsetAnchors,
                                GetSteelDimOffsetByTier(topUsedTierCount)
                            );
                        }

                        // Một chain ngang có thể cố ý chứa nhiều cụm XxY rời nhau (ví dụ hai đầu dầm).
                        // Chỉ tách lại cho DIM dọc để mỗi cụm có một chain đại diện riêng.
                        List<TopBottomHoleGroup> verticalGroups =
                            SplitTopBottomTwoDimensionalGroupForVerticalDims(
                                group,
                                minX,
                                maxX,
                                Math.Max(2.0, TOL + 1.0)
                            );

                        foreach (TopBottomHoleGroup verticalGroup in verticalGroups)
                        {
                            if (verticalGroup == null ||
                                verticalGroup.Holes == null ||
                                verticalGroup.Holes.Count == 0)
                                continue;

                            bool verticalDimOnRight =
                                ShouldTopBottomVerticalDimUseRightSide(verticalGroup, minX, maxX);
                            bool usesOuterSideTier =
                                IsTopBottomHoleGroupNearPartEdge(verticalGroup, minX, maxX);
                            int nextVerticalTier = usesOuterSideTier
                                ? (verticalDimOnRight ? rightUsedTierCount + 1 : leftUsedTierCount + 1)
                                : 0;
                            int verticalCount = CreateTopBottomGroupYFullChainRepresentative(
                                handler,
                                view,
                                verticalGroup.Holes,
                                verticalPolygon,
                                verticalMinY,
                                verticalMaxY,
                                offsetAnchors,
                                usesOuterSideTier
                                    ? GetSteelDimOffsetByTier(nextVerticalTier)
                                    : middleVerticalDimOffset
                            );

                            count += verticalCount;
                            if (verticalCount > 0 && usesOuterSideTier)
                            {
                                if (verticalDimOnRight)
                                    rightUsedTierCount++;
                                else
                                    leftUsedTierCount++;
                            }
                        }

                        continue;
                    }
                }

                usedTierCount = Math.Max(0, topUsedTierCount - reservedHorizontalTierCount);
                usedBottomTierCount = Math.Max(0, bottomUsedTierCount - reservedBottomTierCount);
                usedLeftTierCount = Math.Max(0, leftUsedTierCount - reservedLeftTierCount);
                usedRightTierCount = Math.Max(0, rightUsedTierCount - reservedRightTierCount);
            }
            catch
            {
            }

            return count;
        }

        private static List<TopBottomHoleGroup> SplitTopBottomTwoDimensionalGroupForVerticalDims(
            TopBottomHoleGroup group,
            double partMinX,
            double partMaxX,
            double tol)
        {
            List<TopBottomHoleGroup> result = new List<TopBottomHoleGroup>();

            try
            {
                if (group == null || group.Holes == null || group.Holes.Count == 0)
                    return result;

                List<double> columns =
                    GetUniqueCoordinatesFromHoles(group.Holes, true, tol);

                if (columns.Count < 2 || group.YCount < 2)
                {
                    result.Add(group);
                    return result;
                }

                double smallestColumnGap = 999999999.0;
                for (int i = 0; i < columns.Count - 1; i++)
                {
                    double gap = columns[i + 1] - columns[i];
                    if (gap > tol && gap < smallestColumnGap)
                        smallestColumnGap = gap;
                }

                if (smallestColumnGap > 900000000.0)
                {
                    result.Add(group);
                    return result;
                }

                double adaptiveSplitGap = Math.Max(
                    TOP_BOTTOM_VERTICAL_CLUSTER_MIN_SPLIT_GAP,
                    smallestColumnGap * TOP_BOTTOM_VERTICAL_CLUSTER_GAP_RATIO
                );

                double partWidth = Math.Abs(partMaxX - partMinX);
                bool spansOppositeEdges =
                    Math.Abs(columns[0] - partMinX) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE &&
                    Math.Abs(partMaxX - columns[columns.Count - 1]) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;

                List<int> splitAfterColumnIndexes = new List<int>();
                for (int i = 0; i < columns.Count - 1; i++)
                {
                    double gap = columns[i + 1] - columns[i];
                    bool isRelativeClusterGap =
                        columns.Count >= 3 && gap > adaptiveSplitGap;
                    bool isOppositeEdgeClusterGap =
                        spansOppositeEdges &&
                        partWidth > tol &&
                        gap > Math.Max(
                            TOP_BOTTOM_VERTICAL_CLUSTER_MIN_SPLIT_GAP,
                            partWidth * 0.40
                        );

                    if (isRelativeClusterGap || isOppositeEdgeClusterGap)
                        splitAfterColumnIndexes.Add(i);
                }

                if (splitAfterColumnIndexes.Count == 0)
                {
                    result.Add(group);
                    return result;
                }

                int firstColumnIndex = 0;
                for (int splitIndex = 0;
                     splitIndex <= splitAfterColumnIndexes.Count;
                     splitIndex++)
                {
                    int lastColumnIndex = splitIndex < splitAfterColumnIndexes.Count
                        ? splitAfterColumnIndexes[splitIndex]
                        : columns.Count - 1;

                    List<Point> clusterHoles = new List<Point>();
                    double clusterMinX = columns[firstColumnIndex] - tol;
                    double clusterMaxX = columns[lastColumnIndex] + tol;

                    foreach (Point hole in group.Holes)
                    {
                        if (hole != null &&
                            hole.X >= clusterMinX &&
                            hole.X <= clusterMaxX)
                        {
                            clusterHoles.Add(Clone2DWithDiameter(hole));
                        }
                    }

                    TopBottomHoleGroup cluster =
                        CreateTopBottomHoleGroup(clusterHoles, 3, tol);

                    // Không tách nếu một phía không còn đủ toàn bộ pattern Y của cụm gốc.
                    if (cluster.Holes.Count == 0 ||
                        cluster.YCount != group.YCount ||
                        cluster.Holes.Count != cluster.XCount * cluster.YCount)
                    {
                        result.Clear();
                        result.Add(group);
                        return result;
                    }

                    result.Add(cluster);
                    firstColumnIndex = lastColumnIndex + 1;
                }

                if (result.Count < 2)
                {
                    result.Clear();
                    result.Add(group);
                }
            }
            catch
            {
                result.Clear();
                if (group != null)
                    result.Add(group);
            }

            return result;
        }

        private static bool IsTopBottomHoleGroupNearPartEdge(
            TopBottomHoleGroup group,
            double minX,
            double maxX)
        {
            if (group == null)
                return false;

            double centerX = (group.MinX + group.MaxX) / 2.0;
            return Math.Abs(centerX - minX) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE ||
                   Math.Abs(maxX - centerX) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;
        }

        private static bool IsTopBottomHorizontalRowNearBottom(
            TopBottomHoleGroup group,
            double minY,
            double maxY)
        {
            try
            {
                if (group == null)
                    return false;

                double cy = (group.MinY + group.MaxY) / 2.0;
                return Math.Abs(cy - minY) <= Math.Abs(maxY - cy);
            }
            catch
            {
                return false;
            }
        }

        private static bool AreTopBottomGroupsSameHoleSize(
            TopBottomHoleGroup a,
            TopBottomHoleGroup b)
        {
            try
            {
                if (a == null || b == null ||
                    a.Holes == null || b.Holes == null ||
                    a.Holes.Count == 0 || b.Holes.Count == 0)
                    return false;

                double da = GetHoleDiameterKey(a.Holes[0]);
                double db = GetHoleDiameterKey(b.Holes[0]);

                return Math.Abs(da - db) <= TOP_BOTTOM_HOLE_SIZE_TOL;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasHigherHorizontalRowWithSameColumns(
            List<TopBottomHoleGroup> groups,
            TopBottomHoleGroup current)
        {
            // TOP/BOTTOM - chống DIM ngang lặp 2 dãy trên/dưới:
            // Nếu current là một dãy ngang và phía trên nó có một dãy ngang khác
            // có cùng các cột X, thì current được xem là dãy dưới và không cần DIM ngang.
            try
            {
                if (groups == null || current == null || current.Holes == null || current.Holes.Count == 0)
                    return false;

                if (current.Type != 1)
                    return false;

                double tol = Math.Max(2.0, TOL + 1.0);
                List<Point> currentColumns = BuildUniqueHoleColumnsForChain(current.Holes, tol);
                if (currentColumns == null || currentColumns.Count == 0)
                    return false;

                foreach (TopBottomHoleGroup other in groups)
                {
                    if (other == null || System.Object.ReferenceEquals(other, current))
                        continue;

                    if (other.Type != 1 || other.Holes == null || other.Holes.Count == 0)
                        continue;

                    // Khác giá trị lỗ thì là cụm chain riêng, không được dùng để chặn DIM ngang nhau.
                    if (!AreTopBottomGroupsSameHoleSize(current, other))
                        continue;

                    // Chỉ xét dãy nằm cao hơn dãy hiện tại.
                    if (other.MaxY <= current.MaxY + tol)
                        continue;

                    List<Point> otherColumns = BuildUniqueHoleColumnsForChain(other.Holes, tol);
                    if (otherColumns == null || otherColumns.Count != currentColumns.Count)
                        continue;

                    if (HaveSameHoleColumns(currentColumns, otherColumns, tol))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool HaveSameHoleColumns(
            List<Point> a,
            List<Point> b,
            double tol)
        {
            try
            {
                if (a == null || b == null || a.Count != b.Count)
                    return false;

                a.Sort(delegate (Point p1, Point p2) { return p1.X.CompareTo(p2.X); });
                b.Sort(delegate (Point p1, Point p2) { return p1.X.CompareTo(p2.X); });

                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] == null || b[i] == null)
                        return false;

                    if (Math.Abs(a[i].X - b[i].X) > tol)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<TopBottomHoleGroup> BuildTopBottomHoleGroupsByGeometry(
            List<Point> holes,
            double minX,
            double maxX)
        {
            // TOP/BOTTOM VIEW - GOM NHÓM THEO HÌNH HỌC, KHÔNG GOM THEO PHI:
            // FIX AN TOÀN:
            // - Không ép cả một vùng thành cụm 2 chiều chỉ vì có >=2 X và >=2 Y.
            // - Cụm 2 chiều chỉ hợp lệ khi các cột có cùng pattern Y và tạo được lưới đầy đủ.
            // - Lỗ đơn nằm sát cụm 2x2/2x3 nhưng khác pattern sẽ bị tách riêng, không bị kéo vào chain.
            // - Hai cụm 2 chiều trái/phải giống nhau vẫn tách thành 2 group riêng để DIM dọc riêng,
            //   nhưng hàm phía trên có thể nối DIM ngang chung.
            List<TopBottomHoleGroup> result = new List<TopBottomHoleGroup>();

            try
            {
                if (holes == null || holes.Count == 0)
                    return result;

                List<Point> clean = new List<Point>();
                foreach (Point h in holes)
                {
                    if (h != null)
                        clean.Add(Clone2DWithDiameter(h));
                }

                if (clean.Count == 0)
                    return result;

                double tol = Math.Max(2.0, TOL + 1.0);

                clean.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });

                // FIX PHU 2026-06-11 - ABSOLUTE SAME-Y RULE:
                // Dãy lỗ ngang cùng 1 cao độ Y là 1 cụm thật, TUYỆT ĐỐI không được tách theo khoảng cách X.
                // Rule này áp dụng cho từng hàng Y trong toàn bộ TOP/BOTTOM view, không chỉ khi toàn bộ lỗ có 1 Y.
                // Ví dụ nhiều lỗ Φ14 cùng Y=20 dù cách nhau 500/800/1000 vẫn chỉ tạo 1 chain ngang
                // và 1 DIM dọc đại diện.
                // Sau khi gom xong các hàng ngang cùng Y, phần lỗ còn lại mới dùng logic cũ.
                List<List<Point>> absoluteSameYRows = BuildAbsoluteSameYRows(clean, tol);
                List<Point> usedByAbsoluteSameY = new List<Point>();

                // TOP/BOTTOM - FIX CỤM LỖ 2 HÀNG / NHIỀU HÀNG:
                // Trước đây cùng Y được ưu tiên tách thành từng Type 1 row,
                // nên cụm 2x2 bị tạo DIM dọc kiểu Mép -> Lỗ cho từng hàng.
                // Rule mới: nếu các hàng có cùng pattern cột X thì nhận là cụm 2 chiều Type 3,
                // để DIM dọc chạy đúng chain: Mép -> Lỗ -> Lỗ -> Mép.
                bool[] rowUsed = new bool[absoluteSameYRows.Count];

                for (int i = 0; i < absoluteSameYRows.Count; i++)
                {
                    if (rowUsed[i])
                        continue;

                    List<Point> baseRow = absoluteSameYRows[i];
                    if (baseRow == null || baseRow.Count < 2)
                        continue;

                    List<Point> baseColumns = BuildUniqueHoleColumnsForChain(baseRow, tol);
                    if (baseColumns == null || baseColumns.Count < 2)
                        continue;

                    List<int> matchingRowIndexes = new List<int>();
                    matchingRowIndexes.Add(i);

                    for (int j = i + 1; j < absoluteSameYRows.Count; j++)
                    {
                        if (rowUsed[j])
                            continue;

                        List<Point> otherRow = absoluteSameYRows[j];
                        if (otherRow == null || otherRow.Count < 2)
                            continue;

                        List<Point> otherColumns = BuildUniqueHoleColumnsForChain(otherRow, tol);
                        if (otherColumns == null || otherColumns.Count != baseColumns.Count)
                            continue;

                        if (HaveSameHoleColumns(baseColumns, otherColumns, tol))
                            matchingRowIndexes.Add(j);
                    }

                    if (matchingRowIndexes.Count >= 2)
                    {
                        List<Point> rectangular = new List<Point>();

                        foreach (int rowIndex in matchingRowIndexes)
                        {
                            rowUsed[rowIndex] = true;
                            List<Point> row = absoluteSameYRows[rowIndex];
                            if (row == null)
                                continue;

                            foreach (Point h in row)
                            {
                                if (h != null)
                                {
                                    Point p = Clone2DWithDiameter(h);
                                    rectangular.Add(p);
                                    usedByAbsoluteSameY.Add(Clone2DWithDiameter(p));
                                }
                            }
                        }

                        if (rectangular.Count > 0)
                            result.Add(CreateTopBottomHoleGroup(rectangular, 3, tol));
                    }
                }

                for (int i = 0; i < absoluteSameYRows.Count; i++)
                {
                    if (rowUsed[i])
                        continue;

                    List<Point> row = absoluteSameYRows[i];
                    if (row == null || row.Count < 2)
                        continue;

                    List<double> rowXs = GetUniqueCoordinatesFromHoles(row, true, tol);
                    if (rowXs.Count < 2)
                        continue;

                    row.Sort(delegate (Point a, Point b)
                    {
                        return a.X.CompareTo(b.X);
                    });

                    result.Add(CreateTopBottomHoleGroup(row, 1, tol));

                    foreach (Point h in row)
                    {
                        if (h != null)
                            usedByAbsoluteSameY.Add(Clone2DWithDiameter(h));
                    }
                }

                List<Point> remainingAfterSameY = new List<Point>();
                foreach (Point h in clean)
                {
                    if (h == null)
                        continue;

                    if (!ContainsHoleByXY(usedByAbsoluteSameY, h, tol))
                        remainingAfterSameY.Add(Clone2DWithDiameter(h));
                }

                if (remainingAfterSameY.Count == 0)
                    return result;

                clean = remainingAfterSameY;
                clean.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });

                // FIX PHU 2026-06-11 - CLEAN TOP/BOTTOM NO-X-SPLIT:
                // Đã xóa hoàn toàn rule tách cụm theo khoảng cách X > 300.
                // Phần lỗ còn lại cũng chạy gom hình học trực tiếp trên toàn bộ danh sách,
                // chỉ tách theo Y / pattern hình học / lỗ đơn, không tách theo khoảng cách ngang.
                AddTopBottomHoleGroupsFromBand(result, clean, tol);
            }
            catch
            {
            }

            return result;
        }

        private static List<List<Point>> BuildAbsoluteSameYRows(List<Point> holes, double tol)
        {
            // Gom các lỗ cùng Y trên toàn view thành từng hàng, không xét khoảng cách X.
            // Dùng cho rule: cùng Y là một dãy ngang, tuyệt đối không tách cụm theo X.
            List<List<Point>> rows = new List<List<Point>>();

            try
            {
                if (holes == null || holes.Count == 0)
                    return rows;

                List<Point> sorted = new List<Point>();
                foreach (Point h in holes)
                {
                    if (h != null)
                        sorted.Add(Clone2DWithDiameter(h));
                }

                sorted.Sort(delegate (Point a, Point b)
                {
                    int c = a.Y.CompareTo(b.Y);
                    if (c != 0) return c;
                    return a.X.CompareTo(b.X);
                });

                foreach (Point h in sorted)
                {
                    bool added = false;

                    foreach (List<Point> row in rows)
                    {
                        if (row == null || row.Count == 0)
                            continue;

                        if (Math.Abs(row[0].Y - h.Y) <= tol)
                        {
                            row.Add(Clone2DWithDiameter(h));
                            added = true;
                            break;
                        }
                    }

                    if (!added)
                    {
                        List<Point> row = new List<Point>();
                        row.Add(Clone2DWithDiameter(h));
                        rows.Add(row);
                    }
                }
            }
            catch
            {
            }

            return rows;
        }

        private static void AddTopBottomHoleGroupsFromBand(
            List<TopBottomHoleGroup> result,
            List<Point> band,
            double tol)
        {
            try
            {
                if (result == null || band == null || band.Count == 0)
                    return;

                List<Point> clean = new List<Point>();
                foreach (Point h in band)
                {
                    if (h != null)
                        clean.Add(Clone2DWithDiameter(h));
                }

                if (clean.Count == 0)
                    return;

                List<double> xs = GetUniqueCoordinatesFromHoles(clean, true, tol);
                List<double> ys = GetUniqueCoordinatesFromHoles(clean, false, tol);

                if (clean.Count == 1)
                {
                    result.Add(CreateTopBottomHoleGroup(clean, 0, tol));
                    return;
                }

                if (ys.Count == 1 && xs.Count >= 2)
                {
                    result.Add(CreateTopBottomHoleGroup(clean, 1, tol));
                    return;
                }

                if (xs.Count == 1 && ys.Count >= 2)
                {
                    result.Add(CreateTopBottomHoleGroup(clean, 2, tol));
                    return;
                }

                if (clean.Count == 2 && xs.Count == 2 && ys.Count == 2)
                {
                    foreach (Point h in clean)
                    {
                        List<Point> one = new List<Point>();
                        one.Add(Clone2DWithDiameter(h));
                        result.Add(CreateTopBottomHoleGroup(one, 0, tol));
                    }
                    return;
                }

                // Nếu cả band là một lưới đầy đủ thì nhận ngay là cụm 2 chiều.
                if (IsCompleteRectangularHoleGrid(clean, tol))
                {
                    result.Add(CreateTopBottomHoleGroup(clean, 3, tol));
                    return;
                }

                // Band hỗn hợp: ví dụ cụm 2x2 + 1 lỗ đơn kế bên.
                // Tách cụm 2 chiều bằng pattern Y của từng cột.
                List<List<Point>> rectangularGroups = ExtractCompleteRectangularGroupsByColumnPattern(clean, tol);
                List<Point> used = new List<Point>();

                foreach (List<Point> rg in rectangularGroups)
                {
                    if (rg == null || rg.Count == 0)
                        continue;

                    if (IsCompleteRectangularHoleGrid(rg, tol))
                    {
                        result.Add(CreateTopBottomHoleGroup(rg, 3, tol));
                        foreach (Point h in rg)
                            used.Add(Clone2DWithDiameter(h));
                    }
                }

                List<Point> remaining = new List<Point>();
                foreach (Point h in clean)
                {
                    if (h == null)
                        continue;

                    if (!ContainsHoleByXY(used, h, tol))
                        remaining.Add(Clone2DWithDiameter(h));
                }

                if (remaining.Count == 0)
                    return;

                // Phần còn lại xử lý lại theo rule đơn giản.
                // Nếu vẫn hỗn hợp khó đọc thì tách lỗ đơn để tránh chain sai.
                List<double> rx = GetUniqueCoordinatesFromHoles(remaining, true, tol);
                List<double> ry = GetUniqueCoordinatesFromHoles(remaining, false, tol);

                if (remaining.Count == 1)
                {
                    result.Add(CreateTopBottomHoleGroup(remaining, 0, tol));
                }
                else if (ry.Count == 1 && rx.Count >= 2)
                {
                    result.Add(CreateTopBottomHoleGroup(remaining, 1, tol));
                }
                else if (rx.Count == 1 && ry.Count >= 2)
                {
                    result.Add(CreateTopBottomHoleGroup(remaining, 2, tol));
                }
                else if (IsCompleteRectangularHoleGrid(remaining, tol))
                {
                    result.Add(CreateTopBottomHoleGroup(remaining, 3, tol));
                }
                else
                {
                    foreach (Point h in remaining)
                    {
                        List<Point> one = new List<Point>();
                        one.Add(Clone2DWithDiameter(h));
                        result.Add(CreateTopBottomHoleGroup(one, 0, tol));
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsCompleteRectangularHoleGrid(List<Point> holes, double tol)
        {
            try
            {
                if (holes == null || holes.Count < 4)
                    return false;

                List<double> xs = GetUniqueCoordinatesFromHoles(holes, true, tol);
                List<double> ys = GetUniqueCoordinatesFromHoles(holes, false, tol);

                if (xs.Count < 2 || ys.Count < 2)
                    return false;

                if (holes.Count != xs.Count * ys.Count)
                    return false;

                foreach (double x in xs)
                {
                    foreach (double y in ys)
                    {
                        if (!ContainsHoleByXY(holes, x, y, tol))
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<List<Point>> ExtractCompleteRectangularGroupsByColumnPattern(
            List<Point> holes,
            double tol)
        {
            List<List<Point>> result = new List<List<Point>>();

            try
            {
                if (holes == null || holes.Count < 4)
                    return result;

                List<double> xs = GetUniqueCoordinatesFromHoles(holes, true, tol);

                List<List<Point>> usedGroups = new List<List<Point>>();

                for (int i = 0; i < xs.Count; i++)
                {
                    double xBase = xs[i];
                    List<Point> baseColumn = GetHolesOnColumn(holes, xBase, tol);
                    List<double> baseYs = GetUniqueCoordinatesFromHoles(baseColumn, false, tol);

                    if (baseYs.Count < 2)
                        continue;

                    List<double> matchingXs = new List<double>();
                    matchingXs.Add(xBase);

                    for (int j = i + 1; j < xs.Count; j++)
                    {
                        double xOther = xs[j];
                        List<Point> otherColumn = GetHolesOnColumn(holes, xOther, tol);
                        List<double> otherYs = GetUniqueCoordinatesFromHoles(otherColumn, false, tol);

                        if (AreCoordinatePatternsSame(baseYs, otherYs, tol))
                            matchingXs.Add(xOther);
                    }

                    if (matchingXs.Count < 2)
                        continue;

                    List<Point> candidate = new List<Point>();
                    foreach (double mx in matchingXs)
                    {
                        List<Point> col = GetHolesOnColumn(holes, mx, tol);
                        foreach (Point h in col)
                            candidate.Add(Clone2DWithDiameter(h));
                    }

                    if (!IsCompleteRectangularHoleGrid(candidate, tol))
                        continue;

                    bool alreadyUsed = false;
                    foreach (List<Point> ug in usedGroups)
                    {
                        if (GroupsOverlapByXY(ug, candidate, tol))
                        {
                            alreadyUsed = true;
                            break;
                        }
                    }

                    if (!alreadyUsed)
                    {
                        result.Add(candidate);
                        usedGroups.Add(candidate);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool ContainsHoleByXY(List<Point> holes, Point target, double tol)
        {
            if (target == null)
                return false;

            return ContainsHoleByXY(holes, target.X, target.Y, tol);
        }

        private static bool ContainsHoleByXY(List<Point> holes, double x, double y, double tol)
        {
            try
            {
                if (holes == null)
                    return false;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.X - x) <= tol && Math.Abs(h.Y - y) <= tol)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static List<Point> GetHolesOnColumn(List<Point> holes, double x, double tol)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (holes == null)
                    return result;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.X - x) <= tol)
                        result.Add(Clone2DWithDiameter(h));
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool GroupsOverlapByXY(List<Point> a, List<Point> b, double tol)
        {
            try
            {
                if (a == null || b == null)
                    return false;

                foreach (Point pa in a)
                {
                    if (pa == null)
                        continue;

                    if (ContainsHoleByXY(b, pa, tol))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static TopBottomHoleGroup CreateTopBottomHoleGroup(
            List<Point> holes,
            int type,
            double tol)
        {
            TopBottomHoleGroup group = new TopBottomHoleGroup();
            group.Type = type;
            group.HorizontalDimDone = false;

            try
            {
                if (holes == null)
                    return group;

                group.MinX = 999999999.0;
                group.MaxX = -999999999.0;
                group.MinY = 999999999.0;
                group.MaxY = -999999999.0;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    Point p = Clone2DWithDiameter(h);
                    group.Holes.Add(p);

                    if (p.X < group.MinX) group.MinX = p.X;
                    if (p.X > group.MaxX) group.MaxX = p.X;
                    if (p.Y < group.MinY) group.MinY = p.Y;
                    if (p.Y > group.MaxY) group.MaxY = p.Y;
                }

                group.XCount = GetUniqueCoordinatesFromHoles(group.Holes, true, tol).Count;
                group.YCount = GetUniqueCoordinatesFromHoles(group.Holes, false, tol).Count;
            }
            catch
            {
            }

            return group;
        }

        private static List<double> GetUniqueCoordinatesFromHoles(
            List<Point> holes,
            bool useX,
            double tol)
        {
            List<double> result = new List<double>();

            try
            {
                if (holes == null)
                    return result;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    AddUniqueCoordinate(result, useX ? h.X : h.Y, tol);
                }

                result.Sort();
            }
            catch
            {
            }

            return result;
        }

        private static List<TopBottomHoleGroup> GetTwoDimensionalGroups(
            List<TopBottomHoleGroup> groups)
        {
            List<TopBottomHoleGroup> result = new List<TopBottomHoleGroup>();

            try
            {
                if (groups == null)
                    return result;

                foreach (TopBottomHoleGroup g in groups)
                {
                    if (g != null && g.Type == 3)
                        result.Add(g);
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<TopBottomHoleGroup> FindMergeableTwoDimensionalHorizontalPair(
            List<TopBottomHoleGroup> groups)
        {
            // FIX3:
            // Top/Bottom dùng chung rule: nếu có 2 cụm 2 chiều trái/phải cùng layout
            // thì gộp DIM ngang chung. DIM dọc vẫn tách riêng từng cụm.
            // Không yêu cầu danh sách chỉ có đúng 2 cụm, để tránh Bottom bị tách khi
            // view có thêm cụm/lỗ khác ở giữa.
            List<TopBottomHoleGroup> result = new List<TopBottomHoleGroup>();

            try
            {
                if (groups == null || groups.Count < 2)
                    return result;

                TopBottomHoleGroup bestLeft = null;
                TopBottomHoleGroup bestRight = null;
                double bestSpan = -1.0;

                for (int i = 0; i < groups.Count; i++)
                {
                    TopBottomHoleGroup a = groups[i];
                    if (a == null)
                        continue;

                    for (int j = i + 1; j < groups.Count; j++)
                    {
                        TopBottomHoleGroup b = groups[j];
                        if (b == null)
                            continue;

                        if (!AreTwoDimensionalGroupsSameLayout(a, b))
                            continue;

                        double centerA = (a.MinX + a.MaxX) / 2.0;
                        double centerB = (b.MinX + b.MaxX) / 2.0;
                        double span = Math.Abs(centerB - centerA);

                        if (span > bestSpan)
                        {
                            bestSpan = span;
                            if (centerA <= centerB)
                            {
                                bestLeft = a;
                                bestRight = b;
                            }
                            else
                            {
                                bestLeft = b;
                                bestRight = a;
                            }
                        }
                    }
                }

                if (bestLeft != null && bestRight != null)
                {
                    result.Add(bestLeft);
                    result.Add(bestRight);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool AreTwoDimensionalGroupsSameLayout(
            TopBottomHoleGroup a,
            TopBottomHoleGroup b)
        {
            try
            {
                if (a == null || b == null)
                    return false;

                if (a.Type != 3 || b.Type != 3)
                    return false;

                // Khác giá trị lỗ thì tuyệt đối không gộp DIM ngang chung.
                if (!AreTopBottomGroupsSameHoleSize(a, b))
                    return false;

                if (a.XCount != b.XCount || a.YCount != b.YCount)
                    return false;

                if (a.XCount < 2 || a.YCount < 2)
                    return false;

                double tol = Math.Max(2.0, TOL + 1.0);

                List<double> ax = GetRelativeCoordinates(a.Holes, true, tol);
                List<double> bx = GetRelativeCoordinates(b.Holes, true, tol);
                List<double> ay = GetRelativeCoordinates(a.Holes, false, tol);
                List<double> by = GetRelativeCoordinates(b.Holes, false, tol);

                return AreCoordinatePatternsSame(ax, bx, tol) &&
                       AreCoordinatePatternsSame(ay, by, tol);
            }
            catch
            {
                return false;
            }
        }

        private static List<double> GetRelativeCoordinates(
            List<Point> holes,
            bool useX,
            double tol)
        {
            List<double> result = GetUniqueCoordinatesFromHoles(holes, useX, tol);

            try
            {
                if (result.Count == 0)
                    return result;

                double first = result[0];

                for (int i = 0; i < result.Count; i++)
                    result[i] = result[i] - first;
            }
            catch
            {
            }

            return result;
        }

        private static bool AreCoordinatePatternsSame(
            List<double> a,
            List<double> b,
            double tol)
        {
            if (a == null || b == null)
                return false;

            if (a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i] - b[i]) > tol)
                    return false;
            }

            return true;
        }

        private static bool IsHoleGroupOnLeftSide(
            TopBottomHoleGroup group,
            double minX,
            double maxX)
        {
            try
            {
                if (group == null)
                    return true;

                double center = (group.MinX + group.MaxX) / 2.0;
                double beamCenter = (minX + maxX) / 2.0;

                return center <= beamCenter;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldSingleHoleHorizontalDimBeTop(
            TopBottomHoleGroup group,
            List<TopBottomHoleGroup> groups,
            double minX,
            double maxX,
            bool isLeftSide)
        {
            // LỖ ĐƠN ĐỐI XỨNG - VỊ TRÍ DIM NGANG:
            // Phương /  : trái đặt dưới, phải đặt trên (rule đang ổn).
            // Phương \ : trái đặt trên, phải đặt dưới (rule mới).
            // Nếu không tìm được cặp đối xứng rõ ràng thì giữ rule cũ để tránh phá case đang chạy.
            try
            {
                bool defaultTop = !isLeftSide;

                if (group == null || groups == null)
                    return defaultTop;

                TopBottomHoleGroup partner = FindOppositeSingleHoleGroup(group, groups, minX, maxX, isLeftSide);
                if (partner == null)
                    return defaultTop;

                double groupY = (group.MinY + group.MaxY) / 2.0;
                double partnerY = (partner.MinY + partner.MaxY) / 2.0;
                double tol = Math.Max(2.0, TOL + 1.0);

                // Phương \ nghĩa là lỗ trái cao hơn lỗ phải.
                bool backSlashDirection;
                if (isLeftSide)
                    backSlashDirection = groupY > partnerY + tol;
                else
                    backSlashDirection = partnerY > groupY + tol;

                if (backSlashDirection)
                    return isLeftSide;

                return defaultTop;
            }
            catch
            {
                return !isLeftSide;
            }
        }

        private static TopBottomHoleGroup FindOppositeSingleHoleGroup(
            TopBottomHoleGroup group,
            List<TopBottomHoleGroup> groups,
            double minX,
            double maxX,
            bool isLeftSide)
        {
            try
            {
                if (group == null || groups == null)
                    return null;

                double tol = Math.Max(5.0, TOL + 4.0);
                double groupCenterX = (group.MinX + group.MaxX) / 2.0;
                double groupDistToNearEdge = isLeftSide
                    ? Math.Abs(groupCenterX - minX)
                    : Math.Abs(maxX - groupCenterX);

                TopBottomHoleGroup best = null;
                double bestDiff = 999999999.0;

                foreach (TopBottomHoleGroup other in groups)
                {
                    if (other == null || other == group || other.Type != 0)
                        continue;

                    bool otherLeftSide = IsHoleGroupOnLeftSide(other, minX, maxX);
                    if (otherLeftSide == isLeftSide)
                        continue;

                    double otherCenterX = (other.MinX + other.MaxX) / 2.0;
                    double otherDistToNearEdge = otherLeftSide
                        ? Math.Abs(otherCenterX - minX)
                        : Math.Abs(maxX - otherCenterX);

                    double diff = Math.Abs(groupDistToNearEdge - otherDistToNearEdge);
                    if (diff <= tol && diff < bestDiff)
                    {
                        bestDiff = diff;
                        best = other;
                    }
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldTopBottomVerticalDimUseRightSide(
            TopBottomHoleGroup group,
            double partMinX,
            double partMaxX)
        {
            try
            {
                if (group == null)
                    return false;

                double center = (group.MinX + group.MaxX) / 2.0;
                double distToLeft = Math.Abs(center - partMinX);
                double distToRight = Math.Abs(partMaxX - center);

                bool nearLeft = distToLeft < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;
                bool nearRight = distToRight < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;

                if (nearRight && (!nearLeft || distToRight < distToLeft))
                    return true;

                // Gần trái hoặc nằm giữa dầm đều đặt bên trái.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static int CreateTopBottomSingleHoleDims(
            StraightDimensionSetHandler handler,
            View view,
            Point hole,
            List<Point> verticalPolygon,
            double verticalMinY,
            double verticalMaxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            bool isLeftSide,
            bool horizontalDimOnTop,
            double horizontalOffset,
            double verticalOffset,
            bool useRealSideEdgeOffset,
            out bool verticalDimCreated)
        {
            int count = 0;
            verticalDimCreated = false;

            try
            {
                if (hole == null)
                    return count;

                double edgeTol = Math.Max(2.0, TOL + 1.0);
                double gap = GetHoleDimGap(hole);
                if (gap <= MIN_VALID_HOLE_DIM_GAP)
                    gap = 0.0;

                // DIM ngang:
                // Phương /  : trái đặt dưới, phải đặt trên.
                // Phương \ : trái đặt trên, phải đặt dưới.
                Point horizontalEdgePoint;

                if (isLeftSide)
                {
                    horizontalEdgePoint = horizontalDimOnTop
                        ? Clone2D(edgeAnchors.TopLeft)
                        : Clone2D(edgeAnchors.BottomLeft);

                    if (!edgeAnchors.HasLeftNotchHoleAnchor &&
                        edgeAnchors.LeftMost != null &&
                        edgeAnchors.LeftMost.X < horizontalEdgePoint.X - edgeTol)
                    {
                        horizontalEdgePoint = Clone2D(edgeAnchors.LeftMost);
                    }
                }
                else
                {
                    horizontalEdgePoint = horizontalDimOnTop
                        ? Clone2D(edgeAnchors.TopRight)
                        : Clone2D(edgeAnchors.BottomRight);

                    if (!edgeAnchors.HasRightNotchHoleAnchor &&
                        edgeAnchors.RightMost != null &&
                        edgeAnchors.RightMost.X > horizontalEdgePoint.X + edgeTol)
                    {
                        horizontalEdgePoint = Clone2D(edgeAnchors.RightMost);
                    }
                }

                // Vẫn dùng lỗ thấp nhất của cột, nhưng khoảng hở hướng lên trên.
                Point horizontalHolePoint =
                    CreateHorizontalHoleDimFootAbove(hole, gap);

                double realEdgeHorizontalOffset = ResolveDimDistanceByAnchor4(
                    horizontalEdgePoint,
                    horizontalHolePoint,
                    horizontalDimOnTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                    offsetAnchors,
                    horizontalOffset
                );

                if (CreateDim(
                    handler,
                    view,
                    horizontalEdgePoint,
                    horizontalHolePoint,
                    horizontalDimOnTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0),
                    realEdgeHorizontalOffset))
                    count++;

                // DIM dọc:
                // Lỗ bên trái đặt bên phải, lỗ bên phải đặt bên trái.
                double footX = isLeftSide ? hole.X + gap : hole.X - gap;
                bool useBottomEdge = Math.Abs(hole.Y - verticalMinY) <= Math.Abs(verticalMaxY - hole.Y);

                Point edgePoint = FindRealContourPointOnVerticalLine(
                    verticalPolygon,
                    footX,
                    useBottomEdge,
                    verticalMinY,
                    verticalMaxY
                );

                Point holeFoot = new Point(footX, hole.Y, 0);

                Point p1;
                Point p2;

                if (isLeftSide)
                {
                    // Lỗ trái: Lỗ -> Mép, offset ra bên phải.
                    p1 = holeFoot;
                    p2 = edgePoint;
                }
                else
                {
                    // Lỗ phải: Lỗ -> Mép, offset ra bên trái.
                    p1 = holeFoot;
                    p2 = edgePoint;
                }

                bool verticalDimOnRight = isLeftSide;
                double realEdgeVerticalOffset = verticalOffset;
                if (useRealSideEdgeOffset)
                {
                    realEdgeVerticalOffset = ResolveDimDistanceByAnchor4(
                        p1,
                        p2,
                        verticalDimOnRight ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                        offsetAnchors,
                        verticalOffset
                    );
                }

                if (CreateDim(
                    handler,
                    view,
                    p1,
                    p2,
                    isLeftSide ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                    realEdgeVerticalOffset))
                {
                    count++;
                    verticalDimCreated = true;
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateTopBottomHorizontalGroupRepresentativeYDim(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            List<Point> polygon,
            double minY,
            double maxY,
            DimOffsetAnchor4 offsetAnchors,
            double verticalOffset)
        {
            // Cụm ngang: chỉ tạo 1 DIM dọc đại diện cho cả cụm.
            // Mép -> lỗ, chân lỗ hở theo phi thật.
            int count = 0;

            try
            {
                if (holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                TopBottomHoleGroup group = CreateTopBottomHoleGroup(holes, 1, tol);

                double partMinX = GetMinXFromPoints(polygon, holes[0].X);
                double partMaxX = GetMaxXFromPoints(polygon, holes[0].X);

                bool useRightSide = ShouldTopBottomVerticalDimUseRightSide(group, partMinX, partMaxX);

                // Type 1 da co center-line noi cac lo cung Y:
                // giu cach cu, chon lo gan phia dat DIM.
                Point refHole = FindNearestHoleOnRowToVerticalDim(
                    holes,
                    group.MinY,
                    tol,
                    useRightSide
                );

                if (refHole == null)
                    refHole = holes[0];

                double gap = GetClusterHoleDimGap(refHole, holes);
                if (gap <= MIN_VALID_HOLE_DIM_GAP)
                    gap = 0.0;

                double footX = useRightSide ? refHole.X + gap : refHole.X - gap;
                double distToLeft = Math.Abs(((group.MinX + group.MaxX) / 2.0) - partMinX);
                double distToRight = Math.Abs(partMaxX - ((group.MinX + group.MaxX) / 2.0));
                bool isEdgeCluster =
                    distToLeft < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE ||
                    distToRight < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;

                if (isEdgeCluster)
                {
                    Point edgeAtRow = FindRealContourPointOnHorizontalLine(
                        polygon,
                        refHole.Y,
                        !useRightSide,
                        partMinX,
                        partMaxX
                    );

                    if (edgeAtRow != null)
                    {
                        if (!useRightSide)
                        {
                            if (refHole.X - edgeAtRow.X <= gap + tol)
                                footX = edgeAtRow.X;
                        }
                        else
                        {
                            if (edgeAtRow.X - refHole.X <= gap + tol)
                                footX = edgeAtRow.X;
                        }
                    }
                }

                bool useBottomEdge = Math.Abs(refHole.Y - minY) <= Math.Abs(maxY - refHole.Y);

                Point edgePoint = FindRealContourPointOnVerticalLine(
                    polygon,
                    footX,
                    useBottomEdge,
                    minY,
                    maxY
                );

                Point holeFoot = new Point(footX, refHole.Y, 0);

                double realEdgeVerticalOffset = verticalOffset;
                if (isEdgeCluster)
                {
                    realEdgeVerticalOffset = ResolveDimDistanceByAnchor4(
                        holeFoot,
                        edgePoint,
                        useRightSide ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                        offsetAnchors,
                        verticalOffset
                    );
                }

                // Cụm ngang cùng Y: DIM dọc theo thứ tự Lỗ -> Mép.
                if (CreateDim(
                    handler,
                    view,
                    holeFoot,
                    edgePoint,
                    useRightSide ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                    realEdgeVerticalOffset))
                    count++;
            }
            catch
            {
            }

            return count;
        }

        private static void CreateTopBottomHorizontalGroupCenterLine(
            View view,
            List<Point> holes)
        {
            try
            {
                if (view == null || holes == null || holes.Count < 2)
                    return;

                Point firstHole = null;
                Point lastHole = null;

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    if (firstHole == null || hole.X < firstHole.X)
                        firstHole = hole;

                    if (lastHole == null || hole.X > lastHole.X)
                        lastHole = hole;
                }

                if (firstHole == null || lastHole == null ||
                    Distance2D(firstHole, lastHole) <= TOL)
                    return;

                Point startPoint = new Point(firstHole.X, firstHole.Y, 0);
                Point endPoint = new Point(lastHole.X, lastHole.Y, 0);

                if (HasEquivalentTopBottomHoleCenterLine(view, startPoint, endPoint))
                    return;

                TTSK_AutoDim_Plates.PHU_LineDistance.InsertLineWithLineDistanceAttributes(
                    view,
                    startPoint,
                    endPoint
                );
            }
            catch
            {
            }
        }

        private static bool HasEquivalentTopBottomHoleCenterLine(
            View view,
            Point startPoint,
            Point endPoint)
        {
            try
            {
                if (view == null || startPoint == null || endPoint == null)
                    return false;

                DrawingObjectEnumerator objects =
                    view.GetAllObjects(typeof(Tekla.Structures.Drawing.Line));

                while (objects.MoveNext())
                {
                    Tekla.Structures.Drawing.Line line =
                        objects.Current as Tekla.Structures.Drawing.Line;

                    if (line == null)
                        continue;

                    Point existingStart;
                    Point existingEnd;

                    if (!TryGetDrawingLinePoints(line, out existingStart, out existingEnd))
                        continue;

                    bool sameDirection =
                        Distance2D(existingStart, startPoint) <= TOL &&
                        Distance2D(existingEnd, endPoint) <= TOL;

                    bool reverseDirection =
                        Distance2D(existingStart, endPoint) <= TOL &&
                        Distance2D(existingEnd, startPoint) <= TOL;

                    if (sameDirection || reverseDirection)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryGetDrawingLinePoints(
            Tekla.Structures.Drawing.Line line,
            out Point startPoint,
            out Point endPoint)
        {
            startPoint = null;
            endPoint = null;

            if (line == null)
                return false;

            string[] startNames =
                new string[] { "StartPoint", "Start", "Point1", "FirstPoint", "P1" };
            string[] endNames =
                new string[] { "EndPoint", "End", "Point2", "SecondPoint", "P2" };

            for (int i = 0; i < startNames.Length; i++)
            {
                startPoint = TryGetPointProperty(line, startNames[i]);
                endPoint = TryGetPointProperty(line, endNames[i]);

                if (startPoint != null && endPoint != null)
                    return true;
            }

            return false;
        }

        private static int CreateTopBottomGroupYFullChainRepresentative(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            List<Point> polygon,
            double minY,
            double maxY,
            DimOffsetAnchor4 offsetAnchors,
            double verticalOffset)
        {
            // Cụm dọc / cụm 2 chiều:
            // Chỉ tạo 1 chain dọc đại diện cho cụm.
            // Hai cụm trái/phải gọi hàm này riêng từng cụm, không gộp DIM dọc.
            int count = 0;

            try
            {
                if (holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                TopBottomHoleGroup group = CreateTopBottomHoleGroup(holes, 3, tol);
                List<Point> rows = BuildUniqueHoleRowsForChain(holes, tol);

                if (rows.Count == 0)
                    return count;

                rows.Sort(delegate (Point a, Point b)
                {
                    return a.Y.CompareTo(b.Y);
                });

                double partMinX = GetMinXFromPoints(polygon, holes[0].X);
                double partMaxX = GetMaxXFromPoints(polygon, holes[0].X);

                bool useRightSide = ShouldTopBottomVerticalDimUseRightSide(group, partMinX, partMaxX);
                bool isEdgeCluster =
                    Math.Abs(((group.MinX + group.MaxX) / 2.0) - partMinX) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE ||
                    Math.Abs(partMaxX - ((group.MinX + group.MaxX) / 2.0)) < TOP_BOTTOM_HOLE_EDGE_PRIORITY_DISTANCE;

                PointList pts = new PointList();
                List<Point> rowFeet = new List<Point>();

                foreach (Point r in rows)
                {
                    if (r == null)
                        continue;

                    // DIM phải lấy lỗ trái nhất; DIM trái/giữa lấy lỗ phải nhất.
                    Point refHole = FindFarthestHoleOnRowFromVerticalDim(
                        holes,
                        r.Y,
                        tol,
                        useRightSide
                    );

                    if (refHole == null)
                        refHole = r;

                    double gap = GetClusterHoleDimGap(refHole, holes);
                    if (gap <= MIN_VALID_HOLE_DIM_GAP)
                        gap = 0.0;

                    double footX = useRightSide ? refHole.X + gap : refHole.X - gap;

                    if (isEdgeCluster)
                    {
                        Point edgeAtRow = FindRealContourPointOnHorizontalLine(
                            polygon,
                            refHole.Y,
                            !useRightSide,
                            partMinX,
                            partMaxX
                        );

                        if (edgeAtRow != null)
                        {
                            if (!useRightSide)
                            {
                                if (refHole.X - edgeAtRow.X <= gap + tol)
                                    footX = edgeAtRow.X;
                            }
                            else
                            {
                                if (edgeAtRow.X - refHole.X <= gap + tol)
                                    footX = edgeAtRow.X;
                            }
                        }
                    }

                    rowFeet.Add(new Point(footX, refHole.Y, 0));
                }

                if (rowFeet.Count == 0)
                    return count;

                Point bottomEdge;
                Point topEdge;

                if (isEdgeCluster)
                {
                    bottomEdge = FindRealContourPointOnHorizontalLine(
                        polygon,
                        minY,
                        !useRightSide,
                        partMinX,
                        partMaxX
                    );

                    topEdge = FindRealContourPointOnHorizontalLine(
                        polygon,
                        maxY,
                        !useRightSide,
                        partMinX,
                        partMaxX
                    );

                    double fallbackEdgeX = useRightSide ? partMaxX : partMinX;

                    if (bottomEdge == null)
                        bottomEdge = new Point(fallbackEdgeX, minY, 0);
                    else
                        bottomEdge = Clone2D(bottomEdge);

                    if (topEdge == null)
                        topEdge = new Point(fallbackEdgeX, maxY, 0);
                    else
                        topEdge = Clone2D(topEdge);
                }
                else
                {
                    // Cụm giữa dầm: offset 200 về bên trái.
                    double middleDimFootX = rowFeet[0].X;
                    bottomEdge = new Point(middleDimFootX, minY, 0);
                    topEdge = new Point(middleDimFootX, maxY, 0);
                    useRightSide = false;
                }

                pts.Add(Clone2D(bottomEdge));

                foreach (Point rf in rowFeet)
                {
                    if (rf != null)
                        pts.Add(Clone2D(rf));
                }

                pts.Add(Clone2D(topEdge));

                double realEdgeVerticalOffset = verticalOffset;
                if (isEdgeCluster)
                {
                    realEdgeVerticalOffset = ResolveDimDistanceByAnchor4(
                        pts,
                        useRightSide ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                        offsetAnchors,
                        verticalOffset
                    );
                }
                else
                {
                    realEdgeVerticalOffset =
                        GetMiddleVerticalDimOffsetCoveringCluster(
                            holes,
                            bottomEdge.X,
                            verticalOffset
                        );
                }

                if (handler.CreateDimensionSet(
                    view,
                    pts,
                    useRightSide ? new Vector(1, 0, 0) : new Vector(-1, 0, 0),
                    realEdgeVerticalOffset) != null)
                {
                    count++;
                }
            }
            catch
            {
            }

            return count;
        }


        private static bool HasTopViewRectangularHoleClusterOnSide(
            List<Point> holes,
            double minX,
            double maxX,
            bool leftSide)
        {
            // TOP/BOTTOM VIEW - TẦNG DIM TỔNG DỌC:
            // Hàm tên cũ giữ lại để không phải đổi chỗ gọi.
            // Logic mới không còn xét phi/cụm chữ nhật theo phi.
            // Chỉ trả true khi có DIM dọc lỗ/cụm đặt về phía trái hoặc nằm giữa dầm,
            // để DIM tổng dọc bên trái tự nhảy tầng, tránh chồng.
            try
            {
                if (holes == null || holes.Count == 0)
                    return false;

                List<TopBottomHoleGroup> groups = BuildTopBottomHoleGroupsByGeometry(holes, minX, maxX);

                foreach (TopBottomHoleGroup g in groups)
                {
                    if (g == null)
                        continue;

                    // Lỗ đơn trái đặt DIM dọc sang phải, lỗ đơn phải đặt sang trái gần phía phải,
                    // nên không xem là chiếm tầng tổng dọc bên trái toàn dầm.
                    if (g.Type == 0)
                        continue;

                    bool useRightSide = ShouldTopBottomVerticalDimUseRightSide(g, minX, maxX);

                    if (!useRightSide)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void AddUniqueCoordinate(List<double> values, double value, double tol)
        {
            try
            {
                if (values == null)
                    return;

                foreach (double v in values)
                {
                    if (Math.Abs(v - value) <= tol)
                        return;
                }

                values.Add(value);
            }
            catch
            {
            }
        }
        private static int CreateTopBottomClusterHoleXFullChainOnSide(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            bool useTopSide,
            double horizontalOffset)
        {
            if (useTopSide)
            {
                return CreateTopBottomClusterHoleXFullChain(
                    handler,
                    view,
                    holes,
                    edgeAnchors,
                    offsetAnchors,
                    horizontalOffset
                );
            }

            // CỤM LỖ TOP/BOTTOM GẦN MÉP DƯỚI - DIM NGANG CHAIN XUỐNG DƯỚI:
            // Mép trái -> các cột lỗ -> mép phải.
            // Chân DIM lỗ cách tâm đúng bằng phi lỗ, hoặc bắt thẳng mép nếu lỗ sát mép dưới.
            int count = 0;

            try
            {
                if (holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                List<Point> columns = BuildUniqueHoleColumnsForChain(holes, tol);
                if (columns.Count == 0)
                    return count;

                columns.Sort(delegate (Point a, Point b)
                {
                    return a.X.CompareTo(b.X);
                });

                double bottomEdgeY = edgeAnchors.BottomMost != null
                    ? edgeAnchors.BottomMost.Y
                    : edgeAnchors.BottomLeft.Y;

                PointList pts = new PointList();
                pts.Add(Clone2D(edgeAnchors.BottomLeft));

                foreach (Point c in columns)
                {
                    if (c == null)
                        continue;

                    // DIM ngang đặt xuống dưới: dùng lỗ thấp nhất trong cùng cột để tạo chân DIM.
                    Point refHole = FindBottommostHoleInColumn(holes, c.X, tol);
                    if (refHole == null)
                        refHole = c;

                    double gap = GetClusterHoleDimGap(refHole, holes);
                    double footY = CreateHorizontalHoleDimFootBelow(refHole, gap).Y;

                    if (refHole.Y - bottomEdgeY <= gap + tol)
                        footY = bottomEdgeY;

                    pts.Add(new Point(refHole.X, footY, 0));
                }

                pts.Add(Clone2D(edgeAnchors.BottomRight));

                double realBottomOffset = ResolveDimDistanceByAnchor4(
                    pts,
                    new Vector(0, -1, 0),
                    offsetAnchors,
                    horizontalOffset
                );

                if (handler.CreateDimensionSet(
                    view,
                    pts,
                    new Vector(0, -1, 0),
                    realBottomOffset) != null)
                {
                    count++;
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateTopBottomClusterHoleXFullChain(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double horizontalOffset)
        {
            // CỤM LỖ TOP/BOTTOM - DIM NGANG CHAIN DUY NHẤT:
            // Mép trái -> các cột lỗ -> mép phải.
            // Chân DIM lỗ cách tâm đúng bằng phi lỗ.
            // Nếu lỗ nằm sát mép dầm thì chân DIM bắt thẳng ra mép thật, không tạo điểm hở giả.
            int count = 0;

            try
            {
                if (holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                List<Point> columns = BuildUniqueHoleColumnsForChain(holes, tol);
                if (columns.Count == 0)
                    return count;

                columns.Sort(delegate (Point a, Point b)
                {
                    return a.X.CompareTo(b.X);
                });

                double topEdgeY = edgeAnchors.TopMost != null
                    ? edgeAnchors.TopMost.Y
                    : edgeAnchors.TopLeft.Y;

                Point firstDimPoint = edgeAnchors.HasLeftNotchHoleAnchor
                    ? edgeAnchors.TopLeft
                    : edgeAnchors.LeftMost;

                PointList pts = new PointList();
                pts.Add(Clone2D(firstDimPoint));

                foreach (Point c in columns)
                {
                    if (c == null)
                        continue;

                    // Đường DIM vẫn ở phía trên, chân DIM dùng lỗ thấp nhất
                    // và khoảng hở hướng lên trên.
                    Point refHole = FindBottommostHoleInColumn(holes, c.X, tol);
                    if (refHole == null)
                        refHole = c;

                    double gap = GetClusterHoleDimGap(refHole, holes);

                    double footY = CreateHorizontalHoleDimFootAbove(refHole, gap).Y;

                    // Nếu khoảng hở vượt mép trên, chân DIM bắt vào mép thật.
                    if (topEdgeY - refHole.Y <= gap + tol)
                        footY = topEdgeY;

                    pts.Add(new Point(refHole.X, footY, 0));
                }

                pts.Add(Clone2D(
                    edgeAnchors.HasRightNotchHoleAnchor
                        ? edgeAnchors.TopRight
                        : edgeAnchors.RightMost));

                double realUpperOffset = ResolveDimDistanceByAnchor4(
                    pts,
                    new Vector(0, 1, 0),
                    offsetAnchors,
                    horizontalOffset
                );

                if (handler.CreateDimensionSet(
                    view,
                    pts,
                    new Vector(0, 1, 0),
                    realUpperOffset) != null)
                {
                    count++;
                }
            }
            catch
            {
            }

            return count;
        }

        private static Point FindTopmostHoleInColumn(List<Point> holes, double x, double tol)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.X - x) > tol)
                        continue;

                    if (best == null || h.Y > best.Y)
                        best = h;
                }
            }
            catch
            {
            }

            return best;
        }


        private static Point FindBottommostHoleInColumn(List<Point> holes, double x, double tol)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.X - x) > tol)
                        continue;

                    if (best == null || h.Y < best.Y)
                        best = h;
                }
            }
            catch
            {
            }

            return best;
        }

        private static Point CreateHorizontalHoleDimFootAbove(
            Point hole,
            double gap)
        {
            if (hole == null)
                return null;

            double safeGap = gap > MIN_VALID_HOLE_DIM_GAP ? gap : 0.0;
            return new Point(hole.X, hole.Y + safeGap, 0);
        }

        private static Point CreateHorizontalHoleDimFootBelow(
            Point hole,
            double gap)
        {
            if (hole == null)
                return null;

            double safeGap = gap > MIN_VALID_HOLE_DIM_GAP ? gap : 0.0;
            return new Point(hole.X, hole.Y - safeGap, 0);
        }

        private static Point FindLeftmostHoleOnRow(List<Point> holes, double y, double tol)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.Y - y) > tol)
                        continue;

                    if (best == null || h.X < best.X)
                        best = h;
                }
            }
            catch
            {
            }

            return best;
        }

        private static Point FindRightmostHoleOnRow(List<Point> holes, double y, double tol)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    if (Math.Abs(h.Y - y) > tol)
                        continue;

                    if (best == null || h.X > best.X)
                        best = h;
                }
            }
            catch
            {
            }

            return best;
        }

        private static Point FindFarthestHoleOnRowFromVerticalDim(
            List<Point> holes,
            double y,
            double tol,
            bool dimOnRight)
        {
            // DIM bên phải chọn lỗ trái nhất; DIM bên trái/giữa chọn lỗ phải nhất.
            return dimOnRight
                ? FindLeftmostHoleOnRow(holes, y, tol)
                : FindRightmostHoleOnRow(holes, y, tol);
        }

        private static Point FindNearestHoleOnRowToVerticalDim(
            List<Point> holes,
            double y,
            double tol,
            bool dimOnRight)
        {
            // Chi dung cho Type 1 cung Y: phuc hoi dung cach chon cu.
            return dimOnRight
                ? FindRightmostHoleOnRow(holes, y, tol)
                : FindLeftmostHoleOnRow(holes, y, tol);
        }

        private static double GetMiddleVerticalDimOffsetCoveringCluster(
            List<Point> holes,
            double innerDimFootX,
            double currentTierOffset)
        {
            try
            {
                if (holes == null || holes.Count == 0 ||
                    double.IsNaN(innerDimFootX) ||
                    double.IsInfinity(innerDimFootX))
                    return currentTierOffset;

                double leftOuterFootX = 999999999.0;

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    double gap = GetHoleDimGap(hole);
                    if (gap <= MIN_VALID_HOLE_DIM_GAP)
                        gap = 0.0;

                    double outerFootX = hole.X - gap;
                    if (outerFootX < leftOuterFootX)
                        leftOuterFootX = outerFootX;
                }

                if (leftOuterFootX > 900000000.0)
                    return currentTierOffset;

                double coveredWidth = innerDimFootX - leftOuterFootX;
                if (coveredWidth <= 0.0)
                    return currentTierOffset;

                // Đường DIM nằm ngoài cột trái nhất đúng bằng tier hiện tại.
                return currentTierOffset + coveredWidth;
            }
            catch
            {
                return currentTierOffset;
            }
        }

        private static double GetMinXFromPoints(List<Point> pts, double fallback)
        {
            try
            {
                if (pts == null || pts.Count == 0)
                    return fallback;

                double v = 999999999.0;
                bool found = false;
                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;
                    if (p.X < v) v = p.X;
                    found = true;
                }

                if (found)
                    return v;
            }
            catch
            {
            }

            return fallback;
        }

        private static double GetMaxXFromPoints(List<Point> pts, double fallback)
        {
            try
            {
                if (pts == null || pts.Count == 0)
                    return fallback;

                double v = -999999999.0;
                bool found = false;
                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;
                    if (p.X > v) v = p.X;
                    found = true;
                }

                if (found)
                    return v;
            }
            catch
            {
            }

            return fallback;
        }

        private static Point FindRealContourPointOnHorizontalLine(
            List<Point> polygon,
            double y,
            bool takeLeft,
            double minX,
            double maxX)
        {
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return new Point(takeLeft ? minX : maxX, y, 0);

                Point hit = FindHorizontalContourIntersectionInPointOrder(
                    polygon,
                    y,
                    takeLeft,
                    minX,
                    maxX
                );

                if (hit != null)
                    return hit;

                List<Point> sorted = SortPolygonPointsClockwise(polygon);
                hit = FindHorizontalContourIntersectionInPointOrder(
                    sorted,
                    y,
                    takeLeft,
                    minX,
                    maxX
                );

                if (hit != null)
                    return hit;
            }
            catch
            {
            }

            return new Point(takeLeft ? minX : maxX, y, 0);
        }

        private static Point FindHorizontalContourIntersectionInPointOrder(
            List<Point> pts,
            double y,
            bool takeLeft,
            double minX,
            double maxX)
        {
            try
            {
                if (pts == null || pts.Count < 2)
                    return null;

                double bestX = takeLeft ? 999999999.0 : -999999999.0;
                bool found = false;
                double tol = Math.Max(1.0, TOL);

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    if (Math.Abs(a.Y - b.Y) <= tol)
                    {
                        if (Math.Abs(y - a.Y) > tol)
                            continue;

                        double x1 = Math.Min(a.X, b.X);
                        double x2 = Math.Max(a.X, b.X);

                        if (takeLeft)
                        {
                            if (x1 < bestX)
                            {
                                bestX = x1;
                                found = true;
                            }
                        }
                        else
                        {
                            if (x2 > bestX)
                            {
                                bestX = x2;
                                found = true;
                            }
                        }

                        continue;
                    }

                    double minSegY = Math.Min(a.Y, b.Y) - tol;
                    double maxSegY = Math.Max(a.Y, b.Y) + tol;
                    if (y < minSegY || y > maxSegY)
                        continue;

                    double t = (y - a.Y) / (b.Y - a.Y);
                    if (t < -0.01 || t > 1.01)
                        continue;

                    double x = a.X + t * (b.X - a.X);
                    if (x < minX - tol || x > maxX + tol)
                        continue;

                    if (takeLeft)
                    {
                        if (x < bestX)
                        {
                            bestX = x;
                            found = true;
                        }
                    }
                    else
                    {
                        if (x > bestX)
                        {
                            bestX = x;
                            found = true;
                        }
                    }
                }

                if (found)
                    return new Point(bestX, y, 0);
            }
            catch
            {
            }

            return null;
        }

        private static List<Point> BuildUniqueHoleColumnsForChain(List<Point> holes, double tol)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (holes == null)
                    return result;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    bool merged = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        Point c = result[i];
                        if (c == null)
                            continue;

                        if (Math.Abs(c.X - h.X) <= tol)
                        {
                            // Giữ X theo trung bình cột, Y theo trung bình để Tekla có điểm ổn định.
                            double newX = (c.X + h.X) / 2.0;
                            double newY = (c.Y + h.Y) / 2.0;
                            double newZ = c.Z;
                            double hz = GetHoleDimGap(h);
                            double cz = GetHoleDimGap(c);
                            if (hz > cz)
                                newZ = h.Z;

                            result[i] = new Point(newX, newY, newZ);
                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                        result.Add(Clone2DWithDiameter(h));
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<Point> BuildUniqueHoleRowsForChain(List<Point> holes, double tol)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (holes == null)
                    return result;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    bool merged = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        Point r = result[i];
                        if (r == null)
                            continue;

                        if (Math.Abs(r.Y - h.Y) <= tol)
                        {
                            // Ưu tiên X trái nhất vì DIM dọc offset về trái.
                            double newX = Math.Min(r.X, h.X);
                            double newY = (r.Y + h.Y) / 2.0;
                            double newZ = r.Z;
                            double hz = GetHoleDimGap(h);
                            double rz = GetHoleDimGap(r);
                            if (hz > rz)
                                newZ = h.Z;

                            result[i] = new Point(newX, newY, newZ);
                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                        result.Add(Clone2DWithDiameter(h));
                }
            }
            catch
            {
            }

            return result;
        }


        private static Dictionary<double, List<Point>> GroupTopViewHolesByDiameter(List<Point> holes)
        {
            Dictionary<double, List<Point>> result = new Dictionary<double, List<Point>>();

            try
            {
                if (holes == null)
                    return result;

                foreach (Point h in holes)
                {
                    if (h == null)
                        continue;

                    double key = GetHoleDiameterKey(h);

                    if (!result.ContainsKey(key))
                        result[key] = new List<Point>();

                    result[key].Add(Clone2DWithDiameter(h));
                }
            }
            catch
            {
            }

            return result;
        }

        private static double GetHoleDiameterKey(Point h)
        {
            double d = GetHoleDimGap(h);

            if (d <= MIN_VALID_HOLE_DIM_GAP)
                return 0.0;

            // Làm tròn theo mm để phi thật như 22.0 / 22.1 không bị tách nhầm cụm.
            // Chỉ dùng cho các hàm cũ nếu còn được gọi; thuật toán Top/Bottom mới không chọn rule theo phi.
            return Math.Round(d, 0);
        }

        private static Point Clone2DWithDiameter(Point p)
        {
            if (p == null)
                return new Point(0, 0, 0);

            return new Point(p.X, p.Y, p.Z);
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
        private static List<Point> GetVisibleTopFlangeBoltCentersFromView(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            List<HHoleCandidate> holes = GetHHoleCandidatesInCurrentPlane(
                model,
                view,
                minX,
                maxX,
                minY,
                maxY,
                true
            );

            HHoleClassification classified = ClassifyHHoleCandidates(holes);
            return ConvertHHoleCandidatesToDimPoints(classified.TopCandidates);
        }

        private static List<Point> GetVisibleBottomFlangeBoltCentersFromView(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            List<HHoleCandidate> holes = GetHHoleCandidatesInCurrentPlane(
                model,
                view,
                minX,
                maxX,
                minY,
                maxY,
                true
            );

            HHoleClassification classified = ClassifyHHoleCandidates(holes);
            return ConvertHHoleCandidatesToDimPoints(classified.BottomCandidates);
        }

        private static double GetHoleDimGap(Point h)
        {
            try
            {
                // Point.Z đang được dùng để mang theo phi lỗ thật đã đọc được.
                // Top/Bottom không còn sentinel, không còn kích thước lỗ đặc biệt.
                if (h != null && h.Z > MIN_VALID_HOLE_DIM_GAP && h.Z < 200.0)
                    return h.Z;
            }
            catch
            {
            }

            // Không đọc được phi thì trả 0, tuyệt đối không fallback bằng số cố định.
            return 0.0;
        }
        private static double GetClusterHoleDimGap(Point h, List<Point> holes)
        {
            // Dùng riêng cho DIM chain cụm lỗ TOP/BOTTOM.
            // Không fallback về hằng số cố định nữa.
            // Ưu tiên phi của chính lỗ; nếu point đó không mang phi thì lấy phi từ các lỗ cùng cụm.
            double gap = GetHoleDimGap(h);
            if (gap > MIN_VALID_HOLE_DIM_GAP)
                return gap;

            try
            {
                if (holes != null)
                {
                    foreach (Point p in holes)
                    {
                        double g = GetHoleDimGap(p);
                        if (g > MIN_VALID_HOLE_DIM_GAP)
                            return g;
                    }
                }
            }
            catch
            {
            }

            // Không đọc được phi thì không tự đẩy bằng số cố định để tránh tạo sai chân DIM.
            return 0.0;
        }


        private static double GetTopBottomRealHoleDiameterFromBoltGroup(ModelBoltGroup bg)
        {
            try
            {
                // TOP/BOTTOM ONLY:
                // Gap chân DIM phải lấy theo PHI LỖ THẬT.
                // Không dùng rule M20, không sentinel, không gán cứng 22.
                // Nếu Tekla không trả thẳng HOLE_DIAMETER thì tính theo dữ liệu thật của bolt group:
                //      phi lỗ = bolt size + tolerance/clearance đọc từ model/report.

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

                // Nếu không có sẵn phi lỗ, đọc bolt size + tolerance THẬT từ model.
                // Đây không phải rule M20 hay hằng số 22; M16/F8T nếu model khai tolerance 2 thì ra 18,
                // M20 nếu model khai tolerance 2 thì ra 22, các size khác tự ra đúng theo dữ liệu model.
                double boltSize = ReadFirstValidDouble(
                    bg,
                    new string[] { "BOLT_SIZE", "BOLT.DIAMETER", "DIAMETER_BOLT" },
                    new string[] { "BoltSize", "Size", "Diameter" }
                );

                double tolerance = ReadFirstValidDouble(
                    bg,
                    new string[]
                    {
                        "HOLE_TOLERANCE",
                        "BOLT_HOLE_TOLERANCE",
                        "BOLT_TOLERANCE",
                        "TOLERANCE",
                        "CLEARANCE",
                        "HOLE_CLEARANCE"
                    },
                    new string[]
                    {
                        "HoleTolerance",
                        "BoltHoleTolerance",
                        "Tolerance",
                        "Clearance"
                    }
                );

                if (boltSize > MIN_VALID_HOLE_DIM_GAP && boltSize < 200.0 &&
                    tolerance > 0.0 && tolerance < 50.0)
                    return boltSize + tolerance;

                // Cuối cùng, nếu chỉ đọc được bolt size thì dùng bolt size để còn có gap theo dữ liệu model,
                // tuyệt đối không tự gán 18/22/M20. Trường hợp này hiếm và vẫn tốt hơn ghim vào tâm lỗ.
                if (boltSize > MIN_VALID_HOLE_DIM_GAP && boltSize < 200.0)
                    return boltSize;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double ReadFirstValidDouble(
            ModelBoltGroup bg,
            string[] reportNames,
            string[] propertyNames)
        {
            try
            {
                if (bg == null)
                    return 0.0;

                if (reportNames != null)
                {
                    foreach (string name in reportNames)
                    {
                        double v = GetReportDouble(bg, name);
                        if (v > 0.0 && v < 500.0)
                            return v;
                    }
                }

                if (propertyNames != null)
                {
                    foreach (string name in propertyNames)
                    {
                        double v = GetDoublePropertyByReflection(bg, name);
                        if (v > 0.0 && v < 500.0)
                            return v;
                    }
                }
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
                // Ưu tiên đọc đúng đường kính lỗ.
                // Tekla/môi trường có thể dùng tên report khác nhau nên thử nhiều tên.
                string[] holeNames = new string[]
                {
                    "HOLE_DIAMETER",
                    "BOLT_HOLE_DIAMETER",
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

                // Thử đọc bằng reflection nếu report không trả về.
                string[] propNames = new string[]
                {
                    "HoleDiameter",
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

                // Nếu chỉ đọc được bolt size thì cộng clearance phổ biến 2mm.
                // Ví dụ M16 -> Ø18, M20 -> Ø22.
                double boltSize = GetReportDouble(bg, "BOLT_SIZE");
                if (boltSize > MIN_VALID_HOLE_DIM_GAP && boltSize < 200.0)
                    return boltSize + 2.0;

                double boltSizeByProp = GetDoublePropertyByReflection(bg, "BoltSize");
                if (boltSizeByProp > MIN_VALID_HOLE_DIM_GAP && boltSizeByProp < 200.0)
                    return boltSizeByProp + 2.0;
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
            double distance,
            string attributeName = null)
        {
            if (p1 == null || p2 == null)
                return false;

            if (Distance2D(p1, p2) < 1.0)
                return false;

            PointList list = new PointList();
            list.Add(new Point(p1.X, p1.Y, 0));
            list.Add(new Point(p2.X, p2.Y, 0));

            StraightDimensionSet dim = null;

            if (!string.IsNullOrEmpty(attributeName))
                dim = TryCreateDimensionSetWithAttributes(handler, view, list, direction, distance, attributeName);

            if (dim == null)
                dim = handler.CreateDimensionSet(view, list, direction, distance);

            if (dim != null && !string.IsNullOrEmpty(attributeName))
                TryApplyStraightDimAttributes(dim, attributeName);

            return dim != null;
        }

        private static StraightDimensionSet TryCreateDimensionSetWithAttributes(
            StraightDimensionSetHandler handler,
            View view,
            PointList list,
            Vector direction,
            double distance,
            string attributeName)
        {
            try
            {
                if (handler == null || view == null || list == null || string.IsNullOrEmpty(attributeName))
                    return null;

                MethodInfo[] methods = handler.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "CreateDimensionSet")
                        continue;

                    ParameterInfo[] ps = method.GetParameters();
                    if (ps == null || ps.Length != 5)
                        continue;

                    Type attrType = ps[4].ParameterType;
                    if (attrType == null)
                        continue;

                    object attr = null;

                    try
                    {
                        ConstructorInfo ctor = attrType.GetConstructor(Type.EmptyTypes);
                        if (ctor == null)
                            continue;

                        attr = ctor.Invoke(null);
                    }
                    catch
                    {
                        continue;
                    }

                    TryLoadAttributesObject(attr, attributeName);

                    object result = method.Invoke(
                        handler,
                        new object[]
                        {
                            view,
                            list,
                            direction,
                            distance,
                            attr
                        });

                    StraightDimensionSet dim = result as StraightDimensionSet;
                    if (dim != null)
                        return dim;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void TryApplyStraightDimAttributes(
            StraightDimensionSet dim,
            string attributeName)
        {
            try
            {
                if (dim == null || string.IsNullOrEmpty(attributeName))
                    return;

                object attr = dim.Attributes;
                if (attr == null)
                    return;

                TryLoadAttributesObject(attr, attributeName);
                dim.Modify();
            }
            catch
            {
            }
        }

        private static void TryLoadAttributesObject(
            object attr,
            string attributeName)
        {
            try
            {
                if (attr == null || string.IsNullOrEmpty(attributeName))
                    return;

                MethodInfo loadMethod = attr.GetType().GetMethod(
                    "LoadAttributes",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (loadMethod == null)
                    return;

                loadMethod.Invoke(attr, new object[] { attributeName });
            }
            catch
            {
            }
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

        private static View FindViewByViewTypeForH(
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
                    if (ViewTypeMatchesForH(view, exactViewTypeName, fallbackText))
                        return view;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ViewTypeMatchesForH(
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

                // Fallback mềm cho môi trường Tekla trả chuỗi ViewType hơi khác,
                // nhưng vẫn chỉ đọc ViewType, không quay lại đoán theo thứ tự sort.
                if (!string.IsNullOrEmpty(fallbackText) &&
                    text.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static void ClassifySectionViewsForH(
            List<View> views,
            View frontView,
            View topViewByType,
            View bottomViewByType,
            List<View> specialTopSections,
            List<View> specialBottomSections,
            List<View> exactSectionViews)
        {
            try
            {
                if (views == null)
                    return;

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    if (!ViewTypeMatchesForH(view, "SectionView", "Section"))
                        continue;

                    // Nếu view này trùng Top/Bottom chuẩn thì không xử lý như Section thường.
                    if (IsSameViewForH(view, topViewByType) || IsSameViewForH(view, bottomViewByType))
                        continue;

                    bool isSpecial = false;

                    if (frontView != null && IsSectionWidthCloseToFrontForH(view, frontView))
                    {
                        if (view.Origin.Y > frontView.Origin.Y)
                        {
                            AddUniqueViewForH(specialTopSections, view);
                            isSpecial = true;
                        }
                        else if (view.Origin.Y < frontView.Origin.Y)
                        {
                            AddUniqueViewForH(specialBottomSections, view);
                            isSpecial = true;
                        }
                    }

                    if (!isSpecial)
                        AddUniqueViewForH(exactSectionViews, view);
                }
            }
            catch
            {
            }
        }

        private static bool IsSectionWidthCloseToFrontForH(View sectionView, View frontView)
        {
            try
            {
                double sectionWidth = GetViewRestrictionBoxWidthForH(sectionView);
                double frontWidth = GetViewRestrictionBoxWidthForH(frontView);

                if (sectionWidth <= 0.0 || frontWidth <= 0.0)
                    return false;

                double tolerance = Math.Max(10.0, frontWidth * 0.10);
                return Math.Abs(sectionWidth - frontWidth) <= tolerance;
            }
            catch
            {
                return false;
            }
        }

        private static void RecoverAutoSectionDimViewsForH(
            bool singlePartLayout,
            List<View> views,
            View frontView,
            View topViewByType,
            View bottomViewByType,
            List<View> specialTopSections,
            List<View> specialBottomSections,
            List<View> exactSectionViews)
        {
            try
            {
                if (views == null || frontView == null)
                    return;

                if (singlePartLayout &&
                    topViewByType == null &&
                    specialTopSections != null &&
                    specialTopSections.Count == 0)
                {
                    View topSection = FindAutoSectionCandidateForH(
                        views,
                        frontView,
                        true);

                    if (topSection != null)
                    {
                        AddUniqueViewForH(specialTopSections, topSection);
                        RemoveViewForH(exactSectionViews, topSection);
                    }
                }

                if (bottomViewByType == null &&
                    specialBottomSections != null &&
                    specialBottomSections.Count == 0)
                {
                    View bottomSection = FindAutoSectionCandidateForH(
                        views,
                        frontView,
                        false);

                    if (bottomSection != null)
                    {
                        AddUniqueViewForH(specialBottomSections, bottomSection);
                        RemoveViewForH(exactSectionViews, bottomSection);
                    }
                }
            }
            catch
            {
            }
        }

        private static View FindAutoSectionCandidateForH(
            List<View> views,
            View frontView,
            bool aboveFront)
        {
            View bestView = null;
            double bestWidthDifference = double.MaxValue;
            double bestVerticalDistance = double.MaxValue;

            try
            {
                if (views == null || frontView == null)
                    return null;

                double frontY = frontView.Origin.Y;
                double frontWidth = GetViewRestrictionBoxWidthForH(frontView);

                foreach (View view in views)
                {
                    if (view == null ||
                        !ViewTypeMatchesForH(view, "SectionView", "Section"))
                        continue;

                    double deltaY = view.Origin.Y - frontY;
                    if ((aboveFront && deltaY <= 0.5) ||
                        (!aboveFront && deltaY >= -0.5))
                        continue;

                    double verticalDistance = Math.Abs(deltaY);
                    double sectionWidth = GetViewRestrictionBoxWidthForH(view);
                    double widthDifference =
                        frontWidth > 0.0 && sectionWidth > 0.0
                            ? Math.Abs(sectionWidth - frontWidth)
                            : 1.0e100;

                    if (bestView == null ||
                        widthDifference < bestWidthDifference - 0.01 ||
                        (Math.Abs(widthDifference - bestWidthDifference) <= 0.01 &&
                         verticalDistance < bestVerticalDistance))
                    {
                        bestView = view;
                        bestWidthDifference = widthDifference;
                        bestVerticalDistance = verticalDistance;
                    }
                }
            }
            catch
            {
                return null;
            }

            return bestView;
        }

        private static void RemoveViewForH(List<View> views, View view)
        {
            if (views == null || view == null)
                return;

            for (int i = views.Count - 1; i >= 0; i--)
            {
                if (IsSameViewForH(views[i], view))
                    views.RemoveAt(i);
            }
        }

        private static double GetViewRestrictionBoxWidthForH(View view)
        {
            try
            {
                if (view == null || view.RestrictionBox == null)
                    return 0.0;

                AABB box = view.RestrictionBox;
                if (box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                return Math.Abs(box.MaxPoint.X - box.MinPoint.X);
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<View> BuildDimViewsByViewTypeForH(
            View topView,
            View frontView,
            View bottomViewByType,
            List<View> specialBottomSections)
        {
            List<View> result = new List<View>();

            try
            {
                AddUniqueViewForH(result, topView);
                AddUniqueViewForH(result, frontView);

                // Nếu có BottomView chuẩn thì dùng đúng BottomView chuẩn.
                // Nếu không có BottomView chuẩn, các Section đặc biệt nằm dưới Front sẽ được dim như Bottom.
                if (bottomViewByType != null)
                {
                    AddUniqueViewForH(result, bottomViewByType);
                }
                else if (specialBottomSections != null)
                {
                    foreach (View view in specialBottomSections)
                        AddUniqueViewForH(result, view);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool IsSameViewForH(View a, View b)
        {
            if (a == null || b == null)
                return false;

            return System.Object.ReferenceEquals(a, b);
        }

        private static void AddUniqueViewForH(List<View> list, View view)
        {
            if (list == null || view == null)
                return;

            foreach (View existing in list)
            {
                if (IsSameViewForH(existing, view))
                    return;
            }

            list.Add(view);
        }

        private static View FindSmallestViewByRestrictionBox(List<View> views)
        {
            View bestView = null;
            double bestArea = 0.0;

            try
            {
                if (views == null || views.Count == 0)
                    return null;

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    double area = GetViewRestrictionBoxArea(view);
                    if (area <= 0.0)
                        continue;

                    if (bestView == null || area < bestArea)
                    {
                        bestView = view;
                        bestArea = area;
                    }
                }
            }
            catch
            {
            }

            return bestView;
        }

        private static double GetViewRestrictionBoxArea(View view)
        {
            try
            {
                if (view == null || view.RestrictionBox == null)
                    return 0.0;

                AABB box = view.RestrictionBox;
                if (box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                double width = Math.Abs(box.MaxPoint.X - box.MinPoint.X);
                double height = Math.Abs(box.MaxPoint.Y - box.MinPoint.Y);

                if (width <= 0.0 || height <= 0.0)
                    return 0.0;

                return width * height;
            }
            catch
            {
                return 0.0;
            }
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
        // AUTO SCALE THEO KHỔ GIẤY + CHIỀU DÀI DẦM - CHẠY TRƯỚC KHI DIM
        // -------------------------------------------------------------------------------------
        // Công thức:
        // - Lấy chiều dài dầm theo X của view tham chiếu.
        // - Chiều dài cần chứa = chiều dài dầm + AUTO_SCALE_DIM_RESERVE.
        // - Vùng giấy hữu dụng = cạnh dài khổ giấy - margin.
        // - A3 dùng margin tổng 20mm.
        // - A1 và các khổ giấy khác dùng margin tổng 30mm.
        // - Chọn scale nhỏ nhất trong {5,10,15,20,30} sao cho không vượt giấy.
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

                double beamLength = GetBeamLengthInView(model, part, referenceView);
                if (beamLength <= 1.0)
                    return;

                scale = GetAutoViewScaleByPartLength(beamLength, sheetWidth, sheetHeight);
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

        private static double GetAutoViewScaleByPartLength(
            double beamLength,
            double sheetWidth,
            double sheetHeight)
        {
            double paperLength = Math.Max(sheetWidth, sheetHeight);
            double margin = GetScaleMarginBySheetSize(sheetWidth, sheetHeight);
            double usablePaperLength = paperLength - margin;

            if (usablePaperLength <= 1.0)
                return 30.0;

            double requiredModelLength = beamLength + AUTO_SCALE_DIM_RESERVE;
            double requiredScale = requiredModelLength / usablePaperLength;

            double[] allowedScales = new double[] { 5.0, 10.0, 15.0, 20.0, 30.0 };

            foreach (double scale in allowedScales)
            {
                if (scale >= requiredScale)
                    return scale;
            }

            return 30.0;
        }

        private static double GetScaleMarginBySheetSize(double width, double height)
        {
            if (IsSheetSize(width, height, A3_SHEET_WIDTH, A3_SHEET_HEIGHT))
                return A3_SCALE_MARGIN_TOTAL;

            // A1 và mọi khổ giấy khác đều dùng margin tương tự A1.
            return A1_OR_OTHER_SCALE_MARGIN_TOTAL;
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
            View baseView,
            TopBoundary baseBoundary,
            View targetView,
            TopBoundary targetBoundary,
            int baseMaxTier,
            int targetMaxTier)
        {
            try
            {
                if (baseView == null || targetView == null)
                    return;

                double baseLeft;
                double baseBottom;
                double targetLeft;
                double targetTop;

                if (!TryGetGeometryLeftEdge(baseView, baseBoundary, out baseLeft))
                    return;

                if (!TryGetGeometryBottomEdge(baseView, baseBoundary, out baseBottom))
                    return;

                if (!TryGetGeometryLeftEdge(targetView, targetBoundary, out targetLeft))
                    return;

                if (!TryGetGeometryTopEdge(targetView, targetBoundary, out targetTop))
                    return;

                double scale = GetCurrentDrawingScale(baseView);
                if (scale <= 0.0)
                    scale = 1.0;

                double beamLength = 0.0;
                if (baseBoundary.IsValid)
                    beamLength = Math.Abs(baseBoundary.MaxX - baseBoundary.MinX);
                if (beamLength <= 0.0 && targetBoundary.IsValid)
                    beamLength = Math.Abs(targetBoundary.MaxX - targetBoundary.MinX);

                double gap =
                    (
                        GetSteelDimOffsetByTier(baseMaxTier)
                        + GetSteelDimOffsetByTier(targetMaxTier)
                    ) * 1.0;

                Point baseOrigin = baseView.Origin;
                Point targetOrigin = targetView.Origin;
                if (baseOrigin == null || targetOrigin == null)
                    return;

                // Canh trái theo mép hình học: Target.Left = Base.Left
                double targetSheetLeft = baseOrigin.X + baseLeft / scale;
                double currentSheetLeft = targetOrigin.X + targetLeft / scale;
                double deltaX = targetSheetLeft - currentSheetLeft;

                // Tự arrange dọc: Target nằm dưới Base, khoảng hở theo tier DIM ngoài cùng.
                double targetSheetTop = baseOrigin.Y + baseBottom / scale - gap / scale;
                double currentSheetTop = targetOrigin.Y + targetTop / scale;
                double deltaY = targetSheetTop - currentSheetTop;

                if (Math.Abs(deltaX) <= 0.01 && Math.Abs(deltaY) <= 0.01)
                    return;

                Point newOrigin = new Point(
                    targetOrigin.X + deltaX,
                    targetOrigin.Y + deltaY,
                    targetOrigin.Z
                );

                if (TrySetViewOrigin(targetView, newOrigin))
                {
                    try { targetView.Modify(); }
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

                ViewPaperBox frontGreenBox;
                ViewPaperBox sectionGreenBox;
                if (TryGetViewGreenPaperBoxForShape(frontView, out frontGreenBox) &&
                    TryGetViewGreenPaperBoxForShape(sectionView, out sectionGreenBox))
                {
                    if (greenBoxGap < 0.0)
                        greenBoxGap = 0.0;

                    double greenDeltaX =
                        frontGreenBox.MaxX + greenBoxGap - sectionGreenBox.MinX;
                    Point greenFrontOrigin = frontView.Origin;
                    Point greenSectionOrigin = sectionView.Origin;
                    if (greenFrontOrigin == null || greenSectionOrigin == null)
                        return;

                    // Keep the original Front/Exact Section center-line alignment.
                    // Green boxes are used only for the horizontal gap because
                    // dimensions and marks can shift their visual CenterY.
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
                double sectionLeft;

                if (!TryGetGeometryRightEdge(frontView, frontBoundary, out frontRight))
                    return;

                TopBoundary sectionBoundary = new TopBoundary();
                if (!TryGetExactSectionGeometryBoundary(sectionView, out sectionBoundary))
                    TryGetDrawingPartGeometryBoundary(sectionView, out sectionBoundary);

                if (!TryGetGeometryLeftEdge(sectionView, sectionBoundary, out sectionLeft))
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
                double gap = GetSteelDimOffsetByTier(LastFrontRightDimTier)
                    + 100.0 * shortScale;

                Point frontOrigin = frontView.Origin;
                Point sectionOrigin = sectionView.Origin;
                if (frontOrigin == null || sectionOrigin == null)
                    return;

                double targetSheetLeft = frontOrigin.X + frontRight / scale + gap / scale;
                double currentSheetLeft = sectionOrigin.X + sectionLeft / scale;
                double deltaX = targetSheetLeft - currentSheetLeft;

                // EXACT / A-A: canh thẳng hàng với Front theo Origin.Y như bản Shape L đã chạy ổn.
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

        private static bool TryGetDrawingPartGeometryBoundary(View view, out TopBoundary boundary)
        {
            boundary = new TopBoundary();

            try
            {
                if (view == null)
                    return false;

                double minX = 999999999.0;
                double maxX = -999999999.0;
                double minY = 999999999.0;
                double maxY = -999999999.0;
                bool found = false;

                DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
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


        private static void ApplyForcedTopBottomBlockLimitForCenter(
            double sheetWidth,
            double sheetHeight,
            ref double usableMinY,
            ref double usableMaxY)
        {
            try
            {
                if (!FORCE_CENTER_BY_TOP_BOTTOM_BLOCKS)
                    return;

                if (sheetWidth <= 1.0 || sheetHeight <= 1.0)
                    return;

                double bottomReserved = sheetHeight * CENTER_BOTTOM_BLOCK_HEIGHT_RATIO + CENTER_BLOCK_EXTRA_GAP;
                double topReserved = sheetHeight * CENTER_TOP_BLOCK_HEIGHT_RATIO + CENTER_BLOCK_EXTRA_GAP;

                // Chặn giá trị bất thường để không làm mất vùng center.
                if (bottomReserved < 0.0) bottomReserved = 0.0;
                if (topReserved < 0.0) topReserved = 0.0;
                if (bottomReserved > sheetHeight * 0.40) bottomReserved = sheetHeight * 0.40;
                if (topReserved > sheetHeight * 0.25) topReserved = sheetHeight * 0.25;

                double forcedMinY = bottomReserved;
                double forcedMaxY = sheetHeight - topReserved;

                if (forcedMaxY > forcedMinY + sheetHeight * 0.25)
                {
                    usableMinY = Math.Max(usableMinY, forcedMinY);
                    usableMaxY = Math.Min(usableMaxY, forcedMaxY);
                }
            }
            catch
            {
            }
        }

        private static void ApplyTopBottomSheetBlockLimitForCenter(
            Drawing drawing,
            double sheetWidth,
            double sheetHeight,
            double margin,
            ref double usableMinY,
            ref double usableMaxY)
        {
            try
            {
                if (drawing == null || sheetWidth <= 1.0 || sheetHeight <= 1.0)
                    return;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return;

                double bottomLimit = usableMinY;
                double topLimit = usableMaxY;

                double bottomBandMaxY = sheetHeight * 0.35;
                double topBandMinY = sheetHeight * 0.65;

                DrawingObjectEnumerator objects = null;
                try { objects = sheet.GetAllObjects(); }
                catch { objects = null; }

                if (objects == null)
                    return;

                while (true)
                {
                    bool moved = false;
                    try { moved = objects.MoveNext(); }
                    catch { break; }

                    if (!moved)
                        break;

                    DrawingObject obj = null;
                    try { obj = objects.Current as DrawingObject; }
                    catch { obj = null; }

                    if (obj == null)
                        continue;

                    // Không lấy view làm block cấm. View sẽ được center riêng bên dưới.
                    if (obj is View)
                        continue;

                    AABB box;
                    if (!TryGetDrawingObjectPaperBoxForCenter(obj, out box))
                        continue;

                    if (box == null || box.MinPoint == null || box.MaxPoint == null)
                        continue;

                    double minX = Math.Min(box.MinPoint.X, box.MaxPoint.X);
                    double maxX = Math.Max(box.MinPoint.X, box.MaxPoint.X);
                    double minY = Math.Min(box.MinPoint.Y, box.MaxPoint.Y);
                    double maxY = Math.Max(box.MinPoint.Y, box.MaxPoint.Y);
                    double w = Math.Abs(maxX - minX);
                    double h = Math.Abs(maxY - minY);

                    if (w < 2.0 && h < 2.0)
                        continue;

                    // Bỏ qua khung viền sheet / object bất thường chiếm gần hết tờ.
                    if (w > sheetWidth * 0.95 && h > sheetHeight * 0.90)
                        continue;

                    if (minX < -sheetWidth || maxX > sheetWidth * 2.0 || minY < -sheetHeight || maxY > sheetHeight * 2.0)
                        continue;

                    double centerY = (minY + maxY) * 0.5;

                    // Block dưới: chỉ cần object nằm trong dải thấp của sheet.
                    // Lấy maxY lớn nhất làm đáy vùng vàng.
                    if (centerY <= bottomBandMaxY && minY <= bottomBandMaxY)
                    {
                        if (maxY > bottomLimit && maxY < sheetHeight * 0.55)
                            bottomLimit = maxY;
                    }

                    // Block trên: chỉ cần object nằm trong dải cao của sheet.
                    // Lấy minY nhỏ nhất làm nóc vùng vàng.
                    if (centerY >= topBandMinY && maxY >= topBandMinY)
                    {
                        if (minY < topLimit && minY > sheetHeight * 0.45)
                            topLimit = minY;
                    }
                }

                // Chỉ nhận nếu vùng vàng còn đủ lớn, tránh bắt nhầm object.
                if (topLimit > bottomLimit + sheetHeight * 0.25)
                {
                    usableMinY = Math.Max(usableMinY, bottomLimit + margin);
                    usableMaxY = Math.Min(usableMaxY, topLimit - margin);
                }
            }
            catch
            {
            }
        }

        private static bool TryGetDrawingObjectPaperBoxForCenter(
            DrawingObject obj,
            out AABB box)
        {
            box = null;

            try
            {
                if (obj == null)
                    return false;

                MethodInfo method = obj.GetType().GetMethod(
                    "GetAxisAlignedBoundingBox",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (method == null)
                    return false;

                object value = method.Invoke(obj, null);
                box = value as AABB;

                if (box == null || box.MinPoint == null || box.MaxPoint == null)
                    return false;

                return true;
            }
            catch
            {
                box = null;
                return false;
            }
        }


        private static void CenterShapeViewsByPurpleBoxOnSheet(
            Drawing drawing,
            View topView,
            View frontView,
            View sectionView,
            List<View> bottomViews)
        {
            try
            {
                if (drawing == null || topView == null)
                    return;

                double sheetWidth;
                double sheetHeight;
                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                if (sheetWidth <= 1.0 || sheetHeight <= 1.0)
                    return;

                double marginTotal = GetScaleMarginBySheetSize(sheetWidth, sheetHeight);
                double margin = marginTotal * 0.5;

                double usableMinX = margin;
                double usableMaxX = sheetWidth - margin;
                double usableMinY = margin;
                double usableMaxY = sheetHeight - margin;

                // VÙNG CENTER HỮU DỤNG THEO 2 BLOCK TRÊN / DƯỚI:
                // Không đổi thuật toán DIM, không đổi align, không đổi arrange gap 15.
                // Chỉ co vùng center theo chiều Y để tránh title block dưới và revision block trên.
                ApplyTopBottomSheetBlockLimitForCenter(drawing, sheetWidth, sheetHeight, margin, ref usableMinY, ref usableMaxY);

                // FIX: nếu Tekla không trả được bounding box của template block,
                // ép lại vùng vàng bằng 2 block chiếm chiều cao để không còn dùng margin cũ.
                ApplyForcedTopBottomBlockLimitForCenter(sheetWidth, sheetHeight, ref usableMinY, ref usableMaxY);

                if (usableMaxX <= usableMinX + 1.0 || usableMaxY <= usableMinY + 1.0)
                    return;

                List<View> views = new List<View>();
                AddUniqueViewForMove(views, topView);
                AddUniqueViewForMove(views, frontView);
                AddUniqueViewForMove(views, sectionView);

                if (bottomViews != null)
                {
                    foreach (View bottomView in bottomViews)
                        AddUniqueViewForMove(views, bottomView);
                }

                if (views.Count == 0)
                    return;

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                double maxY = double.MinValue;
                int count = 0;

                foreach (View v in views)
                {
                    ViewPaperBox box;
                    // MOVE CENTER dùng khung tím RestrictionBox, không dùng khung xanh.
                    if (!TryGetViewPurplePaperBoxForShape(v, out box))
                        continue;

                    if (box.MinX < minX) minX = box.MinX;
                    if (box.MaxX > maxX) maxX = box.MaxX;
                    if (box.MinY < minY) minY = box.MinY;
                    if (box.MaxY > maxY) maxY = box.MaxY;
                    count++;
                }

                if (count == 0)
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

                if (Math.Abs(dx) > sheetWidth * 2.0 || Math.Abs(dy) > sheetHeight * 2.0)
                    return;

                foreach (View v in views)
                    MoveViewBySheetDelta(v, dx, dy);
            }
            catch
            {
            }
        }

        private static void ForceFinalEqualArrangeShapeTopFrontBottomGap15(
            View topView,
            View frontView,
            List<View> bottomViews,
            double gap)
        {
            try
            {
                List<View> stackViews = new List<View>();
                AddUniqueViewForMove(stackViews, topView);
                AddUniqueViewForMove(stackViews, frontView);

                if (bottomViews != null)
                {
                    foreach (View bottomView in bottomViews)
                        AddUniqueViewForMove(stackViews, bottomView);
                }

                if (stackViews.Count < 2)
                    return;

                List<ViewPaperBox> boxes = new List<ViewPaperBox>();
                foreach (View v in stackViews)
                {
                    ViewPaperBox b;
                    // ARRANGE dùng khung xanh/view frame để gap có tính cả DIM/mark.
                    if (TryGetViewGreenPaperBoxForShape(v, out b))
                    {
                        if (b != null && b.Width > 1.0 && b.Height > 1.0)
                            boxes.Add(b);
                    }
                }

                if (boxes.Count < 2)
                    return;

                boxes.Sort(delegate (ViewPaperBox a, ViewPaperBox b)
                {
                    return b.CenterY.CompareTo(a.CenterY);
                });

                double totalHeight = 0.0;
                foreach (ViewPaperBox b in boxes)
                    totalHeight += b.Height;

                double currentMinY = double.MaxValue;
                double currentMaxY = double.MinValue;
                foreach (ViewPaperBox b in boxes)
                {
                    if (b.MinY < currentMinY) currentMinY = b.MinY;
                    if (b.MaxY > currentMaxY) currentMaxY = b.MaxY;
                }

                double currentCenter = (currentMinY + currentMaxY) * 0.5;
                double totalStackHeight = totalHeight + gap * (boxes.Count - 1);
                double cursorMaxY = currentCenter + totalStackHeight * 0.5;

                foreach (ViewPaperBox b in boxes)
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

        private static void AddUniqueViewForMove(List<View> views, View view)
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

        private class ViewPaperBox
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

        private static bool TryGetViewPurplePaperBoxForShape(
            View view,
            out ViewPaperBox box)
        {
            box = null;

            try
            {
                if (view == null)
                    return false;

                AABB rb = null;
                try { rb = view.RestrictionBox; }
                catch { rb = null; }

                if (rb == null || rb.MinPoint == null || rb.MaxPoint == null)
                    return false;

                Point origin = view.Origin;
                if (origin == null)
                    return false;

                double scale = GetCurrentDrawingScale(view);
                if (scale <= 0.0)
                    scale = TryGetViewScale(view);
                if (scale <= 0.0)
                    scale = 1.0;

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
                box.CenterY = (box.MinY + box.MaxY) * 0.5;

                if (box.Width <= 0.5 || box.Height <= 0.5)
                    return false;

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

        private static bool TryGetViewGreenPaperBoxForShape(
            View view,
            out ViewPaperBox box)
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

                box = new ViewPaperBox();
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
            View sectionView,
            List<View> bottomViews,
            List<TopBoundary> bottomBoundaries)
        {
            try
            {
                if (drawing == null || topView == null)
                    return;

                double sheetWidth;
                double sheetHeight;
                if (!TryGetDrawingSheetSize(drawing, out sheetWidth, out sheetHeight))
                    return;

                double margin = GetScaleMarginBySheetSize(sheetWidth, sheetHeight);
                double usableMinX = margin * 0.5;
                double usableMaxX = sheetWidth - margin * 0.5;
                double usableMinY = margin * 0.5;
                double usableMaxY = sheetHeight - margin * 0.5;

                double sheetCenterX = sheetWidth * 0.5;
                double sheetCenterY = sheetHeight * 0.5;

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

                if (bottomViews != null)
                {
                    for (int i = 0; i < bottomViews.Count; i++)
                    {
                        TopBoundary bb = new TopBoundary();
                        if (bottomBoundaries != null && i < bottomBoundaries.Count)
                            bb = bottomBoundaries[i];

                        AddViewSheetBoundsToCluster(
                            bottomViews[i],
                            bb,
                            ref clusterMinX,
                            ref clusterMaxX,
                            ref clusterMinY,
                            ref clusterMaxY,
                            ref hasAny
                        );
                    }
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

                if (bottomViews != null)
                {
                    foreach (View bottomView in bottomViews)
                        MoveViewBySheetDelta(bottomView, dx, dy);
                }
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
                if (view == null)
                    return;

                double scale = GetCurrentDrawingScale(view);
                if (scale <= 0.0)
                    scale = 1.0;

                double minX;
                double maxX;
                double minY;
                double maxY;

                if (!TryGetGeometryLeftEdge(view, boundary, out minX))
                    return;
                if (!TryGetGeometryRightEdge(view, boundary, out maxX))
                    return;
                if (!TryGetGeometryBottomEdge(view, boundary, out minY))
                    return;
                if (!TryGetGeometryTopEdge(view, boundary, out maxY))
                    return;

                Point origin = view.Origin;
                if (origin == null)
                    return;

                double sheetMinX = origin.X + minX / scale;
                double sheetMaxX = origin.X + maxX / scale;
                double sheetMinY = origin.Y + minY / scale;
                double sheetMaxY = origin.Y + maxY / scale;

                if (sheetMaxX <= sheetMinX + 0.01 || sheetMaxY <= sheetMinY + 0.01)
                    return;

                if (sheetMinX < clusterMinX) clusterMinX = sheetMinX;
                if (sheetMaxX > clusterMaxX) clusterMaxX = sheetMaxX;
                if (sheetMinY < clusterMinY) clusterMinY = sheetMinY;
                if (sheetMaxY > clusterMaxY) clusterMaxY = sheetMaxY;
                hasAny = true;
            }
            catch
            {
            }
        }

        private static void MoveViewBySheetDelta(View view, double dx, double dy)
        {
            try
            {
                if (view == null)
                    return;

                Point origin = view.Origin;
                if (origin == null)
                    return;

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
        private static int CreateDimsForFrontView(
            Model model,
            ModelPart part,
            View view,
            bool isAssemblyDrawing,
            out TopBoundary boundary)
        {
            boundary = new TopBoundary();
            int count = 0;
            LastFrontTopDimTier = 1;
            LastFrontBottomDimTier = 1;
            LastFrontRightDimTier = 1;

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

                List<Point> frontPolygon = GetFrontWebFacePolygon(solid, min, max);
                if (frontPolygon.Count >= 2)
                {
                    GetMinMax(frontPolygon, out minX, out maxX, out minY, out maxY);
                }

                // FRONT NOTCH - THỬ DÙNG NGUỒN ĐIỂM CHIẾU THEO VIEW GIỐNG PLATE:
                // Plate V20/V21 lấy điểm thật của solid sau khi SetCurrentTransformationPlane(view.DisplayCoordinateSystem),
                // không dựa vào bounding box. Ở đây chỉ dùng nguồn điểm này cho chân DIM rãnh Front.
                List<Point> frontProjectedSolidPoints = GetProjectedSolidPointsForFrontNotchDims(solid);
                if (frontProjectedSolidPoints == null || frontProjectedSolidPoints.Count < 2)
                    frontProjectedSolidPoints = frontPolygon;

                ChamferEdgeAnchors frontEdgeAnchors = BuildChamferEdgeAnchors(frontPolygon, minX, maxX, minY, maxY);
                DimOffsetAnchor4 offsetAnchors =
                    BuildDimOffsetAnchor4(frontEdgeAnchors);

                boundary.IsValid = true;
                boundary.MinX = minX;
                boundary.MaxX = maxX;
                boundary.MinY = minY;
                boundary.MaxY = maxY;

                double beamLength = Math.Abs(maxX - minX);

                List<Point> frontHoles =
                    GetVisibleFrontWebBoltCentersFromView(
                        model,
                        view,
                        minX,
                        maxX,
                        minY,
                        maxY
                    );

                StraightDimensionSetHandler handler =
                    new StraightDimensionSetHandler();

                // FRONT VIEW - tầng dọc cấp theo DIM thực tế, không giữ chỗ cứng:
                // - Chamfer/rãnh dọc dùng tầng 0.
                // - DIM dọc lỗ được ưu tiên lấy tầng 1 nếu tạo thành công.
                // - Nếu không có DIM dọc lỗ, DIM tổng dùng ngay tầng 1.
                // - Left/Right tách riêng, không đẩy tầng chéo hướng.
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
                        out frontChamferInfluence
                    );

                    if (frontChamferCount > 0)
                        frontChamferInfluence.Any = true;

                    count += frontChamferCount;
                }

                bool frontTopChamferDimCreated =
                    frontChamferCount > 0 && frontChamferInfluence.Top;

                // FRONT NOTCH - dùng biên dạng chiếu trực diện giống hướng Plate:
                // Chỉ áp dụng cho thuật toán rãnh mặt Front.
                // Không thay frontPolygon chung để tránh ảnh hưởng DIM lỗ/tổng/front logic khác.
                List<Point> frontNotchProfile =
                    (frontProjectedSolidPoints != null && frontProjectedSolidPoints.Count >= 2)
                    ? frontProjectedSolidPoints
                    : frontPolygon;

                ChamferInfluence frontNotchInfluence = new ChamferInfluence();
                int frontNotchCount = CreateFrontAxisAlignedNotchDims(
                    handler,
                    view,
                    frontNotchProfile,
                    frontProjectedSolidPoints,
                    offsetAnchors,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    beamLength,
                    out frontNotchInfluence
                );

                if (frontNotchCount > 0)
                {
                    MergeInfluence(ref frontChamferInfluence, frontNotchInfluence);
                    count += frontNotchCount;
                }

                int leftStartTier = 1;
                int rightStartTier = 1;

                // FRONT VIEW - TẦNG DIM NGANG LỖ:
                // Chỉ đổi bộ cấp tầng; giữ nguyên cách nhóm lỗ, chân DIM và quy luật lỗ giữa/đặc biệt.
                // Rãnh Front phía trên có DIM ngang riêng ở tầng 1; DIM lỗ vẫn giữ tầng 0.
                // Chỉ chamfer phía trên (không phải rãnh đã nhận diện) mới đẩy DIM lỗ lên tầng 1.
                int frontEndHoleXTier = frontNotchInfluence.Top
                    ? 0
                    : (frontTopChamferDimCreated ? 1 : 0);
                int frontReservedNotchXTier = frontNotchInfluence.Top ? 1 : -1;

                int holeCount = 0;
                int frontHorizontalHoleTierCount = 0;
                int frontHorizontalHoleHighestTier = -1;
                int frontLeftVerticalHoleTierCount = 0;
                int frontRightVerticalHoleTierCount = 0;

                if (isAssemblyDrawing)
                {
                    // ASSEMBLY - FRONT:
                    // Mỗi phía chỉ định vị một lỗ gần mép nhất.
                    // DIM ngang: mép bên -> lỗ.
                    // DIM dọc: luôn mép trên -> lỗ gần mép trên nhất của phía đó.
                    // PointList chỉ có hai điểm nên không tạo chain qua các lỗ còn lại.
                    holeCount += CreateAssemblyFrontNearestEdgeHoleDims(
                        handler,
                        view,
                        frontHoles,
                        minX,
                        maxX,
                        maxY,
                        frontEdgeAnchors,
                        offsetAnchors,
                        frontEndHoleXTier,
                        frontReservedNotchXTier,
                        leftStartTier,
                        rightStartTier,
                        out frontHorizontalHoleTierCount,
                        out frontHorizontalHoleHighestTier,
                        out frontLeftVerticalHoleTierCount,
                        out frontRightVerticalHoleTierCount
                    );
                }
                else
                {
                    holeCount += CreateFrontHoleXDimsByGeometry(
                        handler,
                        view,
                        frontHoles,
                        minX,
                        maxX,
                        maxY,
                        frontEdgeAnchors,
                        offsetAnchors,
                        beamLength,
                        frontEndHoleXTier,
                        frontReservedNotchXTier,
                        frontNotchCount > 0,
                        frontNotchProfile,
                        out frontHorizontalHoleTierCount,
                        out frontHorizontalHoleHighestTier
                    );

                    holeCount += CreateFrontHoleYDimsByGeometry(
                        handler,
                        view,
                        frontHoles,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        frontEdgeAnchors,
                        offsetAnchors,
                        beamLength,
                        leftStartTier,
                        rightStartTier,
                        out frontLeftVerticalHoleTierCount,
                        out frontRightVerticalHoleTierCount
                    );
                }

                count += holeCount;

                // FRONT VIEW - DIM tổng theo từng hướng riêng:
                // - Tổng ngang phía trên nằm ngoài DIM/chamfer phía trên.
                // - Tổng dọc bên trái nằm ngoài DIM/chamfer bên trái.
                // - Không dùng chung 1 tầng tổng cho cả ngang và dọc để tránh kéo nhầm tầng.
                int frontHorizontalHighestTier = -1;
                if (frontTopChamferDimCreated)
                    frontHorizontalHighestTier = Math.Max(frontHorizontalHighestTier, 0);
                if (frontNotchInfluence.Top)
                    frontHorizontalHighestTier = Math.Max(frontHorizontalHighestTier, 1);
                if (frontHorizontalHoleHighestTier >= 0)
                    frontHorizontalHighestTier = Math.Max(frontHorizontalHighestTier, frontHorizontalHoleHighestTier);

                int frontHorizontalTotalTier = Math.Max(1, frontHorizontalHighestTier + 1);
                int frontVerticalTotalTier = leftStartTier + frontLeftVerticalHoleTierCount;
                int frontRightVerticalMaxTier = frontRightVerticalHoleTierCount > 0
                    ? rightStartTier + frontRightVerticalHoleTierCount - 1
                    : rightStartTier;
                int frontRightVerticalTotalTier =
                    rightStartTier + frontRightVerticalHoleTierCount;

                LastFrontTopDimTier = Math.Max(1, frontHorizontalTotalTier);
                LastFrontBottomDimTier = 1;
                LastFrontRightDimTier = Math.Max(
                    1,
                    isAssemblyDrawing
                        ? frontRightVerticalTotalTier
                        : frontRightVerticalMaxTier
                );

                count += CreateFrontTotalDims(
                    handler,
                    view,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    frontEdgeAnchors,
                    offsetAnchors,
                    GetSteelDimOffsetByTier(frontHorizontalTotalTier),
                    GetSteelDimOffsetByTier(frontVerticalTotalTier)
                );

                if (isAssemblyDrawing)
                {
                    // ASSEMBLY - FRONT:
                    // Bổ sung DIM tổng dọc bên phải, đặt ngoài DIM lỗ bên phải nếu có.
                    // Single Part giữ nguyên DIM tổng dọc bên trái như luồng hiện tại.
                    count += CreateAssemblyFrontRightTotalDim(
                        handler,
                        view,
                        frontEdgeAnchors,
                        offsetAnchors,
                        GetSteelDimOffsetByTier(frontRightVerticalTotalTier)
                    );
                }
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

        private static int CreateAssemblyFrontNearestEdgeHoleDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            double minX,
            double maxX,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            int horizontalFirstTier,
            int horizontalReservedTier,
            int leftStartTier,
            int rightStartTier,
            out int usedHorizontalTierCount,
            out int highestHorizontalTier,
            out int usedLeftVerticalTierCount,
            out int usedRightVerticalTierCount)
        {
            int count = 0;
            usedHorizontalTierCount = 0;
            highestHorizontalTier = -1;
            usedLeftVerticalTierCount = 0;
            usedRightVerticalTierCount = 0;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                List<Point> leftEndHoles;
                List<Point> rightEndHoles;
                List<Point> middleHoles;

                SplitFrontHolesByX(
                    holes,
                    minX,
                    maxX,
                    out leftEndHoles,
                    out rightEndHoles,
                    out middleHoles
                );

                Point leftHorizontalHole =
                    FindAssemblyFrontNearestEdgeHole(leftEndHoles, false);
                Point rightHorizontalHole =
                    FindAssemblyFrontNearestEdgeHole(rightEndHoles, true);
                Point leftVerticalHole =
                    FindAssemblyFrontVerticalRepresentativeHole(leftEndHoles, false);
                Point rightVerticalHole =
                    FindAssemblyFrontVerticalRepresentativeHole(rightEndHoles, true);

                int horizontalTier = horizontalFirstTier;
                if (horizontalReservedTier >= 0 && horizontalTier >= horizontalReservedTier)
                    horizontalTier++;

                double horizontalOffset = GetSteelDimOffsetByTier(horizontalTier);
                bool horizontalCreated = false;

                if (leftHorizontalHole != null)
                {
                    Point leftEdge = edgeAnchors.LeftMost != null
                        ? Clone2D(edgeAnchors.LeftMost)
                        : new Point(minX, leftHorizontalHole.Y, 0);

                    PointList horizontalDim = new PointList();
                    horizontalDim.Add(leftEdge);
                    horizontalDim.Add(CreateHorizontalHoleDimFootAbove(
                        leftHorizontalHole,
                        GetHoleDimGap(leftHorizontalHole)
                    ));

                    double leftHorizontalOffset = ResolveDimDistanceByAnchor4(
                        horizontalDim,
                        new Vector(0, 1, 0),
                        offsetAnchors,
                        horizontalOffset);

                    if (handler.CreateDimensionSet(
                        view,
                        horizontalDim,
                        new Vector(0, 1, 0),
                        leftHorizontalOffset) != null)
                    {
                        count++;
                        horizontalCreated = true;
                    }
                }

                if (leftVerticalHole != null)
                {
                    Point topEdge = edgeAnchors.TopLeft != null
                        ? Clone2D(edgeAnchors.TopLeft)
                        : new Point(leftVerticalHole.X, maxY, 0);

                    PointList verticalDim = new PointList();
                    verticalDim.Add(topEdge);
                    verticalDim.Add(new Point(
                        leftVerticalHole.X - GetHoleDimGap(leftVerticalHole),
                        leftVerticalHole.Y,
                        0
                    ));

                    double leftVerticalOffset = GetSteelDimOffsetByTier(leftStartTier);
                    if (edgeAnchors.LeftMost != null)
                    {
                        leftVerticalOffset = ResolveDimDistanceByAnchor4(
                            verticalDim,
                            new Vector(-1, 0, 0),
                            offsetAnchors,
                            leftVerticalOffset
                        );
                    }

                    if (handler.CreateDimensionSet(
                        view,
                        verticalDim,
                        new Vector(-1, 0, 0),
                        leftVerticalOffset) != null)
                    {
                        count++;
                        usedLeftVerticalTierCount = 1;
                    }
                }

                if (rightHorizontalHole != null)
                {
                    Point rightEdge = edgeAnchors.RightMost != null
                        ? Clone2D(edgeAnchors.RightMost)
                        : new Point(maxX, rightHorizontalHole.Y, 0);

                    PointList horizontalDim = new PointList();
                    horizontalDim.Add(rightEdge);
                    horizontalDim.Add(CreateHorizontalHoleDimFootAbove(
                        rightHorizontalHole,
                        GetHoleDimGap(rightHorizontalHole)
                    ));

                    double rightHorizontalOffset = ResolveDimDistanceByAnchor4(
                        horizontalDim,
                        new Vector(0, 1, 0),
                        offsetAnchors,
                        horizontalOffset);

                    if (handler.CreateDimensionSet(
                        view,
                        horizontalDim,
                        new Vector(0, 1, 0),
                        rightHorizontalOffset) != null)
                    {
                        count++;
                        horizontalCreated = true;
                    }
                }

                if (rightVerticalHole != null)
                {
                    Point topEdge = edgeAnchors.TopRight != null
                        ? Clone2D(edgeAnchors.TopRight)
                        : new Point(rightVerticalHole.X, maxY, 0);

                    PointList verticalDim = new PointList();
                    verticalDim.Add(topEdge);
                    verticalDim.Add(new Point(
                        rightVerticalHole.X + GetHoleDimGap(rightVerticalHole),
                        rightVerticalHole.Y,
                        0
                    ));

                    double rightVerticalOffset = GetSteelDimOffsetByTier(rightStartTier);
                    if (edgeAnchors.RightMost != null)
                    {
                        rightVerticalOffset = ResolveDimDistanceByAnchor4(
                            verticalDim,
                            new Vector(1, 0, 0),
                            offsetAnchors,
                            rightVerticalOffset
                        );
                    }

                    if (handler.CreateDimensionSet(
                        view,
                        verticalDim,
                        new Vector(1, 0, 0),
                        rightVerticalOffset) != null)
                    {
                        count++;
                        usedRightVerticalTierCount = 1;
                    }
                }

                if (horizontalCreated)
                {
                    usedHorizontalTierCount = 1;
                    highestHorizontalTier = horizontalTier;
                }
            }
            catch
            {
            }

            return count;
        }

        private static Point FindAssemblyFrontNearestEdgeHole(
            List<Point> holes,
            bool isRight)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    if (best == null)
                    {
                        best = Clone2DWithDiameter(hole);
                        continue;
                    }

                    bool isCloserToSide = isRight
                        ? hole.X > best.X + TOL
                        : hole.X < best.X - TOL;

                    // Cùng cột đại diện gần mép: dùng lỗ thấp nhất cho DIM ngang.
                    bool isSameSideDistanceAndLower =
                        Math.Abs(hole.X - best.X) <= TOL &&
                        hole.Y < best.Y - TOL;

                    if (isCloserToSide || isSameSideDistanceAndLower)
                        best = Clone2DWithDiameter(hole);
                }
            }
            catch
            {
                return null;
            }

            return best;
        }

        private static Point FindAssemblyFrontVerticalRepresentativeHole(
            List<Point> holes,
            bool isRight)
        {
            Point best = null;

            try
            {
                if (holes == null)
                    return null;

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    if (best == null)
                    {
                        best = Clone2DWithDiameter(hole);
                        continue;
                    }

                    // Assembly vẫn chỉ DIM một lỗ đại diện:
                    // - cụm trái / DIM trái: lấy cột X lớn nhất;
                    // - cụm phải / DIM phải: lấy cột X nhỏ nhất;
                    // - trong cột đã chọn vẫn lấy lỗ trên cùng.
                    bool isFartherFromDimSide = isRight
                        ? hole.X < best.X - TOL
                        : hole.X > best.X + TOL;
                    bool isSameColumnAndCloserToTop =
                        Math.Abs(hole.X - best.X) <= TOL &&
                        hole.Y > best.Y + TOL;

                    if (isFartherFromDimSide || isSameColumnAndCloserToTop)
                        best = Clone2DWithDiameter(hole);
                }
            }
            catch
            {
                return null;
            }

            return best;
        }

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

            double realUpperTotalOffset = ResolveDimDistanceByAnchor4(
                lengthPts,
                new Vector(0, 1, 0),
                offsetAnchors,
                horizontalTotalOffset
            );

            if (handler.CreateDimensionSet(
                view,
                lengthPts,
                new Vector(0, 1, 0),
                realUpperTotalOffset) != null)
                count++;

            PointList heightPts = new PointList();
            // FRONT tổng dọc: dùng điểm thấp/cao ngoài cùng thật của dầm.
            heightPts.Add(Clone2D(edgeAnchors.TopMost));
            heightPts.Add(Clone2D(edgeAnchors.BottomMost));

            double realLeftTotalOffset = ResolveDimDistanceByAnchor4(
                heightPts,
                new Vector(-1, 0, 0),
                offsetAnchors,
                verticalTotalOffset
            );

            if (handler.CreateDimensionSet(
                view,
                heightPts,
                new Vector(-1, 0, 0),
                realLeftTotalOffset) != null)
                count++;

            return count;
        }

        private static int CreateAssemblyFrontRightTotalDim(
            StraightDimensionSetHandler handler,
            View view,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double verticalTotalOffset)
        {
            try
            {
                if (handler == null ||
                    view == null ||
                    edgeAnchors.BottomRight == null ||
                    edgeAnchors.TopRight == null ||
                    edgeAnchors.RightMost == null)
                    return 0;

                PointList heightPts = new PointList();
                heightPts.Add(Clone2D(edgeAnchors.TopRight));
                heightPts.Add(Clone2D(edgeAnchors.BottomRight));

                double realRightTotalOffset = ResolveDimDistanceByAnchor4(
                    heightPts,
                    new Vector(1, 0, 0),
                    offsetAnchors,
                    verticalTotalOffset
                );

                return handler.CreateDimensionSet(
                    view,
                    heightPts,
                    new Vector(1, 0, 0),
                    realRightTotalOffset) != null
                    ? 1
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private class HoleColumnYPatternFamily
        {
            public double HoleKey;
            public List<double> YPattern = new List<double>();
            public List<Point> Holes = new List<Point>();
            public List<List<Point>> Clusters = new List<List<Point>>();
        }

        private static int CreateFrontHoleXDimsByGeometry(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            double minX,
            double maxX,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double beamLength,
            int firstTier,
            int reservedTier,
            bool hasFrontNotch,
            List<Point> frontNotchProfile,
            out int usedTierCount,
            out int highestUsedTier)
        {
            int count = 0;
            usedTierCount = 0;
            highestUsedTier = -1;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                List<HoleColumnYPatternFamily> families =
                    BuildHoleFamiliesByColumnYPattern(holes, tol);

                families.Sort(delegate (HoleColumnYPatternFamily a, HoleColumnYPatternFamily b)
                {
                    bool aSpans = HoleFamilySpansOuterHoles(a, holes, tol);
                    bool bSpans = HoleFamilySpansOuterHoles(b, holes, tol);

                    if (aSpans != bSpans)
                        return aSpans ? -1 : 1;

                    int c = a.HoleKey.CompareTo(b.HoleKey);
                    if (c != 0) return c;

                    double ay = a.YPattern.Count > 0 ? a.YPattern[0] : 0.0;
                    double by = b.YPattern.Count > 0 ? b.YPattern[0] : 0.0;
                    return ay.CompareTo(by);
                });

                bool shareOppositeEndFamilyTier =
                    CanShareFrontOppositeEndFamilyTier(
                        families,
                        minX,
                        maxX,
                        tol
                    );

                foreach (HoleColumnYPatternFamily family in families)
                {
                    if (family == null || family.Holes == null || family.Holes.Count == 0 ||
                        family.Clusters == null || family.Clusters.Count == 0)
                        continue;

                    family.Clusters.Sort(delegate (List<Point> a, List<Point> b)
                    {
                        return GetAverageX(a).CompareTo(GetAverageX(b));
                    });

                    int allocationTier = firstTier + usedTierCount;
                    if (reservedTier >= 0 && allocationTier >= reservedTier)
                        allocationTier++;

                    // Giu nguyen tier cap phat cho DIM tong/layout; chi dong bo cao do
                    // cua hai DIM lo hai mep khi dieu kien hep o tren duoc thoa.
                    int drawTier = shareOppositeEndFamilyTier
                        ? firstTier
                        : allocationTier;
                    if (reservedTier >= 0 && drawTier >= reservedTier)
                        drawTier++;

                    double offset = GetSteelDimOffsetByTier(drawTier);
                    int created = 0;

                    double familyMinX;
                    double familyMaxX;
                    GetHoleRangeX(
                        family.Holes,
                        out familyMinX,
                        out familyMaxX
                    );

                    bool hasOtherHoleOnLeft = false;
                    bool hasOtherHoleOnRight = false;

                    foreach (Point h in holes)
                    {
                        if (h == null) continue;
                        if (h.X < familyMinX - tol) hasOtherHoleOnLeft = true;
                        if (h.X > familyMaxX + tol) hasOtherHoleOnRight = true;
                    }

                    bool isMiddleFamily = hasOtherHoleOnLeft && hasOtherHoleOnRight;

                    if (family.Clusters.Count >= 3 || isMiddleFamily)
                    {
                        created += CreateFrontHoleXFullChain(
                            handler,
                            view,
                            family.Holes,
                            edgeAnchors,
                            offsetAnchors,
                            offset,
                            hasFrontNotch,
                            frontNotchProfile
                        );
                    }
                    else if (family.Clusters.Count == 2)
                    {
                        created += CreateFrontEndHoleXDims(
                            handler,
                            view,
                            family.Clusters[0],
                            family.Clusters[1],
                            minX,
                            maxX,
                            maxY,
                            edgeAnchors,
                            offsetAnchors,
                            offset,
                            hasFrontNotch,
                            frontNotchProfile
                        );
                    }
                    else
                    {
                        List<Point> cluster = family.Clusters[0];
                        double centerX = GetAverageX(cluster);
                        bool useLeftEdge =
                            Math.Abs(centerX - minX) <= Math.Abs(maxX - centerX);

                        created += CreateFrontEndHoleXDims(
                            handler,
                            view,
                            useLeftEdge ? cluster : null,
                            useLeftEdge ? null : cluster,
                            minX,
                            maxX,
                            maxY,
                            edgeAnchors,
                            offsetAnchors,
                            offset,
                            hasFrontNotch,
                            frontNotchProfile
                        );
                    }

                    if (created > 0)
                    {
                        count += created;
                        usedTierCount++;
                        highestUsedTier = Math.Max(
                            highestUsedTier,
                            allocationTier
                        );
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static List<HoleColumnYPatternFamily> BuildHoleFamiliesByColumnYPattern(
            List<Point> holes,
            double tol)
        {
            List<HoleColumnYPatternFamily> result =
                new List<HoleColumnYPatternFamily>();

            try
            {
                Dictionary<double, List<Point>> holesByValue =
                    GroupTopViewHolesByDiameter(holes);

                foreach (KeyValuePair<double, List<Point>> pair in holesByValue)
                {
                    List<HoleColumnYPatternFamily> valueFamilies =
                        new List<HoleColumnYPatternFamily>();

                    List<double> xs =
                        GetUniqueCoordinatesFromHoles(pair.Value, true, tol);

                    foreach (double x in xs)
                    {
                        List<Point> column = GetHolesOnColumn(pair.Value, x, tol);
                        if (column == null || column.Count == 0)
                            continue;

                        List<double> yPattern =
                            GetUniqueCoordinatesFromHoles(column, false, tol);

                        HoleColumnYPatternFamily family = null;
                        foreach (HoleColumnYPatternFamily candidate in valueFamilies)
                        {
                            if (candidate != null &&
                                AreCoordinatePatternsSame(
                                    candidate.YPattern,
                                    yPattern,
                                    tol))
                            {
                                family = candidate;
                                break;
                            }
                        }

                        if (family == null)
                        {
                            family = new HoleColumnYPatternFamily();
                            family.HoleKey = pair.Key;
                            foreach (double y in yPattern)
                                family.YPattern.Add(y);
                            valueFamilies.Add(family);
                        }

                        foreach (Point h in column)
                        {
                            if (h != null)
                                family.Holes.Add(Clone2DWithDiameter(h));
                        }
                    }

                    foreach (HoleColumnYPatternFamily family in valueFamilies)
                    {
                        if (family == null || family.Holes.Count == 0)
                            continue;

                        family.Clusters = SplitFrontHolesIntoXClusters(
                            family.Holes,
                            FRONT_HOLE_CLUSTER_SPLIT_GAP
                        );

                        result.Add(family);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool HoleFamilySpansOuterHoles(
            HoleColumnYPatternFamily family,
            List<Point> allHoles,
            double tol)
        {
            try
            {
                if (family == null || family.Holes == null || family.Holes.Count == 0 ||
                    allHoles == null || allHoles.Count == 0)
                    return false;

                double familyMinX;
                double familyMaxX;
                double allMinX;
                double allMaxX;

                GetHoleRangeX(family.Holes, out familyMinX, out familyMaxX);
                GetHoleRangeX(allHoles, out allMinX, out allMaxX);

                return Math.Abs(familyMinX - allMinX) <= tol &&
                       Math.Abs(familyMaxX - allMaxX) <= tol;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanShareFrontOppositeEndFamilyTier(
            List<HoleColumnYPatternFamily> families,
            double minX,
            double maxX,
            double tol)
        {
            try
            {
                if (families == null || families.Count != 2)
                    return false;

                HoleColumnYPatternFamily first = families[0];
                HoleColumnYPatternFamily second = families[1];

                if (first == null || second == null ||
                    first.Clusters == null || first.Clusters.Count != 1 ||
                    second.Clusters == null || second.Clusters.Count != 1 ||
                    first.Clusters[0] == null || first.Clusters[0].Count == 0 ||
                    second.Clusters[0] == null || second.Clusters[0].Count == 0)
                    return false;

                // Chi ap dung cho hai family cung loai lo, bi tach do mau vi tri Y khac nhau.
                if (Math.Abs(first.HoleKey - second.HoleKey) > 0.001)
                    return false;

                double firstCenterX = GetAverageX(first.Clusters[0]);
                double secondCenterX = GetAverageX(second.Clusters[0]);
                bool firstUsesLeft =
                    Math.Abs(firstCenterX - minX) <= Math.Abs(maxX - firstCenterX);
                bool secondUsesLeft =
                    Math.Abs(secondCenterX - minX) <= Math.Abs(maxX - secondCenterX);

                if (firstUsesLeft == secondUsesLeft)
                    return false;

                double firstEdgeDistance = firstUsesLeft
                    ? Math.Abs(firstCenterX - minX)
                    : Math.Abs(maxX - firstCenterX);
                double secondEdgeDistance = secondUsesLeft
                    ? Math.Abs(secondCenterX - minX)
                    : Math.Abs(maxX - secondCenterX);

                return firstEdgeDistance <= FRONT_END_HOLE_ZONE + tol &&
                       secondEdgeDistance <= FRONT_END_HOLE_ZONE + tol;
            }
            catch
            {
                return false;
            }
        }

        private static void GetHoleRangeX(
            List<Point> holes,
            out double minX,
            out double maxX)
        {
            minX = 999999999.0;
            maxX = -999999999.0;

            try
            {
                if (holes == null)
                    return;

                foreach (Point h in holes)
                {
                    if (h == null) continue;
                    if (h.X < minX) minX = h.X;
                    if (h.X > maxX) maxX = h.X;
                }
            }
            catch
            {
            }
        }

        private static int CreateFrontHoleXFullChain(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double offset,
            bool hasFrontNotch,
            List<Point> frontNotchProfile)
        {
            int count = 0;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                List<Point> sorted = SortByX(holes);
                List<Point> chainHoles = new List<Point>();

                foreach (Point h in sorted)
                {
                    if (h == null) continue;

                    bool merged = false;
                    for (int i = 0; i < chainHoles.Count; i++)
                    {
                        Point old = chainHoles[i];
                        if (old == null) continue;

                        if (Math.Abs(old.X - h.X) <= TOL)
                        {
                            // Mỗi cột X lấy lỗ dưới cùng cho chân DIM ngang.
                            if (h.Y < old.Y)
                                chainHoles[i] = Clone2DWithDiameter(h);
                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                        chainHoles.Add(Clone2DWithDiameter(h));
                }

                if (chainHoles.Count == 0)
                    return count;

                chainHoles.Sort(delegate (Point a, Point b)
                {
                    return a.X.CompareTo(b.X);
                });

                Point leftAnchor = Clone2D(edgeAnchors.LeftMost);
                Point rightAnchor = Clone2D(edgeAnchors.RightMost);

                PointList dim = new PointList();
                dim.Add(Clone2D(leftAnchor));

                foreach (Point h in chainHoles)
                    dim.Add(CreateHorizontalHoleDimFootAbove(h, GetHoleDimGap(h)));

                dim.Add(Clone2D(rightAnchor));

                double realOffset = ResolveDimDistanceByAnchor4(
                    dim,
                    new Vector(0, 1, 0),
                    offsetAnchors,
                    offset);

                if (handler.CreateDimensionSet(
                    view,
                    dim,
                    new Vector(0, 1, 0),
                    realOffset) != null)
                {
                    count++;
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateFrontEndHoleXDims(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> leftHoles,
            List<Point> rightHoles,
            double minX,
            double maxX,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double offset,
            bool hasFrontNotch,
            List<Point> frontNotchProfile)
        {
            int count = 0;

            if (leftHoles != null && leftHoles.Count > 0)
            {
                // FRONT - DIM NGANG LỖ CỤM TRÁI:
                // Sửa theo yêu cầu:
                // - Nếu có nhiều cột lỗ ở cùng cụm mép trái thì tạo 1 chain nhỏ:
                //      Mép trái -> Lỗ -> Lỗ
                // - Chỉ chain đến mép gần nhất, tuyệt đối không kéo qua cụm bên kia.
                // - Không đụng DIM dọc, DIM tổng, Top/Bottom, chamfer/notch.
                List<Point> sorted = SortByX(leftHoles);
                List<Point> chainHoles = new List<Point>();

                foreach (Point h in sorted)
                {
                    if (h == null)
                        continue;

                    bool merged = false;

                    for (int i = 0; i < chainHoles.Count; i++)
                    {
                        Point old = chainHoles[i];
                        if (old == null)
                            continue;

                        if (Math.Abs(old.X - h.X) <= TOL)
                        {
                            // Nếu cùng cột X, chỉ lấy 1 lỗ đại diện cho DIM ngang.
                            // Tiêu chuẩn mới: ưu tiên lỗ dưới cùng.
                            if (h.Y < old.Y)
                                chainHoles[i] = new Point(h.X, h.Y, h.Z);

                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                        chainHoles.Add(new Point(h.X, h.Y, h.Z));
                }

                if (chainHoles.Count > 0)
                {
                    PointList leftDim = new PointList();

                    // Chỉ khi Front có rãnh: chân DIM phía mép phải bắt vào mép ngoài cạnh thực của dầm,
                    // không bắt vào mép trong của rãnh. Khi không có rãnh giữ nguyên 100% logic cũ.
                    Point leftOuterAnchor = Clone2D(edgeAnchors.LeftMost);

                    leftDim.Add(Clone2D(leftOuterAnchor));

                    foreach (Point h in chainHoles)
                    {
                        double gap = GetHoleDimGap(h);
                        leftDim.Add(CreateHorizontalHoleDimFootAbove(h, gap));
                    }

                    double leftOffset = ResolveDimDistanceByAnchor4(
                        leftDim,
                        new Vector(0, 1, 0),
                        offsetAnchors,
                        offset);

                    if (handler.CreateDimensionSet(
                        view,
                        leftDim,
                        new Vector(0, 1, 0),
                        leftOffset) != null)
                        count++;
                }
            }

            if (rightHoles != null && rightHoles.Count > 0)
            {
                // FRONT - DIM NGANG LỖ CỤM PHẢI:
                // Sửa theo yêu cầu:
                // - Nếu có nhiều cột lỗ ở cùng cụm mép phải thì tạo 1 chain nhỏ:
                //      Mép phải -> Lỗ -> Lỗ
                // - Chỉ chain đến mép gần nhất, tuyệt đối không kéo qua cụm bên kia.
                // - Không đụng DIM dọc, DIM tổng, Top/Bottom, chamfer/notch.
                List<Point> sorted = SortByX(rightHoles);
                List<Point> chainHoles = new List<Point>();

                foreach (Point h in sorted)
                {
                    if (h == null)
                        continue;

                    bool merged = false;

                    for (int i = 0; i < chainHoles.Count; i++)
                    {
                        Point old = chainHoles[i];
                        if (old == null)
                            continue;

                        if (Math.Abs(old.X - h.X) <= TOL)
                        {
                            // Nếu cùng cột X, chỉ lấy 1 lỗ đại diện cho DIM ngang.
                            // Tiêu chuẩn mới: ưu tiên lỗ dưới cùng.
                            if (h.Y < old.Y)
                                chainHoles[i] = new Point(h.X, h.Y, h.Z);

                            merged = true;
                            break;
                        }
                    }

                    if (!merged)
                        chainHoles.Add(new Point(h.X, h.Y, h.Z));
                }

                // Cụm phải chain từ mép phải đi vào trong, nên đảo thứ tự lỗ:
                // gần mép phải trước, lỗ bên trong sau.
                chainHoles.Sort(delegate (Point a, Point b)
                {
                    return b.X.CompareTo(a.X);
                });

                if (chainHoles.Count > 0)
                {
                    PointList rightDim = new PointList();

                    // Chỉ khi Front có rãnh: chân DIM phía mép phải bắt vào mép ngoài cạnh thực của dầm,
                    // không bắt vào mép trong của rãnh. Khi không có rãnh giữ nguyên 100% logic cũ.
                    Point rightOuterAnchor = Clone2D(edgeAnchors.RightMost);

                    rightDim.Add(Clone2D(rightOuterAnchor));

                    foreach (Point h in chainHoles)
                    {
                        double gap = GetHoleDimGap(h);
                        rightDim.Add(CreateHorizontalHoleDimFootAbove(h, gap));
                    }

                    double rightOffset = ResolveDimDistanceByAnchor4(
                        rightDim,
                        new Vector(0, 1, 0),
                        offsetAnchors,
                        offset);

                    if (handler.CreateDimensionSet(
                        view,
                        rightDim,
                        new Vector(0, 1, 0),
                        rightOffset) != null)
                        count++;
                }
            }

            return count;
        }


        private static Point FindFrontEndHoleXOuterAnchorForNotch(
            List<Point> profilePoints,
            List<Point> chainHoles,
            bool isLeft)
        {
            try
            {
                if (profilePoints == null || profilePoints.Count == 0 ||
                    chainHoles == null || chainHoles.Count == 0)
                    return null;

                double minHoleX = 999999999.0;
                double maxHoleX = -999999999.0;

                foreach (Point h in chainHoles)
                {
                    if (h == null)
                        continue;

                    if (h.X < minHoleX) minHoleX = h.X;
                    if (h.X > maxHoleX) maxHoleX = h.X;
                }

                if (minHoleX > 900000000.0 || maxHoleX < -900000000.0)
                    return null;

                Point best = null;

                foreach (Point p in profilePoints)
                {
                    if (p == null)
                        continue;

                    if (isLeft)
                    {
                        if (p.X >= minHoleX - TOL)
                            continue;

                        if (best == null ||
                            p.X > best.X + TOL ||
                            (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y))
                        {
                            best = p;
                        }
                    }
                    else
                    {
                        if (p.X <= maxHoleX + TOL)
                            continue;

                        if (best == null ||
                            p.X < best.X - TOL ||
                            (Math.Abs(p.X - best.X) <= TOL && p.Y > best.Y))
                        {
                            best = p;
                        }
                    }
                }

                if (best == null)
                    return null;

                return Clone2D(best);
            }
            catch
            {
                return null;
            }
        }


        private static int CreateFrontHoleYDimsByGeometry(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> holes,
            double minX,
            double maxX,
            double minY,
            double maxY,
            ChamferEdgeAnchors edgeAnchors,
            DimOffsetAnchor4 offsetAnchors,
            double beamLength,
            int leftStartTier,
            int rightStartTier,
            out int usedLeftTierCount,
            out int usedRightTierCount)
        {
            int count = 0;
            usedLeftTierCount = 0;
            usedRightTierCount = 0;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                double tol = Math.Max(2.0, TOL + 1.0);
                double topEdgeY = edgeAnchors.TopMost != null
                    ? edgeAnchors.TopMost.Y
                    : maxY;
                double bottomEdgeY = edgeAnchors.BottomMost != null
                    ? edgeAnchors.BottomMost.Y
                    : minY;
                double globalMinHoleX;
                double globalMaxHoleX;
                GetHoleRangeX(holes, out globalMinHoleX, out globalMaxHoleX);

                double middleOffset = GetSteelDimOffsetByTier(
                    1);

                List<HoleColumnYPatternFamily> families =
                    BuildHoleFamiliesByColumnYPattern(holes, tol);

                foreach (HoleColumnYPatternFamily family in families)
                {
                    if (family == null || family.Clusters == null)
                        continue;

                    foreach (List<Point> cluster in family.Clusters)
                    {
                        if (cluster == null || cluster.Count == 0)
                            continue;

                        double clusterMinX;
                        double clusterMaxX;
                        GetHoleRangeX(cluster, out clusterMinX, out clusterMaxX);

                        bool isLeftEdgeCluster =
                            Math.Abs(clusterMinX - globalMinHoleX) <= tol;
                        bool isRightEdgeCluster =
                            Math.Abs(clusterMaxX - globalMaxHoleX) <= tol;

                        double clusterCenterX = GetAverageX(cluster);

                        if (isLeftEdgeCluster && isRightEdgeCluster)
                        {
                            if (Math.Abs(clusterCenterX - minX) <=
                                Math.Abs(maxX - clusterCenterX))
                            {
                                isRightEdgeCluster = false;
                            }
                            else
                            {
                                isLeftEdgeCluster = false;
                            }
                        }

                        bool usesLeftOuterTier =
                            isLeftEdgeCluster &&
                            Math.Abs(clusterCenterX - minX) <= FRONT_END_HOLE_ZONE;
                        bool usesRightOuterTier =
                            isRightEdgeCluster &&
                            Math.Abs(maxX - clusterCenterX) <= FRONT_END_HOLE_ZONE;

                        bool useRightSide = isRightEdgeCluster;
                        List<Point> rows = BuildUniqueHoleRowsForChain(cluster, tol);
                        if (rows == null || rows.Count == 0)
                            continue;

                        rows.Sort(delegate (Point a, Point b)
                        {
                            return b.Y.CompareTo(a.Y);
                        });

                        List<Point> rowFeet = new List<Point>();

                        foreach (Point row in rows)
                        {
                            if (row == null)
                                continue;

                            // Chọn lỗ xa đường DIM nhất để đường dóng chạy hết bề ngang cụm.
                            Point refHole = FindFarthestHoleOnRowFromVerticalDim(
                                cluster,
                                row.Y,
                                tol,
                                useRightSide
                            );

                            if (refHole == null)
                                refHole = row;

                            double gap = GetHoleDimGap(refHole);
                            double footX = useRightSide
                                ? refHole.X + gap
                                : refHole.X - gap;

                            rowFeet.Add(new Point(footX, refHole.Y, 0));
                        }

                        if (rowFeet.Count == 0)
                            continue;

                        double chainFootX = rowFeet[0].X;
                        foreach (Point foot in rowFeet)
                        {
                            if (foot == null) continue;

                            if (useRightSide)
                            {
                                if (foot.X > chainFootX) chainFootX = foot.X;
                            }
                            else
                            {
                                if (foot.X < chainFootX) chainFootX = foot.X;
                            }
                        }

                        Point topAnchor;
                        Point bottomAnchor;
                        double dimOffset;

                        if (isLeftEdgeCluster)
                        {
                            topAnchor = edgeAnchors.TopLeft != null
                                ? Clone2D(edgeAnchors.TopLeft)
                                : new Point(chainFootX, topEdgeY, 0);
                            bottomAnchor = edgeAnchors.BottomLeft != null
                                ? Clone2D(edgeAnchors.BottomLeft)
                                : new Point(chainFootX, bottomEdgeY, 0);
                            dimOffset = usesLeftOuterTier
                                ? GetSteelDimOffsetByTier(
                                    leftStartTier + usedLeftTierCount)
                                : middleOffset;
                        }
                        else if (isRightEdgeCluster)
                        {
                            topAnchor = edgeAnchors.TopRight != null
                                ? Clone2D(edgeAnchors.TopRight)
                                : new Point(chainFootX, topEdgeY, 0);
                            bottomAnchor = edgeAnchors.BottomRight != null
                                ? Clone2D(edgeAnchors.BottomRight)
                                : new Point(chainFootX, bottomEdgeY, 0);
                            dimOffset = usesRightOuterTier
                                ? GetSteelDimOffsetByTier(
                                    rightStartTier + usedRightTierCount)
                                : middleOffset;
                        }
                        else
                        {
                            topAnchor = new Point(chainFootX, topEdgeY, 0);
                            bottomAnchor = new Point(chainFootX, bottomEdgeY, 0);
                            dimOffset = GetMiddleVerticalDimOffsetCoveringCluster(
                                cluster,
                                chainFootX,
                                middleOffset
                            );
                        }

                        PointList yDim = new PointList();
                        yDim.Add(Clone2D(topAnchor));

                        foreach (Point foot in rowFeet)
                            yDim.Add(Clone2D(foot));

                        yDim.Add(Clone2D(bottomAnchor));

                        if (usesLeftOuterTier || usesRightOuterTier)
                        {
                            dimOffset = ResolveDimDistanceByAnchor4(
                                yDim,
                                usesRightOuterTier
                                    ? new Vector(1, 0, 0)
                                    : new Vector(-1, 0, 0),
                                offsetAnchors,
                                dimOffset
                            );
                        }

                        if (handler.CreateDimensionSet(
                            view,
                            yDim,
                            useRightSide
                                ? new Vector(1, 0, 0)
                                : new Vector(-1, 0, 0),
                            dimOffset) != null)
                        {
                            count++;

                            if (usesLeftOuterTier)
                                usedLeftTierCount++;
                            else if (usesRightOuterTier)
                                usedRightTierCount++;
                        }
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateFrontEndHoleYDims(
                    StraightDimensionSetHandler handler,
                    View view,
                    List<Point> leftHoles,
                    List<Point> rightHoles,
                    double minX,
                    double maxX,
                    double minY,
                    double maxY,
                    ChamferEdgeAnchors edgeAnchors,
                    DimOffsetAnchor4 offsetAnchors,
                    double leftOffset,
                    double rightOffset)
        {
            int count = 0;

            if (leftHoles != null && leftHoles.Count > 0)
            {
                List<Point> sorted = SortByY(leftHoles);

                PointList yDim = new PointList();

                // Hai đầu chain DIM phải bắt đúng mép dầm thật.
                // Không dùng X dịch theo lỗ cho 2 điểm đầu/cuối nữa.
                yDim.Add(Clone2D(edgeAnchors.BottomLeft));

                // Các chân DIM tại lỗ dịch khỏi tâm đúng bằng phi lỗ.
                foreach (Point h in sorted)
                    yDim.Add(new Point(h.X - GetHoleDimGap(h), h.Y, 0));

                yDim.Add(Clone2D(edgeAnchors.TopLeft));

                double realLeftOffset = ResolveDimDistanceByAnchor4(
                    yDim,
                    new Vector(-1, 0, 0),
                    offsetAnchors,
                    leftOffset);

                if (handler.CreateDimensionSet(
                    view,
                    yDim,
                    new Vector(-1, 0, 0),
                    realLeftOffset) != null)
                    count++;
            }

            if (rightHoles != null && rightHoles.Count > 0)
            {
                List<Point> sorted = SortByY(rightHoles);

                PointList yDim = new PointList();

                // Hai đầu chain DIM phải bắt đúng mép dầm thật.
                // Không dùng X dịch theo lỗ cho 2 điểm đầu/cuối nữa.
                yDim.Add(Clone2D(edgeAnchors.BottomRight));

                // Các chân DIM tại lỗ dịch khỏi tâm đúng bằng phi lỗ.
                foreach (Point h in sorted)
                    yDim.Add(new Point(h.X + GetHoleDimGap(h), h.Y, 0));

                yDim.Add(Clone2D(edgeAnchors.TopRight));

                double realRightOffset = ResolveDimDistanceByAnchor4(
                    yDim,
                    new Vector(1, 0, 0),
                    offsetAnchors,
                    rightOffset);

                if (handler.CreateDimensionSet(
                    view,
                    yDim,
                    new Vector(1, 0, 0),
                    realRightOffset) != null)
                    count++;
            }

            return count;
        }

        private static int CreateFrontMiddleHoleXDimBelow(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> middleHoles,
            double minX,
            double maxX,
            double minY,
            ChamferEdgeAnchors edgeAnchors,
            double offset)
        {
            int count = 0;

            if (middleHoles == null || middleHoles.Count == 0)
                return count;

            List<Point> sorted = SortByX(middleHoles);

            PointList xDim = new PointList();
            xDim.Add(Clone2D(edgeAnchors.BottomLeft));

            foreach (Point h in sorted)
                xDim.Add(CreateHorizontalHoleDimFootAbove(h, GetHoleDimGap(h)));

            xDim.Add(Clone2D(edgeAnchors.BottomRight));

            // FRONT - thuật toán điểm neo cho DIM ngang lỗ phía dưới:
            // Offset dùng trực tiếp từ chân DIM thật ngoài cùng, không bù theo bounding box.
            if (handler.CreateDimensionSet(
                view,
                xDim,
                new Vector(0, -1, 0),
                offset) != null)
                count++;

            return count;
        }


        private static int CreateFrontMiddleHoleYDimChain(
            StraightDimensionSetHandler handler,
            View view,
            List<Point> middleHoles,
            double minY,
            double maxY,
            double offset)
        {
            int count = 0;

            try
            {
                if (middleHoles == null || middleHoles.Count == 0)
                    return count;

                // middleHoles có thể gồm nhiều cụm giữa.
                // Tách lại theo khoảng hở X giống rule Front hiện có để mỗi cụm có 1 DIM dọc riêng.
                List<List<Point>> clusters = SplitFrontHolesIntoXClusters(
                    middleHoles,
                    FRONT_HOLE_CLUSTER_SPLIT_GAP
                );

                foreach (List<Point> cluster in clusters)
                {
                    if (cluster == null || cluster.Count == 0)
                        continue;

                    List<Point> sortedByY = SortByY(cluster);
                    List<Point> rowFeet = new List<Point>();

                    foreach (Point h in sortedByY)
                    {
                        if (h == null)
                            continue;

                        bool merged = false;

                        for (int i = 0; i < rowFeet.Count; i++)
                        {
                            Point old = rowFeet[i];
                            if (old == null)
                                continue;

                            if (Math.Abs(old.Y - h.Y) <= TOL)
                            {
                                // Cùng hàng Y: chỉ lấy 1 chân đại diện cho DIM dọc.
                                // Vì DIM đặt bên trái cụm giữa, ưu tiên chân lỗ nằm bên trái nhất.
                                double gap = GetHoleDimGap(h);
                                double footX = h.X - gap;

                                if (footX < old.X)
                                    rowFeet[i] = new Point(footX, h.Y, 0);

                                merged = true;
                                break;
                            }
                        }

                        if (!merged)
                        {
                            double gap = GetHoleDimGap(h);
                            rowFeet.Add(new Point(h.X - gap, h.Y, 0));
                        }
                    }

                    if (rowFeet.Count == 0)
                        continue;

                    rowFeet.Sort(delegate (Point a, Point b)
                    {
                        return a.Y.CompareTo(b.Y);
                    });

                    double dimFootX = rowFeet[0].X;

                    PointList yDim = new PointList();
                    yDim.Add(new Point(dimFootX, minY, 0));

                    foreach (Point rf in rowFeet)
                        yDim.Add(Clone2D(rf));

                    yDim.Add(new Point(dimFootX, maxY, 0));

                    // FRONT - DIM dọc cụm lỗ giữa:
                    // Chain Mép dưới -> Lỗ -> Lỗ -> Mép trên, đặt về bên trái cụm.
                    if (handler.CreateDimensionSet(
                        view,
                        yDim,
                        new Vector(-1, 0, 0),
                        offset) != null)
                        count++;
                }
            }
            catch
            {
            }

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

        private static List<Point> GetVisibleFrontWebBoltCentersFromView(
            Model model,
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            List<HHoleCandidate> holes = GetHHoleCandidatesInCurrentPlane(
                model,
                view,
                minX,
                maxX,
                minY,
                maxY,
                false
            );

            HHoleClassification classified = ClassifyHHoleCandidates(holes);
            return ConvertHHoleCandidatesToDimPoints(classified.FrontCandidates);
        }

        private static void SplitFrontHolesByX(
            List<Point> holes,
            double minX,
            double maxX,
            out List<Point> leftEndHoles,
            out List<Point> rightEndHoles,
            out List<Point> middleHoles)
        {
            leftEndHoles = new List<Point>();
            rightEndHoles = new List<Point>();
            middleHoles = new List<Point>();

            if (holes == null || holes.Count == 0)
                return;

            // BẢO VỆ LOGIC CŨ:
            // Trường hợp dầm chỉ có lỗ ở 2 mép hoặc 1 bên mép thì KHÔNG tạo DIM giữa.
            // Vì đa số dầm công ty là dạng này.
            //
            // Chỉ khi phát hiện từ 3 cụm lỗ theo phương X trở lên:
            // - cụm đầu tiên  -> lỗ mép trái
            // - cụm cuối cùng -> lỗ mép phải
            // - các cụm nằm giữa -> middleHoles để tạo DIM dưới riêng.
            List<List<Point>> clusters = SplitFrontHolesIntoXClusters(
                holes,
                FRONT_HOLE_CLUSTER_SPLIT_GAP
            );

            if (clusters.Count == 0)
                return;

            if (clusters.Count == 1)
            {
                double cx = GetAverageX(clusters[0]);
                double distToLeft = Math.Abs(cx - minX);
                double distToRight = Math.Abs(maxX - cx);

                if (distToLeft <= distToRight)
                    AddPointListUnique(leftEndHoles, clusters[0]);
                else
                    AddPointListUnique(rightEndHoles, clusters[0]);

                return;
            }

            if (clusters.Count == 2)
            {
                // Chỉ có 2 cụm: coi là 2 cụm mép, giữ nguyên cách DIM cũ.
                AddPointListUnique(leftEndHoles, clusters[0]);
                AddPointListUnique(rightEndHoles, clusters[1]);
                return;
            }

            // Từ 3 cụm trở lên: có lỗ giữa thật.
            AddPointListUnique(leftEndHoles, clusters[0]);
            AddPointListUnique(rightEndHoles, clusters[clusters.Count - 1]);

            for (int i = 1; i < clusters.Count - 1; i++)
                AddPointListUnique(middleHoles, clusters[i]);
        }

        private static List<List<Point>> SplitFrontHolesIntoXClusters(
            List<Point> holes,
            double splitGap)
        {
            List<List<Point>> clusters = new List<List<Point>>();

            if (holes == null || holes.Count == 0)
                return clusters;

            List<Point> sorted = SortByX(holes);

            List<Point> current = new List<Point>();
            current.Add(Clone2DWithZ(sorted[0]));

            for (int i = 1; i < sorted.Count; i++)
            {
                Point prev = sorted[i - 1];
                Point now = sorted[i];

                if (prev == null || now == null)
                    continue;

                double gap = Math.Abs(now.X - prev.X);

                if (gap > splitGap)
                {
                    if (current.Count > 0)
                        clusters.Add(current);

                    current = new List<Point>();
                }

                current.Add(Clone2DWithZ(now));
            }

            if (current.Count > 0)
                clusters.Add(current);

            return clusters;
        }

        private static void AddPointListUnique(
            List<Point> target,
            List<Point> source)
        {
            if (target == null || source == null)
                return;

            foreach (Point p in source)
            {
                if (p == null)
                    continue;

                AddUniquePoint(target, Clone2DWithZ(p), 1.0);
            }
        }

        private static Point Clone2DWithZ(Point p)
        {
            if (p == null)
                return new Point(0, 0, 0);

            return new Point(p.X, p.Y, p.Z);
        }

        private static double GetWebThicknessFromProfile(ModelPart part)
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

                // H200x200x8x12 -> token thứ 3 là web thickness.
                if (values.Count >= 4)
                    return values[2];

                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetAverageX(List<Point> pts)
        {
            if (pts == null || pts.Count == 0)
                return 0.0;

            double sum = 0.0;
            int n = 0;

            foreach (Point p in pts)
            {
                if (p == null)
                    continue;

                sum += p.X;
                n++;
            }

            if (n == 0)
                return 0.0;

            return sum / n;
        }
        private static List<Point> SortByY(List<Point> pts)
        {
            List<Point> result = new List<Point>();

            if (pts == null)
                return result;

            foreach (Point p in pts)
            {
                if (p != null)
                    result.Add(new Point(p.X, p.Y, p.Z));
            }

            result.Sort(delegate (Point a, Point b)
            {
                return a.Y.CompareTo(b.Y);
            });

            return result;
        }

        private static List<Point> SortByX(List<Point> pts)
        {
            List<Point> result = new List<Point>();

            if (pts == null)
                return result;

            foreach (Point p in pts)
            {
                if (p != null)
                    result.Add(new Point(p.X, p.Y, p.Z));
            }

            result.Sort(delegate (Point a, Point b)
            {
                return a.X.CompareTo(b.X);
            });

            return result;
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

        private static List<View> FindManualBottomCandidateViews(
            List<View> views,
            View topView,
            View frontView,
            View smallestExactView)
        {
            List<View> result = new List<View>();

            try
            {
                if (views == null)
                    return result;

                foreach (View view in views)
                {
                    if (view == null)
                        continue;

                    // Giữ nguyên quy ước hiện tại:
                    // - views[0] là Top đã chạy thuật toán TOP
                    // - views[1] là Front đã chạy thuật toán FRONT
                    // - smallestExactView là view nhỏ nhất chỉ dùng để set Representation = Exact
                    // Các view phụ còn lại do người dùng tự cắt ra sẽ được xem là Bottom candidate.
                    if (System.Object.ReferenceEquals(view, topView) ||
                        System.Object.ReferenceEquals(view, frontView) ||
                        System.Object.ReferenceEquals(view, smallestExactView))
                        continue;

                    result.Add(view);
                }
            }
            catch
            {
            }

            return result;
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
