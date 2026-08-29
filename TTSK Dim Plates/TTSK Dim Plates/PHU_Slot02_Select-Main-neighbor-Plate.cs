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
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 02 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot02.Run()
    public class PHU_AutoDimSlot02
    {
        public static void Run()
        {
            PHU_Slot02_NeighborReferencePlateDim.Run();
        }
    }

    public class PHU_Slot02_NeighborReferencePlateDim
    {
        private const double TOL = 1.0;

        // Khoảng offset tầng cho 2 dim mẫu.
        // Dim từ mép thép chính -> tâm/reference neighbor dùng tầng ngoài hơn.
        private const double MAIN_TO_NEIGHBOR_TIER = 550.0;

        // Dim từ tâm/reference neighbor -> mép plate dùng tầng trong hơn.
        private const double NEIGHBOR_TO_PLATE_TIER = 450.0;

        private const double BOUND_TOL = 20.0;
        private const double FLANGE_FACE_MIN_ALIGNMENT = 0.98;
        private const double REFERENCE_AXIS_MIN_ALIGNMENT = 0.999;

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

            ModelPart mainBeam = PHU_MainPartResolver.Resolve(model, drawing);
            if (mainBeam == null || mainBeam.Identifier == null)
            {
                Msg("Slot02: Không xác định được Main Part từ bản vẽ đang mở.");
                return;
            }

            List<DrawingPart> selectedParts = GetSelectedDrawingParts(dh);
            if (selectedParts.Count < 1)
            {
                List<Slot02AutomaticViewTarget> automaticTargets = FindAutomaticSlot02Targets(
                    model,
                    drawing,
                    mainBeam
                );

                if (automaticTargets.Count == 0)
                {
                    Msg(
                        "Slot02: Không tìm thấy liên kết chắc chắn Main → Plate → Neighbor H trong các view."
                    );
                    return;
                }

                int automaticCreated = 0;
                for (int targetIndex = 0; targetIndex < automaticTargets.Count; targetIndex++)
                {
                    Slot02AutomaticViewTarget target = automaticTargets[targetIndex];
                    if (target == null || target.View == null || target.Connections.Count == 0)
                        continue;

                    automaticCreated += CreateNeighborPlateReferenceDims(
                        model,
                        target.View,
                        mainBeam,
                        target.NeighborBeams,
                        target.Plates,
                        target.Connections
                    );
                }

                if (automaticCreated <= 0)
                {
                    Msg("Slot02: Đã nhận diện liên kết nhưng không tạo được dimension hợp lệ.");
                    return;
                }

                try
                {
                    drawing.CommitChanges();
                }
                catch { }
                return;
            }

            List<DrawingPart> plateDrawingParts = new List<DrawingPart>();
            List<ModelPart> plates = new List<ModelPart>();

            // Van chap nhan neighbor duoc chon de giu tuong thich thao tac cu,
            // nhung main authoritative luon lay tu drawing/assembly.
            List<DrawingPart> selectedNeighborDrawingParts = new List<DrawingPart>();
            List<ModelPart> neighborBeams = new List<ModelPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart dp = selectedParts[i];
                ModelPart mp = SelectModelPart(model, dp);
                if (mp == null)
                    continue;

                // Area Selection / quét có thể dính dummy part như BJ19z / DUMMY-99 / JOINT.
                // Dummy này có profile PL10*10 nên nếu không lọc trước sẽ bị nhận nhầm là plate thật,
                // làm sai thuật toán xác định main-neighbor-plate.
                if (IsDummyReferencePart(mp))
                    continue;

                if (IsPlatePart(mp))
                {
                    plates.Add(mp);
                    plateDrawingParts.Add(dp);
                }
                else
                {
                    if (!SameIdentifier(mp.Identifier, mainBeam.Identifier))
                    {
                        AddUniqueModelPart(neighborBeams, mp);
                        selectedNeighborDrawingParts.Add(dp);
                    }
                }
            }

            if (plates.Count == 0)
            {
                Msg("Slot02: Không nhận diện được plate trong selection.");
                return;
            }

            // Khi user chi chon plate, neighbor duoc lay bang quan he model chac chan:
            // main cua assembly cua plate (neu khac drawing main) va cac part cung bolt group.
            for (int i = 0; i < plates.Count; i++)
            {
                AddSemanticNeighborBeamsForPlate(plates[i], mainBeam, neighborBeams);
            }

            if (neighborBeams.Count == 0)
            {
                Msg(
                    "Slot02: Plate đã chọn không có quan hệ assembly/bolt đủ chắc chắn để xác định neighbor."
                );
                return;
            }

            DrawingPart firstPlateDrawingPart =
                plateDrawingParts.Count > 0 ? plateDrawingParts[0] : null;
            DrawingPart firstNeighborDrawingPart =
                selectedNeighborDrawingParts.Count > 0 ? selectedNeighborDrawingParts[0] : null;

            TSD.View view = TryGetSelectedPartsView(
                firstPlateDrawingPart,
                firstNeighborDrawingPart
            );
            if (
                view != null
                && (
                    !ViewContainsPart(view, mainBeam.Identifier)
                    || !ViewContainsPart(view, neighborBeams[0].Identifier)
                )
            )
            {
                view = null;
            }

            if (view == null)
                view = FindViewContainingParts(
                    drawing,
                    plates[0].Identifier,
                    mainBeam.Identifier,
                    neighborBeams[0].Identifier
                );

            if (view == null)
            {
                Msg("Slot02: Không tìm thấy view chứa đủ plate, Main Part và neighbor đã resolve.");
                return;
            }

            int created = CreateNeighborPlateReferenceDims(
                model,
                view,
                mainBeam,
                neighborBeams,
                plates
            );

            try
            {
                drawing.CommitChanges();
            }
            catch { }

            //Msg("Slot02 DONE. DIM đã tạo: " + created.ToString());  Tắt popup debug
        }

        private static int CreateNeighborPlateReferenceDims(
            TSM.Model model,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates
        )
        {
            return CreateNeighborPlateReferenceDims(
                model,
                view,
                mainBeam,
                neighborBeams,
                plates,
                null
            );
        }

        private static int CreateNeighborPlateReferenceDims(
            TSM.Model model,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates,
            List<Slot02AutomaticConnection> exactConnections
        )
        {
            int count = 0;

            if (
                model == null
                || view == null
                || mainBeam == null
                || neighborBeams == null
                || neighborBeams.Count == 0
                || plates == null
                || plates.Count == 0
            )
                return count;

            TSM.TransformationPlane oldPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();

            try
            {
                model
                    .GetWorkPlaneHandler()
                    .SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem)
                    );

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                // Các chân ngoài của DIM ngang phải nằm trên contour thật của dầm.
                // MinimumPoint/MaximumPoint chỉ còn dùng cho phân loại và bố trí;
                // không được ghép chéo thành một góc bounding-box không tồn tại.
                List<Point> mainContourVertices = GetSolidEdgeVertices2D(mainBeam);

                Point mainReferenceStart;
                Point mainReferenceEnd;
                if (
                    !TryGetStraightReferenceAxis(
                        mainBeam,
                        out mainReferenceStart,
                        out mainReferenceEnd
                    )
                )
                    return count;

                // Slot02 đo theo X/Y của view: main bắt buộc gần ngang.
                // Chế độ auto đã lọc bằng hình dáng; guard này bảo vệ cả flow
                // selection thủ công khỏi tạo DIM chiếu sai cho view xoay chéo.
                if (!IsReferenceAxisAligned(mainReferenceStart, mainReferenceEnd, true))
                    return count;

                List<NeighborPlateGroup> groups = new List<NeighborPlateGroup>();

                double allMinY = mainBox.MinY;
                double allMaxY = mainBox.MaxY;

                for (int i = 0; i < neighborBeams.Count; i++)
                {
                    Bounds2D nb = GetPartBounds2D(neighborBeams[i]);
                    if (!nb.Valid)
                        continue;

                    if (nb.MinY < allMinY)
                        allMinY = nb.MinY;
                    if (nb.MaxY > allMaxY)
                        allMaxY = nb.MaxY;
                }

                List<Slot02AutomaticConnection> connectionsToProcess =
                    new List<Slot02AutomaticConnection>();

                if (exactConnections != null)
                {
                    connectionsToProcess.AddRange(exactConnections);
                }
                else
                {
                    // Legacy selection flow giu nguyen cach ghep plate voi
                    // neighbor gan nhat trong tap neighbor da resolve.
                    for (int i = 0; i < plates.Count; i++)
                    {
                        ModelPart plate = plates[i];
                        if (plate == null)
                            continue;

                        Bounds2D plateBoxForSelection = GetPartBounds2D(plate);
                        if (!plateBoxForSelection.Valid)
                            continue;

                        Point plateCenterForSelection = new Point(
                            (plateBoxForSelection.MinX + plateBoxForSelection.MaxX) / 2.0,
                            (plateBoxForSelection.MinY + plateBoxForSelection.MaxY) / 2.0,
                            0
                        );

                        ModelPart nearestNeighbor;
                        Bounds2D nearestNeighborBox;
                        if (
                            !FindNearestNeighborBeam(
                                plateCenterForSelection,
                                neighborBeams,
                                out nearestNeighbor,
                                out nearestNeighborBox
                            )
                        )
                            continue;

                        Slot02AutomaticConnection legacyConnection =
                            new Slot02AutomaticConnection();
                        legacyConnection.Plate = plate;
                        legacyConnection.Neighbor = nearestNeighbor;
                        connectionsToProcess.Add(legacyConnection);
                    }
                }

                for (int i = 0; i < connectionsToProcess.Count; i++)
                {
                    Slot02AutomaticConnection connection = connectionsToProcess[i];
                    ModelPart plate = connection == null ? null : connection.Plate;
                    ModelPart neighborBeam = connection == null ? null : connection.Neighbor;
                    if (plate == null || neighborBeam == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    Bounds2D neighborBox = GetPartBounds2D(neighborBeam);
                    if (!plateBox.Valid || !neighborBox.Valid)
                        continue;

                    Point plateCenter = new Point(
                        (plateBox.MinX + plateBox.MaxX) / 2.0,
                        (plateBox.MinY + plateBox.MaxY) / 2.0,
                        0
                    );

                    // Chan reference bat buoc la giao hinh hoc cua hai
                    // GetReferenceLine that trong cung he toa do view.
                    Point neighborRef;
                    if (
                        !TryResolveReferenceIntersection(
                            mainReferenceStart,
                            mainReferenceEnd,
                            neighborBeam,
                            mainBox,
                            neighborBox,
                            out neighborRef
                        )
                    )
                        continue;

                    bool dimToTop = plateCenter.Y >= neighborRef.Y;
                    Vector direction = dimToTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0);

                    Point plateEdge = GetPlateEdgePointTowardNeighbor(
                        plateBox,
                        neighborRef,
                        dimToTop
                    );

                    if (plateBox.MinY < allMinY)
                        allMinY = plateBox.MinY;
                    if (plateBox.MaxY > allMaxY)
                        allMaxY = plateBox.MaxY;

                    NeighborPlateGroup g = new NeighborPlateGroup();
                    g.Plate = plate;
                    g.Neighbor = neighborBeam;
                    g.PlateBox = plateBox;
                    g.NeighborBox = neighborBox;
                    g.PlateCenter = plateCenter;
                    g.NeighborRef = neighborRef;
                    g.PlateEdge = plateEdge;
                    g.Direction = direction;
                    g.IsTop = dimToTop;
                    g.AttributeName =
                        plateCenter.X >= neighborRef.X ? "GEO_HIGE_RIGHT" : "GEO_HIGE_LEFT";

                    if (exactConnections == null || !HasEquivalentAutomaticGroup(groups, g))
                        groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();

                List<Point> topRefs = new List<Point>();
                List<Point> bottomRefs = new List<Point>();

                for (int i = 0; i < groups.Count; i++)
                {
                    NeighborPlateGroup g = groups[i];
                    if (g == null)
                        continue;

                    // DIM nội bộ từng cụm: Reference/tâm neighbor -> mép trên plate.
                    double distanceNeighborToPlate = GetHorizontalDistanceFromOuterBoundary(
                        g.Direction,
                        g.NeighborRef,
                        allMinY,
                        allMaxY,
                        NEIGHBOR_TO_PLATE_TIER
                    );

                    if (
                        CreateDimChain(
                            handler,
                            view,
                            new Point[] { g.NeighborRef, g.PlateEdge },
                            g.Direction,
                            distanceNeighborToPlate,
                            g.AttributeName
                        )
                    )
                    {
                        count++;
                    }

                    if (g.IsTop)
                        AddUniquePoint2D(topRefs, g.NeighborRef, 0.5);
                    else
                        AddUniquePoint2D(bottomRefs, g.NeighborRef, 0.5);
                }

                topRefs.Sort(ComparePointByXThenY);
                bottomRefs.Sort(ComparePointByXThenY);

                if (topRefs.Count > 0)
                {
                    Vector direction = new Vector(0, 1, 0);
                    Point mainLeftEdge;
                    Point mainRightEdge;
                    if (
                        !TryResolveMainHorizontalRealEdgePoints(
                            mainContourVertices,
                            true,
                            out mainLeftEdge,
                            out mainRightEdge
                        )
                    )
                    {
                        mainLeftEdge = null;
                        mainRightEdge = null;
                    }

                    if (mainLeftEdge != null && mainRightEdge != null)
                    {
                        List<Point> chain = new List<Point>();
                        chain.Add(mainLeftEdge);
                        for (int i = 0; i < topRefs.Count; i++)
                            chain.Add(topRefs[i]);
                        chain.Add(mainRightEdge);
                        chain.Sort(ComparePointByXThenY);

                        double distanceMainToNeighbor = GetHorizontalDistanceFromOuterBoundary(
                            direction,
                            mainLeftEdge,
                            allMinY,
                            allMaxY,
                            MAIN_TO_NEIGHBOR_TIER
                        );

                        if (
                            CreateDimChain(
                                handler,
                                view,
                                chain.ToArray(),
                                direction,
                                distanceMainToNeighbor
                            )
                        )
                        {
                            count++;
                        }
                    }
                }

                if (bottomRefs.Count > 0)
                {
                    Vector direction = new Vector(0, -1, 0);
                    Point mainLeftEdge;
                    Point mainRightEdge;
                    if (
                        !TryResolveMainHorizontalRealEdgePoints(
                            mainContourVertices,
                            false,
                            out mainLeftEdge,
                            out mainRightEdge
                        )
                    )
                    {
                        mainLeftEdge = null;
                        mainRightEdge = null;
                    }

                    if (mainLeftEdge != null && mainRightEdge != null)
                    {
                        List<Point> chain = new List<Point>();
                        chain.Add(mainLeftEdge);
                        for (int i = 0; i < bottomRefs.Count; i++)
                            chain.Add(bottomRefs[i]);
                        chain.Add(mainRightEdge);
                        chain.Sort(ComparePointByXThenY);

                        double distanceMainToNeighbor = GetHorizontalDistanceFromOuterBoundary(
                            direction,
                            mainLeftEdge,
                            allMinY,
                            allMaxY,
                            MAIN_TO_NEIGHBOR_TIER
                        );

                        if (
                            CreateDimChain(
                                handler,
                                view,
                                chain.ToArray(),
                                direction,
                                distanceMainToNeighbor
                            )
                        )
                        {
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot02 ERROR:\n" + ex.Message);
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }

            return count;
        }

        private class NeighborPlateGroup
        {
            public ModelPart Plate;
            public ModelPart Neighbor;
            public Bounds2D PlateBox;
            public Bounds2D NeighborBox;
            public Point PlateCenter;
            public Point NeighborRef;
            public Point PlateEdge;
            public Vector Direction;
            public bool IsTop;
            public string AttributeName;
        }

        private static bool HasEquivalentAutomaticGroup(
            List<NeighborPlateGroup> groups,
            NeighborPlateGroup candidate
        )
        {
            if (
                groups == null
                || candidate == null
                || candidate.NeighborRef == null
                || candidate.PlateEdge == null
            )
                return false;

            for (int i = 0; i < groups.Count; i++)
            {
                NeighborPlateGroup old = groups[i];
                if (
                    old == null
                    || old.NeighborRef == null
                    || old.PlateEdge == null
                    || old.Direction == null
                    || candidate.Direction == null
                )
                    continue;

                if (
                    Distance2D(old.NeighborRef, candidate.NeighborRef) <= 0.5
                    && Distance2D(old.PlateEdge, candidate.PlateEdge) <= 0.5
                    && Math.Abs(old.Direction.X - candidate.Direction.X) <= TOL
                    && Math.Abs(old.Direction.Y - candidate.Direction.Y) <= TOL
                )
                    return true;
            }

            return false;
        }

        private sealed class Slot02AutomaticConnection
        {
            public ModelPart Plate;
            public ModelPart Neighbor;
        }

        private sealed class Slot02AutomaticViewTarget
        {
            public TSD.View View;
            public readonly List<Slot02AutomaticConnection> Connections =
                new List<Slot02AutomaticConnection>();
            public readonly List<ModelPart> Plates = new List<ModelPart>();
            public readonly List<ModelPart> NeighborBeams = new List<ModelPart>();
        }

        private static List<Slot02AutomaticViewTarget> FindAutomaticSlot02Targets(
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart authoritativeMain
        )
        {
            List<Slot02AutomaticViewTarget> result = new List<Slot02AutomaticViewTarget>();

            if (
                model == null
                || drawing == null
                || authoritativeMain == null
                || authoritativeMain.Identifier == null
            )
                return result;

            TSM.TransformationPlane oldPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();

            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return result;

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    try
                    {
                        model
                            .GetWorkPlaneHandler()
                            .SetCurrentTransformationPlane(
                                new TSM.TransformationPlane(view.DisplayCoordinateSystem)
                            );

                        List<ModelPart> viewParts = GetModelPartsInView(model, view);
                        if (!ContainsModelPart(viewParts, authoritativeMain.Identifier))
                            continue;

                        ModelPart mainInView =
                            model.SelectModelObject(authoritativeMain.Identifier) as ModelPart;
                        Bounds2D mainBox = GetPartBounds2D(mainInView);

                        // Writer Slot02 hien tai co semantic main ngang -> neighbor H doc.
                        // View dau thanh/section khong duoc dua vao auto flow.
                        if (!IsHorizontallyElongated(mainBox))
                            continue;

                        List<ModelPart> plateCandidates = new List<ModelPart>();
                        List<ModelPart> neighborCandidates = new List<ModelPart>();
                        Dictionary<int, Bounds2D> boundsByPartId = new Dictionary<int, Bounds2D>();

                        for (int partIndex = 0; partIndex < viewParts.Count; partIndex++)
                        {
                            ModelPart part = viewParts[partIndex];
                            if (
                                part == null
                                || part.Identifier == null
                                || SameIdentifier(part.Identifier, authoritativeMain.Identifier)
                                || IsDummyReferencePart(part)
                            )
                                continue;

                            Bounds2D partBox = GetPartBounds2D(part);
                            boundsByPartId[part.Identifier.ID] = partBox;

                            if (IsPlatePart(part))
                            {
                                plateCandidates.Add(part);
                            }
                            else if (
                                IsHProfilePart(part)
                                && IsAssemblyMainPart(part)
                                && IsNeighborShownOnFlangeFace(part)
                                && IsVerticallyElongated(partBox)
                            )
                            {
                                neighborCandidates.Add(part);
                            }
                        }

                        Slot02AutomaticViewTarget target = new Slot02AutomaticViewTarget();
                        target.View = view;

                        for (int plateIndex = 0; plateIndex < plateCandidates.Count; plateIndex++)
                        {
                            ModelPart plate = plateCandidates[plateIndex];
                            Bounds2D plateBox = boundsByPartId[plate.Identifier.ID];
                            if (!plateBox.Valid)
                                continue;

                            for (
                                int neighborIndex = 0;
                                neighborIndex < neighborCandidates.Count;
                                neighborIndex++
                            )
                            {
                                ModelPart neighbor = neighborCandidates[neighborIndex];
                                Bounds2D neighborBox = boundsByPartId[neighbor.Identifier.ID];

                                if (
                                    !IsExactAutomaticSlot02Topology(
                                        authoritativeMain,
                                        plate,
                                        neighbor
                                    )
                                )
                                    continue;

                                if (!IsAtMainNeighborInterface(mainBox, plateBox, neighborBox))
                                    continue;

                                AddAutomaticConnection(target, plate, neighbor);
                            }
                        }

                        if (target.Connections.Count > 0)
                            result.Add(target);
                    }
                    catch
                    {
                        // Mot view loi/khong doc duoc khong duoc lam mat cac
                        // cap lien ket chac chan da tim thay trong view khac.
                        continue;
                    }
                }
            }
            catch
            {
                // Giu lai cac target da xac minh neu enumerator dung giua chung.
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }

            return result;
        }

        private static void AddAutomaticConnection(
            Slot02AutomaticViewTarget target,
            ModelPart plate,
            ModelPart neighbor
        )
        {
            if (
                target == null
                || plate == null
                || neighbor == null
                || plate.Identifier == null
                || neighbor.Identifier == null
            )
                return;

            for (int i = 0; i < target.Connections.Count; i++)
            {
                Slot02AutomaticConnection old = target.Connections[i];
                if (
                    old != null
                    && SameIdentifier(old.Plate.Identifier, plate.Identifier)
                    && SameIdentifier(old.Neighbor.Identifier, neighbor.Identifier)
                )
                    return;
            }

            Slot02AutomaticConnection connection = new Slot02AutomaticConnection();
            connection.Plate = plate;
            connection.Neighbor = neighbor;
            target.Connections.Add(connection);
            AddUniqueModelPart(target.Plates, plate);
            AddUniqueModelPart(target.NeighborBeams, neighbor);
        }

        private static bool IsExactAutomaticSlot02Topology(
            ModelPart main,
            ModelPart plate,
            ModelPart neighbor
        )
        {
            if (main == null || plate == null || neighbor == null)
                return false;

            // Semantic Slot02: plate la secondary part cua assembly Main va
            // duoc bolt truc tiep sang neighbor H. Chieu nguoc (plate thuoc
            // assembly neighbor roi bolt vao Main) la mot loai lien ket khac;
            // neu chap nhan se nhan nham hinh chieu o dau Main.
            bool plateInMainAssembly = IsPartInSameAssembly(plate, main);
            bool plateBoltedToNeighbor = ArePartsDirectlyBoltConnected(plate, neighbor);

            return plateInMainAssembly && plateBoltedToNeighbor;
        }

        private static bool IsAtMainNeighborInterface(
            Bounds2D mainBox,
            Bounds2D plateBox,
            Bounds2D neighborBox
        )
        {
            if (!mainBox.Valid || !plateBox.Valid || !neighborBox.Valid)
                return false;

            double mainCenterY = (mainBox.MinY + mainBox.MaxY) / 2.0;
            double neighborCenterX = (neighborBox.MinX + neighborBox.MaxX) / 2.0;
            double mainHeight = Math.Abs(mainBox.MaxY - mainBox.MinY);
            double neighborWidth = Math.Abs(neighborBox.MaxX - neighborBox.MinX);

            double plateToNeighborAxisX = DistanceToInterval(
                neighborCenterX,
                plateBox.MinX,
                plateBox.MaxX
            );
            double plateToMainAxisY = DistanceToInterval(mainCenterY, plateBox.MinY, plateBox.MaxY);
            double neighborToMainAxisY = DistanceToInterval(
                mainCenterY,
                neighborBox.MinY,
                neighborBox.MaxY
            );

            bool neighborFallsOnMainSpan =
                neighborCenterX >= mainBox.MinX - neighborWidth - BOUND_TOL
                && neighborCenterX <= mainBox.MaxX + neighborWidth + BOUND_TOL;

            return neighborFallsOnMainSpan
                && plateToNeighborAxisX <= neighborWidth + BOUND_TOL
                && plateToMainAxisY <= mainHeight + BOUND_TOL
                && neighborToMainAxisY <= mainHeight + BOUND_TOL;
        }

        private static double DistanceToInterval(double value, double min, double max)
        {
            if (value < min)
                return min - value;
            if (value > max)
                return value - max;
            return 0.0;
        }

        private static bool IsHorizontallyElongated(Bounds2D box)
        {
            if (!box.Valid)
                return false;

            double width = Math.Abs(box.MaxX - box.MinX);
            double height = Math.Abs(box.MaxY - box.MinY);
            return width > height * 2.0;
        }

        private static bool IsVerticallyElongated(Bounds2D box)
        {
            if (!box.Valid)
                return false;

            double width = Math.Abs(box.MaxX - box.MinX);
            double height = Math.Abs(box.MaxY - box.MinY);
            return height > width * 2.0;
        }

        private static bool IsHProfilePart(ModelPart part)
        {
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            return profile.StartsWith("H", StringComparison.OrdinalIgnoreCase)
                || profile.StartsWith("I", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNeighborShownOnFlangeFace(ModelPart part)
        {
            try
            {
                if (part == null)
                    return false;

                // Finder da dat current transformation plane = view. Trong he
                // nay truc Z la phap tuyen view; DSTV AxisY cua H/I la phap
                // tuyen mat canh thuc, ke ca khi profile bi rotation/mirror.
                CoordinateSystem partCoordinateSystem = part.GetDSTVCoordinateSystem();
                if (partCoordinateSystem == null)
                    partCoordinateSystem = part.GetCoordinateSystem();
                if (partCoordinateSystem == null || partCoordinateSystem.AxisY == null)
                    return false;

                Vector flangeNormal = partCoordinateSystem.AxisY;
                double length = Math.Sqrt(
                    flangeNormal.X * flangeNormal.X
                        + flangeNormal.Y * flangeNormal.Y
                        + flangeNormal.Z * flangeNormal.Z
                );
                if (length <= TOL * 0.001)
                    return false;

                double alignment = Math.Abs(flangeNormal.Z) / length;
                return alignment >= FLANGE_FACE_MIN_ALIGNMENT;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAssemblyMainPart(ModelPart part)
        {
            try
            {
                if (part == null || part.Identifier == null)
                    return false;

                TSM.Assembly assembly = part.GetAssembly();
                ModelPart assemblyMain =
                    assembly == null ? null : assembly.GetMainPart() as ModelPart;
                return assemblyMain != null
                    && SameIdentifier(assemblyMain.Identifier, part.Identifier);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPartInSameAssembly(ModelPart first, ModelPart second)
        {
            try
            {
                if (first == null || second == null)
                    return false;

                TSM.Assembly firstAssembly = first.GetAssembly();
                TSM.Assembly secondAssembly = second.GetAssembly();
                return firstAssembly != null
                    && secondAssembly != null
                    && SameIdentifier(firstAssembly.Identifier, secondAssembly.Identifier);
            }
            catch
            {
                return false;
            }
        }

        private static bool ArePartsDirectlyBoltConnected(ModelPart first, ModelPart second)
        {
            if (first == null || second == null || second.Identifier == null)
                return false;

            return PartBoltCollectionReferencesPart(first, second)
                || PartBoltCollectionReferencesPart(second, first);
        }

        private static bool PartBoltCollectionReferencesPart(ModelPart owner, ModelPart other)
        {
            try
            {
                if (owner == null || other == null || other.Identifier == null)
                    return false;

                TSM.ModelObjectEnumerator bolts = owner.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    object boltGroup = bolts.Current;
                    if (
                        ObjectOrEnumerableContainsIdentifier(
                            GetPropertyValue(boltGroup, "PartToBoltTo"),
                            other.Identifier
                        )
                        || ObjectOrEnumerableContainsIdentifier(
                            GetPropertyValue(boltGroup, "PartToBeBolted"),
                            other.Identifier
                        )
                        || ObjectOrEnumerableContainsIdentifier(
                            GetPropertyValue(boltGroup, "OtherPartsToBolt"),
                            other.Identifier
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool ObjectOrEnumerableContainsIdentifier(
            object value,
            Identifier identifier
        )
        {
            if (value == null || identifier == null)
                return false;

            ModelObject modelObject = value as ModelObject;
            if (modelObject != null && SameIdentifier(modelObject.Identifier, identifier))
                return true;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return false;

            foreach (object item in enumerable)
            {
                if (ObjectOrEnumerableContainsIdentifier(item, identifier))
                    return true;
            }

            return false;
        }

        private static List<ModelPart> GetModelPartsInView(TSM.Model model, TSD.View view)
        {
            List<ModelPart> result = new List<ModelPart>();

            try
            {
                if (model == null || view == null)
                    return result;

                TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (drawingPart == null || drawingPart.ModelIdentifier == null)
                        continue;

                    ModelPart modelPart =
                        model.SelectModelObject(drawingPart.ModelIdentifier) as ModelPart;
                    AddUniqueModelPart(result, modelPart);
                }
            }
            catch { }

            return result;
        }

        private static bool ContainsModelPart(List<ModelPart> parts, Identifier identifier)
        {
            if (parts == null || identifier == null)
                return false;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && SameIdentifier(parts[i].Identifier, identifier))
                    return true;
            }

            return false;
        }

        private static void AddSemanticNeighborBeamsForPlate(
            ModelPart plate,
            ModelPart authoritativeMain,
            List<ModelPart> neighbors
        )
        {
            if (plate == null || neighbors == null)
                return;

            try
            {
                TSM.Assembly assembly = plate.GetAssembly();
                ModelPart assemblyMain =
                    assembly == null ? null : assembly.GetMainPart() as ModelPart;
                AddNeighborCandidate(assemblyMain, plate, authoritativeMain, neighbors);
            }
            catch { }

            try
            {
                TSM.ModelObjectEnumerator bolts = plate.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    object boltGroup = bolts.Current;
                    AddNeighborCandidatesFromValue(
                        GetPropertyValue(boltGroup, "PartToBoltTo"),
                        plate,
                        authoritativeMain,
                        neighbors
                    );
                    AddNeighborCandidatesFromValue(
                        GetPropertyValue(boltGroup, "PartToBeBolted"),
                        plate,
                        authoritativeMain,
                        neighbors
                    );
                    AddNeighborCandidatesFromValue(
                        GetPropertyValue(boltGroup, "OtherPartsToBolt"),
                        plate,
                        authoritativeMain,
                        neighbors
                    );
                }
            }
            catch { }
        }

        private static void AddNeighborCandidatesFromValue(
            object value,
            ModelPart plate,
            ModelPart authoritativeMain,
            List<ModelPart> neighbors
        )
        {
            if (value == null)
                return;

            ModelPart directPart = value as ModelPart;
            if (directPart != null)
            {
                AddNeighborCandidate(directPart, plate, authoritativeMain, neighbors);
                return;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return;

            foreach (object item in enumerable)
            {
                AddNeighborCandidatesFromValue(item, plate, authoritativeMain, neighbors);
            }
        }

        private static void AddNeighborCandidate(
            ModelPart candidate,
            ModelPart plate,
            ModelPart authoritativeMain,
            List<ModelPart> neighbors
        )
        {
            if (
                candidate == null
                || candidate.Identifier == null
                || plate == null
                || plate.Identifier == null
            )
                return;

            if (
                SameIdentifier(candidate.Identifier, plate.Identifier)
                || (
                    authoritativeMain != null
                    && SameIdentifier(candidate.Identifier, authoritativeMain.Identifier)
                )
                || IsDummyReferencePart(candidate)
                || IsPlatePart(candidate)
            )
            {
                return;
            }

            AddUniqueModelPart(neighbors, candidate);
        }

        private static void AddUniqueModelPart(List<ModelPart> parts, ModelPart candidate)
        {
            if (parts == null || candidate == null || candidate.Identifier == null)
                return;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && SameIdentifier(parts[i].Identifier, candidate.Identifier))
                    return;
            }

            parts.Add(candidate);
        }

        private static bool FindNearestNeighborBeam(
            Point plateCenter,
            List<ModelPart> neighborBeams,
            out ModelPart neighborBeam,
            out Bounds2D neighborBox
        )
        {
            neighborBeam = null;
            neighborBox = new Bounds2D();
            neighborBox.Valid = false;

            if (plateCenter == null || neighborBeams == null || neighborBeams.Count == 0)
                return false;

            double bestDistance = 999999999.0;

            for (int i = 0; i < neighborBeams.Count; i++)
            {
                ModelPart candidate = neighborBeams[i];
                if (candidate == null)
                    continue;

                Bounds2D box = GetPartBounds2D(candidate);
                if (!box.Valid)
                    continue;

                Point center = new Point(
                    (box.MinX + box.MaxX) / 2.0,
                    (box.MinY + box.MaxY) / 2.0,
                    0
                );

                double d = Distance2D(plateCenter, center);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    neighborBeam = candidate;
                    neighborBox = box;
                }
            }

            return neighborBeam != null && neighborBox.Valid;
        }

        private static int ComparePointByXThenY(Point a, Point b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            int c = a.X.CompareTo(b.X);
            if (c != 0)
                return c;
            return a.Y.CompareTo(b.Y);
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

        private static bool TryGetStraightReferenceAxis(
            ModelPart part,
            out Point axisStart,
            out Point axisEnd
        )
        {
            axisStart = null;
            axisEnd = null;

            try
            {
                if (part == null)
                    return false;

                ArrayList rawReference = part.GetReferenceLine(false);
                List<Point> referencePoints = new List<Point>();
                if (rawReference != null)
                {
                    foreach (object value in rawReference)
                    {
                        Point point = value as Point;
                        if (IsFinitePoint2D(point))
                            AddUniquePoint2D(
                                referencePoints,
                                new Point(point.X, point.Y, 0),
                                TOL * 0.01
                            );
                    }
                }

                double longestDistance = 0.0;
                for (int firstIndex = 0; firstIndex < referencePoints.Count; firstIndex++)
                {
                    for (
                        int secondIndex = firstIndex + 1;
                        secondIndex < referencePoints.Count;
                        secondIndex++
                    )
                    {
                        double distance = Distance2D(
                            referencePoints[firstIndex],
                            referencePoints[secondIndex]
                        );
                        if (distance > longestDistance)
                        {
                            longestDistance = distance;
                            axisStart = referencePoints[firstIndex];
                            axisEnd = referencePoints[secondIndex];
                        }
                    }
                }

                if (axisStart == null || axisEnd == null || longestDistance <= TOL)
                    return false;

                // Slot02 chi ho tro reference thang. Farthest pair xac dinh
                // axis khong phu thuoc thu tu point Tekla tra ve; cac point
                // con lai phai nam tren cung axis.
                double axisX = axisEnd.X - axisStart.X;
                double axisY = axisEnd.Y - axisStart.Y;
                double collinearTolerance = Math.Max(TOL, longestDistance * 0.00001);
                for (int pointIndex = 0; pointIndex < referencePoints.Count; pointIndex++)
                {
                    Point point = referencePoints[pointIndex];
                    double signedArea =
                        axisX * (point.Y - axisStart.Y) - axisY * (point.X - axisStart.X);
                    double distanceToAxis = Math.Abs(signedArea) / longestDistance;
                    if (distanceToAxis > collinearTolerance)
                        return false;
                }

                return true;
            }
            catch
            {
                axisStart = null;
                axisEnd = null;
                return false;
            }
        }

        private static bool TryResolveReferenceIntersection(
            Point mainReferenceStart,
            Point mainReferenceEnd,
            ModelPart neighbor,
            Bounds2D mainBox,
            Bounds2D neighborBox,
            out Point intersection
        )
        {
            intersection = null;

            Point neighborReferenceStart;
            Point neighborReferenceEnd;
            if (
                !TryGetStraightReferenceAxis(
                    neighbor,
                    out neighborReferenceStart,
                    out neighborReferenceEnd
                )
            )
                return false;

            // Neighbor của Slot02 có semantic H đứng trong view.
            if (!IsReferenceAxisAligned(neighborReferenceStart, neighborReferenceEnd, false))
                return false;

            Point candidate;
            if (
                !TryIntersectInfiniteReferenceAxes(
                    mainReferenceStart,
                    mainReferenceEnd,
                    neighborReferenceStart,
                    neighborReferenceEnd,
                    out candidate
                )
            )
                return false;

            // Cho phep reference dung tai tim Main trong khi solid neighbor
            // dung o mep Main, nhung loai giao diem nam xa cum lien ket.
            double xTolerance = Math.Max(Math.Abs(neighborBox.MaxX - neighborBox.MinX), BOUND_TOL);
            double yTolerance = Math.Max(Math.Abs(mainBox.MaxY - mainBox.MinY), BOUND_TOL);
            if (
                DistanceToInterval(candidate.X, mainBox.MinX, mainBox.MaxX) > xTolerance
                || DistanceToInterval(candidate.Y, neighborBox.MinY, neighborBox.MaxY) > yTolerance
            )
                return false;

            intersection = candidate;
            return true;
        }

        private static bool TryIntersectInfiniteReferenceAxes(
            Point mainReferenceStart,
            Point mainReferenceEnd,
            Point neighborReferenceStart,
            Point neighborReferenceEnd,
            out Point intersection
        )
        {
            intersection = null;
            if (
                !IsFinitePoint2D(mainReferenceStart)
                || !IsFinitePoint2D(mainReferenceEnd)
                || !IsFinitePoint2D(neighborReferenceStart)
                || !IsFinitePoint2D(neighborReferenceEnd)
            )
                return false;

            double mainX = mainReferenceEnd.X - mainReferenceStart.X;
            double mainY = mainReferenceEnd.Y - mainReferenceStart.Y;
            double neighborX = neighborReferenceEnd.X - neighborReferenceStart.X;
            double neighborY = neighborReferenceEnd.Y - neighborReferenceStart.Y;
            double mainLength = Math.Sqrt(mainX * mainX + mainY * mainY);
            double neighborLength = Math.Sqrt(neighborX * neighborX + neighborY * neighborY);
            if (mainLength <= TOL || neighborLength <= TOL)
                return false;

            double denominator = mainX * neighborY - mainY * neighborX;
            double normalizedCross = Math.Abs(denominator) / (mainLength * neighborLength);
            if (normalizedCross <= 0.000001)
                return false;

            double deltaX = neighborReferenceStart.X - mainReferenceStart.X;
            double deltaY = neighborReferenceStart.Y - mainReferenceStart.Y;
            double mainParameter = (deltaX * neighborY - deltaY * neighborX) / denominator;

            Point candidate = new Point(
                mainReferenceStart.X + mainParameter * mainX,
                mainReferenceStart.Y + mainParameter * mainY,
                0
            );
            if (!IsFinitePoint2D(candidate))
                return false;

            intersection = candidate;
            return true;
        }

        private static bool IsFinitePoint2D(Point point)
        {
            return point != null
                && !Double.IsNaN(point.X)
                && !Double.IsInfinity(point.X)
                && !Double.IsNaN(point.Y)
                && !Double.IsInfinity(point.Y);
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
            catch { }

            return b;
        }

        private static List<Point> GetSolidEdgeVertices2D(ModelPart part)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (part == null)
                    return result;

                Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;

                    if (edge.StartPoint != null)
                    {
                        AddUniquePoint2D(
                            result,
                            new Point(edge.StartPoint.X, edge.StartPoint.Y, 0),
                            0.01
                        );
                    }

                    if (edge.EndPoint != null)
                    {
                        AddUniquePoint2D(
                            result,
                            new Point(edge.EndPoint.X, edge.EndPoint.Y, 0),
                            0.01
                        );
                    }
                }
            }
            catch { }

            return result;
        }

        private static bool TryResolveMainHorizontalRealEdgePoints(
            List<Point> contourVertices,
            bool preferTop,
            out Point leftPoint,
            out Point rightPoint
        )
        {
            leftPoint = null;
            rightPoint = null;

            try
            {
                if (contourVertices == null || contourVertices.Count < 2)
                    return false;

                // DIM đo theo X: trước hết chốt hai X cực trị của toàn contour.
                // Tại mỗi X cực trị, TOP lấy vertex có Y lớn nhất, BOTTOM lấy Y
                // nhỏ nhất. Nhờ vậy chân DIM luôn là vertex thật, kể cả khi bất kỳ
                // góc trái/phải, trên/dưới bị khoét; không thể sinh góc bbox ảo.
                double minX = Double.MaxValue;
                double maxX = Double.MinValue;
                for (int i = 0; i < contourVertices.Count; i++)
                {
                    Point point = contourVertices[i];
                    if (!IsFinitePoint2D(point))
                        continue;

                    if (point.X < minX)
                        minX = point.X;
                    if (point.X > maxX)
                        maxX = point.X;
                }

                if (!IsFinite(minX) || !IsFinite(maxX) || Math.Abs(maxX - minX) <= TOL)
                    return false;

                const double extremeTol = 0.01;
                Point leftAnchor = null;
                Point rightAnchor = null;

                for (int i = 0; i < contourVertices.Count; i++)
                {
                    Point point = contourVertices[i];
                    if (!IsFinitePoint2D(point))
                        continue;

                    if (Math.Abs(point.X - minX) <= extremeTol)
                    {
                        if (
                            leftAnchor == null
                            || (preferTop && point.Y > leftAnchor.Y)
                            || (!preferTop && point.Y < leftAnchor.Y)
                        )
                            leftAnchor = point;
                    }

                    if (Math.Abs(point.X - maxX) <= extremeTol)
                    {
                        if (
                            rightAnchor == null
                            || (preferTop && point.Y > rightAnchor.Y)
                            || (!preferTop && point.Y < rightAnchor.Y)
                        )
                            rightAnchor = point;
                    }
                }

                if (leftAnchor == null || rightAnchor == null)
                    return false;

                leftPoint = new Point(minX, leftAnchor.Y, 0);
                rightPoint = new Point(maxX, rightAnchor.Y, 0);
                return true;
            }
            catch
            {
                leftPoint = null;
                rightPoint = null;
                return false;
            }
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static bool IsReferenceAxisAligned(
            Point axisStart,
            Point axisEnd,
            bool requireHorizontal
        )
        {
            if (!IsFinitePoint2D(axisStart) || !IsFinitePoint2D(axisEnd))
                return false;

            double deltaX = axisEnd.X - axisStart.X;
            double deltaY = axisEnd.Y - axisStart.Y;
            double length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (!IsFinite(length) || length <= TOL)
                return false;

            double alignment = requireHorizontal
                ? Math.Abs(deltaX) / length
                : Math.Abs(deltaY) / length;
            return alignment >= REFERENCE_AXIS_MIN_ALIGNMENT;
        }

        private static Point GetPlateEdgePointTowardNeighbor(
            Bounds2D plateBox,
            Point neighborRef,
            bool isTopGroup
        )
        {
            double x = Clamp(neighborRef.X, plateBox.MinX, plateBox.MaxX);

            double y = isTopGroup ? plateBox.MaxY : plateBox.MinY;

            return new Point(x, y, 0);
        }

        private static double GetHorizontalDistanceFromOuterBoundary(
            Vector direction,
            Point firstDimPoint,
            double minY,
            double maxY,
            double tier
        )
        {
            return tier;
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance
        )
        {
            return CreateDimChain(handler, view, points, direction, distance, null);
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            string attributeName
        )
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

            TSD.StraightDimensionSet dim = handler.CreateDimensionSet(
                view,
                list,
                direction,
                distance
            );

            if (dim != null && !string.IsNullOrEmpty(attributeName))
                TryApplyStraightDimAttributes(dim, attributeName);

            return dim != null;
        }

        private static void TryApplyStraightDimAttributes(
            TSD.StraightDimensionSet dim,
            string attributeName
        )
        {
            try
            {
                if (dim == null || string.IsNullOrEmpty(attributeName))
                    return;

                object attr = dim.Attributes;
                if (attr == null)
                    return;

                MethodInfo loadMethod = attr.GetType()
                    .GetMethod(
                        "LoadAttributes",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string) },
                        null
                    );

                if (loadMethod == null)
                    return;

                loadMethod.Invoke(attr, new object[] { attributeName });
                dim.Modify();
            }
            catch { }
        }

        private static List<DrawingPart> GetSelectedDrawingParts(TSD.DrawingHandler dh)
        {
            List<DrawingPart> result = new List<DrawingPart>();

            try
            {
                TSD.DrawingObjectEnumerator e = dh.GetDrawingObjectSelector().GetSelected();

                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp != null)
                        result.Add(dp);
                }
            }
            catch { }

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

            string[] methodNames = new string[] { "GetView", "GetFatherView", "GetParentView" };

            for (int i = 0; i < methodNames.Length; i++)
            {
                try
                {
                    MethodInfo m = drawingObject
                        .GetType()
                        .GetMethod(
                            methodNames[i],
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            Type.EmptyTypes,
                            null
                        );

                    if (m == null)
                        continue;

                    object value = m.Invoke(drawingObject, null);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch { }
            }

            string[] propertyNames = new string[] { "View", "FatherView", "ParentView" };

            for (int i = 0; i < propertyNames.Length; i++)
            {
                try
                {
                    object value = GetPropertyValue(drawingObject, propertyNames[i]);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch { }
            }

            return null;
        }

        private static TSD.View FindViewContainingParts(
            TSD.Drawing drawing,
            Identifier id1,
            Identifier id2,
            Identifier id3
        )
        {
            try
            {
                if (drawing == null || id1 == null || id2 == null || id3 == null)
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
                    bool has3 = false;

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
                        if (SameIdentifier(dp.ModelIdentifier, id3))
                            has3 = true;

                        if (has1 && has2 && has3)
                            return view;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool ViewContainsPart(TSD.View view, Identifier identifier)
        {
            try
            {
                if (view == null || identifier == null)
                    return false;

                TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (
                        drawingPart != null
                        && SameIdentifier(drawingPart.ModelIdentifier, identifier)
                    )
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            if (part == null)
                return false;

            string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();
            string material = GetReportString(part, "MATERIAL").Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (
                partPos == "DUMMY-99"
                || partPos.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase)
                || material == "JOINT"
                || name.StartsWith("BJ", StringComparison.OrdinalIgnoreCase)
            )
                return true;

            return false;
        }

        private static bool IsPlatePart(ModelPart part)
        {
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(profile))
                return false;

            if (
                profile.StartsWith("PL")
                || profile.StartsWith("PLT")
                || profile.StartsWith("FB")
                || profile.StartsWith("FL")
                || profile.IndexOf("PLATE") >= 0
            )
                return true;

            if (
                profile.IndexOf("H") == 0
                || profile.IndexOf("I") == 0
                || profile.IndexOf("C") == 0
                || profile.IndexOf("L") == 0
                || profile.IndexOf("RHS") >= 0
                || profile.IndexOf("SHS") >= 0
                || profile.IndexOf("PIPE") >= 0
            )
                return false;

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
            catch { }

            string value = "";
            try
            {
                if (part.GetReportProperty("PROFILE", ref value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            catch { }

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

                PropertyInfo p = obj.GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static double Min(double a, double b, double c)
        {
            return Math.Min(a, Math.Min(b, c));
        }

        private static double Max(double a, double b, double c)
        {
            return Math.Max(a, Math.Max(b, c));
        }

        private static void Msg(string text)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    text,
                    "PHU Slot02 Neighbor Ref Plate Dim",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information
                );
            }
            catch { }
        }
    }
}
