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
            if (selectedParts.Count < 3)
            {
                Msg("Slot02: Hãy chọn ít nhất 3 part trong drawing: 1 main thép hình + 1 hoặc nhiều neighbor thép hình + 1 hoặc nhiều plate.");
                return;
            }

            List<DrawingPart> plateDrawingParts = new List<DrawingPart>();
            List<ModelPart> plates = new List<ModelPart>();

            List<DrawingPart> beamDrawingParts = new List<DrawingPart>();
            List<ModelPart> beams = new List<ModelPart>();

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
                    beams.Add(mp);
                    beamDrawingParts.Add(dp);
                }
            }

            if (plates.Count == 0 || beams.Count < 2)
            {
                Msg("Slot02: Không nhận diện đủ plate và thép hình. Cần 1 main + 1 hoặc nhiều neighbor + 1 hoặc nhiều plate.");
                return;
            }

            ModelPart mainBeam = null;
            DrawingPart mainDrawingPart = null;
            PickMainBeamByPlateAssemblies(plates, beams, beamDrawingParts, out mainBeam, out mainDrawingPart);

            if (mainBeam == null)
            {
                Msg("Slot02: Không xác định được main beam.");
                return;
            }

            List<ModelPart> neighborBeams = new List<ModelPart>();
            List<DrawingPart> neighborDrawingParts = new List<DrawingPart>();
            for (int i = 0; i < beams.Count; i++)
            {
                if (SameIdentifier(beams[i].Identifier, mainBeam.Identifier))
                    continue;

                neighborBeams.Add(beams[i]);
                if (i < beamDrawingParts.Count)
                    neighborDrawingParts.Add(beamDrawingParts[i]);
            }

            if (neighborBeams.Count == 0)
            {
                Msg("Slot02: Không xác định được neighbor beam.");
                return;
            }

            DrawingPart firstPlateDrawingPart = plateDrawingParts.Count > 0 ? plateDrawingParts[0] : null;
            DrawingPart firstNeighborDrawingPart = neighborDrawingParts.Count > 0 ? neighborDrawingParts[0] : null;

            TSD.View view = TryGetSelectedPartsView(firstPlateDrawingPart, mainDrawingPart, firstNeighborDrawingPart);
            if (view == null)
                view = FindViewContainingParts(drawing, plates[0].Identifier, mainBeam.Identifier, neighborBeams[0].Identifier);

            if (view == null)
            {
                Msg("Slot02: Không tìm thấy view chứa đủ plate, main beam và neighbor beam đã chọn.");
                return;
            }

            int created = CreateNeighborPlateReferenceDims(model, view, mainBeam, neighborBeams, plates);

            try { drawing.CommitChanges(); } catch { }

            //Msg("Slot02 DONE. DIM đã tạo: " + created.ToString());  Tắt popup debug
        }

        private static int CreateNeighborPlateReferenceDims(
            TSM.Model model,
            TSD.View view,
            ModelPart mainBeam,
            List<ModelPart> neighborBeams,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || view == null || mainBeam == null || neighborBeams == null || neighborBeams.Count == 0 || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainBeam);
                if (!mainBox.Valid)
                    return count;

                Point mainCenter = new Point(
                    (mainBox.MinX + mainBox.MaxX) / 2.0,
                    (mainBox.MinY + mainBox.MaxY) / 2.0,
                    0);

                List<NeighborPlateGroup> groups = new List<NeighborPlateGroup>();

                double allMinY = mainBox.MinY;
                double allMaxY = mainBox.MaxY;

                for (int i = 0; i < neighborBeams.Count; i++)
                {
                    Bounds2D nb = GetPartBounds2D(neighborBeams[i]);
                    if (!nb.Valid)
                        continue;

                    if (nb.MinY < allMinY) allMinY = nb.MinY;
                    if (nb.MaxY > allMaxY) allMaxY = nb.MaxY;
                }

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart plate = plates[i];
                    if (plate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid)
                        continue;

                    Point plateCenter = new Point(
                        (plateBox.MinX + plateBox.MaxX) / 2.0,
                        (plateBox.MinY + plateBox.MaxY) / 2.0,
                        0);

                    ModelPart neighborBeam;
                    Bounds2D neighborBox;
                    if (!FindNearestNeighborBeam(plateCenter, neighborBeams, out neighborBeam, out neighborBox))
                        continue;

                    Point neighborBoxCenter = new Point(
                        (neighborBox.MinX + neighborBox.MaxX) / 2.0,
                        (neighborBox.MinY + neighborBox.MaxY) / 2.0,
                        0);

                    // Neighbor reference chân DIM = giao giữa ref neighbor (X tâm neighbor) và ref main chính (Y tâm main).
                    Point neighborRef = new Point(neighborBoxCenter.X, mainCenter.Y, 0);

                    bool dimToTop = plateCenter.Y >= mainCenter.Y;
                    Vector direction = dimToTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0);

                    Point plateEdge = GetPlateEdgePointTowardNeighbor(plateBox, neighborRef, dimToTop);

                    if (plateBox.MinY < allMinY) allMinY = plateBox.MinY;
                    if (plateBox.MaxY > allMaxY) allMaxY = plateBox.MaxY;

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
                    g.AttributeName = plateCenter.X >= neighborRef.X
                        ? "GEO_HIGE_RIGHT"
                        : "GEO_HIGE_LEFT";

                    groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

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
                        NEIGHBOR_TO_PLATE_TIER);

                    if (CreateDimChain(
                        handler,
                        view,
                        new Point[]
                        {
                            g.NeighborRef,
                            g.PlateEdge
                        },
                        g.Direction,
                        distanceNeighborToPlate,
                        g.AttributeName))
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
                    double mainEdgeY = mainBox.MaxY;
                    Point mainLeftEdge = new Point(mainBox.MinX, mainEdgeY, 0);
                    Point mainRightEdge = new Point(mainBox.MaxX, mainEdgeY, 0);

                    List<Point> chain = new List<Point>();
                    chain.Add(mainLeftEdge);
                    for (int i = 0; i < topRefs.Count; i++)
                        chain.Add(topRefs[i]);
                    chain.Add(mainRightEdge);

                    double distanceMainToNeighbor = GetHorizontalDistanceFromOuterBoundary(
                        direction,
                        mainLeftEdge,
                        allMinY,
                        allMaxY,
                        MAIN_TO_NEIGHBOR_TIER);

                    if (CreateDimChain(
                        handler,
                        view,
                        chain.ToArray(),
                        direction,
                        distanceMainToNeighbor))
                    {
                        count++;
                    }
                }

                if (bottomRefs.Count > 0)
                {
                    Vector direction = new Vector(0, -1, 0);
                    double mainEdgeY = mainBox.MinY;
                    Point mainLeftEdge = new Point(mainBox.MinX, mainEdgeY, 0);
                    Point mainRightEdge = new Point(mainBox.MaxX, mainEdgeY, 0);

                    List<Point> chain = new List<Point>();
                    chain.Add(mainLeftEdge);
                    for (int i = 0; i < bottomRefs.Count; i++)
                        chain.Add(bottomRefs[i]);
                    chain.Add(mainRightEdge);

                    double distanceMainToNeighbor = GetHorizontalDistanceFromOuterBoundary(
                        direction,
                        mainLeftEdge,
                        allMinY,
                        allMaxY,
                        MAIN_TO_NEIGHBOR_TIER);

                    if (CreateDimChain(
                        handler,
                        view,
                        chain.ToArray(),
                        direction,
                        distanceMainToNeighbor))
                    {
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot02 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
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

        private static void PickMainBeamByPlateAssemblies(
            List<ModelPart> plates,
            List<ModelPart> beams,
            List<DrawingPart> beamDrawingParts,
            out ModelPart mainBeam,
            out DrawingPart mainDrawingPart)
        {
            mainBeam = null;
            mainDrawingPart = null;

            int bestScore = -1;
            int bestIndex = -1;

            for (int i = 0; i < beams.Count; i++)
            {
                ModelPart beam = beams[i];
                if (beam == null)
                    continue;

                string beamAssembly = GetReportString(beam, "ASSEMBLY_POS");
                int score = 0;

                for (int p = 0; p < plates.Count; p++)
                {
                    string plateAssembly = GetReportString(plates[p], "ASSEMBLY_POS");
                    if (!string.IsNullOrEmpty(plateAssembly) &&
                        !string.IsNullOrEmpty(beamAssembly) &&
                        string.Equals(plateAssembly, beamAssembly, StringComparison.OrdinalIgnoreCase))
                    {
                        score++;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestScore <= 0)
            {
                double bestArea = -1.0;
                bestIndex = 0;

                for (int i = 0; i < beams.Count; i++)
                {
                    Bounds2D box = GetPartBounds2D(beams[i]);
                    double area = box.Valid ? Math.Abs(box.MaxX - box.MinX) * Math.Abs(box.MaxY - box.MinY) : 0.0;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex >= 0 && bestIndex < beams.Count)
            {
                mainBeam = beams[bestIndex];
                if (bestIndex < beamDrawingParts.Count)
                    mainDrawingPart = beamDrawingParts[bestIndex];
            }
        }

        private static bool FindNearestNeighborBeam(
            Point plateCenter,
            List<ModelPart> neighborBeams,
            out ModelPart neighborBeam,
            out Bounds2D neighborBox)
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
                    0);

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
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;
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

        private static Point GetPlateEdgePointTowardNeighbor(Bounds2D plateBox, Point neighborRef, bool isTopGroup)
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
            double tier)
        {
            return tier;
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance)
        {
            return CreateDimChain(handler, view, points, direction, distance, null);
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            string attributeName)
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

            TSD.StraightDimensionSet dim =
                handler.CreateDimensionSet(view, list, direction, distance);

            if (dim != null && !string.IsNullOrEmpty(attributeName))
                TryApplyStraightDimAttributes(dim, attributeName);

            return dim != null;
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

        private static void PickMainAndNeighborByAssembly(
            ModelPart plate,
            List<ModelPart> beams,
            List<DrawingPart> beamDrawingParts,
            out ModelPart mainBeam,
            out DrawingPart mainDrawingPart,
            out ModelPart neighborBeam,
            out DrawingPart neighborDrawingPart)
        {
            mainBeam = null;
            neighborBeam = null;
            mainDrawingPart = null;
            neighborDrawingPart = null;

            string plateAssembly = GetReportString(plate, "ASSEMBLY_POS");

            for (int i = 0; i < beams.Count; i++)
            {
                ModelPart b = beams[i];
                string beamAssembly = GetReportString(b, "ASSEMBLY_POS");

                if (!string.IsNullOrEmpty(plateAssembly) &&
                    !string.IsNullOrEmpty(beamAssembly) &&
                    string.Equals(plateAssembly, beamAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    mainBeam = b;
                    if (i < beamDrawingParts.Count)
                        mainDrawingPart = beamDrawingParts[i];
                    break;
                }
            }

            if (mainBeam == null && beams.Count > 0)
            {
                // Fallback: chọn beam có hộp bao lớn hơn làm main.
                double bestArea = -1.0;
                int bestIndex = 0;

                for (int i = 0; i < beams.Count; i++)
                {
                    Bounds2D box = GetPartBounds2D(beams[i]);
                    double area = box.Valid ? Math.Abs(box.MaxX - box.MinX) * Math.Abs(box.MaxY - box.MinY) : 0.0;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestIndex = i;
                    }
                }

                mainBeam = beams[bestIndex];
                if (bestIndex < beamDrawingParts.Count)
                    mainDrawingPart = beamDrawingParts[bestIndex];
            }

            for (int i = 0; i < beams.Count; i++)
            {
                if (mainBeam != null && SameIdentifier(beams[i].Identifier, mainBeam.Identifier))
                    continue;

                neighborBeam = beams[i];
                if (i < beamDrawingParts.Count)
                    neighborDrawingPart = beamDrawingParts[i];
                break;
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

        private static TSD.View FindViewContainingParts(
            TSD.Drawing drawing,
            Identifier id1,
            Identifier id2,
            Identifier id3)
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

                        if (SameIdentifier(dp.ModelIdentifier, id1)) has1 = true;
                        if (SameIdentifier(dp.ModelIdentifier, id2)) has2 = true;
                        if (SameIdentifier(dp.ModelIdentifier, id3)) has3 = true;

                        if (has1 && has2 && has3)
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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
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
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
