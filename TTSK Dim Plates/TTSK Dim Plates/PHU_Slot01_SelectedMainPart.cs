#pragma warning disable 1633

public class PHU_DumpSelectedDrawingObjectAttributes
{
    public static string Run()
    {
        Tekla.Technology.Akit.UserScript.PHU_SelectedMainPartAutoDim.Run();
        return "OK";
    }
}

namespace Tekla.Technology.Akit.UserScript
{
    public class PHU_SelectedMainPartAutoDim
    {
        public static void Run()
        {
            PHU_UnifiedDimRuntime.Begin();

            try
            {
                PHU_PlateShapeInternal.RunInternal();
            }
            finally
            {
                PHU_UnifiedDimRuntime.End();
            }
        }
    }
}
namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using Tekla.Structures;
    using Tekla.Structures.Geometry3d;
    using Tekla.Structures.Model;
    using Tekla.Structures.Drawing;

    using TSM = Tekla.Structures.Model;
    using TSD = Tekla.Structures.Drawing;
    using ModelPart = Tekla.Structures.Model.Part;
    using ModelObject = Tekla.Structures.Model.ModelObject;
    using DrawingPart = Tekla.Structures.Drawing.Part;
    using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;


    public static class PHU_UnifiedDimRuntime
    {
        private static bool _active = false;
        private static string _currentViewKey = "__GLOBAL__";
        private static Dictionary<string, int[]> _tiersByView = new Dictionary<string, int[]>();
        // Giữ đúng nền tầng Shape hiện có.
        private const double STEEL_DIM_TIER_0 = 150.0;
        private const double STEEL_DIM_TIER_STEP = 150.0;
        private const double SHORT_BEAM_DIM_SCALE_LIMIT = 2000.0;
        private const double SHORT_BEAM_DIM_SCALE = 1.0 / 2.0;

        public static bool IsActive
        {
            get { return _active; }
        }

        public static void Begin()
        {
            _active = true;
            _currentViewKey = "__GLOBAL__";
            _tiersByView.Clear();
        }

        public static void End()
        {
            _active = false;
            _currentViewKey = "__GLOBAL__";
            _tiersByView.Clear();
        }

        // Tầng độc lập theo từng VIEW + từng hướng.
        // Lý do: TOP/BOTTOM/LEFT/RIGHT của Front view không được bị Plate ở Top/Bottom view đẩy tầng.
        public static void SetCurrentView(TSD.View view)
        {
            _currentViewKey = GetViewKey(view);
            EnsureCurrentView();
        }

        private static string GetViewKey(TSD.View view)
        {
            if (view == null)
                return "__GLOBAL__";

            // Không dùng GetHashCode() vì cùng một Tekla View có thể được lấy lại thành object khác
            // giữa bước Plate và Shape, làm key tầng khác nhau và mất thông tin tầng đã chiếm.
            string idText = "";

            string originText = "";
            try
            {
                if (view.Origin != null)
                    originText = Math.Round(view.Origin.X, 3).ToString() + ":" + Math.Round(view.Origin.Y, 3).ToString();
            }
            catch
            {
            }

            string nameText = "";
            try
            {
                if (!string.IsNullOrEmpty(view.Name))
                    nameText = view.Name.Trim();
            }
            catch
            {
            }

            string key = idText + "|" + nameText + "|" + originText;
            if (string.IsNullOrEmpty(key.Trim('|', ' ')))
                key = "__GLOBAL__";

            return key;
        }

        private static int[] EnsureCurrentView()
        {
            string key = string.IsNullOrEmpty(_currentViewKey) ? "__GLOBAL__" : _currentViewKey;

            int[] tiers;
            if (!_tiersByView.TryGetValue(key, out tiers) || tiers == null || tiers.Length < 4)
            {
                tiers = new int[] { 0, 0, 0, 0 }; // 0=Top, 1=Bottom, 2=Left, 3=Right
                _tiersByView[key] = tiers;
            }

            return tiers;
        }

        private static double GetDimScaleByBeamLength(double beamLength)
        {
            if (beamLength > 0.0 && beamLength < SHORT_BEAM_DIM_SCALE_LIMIT)
                return SHORT_BEAM_DIM_SCALE;

            return 1.0;
        }

        private static double OffsetByTier(int tier, double beamLength)
        {
            double scale = GetDimScaleByBeamLength(beamLength);
            double baseOffset = STEEL_DIM_TIER_0 * scale;
            double stepOffset = STEEL_DIM_TIER_STEP * scale;

            if (tier <= 0)
                return baseOffset;

            return baseOffset + tier * stepOffset;
        }

        public static double PeekTop(double beamLength) { return OffsetByTier(EnsureCurrentView()[0], beamLength); }
        public static double PeekBottom(double beamLength) { return OffsetByTier(EnsureCurrentView()[1], beamLength); }
        public static double PeekLeft(double beamLength) { return OffsetByTier(EnsureCurrentView()[2], beamLength); }
        public static double PeekRight(double beamLength) { return OffsetByTier(EnsureCurrentView()[3], beamLength); }

        public static void CommitTop() { if (_active) EnsureCurrentView()[0]++; }
        public static void CommitBottom() { if (_active) EnsureCurrentView()[1]++; }
        public static void CommitLeft() { if (_active) EnsureCurrentView()[2]++; }
        public static void CommitRight() { if (_active) EnsureCurrentView()[3]++; }

        // Shape chạy sau Plate: trả base độc lập theo từng VIEW + từng hướng.
        // Mỗi view/hướng luôn bắt đầu từ tầng 0 nếu view/hướng đó chưa có dim chiếm.
        // Giữ lại để không phá nếu còn chỗ gọi cũ; không dùng cho Shape nữa.
    }

    // Slot 02 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot02.Run()
    public class PHU_PlateShapeInternal
    {
        private const double TOL = 1.0;

        // Tầng DIM độc lập theo từng hướng của plate.
        // Hướng ngang chưa có dim nào chiếm -> dùng tầng 1 = 100.
        private const double PLATE_HORIZONTAL_DIM_TIER_1 = 100.0;
        private const double PLATE_VERTICAL_DIM_TIER_1 = 100.0;
        private const double PLATE_VERTICAL_DIM_TIER_2 = 200.0;

        // Slot02: dim ngang dầm dùng tầng riêng để không chồng với dim ngang plate.
        private const double BEAM_HORIZONTAL_DIM_TIER_2 = 200.0;

        // Nếu khoảng cách từ tâm lỗ plate tới mép dầm gần nhất nhỏ hơn giá trị này,
        // dim dọc phải bắt vào điểm mép thật của dầm và offset tính từ mép thật đó.
        private const double BEAM_EDGE_TO_HOLE_NEAR_LIMIT = 400.0;

        private const double BOUND_TOL = 20.0;
        private const double UNIQUE_HOLE_TOL = 2.0;
        private const double MULTI_HOLE_SELECTION_TOL = 2.0;

        public static void RunInternal()
        {
            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                Msg("DrawingHandler chưa kết nối.");
                return;
            }

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                Msg("Không có active drawing.");
                return;
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                Msg("Model chưa kết nối.");
                return;
            }

            List<DrawingPart> selectedParts = GetSelectedDrawingParts(dh);
            if (selectedParts.Count < 2)
            {
                Msg("Slot 02: Hãy chọn ít nhất 2 part trong drawing: 1 dầm/thép hình + 1 hoặc nhiều plate.");
                return;
            }

            List<ModelPart> plates = new List<ModelPart>();
            List<DrawingPart> plateDrawingParts = new List<DrawingPart>();
            ModelPart beam = null;
            DrawingPart beamDrawingPart = null;
            int beamCount = 0;

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart dp = selectedParts[i];
                ModelPart mp = SelectModelPart(model, dp);

                if (mp == null)
                    continue;

                if (IsPlatePart(mp))
                {
                    plates.Add(mp);
                    plateDrawingParts.Add(dp);
                }
                else
                {
                    beam = mp;
                    beamDrawingPart = dp;
                    beamCount++;
                }
            }

            if (beamCount != 1 || beam == null || plates.Count == 0)
            {
                Msg("Slot 02: Hãy chọn đúng 1 dầm/thép hình và 1 hoặc nhiều plate.");
                return;
            }

            // Ưu tiên đúng view mà user đang click chọn các part.
            TSD.View view = null;
            if (plateDrawingParts.Count > 0)
                view = TryGetSelectedPartsView(plateDrawingParts[0], beamDrawingPart);

            if (view == null)
                view = FindViewContainingBothParts(drawing, plates[0].Identifier, beam.Identifier);

            if (view == null)
            {
                Msg("Slot 02: Không tìm thấy view chứa đồng thời plate và dầm đã chọn.");
                return;
            }

            int created = CreatePlatesToBeamDims(model, view, plates, beam);

            try { drawing.CommitChanges(); } catch { }

            Msg("Slot 02 DONE. DIM đã tạo: " + created.ToString());
        }


        private static int CreatePlatesToBeamDims(
            TSM.Model model,
            TSD.View view,
            List<ModelPart> plates,
            ModelPart beam)
        {
            int count = 0;

            if (plates == null || plates.Count == 0 || beam == null)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                try { if (PHU_UnifiedDimRuntime.IsActive) PHU_UnifiedDimRuntime.SetCurrentView(view); } catch { }
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Solid beamSolid = beam.GetSolid();
                Point beamMin = beamSolid.MinimumPoint;
                Point beamMax = beamSolid.MaximumPoint;
                List<Point> beamPolygon = GetFrontSectionPolygon(beamSolid, beamMin, beamMax);

                double beamMinX = Math.Min(beamMin.X, beamMax.X);
                double beamMaxX = Math.Max(beamMin.X, beamMax.X);
                double beamMinY = Math.Min(beamMin.Y, beamMax.Y);
                double beamMaxY = Math.Max(beamMin.Y, beamMax.Y);

                if (beamPolygon != null && beamPolygon.Count >= 2)
                    GetMinMax2D(beamPolygon, out beamMinX, out beamMaxX, out beamMinY, out beamMaxY);

                double beamCenterY = (beamMinY + beamMaxY) / 2.0;
                double unifiedBeamLength = Math.Abs(beamMaxX - beamMinX);

                StraightDimensionSetHandler handler = new StraightDimensionSetHandler();

                List<Point> allPlateHolesForBeamDim = new List<Point>();
                List<Point> topNoHolePlateEdgesForBeamChain = new List<Point>();
                List<Point> bottomNoHolePlateEdgesForBeamChain = new List<Point>();

                double allPlateMinY = 999999999.0;
                double allPlateMaxY = -999999999.0;

                // UNIFIED V8:
                // Dim ngang nội bộ của các Plate cùng phía dùng CHUNG 1 tầng.
                // Ví dụ 2 plate phía trên ở hai đầu dầm: cả hai cùng TOP tier hiện tại,
                // sau khi xử lý xong toàn bộ plate mới CommitTop() đúng 1 lần.
                // Không đụng điểm bắt dim / thuật toán lấy mép plate.
                double sharedPlateTopHorizontalTier = PLATE_HORIZONTAL_DIM_TIER_1;
                double sharedPlateBottomHorizontalTier = PLATE_HORIZONTAL_DIM_TIER_1;
                bool sharedPlateTopHorizontalTierReady = false;
                bool sharedPlateBottomHorizontalTierReady = false;
                bool sharedPlateTopHorizontalTierUsed = false;
                bool sharedPlateBottomHorizontalTierUsed = false;

                for (int pIndex = 0; pIndex < plates.Count; pIndex++)
                {
                    ModelPart plate = plates[pIndex];
                    if (plate == null)
                        continue;

                    Solid plateSolid = plate.GetSolid();
                    Point plateMin = plateSolid.MinimumPoint;
                    Point plateMax = plateSolid.MaximumPoint;

                    List<Point> platePolygon = GetFrontSectionPolygon(plateSolid, plateMin, plateMax);

                    double plateMinX = Math.Min(plateMin.X, plateMax.X);
                    double plateMaxX = Math.Max(plateMin.X, plateMax.X);
                    double plateMinY = Math.Min(plateMin.Y, plateMax.Y);
                    double plateMaxY = Math.Max(plateMin.Y, plateMax.Y);

                    if (platePolygon != null && platePolygon.Count >= 2)
                        GetMinMax2D(platePolygon, out plateMinX, out plateMaxX, out plateMinY, out plateMaxY);

                    if (plateMinY < allPlateMinY) allPlateMinY = plateMinY;
                    if (plateMaxY > allPlateMaxY) allPlateMaxY = plateMaxY;

                    List<Point> holes = GetPlateHoleCentersFromView(
                        model,
                        view,
                        plate,
                        plateMinX,
                        plateMaxX,
                        plateMinY,
                        plateMaxY);

                    if (holes.Count == 0)
                    {
                        double plateCenterY = (plateMinY + plateMaxY) / 2.0;
                        Vector horizontalDimDirection = plateCenterY >= beamCenterY
                            ? new Vector(0, 1, 0)
                            : new Vector(0, -1, 0);
                        bool preferTopPlateAnchor = horizontalDimDirection.Y >= 0.0;

                        Point hLeft = GetRealPlateSideAnchorForHorizontalDim(
                            platePolygon,
                            plateMinX,
                            true,
                            preferTopPlateAnchor);

                        Point hRight = GetRealPlateSideAnchorForHorizontalDim(
                            platePolygon,
                            plateMaxX,
                            false,
                            preferTopPlateAnchor);

                        double fallbackHorizontalY = preferTopPlateAnchor
                            ? plateMaxY
                            : plateMinY;

                        if (hLeft == null)
                            hLeft = new Point(plateMinX, fallbackHorizontalY, 0);

                        if (hRight == null)
                            hRight = new Point(plateMaxX, fallbackHorizontalY, 0);

                        List<Point> noHolePlateEdges = preferTopPlateAnchor
                            ? topNoHolePlateEdgesForBeamChain
                            : bottomNoHolePlateEdgesForBeamChain;

                        AddUniquePoint2D(noHolePlateEdges, hLeft, UNIQUE_HOLE_TOL);
                        AddUniquePoint2D(noHolePlateEdges, hRight, UNIQUE_HOLE_TOL);

                        Point plateOuterPoint = GetRealPlateOuterPointForNoHoleVerticalDim(
                            platePolygon,
                            plateMinX,
                            plateMinY,
                            plateMaxY,
                            beamCenterY);

                        if (plateOuterPoint != null)
                        {
                            Point beamCenterPoint = new Point(
                                plateOuterPoint.X,
                                beamCenterY,
                                0);

                            double verticalTier = PLATE_VERTICAL_DIM_TIER_1;

                            double verticalDistance = GetLeftDistanceByFeet(
                                new Point[] { plateOuterPoint, beamCenterPoint },
                                plateMinX,
                                verticalTier);

                            if (CreateDimChain(
                                handler,
                                view,
                                new Point[] { plateOuterPoint, beamCenterPoint },
                                new Vector(-1, 0, 0),
                                verticalDistance))
                            {
                                count++;
                            }
                        }

                        continue;
                    }

                    // Plate có đúng 1 lỗ giữ nguyên thuật toán cũ.
                    // Plate có nhiều lỗ chỉ chọn 1 lỗ đại diện ở phía liên kết với dầm.
                    if (holes.Count > 1)
                    {
                        Point representativeHole = SelectRepresentativePlateHole(
                            holes,
                            platePolygon,
                            beamPolygon,
                            plateMinX,
                            plateMaxX,
                            plateMinY,
                            plateMaxY,
                            beamMinX,
                            beamMaxX,
                            beamMinY,
                            beamMaxY);

                        if (representativeHole == null)
                            representativeHole = holes[0];

                        holes.Clear();
                        holes.Add(representativeHole);
                    }

                    foreach (Point hole in holes)
                    {
                        if (hole == null)
                            continue;

                        AddUniquePoint2D(allPlateHolesForBeamDim, new Point(hole.X, hole.Y, 0), UNIQUE_HOLE_TOL);

                        // =========================
                        // PLATE - PHƯƠNG NGANG
                        // GIỮ NGUYÊN thuật toán hiện tại của từng plate.
                        // =========================
                        Vector horizontalDimDirection =
                            hole.Y >= beamCenterY
                            ? new Vector(0, 1, 0)
                            : new Vector(0, -1, 0);

                        bool preferTopPlateAnchor = horizontalDimDirection.Y >= 0.0;

                        Point hLeft = GetRealPlateSideAnchorForHorizontalDim(
                            platePolygon,
                            plateMinX,
                            true,
                            preferTopPlateAnchor);

                        Point hRight = GetRealPlateSideAnchorForHorizontalDim(
                            platePolygon,
                            plateMaxX,
                            false,
                            preferTopPlateAnchor);

                        if (hLeft == null)
                            hLeft = new Point(plateMinX, hole.Y, 0);

                        if (hRight == null)
                            hRight = new Point(plateMaxX, hole.Y, 0);

                        Point hHole = new Point(hole.X, hole.Y, 0);

                        double horizontalTier = PLATE_HORIZONTAL_DIM_TIER_1;
                        bool horizontalUnifiedTop = horizontalDimDirection.Y >= 0.0;
                        if (PHU_UnifiedDimRuntime.IsActive)
                        {
                            if (horizontalUnifiedTop)
                            {
                                if (!sharedPlateTopHorizontalTierReady)
                                {
                                    sharedPlateTopHorizontalTier = PHU_UnifiedDimRuntime.PeekTop(unifiedBeamLength);
                                    sharedPlateTopHorizontalTierReady = true;
                                }

                                horizontalTier = sharedPlateTopHorizontalTier;
                            }
                            else
                            {
                                if (!sharedPlateBottomHorizontalTierReady)
                                {
                                    sharedPlateBottomHorizontalTier = PHU_UnifiedDimRuntime.PeekBottom(unifiedBeamLength);
                                    sharedPlateBottomHorizontalTierReady = true;
                                }

                                horizontalTier = sharedPlateBottomHorizontalTier;
                            }
                        }

                        double horizontalDistance = GetOffsetFromPlateOuterBoundary(
                            horizontalDimDirection,
                            hole.X,
                            hole.Y,
                            plateMinX,
                            plateMaxX,
                            plateMinY,
                            plateMaxY,
                            horizontalTier);

                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[] { hLeft, hHole, hRight },
                            horizontalDimDirection,
                            horizontalDistance))
                        {
                            count++;
                            if (PHU_UnifiedDimRuntime.IsActive)
                            {
                                if (horizontalUnifiedTop) sharedPlateTopHorizontalTierUsed = true;
                                else sharedPlateBottomHorizontalTierUsed = true;
                            }
                        }

                        // =========================
                        // PLATE - PHƯƠNG DỌC TẦNG 1/2
                        // Gần mép trái dầm: đẩy trái theo mép thật dầm.
                        // Gần mép phải dầm: đẩy phải theo mép thật dầm.
                        // Plate ở giữa: đẩy trái theo mép thật plate.
                        // =========================
                        double beamEdgeY = GetNearestBeamEdgeY(hole.Y, beamMinY, beamMaxY);

                        double distToBeamLeft = Math.Abs(hole.X - beamMinX);
                        double distToBeamRight = Math.Abs(beamMaxX - hole.X);

                        bool nearLeftBeamEdge = distToBeamLeft < BEAM_EDGE_TO_HOLE_NEAR_LIMIT;
                        bool nearRightBeamEdge = distToBeamRight < BEAM_EDGE_TO_HOLE_NEAR_LIMIT;
                        bool useBeamOutsideEdge = nearLeftBeamEdge || nearRightBeamEdge;
                        bool useLeftBeamEdge = distToBeamLeft <= distToBeamRight;

                        Vector verticalDimDirection = new Vector(-1, 0, 0);
                        Point verticalHolePoint = new Point(hole.X, hole.Y, 0);
                        Point beamEdgePointForVertical = new Point(hole.X, beamEdgeY, 0);
                        Point beamCenterPointForVertical = new Point(hole.X, beamCenterY, 0);

                        if (useBeamOutsideEdge)
                        {
                            double beamSideX = useLeftBeamEdge ? beamMinX : beamMaxX;

                            Point realBeamEdgePoint = GetRealBeamSidePointNearY(
                                beamPolygon,
                                beamSideX,
                                useLeftBeamEdge,
                                beamEdgeY);

                            if (realBeamEdgePoint == null)
                                realBeamEdgePoint = new Point(beamSideX, beamEdgeY, 0);

                            beamEdgePointForVertical = realBeamEdgePoint;
                            beamCenterPointForVertical = new Point(realBeamEdgePoint.X, beamCenterY, 0);

                            verticalDimDirection = useLeftBeamEdge
                                ? new Vector(-1, 0, 0)
                                : new Vector(1, 0, 0);
                        }

                        double verticalTier1 = PLATE_VERTICAL_DIM_TIER_1;

                        double verticalDistance1 = useBeamOutsideEdge
                            ? (
                                useLeftBeamEdge
                                ? GetLeftDistanceByFeet(
                                    new Point[] { beamEdgePointForVertical, verticalHolePoint },
                                    beamEdgePointForVertical.X,
                                    verticalTier1)
                                : GetRightDistanceByFeet(
                                    new Point[] { beamEdgePointForVertical, verticalHolePoint },
                                    beamEdgePointForVertical.X,
                                    verticalTier1)
                              )
                            : GetLeftDistanceByFeet(
                                new Point[] { beamEdgePointForVertical, verticalHolePoint },
                                plateMinX,
                                verticalTier1);

                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[]
                            {
                                beamEdgePointForVertical,
                                verticalHolePoint
                            },
                            verticalDimDirection,
                            verticalDistance1))
                        {
                            count++;
                        }

                        double verticalTier2 = PLATE_VERTICAL_DIM_TIER_2;

                        double verticalDistance2 = useBeamOutsideEdge
                            ? (
                                useLeftBeamEdge
                                ? GetLeftDistanceByFeet(
                                    new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                                    beamEdgePointForVertical.X,
                                    verticalTier2)
                                : GetRightDistanceByFeet(
                                    new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                                    beamEdgePointForVertical.X,
                                    verticalTier2)
                              )
                            : GetLeftDistanceByFeet(
                                new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                                plateMinX,
                                verticalTier2);

                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[]
                            {
                                beamCenterPointForVertical,
                                verticalHolePoint
                            },
                            verticalDimDirection,
                            verticalDistance2))
                        {
                            count++;
                        }
                    }
                }

                // UNIFIED V8:
                // Commit tầng dim ngang nội bộ Plate đúng 1 lần cho mỗi phía.
                // Nhờ vậy các plate cùng phía dùng cùng tầng, còn Shape/chain sau đó vẫn biết tầng này đã bị chiếm.
                if (PHU_UnifiedDimRuntime.IsActive)
                {
                    if (sharedPlateTopHorizontalTierUsed)
                        PHU_UnifiedDimRuntime.CommitTop();

                    if (sharedPlateBottomHorizontalTierUsed)
                        PHU_UnifiedDimRuntime.CommitBottom();
                }

                // =========================
                // DẦM - PHƯƠNG NGANG
                // Chain chung: Mép dầm -> lỗ -> lỗ -> mép dầm.
                // Lấy lỗ của tất cả plate đã chọn.
                // FIX V4: chia chain theo phía trên/dưới tâm dầm để chân ngoài bắt đúng mép trên/dưới dầm.
                // Không đụng dim ngang/dọc nội bộ của từng plate.
                // =========================
                if (allPlateHolesForBeamDim.Count > 0)
                {
                    List<Point> topBeamChainHoles = new List<Point>();
                    List<Point> bottomBeamChainHoles = new List<Point>();

                    for (int i = 0; i < allPlateHolesForBeamDim.Count; i++)
                    {
                        Point hp = allPlateHolesForBeamDim[i];
                        if (hp == null)
                            continue;

                        if (hp.Y >= beamCenterY)
                            topBeamChainHoles.Add(hp);
                        else
                            bottomBeamChainHoles.Add(hp);
                    }

                    count += CreateBeamHorizontalChainForPlateHoles(
                        handler,
                        view,
                        beamPolygon,
                        topBeamChainHoles,
                        true,
                        beamMinX,
                        beamMaxX,
                        beamMinY,
                        beamMaxY,
                        allPlateMinY,
                        allPlateMaxY,
                        unifiedBeamLength);

                    count += CreateBeamHorizontalChainForPlateHoles(
                        handler,
                        view,
                        beamPolygon,
                        bottomBeamChainHoles,
                        false,
                        beamMinX,
                        beamMaxX,
                        beamMinY,
                        beamMaxY,
                        allPlateMinY,
                        allPlateMaxY,
                        unifiedBeamLength);
                }

                count += CreateBeamHorizontalChainForNoHolePlateEdges(
                    handler,
                    view,
                    beamPolygon,
                    topNoHolePlateEdgesForBeamChain,
                    true,
                    beamMinX,
                    beamMaxX,
                    beamMinY,
                    beamMaxY,
                    allPlateMinY,
                    allPlateMaxY,
                    unifiedBeamLength);

                count += CreateBeamHorizontalChainForNoHolePlateEdges(
                    handler,
                    view,
                    beamPolygon,
                    bottomNoHolePlateEdgesForBeamChain,
                    false,
                    beamMinX,
                    beamMaxX,
                    beamMinY,
                    beamMaxY,
                    allPlateMinY,
                    allPlateMaxY,
                    unifiedBeamLength);
            }
            catch (Exception ex)
            {
                Msg("Slot 02 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static int CreateBeamHorizontalChainForNoHolePlateEdges(
            StraightDimensionSetHandler handler,
            TSD.View view,
            List<Point> beamPolygon,
            List<Point> plateEdges,
            bool useTopSide,
            double beamMinX,
            double beamMaxX,
            double beamMinY,
            double beamMaxY,
            double allPlateMinY,
            double allPlateMaxY,
            double unifiedBeamLength)
        {
            int count = 0;

            try
            {
                if (handler == null || view == null ||
                    plateEdges == null || plateEdges.Count == 0)
                {
                    return count;
                }

                plateEdges.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });

                Vector direction = useTopSide
                    ? new Vector(0, 1, 0)
                    : new Vector(0, -1, 0);

                double targetY = useTopSide ? beamMaxY : beamMinY;
                Point beamLeftPoint;
                Point beamRightPoint;

                if (!TryGetBeamHorizontalRealEdgePoints(
                    beamPolygon,
                    targetY,
                    beamMinX,
                    beamMaxX,
                    beamMinY,
                    beamMaxY,
                    out beamLeftPoint,
                    out beamRightPoint))
                {
                    beamLeftPoint = new Point(beamMinX, targetY, 0);
                    beamRightPoint = new Point(beamMaxX, targetY, 0);
                }

                List<Point> chain = new List<Point>();
                chain.Add(beamLeftPoint);

                double lastX = beamLeftPoint.X;
                for (int i = 0; i < plateEdges.Count; i++)
                {
                    Point edge = plateEdges[i];
                    if (edge == null || Math.Abs(edge.X - lastX) <= 0.5)
                        continue;

                    if (Math.Abs(edge.X - beamRightPoint.X) <= 0.5)
                        continue;

                    chain.Add(new Point(edge.X, edge.Y, 0));
                    lastX = edge.X;
                }

                if (Math.Abs(beamRightPoint.X - lastX) > 0.5)
                    chain.Add(beamRightPoint);

                double tier = BEAM_HORIZONTAL_DIM_TIER_2;
                if (PHU_UnifiedDimRuntime.IsActive)
                    tier = useTopSide
                        ? PHU_UnifiedDimRuntime.PeekTop(unifiedBeamLength)
                        : PHU_UnifiedDimRuntime.PeekBottom(unifiedBeamLength);

                double distance = GetHorizontalDistanceFromOuterBoundary(
                    direction,
                    beamLeftPoint,
                    allPlateMinY,
                    allPlateMaxY,
                    beamMinY,
                    beamMaxY,
                    tier);

                if (CreateDimChain(
                    handler,
                    view,
                    chain.ToArray(),
                    direction,
                    distance))
                {
                    count++;
                    if (PHU_UnifiedDimRuntime.IsActive)
                    {
                        if (useTopSide)
                            PHU_UnifiedDimRuntime.CommitTop();
                        else
                            PHU_UnifiedDimRuntime.CommitBottom();
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreateBeamHorizontalChainForPlateHoles(
            StraightDimensionSetHandler handler,
            TSD.View view,
            List<Point> beamPolygon,
            List<Point> holes,
            bool useTopSide,
            double beamMinX,
            double beamMaxX,
            double beamMinY,
            double beamMaxY,
            double allPlateMinY,
            double allPlateMaxY,
            double unifiedBeamLength)
        {
            int count = 0;

            try
            {
                if (handler == null || view == null || holes == null || holes.Count == 0)
                    return count;

                holes.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });

                Vector beamHorizontalDirection = useTopSide
                    ? new Vector(0, 1, 0)
                    : new Vector(0, -1, 0);

                double targetY = useTopSide ? beamMaxY : beamMinY;

                Point beamLeftPoint;
                Point beamRightPoint;
                if (!TryGetBeamHorizontalRealEdgePoints(
                    beamPolygon,
                    targetY,
                    beamMinX,
                    beamMaxX,
                    beamMinY,
                    beamMaxY,
                    out beamLeftPoint,
                    out beamRightPoint))
                {
                    beamLeftPoint = new Point(beamMinX, targetY, 0);
                    beamRightPoint = new Point(beamMaxX, targetY, 0);
                }

                List<Point> chain = new List<Point>();
                chain.Add(beamLeftPoint);
                for (int i = 0; i < holes.Count; i++)
                    chain.Add(new Point(holes[i].X, holes[i].Y, 0));
                chain.Add(beamRightPoint);

                if (allPlateMinY > allPlateMaxY)
                {
                    allPlateMinY = beamMinY;
                    allPlateMaxY = beamMaxY;
                }

                double beamHorizontalTier = BEAM_HORIZONTAL_DIM_TIER_2;
                if (PHU_UnifiedDimRuntime.IsActive)
                    beamHorizontalTier = useTopSide
                        ? PHU_UnifiedDimRuntime.PeekTop(unifiedBeamLength)
                        : PHU_UnifiedDimRuntime.PeekBottom(unifiedBeamLength);

                double beamHorizontalDistance = GetHorizontalDistanceFromOuterBoundary(
                    beamHorizontalDirection,
                    beamLeftPoint,
                    allPlateMinY,
                    allPlateMaxY,
                    allPlateMinY,
                    allPlateMaxY,
                    beamHorizontalTier);

                if (CreateDimChain(
                    handler,
                    view,
                    chain.ToArray(),
                    beamHorizontalDirection,
                    beamHorizontalDistance))
                {
                    count++;
                    if (PHU_UnifiedDimRuntime.IsActive)
                    {
                        if (useTopSide) PHU_UnifiedDimRuntime.CommitTop();
                        else PHU_UnifiedDimRuntime.CommitBottom();
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        private static int CreatePlateToBeamDims(
            TSM.Model model,
            TSD.View view,
            ModelPart plate,
            ModelPart beam)
        {
            int count = 0;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Solid plateSolid = plate.GetSolid();
                Solid beamSolid = beam.GetSolid();

                Point plateMin = plateSolid.MinimumPoint;
                Point plateMax = plateSolid.MaximumPoint;
                Point beamMin = beamSolid.MinimumPoint;
                Point beamMax = beamSolid.MaximumPoint;

                List<Point> platePolygon = GetFrontSectionPolygon(plateSolid, plateMin, plateMax);
                List<Point> beamPolygon = GetFrontSectionPolygon(beamSolid, beamMin, beamMax);

                double plateMinX = Math.Min(plateMin.X, plateMax.X);
                double plateMaxX = Math.Max(plateMin.X, plateMax.X);
                double plateMinY = Math.Min(plateMin.Y, plateMax.Y);
                double plateMaxY = Math.Max(plateMin.Y, plateMax.Y);

                if (platePolygon != null && platePolygon.Count >= 2)
                    GetMinMax2D(platePolygon, out plateMinX, out plateMaxX, out plateMinY, out plateMaxY);

                double beamMinX = Math.Min(beamMin.X, beamMax.X);
                double beamMaxX = Math.Max(beamMin.X, beamMax.X);
                double beamMinY = Math.Min(beamMin.Y, beamMax.Y);
                double beamMaxY = Math.Max(beamMin.Y, beamMax.Y);

                if (beamPolygon != null && beamPolygon.Count >= 2)
                    GetMinMax2D(beamPolygon, out beamMinX, out beamMaxX, out beamMinY, out beamMaxY);

                double beamCenterY = (beamMinY + beamMaxY) / 2.0;

                List<Point> holes = GetPlateHoleCentersFromView(
                    model,
                    view,
                    plate,
                    plateMinX,
                    plateMaxX,
                    plateMinY,
                    plateMaxY);

                if (holes.Count == 0)
                    return 0;

                StraightDimensionSetHandler handler = new StraightDimensionSetHandler();

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    // =========================
                    // PLATE - PHƯƠNG NGANG
                    // Mép plate trái -> tâm lỗ plate -> mép plate phải
                    // Hướng ngang có tầng riêng: tầng 1 = 100.
                    // =========================
                    Vector horizontalDimDirection =
                        hole.Y >= beamCenterY
                        ? new Vector(0, 1, 0)
                        : new Vector(0, -1, 0);

                    // FIX MÉP THỰC PLATE:
                    // Không ép 3 chân DIM nằm thẳng hàng theo Y tâm lỗ nữa.
                    // Hai chân ngoài phải bắt vào điểm thật trên cạnh trái/phải của plate
                    // theo phía đặt DIM. Ví dụ cạnh phải xiên thì chân ngoài phải bắt về
                    // đúng vertex/cạnh thật, không lấy điểm giao ảo tại Y tâm lỗ.
                    bool preferTopPlateAnchor = horizontalDimDirection.Y >= 0.0;

                    Point hLeft = GetRealPlateSideAnchorForHorizontalDim(
                        platePolygon,
                        plateMinX,
                        true,
                        preferTopPlateAnchor);

                    Point hRight = GetRealPlateSideAnchorForHorizontalDim(
                        platePolygon,
                        plateMaxX,
                        false,
                        preferTopPlateAnchor);

                    if (hLeft == null)
                        hLeft = new Point(plateMinX, hole.Y, 0);

                    if (hRight == null)
                        hRight = new Point(plateMaxX, hole.Y, 0);

                    Point hHole = new Point(hole.X, hole.Y, 0);

                    double horizontalDistance = GetOffsetFromPlateOuterBoundary(
                        horizontalDimDirection,
                        hole.X,
                        hole.Y,
                        plateMinX,
                        plateMaxX,
                        plateMinY,
                        plateMaxY,
                        PLATE_HORIZONTAL_DIM_TIER_1);

                    if (CreateDimChain(
                        handler,
                        view,
                        new Point[] { hLeft, hHole, hRight },
                        horizontalDimDirection,
                        horizontalDistance))
                    {
                        count++;
                    }

                    // =========================
                    // DẦM - PHƯƠNG NGANG
                    // Mép dầm thật -> tâm lỗ plate -> mép dầm thật.
                    // Chỉ dùng lỗ của plate, không lấy lỗ của dầm.
                    // =========================
                    Point beamLeftPoint;
                    Point beamRightPoint;
                    if (TryGetBeamHorizontalRealEdgePoints(
                        beamPolygon,
                        hole.Y,
                        beamMinX,
                        beamMaxX,
                        beamMinY,
                        beamMaxY,
                        out beamLeftPoint,
                        out beamRightPoint))
                    {
                        // DIM ngang dầm dùng tầng riêng của hướng ngang.
                        // Plate đã chiếm tầng 1, nên dầm lấy tầng 2 tính từ biên ngoài xa nhất
                        // của cụm plate + dầm theo hướng đặt DIM, tránh chồng nhau.
                        double beamHorizontalDistance = GetHorizontalDistanceFromOuterBoundary(
                            horizontalDimDirection,
                            beamLeftPoint,
                            plateMinY,
                            plateMaxY,
                            beamMinY,
                            beamMaxY,
                            BEAM_HORIZONTAL_DIM_TIER_2);

                        if (CreateDimChain(
                            handler,
                            view,
                            new Point[]
                            {
                                beamLeftPoint,
                                new Point(hole.X, hole.Y, 0),
                                beamRightPoint
                            },
                            horizontalDimDirection,
                            beamHorizontalDistance))
                        {
                            count++;
                        }
                    }

                    // =========================
                    // PLATE - PHƯƠNG DỌC TẦNG 1
                    // Tâm lỗ plate -> mép dầm gần tâm lỗ nhất.
                    //
                    // PORT RULE THÉP L:
                    // Xét khoảng cách theo phương X từ tâm lỗ tới mép trái/phải thật của dầm.
                    // Nếu lỗ nằm gần mép dầm (< BEAM_EDGE_TO_HOLE_NEAR_LIMIT = 300),
                    // DIM dọc phải đưa ra ngoài đúng mép dầm đó, offset tính từ mép dầm ra tầng.
                    // Không xét sai theo khoảng cách Y nữa.
                    // =========================
                    double beamEdgeY = GetNearestBeamEdgeY(hole.Y, beamMinY, beamMaxY);

                    double distToBeamLeft = Math.Abs(hole.X - beamMinX);
                    double distToBeamRight = Math.Abs(beamMaxX - hole.X);

                    bool nearLeftBeamEdge = distToBeamLeft < BEAM_EDGE_TO_HOLE_NEAR_LIMIT;
                    bool nearRightBeamEdge = distToBeamRight < BEAM_EDGE_TO_HOLE_NEAR_LIMIT;
                    bool useBeamOutsideEdge = nearLeftBeamEdge || nearRightBeamEdge;
                    bool useLeftBeamEdge = distToBeamLeft <= distToBeamRight;

                    Vector verticalDimDirection = new Vector(-1, 0, 0);
                    Point verticalHolePoint = new Point(hole.X, hole.Y, 0);
                    Point beamEdgePointForVertical = new Point(hole.X, beamEdgeY, 0);
                    Point beamCenterPointForVertical = new Point(hole.X, beamCenterY, 0);

                    if (useBeamOutsideEdge)
                    {
                        double beamSideX = useLeftBeamEdge ? beamMinX : beamMaxX;

                        Point realBeamEdgePoint = GetRealBeamSidePointNearY(
                            beamPolygon,
                            beamSideX,
                            useLeftBeamEdge,
                            beamEdgeY);

                        if (realBeamEdgePoint == null)
                            realBeamEdgePoint = new Point(beamSideX, beamEdgeY, 0);

                        beamEdgePointForVertical = realBeamEdgePoint;

                        // Center/reference line vẫn đo theo Y, nhưng đưa chân về cùng mép dầm
                        // để DIM nằm ngoài mép dầm và không chạy theo tâm lỗ.
                        beamCenterPointForVertical = new Point(realBeamEdgePoint.X, beamCenterY, 0);

                        verticalDimDirection = useLeftBeamEdge
                            ? new Vector(-1, 0, 0)
                            : new Vector(1, 0, 0);
                    }

                    double verticalDistance1 = useBeamOutsideEdge
                        ? (
                            useLeftBeamEdge
                            ? GetLeftDistanceByFeet(
                                new Point[] { beamEdgePointForVertical, verticalHolePoint },
                                beamEdgePointForVertical.X,
                                PLATE_VERTICAL_DIM_TIER_1)
                            : GetRightDistanceByFeet(
                                new Point[] { beamEdgePointForVertical, verticalHolePoint },
                                beamEdgePointForVertical.X,
                                PLATE_VERTICAL_DIM_TIER_1)
                          )
                        : GetLeftDistanceByFeet(
                            new Point[] { beamEdgePointForVertical, verticalHolePoint },
                            plateMinX,
                            PLATE_VERTICAL_DIM_TIER_1);

                    if (CreateDimChain(
                        handler,
                        view,
                        new Point[]
                        {
                            beamEdgePointForVertical,
                            verticalHolePoint
                        },
                        verticalDimDirection,
                        verticalDistance1))
                    {
                        count++;
                    }

                    // =========================
                    // PLATE - PHƯƠNG DỌC TẦNG 2
                    // Reference line / tâm dầm -> tâm lỗ plate.
                    // Nếu lỗ gần mép dầm, tầng 2 cũng offset từ mép dầm thật ra ngoài.
                    // =========================
                    double verticalDistance2 = useBeamOutsideEdge
                        ? (
                            useLeftBeamEdge
                            ? GetLeftDistanceByFeet(
                                new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                                beamEdgePointForVertical.X,
                                PLATE_VERTICAL_DIM_TIER_2)
                            : GetRightDistanceByFeet(
                                new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                                beamEdgePointForVertical.X,
                                PLATE_VERTICAL_DIM_TIER_2)
                          )
                        : GetLeftDistanceByFeet(
                            new Point[] { beamCenterPointForVertical, verticalHolePoint, beamEdgePointForVertical },
                            plateMinX,
                            PLATE_VERTICAL_DIM_TIER_2);

                    if (CreateDimChain(
                        handler,
                        view,
                        new Point[]
                        {
                            beamCenterPointForVertical,
                            verticalHolePoint
                        },
                        verticalDimDirection,
                        verticalDistance2))
                    {
                        count++;
                    }

                }
            }
            catch (Exception ex)
            {
                Msg("Slot 02 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static List<Point> GetPlateHoleCentersFromView(
            TSM.Model model,
            TSD.View view,
            ModelPart plate,
            double plateMinX,
            double plateMaxX,
            double plateMinY,
            double plateMaxY)
        {
            List<Point> result = new List<Point>();

            try
            {
                TSD.DrawingObjectEnumerator e =
                    view.GetAllObjects(typeof(TSD.Bolt));

                while (e != null && e.MoveNext())
                {
                    TSD.DrawingObject drawingBolt = e.Current as TSD.DrawingObject;
                    if (drawingBolt == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(drawingBolt);
                    if (id == null)
                        continue;

                    ModelObject mo = model.SelectModelObject(id);
                    ModelBoltGroup bg = mo as ModelBoltGroup;
                    if (bg == null)
                        continue;

                    // Chức năng 2 là dim cho plate: chỉ lấy lỗ/bolt liên quan tới plate.
                    // Dầm chỉ dùng làm mốc mép/reference, không lấy lỗ của dầm.
                    if (!BoltBelongsToPart(bg, plate))
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (p.X < plateMinX - BOUND_TOL || p.X > plateMaxX + BOUND_TOL)
                            continue;

                        if (p.Y < plateMinY - BOUND_TOL || p.Y > plateMaxY + BOUND_TOL)
                            continue;

                        AddUniquePoint2D(result, new Point(p.X, p.Y, 0), UNIQUE_HOLE_TOL);
                    }
                }
            }
            catch
            {
            }

            result.Sort(delegate (Point a, Point b)
            {
                int c = a.X.CompareTo(b.X);
                if (c != 0) return c;
                return a.Y.CompareTo(b.Y);
            });

            return result;
        }

        private static Point GetRealPlateOuterPointForNoHoleVerticalDim(
            List<Point> platePolygon,
            double plateMinX,
            double plateMinY,
            double plateMaxY,
            double beamCenterY)
        {
            bool useTopOuterEdge =
                Math.Abs(plateMaxY - beamCenterY) >=
                Math.Abs(plateMinY - beamCenterY);

            Point best = null;

            if (platePolygon != null)
            {
                foreach (Point p in platePolygon)
                {
                    if (p == null)
                        continue;

                    if (best == null ||
                        (useTopOuterEdge && p.Y > best.Y + TOL) ||
                        (!useTopOuterEdge && p.Y < best.Y - TOL) ||
                        (Math.Abs(p.Y - best.Y) <= TOL && p.X < best.X))
                    {
                        best = p;
                    }
                }
            }

            if (best != null)
                return new Point(best.X, best.Y, 0);

            return new Point(
                plateMinX,
                useTopOuterEdge ? plateMaxY : plateMinY,
                0);
        }

        private static Point SelectRepresentativePlateHole(
            List<Point> holes,
            List<Point> platePolygon,
            List<Point> beamPolygon,
            double plateMinX,
            double plateMaxX,
            double plateMinY,
            double plateMaxY,
            double beamMinX,
            double beamMaxX,
            double beamMinY,
            double beamMaxY)
        {
            try
            {
                if (holes == null || holes.Count == 0)
                    return null;

                if (holes.Count == 1)
                    return holes[0];

                Point plateCenter = new Point(
                    (plateMinX + plateMaxX) / 2.0,
                    (plateMinY + plateMaxY) / 2.0,
                    0);

                Point beamTarget = GetClosestPointOnPolygon2D(beamPolygon, plateCenter);
                if (beamTarget == null)
                {
                    beamTarget = new Point(
                        ClampDouble(plateCenter.X, beamMinX, beamMaxX),
                        ClampDouble(plateCenter.Y, beamMinY, beamMaxY),
                        0);
                }

                Point plateConnectionPoint = GetClosestPointOnPolygon2D(platePolygon, beamTarget);
                if (plateConnectionPoint == null)
                    plateConnectionPoint = plateCenter;

                double directionX = beamTarget.X - plateCenter.X;
                double directionY = beamTarget.Y - plateCenter.Y;
                double directionLength = Math.Sqrt(
                    directionX * directionX + directionY * directionY);

                if (directionLength <= 0.0001)
                {
                    directionX = ((beamMinX + beamMaxX) / 2.0) - plateCenter.X;
                    directionY = ((beamMinY + beamMaxY) / 2.0) - plateCenter.Y;
                    directionLength = Math.Sqrt(
                        directionX * directionX + directionY * directionY);
                }

                if (directionLength > 0.0001)
                {
                    directionX = directionX / directionLength;
                    directionY = directionY / directionLength;
                }

                Point best = null;
                double bestBeamDistance = 999999999.0;
                double bestConnectionDistance = 999999999.0;
                double bestTowardBeam = -999999999.0;
                double bestLowerEdgeDistance = 999999999.0;

                foreach (Point hole in holes)
                {
                    if (hole == null)
                        continue;

                    double beamDistance = GetDistanceToPolygonOrBounds2D(
                        beamPolygon,
                        hole,
                        beamMinX,
                        beamMaxX,
                        beamMinY,
                        beamMaxY);

                    double connectionDistance = Distance2D(hole, plateConnectionPoint);
                    double towardBeam =
                        (hole.X - plateCenter.X) * directionX +
                        (hole.Y - plateCenter.Y) * directionY;

                    double lowerEdgeDistance = GetDistanceToLowerPlateBoundary2D(
                        platePolygon,
                        hole,
                        plateCenter.Y);

                    bool better = false;

                    if (best == null ||
                        beamDistance < bestBeamDistance - MULTI_HOLE_SELECTION_TOL)
                    {
                        better = true;
                    }
                    else if (Math.Abs(beamDistance - bestBeamDistance) <= MULTI_HOLE_SELECTION_TOL)
                    {
                        if (connectionDistance < bestConnectionDistance - MULTI_HOLE_SELECTION_TOL)
                        {
                            better = true;
                        }
                        else if (Math.Abs(connectionDistance - bestConnectionDistance) <= MULTI_HOLE_SELECTION_TOL)
                        {
                            if (towardBeam > bestTowardBeam + 0.5)
                            {
                                better = true;
                            }
                            else if (Math.Abs(towardBeam - bestTowardBeam) <= 0.5)
                            {
                                if (lowerEdgeDistance < bestLowerEdgeDistance - 0.5)
                                {
                                    better = true;
                                }
                                else if (Math.Abs(lowerEdgeDistance - bestLowerEdgeDistance) <= 0.5)
                                {
                                    // Tie-break cuối chỉ để kết quả ổn định giữa các lần chạy.
                                    if (hole.X < best.X - 0.01 ||
                                        (Math.Abs(hole.X - best.X) <= 0.01 && hole.Y < best.Y))
                                        better = true;
                                }
                            }
                        }
                    }

                    if (better)
                    {
                        best = hole;
                        bestBeamDistance = beamDistance;
                        bestConnectionDistance = connectionDistance;
                        bestTowardBeam = towardBeam;
                        bestLowerEdgeDistance = lowerEdgeDistance;
                    }
                }

                if (best != null)
                    return new Point(best.X, best.Y, 0);
            }
            catch
            {
            }

            return null;
        }

        private static double GetDistanceToPolygonOrBounds2D(
            List<Point> polygon,
            Point point,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            if (point == null)
                return 999999999.0;

            try
            {
                if (polygon != null && polygon.Count >= 2)
                {
                    double best = 999999999.0;

                    for (int i = 0; i < polygon.Count; i++)
                    {
                        Point a = polygon[i];
                        Point b = polygon[(i + 1) % polygon.Count];
                        if (a == null || b == null)
                            continue;

                        Point q = ClosestPointOnSegment2D(a, b, point);
                        double distance = Distance2D(q, point);
                        if (distance < best)
                            best = distance;
                    }

                    if (best < 999999999.0)
                        return best;
                }
            }
            catch
            {
            }

            double nearestX = ClampDouble(point.X, minX, maxX);
            double nearestY = ClampDouble(point.Y, minY, maxY);
            return Distance2D(point, new Point(nearestX, nearestY, 0));
        }

        private static double GetDistanceToLowerPlateBoundary2D(
            List<Point> polygon,
            Point point,
            double plateCenterY)
        {
            if (polygon == null || polygon.Count < 2 || point == null)
                return 999999999.0;

            double best = 999999999.0;

            try
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    Point a = polygon[i];
                    Point b = polygon[(i + 1) % polygon.Count];
                    if (a == null || b == null)
                        continue;

                    double middleY = (a.Y + b.Y) / 2.0;
                    if (middleY > plateCenterY + TOL)
                        continue;

                    Point q = ClosestPointOnSegment2D(a, b, point);
                    double distance = Distance2D(q, point);
                    if (distance < best)
                        best = distance;
                }
            }
            catch
            {
            }

            return best;
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (min > max)
            {
                double temp = min;
                min = max;
                max = temp;
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double GetHorizontalDistanceFromOuterBoundary(
            Vector direction,
            Point firstDimPoint,
            double plateMinY,
            double plateMaxY,
            double beamMinY,
            double beamMaxY,
            double tier)
        {
            if (direction == null || firstDimPoint == null)
                return tier;

            double outerY;
            double distance;

            if (direction.Y >= 0.0)
            {
                outerY = Math.Max(plateMaxY, beamMaxY);
                distance = (outerY - firstDimPoint.Y) + tier;
            }
            else
            {
                outerY = Math.Min(plateMinY, beamMinY);
                distance = (firstDimPoint.Y - outerY) + tier;
            }

            if (distance < tier)
                distance = tier;

            return distance;
        }

        private static double GetLeftDistanceByFeet(
            Point[] feet,
            double plateMinX,
            double tier)
        {
            double minX = plateMinX;
            double firstX = 0.0;
            bool hasFirst = false;

            if (feet != null)
            {
                for (int i = 0; i < feet.Length; i++)
                {
                    Point p = feet[i];
                    if (p == null)
                        continue;

                    if (!hasFirst)
                    {
                        firstX = p.X;
                        hasFirst = true;
                    }

                    if (p.X < minX)
                        minX = p.X;
                }
            }

            if (!hasFirst)
                return tier;

            double distance = (firstX - minX) + tier;
            if (distance < tier)
                distance = tier;

            return distance;
        }


        private static double GetRightDistanceByFeet(
            Point[] feet,
            double beamMaxX,
            double tier)
        {
            double maxX = beamMaxX;
            double firstX = 0.0;
            bool hasFirst = false;

            if (feet != null)
            {
                for (int i = 0; i < feet.Length; i++)
                {
                    Point p = feet[i];
                    if (p == null)
                        continue;

                    if (!hasFirst)
                    {
                        firstX = p.X;
                        hasFirst = true;
                    }

                    if (p.X > maxX)
                        maxX = p.X;
                }
            }

            if (!hasFirst)
                return tier;

            double distance = (maxX - firstX) + tier;
            if (distance < tier)
                distance = tier;

            return distance;
        }

        private static Point GetRealBeamSidePointNearY(
            List<Point> polygon,
            double sideX,
            bool leftSide,
            double targetY)
        {
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return null;

                List<Point> pts = SortPolygonPointsClockwise(polygon);
                if (pts == null || pts.Count < 2)
                    return null;

                double edgeTol = Math.Max(3.0, TOL + 2.0);
                Point best = null;
                double bestScore = 999999999.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];

                    if (a == null || b == null)
                        continue;

                    double edgeX = leftSide
                        ? Math.Min(a.X, b.X)
                        : Math.Max(a.X, b.X);

                    if (Math.Abs(edgeX - sideX) > edgeTol)
                        continue;

                    Point q = ClosestPointOnSegment2D(a, b, new Point(sideX, targetY, 0));
                    if (q == null)
                        continue;

                    double sideScore = Math.Abs(q.X - sideX);
                    double yScore = Math.Abs(q.Y - targetY);
                    double score = sideScore * 1000.0 + yScore;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = q;
                    }
                }

                if (best != null)
                    return new Point(best.X, best.Y, 0);

                // Fallback: lấy vertex thật nằm gần mép trái/phải và gần Y cần bắt nhất.
                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.X - sideX) > edgeTol)
                        continue;

                    double score = Math.Abs(p.Y - targetY);
                    if (best == null || score < bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }

                if (best != null)
                    return new Point(best.X, best.Y, 0);
            }
            catch
            {
            }

            return null;
        }

        private static Point GetClosestPointOnPolygon2D(List<Point> polygon, Point target)
        {
            try
            {
                if (polygon == null || polygon.Count < 2 || target == null)
                    return null;

                List<Point> pts = SortPolygonPointsClockwise(polygon);
                if (pts == null || pts.Count < 2)
                    return null;

                Point best = null;
                double bestDist = 999999999.0;

                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i];
                    Point b = pts[(i + 1) % pts.Count];
                    if (a == null || b == null)
                        continue;

                    Point q = ClosestPointOnSegment2D(a, b, target);
                    if (q == null)
                        continue;

                    double d = Distance2D(q, target);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = q;
                    }
                }

                if (best != null)
                    return new Point(best.X, best.Y, 0);
            }
            catch
            {
            }

            return null;
        }

        private static Point ClosestPointOnSegment2D(Point a, Point b, Point p)
        {
            if (a == null || b == null || p == null)
                return null;

            double ax = a.X;
            double ay = a.Y;
            double bx = b.X;
            double by = b.Y;
            double px = p.X;
            double py = p.Y;

            double vx = bx - ax;
            double vy = by - ay;
            double len2 = vx * vx + vy * vy;

            if (len2 <= 0.000001)
                return new Point(ax, ay, 0);

            double t = ((px - ax) * vx + (py - ay) * vy) / len2;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;

            return new Point(ax + vx * t, ay + vy * t, 0);
        }

        private static double GetOffsetFromPlateOuterBoundary(
            Vector direction,
            double baseX,
            double baseY,
            double plateMinX,
            double plateMaxX,
            double plateMinY,
            double plateMaxY,
            double tier)
        {
            if (direction == null)
                return tier;

            // Tekla đặt đường DIM theo distance tính từ điểm bắt DIM theo hướng direction.
            // Vì vậy phải cộng thêm khoảng từ điểm bắt đến biên dạng xa nhất của plate,
            // để tầng DIM luôn bắt đầu từ ngoài biên plate, không bắt đầu từ tâm lỗ.
            if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
            {
                if (direction.X >= 0.0)
                    return Math.Max(tier, (plateMaxX - baseX) + tier);

                return Math.Max(tier, (baseX - plateMinX) + tier);
            }

            if (direction.Y >= 0.0)
                return Math.Max(tier, (plateMaxY - baseY) + tier);

            return Math.Max(tier, (baseY - plateMinY) + tier);
        }

        private static Point GetRealPlateSideAnchorForHorizontalDim(
            List<Point> polygon,
            double fallbackEdgeX,
            bool leftSide,
            bool preferTop)
        {
            try
            {
                if (polygon == null || polygon.Count < 2)
                    return null;

                List<Point> pts = SortPolygonPointsClockwise(polygon);
                if (pts == null || pts.Count < 2)
                    return null;

                double sideX = fallbackEdgeX;
                bool hasSideX = false;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (!hasSideX)
                    {
                        sideX = p.X;
                        hasSideX = true;
                        continue;
                    }

                    if (leftSide)
                    {
                        if (p.X < sideX)
                            sideX = p.X;
                    }
                    else
                    {
                        if (p.X > sideX)
                            sideX = p.X;
                    }
                }

                if (!hasSideX)
                    return null;

                // Chân ngoài của DIM ngang nội bộ plate phải luôn nằm tại X ngoài
                // cùng thật của toàn tấm. Không chọn "cạnh bên" chỉ vì một đầu cạnh
                // chạm sideX: cạnh vát cũng thỏa điều kiện đó và đầu còn lại của nó
                // có thể nằm lùi vào trong plate.
                Point outermost = null;
                double sideTol = 0.01;

                foreach (Point p in pts)
                {
                    if (p == null)
                        continue;

                    if (Math.Abs(p.X - sideX) > sideTol)
                        continue;

                    if (outermost == null)
                    {
                        outermost = p;
                        continue;
                    }

                    if (preferTop)
                    {
                        if (p.Y > outermost.Y)
                            outermost = p;
                    }
                    else
                    {
                        if (p.Y < outermost.Y)
                            outermost = p;
                    }
                }

                if (outermost != null)
                    return new Point(sideX, outermost.Y, 0);
            }
            catch
            {
            }

            return null;
        }

        private static bool TryGetBeamHorizontalRealEdgePoints(
            List<Point> polygon,
            double holeY,
            double fallbackMinX,
            double fallbackMaxX,
            double fallbackMinY,
            double fallbackMaxY,
            out Point leftPoint,
            out Point rightPoint)
        {
            double centerY = (fallbackMinY + fallbackMaxY) / 2.0;
            leftPoint = new Point(fallbackMinX, centerY, 0);
            rightPoint = new Point(fallbackMaxX, centerY, 0);

            try
            {
                double leftX;
                double rightX;

                // Ưu tiên bắt đúng giao điểm mép dầm tại cao độ tâm lỗ.
                if (TryGetHorizontalRealEdgesAtY(
                    polygon,
                    holeY,
                    fallbackMinX,
                    fallbackMaxX,
                    out leftX,
                    out rightX))
                {
                    leftPoint = new Point(leftX, holeY, 0);
                    rightPoint = new Point(rightX, holeY, 0);
                    return true;
                }

                // Nếu tâm lỗ nằm ngoài vùng cắt của dầm, vẫn phải tạo DIM dầm.
                // Lấy mép thật ngoài cùng của polygon dầm để chân dim không nằm lưng chừng.
                Point leftMost = null;
                Point rightMost = null;
                double minX = 999999999.0;
                double maxX = -999999999.0;

                if (polygon != null)
                {
                    foreach (Point p in polygon)
                    {
                        if (p == null)
                            continue;

                        if (p.X < minX)
                        {
                            minX = p.X;
                            leftMost = p;
                        }

                        if (p.X > maxX)
                        {
                            maxX = p.X;
                            rightMost = p;
                        }
                    }
                }

                if (leftMost != null && rightMost != null && Math.Abs(rightMost.X - leftMost.X) > 1.0)
                {
                    leftPoint = new Point(leftMost.X, leftMost.Y, 0);
                    rightPoint = new Point(rightMost.X, rightMost.Y, 0);
                    return true;
                }

                return Math.Abs(fallbackMaxX - fallbackMinX) > 1.0;
            }
            catch
            {
                return Math.Abs(fallbackMaxX - fallbackMinX) > 1.0;
            }
        }

        private static bool TryGetHorizontalRealEdgesAtY(
            List<Point> polygon,
            double y,
            double fallbackMinX,
            double fallbackMaxX,
            out double leftX,
            out double rightX)
        {
            leftX = fallbackMinX;
            rightX = fallbackMaxX;

            try
            {
                if (polygon == null || polygon.Count < 2)
                    return false;

                List<double> xs = new List<double>();
                double tol = Math.Max(1.0, TOL);

                for (int i = 0; i < polygon.Count; i++)
                {
                    Point a = polygon[i];
                    Point b = polygon[(i + 1) % polygon.Count];
                    if (a == null || b == null)
                        continue;

                    if (Math.Abs(a.Y - y) <= tol)
                        AddUniqueDouble(xs, a.X, 0.5);

                    if (Math.Abs(b.Y - y) <= tol)
                        AddUniqueDouble(xs, b.X, 0.5);

                    double minY = Math.Min(a.Y, b.Y);
                    double maxY = Math.Max(a.Y, b.Y);
                    if (y < minY - tol || y > maxY + tol)
                        continue;

                    double dy = b.Y - a.Y;
                    if (Math.Abs(dy) <= 0.0001)
                        continue;

                    double t = (y - a.Y) / dy;
                    if (t < -0.01 || t > 1.01)
                        continue;

                    double x = a.X + (b.X - a.X) * t;
                    AddUniqueDouble(xs, x, 0.5);
                }

                if (xs.Count < 2)
                    return false;

                xs.Sort();
                leftX = xs[0];
                rightX = xs[xs.Count - 1];

                return Math.Abs(rightX - leftX) > 1.0;
            }
            catch
            {
                leftX = fallbackMinX;
                rightX = fallbackMaxX;
                return false;
            }
        }

        private static void AddUniqueDouble(List<double> list, double value, double tol)
        {
            if (list == null)
                return;

            foreach (double old in list)
            {
                if (Math.Abs(old - value) <= tol)
                    return;
            }

            list.Add(value);
        }

        private static List<Point> GetFrontSectionPolygon(Solid solid, Point min, Point max)
        {
            List<Point> best = new List<Point>();

            try
            {
                if (solid == null || min == null || max == null)
                    return best;

                double midZ = (min.Z + max.Z) / 2.0;

                double[] zPlanes = new double[]
                {
                    midZ,
                    midZ - 1.0,
                    midZ + 1.0,
                    midZ - 2.0,
                    midZ + 2.0,
                    min.Z + 1.0,
                    max.Z - 1.0
                };

                double bestScore = -1.0;

                foreach (double z in zPlanes)
                {
                    Point p1 = new Point(min.X - 1000.0, min.Y - 1000.0, z);
                    Point p2 = new Point(max.X + 1000.0, min.Y - 1000.0, z);
                    Point p3 = new Point(min.X - 1000.0, max.Y + 1000.0, z);

                    List<Point> poly = GetLargestIntersectionPolygon(
                        solid.IntersectAllFaces(p1, p2, p3));

                    if (poly.Count < 2)
                        continue;

                    double minX, maxX, minY, maxY;
                    GetMinMax2D(poly, out minX, out maxX, out minY, out maxY);

                    double width = Math.Abs(maxX - minX);
                    double height = Math.Abs(maxY - minY);
                    double score = width * height;

                    if (width < 10.0 || height < 10.0)
                        continue;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = SortPolygonPointsClockwise(poly);
                    }
                }
            }
            catch
            {
            }

            return best;
        }

        private static List<Point> GetLargestIntersectionPolygon(IEnumerator en)
        {
            List<List<Point>> all = new List<List<Point>>();

            try
            {
                while (en != null && en.MoveNext())
                    CollectPointLists(en.Current, all, 0);
            }
            catch
            {
            }

            List<Point> best = new List<Point>();
            double bestScore = -1.0;

            foreach (List<Point> list in all)
            {
                if (list == null || list.Count < 2)
                    continue;

                double minX, maxX, minY, maxY;
                GetMinMax2D(list, out minX, out maxX, out minY, out maxY);

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
            if (obj == null || result == null || depth > 6)
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

        private static void GetMinMax2D(
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

            if (pts == null)
                return;

            foreach (Point p in pts)
            {
                if (p == null)
                    continue;

                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        private static bool CreateDimChain(
            StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance)
        {
            if (handler == null || view == null || points == null || points.Length < 2)
                return false;

            PointList list = new PointList();

            for (int i = 0; i < points.Length; i++)
            {
                Point p = points[i];
                if (p == null)
                    continue;

                bool duplicate = false;
                foreach (Point old in list)
                {
                    if (Distance2D(old, p) <= 0.5)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    list.Add(new Point(p.X, p.Y, 0));
            }

            if (list.Count < 2)
                return false;

            StraightDimensionSet dim =
                handler.CreateDimensionSet(view, list, direction, distance);

            return dim != null;
        }

        private static double GetNearestBeamEdgeY(double y, double beamMinY, double beamMaxY)
        {
            double d1 = Math.Abs(y - beamMinY);
            double d2 = Math.Abs(y - beamMaxY);
            return d1 <= d2 ? beamMinY : beamMaxY;
        }

        private static List<DrawingPart> GetSelectedDrawingParts(TSD.DrawingHandler dh)
        {
            List<DrawingPart> result = new List<DrawingPart>();

            try
            {
                TSD.DrawingObjectEnumerator e =
                    dh.GetDrawingObjectSelector().GetSelected();

                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp != null)
                        result.Add(dp);
                }
            }
            catch
            {
            }

            return result;
        }

        private static ModelPart SelectModelPart(TSM.Model model, DrawingPart dp)
        {
            try
            {
                if (model == null || dp == null || dp.ModelIdentifier == null)
                    return null;

                return model.SelectModelObject(dp.ModelIdentifier) as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static TSD.View TryGetSelectedPartsView(DrawingPart plateDrawingPart, DrawingPart beamDrawingPart)
        {
            TSD.View v1 = TryGetDrawingObjectView(plateDrawingPart);
            TSD.View v2 = TryGetDrawingObjectView(beamDrawingPart);

            if (v1 != null && v2 != null && object.ReferenceEquals(v1, v2))
                return v1;

            if (v1 != null && v2 == null)
                return v1;

            if (v2 != null && v1 == null)
                return v2;

            return v1;
        }

        private static TSD.View TryGetDrawingObjectView(object drawingObject)
        {
            if (drawingObject == null)
                return null;

            string[] methodNames = new string[]
            {
                "GetView",
                "GetFatherView",
                "GetParentView"
            };

            for (int i = 0; i < methodNames.Length; i++)
            {
                try
                {
                    MethodInfo m = drawingObject.GetType().GetMethod(
                        methodNames[i],
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (m == null)
                        continue;

                    object value = m.Invoke(drawingObject, null);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            string[] propertyNames = new string[]
            {
                "View",
                "FatherView",
                "ParentView"
            };

            for (int i = 0; i < propertyNames.Length; i++)
            {
                try
                {
                    object value = GetPropertyValue(drawingObject, propertyNames[i]);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            return null;
        }

        private static TSD.View FindViewContainingBothParts(
            TSD.Drawing drawing,
            Identifier id1,
            Identifier id2)
        {
            try
            {
                if (drawing == null || id1 == null || id2 == null)
                    return null;

                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    bool has1 = false;
                    bool has2 = false;

                    TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                    while (parts != null && parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        if (SameIdentifier(dp.ModelIdentifier, id1))
                            has1 = true;

                        if (SameIdentifier(dp.ModelIdentifier, id2))
                            has2 = true;

                        if (has1 && has2)
                            return view;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsPlatePart(ModelPart part)
        {
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(profile))
                return false;

            if (profile.StartsWith("PL") ||
                profile.StartsWith("PLT") ||
                profile.StartsWith("FB") ||
                profile.StartsWith("FL") ||
                profile.IndexOf("PLATE") >= 0)
                return true;

            // Một số môi trường trả dạng WIDTH*THICKNESS không có chữ PL.
            // Chỉ dùng fallback này khi profile không giống thép hình phổ biến.
            if (profile.IndexOf("H") == 0 ||
                profile.IndexOf("I") == 0 ||
                profile.IndexOf("C") == 0 ||
                profile.IndexOf("L") == 0 ||
                profile.IndexOf("RHS") >= 0 ||
                profile.IndexOf("SHS") >= 0 ||
                profile.IndexOf("PIPE") >= 0)
                return false;

            return false;
        }

        private static ModelPart GetThinnerPartBySolid(TSM.Model model, ModelPart a, ModelPart b)
        {
            try
            {
                if (a == null || b == null)
                    return null;

                double ta = GetSmallestSolidDimension(a);
                double tb = GetSmallestSolidDimension(b);

                if (ta <= 0.0 || tb <= 0.0)
                    return null;

                return ta <= tb ? a : b;
            }
            catch
            {
                return null;
            }
        }

        private static double GetSmallestSolidDimension(ModelPart part)
        {
            try
            {
                Solid s = part.GetSolid();
                Point min = s.MinimumPoint;
                Point max = s.MaximumPoint;

                double dx = Math.Abs(max.X - min.X);
                double dy = Math.Abs(max.Y - min.Y);
                double dz = Math.Abs(max.Z - min.Z);

                double v = dx;
                if (dy < v) v = dy;
                if (dz < v) v = dz;
                return v;
            }
            catch
            {
                return 0.0;
            }
        }

        private static string GetProfileString(ModelPart part)
        {
            if (part == null)
                return "";

            try
            {
                object profileObj = GetPropertyValue(part, "Profile");
                object profileString = GetPropertyValue(profileObj, "ProfileString");
                if (profileString != null)
                    return profileString.ToString();
            }
            catch
            {
            }

            string value = "";
            try
            {
                if (part.GetReportProperty("PROFILE", ref value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
            }

            try
            {
                if (part.GetReportProperty("PROFILE_NAME", ref value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
            }

            return "";
        }

        private static bool BoltBelongsToPart(ModelBoltGroup bg, ModelPart part)
        {
            if (bg == null || part == null)
                return false;

            try
            {
                ModelPart p1 = GetPropertyValue(bg, "PartToBeBolted") as ModelPart;
                if (p1 != null && SameIdentifier(p1.Identifier, part.Identifier))
                    return true;

                ModelPart p2 = GetPropertyValue(bg, "PartToBoltTo") as ModelPart;
                if (p2 != null && SameIdentifier(p2.Identifier, part.Identifier))
                    return true;
            }
            catch
            {
            }

            // Fallback: thử report assembly/part id không đủ tin cậy thì bỏ qua.
            return false;
        }

        private static Identifier TryGetModelIdentifier(object drawingObject)
        {
            try
            {
                if (drawingObject == null)
                    return null;

                PropertyInfo prop = drawingObject.GetType().GetProperty(
                    "ModelIdentifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return null;

                return prop.GetValue(drawingObject, null) as Identifier;
            }
            catch
            {
                return null;
            }
        }

        private static object GetPropertyValue(object obj, string name)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(name))
                    return null;

                PropertyInfo p = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0)
                    return null;

                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool SameIdentifier(Identifier a, Identifier b)
        {
            if (a == null || b == null)
                return false;

            try
            {
                return a.ID == b.ID;
            }
            catch
            {
                return a.ToString() == b.ToString();
            }
        }

        private static void AddUniquePoint2D(List<Point> list, Point p, double tol)
        {
            if (list == null || p == null)
                return;

            foreach (Point q in list)
            {
                if (Distance2D(q, p) <= tol)
                    return;
            }

            list.Add(p);
        }

        private static double Distance2D(Point a, Point b)
        {
            if (a == null || b == null)
                return 999999999.0;

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Msg(string text)
        {
            // Popup disabled.
        }
    }
}
