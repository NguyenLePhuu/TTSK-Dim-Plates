#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Nishi Azabu one-click dimensioning. This implementation is isolated from
    /// every existing Auto Dimension slot.
    /// </summary>
    public class PHU_NishiAzabuAutoDim
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunMessage = string.Empty;
            string message = PHU_NishiAzabuDimensionEngine.Run();
            if (!String.IsNullOrEmpty(message))
            {
                LastRunSucceeded = true;
                LastRunMessage = message;
            }
        }

        // Read-only entry point used to validate a live drawing before mutation.
        public static string AuditPlan()
        {
            return PHU_NishiAzabuDimensionEngine.AuditPlan();
        }
    }

    internal static class PHU_NishiAzabuDimensionEngine
    {
        private const int BaseDimensionSetCount = 31;
        private const int MaximumOffsetDimensionCount = 3;
        private const double GeometryTolerance = 0.05;
        private const double AxisAlignmentTolerance = 0.5;

        private enum DimSide
        {
            Left,
            Right,
            Top,
            Bottom
        }

        private sealed class P2
        {
            public double X;
            public double Y;

            public P2(double x, double y)
            {
                X = x;
                Y = y;
            }

            public TSG.Point ToPoint()
            {
                return new TSG.Point(X, Y, 0.0);
            }
        }

        private sealed class ProjectedPart
        {
            public TSM.Part ModelPart;
            public int ModelId;
            public bool IsMain;
            public string Name;
            public string Profile;
            public string Position;
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<P2> Bolts = new List<P2>();
            public double MinX = Double.PositiveInfinity;
            public double MaxX = Double.NegativeInfinity;
            public double MinY = Double.PositiveInfinity;
            public double MaxY = Double.NegativeInfinity;

            public double CenterX { get { return (MinX + MaxX) * 0.5; } }
            public double CenterY { get { return (MinY + MaxY) * 0.5; } }
            public double Width { get { return MaxX - MinX; } }
            public double Height { get { return MaxY - MinY; } }

            public bool HasBounds
            {
                get
                {
                    return IsFinite(MinX) && IsFinite(MaxX) &&
                           IsFinite(MinY) && IsFinite(MaxY) &&
                           MaxX > MinX + GeometryTolerance &&
                           MaxY > MinY + GeometryTolerance;
                }
            }

            public void AddVertex(P2 point)
            {
                if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                    return;

                AddUnique(Vertices, point);
                MinX = Math.Min(MinX, point.X);
                MaxX = Math.Max(MaxX, point.X);
                MinY = Math.Min(MinY, point.Y);
                MaxY = Math.Max(MaxY, point.Y);
            }

            public void AddBolt(P2 point)
            {
                AddUnique(Bolts, point);
            }
        }

        private sealed class GeometryGroup
        {
            public readonly List<ProjectedPart> Parts = new List<ProjectedPart>();
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<P2> Bolts = new List<P2>();
            public double MinX = Double.PositiveInfinity;
            public double MaxX = Double.NegativeInfinity;
            public double MinY = Double.PositiveInfinity;
            public double MaxY = Double.NegativeInfinity;

            public double CenterX { get { return (MinX + MaxX) * 0.5; } }
            public double CenterY { get { return (MinY + MaxY) * 0.5; } }
            public double Width { get { return MaxX - MinX; } }
            public double Height { get { return MaxY - MinY; } }
            public bool IsValid { get { return Parts.Count > 0 && IsFinite(MinX); } }

            public void Add(ProjectedPart part)
            {
                if (part == null || !part.HasBounds)
                    return;

                Parts.Add(part);
                MinX = Math.Min(MinX, part.MinX);
                MaxX = Math.Max(MaxX, part.MaxX);
                MinY = Math.Min(MinY, part.MinY);
                MaxY = Math.Max(MaxY, part.MaxY);

                for (int i = 0; i < part.Vertices.Count; i++)
                    AddUnique(Vertices, part.Vertices[i]);
                for (int i = 0; i < part.Bolts.Count; i++)
                    AddUnique(Bolts, part.Bolts[i]);
            }
        }

        private sealed class ViewGeometry
        {
            public string Key;
            public TSD.View View;
            public double Scale;
            public double TierStep;
            public readonly List<ProjectedPart> Parts = new List<ProjectedPart>();
            public readonly List<double> VerticalGridCoordinates = new List<double>();
            public readonly List<double> HorizontalGridCoordinates = new List<double>();
            public ProjectedPart Main;
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes DimensionAttributes;
        }

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.DrawingHandler DrawingHandler;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public readonly Dictionary<string, ViewGeometry> Views =
                new Dictionary<string, ViewGeometry>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Errors = new List<string>();
        }

        private sealed class DimPlan
        {
            public string Name;
            public ViewGeometry View;
            public DimSide Side;
            public double LineCoordinate;
            public bool ShowEqualSegmentsSeparately;
            public bool DisableCombine;
            public readonly List<P2> Points = new List<P2>();
        }

        internal static string RunSlot07()
        {
            List<TSD.StraightDimensionSet> created =
                new List<TSD.StraightDimensionSet>();
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildAllPlansSlot07(context);
                ValidateReadySlot07(context, plans);
                List<TSD.DrawingObject> backgroundDimensions =
                    CollectBackgroundDimensions(context.Views);
                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList pointList = new TSD.PointList();
                    for (int p = 0; p < plan.Points.Count; p++)
                        pointList.Add(plan.Points[p].ToPoint());

                    TSD.StraightDimensionSet dimension =
                        plan.View.DimensionAttributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View, pointList,
                                GetDirection(plan.Side), GetDistance(plan))
                            : handler.CreateDimensionSet(
                                plan.View.View, pointList,
                                GetDirection(plan.Side), GetDistance(plan),
                                plan.View.DimensionAttributes);
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla khong tao duoc " + plan.Name + ".");

                    created.Add(dimension);
                    if (plan.DisableCombine || plan.ShowEqualSegmentsSeparately)
                        ForceSeparateEqualSegmentLabels(plan, dimension);
                }

                if (created.Count != plans.Count)
                    throw new InvalidOperationException(
                        "So dim tao thu khong khop plan da kiem tra.");

                int deleted = 0;
                for (int i = 0; i < backgroundDimensions.Count; i++)
                {
                    TSD.DrawingObject item = backgroundDimensions[i];
                    if (item != null && item.Delete())
                        deleted++;
                }
                if (deleted != backgroundDimensions.Count)
                    throw new InvalidOperationException(
                        "Khong xoa het dim nen; da dung truoc CommitChanges.");

                context.Drawing.CommitChanges();
                return "Tạo " + created.Count +
                    " dim trên 4 view, Xóa " + deleted + " dim nền";
            }
            catch (Exception ex)
            {
                for (int i = 0; i < created.Count; i++)
                {
                    try
                    {
                        if (created[i] != null)
                            created[i].Delete();
                    }
                    catch
                    {
                    }
                }
                ShowMessage(
                    "Nishi Azabu slot 07 da dung an toan.\r\n\r\n" + ex.Message,
                    MessageBoxIcon.Warning);
                return null;
            }
        }

        internal static string AuditPlanSlot07()
        {
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildAllPlansSlot07(context);
                ValidateReadySlot07(context, plans);
                StringBuilder text = new StringBuilder();
                text.AppendLine("NISHI AZABU SLOT 07 PLAN AUDIT - READ ONLY");
                text.AppendLine("No dimension was created, deleted or modified.");
                text.AppendLine("PlanCount=" + plans.Count);
                text.AppendLine("BackgroundDimensionCount=" +
                    CollectBackgroundDimensions(context.Views).Count);
                foreach (KeyValuePair<string, ViewGeometry> pair in context.Views)
                {
                    text.Append(pair.Key).Append(" scale=")
                        .Append(Format(pair.Value.Scale)).Append(" parts=")
                        .Append(pair.Value.Parts.Count).AppendLine();
                }
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append(plan.Name).Append(" side=").Append(plan.Side)
                        .Append(" line=").Append(Format(plan.LineCoordinate))
                        .Append(" distance=").Append(Format(GetDistance(plan)))
                        .Append(" combine=")
                        .Append(
                            plan.DisableCombine || plan.ShowEqualSegmentsSeparately
                                ? "Off"
                                : "Sample")
                        .Append(" points=");
                    for (int p = 0; p < plan.Points.Count; p++)
                    {
                        if (p > 0) text.Append(";");
                        text.Append(FormatPoint(plan.Points[p]));
                    }
                    text.AppendLine();
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "NISHI AZABU SLOT 07 PLAN AUDIT FAILED\r\n" + ex;
            }
        }

        public static string Run()
        {
            List<TSD.StraightDimensionSet> created =
                new List<TSD.StraightDimensionSet>();

            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildAllPlans(context);
                ValidateReady(context, plans);

                // Snapshot only. Existing dimensions remain untouched until all
                // replacement dimension sets have been created successfully.
                List<TSD.DrawingObject> backgroundDimensions =
                    CollectBackgroundDimensions(context.Views);

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList pointList = new TSD.PointList();
                    for (int p = 0; p < plan.Points.Count; p++)
                        pointList.Add(plan.Points[p].ToPoint());

                    TSG.Vector direction = GetDirection(plan.Side);
                    double distance = GetDistance(plan);
                    TSD.StraightDimensionSet dimension =
                        plan.View.DimensionAttributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                pointList,
                                direction,
                                distance)
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                pointList,
                                direction,
                                distance,
                                plan.View.DimensionAttributes);

                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla không tạo được " + plan.Name + ".");

                    created.Add(dimension);
                    if (plan.ShowEqualSegmentsSeparately)
                        ForceSeparateEqualSegmentLabels(plan, dimension);
                }

                if (created.Count != plans.Count)
                    throw new InvalidOperationException(
                        "Số dim tạo thử không khớp plan đã kiểm tra.");

                int deleted = 0;
                for (int i = 0; i < backgroundDimensions.Count; i++)
                {
                    TSD.DrawingObject item = backgroundDimensions[i];
                    if (item != null && item.Delete())
                        deleted++;
                }

                if (deleted != backgroundDimensions.Count)
                    throw new InvalidOperationException(
                        "Không xóa hết dim nền; đã dừng trước CommitChanges.");

                context.Drawing.CommitChanges();
                return "Tạo " + created.Count +
                    " dim trên 4 view, Xóa " + deleted + " dim nền";
            }
            catch (Exception ex)
            {
                // Best-effort cleanup of new, uncommitted dimensions. Old
                // dimensions were not touched unless every new set existed.
                for (int i = 0; i < created.Count; i++)
                {
                    try
                    {
                        if (created[i] != null)
                            created[i].Delete();
                    }
                    catch
                    {
                    }
                }

                ShowMessage(
                    "Nishi Azabu đã dừng an toàn.\r\n\r\n" + ex.Message,
                    MessageBoxIcon.Warning);
                return null;
            }
        }

        public static string AuditPlan()
        {
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildAllPlans(context);
                ValidateReady(context, plans);

                StringBuilder text = new StringBuilder();
                text.AppendLine("NISHI AZABU PLAN AUDIT - READ ONLY");
                text.AppendLine("No dimension was created, deleted or modified.");
                text.AppendLine("PlanCount=" + plans.Count);
                text.AppendLine("BackgroundDimensionCount=" +
                    CollectBackgroundDimensions(context.Views).Count);
                foreach (KeyValuePair<string, ViewGeometry> pair in context.Views)
                {
                    text.Append(pair.Key).Append(" scale=")
                        .Append(Format(pair.Value.Scale)).Append(" parts=")
                        .Append(pair.Value.Parts.Count).AppendLine();
                }

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append(plan.Name).Append(" side=").Append(plan.Side)
                        .Append(" line=").Append(Format(plan.LineCoordinate))
                        .Append(" distance=").Append(Format(GetDistance(plan)))
                        .Append(" points=");
                    for (int p = 0; p < plan.Points.Count; p++)
                    {
                        if (p > 0) text.Append(";");
                        text.Append(FormatPoint(plan.Points[p]));
                    }
                    text.AppendLine();
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "NISHI AZABU PLAN AUDIT FAILED\r\n" + ex.ToString();
            }
        }

        private static Context AnalyzeDrawing()
        {
            Context context = new Context();
            context.Model = new TSM.Model();
            context.DrawingHandler = new TSD.DrawingHandler();

            if (!context.Model.GetConnectionStatus() ||
                !context.DrawingHandler.GetConnectionStatus())
                throw new InvalidOperationException(
                    "Không kết nối được Tekla Model/Drawing API.");

            context.Drawing = context.DrawingHandler.GetActiveDrawing();
            if (context.Drawing == null)
                throw new InvalidOperationException("Không có bản vẽ đang mở.");

            context.MainPart = GetDrawingMainPart(context.Model, context.Drawing);
            if (context.MainPart == null)
                throw new InvalidOperationException(
                    "Bản vẽ phải là AssemblyDrawing và phải xác định được main part.");

            TSM.TransformationPlane currentPlane = context.Model
                .GetWorkPlaneHandler().GetCurrentTransformationPlane();
            TSG.Matrix currentToGlobal = currentPlane.TransformationMatrixToGlobal;

            TSD.ContainerView sheet = context.Drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
            while (views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null)
                    continue;

                string key = GetViewKey(view);
                if (String.IsNullOrEmpty(key))
                    continue;

                if (context.Views.ContainsKey(key))
                {
                    context.Errors.Add("Có nhiều hơn một view " + key + ".");
                    continue;
                }

                ViewGeometry geometry = ReadViewGeometry(
                    context.Model,
                    context.MainPart,
                    view,
                    key,
                    currentToGlobal);
                context.Views.Add(key, geometry);
            }

            string[] required = { "A", "B", "C", "FRONT" };
            for (int i = 0; i < required.Length; i++)
            {
                if (!context.Views.ContainsKey(required[i]))
                    context.Errors.Add("Thiếu view " + required[i] + ".");
            }

            return context;
        }

        private static ViewGeometry ReadViewGeometry(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            string key,
            TSG.Matrix currentToGlobal)
        {
            ViewGeometry result = new ViewGeometry();
            result.Key = key;
            result.View = view;
            result.Scale = ReadViewScale(view);
            // The approved Nishi sample uses a 15 mm paper-space tier rhythm.
            // Convert that visual spacing to model coordinates per view scale.
            result.TierStep = result.Scale * 15.0;
            result.DimensionAttributes = ReadDimensionAttributes(view);

            TSG.Matrix globalToView =
                TSG.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            TSD.DrawingObjectEnumerator drawingParts =
                view.GetAllObjects(typeof(TSD.Part));

            while (drawingParts.MoveNext())
            {
                TSD.Part drawingPart = drawingParts.Current as TSD.Part;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;

                TSM.Part modelPart =
                    model.SelectModelObject(drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null)
                    continue;

                ProjectedPart projected = new ProjectedPart();
                projected.ModelPart = modelPart;
                projected.ModelId = modelPart.Identifier.ID;
                projected.IsMain = SameIdentifier(modelPart, mainPart);
                projected.Name = SafeUpper(modelPart.Name);
                projected.Profile = SafeUpper(
                    modelPart.Profile == null ? "" : modelPart.Profile.ProfileString);
                projected.Position = SafeUpper(GetPartPosition(modelPart));

                ReadPartVertices(modelPart, projected, currentToGlobal, globalToView);
                ReadPartBolts(modelPart, projected, currentToGlobal, globalToView);
                if (!projected.HasBounds)
                    continue;

                result.Parts.Add(projected);
                if (projected.IsMain)
                    result.Main = projected;
            }

            ReadGridReferences(
                model,
                view,
                result,
                currentToGlobal,
                globalToView);

            if (result.Main == null)
                throw new InvalidOperationException("View " + key + " không chứa main part.");
            if (!IsFinite(result.Scale) || result.Scale <= 0.0)
                throw new InvalidOperationException("Không đọc được scale của view " + key + ".");

            return result;
        }

        private static void ReadGridReferences(
            TSM.Model model,
            TSD.View view,
            ViewGeometry result,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            try
            {
                TSD.DrawingObjectEnumerator gridLines =
                    view.GetAllObjects(typeof(TSD.GridLine));
                HashSet<int> seen = new HashSet<int>();
                while (gridLines != null && gridLines.MoveNext())
                {
                    TSD.GridLine drawingGridLine = gridLines.Current as TSD.GridLine;
                    if (drawingGridLine == null ||
                        drawingGridLine.ModelIdentifier == null ||
                        !seen.Add(drawingGridLine.ModelIdentifier.ID))
                        continue;

                    TSM.GridPlane gridPlane = model.SelectModelObject(
                        drawingGridLine.ModelIdentifier) as TSM.GridPlane;
                    if (gridPlane == null || gridPlane.Plane == null ||
                        gridPlane.Plane.Origin == null)
                        continue;

                    TSG.Point origin = gridPlane.Plane.Origin;
                    P2 projectedOrigin = Transform(
                        origin, currentToGlobal, globalToView);
                    P2 projectedX = Transform(
                        AddVector(origin, gridPlane.Plane.AxisX),
                        currentToGlobal,
                        globalToView);
                    P2 projectedY = Transform(
                        AddVector(origin, gridPlane.Plane.AxisY),
                        currentToGlobal,
                        globalToView);

                    P2 lineVector = Distance(projectedOrigin, projectedX) >=
                        Distance(projectedOrigin, projectedY)
                            ? Pt(
                                projectedX.X - projectedOrigin.X,
                                projectedX.Y - projectedOrigin.Y)
                            : Pt(
                                projectedY.X - projectedOrigin.X,
                                projectedY.Y - projectedOrigin.Y);

                    if (Math.Abs(lineVector.Y) > Math.Abs(lineVector.X) * 10.0)
                    {
                        AddUniqueCoordinate(
                            result.VerticalGridCoordinates,
                            projectedOrigin.X);
                    }
                    else if (Math.Abs(lineVector.X) > Math.Abs(lineVector.Y) * 10.0)
                    {
                        AddUniqueCoordinate(
                            result.HorizontalGridCoordinates,
                            projectedOrigin.Y);
                    }
                }
            }
            catch
            {
                // A missing or hidden grid is valid. Offset dimensions are
                // optional and will simply not be planned for that view.
            }
        }

        private static TSG.Point AddVector(TSG.Point point, TSG.Vector vector)
        {
            return new TSG.Point(
                point.X + vector.X,
                point.Y + vector.Y,
                point.Z + vector.Z);
        }

        private static void AddUniqueCoordinate(List<double> values, double value)
        {
            if (!IsFinite(value))
                return;
            for (int i = 0; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - value) <= GeometryTolerance)
                    return;
            }
            values.Add(value);
        }

        private static bool TryGetClosestCoordinate(
            List<double> values,
            double target,
            out double coordinate)
        {
            coordinate = Double.NaN;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < values.Count; i++)
            {
                double distance = Math.Abs(values[i] - target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    coordinate = values[i];
                }
            }
            return IsFinite(coordinate);
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes
            ReadDimensionAttributes(TSD.View view)
        {
            try
            {
                Type straightType = typeof(TSD.StraightDimensionSet);
                TSD.DrawingObjectEnumerator dimensions = view.GetAllObjects(straightType);
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set =
                        dimensions.Current as TSD.StraightDimensionSet;
                    if (set != null && set.Attributes != null)
                        return set.Attributes;
                }
            }
            catch
            {
            }
            return null;
        }

        private static void ReadPartVertices(
            TSM.Part modelPart,
            ProjectedPart projected,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            try
            {
                TSM.Solid solid = modelPart.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;

                    projected.AddVertex(Transform(
                        edge.StartPoint,
                        currentToGlobal,
                        globalToView));
                    projected.AddVertex(Transform(
                        edge.EndPoint,
                        currentToGlobal,
                        globalToView));
                }
            }
            catch
            {
            }
        }

        private static void ReadPartBolts(
            TSM.Part modelPart,
            ProjectedPart projected,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            try
            {
                TSM.ModelObjectEnumerator bolts = modelPart.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    TSM.BoltGroup group = bolts.Current as TSM.BoltGroup;
                    if (group == null || group.BoltPositions == null)
                        continue;

                    foreach (object value in group.BoltPositions)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                        {
                            projected.AddBolt(Transform(
                                point,
                                currentToGlobal,
                                globalToView));
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static List<DimPlan> BuildAllPlans(Context context)
        {
            List<DimPlan> result = new List<DimPlan>();
            if (context.Views.ContainsKey("A"))
                BuildViewA(context.Views["A"], result, context.Errors);
            if (context.Views.ContainsKey("B"))
                BuildViewB(context.Views["B"], result, context.Errors);
            if (context.Views.ContainsKey("C"))
                BuildViewC(context.Views["C"], result, context.Errors);
            if (context.Views.ContainsKey("FRONT"))
                BuildFrontView(context.Views["FRONT"], result, context.Errors);
            return result;
        }

        private static void BuildViewA(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ConcreteGroup(view);
            GeometryGroup fp = PositionGroup(view, "CUFP");
            GeometryGroup wp = PositionGroup(view, "CUWP");
            GeometryGroup splice = PositionGroup(view, "CUSP");
            if (!RequireGroups(view, errors, concrete, fp, wp, splice)) return;

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            P2 fpLB = Pt(fp.MinX, fp.MinY);
            P2 fpRB = Pt(fp.MaxX, fp.MinY);
            P2 centerBottom = Pt(wp.CenterX, wp.MinY);
            P2 leftBolt = ExtremeBolt(fp.Bolts, true, true);
            P2 rightBolt = ExtremeBolt(fp.Bolts, false, true);
            List<P2> mainBolts = UniqueBoltsByX(main.Bolts, true);

            if (leftBolt == null || rightBolt == null || mainBolts.Count < 2)
            {
                errors.Add("View A thiếu bolt selector cần thiết.");
                return;
            }

            double step = view.TierStep;
            double internalBottom = splice.MinY - 0.05145 * step;
            double concreteY = concrete.MinY + 0.375 * concrete.Height;

            AddPlan(plans, view, "A-D01", DimSide.Top,
                main.MaxY + 4.34225 * step,
                Pt(concrete.MinX, concreteY), mainLT, mainRT,
                Pt(concrete.MaxX, concreteY));
            AddPlan(plans, view, "A-D02", DimSide.Top,
                main.MaxY + 3.41617 * step,
                mainLT,
                Pt(mainBolts[0].X, main.MaxY),
                Pt(mainBolts[mainBolts.Count - 1].X, main.MaxY),
                mainRT);
            AddPlan(plans, view, "A-D03", DimSide.Left,
                main.MinX - 3.01060 * step,
                fpLB, mainLT, Pt(concrete.MinX, concrete.MinY));
            AddPlan(plans, view, "A-D04", DimSide.Left,
                main.MinX - 2.03734 * step,
                fpLB, mainLB, mainLT);
            AddPlan(plans, view, "A-D05", DimSide.Left,
                main.MinX - 0.99775 * step,
                leftBolt, mainLB);
            AddPlan(plans, view, "A-D06", DimSide.Right,
                main.MaxX + 1.23855 * step,
                rightBolt, mainRB);
            AddPlan(plans, view, "A-D07", DimSide.Bottom,
                internalBottom - 2.64854 * step,
                mainLB, mainRB);
            AddPlan(plans, view, "A-D08", DimSide.Bottom,
                internalBottom - 1.75340 * step,
                mainLB, fpLB, centerBottom, fpRB, mainRB);
            plans[plans.Count - 1].ShowEqualSegmentsSeparately = true;
            AddPlan(plans, view, "A-D09", DimSide.Bottom,
                internalBottom,
                rightBolt, fpRB);
            AddPlan(plans, view, "A-D10", DimSide.Bottom,
                internalBottom,
                leftBolt, fpLB);

            double gridX;
            if (TryGetClosestCoordinate(
                    view.VerticalGridCoordinates,
                    wp.CenterX,
                    out gridX) &&
                Math.Abs(gridX - wp.CenterX) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "A-AXIS-OFFSET", DimSide.Top,
                    main.MaxY + 2.91463 * step,
                    Pt(wp.CenterX, main.MinY),
                    Pt(gridX, concrete.MinY));
            }
        }

        private static void BuildViewB(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ConcreteGroup(view);
            GeometryGroup fill = PositionGroup(view, "CUFIP");
            GeometryGroup splice = PositionGroup(view, "CUSP");
            GeometryGroup rp = PositionGroup(view, "CURP");
            GeometryGroup wp = PositionGroup(view, "CUWP");
            GeometryGroup dummy45 = NameProfileGroup(view, "DUMMY", "PL4.5");
            if (!RequireGroups(view, errors, concrete, fill, splice, rp, wp, dummy45)) return;

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            List<P2> spliceBolts = SortByY(splice.Bolts);
            if (spliceBolts.Count < 2)
            {
                errors.Add("View B thiếu bolt splice.");
                return;
            }

            P2 lowBolt = ClosestBoltBelow(spliceBolts, fill.MinY);
            P2 highBolt = ClosestBoltAbove(spliceBolts, fill.MinY);
            if (lowBolt == null || highBolt == null)
            {
                errors.Add("View B không tìm được hai hàng bolt kẹp mép fill plate.");
                return;
            }
            double step = view.TierStep;
            double concreteY = concrete.MinY + 0.526 * concrete.Height;

            AddPlan(plans, view, "B-D01", DimSide.Right,
                main.MaxX - 0.32805 * step,
                Pt(fill.MaxX, dummy45.MaxY), Pt(fill.MaxX, fill.MinY));
            AddPlan(plans, view, "B-D02", DimSide.Left,
                main.MinX - 3.02163 * step,
                Pt(fill.MinX, fill.MinY), mainLB, mainLT);
            AddPlan(plans, view, "B-D03", DimSide.Left,
                main.MinX - 3.91652 * step,
                Pt(fill.MinX, fill.MinY), mainLT,
                Pt(concrete.MinX, concrete.MinY));
            AddPlan(plans, view, "B-D04", DimSide.Bottom,
                main.MinY - 5.22193 * step,
                mainLB, Pt(rp.CenterX, main.MinY), mainRB);
            AddPlan(plans, view, "B-D05", DimSide.Bottom,
                main.MinY - 6.26414 * step,
                mainLB, mainRB);
            AddPlan(plans, view, "B-D06", DimSide.Left,
                main.MinX - 2.10964 * step,
                Pt(fill.MinX, lowBolt.Y), Pt(fill.MinX, fill.MinY),
                Pt(splice.MinX, highBolt.Y));
            AddPlan(plans, view, "B-D07", DimSide.Top,
                main.MaxY + 4.32093 * step,
                Pt(concrete.MinX, concreteY), mainLT, mainRT,
                Pt(concrete.MaxX, concreteY));

            double gridX;
            if (TryGetClosestCoordinate(
                    view.VerticalGridCoordinates,
                    wp.CenterX,
                    out gridX) &&
                Math.Abs(gridX - wp.CenterX) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "B-AXIS-OFFSET", DimSide.Top,
                    main.MaxY + 2.92040 * step,
                    Pt(wp.CenterX, main.MinY),
                    Pt(gridX, concrete.MinY));
            }
        }

        private static void BuildViewC(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ConcreteGroup(view);
            GeometryGroup wp = PositionGroup(view, "CUWP");
            List<ProjectedPart> rpParts = PositionParts(view, "CURP");
            List<ProjectedPart> fpParts = PositionParts(view, "CUFP");
            List<ProjectedPart> uaParts = PositionParts(view, "CUUA");
            if (!RequireGroups(view, errors, concrete, wp) ||
                rpParts.Count < 2 || fpParts.Count < 2 || uaParts.Count < 2)
            {
                errors.Add("View C thiếu cặp RP/FP/UA.");
                return;
            }

            ProjectedPart rpLeft = ExtremePart(rpParts, true);
            ProjectedPart rpRight = ExtremePart(rpParts, false);
            ProjectedPart fpLeft = ExtremePart(fpParts, true);
            ProjectedPart fpRight = ExtremePart(fpParts, false);
            ProjectedPart uaLeft = ExtremePart(uaParts, true);
            ProjectedPart uaRight = ExtremePart(uaParts, false);
            List<P2> mainBoltsTop = UniqueBoltsByX(main.Bolts, true);
            if (mainBoltsTop.Count < 4)
            {
                errors.Add("View C thiếu 4 cột bolt main.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            List<P2> rightBolts = BoltsAtExtremeX(main.Bolts, false);
            if (rightBolts.Count < 2)
            {
                errors.Add("View C thiếu cặp bolt phía phải.");
                return;
            }

            double step = view.TierStep;
            AddPlan(plans, view, "C-D01", DimSide.Right,
                main.MaxX + 2.88689 * step,
                mainRB, rightBolts[0], rightBolts[rightBolts.Count - 1], mainRT);
            AddPlan(plans, view, "C-D02", DimSide.Left,
                main.MinX - 3.82885 * step,
                mainLB, Pt(main.MinX, main.CenterY), mainLT);
            AddPlan(plans, view, "C-D03", DimSide.Left,
                main.MinX - 5.06507 * step,
                Pt(main.MinX, concrete.MinY), mainLB, mainLT,
                Pt(main.MinX, concrete.MaxY));
            AddPlan(plans, view, "C-D04", DimSide.Top,
                main.MaxY + 4.24923 * step,
                mainLT, mainRT);
            AddPlan(plans, view, "C-D05", DimSide.Bottom,
                main.MinY - 2.02993 * step,
                mainLB,
                Pt(rpLeft.MinX, rpLeft.MinY),
                Pt(fpLeft.MinX, fpLeft.MinY),
                Pt(uaLeft.MinX, uaLeft.MinY),
                Pt(uaRight.MaxX, uaRight.MinY),
                Pt(fpRight.MaxX, fpRight.MinY),
                Pt(rpRight.MaxX, rpRight.MinY),
                mainRB);
            AddPlan(plans, view, "C-D06", DimSide.Top,
                main.MaxY + 3.38836 * step,
                mainLT,
                mainBoltsTop[0], mainBoltsTop[1],
                mainBoltsTop[mainBoltsTop.Count - 2],
                mainBoltsTop[mainBoltsTop.Count - 1],
                mainRT);

            double gridY;
            if (TryGetClosestCoordinate(
                    view.HorizontalGridCoordinates,
                    wp.CenterY,
                    out gridY) &&
                Math.Abs(gridY - wp.CenterY) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "C-AXIS-OFFSET", DimSide.Right,
                    main.MaxX + 1.30596 * step,
                    Pt(main.MaxX, gridY),
                    Pt(rpRight.MaxX, wp.CenterY));
            }
        }

        private static void BuildFrontView(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ConcreteGroup(view);
            GeometryGroup wp = PositionGroup(view, "CUWP");
            GeometryGroup spliceLeft = PositionExtremeCenterGroup(view, "CUSP", true);
            GeometryGroup spliceRight = PositionExtremeCenterGroup(view, "CUSP", false);
            List<ProjectedPart> rpParts = PositionParts(view, "CURP");
            List<ProjectedPart> fpParts = PositionParts(view, "CUFP");
            if (!RequireGroups(view, errors, concrete, wp, spliceLeft, spliceRight) ||
                rpParts.Count < 2 || fpParts.Count < 2)
            {
                errors.Add("Front view thiếu cặp RP/FP.");
                return;
            }

            ProjectedPart rpLeft = ExtremePart(rpParts, true);
            ProjectedPart rpRight = ExtremePart(rpParts, false);
            ProjectedPart fpLeft = ExtremePart(fpParts, true);
            ProjectedPart fpRight = ExtremePart(fpParts, false);
            P2 rpLeftOuter = ExtremeVertex(rpLeft.Vertices, true, true);
            P2 rpRightOuter = ExtremeVertex(rpRight.Vertices, false, true);
            P2 rpLeftBottom = BottomVertex(rpLeft.Vertices, true);
            List<P2> mainBoltsTop = UniqueBoltsByX(main.Bolts, true);
            List<P2> spliceLeftBolts = UniqueBoltsByX(spliceLeft.Bolts, true);
            List<P2> spliceRightBolts = UniqueBoltsByX(spliceRight.Bolts, true);

            if (rpLeftOuter == null || rpRightOuter == null ||
                rpLeftBottom == null || mainBoltsTop.Count < 4 ||
                spliceLeftBolts.Count < 2 || spliceRightBolts.Count < 2)
            {
                errors.Add("Front view thiếu vertex/bolt selector.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            double step = view.TierStep;

            AddPlan(plans, view, "F-D01", DimSide.Left,
                main.MinX - 3.01060 * step,
                rpLeftBottom, mainLT, Pt(main.MinX - 0.04 * concrete.Width, concrete.MinY));
            AddPlan(plans, view, "F-D02", DimSide.Top,
                main.MaxY + 4.34440 * step,
                mainLT, mainRT);
            AddPlan(plans, view, "F-D03", DimSide.Left,
                main.MinX - 2.03734 * step,
                rpLeftBottom, mainLB, mainLT);

            List<P2> fD04 = new List<P2>();
            fD04.Add(Pt(fpLeft.MaxX, fpLeft.MinY));
            fD04.Add(spliceLeftBolts[0]);
            fD04.Add(spliceLeftBolts[spliceLeftBolts.Count - 1]);
            for (int i = 0; i < spliceRightBolts.Count; i++)
                fD04.Add(spliceRightBolts[i]);
            fD04.Add(Pt(fpRight.MinX, fpRight.MinY));
            AddPlanList(plans, view, "F-D04", DimSide.Bottom,
                Math.Min(fpLeft.MinY, fpRight.MinY) - 3.08429 * step,
                fD04);

            AddPlan(plans, view, "F-D05", DimSide.Right,
                main.MaxX + 1.86199 * step,
                rpRightOuter, mainRB);
            AddPlan(plans, view, "F-D06", DimSide.Bottom,
                Math.Min(fpLeft.MinY, fpRight.MinY) - 4.09215 * step,
                mainLB,
                rpLeftOuter,
                Pt(fpLeft.MinX, fpLeft.MinY),
                Pt(fpLeft.MaxX, fpLeft.MinY),
                Pt(fpRight.MinX, fpRight.MinY),
                Pt(fpRight.MaxX, fpRight.MinY),
                rpRightOuter,
                mainRB);
            AddPlan(plans, view, "F-D07", DimSide.Top,
                main.MaxY + 3.29507 * step,
                mainLT,
                mainBoltsTop[0], mainBoltsTop[1],
                mainBoltsTop[mainBoltsTop.Count - 2],
                mainBoltsTop[mainBoltsTop.Count - 1],
                mainRT);
            AddPlan(plans, view, "F-D08", DimSide.Left,
                main.MinX - 1.18565 * step,
                rpLeftOuter, mainLB);
        }

        // Slot 07 is a separate semantic plan for the second Nishi topology.
        // Coordinates below are derived from part roles and geometric features;
        // no approved sample length (including the 40 mm grid offset) is fixed.
        private static List<DimPlan> BuildAllPlansSlot07(Context context)
        {
            List<DimPlan> result = new List<DimPlan>();
            if (context.Views.ContainsKey("A"))
                BuildViewASlot07(context.Views["A"], result, context.Errors);
            if (context.Views.ContainsKey("B"))
                BuildViewBSlot07(context.Views["B"], result, context.Errors);
            if (context.Views.ContainsKey("C"))
                BuildViewCSlot07(context.Views["C"], result, context.Errors);
            if (context.Views.ContainsKey("FRONT"))
                BuildFrontViewSlot07(context.Views["FRONT"], result, context.Errors);
            return result;
        }

        private static void BuildViewCSlot07(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ShortConcreteGroupSlot07(view);
            GeometryGroup wp = RoleGroupSlot07(view, "WP");
            List<ProjectedPart> rpParts = RolePartsSlot07(view, "RP");
            List<ProjectedPart> fpParts = RolePartsSlot07(view, "FP");
            if (!RequireGroups(view, errors, concrete, wp) ||
                rpParts.Count < 2 || fpParts.Count < 2)
            {
                errors.Add("View C slot 07 thieu cap RP/FP.");
                return;
            }

            ProjectedPart fpLeft = ExtremePart(fpParts, true);
            ProjectedPart fpRight = ExtremePart(fpParts, false);
            List<P2> topBolts = UniqueBoltsByX(main.Bolts, true);
            List<P2> rightBolts = BoltsAtExtremeX(main.Bolts, false);
            if (topBolts.Count < 4 || rightBolts.Count < 2)
            {
                errors.Add("View C slot 07 thieu bolt main bat buoc.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            double step = view.TierStep;
            double concreteFootX = main.MinX - 1.58109 * step;

            AddPlan(plans, view, "S07-C-D01", DimSide.Bottom,
                main.MinY - 3.69602 * step,
                mainLB,
                Pt(fpLeft.MinX, fpLeft.MinY),
                Pt(fpLeft.MaxX, fpLeft.MinY),
                Pt(fpRight.MinX, fpRight.MinY),
                Pt(fpRight.MaxX, fpRight.MinY),
                mainRB);
            AddPlan(plans, view, "S07-C-D02", DimSide.Top,
                main.MaxY + 2.93224 * step,
                mainLT, topBolts[0], topBolts[1],
                topBolts[topBolts.Count - 2], topBolts[topBolts.Count - 1],
                mainRT);
            DisableLastPlanSlot07(plans);
            AddPlan(plans, view, "S07-C-D03", DimSide.Right,
                main.MaxX + 3.87885 * step,
                mainRB, rightBolts[0], rightBolts[rightBolts.Count - 1], mainRT);
            DisableLastPlanSlot07(plans);

            double gridY;
            if (TryGetClosestCoordinate(
                    view.HorizontalGridCoordinates,
                    wp.CenterY,
                    out gridY) &&
                Math.Abs(gridY - wp.CenterY) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "S07-C-AXIS-OFFSET", DimSide.Left,
                    main.MinX - 3.26046 * step,
                    Pt(main.MinX, gridY), Pt(main.MinX, wp.CenterY));
            }

            AddPlan(plans, view, "S07-C-D05", DimSide.Left,
                main.MinX - 4.11824 * step,
                mainLB, Pt(main.MinX, wp.CenterY), mainLT);
            AddPlan(plans, view, "S07-C-D06", DimSide.Left,
                main.MinX - 5.13919 * step,
                Pt(concreteFootX, concrete.MinY), mainLB, mainLT,
                Pt(concreteFootX, concrete.MaxY));
            AddPlan(plans, view, "S07-C-D07", DimSide.Left,
                main.MinX - 6.09291 * step,
                Pt(concreteFootX, concrete.MinY),
                Pt(concreteFootX, concrete.MaxY));
        }

        private static void BuildViewBSlot07(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ShortConcreteGroupSlot07(view);
            GeometryGroup wp = RoleGroupSlot07(view, "WP");
            GeometryGroup sp1 = RoleGroupSlot07(view, "SP-1");
            GeometryGroup dummy28 = DummyPlateGroupSlot07(view, 28.0);
            if (!RequireGroups(view, errors, concrete, wp, sp1, dummy28))
                return;

            List<P2> sp1Bolts = SortByY(sp1.Bolts);
            if (sp1Bolts.Count < 2)
            {
                errors.Add("View B slot 07 thieu cap bolt SP-1.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            double step = view.TierStep;

            AddPlan(plans, view, "S07-B-D01", DimSide.Left,
                main.MinX - 1.49875 * step,
                Pt(sp1.MinX, sp1Bolts[0].Y),
                Pt(wp.MinX, wp.MaxY),
                Pt(sp1.MinX, sp1Bolts[sp1Bolts.Count - 1].Y));
            DisableLastPlanSlot07(plans);

            double gridX;
            if (TryGetClosestCoordinate(
                    view.VerticalGridCoordinates,
                    wp.CenterX,
                    out gridX) &&
                Math.Abs(gridX - wp.CenterX) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "S07-B-AXIS-OFFSET", DimSide.Bottom,
                    main.MinY - 2.58373 * step,
                    Pt(wp.CenterX, main.MaxY), Pt(gridX, main.MaxY));
            }

            AddPlan(plans, view, "S07-B-D03", DimSide.Bottom,
                main.MinY - 3.48086 * step,
                mainLB, Pt(wp.CenterX, main.MaxY), mainRB);
            DisableLastPlanSlot07(plans);
            AddPlan(plans, view, "S07-B-D04", DimSide.Top,
                main.MaxY + 5.78437 * step,
                mainLT, Pt(wp.CenterX, main.MaxY), mainRT);
            AddPlan(plans, view, "S07-B-D05", DimSide.Left,
                main.MinX - 4.64865 * step,
                Pt(concrete.MinX, concrete.MaxY), mainLB,
                Pt(wp.MinX, wp.MaxY));
            AddPlan(plans, view, "S07-B-D06", DimSide.Left,
                main.MinX - 3.71943 * step,
                mainLB, mainLT, Pt(wp.MinX, wp.MaxY));
            AddPlan(plans, view, "S07-B-D07", DimSide.Bottom,
                main.MinY - 4.32336 * step,
                mainLB, mainRB);
            AddPlan(plans, view, "S07-B-D08", DimSide.Right,
                main.MaxX - 0.39683 * step,
                Pt(wp.MaxX, wp.MaxY), Pt(wp.MaxX, dummy28.MinY));
        }

        private static void BuildViewASlot07(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup concrete = ShortConcreteGroupSlot07(view);
            GeometryGroup wp = RoleGroupSlot07(view, "WP");
            GeometryGroup fp = RoleGroupSlot07(view, "FP");
            GeometryGroup sp2Left = RoleExtremeCenterGroupSlot07(view, "SP-2", true);
            GeometryGroup sp2Right = RoleExtremeCenterGroupSlot07(view, "SP-2", false);
            GeometryGroup dummy25 = DummyPlateGroupSlot07(view, 25.0);
            GeometryGroup dummy25Right = DummyPlateExtremeCenterGroupSlot07(view, 25.0, false);
            if (!RequireGroups(
                    view, errors, concrete, wp, fp,
                    sp2Left, sp2Right, dummy25, dummy25Right))
                return;

            List<P2> leftFpBolts = BoltsAtExtremeX(fp.Bolts, true);
            List<P2> leftDummyBolts = BoltsAtExtremeX(dummy25.Bolts, true);
            P2 fpBoltBelow = ClosestBoltBelow(leftFpBolts, fp.MaxY);
            P2 fpBoltAbove = ClosestBoltAbove(leftDummyBolts, fp.MaxY);
            if (fpBoltBelow == null || fpBoltAbove == null)
            {
                errors.Add("View A slot 07 thieu cap bolt FP ben trai.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            double step = view.TierStep;
            double dummyRightX = dummy25Right.MaxX;

            AddPlan(plans, view, "S07-A-D01", DimSide.Left,
                main.MinX - 4.81203 * step,
                Pt(concrete.MinX, concrete.MaxY), mainLB,
                Pt(fp.MinX, fp.MaxY));
            AddPlan(plans, view, "S07-A-D02", DimSide.Top,
                main.MaxY + 6.43608 * step,
                mainLT,
                Pt(fp.MinX, fp.MaxY),
                Pt(fp.CenterX, fp.MaxY),
                Pt(fp.MaxX, fp.MaxY),
                mainRT);
            plans[plans.Count - 1].ShowEqualSegmentsSeparately = true;
            AddPlan(plans, view, "S07-A-D03", DimSide.Top,
                sp2Right.MaxY + 2.23221 * step,
                Pt(sp2Right.MinX, sp2Right.MaxY),
                Pt(sp2Right.MaxX, sp2Right.MaxY));
            AddPlan(plans, view, "S07-A-D04", DimSide.Top,
                sp2Left.MaxY + 2.23121 * step,
                Pt(sp2Left.MinX, sp2Left.MaxY),
                Pt(sp2Left.MaxX, sp2Left.MaxY));
            AddPlan(plans, view, "S07-A-D05", DimSide.Left,
                main.MinX - 2.84050 * step,
                fpBoltBelow, Pt(fp.MinX, fp.MaxY), fpBoltAbove);
            DisableLastPlanSlot07(plans);
            AddPlan(plans, view, "S07-A-D06", DimSide.Left,
                main.MinX - 3.88327 * step,
                mainLB, mainLT, Pt(fp.MinX, fp.MaxY));
            AddPlan(plans, view, "S07-A-D07", DimSide.Right,
                main.MaxX - 0.13263 * step,
                Pt(dummyRightX, fp.MaxY),
                Pt(dummyRightX, dummy25Right.MinY));
            AddPlan(plans, view, "S07-A-D08", DimSide.Bottom,
                main.MinY - 3.47086 * step,
                mainLB, Pt(wp.CenterX, main.MaxY), mainRB);
            DisableLastPlanSlot07(plans);

            double gridX;
            if (TryGetClosestCoordinate(
                    view.VerticalGridCoordinates,
                    wp.CenterX,
                    out gridX) &&
                Math.Abs(gridX - wp.CenterX) > AxisAlignmentTolerance)
            {
                AddPlan(plans, view, "S07-A-AXIS-OFFSET", DimSide.Bottom,
                    main.MinY - 2.58373 * step,
                    Pt(wp.CenterX, main.MaxY), Pt(gridX, main.MaxY));
            }

            AddPlan(plans, view, "S07-A-D10", DimSide.Bottom,
                main.MinY - 4.40185 * step,
                mainLB, mainRB);
        }

        private static void BuildFrontViewSlot07(
            ViewGeometry view,
            List<DimPlan> plans,
            List<string> errors)
        {
            GeometryGroup main = Group(view.Main);
            GeometryGroup wp = RoleGroupSlot07(view, "WP");
            GeometryGroup dummy28 = DummyPlateGroupSlot07(view, 28.0);
            GeometryGroup dummy25Right = DummyPlateExtremeCenterGroupSlot07(view, 25.0, false);
            List<ProjectedPart> rpParts = RolePartsSlot07(view, "RP");
            List<GeometryGroup> sp2Columns = RoleCenterGroupsSlot07(view, "SP-2");
            if (!RequireGroups(view, errors, wp, dummy28, dummy25Right) ||
                rpParts.Count < 2 || sp2Columns.Count != 4)
            {
                errors.Add("Front slot 07 thieu cap RP hoac 4 cot SP-2.");
                return;
            }

            ProjectedPart rpLeft = ExtremePart(rpParts, true);
            ProjectedPart rpRight = ExtremePart(rpParts, false);
            P2 rpLeftOuter = ExtremeXVertexSlot07(rpLeft.Vertices, true, true);
            P2 rpRightOuter = ExtremeXVertexSlot07(rpRight.Vertices, false, true);
            P2 rpLeftTopInner = TopVertexSlot07(rpLeft.Vertices, true);
            P2 rpRightTopInner = TopVertexSlot07(rpRight.Vertices, false);
            P2 wpTopLeft = TopVertexSlot07(wp.Vertices, true);
            List<P2> mainBolts = UniqueBoltsByX(main.Bolts, true);
            List<P2> wpBolts = UniqueBoltsByX(wp.Bolts, true);
            if (rpLeftOuter == null || rpRightOuter == null ||
                rpLeftTopInner == null || rpRightTopInner == null ||
                wpTopLeft == null || mainBolts.Count < 4 || wpBolts.Count < 7)
            {
                errors.Add("Front slot 07 thieu vertex/bolt selector bat buoc.");
                return;
            }

            P2 dummyBoltLeft = ClosestBoltByXSlot07(
                dummy28.Bolts, wpBolts[1].X, true);
            P2 dummyBoltRight = ClosestBoltByXSlot07(
                dummy28.Bolts, wpBolts[wpBolts.Count - 2].X, true);
            if (dummyBoltLeft == null || dummyBoltRight == null)
            {
                errors.Add("Front slot 07 thieu bolt lien ket dummy PL28.");
                return;
            }

            P2 mainLB = Pt(main.MinX, main.MinY);
            P2 mainLT = Pt(main.MinX, main.MaxY);
            P2 mainRB = Pt(main.MaxX, main.MinY);
            P2 mainRT = Pt(main.MaxX, main.MaxY);
            double step = view.TierStep;

            List<P2> fD01 = new List<P2>();
            fD01.Add(mainLB);
            for (int i = 0; i < mainBolts.Count; i++)
                fD01.Add(Pt(mainBolts[i].X, main.MinY));
            fD01.Add(mainRB);
            AddPlanList(plans, view, "S07-F-D01", DimSide.Bottom,
                main.MinY - 3.96074 * step, fD01);

            AddPlan(plans, view, "S07-F-D02", DimSide.Top,
                sp2Columns[3].MaxY + 1.07871 * step,
                Pt(sp2Columns[3].MinX, sp2Columns[3].MaxY),
                Pt(sp2Columns[3].MaxX, sp2Columns[3].MaxY));
            AddPlan(plans, view, "S07-F-D03", DimSide.Top,
                sp2Columns[2].MaxY + 1.07437 * step,
                Pt(sp2Columns[2].MaxX, sp2Columns[2].MaxY),
                Pt(sp2Columns[2].MinX, sp2Columns[2].MaxY));
            AddPlan(plans, view, "S07-F-D04", DimSide.Top,
                sp2Columns[1].MaxY + 1.08711 * step,
                Pt(sp2Columns[1].MinX, sp2Columns[1].MaxY),
                Pt(sp2Columns[1].MaxX, sp2Columns[1].MaxY));
            AddPlan(plans, view, "S07-F-D05", DimSide.Top,
                sp2Columns[0].MaxY + 1.08781 * step,
                Pt(sp2Columns[0].MaxX, sp2Columns[0].MaxY),
                Pt(sp2Columns[0].MinX, sp2Columns[0].MaxY));

            List<P2> fD06 = new List<P2>();
            fD06.Add(Pt(wp.MinX, wp.MaxY));
            for (int i = 1; i < wpBolts.Count - 1; i++)
                fD06.Add(wpBolts[i]);
            fD06.Add(Pt(wp.MaxX, wp.MaxY));
            AddPlanList(plans, view, "S07-F-D06", DimSide.Top,
                wp.MaxY + 4.97557 * step, fD06);

            AddPlan(plans, view, "S07-F-D07", DimSide.Top,
                main.MaxY + 7.58362 * step,
                Pt(rpLeft.MinX, main.MaxY),
                Pt(rpLeft.MaxX, rpLeft.MaxY),
                Pt(wp.MinX, wp.MaxY),
                Pt(wp.MaxX, wp.MaxY),
                Pt(rpRight.MinX, rpRight.MaxY),
                Pt(rpRight.MaxX, main.MaxY));
            AddPlan(plans, view, "S07-F-D08", DimSide.Top,
                rpRightOuter.Y + 0.55809 * step,
                rpRightOuter, mainRT);
            AddPlan(plans, view, "S07-F-D09", DimSide.Left,
                wp.MinX - 1.66727 * step,
                wpBolts[1], wpTopLeft, dummyBoltLeft);
            double fD10Line = main.MaxY + 0.96081 * step;
            DimSide fD10Side = fD10Line >= rpLeftOuter.Y
                ? DimSide.Top
                : DimSide.Bottom;
            AddPlan(plans, view, "S07-F-D10", fD10Side,
                fD10Line, rpLeftOuter, mainLT);
            AddPlan(plans, view, "S07-F-D11", DimSide.Bottom,
                main.MinY - 4.98529 * step,
                mainLB, mainRB);
            AddPlan(plans, view, "S07-F-D12", DimSide.Left,
                main.MinX - 2.54915 * step,
                mainLB, mainLT, rpLeftTopInner);
            AddPlan(plans, view, "S07-F-D13", DimSide.Left,
                main.MinX - 3.41377 * step,
                mainLB, rpLeftTopInner);
            AddPlan(plans, view, "S07-F-D14", DimSide.Top,
                wpBolts[0].Y + 3.44637 * step,
                wpBolts[0], dummyBoltLeft);
            AddPlan(plans, view, "S07-F-D15", DimSide.Top,
                dummyBoltRight.Y + 2.78282 * step,
                dummyBoltRight, wpBolts[wpBolts.Count - 1]);
            AddPlan(plans, view, "S07-F-D16", DimSide.Right,
                main.MaxX - 0.89335 * step,
                rpRightTopInner,
                Pt(dummy25Right.MaxX, dummy25Right.MinY));
            AddPlan(plans, view, "S07-F-D17", DimSide.Right,
                main.MaxX + 1.19331 * step,
                mainRT, rpRightOuter);
            AddPlan(plans, view, "S07-F-D18", DimSide.Left,
                main.MinX - 1.05546 * step,
                mainLT, rpLeftOuter);
        }

        private static void ValidateReadySlot07(Context context, List<DimPlan> plans)
        {
            if (context.Errors.Count > 0)
                throw new InvalidOperationException(
                    "Preflight slot 07 khong dat:\r\n- " +
                    string.Join("\r\n- ", context.Errors.ToArray()));

            if (plans == null || plans.Count < 40 || plans.Count > 43)
                throw new InvalidOperationException(
                    "Slot 07 phai tao 40 plan nen va toi da 3 plan lech truc; hien co " +
                    (plans == null ? 0 : plans.Count) + ".");

            Dictionary<string, int> perView = new Dictionary<string, int>();
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                int count;
                perView.TryGetValue(plan.View.Key, out count);
                perView[plan.View.Key] = count + 1;
                ValidatePlanGeometrySlot07(plan);
            }

            ValidateViewPlanCountSlot07(perView, "A", 9, 10);
            ValidateViewPlanCountSlot07(perView, "B", 7, 8);
            ValidateViewPlanCountSlot07(perView, "C", 6, 7);
            ValidateViewPlanCountSlot07(perView, "FRONT", 18, 18);
        }

        private static void ValidatePlanGeometrySlot07(DimPlan plan)
        {
            if (plan == null || plan.Points.Count < 2)
                throw new InvalidOperationException(
                    (plan == null ? "Plan" : plan.Name) + " thieu chan dim.");

            double minimum = Double.PositiveInfinity;
            double maximum = Double.NegativeInfinity;
            for (int i = 0; i < plan.Points.Count; i++)
            {
                P2 point = plan.Points[i];
                if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                    throw new InvalidOperationException(plan.Name + " co diem khong hop le.");
                double value = plan.Side == DimSide.Left || plan.Side == DimSide.Right
                    ? point.Y
                    : point.X;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }

            if (maximum - minimum <= GeometryTolerance ||
                GetDistance(plan) <= GeometryTolerance)
                throw new InvalidOperationException(
                    plan.Name + " co kich thuoc/vi tri dat dim khong hop le.");
        }

        private static void ValidateViewPlanCountSlot07(
            Dictionary<string, int> counts,
            string key,
            int minimum,
            int maximum)
        {
            int value;
            counts.TryGetValue(key, out value);
            if (value < minimum || value > maximum)
                throw new InvalidOperationException(
                    "View " + key + " slot 07 co " + value +
                    " plan; mong doi " + minimum + ".." + maximum + ".");
        }

        private static void ValidateReady(Context context, List<DimPlan> plans)
        {
            if (context.Errors.Count > 0)
                throw new InvalidOperationException(
                    "Preflight không đạt:\r\n- " +
                    string.Join("\r\n- ", context.Errors.ToArray()));

            int maximumPlanCount =
                BaseDimensionSetCount + MaximumOffsetDimensionCount;
            if (plans == null || plans.Count < BaseDimensionSetCount ||
                plans.Count > maximumPlanCount)
                throw new InvalidOperationException(
                    "Preflight phải tạo từ " + BaseDimensionSetCount +
                    " đến " + maximumPlanCount + " plan, hiện có " +
                    (plans == null ? 0 : plans.Count) + ".");

            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (plan.Points.Count < 2)
                    throw new InvalidOperationException(plan.Name + " thiếu chân dim.");

                double minMeasure = Double.PositiveInfinity;
                double maxMeasure = Double.NegativeInfinity;
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    P2 point = plan.Points[p];
                    if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                        throw new InvalidOperationException(plan.Name + " có điểm không hợp lệ.");

                    double measure = plan.Side == DimSide.Left || plan.Side == DimSide.Right
                        ? point.Y
                        : point.X;
                    minMeasure = Math.Min(minMeasure, measure);
                    maxMeasure = Math.Max(maxMeasure, measure);
                }

                if (maxMeasure - minMeasure <= GeometryTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " không có khoảng đo hợp lệ.");
                if (GetDistance(plan) <= GeometryTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " có khoảng cách đặt dim không hợp lệ.");
            }
        }

        private static List<TSD.DrawingObject> CollectBackgroundDimensions(
            Dictionary<string, ViewGeometry> views)
        {
            List<TSD.DrawingObject> result = new List<TSD.DrawingObject>();
            HashSet<int> seen = new HashSet<int>();

            foreach (KeyValuePair<string, ViewGeometry> pair in views)
            {
                TSD.DrawingObjectEnumerator all = pair.Value.View.GetAllObjects();
                while (all != null && all.MoveNext())
                {
                    TSD.DrawingObject item = all.Current as TSD.DrawingObject;
                    if (item == null)
                        continue;

                    string name = item.GetType().Name;
                    bool isSet = name.IndexOf(
                        "DimensionSet", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isStandalone = name.IndexOf(
                        "Dimension", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !String.Equals(name, "StraightDimension", StringComparison.OrdinalIgnoreCase);
                    if (!isSet && !isStandalone)
                        continue;

                    int id = RuntimeHelpers.GetHashCode(item);
                    if (seen.Add(id))
                        result.Add(item);
                }
            }

            return result;
        }

        private static void AddPlan(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            DimSide side,
            double lineCoordinate,
            params P2[] points)
        {
            List<P2> list = new List<P2>();
            if (points != null)
            {
                for (int i = 0; i < points.Length; i++)
                    AddUnique(list, points[i]);
            }
            AddPlanList(plans, view, name, side, lineCoordinate, list);
        }

        private static void AddPlanList(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            DimSide side,
            double lineCoordinate,
            List<P2> points)
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.Side = side;
            plan.LineCoordinate = lineCoordinate;
            for (int i = 0; i < points.Count; i++)
                AddUnique(plan.Points, points[i]);
            plans.Add(plan);
        }

        private static TSG.Vector GetDirection(DimSide side)
        {
            if (side == DimSide.Left) return new TSG.Vector(-1.0, 0.0, 0.0);
            if (side == DimSide.Right) return new TSG.Vector(1.0, 0.0, 0.0);
            if (side == DimSide.Top) return new TSG.Vector(0.0, 1.0, 0.0);
            return new TSG.Vector(0.0, -1.0, 0.0);
        }

        private static void ForceSeparateEqualSegmentLabels(
            DimPlan plan,
            TSD.StraightDimensionSet dimension)
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            if (attributes == null)
                throw new InvalidOperationException(
                    "Không đọc được attributes của " + plan.Name + ".");

            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension ??
                new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format =
                TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            // A five-foot chain contains four equal segments.  Keeping the
            // threshold above the segment count is a second guard for Tekla
            // environments that preserve the format but rewrite its enum.
            combined.MinimumNumberToCombine = Math.Max(5, plan.Points.Count);
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;

            if (!dimension.Modify())
                throw new InvalidOperationException(
                    "Không áp dụng được kiểu 125-125-125-125 cho " + plan.Name + ".");

            dimension.Select();
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes verified =
                dimension.Attributes == null
                    ? null
                    : dimension.Attributes.CombinedDimension;
            int segmentCount = plan.Points.Count - 1;
            bool combineDisabled = verified != null &&
                (verified.Format == TSD.DimensionSetBaseAttributes.CombineFormats.Off ||
                 verified.MinimumNumberToCombine > segmentCount);
            if (!combineDisabled)
                throw new InvalidOperationException(
                    "Tekla đã ghi đè kiểu hiển thị của " + plan.Name +
                    "; dừng trước khi xóa dim nền.");
        }

        private static double GetDistance(DimPlan plan)
        {
            P2 first = plan.Points[0];
            if (plan.Side == DimSide.Left) return first.X - plan.LineCoordinate;
            if (plan.Side == DimSide.Right) return plan.LineCoordinate - first.X;
            if (plan.Side == DimSide.Top) return plan.LineCoordinate - first.Y;
            return first.Y - plan.LineCoordinate;
        }

        private static bool RequireGroups(
            ViewGeometry view,
            List<string> errors,
            params GeometryGroup[] groups)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] == null || !groups[i].IsValid)
                {
                    errors.Add("View " + view.Key + " thiếu nhóm hình học bắt buộc.");
                    return false;
                }
            }
            return true;
        }

        private static void DisableLastPlanSlot07(List<DimPlan> plans)
        {
            if (plans != null && plans.Count > 0)
                plans[plans.Count - 1].DisableCombine = true;
        }

        private static List<ProjectedPart> RolePartsSlot07(
            ViewGeometry view,
            string role)
        {
            List<ProjectedPart> result = new List<ProjectedPart>();
            string marker = SafeUpper(role);
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                if (part == null || part.IsMain || IsDummyPart(part))
                    continue;

                bool semanticMatch = marker.StartsWith("SP-", StringComparison.Ordinal)
                    ? part.Position.IndexOf(marker, StringComparison.Ordinal) >= 0
                    : part.Position.IndexOf(marker + "-", StringComparison.Ordinal) >= 0;
                if (semanticMatch)
                    result.Add(part);
            }

            if (result.Count > 0)
                return result;

            // Position prefixes contain the member number and can change.  Use
            // the profile/shape relation only when it is unambiguous.
            if (marker == "WP")
                return GeometricRoleParts(view, "CUWP");
            if (marker == "UA")
                return GeometricRoleParts(view, "CUUA");
            if (marker == "RP")
                return GeometricRoleParts(view, "CURP");
            if (marker == "FP")
                return GeometricRoleParts(view, "CUFP");

            if (marker == "SP-1" || marker == "SP-2")
            {
                List<ProjectedPart> splice = new List<ProjectedPart>();
                for (int i = 0; i < view.Parts.Count; i++)
                {
                    ProjectedPart part = view.Parts[i];
                    if (part != null && !part.IsMain && !IsDummyPart(part) &&
                        IsPlateProfile(part, 12.0) &&
                        PartNameContains(part, "SPLICE"))
                        splice.Add(part);
                }
                if (splice.Count != 10)
                    return result;

                splice.Sort(delegate(ProjectedPart a, ProjectedPart b)
                {
                    return a.Width.CompareTo(b.Width);
                });
                bool pairIsNarrow = view.Key == "A" || view.Key == "B";
                int pairStart = pairIsNarrow ? 0 : splice.Count - 2;
                List<ProjectedPart> geometric = new List<ProjectedPart>();
                for (int i = 0; i < splice.Count; i++)
                {
                    bool inPair = i >= pairStart && i < pairStart + 2;
                    if ((marker == "SP-1" && inPair) ||
                        (marker == "SP-2" && !inPair))
                        geometric.Add(splice[i]);
                }
                return geometric;
            }

            return result;
        }

        private static GeometryGroup RoleGroupSlot07(
            ViewGeometry view,
            string role)
        {
            GeometryGroup result = new GeometryGroup();
            List<ProjectedPart> parts = RolePartsSlot07(view, role);
            for (int i = 0; i < parts.Count; i++)
                result.Add(parts[i]);
            return result;
        }

        private static GeometryGroup RoleExtremeCenterGroupSlot07(
            ViewGeometry view,
            string role,
            bool minimumX)
        {
            List<GeometryGroup> groups = RoleCenterGroupsSlot07(view, role);
            if (groups.Count == 0)
                return new GeometryGroup();
            return minimumX ? groups[0] : groups[groups.Count - 1];
        }

        private static List<GeometryGroup> RoleCenterGroupsSlot07(
            ViewGeometry view,
            string role)
        {
            List<ProjectedPart> parts = RolePartsSlot07(view, role);
            parts.Sort(delegate(ProjectedPart a, ProjectedPart b)
            {
                return a.CenterX.CompareTo(b.CenterX);
            });

            List<GeometryGroup> groups = new List<GeometryGroup>();
            for (int i = 0; i < parts.Count; i++)
            {
                ProjectedPart part = parts[i];
                GeometryGroup target = null;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (Math.Abs(groups[g].CenterX - part.CenterX) <= GeometryTolerance)
                    {
                        target = groups[g];
                        break;
                    }
                }
                if (target == null)
                {
                    target = new GeometryGroup();
                    groups.Add(target);
                }
                target.Add(part);
            }
            groups.Sort(delegate(GeometryGroup a, GeometryGroup b)
            {
                return a.CenterX.CompareTo(b.CenterX);
            });
            return groups;
        }

        private static GeometryGroup ShortConcreteGroupSlot07(ViewGeometry view)
        {
            ProjectedPart selected = null;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                bool dummy = part.Name.IndexOf("DUMMY", StringComparison.Ordinal) >= 0 ||
                             part.Position.IndexOf("CONCRETE_DUMMY", StringComparison.Ordinal) >= 0;
                bool plate = part.Profile.StartsWith("PL", StringComparison.Ordinal);
                if (!dummy || plate)
                    continue;
                if (selected == null || part.Height < selected.Height)
                    selected = part;
            }
            return Group(selected);
        }

        private static List<ProjectedPart> DummyPlatePartsSlot07(
            ViewGeometry view,
            double thickness)
        {
            List<ProjectedPart> result = new List<ProjectedPart>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                if (IsDummyPart(part) && IsPlateProfile(part, thickness))
                    result.Add(part);
            }
            return result;
        }

        private static GeometryGroup DummyPlateGroupSlot07(
            ViewGeometry view,
            double thickness)
        {
            GeometryGroup result = new GeometryGroup();
            List<ProjectedPart> parts = DummyPlatePartsSlot07(view, thickness);
            for (int i = 0; i < parts.Count; i++)
                result.Add(parts[i]);
            return result;
        }

        private static GeometryGroup DummyPlateExtremeCenterGroupSlot07(
            ViewGeometry view,
            double thickness,
            bool minimumX)
        {
            List<ProjectedPart> parts = DummyPlatePartsSlot07(view, thickness);
            if (parts.Count == 0)
                return new GeometryGroup();

            double target = minimumX
                ? Double.PositiveInfinity
                : Double.NegativeInfinity;
            for (int i = 0; i < parts.Count; i++)
            {
                target = minimumX
                    ? Math.Min(target, parts[i].CenterX)
                    : Math.Max(target, parts[i].CenterX);
            }

            GeometryGroup result = new GeometryGroup();
            for (int i = 0; i < parts.Count; i++)
            {
                if (Math.Abs(parts[i].CenterX - target) <= GeometryTolerance)
                    result.Add(parts[i]);
            }
            return result;
        }

        private static P2 TopVertexSlot07(List<P2> points, bool minimumXOnTie)
        {
            P2 best = null;
            for (int i = 0; i < points.Count; i++)
            {
                P2 point = points[i];
                if (best == null || point.Y > best.Y + GeometryTolerance)
                {
                    best = point;
                }
                else if (Math.Abs(point.Y - best.Y) <= GeometryTolerance &&
                         ((minimumXOnTie && point.X < best.X) ||
                          (!minimumXOnTie && point.X > best.X)))
                {
                    best = point;
                }
            }
            return best == null ? null : Copy(best);
        }

        private static P2 ExtremeXVertexSlot07(
            List<P2> points,
            bool minimumX,
            bool maximumYOnTie)
        {
            P2 best = null;
            for (int i = 0; i < points.Count; i++)
            {
                P2 point = points[i];
                if (best == null ||
                    (minimumX && point.X < best.X - GeometryTolerance) ||
                    (!minimumX && point.X > best.X + GeometryTolerance))
                {
                    best = point;
                }
                else if (Math.Abs(point.X - best.X) <= GeometryTolerance &&
                         ((maximumYOnTie && point.Y > best.Y) ||
                          (!maximumYOnTie && point.Y < best.Y)))
                {
                    best = point;
                }
            }
            return best == null ? null : Copy(best);
        }

        private static P2 ClosestBoltByXSlot07(
            List<P2> bolts,
            double x,
            bool maximumYOnTie)
        {
            P2 best = null;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < bolts.Count; i++)
            {
                P2 bolt = bolts[i];
                double distance = Math.Abs(bolt.X - x);
                if (distance < bestDistance - GeometryTolerance ||
                    (Math.Abs(distance - bestDistance) <= GeometryTolerance &&
                     best != null &&
                     ((maximumYOnTie && bolt.Y > best.Y) ||
                      (!maximumYOnTie && bolt.Y < best.Y))))
                {
                    best = bolt;
                    bestDistance = distance;
                }
            }
            return best == null ? null : Copy(best);
        }

        private static GeometryGroup Group(ProjectedPart part)
        {
            GeometryGroup result = new GeometryGroup();
            result.Add(part);
            return result;
        }

        private static GeometryGroup PositionGroup(ViewGeometry view, string token)
        {
            GeometryGroup result = new GeometryGroup();
            List<ProjectedPart> parts = PositionParts(view, token);
            for (int i = 0; i < parts.Count; i++)
                result.Add(parts[i]);
            return result;
        }

        private static GeometryGroup PositionExtremeCenterGroup(
            ViewGeometry view,
            string token,
            bool minimumX)
        {
            GeometryGroup result = new GeometryGroup();
            List<ProjectedPart> parts = PositionParts(view, token);
            if (parts.Count == 0)
                return result;

            double target = minimumX
                ? Double.PositiveInfinity
                : Double.NegativeInfinity;
            for (int i = 0; i < parts.Count; i++)
            {
                target = minimumX
                    ? Math.Min(target, parts[i].CenterX)
                    : Math.Max(target, parts[i].CenterX);
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (Math.Abs(parts[i].CenterX - target) <= GeometryTolerance)
                    result.Add(parts[i]);
            }
            return result;
        }

        private static List<ProjectedPart> PositionParts(ViewGeometry view, string token)
        {
            List<ProjectedPart> result = new List<ProjectedPart>();
            string upper = SafeUpper(token);
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                if (part.Position.IndexOf(upper, StringComparison.Ordinal) >= 0)
                    result.Add(part);
            }

            // PART_POS begins with the drawing/member number (8CURP, 10CURP,
            // ...).  The number is not a geometric identity; the stable suffix
            // describes the role of the plate.  Prefer that semantic suffix,
            // then use an unambiguous geometry/profile fallback for models
            // whose position marks have been renamed completely.
            return result.Count > 0
                ? result
                : GeometricRoleParts(view, upper);
        }

        private static List<ProjectedPart> GeometricRoleParts(
            ViewGeometry view,
            string role)
        {
            List<ProjectedPart> candidates = new List<ProjectedPart>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                if (part == null || part.IsMain || IsDummyPart(part))
                    continue;

                bool match = false;
                if (role == "CUWP")
                    match = IsPlateProfile(part, 28.0) && PartNameContains(part, "PLATE");
                else if (role == "CUUA")
                    match = IsPlateProfile(part, 9.0) && PartNameContains(part, "PLATE");
                else if (role == "CUSP")
                    match = IsPlateProfile(part, 19.0) && PartNameContains(part, "SPLICE");
                else if (role == "CUFIP")
                    match = IsPlateProfile(part, 4.5) && PartNameContains(part, "FILL");
                else if (role == "CURP" || role == "CUFP")
                    match = IsPlateProfile(part, 25.0) && PartNameContains(part, "PLATE");

                if (match)
                    candidates.Add(part);
            }

            if (role != "CURP" && role != "CUFP")
                return candidates;

            // RP and FP share PL25/name PLATE.  Their projected widths separate
            // them in every approved Nishi view: A/B look along the member, so
            // FP is wide and RP narrow; C/FRONT show the member elevation, so
            // RP is wide and FP narrow.  Accept the fallback only when the four
            // candidates split into two clearly distinct pairs.
            if (candidates.Count != 4)
                return new List<ProjectedPart>();

            candidates.Sort(delegate(ProjectedPart a, ProjectedPart b)
            {
                return a.Width.CompareTo(b.Width);
            });

            if (candidates[1].Width + GeometryTolerance >= candidates[2].Width)
                return new List<ProjectedPart>();

            bool takeNarrowPair =
                (view.Key == "A" || view.Key == "B")
                    ? role == "CURP"
                    : role == "CUFP";

            List<ProjectedPart> result = new List<ProjectedPart>();
            int start = takeNarrowPair ? 0 : 2;
            result.Add(candidates[start]);
            result.Add(candidates[start + 1]);
            return result;
        }

        private static bool IsDummyPart(ProjectedPart part)
        {
            return part.Name.IndexOf("DUMMY", StringComparison.Ordinal) >= 0 ||
                   part.Position.IndexOf("DUMMY", StringComparison.Ordinal) >= 0;
        }

        private static bool PartNameContains(ProjectedPart part, string token)
        {
            return part.Name.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private static bool IsPlateProfile(ProjectedPart part, double thickness)
        {
            if (part == null || String.IsNullOrEmpty(part.Profile) ||
                !part.Profile.StartsWith("PL", StringComparison.Ordinal))
                return false;

            int end = 2;
            while (end < part.Profile.Length)
            {
                char value = part.Profile[end];
                if (!Char.IsDigit(value) && value != '.')
                    break;
                end++;
            }

            double actual;
            return end > 2 &&
                   Double.TryParse(
                       part.Profile.Substring(2, end - 2),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out actual) &&
                   Math.Abs(actual - thickness) <= GeometryTolerance;
        }

        private static GeometryGroup NameProfileGroup(
            ViewGeometry view,
            string nameToken,
            string profileToken)
        {
            GeometryGroup result = new GeometryGroup();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                if (part.Name.IndexOf(SafeUpper(nameToken), StringComparison.Ordinal) >= 0 &&
                    part.Profile.IndexOf(SafeUpper(profileToken), StringComparison.Ordinal) >= 0)
                    result.Add(part);
            }
            return result;
        }

        private static GeometryGroup ConcreteGroup(ViewGeometry view)
        {
            GeometryGroup result = new GeometryGroup();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                ProjectedPart part = view.Parts[i];
                bool dummy = part.Name.IndexOf("DUMMY", StringComparison.Ordinal) >= 0 ||
                             part.Position.IndexOf("CONCRETE_DUMMY", StringComparison.Ordinal) >= 0;
                bool plate = part.Profile.StartsWith("PL", StringComparison.Ordinal);
                if (dummy && !plate)
                    result.Add(part);
            }
            return result;
        }

        private static ProjectedPart ExtremePart(
            List<ProjectedPart> parts,
            bool minimumX)
        {
            ProjectedPart best = null;
            for (int i = 0; i < parts.Count; i++)
            {
                ProjectedPart part = parts[i];
                if (best == null ||
                    (minimumX && part.CenterX < best.CenterX) ||
                    (!minimumX && part.CenterX > best.CenterX))
                    best = part;
            }
            return best;
        }

        private static P2 ExtremeVertex(
            List<P2> points,
            bool minimumX,
            bool minimumYOnTie)
        {
            P2 best = null;
            for (int i = 0; i < points.Count; i++)
            {
                P2 point = points[i];
                if (best == null)
                {
                    best = point;
                    continue;
                }

                double delta = point.X - best.X;
                if ((minimumX && delta < -GeometryTolerance) ||
                    (!minimumX && delta > GeometryTolerance))
                {
                    best = point;
                }
                else if (Math.Abs(delta) <= GeometryTolerance && minimumYOnTie &&
                         point.Y < best.Y)
                {
                    best = point;
                }
            }
            return Copy(best);
        }

        private static P2 BottomVertex(List<P2> points, bool minimumXOnTie)
        {
            P2 best = null;
            for (int i = 0; i < points.Count; i++)
            {
                P2 point = points[i];
                if (best == null || point.Y < best.Y - GeometryTolerance ||
                    (Math.Abs(point.Y - best.Y) <= GeometryTolerance &&
                     ((minimumXOnTie && point.X < best.X) ||
                      (!minimumXOnTie && point.X > best.X))))
                    best = point;
            }
            return Copy(best);
        }

        private static P2 ExtremeBolt(
            List<P2> bolts,
            bool minimumX,
            bool minimumYOnTie)
        {
            return ExtremeVertex(bolts, minimumX, minimumYOnTie);
        }

        private static List<P2> SortByX(List<P2> source)
        {
            List<P2> result = CopyUnique(source);
            result.Sort(delegate(P2 a, P2 b)
            {
                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });
            return result;
        }

        private static List<P2> SortByY(List<P2> source)
        {
            List<P2> result = CopyUnique(source);
            result.Sort(delegate(P2 a, P2 b)
            {
                int byY = a.Y.CompareTo(b.Y);
                return byY != 0 ? byY : a.X.CompareTo(b.X);
            });
            return result;
        }

        private static P2 ClosestBoltBelow(List<P2> bolts, double y)
        {
            P2 best = null;
            for (int i = 0; i < bolts.Count; i++)
            {
                P2 point = bolts[i];
                if (point.Y >= y - GeometryTolerance)
                    continue;
                if (best == null || point.Y > best.Y)
                    best = point;
            }
            return Copy(best);
        }

        private static P2 ClosestBoltAbove(List<P2> bolts, double y)
        {
            P2 best = null;
            for (int i = 0; i < bolts.Count; i++)
            {
                P2 point = bolts[i];
                if (point.Y <= y + GeometryTolerance)
                    continue;
                if (best == null || point.Y < best.Y)
                    best = point;
            }
            return Copy(best);
        }

        private static List<P2> UniqueBoltsByX(List<P2> source, bool maximumY)
        {
            List<P2> sorted = SortByX(source);
            List<P2> result = new List<P2>();
            for (int i = 0; i < sorted.Count; i++)
            {
                P2 point = sorted[i];
                int found = -1;
                for (int r = 0; r < result.Count; r++)
                {
                    if (Math.Abs(result[r].X - point.X) <= GeometryTolerance)
                    {
                        found = r;
                        break;
                    }
                }

                if (found < 0)
                    result.Add(Copy(point));
                else if ((maximumY && point.Y > result[found].Y) ||
                         (!maximumY && point.Y < result[found].Y))
                    result[found] = Copy(point);
            }

            result.Sort(delegate(P2 a, P2 b) { return a.X.CompareTo(b.X); });
            return result;
        }

        private static List<P2> BoltsAtExtremeX(List<P2> source, bool minimumX)
        {
            if (source.Count == 0)
                return new List<P2>();

            double target = minimumX
                ? Double.PositiveInfinity
                : Double.NegativeInfinity;
            for (int i = 0; i < source.Count; i++)
                target = minimumX ? Math.Min(target, source[i].X) : Math.Max(target, source[i].X);

            List<P2> result = new List<P2>();
            for (int i = 0; i < source.Count; i++)
            {
                if (Math.Abs(source[i].X - target) <= GeometryTolerance)
                    AddUnique(result, source[i]);
            }
            return SortByY(result);
        }

        private static P2 Transform(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            if (point == null)
                return null;
            TSG.Point global = currentToGlobal.Transform(point);
            TSG.Point local = globalToView.Transform(global);
            return Pt(local.X, local.Y);
        }

        private static TSM.Part GetDrawingMainPart(TSM.Model model, TSD.Drawing drawing)
        {
            TSD.AssemblyDrawing assemblyDrawing = drawing as TSD.AssemblyDrawing;
            if (assemblyDrawing == null)
                return null;

            Tekla.Structures.Identifier id =
                GetMember(assemblyDrawing, "AssemblyIdentifier") as Tekla.Structures.Identifier;
            if (id == null)
                id = GetMember(assemblyDrawing, "ModelIdentifier") as Tekla.Structures.Identifier;
            if (id == null)
                return null;

            TSM.ModelObject value = model.SelectModelObject(id);
            TSM.Part direct = value as TSM.Part;
            if (direct != null)
                return direct;

            TSM.Assembly assembly = value as TSM.Assembly;
            return assembly == null ? null : assembly.GetMainPart() as TSM.Part;
        }

        private static string GetViewKey(TSD.View view)
        {
            object viewType = GetMember(view, "ViewType");
            string typeText = viewType == null ? "" : viewType.ToString();
            if (typeText.IndexOf("FRONT", StringComparison.OrdinalIgnoreCase) >= 0)
                return "FRONT";

            string name = SafeUpper(Convert.ToString(GetMember(view, "Name"), CultureInfo.InvariantCulture));
            if (name == "A" || name == "B" || name == "C")
                return name;
            return "";
        }

        private static double ReadViewScale(TSD.View view)
        {
            try
            {
                return view.Attributes.Scale;
            }
            catch
            {
                object attributes = GetMember(view, "Attributes");
                return ReadDouble(attributes, "Scale");
            }
        }

        private static string GetPartPosition(TSM.Part part)
        {
            try
            {
                string value = "";
                if (part.GetReportProperty("PART_POS", ref value) &&
                    !String.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch
            {
            }

            try
            {
                return part.GetPartMark();
            }
            catch
            {
                return "";
            }
        }

        private static bool SameIdentifier(TSM.Part first, TSM.Part second)
        {
            return first != null && second != null &&
                   first.Identifier != null && second.Identifier != null &&
                   first.Identifier.ID == second.Identifier.ID;
        }

        private static object GetMember(object value, string memberName)
        {
            if (value == null)
                return null;

            try
            {
                Type type = value.GetType();
                PropertyInfo property = type.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                    return property.GetValue(value, null);

                FieldInfo field = type.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return field == null ? null : field.GetValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static double ReadDouble(object value, string memberName)
        {
            object raw = GetMember(value, memberName);
            if (raw == null)
                return Double.NaN;
            try
            {
                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return Double.NaN;
            }
        }

        private static void AddUnique(List<P2> points, P2 point)
        {
            if (points == null || point == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], point) <= GeometryTolerance)
                    return;
            }
            points.Add(Copy(point));
        }

        private static List<P2> CopyUnique(List<P2> source)
        {
            List<P2> result = new List<P2>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                    AddUnique(result, source[i]);
            }
            return result;
        }

        private static P2 Pt(double x, double y)
        {
            return new P2(x, y);
        }

        private static P2 Copy(P2 point)
        {
            return point == null ? null : Pt(point.X, point.Y);
        }

        private static double Distance(P2 a, P2 b)
        {
            if (a == null || b == null)
                return Double.PositiveInfinity;
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string SafeUpper(string value)
        {
            return String.IsNullOrWhiteSpace(value)
                ? ""
                : value.Trim().ToUpperInvariant();
        }

        private static string Format(double value)
        {
            return IsFinite(value)
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : "NA";
        }

        private static string FormatPoint(P2 point)
        {
            return point == null ? "NA" : "(" + Format(point.X) + "," + Format(point.Y) + ")";
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static void ShowMessage(string message, MessageBoxIcon icon)
        {
            MessageBox.Show(
                message,
                "Auto Dimension - Nishi Azabu",
                MessageBoxButtons.OK,
                icon);
        }
    }
}
