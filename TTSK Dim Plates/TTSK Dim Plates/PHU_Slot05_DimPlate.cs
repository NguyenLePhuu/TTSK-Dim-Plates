#pragma warning disable 1633

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
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 05 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05.Run()
    public class PHU_AutoDimSlot05
    {
        public static string Run()
        {
            PHU_Slot05_SelectedPlateEdgeHoleDim.Run();
            return "OK";
        }
    }

    // Slot 05 thuật toán 2 cho MainForm switch:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05_TopBottomMode.Run()
    public class PHU_AutoDimSlot05_TopBottomMode
    {
        public static string Run()
        {
            PHU_Slot05_SelectedPlateEdgeHoleDim.RunTopBottomMode();
            return "OK";
        }
    }

    public class PHU_Slot05_SelectedPlateEdgeHoleDim
    {
        private const double TOL = 1.0;
        private const double POINT_DUP_TOL = 0.5;
        private const double PLATE_BOUND_TOL = 25.0;

        // SLOT05 - QUY TẮC TẦNG DIM
        // Tầng 1 ngang: mép plate -> tất cả lỗ trong plate -> mép plate.
        // Tầng 2 ngang: mép main -> mép plate -> mép plate -> mép main.
        // Dim dọc: mép main -> mép plate -> mép plate -> mép main.
        private const double SLOT05_DIM_TIER_BASE = 150.0;
        private const double SLOT05_DIM_TIER_STEP = 150.0;

        // Tolerance nhận biết plate nằm gần mép trái/phải main.
        private const double SLOT05_NEAR_MAIN_EDGE_TOL = 300.0;

        // Tầng ngang nội bộ plate: tất cả plate dùng chung 1 tầng, không tăng tầng theo số plate.
        private const double SLOT05_INTERNAL_PLATE_HOLE_TIER = 150.0;

        // Tầng ngang tổng main/plate.
        private const double SLOT05_MAIN_PLATE_CHAIN_TIER = 300.0;

        public static void Run()
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
            if (selectedParts.Count < 1)
            {
                Msg("Slot05: Hãy chọn 1 hoặc nhiều plate/tấm cần dim.");
                return;
            }

            List<DrawingPart> selectedPlateDrawingParts = new List<DrawingPart>();
            List<ModelPart> selectedPlates = new List<ModelPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart dp = selectedParts[i];
                ModelPart mp = SelectModelPart(model, dp);
                if (mp == null)
                    continue;

                if (IsDummyReferencePart(mp))
                    continue;

                if (IsPlateLikePart(mp))
                {
                    selectedPlates.Add(mp);
                    selectedPlateDrawingParts.Add(dp);
                }
            }

            if (selectedPlates.Count == 0)
            {
                Msg("Slot05: Không nhận diện được tấm/plate trong selection.");
                return;
            }

            TSD.View view = TryGetSelectedPartsView(selectedPlateDrawingParts.ToArray());
            if (view == null)
                view = FindViewContainingPart(drawing, selectedPlates[0].Identifier);

            if (view == null)
            {
                Msg("Slot05: Không tìm thấy view chứa tấm đã chọn.");
                return;
            }

            int created = CreateSelectedPlateDims(model, drawing, view, selectedPlates);

            try { drawing.CommitChanges(); } catch { }

            // Không popup hoàn thành để chạy gọn.
        }

        public static void RunTopBottomMode()
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
            if (selectedParts.Count < 1)
            {
                Msg("Slot05: Hãy chọn 1 hoặc nhiều plate/tấm cần dim.");
                return;
            }

            List<DrawingPart> selectedPlateDrawingParts = new List<DrawingPart>();
            List<ModelPart> selectedPlates = new List<ModelPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart dp = selectedParts[i];
                ModelPart mp = SelectModelPart(model, dp);
                if (mp == null)
                    continue;

                if (IsDummyReferencePart(mp))
                    continue;

                if (IsPlateLikePart(mp))
                {
                    selectedPlates.Add(mp);
                    selectedPlateDrawingParts.Add(dp);
                }
            }

            if (selectedPlates.Count == 0)
            {
                Msg("Slot05: Không nhận diện được tấm/plate trong selection.");
                return;
            }

            TSD.View view = TryGetSelectedPartsView(selectedPlateDrawingParts.ToArray());
            if (view == null)
                view = FindViewContainingPart(drawing, selectedPlates[0].Identifier);

            if (view == null)
            {
                Msg("Slot05: Không tìm thấy view chứa tấm đã chọn.");
                return;
            }

            int created = CreateSelectedPlateDims_TopBottomMode(model, drawing, view, selectedPlates);

            try { drawing.CommitChanges(); } catch { }

            // Không popup hoàn thành để chạy gọn.
        }

        private static int CreateSelectedPlateDims(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || drawing == null || view == null || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                if (allViewParts.Count == 0)
                    return count;

                List<Slot05PlateGroup> groups = new List<Slot05PlateGroup>();

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart plate = plates[i];
                    if (plate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid)
                        continue;

                    ModelPart mainBeam = FindMainBeamForPlate(plate, plateBox, allViewParts);
                    if (mainBeam == null)
                        continue;

                    Bounds2D mainBox = GetPartBounds2D(mainBeam);
                    if (!mainBox.Valid)
                        continue;

                    List<Point> holeCenters = GetBoltCentersInsidePlate(model, view, plate, plateBox);
                    holeCenters.Sort(ComparePointByXThenY);

                    Slot05PlateGroup g = new Slot05PlateGroup();
                    g.Plate = plate;
                    g.PlateBox = plateBox;
                    g.MainBeam = mainBeam;
                    g.MainBox = mainBox;
                    g.HoleCenters = holeCenters;
                    groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                // Slot05 theo dump: DIM ngang đặt phía trên.
                Vector horizontalDirection = new Vector(0, 1, 0);

                // 1) DIM NGANG TẦNG 1 - DIM NỘI BỘ TỪNG PLATE RIÊNG:
                // Mép plate -> tất cả lỗ nằm trong chính plate đó -> mép plate.
                // Nếu có N plate thì tạo N dim riêng, nhưng TẤT CẢ dùng chung một tầng offset.
                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.PlateBox.Valid)
                        continue;

                    if (g.HoleCenters == null || g.HoleCenters.Count == 0)
                        continue;

                    List<Point> h1 = new List<Point>();
                    double plateTopY = g.PlateBox.MaxY;

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);

                    if (g.HoleCenters != null)
                    {
                        for (int h = 0; h < g.HoleCenters.Count; h++)
                        {
                            Point hp = g.HoleCenters[h];
                            if (hp != null)
                            {
                                Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, horizontalDirection);
                                AddUniquePoint2D(h1, hpGap, POINT_DUP_TOL);
                            }
                        }
                    }

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                    h1.Sort(ComparePointByXThenY);

                    if (h1.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h1.ToArray(),
                            horizontalDirection,
                            SLOT05_INTERNAL_PLATE_HOLE_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // 2) DIM NGANG TẦNG 2 - CHAIN CHUNG MÉP MAIN + MÉP CÁC PLATE:
                // Giữ chain chung cho quan hệ tổng, đặt phía trên theo dump.
                List<Point> h2 = new List<Point>();
                Bounds2D mainUnion = GetMainUnionBox(groups);
                if (mainUnion.Valid)
                {
                    double mainTopY = mainUnion.MaxY;
                    AddUniquePoint2D(h2, new Point(mainUnion.MinX, mainTopY, 0), POINT_DUP_TOL);

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Slot05PlateGroup g = groups[i];
                        if (g == null || !g.PlateBox.Valid)
                            continue;

                        double plateTopY = g.PlateBox.MaxY;
                        AddUniquePoint2D(h2, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);
                        AddUniquePoint2D(h2, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                    }

                    AddUniquePoint2D(h2, new Point(mainUnion.MaxX, mainTopY, 0), POINT_DUP_TOL);
                    h2.Sort(ComparePointByXThenY);

                    if (h2.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h2.ToArray(),
                            horizontalDirection,
                            SLOT05_MAIN_PLATE_CHAIN_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // 3) DIM DỌC:
                // Quy tắc mới:
                // - Plate gần mép trái/phải main: chân trên/dưới bám đúng X mép ngoài thật của main.
                // - Plate giữa main: dim dọc offset về bên trái theo quy tắc tầng.
                // - Chuỗi điểm: mép trên main -> tâm lỗ plate -> mép dưới main.
                int edgeLeftTier = 0;
                int edgeRightTier = 0;

                // Sort theo X để tầng dọc của plate giữa ổn định và dễ đoán.
                groups.Sort(CompareGroupByPlateCenterXThenY);

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || g.HoleCenters == null || g.HoleCenters.Count == 0 || !g.MainBox.Valid)
                        continue;

                    double leftEdgeGap = Math.Abs(g.PlateBox.MinX - g.MainBox.MinX);
                    double rightEdgeGap = Math.Abs(g.PlateBox.MaxX - g.MainBox.MaxX);
                    bool nearLeftMainEdge = leftEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;
                    bool nearRightMainEdge = rightEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;

                    if (nearLeftMainEdge && nearRightMainEdge)
                    {
                        if (leftEdgeGap <= rightEdgeGap)
                            nearRightMainEdge = false;
                        else
                            nearLeftMainEdge = false;
                    }

                    // Quy tắc chọn lỗ để dim dọc:
                    // - Plate gần mép phải main: lấy lỗ ngoài cùng bên phải.
                    // - Plate gần mép trái hoặc nằm giữa: lấy lỗ ngoài cùng bên trái.
                    bool pickRightHole = nearRightMainEdge && !nearLeftMainEdge;
                    Point primaryHole = PickPrimaryVerticalHole(g.PlateBox, g.HoleCenters, pickRightHole);
                    if (primaryHole == null)
                        continue;

                    Vector verticalDirection;
                    double distance;
                    Point mainTop;
                    Point mainBottom;

                    if (nearLeftMainEdge && !nearRightMainEdge)
                    {
                        // Gần mép trái: chân main dùng đúng mép ngoài trái thật của main.
                        verticalDirection = new Vector(-1, 0, 0);
                        mainTop = new Point(g.MainBox.MinX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.MainBox.MinX, g.MainBox.MinY, 0);
                        distance = GetLeftTierDistance(mainTop, g.MainBox, edgeLeftTier);
                        edgeLeftTier++;
                    }
                    else if (nearRightMainEdge && !nearLeftMainEdge)
                    {
                        // Gần mép phải: chân main dùng đúng mép ngoài phải thật của main.
                        verticalDirection = new Vector(1, 0, 0);
                        mainTop = new Point(g.MainBox.MaxX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.MainBox.MaxX, g.MainBox.MinY, 0);
                        distance = GetRightTierDistance(mainTop, g.MainBox, edgeRightTier);
                        edgeRightTier++;
                    }
                    else
                    {
                        // Plate nằm giữa: chân trên/dưới dùng X mép ngoài trái plate, offset cố định 1 tầng.
                        verticalDirection = new Vector(-1, 0, 0);
                        mainTop = new Point(g.PlateBox.MinX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.PlateBox.MinX, g.MainBox.MinY, 0);
                        distance = Slot05TierOffset(0);
                    }

                    Point holePoint = GetHolePointWithMBoltGap(g.Plate, primaryHole, verticalDirection);

                    List<Point> v = new List<Point>();
                    v.Add(mainBottom);
                    v.Add(holePoint);
                    v.Add(mainTop);
                    v.Sort(ComparePointByYThenX);

                    if (CreateDimChain(
                        handler,
                        view,
                        v.ToArray(),
                        verticalDirection,
                        distance,
                        "GEO_DIMENSION"))
                    {
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot05 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static int CreateSelectedPlateDims_TopBottomMode(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || drawing == null || view == null || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                if (allViewParts.Count == 0)
                    return count;

                List<Slot05PlateGroup> groups = new List<Slot05PlateGroup>();

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart plate = plates[i];
                    if (plate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid)
                        continue;

                    ModelPart mainBeam = FindMainBeamForPlate(plate, plateBox, allViewParts);
                    if (mainBeam == null)
                        continue;

                    Bounds2D mainBox = GetPartBounds2D(mainBeam);
                    if (!mainBox.Valid)
                        continue;

                    List<Point> holeCenters = GetBoltCentersInsidePlate(model, view, plate, plateBox);
                    holeCenters.Sort(ComparePointByXThenY);

                    Slot05PlateGroup g = new Slot05PlateGroup();
                    g.Plate = plate;
                    g.PlateBox = plateBox;
                    g.MainBeam = mainBeam;
                    g.MainBox = mainBox;
                    g.HoleCenters = holeCenters;
                    groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                Bounds2D mainUnion = GetMainUnionBox(groups);

                // SLOT05 THUẬT TOÁN 2:
                // Plate nằm trên main thì dim ngang đặt lên trên và chân dim dùng mép trên.
                // Plate nằm dưới main thì dim ngang đặt xuống dưới và chân dim dùng mép dưới.
                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.PlateBox.Valid)
                        continue;

                    if (g.HoleCenters == null || g.HoleCenters.Count == 0)
                        continue;

                    bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                    Vector horizontalDirection = plateBelowMain ? new Vector(0, -1, 0) : new Vector(0, 1, 0);
                    double plateEdgeY = plateBelowMain ? g.PlateBox.MinY : g.PlateBox.MaxY;

                    List<Point> h1 = new List<Point>();

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MinX, plateEdgeY, 0), POINT_DUP_TOL);

                    if (g.HoleCenters != null)
                    {
                        for (int h = 0; h < g.HoleCenters.Count; h++)
                        {
                            Point hp = g.HoleCenters[h];
                            if (hp != null)
                            {
                                Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, horizontalDirection);
                                AddUniquePoint2D(h1, hpGap, POINT_DUP_TOL);
                            }
                        }
                    }

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MaxX, plateEdgeY, 0), POINT_DUP_TOL);
                    h1.Sort(ComparePointByXThenY);

                    if (h1.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h1.ToArray(),
                            horizontalDirection,
                            SLOT05_INTERNAL_PLATE_HOLE_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // DIM NGANG TẦNG 2 - tách chain trên/dưới để chân dim đi theo đúng mép trên hoặc mép dưới.
                List<Point> h2Top = new List<Point>();
                List<Point> h2Bottom = new List<Point>();
                if (mainUnion.Valid)
                {
                    AddUniquePoint2D(h2Top, new Point(mainUnion.MinX, mainUnion.MaxY, 0), POINT_DUP_TOL);
                    AddUniquePoint2D(h2Bottom, new Point(mainUnion.MinX, mainUnion.MinY, 0), POINT_DUP_TOL);

                    bool hasTopPlate = false;
                    bool hasBottomPlate = false;
                    double topPlateOuterY = -999999999.0;
                    double bottomPlateOuterY = 999999999.0;

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Slot05PlateGroup g = groups[i];
                        if (g == null || !g.PlateBox.Valid)
                            continue;

                        bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                        if (plateBelowMain)
                        {
                            hasBottomPlate = true;
                            double plateBottomY = g.PlateBox.MinY;
                            if (plateBottomY < bottomPlateOuterY)
                                bottomPlateOuterY = plateBottomY;
                            AddUniquePoint2D(h2Bottom, new Point(g.PlateBox.MinX, plateBottomY, 0), POINT_DUP_TOL);
                            AddUniquePoint2D(h2Bottom, new Point(g.PlateBox.MaxX, plateBottomY, 0), POINT_DUP_TOL);
                        }
                        else
                        {
                            hasTopPlate = true;
                            double plateTopY = g.PlateBox.MaxY;
                            if (plateTopY > topPlateOuterY)
                                topPlateOuterY = plateTopY;
                            AddUniquePoint2D(h2Top, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);
                            AddUniquePoint2D(h2Top, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                        }
                    }

                    AddUniquePoint2D(h2Top, new Point(mainUnion.MaxX, mainUnion.MaxY, 0), POINT_DUP_TOL);
                    AddUniquePoint2D(h2Bottom, new Point(mainUnion.MaxX, mainUnion.MinY, 0), POINT_DUP_TOL);

                    h2Top.Sort(ComparePointByXThenY);
                    h2Bottom.Sort(ComparePointByXThenY);

                    if (hasTopPlate && h2Top.Count >= 2)
                    {
                        double topChainTargetY = topPlateOuterY + SLOT05_MAIN_PLATE_CHAIN_TIER;
                        double topChainDistance = Math.Abs(topChainTargetY - h2Top[0].Y);

                        if (CreateDimChain(
                            handler,
                            view,
                            h2Top.ToArray(),
                            new Vector(0, 1, 0),
                            topChainDistance,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }

                    if (hasBottomPlate && h2Bottom.Count >= 2)
                    {
                        double bottomChainTargetY = bottomPlateOuterY - SLOT05_MAIN_PLATE_CHAIN_TIER;
                        double bottomChainDistance = Math.Abs(h2Bottom[0].Y - bottomChainTargetY);

                        if (CreateDimChain(
                            handler,
                            view,
                            h2Bottom.ToArray(),
                            new Vector(0, -1, 0),
                            bottomChainDistance,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // DIM DỌC THUẬT TOÁN 2:
                // Vẫn giữ quy luật trái/phải/giữa và tier như hiện tại.
                // Chuỗi điểm đổi thành: mép plate -> tất cả lỗ plate -> mép main gần plate.
                int edgeLeftTier = 0;
                int edgeRightTier = 0;

                groups.Sort(CompareGroupByPlateCenterXThenY);

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || g.HoleCenters == null || g.HoleCenters.Count == 0 || !g.MainBox.Valid)
                        continue;

                    double leftEdgeGap = Math.Abs(g.PlateBox.MinX - g.MainBox.MinX);
                    double rightEdgeGap = Math.Abs(g.PlateBox.MaxX - g.MainBox.MaxX);
                    bool nearLeftMainEdge = leftEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;
                    bool nearRightMainEdge = rightEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;

                    if (nearLeftMainEdge && nearRightMainEdge)
                    {
                        if (leftEdgeGap <= rightEdgeGap)
                            nearRightMainEdge = false;
                        else
                            nearLeftMainEdge = false;
                    }

                    bool pickRightHole = nearRightMainEdge && !nearLeftMainEdge;
                    Point primaryHole = PickPrimaryVerticalHole(g.PlateBox, g.HoleCenters, pickRightHole);
                    if (primaryHole == null)
                        continue;

                    Vector verticalDirection;
                    double distance;
                    double mainEdgeX;

                    if (nearLeftMainEdge && !nearRightMainEdge)
                    {
                        verticalDirection = new Vector(-1, 0, 0);
                        mainEdgeX = g.MainBox.MinX;
                        distance = GetLeftTierDistance(new Point(mainEdgeX, primaryHole.Y, 0), g.MainBox, edgeLeftTier);
                        edgeLeftTier++;
                    }
                    else if (nearRightMainEdge && !nearLeftMainEdge)
                    {
                        verticalDirection = new Vector(1, 0, 0);
                        mainEdgeX = g.MainBox.MaxX;
                        distance = GetRightTierDistance(new Point(mainEdgeX, primaryHole.Y, 0), g.MainBox, edgeRightTier);
                        edgeRightTier++;
                    }
                    else
                    {
                        verticalDirection = new Vector(-1, 0, 0);
                        mainEdgeX = g.PlateBox.MinX;
                        distance = Slot05TierOffset(0);
                    }

                    bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                    double plateEdgeY = plateBelowMain ? g.PlateBox.MinY : g.PlateBox.MaxY;
                    double mainEdgeY = plateBelowMain ? g.MainBox.MinY : g.MainBox.MaxY;

                    List<Point> v = new List<Point>();
                    double plateOuterEdgeX = verticalDirection.X < 0.0 ? g.PlateBox.MinX : g.PlateBox.MaxX;
                    AddUniquePoint2D(v, new Point(plateOuterEdgeX, plateEdgeY, 0), POINT_DUP_TOL);

                    List<Point> holesForVerticalDim = new List<Point>();
                    for (int h = 0; h < g.HoleCenters.Count; h++)
                    {
                        Point hp = g.HoleCenters[h];
                        if (hp == null)
                            continue;

                        if (hp.X < g.PlateBox.MinX - PLATE_BOUND_TOL || hp.X > g.PlateBox.MaxX + PLATE_BOUND_TOL ||
                            hp.Y < g.PlateBox.MinY - PLATE_BOUND_TOL || hp.Y > g.PlateBox.MaxY + PLATE_BOUND_TOL)
                            continue;

                        holesForVerticalDim.Add(new Point(hp.X, hp.Y, 0));
                    }

                    holesForVerticalDim.Sort(ComparePointByYThenX);

                    for (int h = 0; h < holesForVerticalDim.Count; h++)
                    {
                        Point hp = holesForVerticalDim[h];
                        Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, verticalDirection);
                        AddUniquePoint2D(v, hpGap, POINT_DUP_TOL);
                    }

                    AddUniquePoint2D(v, new Point(mainEdgeX, mainEdgeY, 0), POINT_DUP_TOL);
                    v.Sort(ComparePointByYThenX);

                    if (CreateDimChain(
                        handler,
                        view,
                        v.ToArray(),
                        verticalDirection,
                        distance,
                        "GEO_DIMENSION"))
                    {
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot05 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static bool IsPlateBelowMain(Bounds2D plateBox, Bounds2D mainBox)
        {
            if (!plateBox.Valid || !mainBox.Valid)
                return false;

            double plateCenterY = (plateBox.MinY + plateBox.MaxY) / 2.0;
            double mainCenterY = (mainBox.MinY + mainBox.MaxY) / 2.0;

            return plateCenterY < mainCenterY;
        }

        private class Slot05PlateGroup
        {
            public ModelPart Plate;
            public Bounds2D PlateBox;
            public ModelPart MainBeam;
            public Bounds2D MainBox;
            public List<Point> HoleCenters;
        }

        private static double Slot05TierOffset(int tier)
        {
            if (tier < 0)
                tier = 0;

            return SLOT05_DIM_TIER_BASE + SLOT05_DIM_TIER_STEP * tier;
        }

        private static double GetLeftTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            double targetX = mainBox.MinX - Slot05TierOffset(tier);
            if (firstPoint == null)
                return Slot05TierOffset(tier);

            return Math.Abs(firstPoint.X - targetX);
        }

        private static double GetRightTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            double targetX = mainBox.MaxX + Slot05TierOffset(tier);
            if (firstPoint == null)
                return Slot05TierOffset(tier);

            return Math.Abs(targetX - firstPoint.X);
        }

        private static double GetMiddleLeftTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            // Plate giữa: dim luôn đẩy về bên trái, offset tính từ mép trái ngoài cùng của main.
            return GetLeftTierDistance(firstPoint, mainBox, tier);
        }

        private static int CompareGroupByPlateCenterXThenY(Slot05PlateGroup a, Slot05PlateGroup b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            double ax = (a.PlateBox.MinX + a.PlateBox.MaxX) / 2.0;
            double bx = (b.PlateBox.MinX + b.PlateBox.MaxX) / 2.0;
            int c = ax.CompareTo(bx);
            if (c != 0) return c;

            double ay = (a.PlateBox.MinY + a.PlateBox.MaxY) / 2.0;
            double by = (b.PlateBox.MinY + b.PlateBox.MaxY) / 2.0;
            return ay.CompareTo(by);
        }

        private static Bounds2D GetMainUnionBox(List<Slot05PlateGroup> groups)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            try
            {
                if (groups == null || groups.Count == 0)
                    return b;

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.MainBox.Valid)
                        continue;

                    if (!b.Valid)
                    {
                        b = g.MainBox;
                        b.Valid = true;
                    }
                    else
                    {
                        if (g.MainBox.MinX < b.MinX) b.MinX = g.MainBox.MinX;
                        if (g.MainBox.MaxX > b.MaxX) b.MaxX = g.MainBox.MaxX;
                        if (g.MainBox.MinY < b.MinY) b.MinY = g.MainBox.MinY;
                        if (g.MainBox.MaxY > b.MaxY) b.MaxY = g.MainBox.MaxY;
                    }
                }
            }
            catch
            {
            }

            return b;
        }

        private static Point PickPrimaryVerticalHole(Bounds2D plateBox, List<Point> holes, bool pickRightHole)
        {
            if (holes == null || holes.Count == 0)
                return null;

            Point best = null;
            double bestX = pickRightHole ? -999999999.0 : 999999999.0;

            for (int i = 0; i < holes.Count; i++)
            {
                Point p = holes[i];
                if (p == null)
                    continue;

                if (p.X < plateBox.MinX - PLATE_BOUND_TOL || p.X > plateBox.MaxX + PLATE_BOUND_TOL ||
                    p.Y < plateBox.MinY - PLATE_BOUND_TOL || p.Y > plateBox.MaxY + PLATE_BOUND_TOL)
                    continue;

                if (best == null ||
                    (pickRightHole && p.X > bestX) ||
                    (!pickRightHole && p.X < bestX))
                {
                    best = p;
                    bestX = p.X;
                }
            }

            if (best == null)
                return null;

            return new Point(best.X, best.Y, 0);
        }

        private static double GetReferenceYForHorizontalPlateEdge(
            Bounds2D plateBox,
            List<Point> holeCenters,
            bool dimToTop)
        {
            try
            {
                if (holeCenters == null || holeCenters.Count == 0)
                    return dimToTop ? plateBox.MaxY : plateBox.MinY;

                double avgY = 0.0;
                for (int i = 0; i < holeCenters.Count; i++)
                    avgY += holeCenters[i].Y;
                avgY = avgY / holeCenters.Count;

                // Nếu lỗ nằm gần mép trên/dưới thì dùng đúng mép gần đó.
                // Còn lại dùng mép theo hướng dim để chân dim bám vào biên plate.
                double dTop = Math.Abs(plateBox.MaxY - avgY);
                double dBottom = Math.Abs(avgY - plateBox.MinY);

                if (dTop < dBottom)
                    return plateBox.MaxY;

                if (dBottom < dTop)
                    return plateBox.MinY;

                return dimToTop ? plateBox.MaxY : plateBox.MinY;
            }
            catch
            {
                return dimToTop ? plateBox.MaxY : plateBox.MinY;
            }
        }

        private static ModelPart FindMainBeamForPlate(
            ModelPart plate,
            Bounds2D plateBox,
            List<ModelPart> allViewParts)
        {
            if (plate == null || !plateBox.Valid || allViewParts == null || allViewParts.Count == 0)
                return null;

            string plateAssembly = GetReportString(plate, "ASSEMBLY_POS");
            Point plateCenter = CenterOf(plateBox);

            ModelPart best = null;
            double bestScore = -999999999.0;

            for (int i = 0; i < allViewParts.Count; i++)
            {
                ModelPart p = allViewParts[i];
                if (p == null || p.Identifier == null || plate.Identifier == null)
                    continue;

                if (SameIdentifier(p.Identifier, plate.Identifier))
                    continue;

                if (IsDummyReferencePart(p))
                    continue;

                if (IsPlateLikePart(p))
                    continue;

                Bounds2D b = GetPartBounds2D(p);
                if (!b.Valid)
                    continue;

                Point c = CenterOf(b);
                double area = Math.Abs(b.MaxX - b.MinX) * Math.Abs(b.MaxY - b.MinY);
                double distance = Distance2D(plateCenter, c);

                double score = area - distance * 0.25;

                string asm = GetReportString(p, "ASSEMBLY_POS");
                if (!string.IsNullOrEmpty(plateAssembly) &&
                    !string.IsNullOrEmpty(asm) &&
                    string.Equals(plateAssembly, asm, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100000000.0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        private static List<ModelPart> GetAllModelPartsInView(TSM.Model model, TSD.View view)
        {
            List<ModelPart> result = new List<ModelPart>();

            try
            {
                if (model == null || view == null)
                    return result;

                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(DrawingPart));
                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp == null || dp.ModelIdentifier == null)
                        continue;

                    ModelPart mp = model.SelectModelObject(dp.ModelIdentifier) as ModelPart;
                    if (mp == null)
                        continue;

                    bool exists = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (SameIdentifier(result[i].Identifier, mp.Identifier))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        result.Add(mp);
                }
            }
            catch
            {
            }

            return result;
        }

        private static Point GetHolePointWithMBoltGap(ModelPart plate, Point holeCenter, Vector direction)
        {
            try
            {
                if (plate == null || holeCenter == null || direction == null)
                    return holeCenter;

                double gap = GetHoleCenterDimGapByMThenPhi(plate, holeCenter);
                if (gap <= 0.0)
                    return new Point(holeCenter.X, holeCenter.Y, 0);

                double x = holeCenter.X;
                double y = holeCenter.Y;

                if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
                {
                    if (direction.X < 0.0)
                        x -= gap;
                    else if (direction.X > 0.0)
                        x += gap;
                }
                else
                {
                    if (direction.Y < 0.0)
                        y -= gap;
                    else if (direction.Y > 0.0)
                        y += gap;
                }

                return new Point(x, y, 0);
            }
            catch
            {
                return holeCenter;
            }
        }

        private static double GetHoleCenterDimGapByMThenPhi(ModelPart plate, Point holeCenter)
        {
            try
            {
                if (plate == null || holeCenter == null)
                    return 0.0;

                ModelObjectEnumerator bolts = plate.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - holeCenter.X) <= 1.0 &&
                            Math.Abs(p.Y - holeCenter.Y) <= 1.0)
                        {
                            double d = GetBoltGroupMThenPhiForDimGap(bg);
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

        private static double GetBoltGroupMThenPhiForDimGap(ModelBoltGroup bg)
        {
            if (bg == null)
                return 0.0;

            // Slot05 yêu cầu ưu tiên M/BoltSize trước, sau đó mới tới phi lỗ.
            double v = GetReportDouble(bg, "BOLT_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "Diameter");
            if (v > 0.0 && v < 500.0) return v;

            // Sau M mới tới phi lỗ / hole size.
            v = GetReportDouble(bg, "HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_DIAM");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleSize");
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

        private static List<Point> GetBoltCentersInsidePlate(
            TSM.Model model,
            TSD.View view,
            ModelPart plate,
            Bounds2D plateBox)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (model == null || view == null || plate == null || !plateBox.Valid)
                    return result;

                List<Identifier> allowedBoltIds = new List<Identifier>();

                // Ưu tiên bolts thật thuộc part.
                try
                {
                    ModelObjectEnumerator bolts = plate.GetBolts();
                    while (bolts != null && bolts.MoveNext())
                    {
                        ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                        if (bg == null)
                            continue;

                        AddUniqueIdentifier(allowedBoltIds, bg.Identifier);
                        AddBoltGroupPositionsInsideBounds(bg, plateBox, result);
                    }
                }
                catch
                {
                }

                // Fallback/ bổ sung: quét Drawing Bolt trong view, nhưng chỉ nhận bolt thuộc chính plate đang xét.
                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));
                while (e != null && e.MoveNext())
                {
                    TSD.DrawingObject dobj = e.Current as TSD.DrawingObject;
                    if (dobj == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(dobj);
                    if (id == null)
                        continue;

                    if (!ContainsIdentifier(allowedBoltIds, id))
                        continue;

                    ModelObject mo = model.SelectModelObject(id);
                    ModelBoltGroup bg = mo as ModelBoltGroup;
                    AddBoltGroupPositionsInsideBounds(bg, plateBox, result);
                }
            }
            catch
            {
            }

            result.Sort(ComparePointByXThenY);
            return result;
        }

        private static void AddBoltGroupPositionsInsideBounds(
            ModelBoltGroup bg,
            Bounds2D plateBox,
            List<Point> result)
        {
            try
            {
                if (bg == null || bg.BoltPositions == null || result == null || !plateBox.Valid)
                    return;

                foreach (object obj in bg.BoltPositions)
                {
                    Point p = obj as Point;
                    if (p == null)
                        continue;

                    if (!PointInsideBounds(p, plateBox, PLATE_BOUND_TOL))
                        continue;

                    AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
                }
            }
            catch
            {
            }
        }

        private static void AddUniqueIdentifier(List<Identifier> list, Identifier id)
        {
            try
            {
                if (list == null || id == null)
                    return;

                if (ContainsIdentifier(list, id))
                    return;

                list.Add(id);
            }
            catch
            {
            }
        }

        private static bool ContainsIdentifier(List<Identifier> list, Identifier id)
        {
            try
            {
                if (list == null || id == null)
                    return false;

                for (int i = 0; i < list.Count; i++)
                {
                    if (SameIdentifier(list[i], id))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static Identifier TryGetModelIdentifier(object drawingObject)
        {
            try
            {
                if (drawingObject == null)
                    return null;

                object value = GetPropertyValue(drawingObject, "ModelIdentifier");
                return value as Identifier;
            }
            catch
            {
                return null;
            }
        }

        private struct Bounds2D
        {
            public bool Valid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private static Bounds2D GetPartBounds2D(ModelPart part)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            try
            {
                Solid s = part.GetSolid();
                Point min = s.MinimumPoint;
                Point max = s.MaximumPoint;

                b.MinX = Math.Min(min.X, max.X);
                b.MaxX = Math.Max(min.X, max.X);
                b.MinY = Math.Min(min.Y, max.Y);
                b.MaxY = Math.Max(min.Y, max.Y);
                b.Valid = Math.Abs(b.MaxX - b.MinX) > TOL && Math.Abs(b.MaxY - b.MinY) > TOL;
            }
            catch
            {
            }

            return b;
        }

        private static Point CenterOf(Bounds2D b)
        {
            return new Point(
                (b.MinX + b.MaxX) / 2.0,
                (b.MinY + b.MaxY) / 2.0,
                0);
        }

        private static bool PointInsideBounds(Point p, Bounds2D b, double tol)
        {
            if (p == null || !b.Valid)
                return false;

            return p.X >= b.MinX - tol &&
                   p.X <= b.MaxX + tol &&
                   p.Y >= b.MinY - tol &&
                   p.Y <= b.MaxY + tol;
        }

        private static int ComparePointByXThenY(Point a, Point b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            return a.Y.CompareTo(b.Y);
        }

        private static int ComparePointByYThenX(Point a, Point b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            return a.X.CompareTo(b.X);
        }

        private static void AddUniquePoint2D(List<Point> list, Point p, double tol)
        {
            if (list == null || p == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                if (Distance2D(list[i], p) <= tol)
                    return;
            }

            list.Add(p);
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            string attributeName)
        {
            try
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
                        if (Distance2D(old, p) <= POINT_DUP_TOL)
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

                TSD.StraightDimensionSet dim =
                    handler.CreateDimensionSet(view, list, direction, distance);

                if (dim != null && !string.IsNullOrEmpty(attributeName))
                    TryApplyStraightDimAttributes(dim, attributeName);

                return dim != null;
            }
            catch
            {
                return false;
            }
        }

        private static void TryApplyStraightDimAttributes(
            TSD.StraightDimensionSet dim,
            string attributeName)
        {
            try
            {
                if (dim == null || string.IsNullOrEmpty(attributeName))
                    return;

                object attr = dim.Attributes;
                if (attr == null)
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
                dim.Modify();
            }
            catch
            {
            }
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

        private static TSD.View TryGetSelectedPartsView(params DrawingPart[] parts)
        {
            TSD.View result = null;

            if (parts == null)
                return null;

            for (int i = 0; i < parts.Length; i++)
            {
                TSD.View v = TryGetDrawingObjectView(parts[i]);
                if (v == null)
                    continue;

                if (result == null)
                    result = v;
                else if (!object.ReferenceEquals(result, v))
                    return result;
            }

            return result;
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

        private static TSD.View FindViewContainingPart(TSD.Drawing drawing, Identifier id)
        {
            try
            {
                if (drawing == null || id == null)
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

                    TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                    while (parts != null && parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        if (SameIdentifier(dp.ModelIdentifier, id))
                            return view;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            if (part == null)
                return false;

            string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();
            string material = GetReportString(part, "MATERIAL").Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (partPos == "DUMMY-99" ||
                partPos.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase) ||
                material == "JOINT" ||
                name.StartsWith("BJ", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsPlateLikePart(ModelPart part)
        {
            if (part == null)
                return false;

            if (IsDummyReferencePart(part))
                return false;

            string typeName = part.GetType().FullName;
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (typeName.IndexOf("ContourPlate") >= 0)
                return true;

            if (name.IndexOf("PLATE") >= 0)
                return true;

            if (profile.StartsWith("PL") ||
                profile.StartsWith("PLT") ||
                profile.StartsWith("FB") ||
                profile.StartsWith("FL") ||
                profile.IndexOf("PLATE") >= 0)
                return true;

            // Slot05 cho phép tấm dạng L/angle plate nếu người dùng pick trực tiếp.
            // Nhưng vẫn tránh nhận nhầm các beam chính quá lớn khi tự quét main.
            if (profile.StartsWith("L") && typeName.IndexOf("Beam") >= 0)
                return true;

            return false;
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

            return "";
        }

        private static string GetReportString(ModelObject obj, string reportName)
        {
            if (obj == null)
                return "";

            try
            {
                string s = "";
                obj.GetReportProperty(reportName, ref s);
                if (s == null)
                    return "";
                return s.Trim();
            }
            catch
            {
                return "";
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
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    text,
                    "PHU Slot05 Selected Plate Edge Hole Dim",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
