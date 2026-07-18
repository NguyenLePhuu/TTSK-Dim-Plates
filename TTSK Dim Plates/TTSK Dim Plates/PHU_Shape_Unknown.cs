#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Drawing.UI;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using ModelPart = Tekla.Structures.Model.Part;

namespace Tekla.Technology.Akit.UserScript
{
    public sealed class ShapeUnknownRunResult
    {
        public bool Success;
        public int ProcessedViewCount;
        public int CreatedDimensionCount;
        public string Message = "";
    }

    public static class ShapeUnknownScript
    {
        private const double MIN_DIMENSION_LENGTH = 1.0;
        private const double DIMENSION_OFFSET = 150.0;
        private const double POINT_UNIQUE_TOLERANCE = 0.01;
        private const int MAX_ENUMERATOR_ITEMS = 20000;
        private const double VIEW_PADDING = 20.0;
        private const double AUTO_SCALE_RESERVE = 200.0;
        private const double A3_SHEET_WIDTH = 420.0;
        private const double A3_SHEET_HEIGHT = 297.0;
        private const double A3_SHEET_MARGIN = 20.0;
        private const double DEFAULT_SHEET_MARGIN = 30.0;
        private const double SHEET_SIZE_TOLERANCE = 2.0;
        private const bool FORCE_CENTER_BY_TOP_BOTTOM_BLOCKS = true;
        private const double CENTER_BOTTOM_BLOCK_HEIGHT_RATIO = 0.18;
        private const double CENTER_TOP_BLOCK_HEIGHT_RATIO = 0.08;
        private const double CENTER_BLOCK_EXTRA_GAP = 5.0;
        private static double LastAppliedAutoScale = 0.0;

        private sealed class ViewDimensionPlan
        {
            public string Name;
            public View View;
            public Point LeftPoint;
            public Point RightPoint;
            public Point BottomPoint;
            public Point TopPoint;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private sealed class ViewPaperBox
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

        public static ShapeUnknownRunResult RunSafe(
            Drawing drawing,
            Model model,
            ModelPart part)
        {
            ShapeUnknownRunResult result = new ShapeUnknownRunResult();
            List<StraightDimensionSet> createdDimensions =
                new List<StraightDimensionSet>();
            LastAppliedAutoScale = 0.0;

            try
            {
                if (drawing == null)
                    return Fail(result, "Khong co drawing dang active.");

                if (model == null || !model.GetConnectionStatus())
                    return Fail(result, "Khong ket noi duoc model Tekla.");

                if (part == null || part.Identifier == null)
                    return Fail(result, "Khong xac dinh duoc ModelPart can dim.");

                List<View> partViews = GetViewsContainingPart(
                    drawing,
                    part.Identifier);

                partViews.Sort(delegate (View a, View b)
                {
                    return b.Origin.Y.CompareTo(a.Origin.Y);
                });

                View topView = FindViewByViewTypeForUnknown(partViews, "TopView", "Top");
                View frontView = FindViewByViewTypeForUnknown(partViews, "FrontView", "Front");
                View bottomView = FindViewByViewTypeForUnknown(partViews, "BottomView", "Bottom");

                List<View> specialTopSections = new List<View>();
                List<View> specialBottomSections = new List<View>();
                List<View> exactSectionViews = new List<View>();

                ClassifySectionViewsForUnknown(
                    partViews,
                    frontView,
                    topView,
                    bottomView,
                    specialTopSections,
                    specialBottomSections,
                    exactSectionViews);

                if (topView == null && specialTopSections.Count > 0)
                    topView = specialTopSections[0];

                List<string> missingViews = new List<string>();
                if (frontView == null) missingViews.Add("Front");
                if (topView == null) missingViews.Add("Top");

                if (missingViews.Count > 0)
                {
                    return Fail(
                        result,
                        "Shape Unknown thieu view chinh: " +
                        string.Join(", ", missingViews.ToArray()) +
                        ". Khong xoa dim cu.");
                }

                List<View> dimViews = BuildDimViewsByViewTypeForUnknown(
                    topView,
                    frontView,
                    bottomView,
                    specialBottomSections);

                View exactSectionView = null;
                bool isSinglePartDrawing = drawing is SinglePartDrawing;
                if (isSinglePartDrawing && exactSectionViews.Count > 0)
                    exactSectionView = exactSectionViews[0];

                List<ViewDimensionPlan> plans = new List<ViewDimensionPlan>();
                string planError;

                ViewDimensionPlan topPlan = BuildViewPlan(
                    model,
                    part,
                    topView,
                    "Top",
                    out planError);
                if (topPlan == null)
                    return Fail(result, planError + " Khong xoa dim cu.");
                plans.Add(topPlan);

                ViewDimensionPlan frontPlan = BuildViewPlan(
                    model,
                    part,
                    frontView,
                    "Front",
                    out planError);
                if (frontPlan == null)
                    return Fail(result, planError + " Khong xoa dim cu.");
                plans.Add(frontPlan);

                for (int i = 2; i < dimViews.Count; i++)
                {
                    ViewDimensionPlan bottomPlan = BuildViewPlan(
                        model,
                        part,
                        dimViews[i],
                        "Bottom",
                        out planError);
                    if (bottomPlan == null)
                        return Fail(result, planError + " Khong xoa dim cu.");
                    plans.Add(bottomPlan);
                }

                if (isSinglePartDrawing)
                {
                    foreach (View exactView in exactSectionViews)
                    {
                        ApplyExactRepresentationToViewUnknown(exactView);
                        CommitAndWait(drawing, 250);
                    }
                }

                foreach (ViewDimensionPlan plan in plans)
                {
                    if (!ApplyExactToTargetPart(plan.View, part.Identifier))
                    {
                        return Fail(
                            result,
                            "Khong dat duoc Exact cho part trong view " +
                            plan.Name + ". Khong xoa dim cu.");
                    }
                }

                foreach (ViewDimensionPlan plan in plans)
                {
                    string deleteError;
                    if (!DeleteDimensionsInView(plan.View, out deleteError))
                    {
                        throw new InvalidOperationException(
                            "Khong xoa duoc dim cu trong view " +
                            plan.Name + ": " + deleteError);
                    }
                }

                CommitAndWait(drawing, 250);

                if (isSinglePartDrawing)
                {
                    ApplyAutoScaleByPartLength(
                        drawing,
                        model,
                        part,
                        topView,
                        partViews);
                    CommitAndWait(drawing, 500);
                }

                StraightDimensionSetHandler handler =
                    new StraightDimensionSetHandler();

                foreach (ViewDimensionPlan plan in plans)
                {
                    StraightDimensionSet horizontal = CreateDimension(
                        handler,
                        plan.View,
                        plan.LeftPoint,
                        plan.RightPoint,
                        new Vector(0, 1, 0),
                        DIMENSION_OFFSET);

                    if (horizontal == null)
                    {
                        throw new InvalidOperationException(
                            "Khong tao duoc dim ngang trong view " + plan.Name + ".");
                    }

                    createdDimensions.Add(horizontal);

                    StraightDimensionSet vertical = CreateDimension(
                        handler,
                        plan.View,
                        plan.BottomPoint,
                        plan.TopPoint,
                        new Vector(-1, 0, 0),
                        DIMENSION_OFFSET);

                    if (vertical == null)
                    {
                        throw new InvalidOperationException(
                            "Khong tao duoc dim doc trong view " + plan.Name + ".");
                    }

                    createdDimensions.Add(vertical);
                }

                CommitAndWait(drawing, 250);

                if (isSinglePartDrawing)
                {
                    foreach (ViewDimensionPlan plan in plans)
                    {
                        ResizeViewBoundaryKeepDepthUnknown(
                            plan.View,
                            plan.MinX,
                            plan.MaxX,
                            plan.MinY,
                            plan.MaxY);
                    }

                    CommitAndWait(drawing, 250);
                }

                ViewDimensionPlan topLayoutPlan = plans[0];
                ViewDimensionPlan frontLayoutPlan = plans[1];
                List<View> bottomViews = new List<View>();

                AlignMainViewsByGeometryUnknown(
                    topLayoutPlan,
                    frontLayoutPlan);

                ViewDimensionPlan previousLayoutPlan = frontLayoutPlan;
                for (int i = 2; i < plans.Count; i++)
                {
                    bottomViews.Add(plans[i].View);
                    AlignMainViewsByGeometryUnknown(
                        previousLayoutPlan,
                        plans[i]);
                    previousLayoutPlan = plans[i];
                }

                const double finalGreenBoxGap = 15.0;
                ArrangeSectionViewRightOfFrontUnknown(
                    exactSectionView,
                    frontView,
                    finalGreenBoxGap);
                CommitAndWait(drawing, 100);

                CenterShapeViewsByPurpleBoxOnSheetUnknown(
                    drawing,
                    topView,
                    frontView,
                    exactSectionView,
                    bottomViews);
                CommitAndWait(drawing, 250);

                ForceFinalEqualArrangeShapeTopFrontBottomGap15Unknown(
                    topView,
                    frontView,
                    bottomViews,
                    finalGreenBoxGap);
                CommitAndWait(drawing, 250);

                ArrangeSectionViewRightOfFrontUnknown(
                    exactSectionView,
                    frontView,
                    finalGreenBoxGap);
                CommitAndWait(drawing, 250);

                UpdateDrawingTitle3ScaleUnknown(drawing, topView);
                CommitAndWait(drawing, 250);

                SelectViewsUnknown(partViews);

                result.Success = true;
                result.ProcessedViewCount = plans.Count;
                result.CreatedDimensionCount = createdDimensions.Count;
                result.Message = plans.Count > 2
                    ? "Shape Unknown: dim ngang/doc Front-Top-Bottom thanh cong (" +
                      createdDimensions.Count.ToString() + " dim)."
                    : "Shape Unknown: dim ngang/doc Front-Top thanh cong (4 dim).";
                return result;
            }
            catch (Exception ex)
            {
                RollBackCreatedDimensions(createdDimensions, drawing);
                return Fail(result, "Shape Unknown dim that bai: " + ex.Message);
            }
        }

        private static ShapeUnknownRunResult Fail(
            ShapeUnknownRunResult result,
            string message)
        {
            if (result == null)
                result = new ShapeUnknownRunResult();

            result.Success = false;
            result.Message = message ?? "Shape Unknown dim that bai.";
            return result;
        }

        private static ViewDimensionPlan BuildViewPlan(
            Model model,
            ModelPart part,
            View view,
            string name,
            out string error)
        {
            error = "";
            TransformationPlane oldPlane = null;

            try
            {
                if (model == null || part == null || view == null)
                {
                    error = "Du lieu preflight " + name + " khong hop le.";
                    return null;
                }

                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem));

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                {
                    error = "Khong doc duoc Solid trong view " + name + ".";
                    return null;
                }

                double minX = solid.MinimumPoint.X;
                double maxX = solid.MaximumPoint.X;
                double minY = solid.MinimumPoint.Y;
                double maxY = solid.MaximumPoint.Y;

                if (!IsFinite(minX) || !IsFinite(maxX) ||
                    !IsFinite(minY) || !IsFinite(maxY))
                {
                    error = "Gioi han Solid trong view " + name + " khong hop le.";
                    return null;
                }

                if (Math.Abs(maxX - minX) < MIN_DIMENSION_LENGTH ||
                    Math.Abs(maxY - minY) < MIN_DIMENSION_LENGTH)
                {
                    error = "Kich thuoc chieu ngang/doc trong view " +
                            name + " nho hon 1 mm.";
                    return null;
                }

                List<Point> projectedPoints = GetProjectedSolidPoints(solid);
                double centerX = (minX + maxX) / 2.0;
                double centerY = (minY + maxY) / 2.0;

                Point leftCandidate = FindNearestPointForX(
                    projectedPoints,
                    minX,
                    centerY);
                Point rightCandidate = FindNearestPointForX(
                    projectedPoints,
                    maxX,
                    centerY);
                Point bottomCandidate = FindNearestPointForY(
                    projectedPoints,
                    minY,
                    centerX);
                Point topCandidate = FindNearestPointForY(
                    projectedPoints,
                    maxY,
                    centerX);

                bool useTopEdgeForHorizontal =
                    string.Equals(name, "Top", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Front", StringComparison.OrdinalIgnoreCase);
                bool useLeftEdgeForVertical =
                    string.Equals(name, "Top", StringComparison.OrdinalIgnoreCase);

                ViewDimensionPlan plan = new ViewDimensionPlan();
                plan.Name = name;
                plan.View = view;
                plan.LeftPoint = new Point(
                    minX,
                    useTopEdgeForHorizontal
                        ? maxY
                        : (leftCandidate != null ? leftCandidate.Y : centerY),
                    0);
                plan.RightPoint = new Point(
                    maxX,
                    useTopEdgeForHorizontal
                        ? maxY
                        : (rightCandidate != null ? rightCandidate.Y : centerY),
                    0);
                plan.BottomPoint = new Point(
                    useLeftEdgeForVertical
                        ? minX
                        : (bottomCandidate != null ? bottomCandidate.X : centerX),
                    minY,
                    0);
                plan.TopPoint = new Point(
                    useLeftEdgeForVertical
                        ? minX
                        : (topCandidate != null ? topCandidate.X : centerX),
                    maxY,
                    0);
                plan.MinX = minX;
                plan.MaxX = maxX;
                plan.MinY = minY;
                plan.MaxY = maxY;

                return plan;
            }
            catch (Exception ex)
            {
                error = "Preflight view " + name + " loi: " + ex.Message;
                return null;
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

        private static StraightDimensionSet CreateDimension(
            StraightDimensionSetHandler handler,
            View view,
            Point first,
            Point second,
            Vector direction,
            double offset)
        {
            if (handler == null || view == null ||
                first == null || second == null || direction == null)
                return null;

            PointList points = new PointList();
            points.Add(new Point(first.X, first.Y, 0));
            points.Add(new Point(second.X, second.Y, 0));

            return handler.CreateDimensionSet(
                view,
                points,
                direction,
                offset);
        }

        private static List<View> GetViewsContainingPart(
            Drawing drawing,
            Identifier partIdentifier)
        {
            List<View> result = new List<View>();

            if (drawing == null || partIdentifier == null)
                return result;

            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                return result;

            DrawingObjectEnumerator views = sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                View view = views.Current as View;
                if (view != null && ViewContainsPart(view, partIdentifier))
                    result.Add(view);
            }

            return result;
        }

        private static bool ViewContainsPart(
            View view,
            Identifier partIdentifier)
        {
            if (view == null || partIdentifier == null)
                return false;

            DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
            while (parts != null && parts.MoveNext())
            {
                DrawingPart drawingPart = parts.Current as DrawingPart;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;

                if (drawingPart.ModelIdentifier.ID == partIdentifier.ID)
                    return true;
            }

            return false;
        }

        private static View FindStandardView(
            List<View> views,
            string exactViewTypeName,
            string fallbackText)
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

                if (viewTypeText.IndexOf(
                    "Section",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (string.Equals(
                    viewTypeText,
                    exactViewTypeName,
                    StringComparison.OrdinalIgnoreCase))
                    return view;
            }

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

                if (viewTypeText.IndexOf(
                    "Section",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!string.IsNullOrEmpty(fallbackText) &&
                    viewTypeText.IndexOf(
                        fallbackText,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return view;
            }

            return null;
        }

        private static bool ApplyExactToTargetPart(
            View view,
            Identifier partIdentifier)
        {
            if (view == null || partIdentifier == null)
                return false;

            DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
            while (parts != null && parts.MoveNext())
            {
                DrawingPart drawingPart = parts.Current as DrawingPart;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;

                if (drawingPart.ModelIdentifier.ID != partIdentifier.ID)
                    continue;

                DrawingPart.PartAttributes attributes = drawingPart.Attributes;
                if (attributes == null)
                    return false;

                attributes.Representation = DrawingPart.Representation.Exact;
                drawingPart.Attributes = attributes;
                return drawingPart.Modify();
            }

            return false;
        }

        private static void ApplyExactRepresentationToViewUnknown(View view)
        {
            try
            {
                if (view == null)
                    return;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(DrawingPart));
                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (drawingPart == null)
                        continue;

                    DrawingPart.PartAttributes attributes = drawingPart.Attributes;
                    if (attributes == null)
                        continue;

                    attributes.Representation = DrawingPart.Representation.Exact;
                    drawingPart.Attributes = attributes;
                    drawingPart.Modify();
                }
            }
            catch
            {
            }
        }

        private static bool DeleteDimensionsInView(
            View view,
            out string error)
        {
            error = "";

            try
            {
                if (view == null)
                {
                    error = "View null.";
                    return false;
                }

                Type[] dimensionTypes = new Type[]
                {
                    typeof(StraightDimensionSet),
                    typeof(CurvedDimensionSetRadial),
                    typeof(CurvedDimensionSetOrthogonal),
                    typeof(AngleDimension),
                    typeof(RadiusDimension)
                };

                foreach (Type dimensionType in dimensionTypes)
                {
                    DrawingObjectEnumerator objects =
                        view.GetAllObjects(dimensionType);

                    while (objects != null && objects.MoveNext())
                    {
                        DrawingObject drawingObject =
                            objects.Current as DrawingObject;

                        if (drawingObject != null && !drawingObject.Delete())
                        {
                            error = "Delete() tra false cho " + dimensionType.Name + ".";
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void RollBackCreatedDimensions(
            List<StraightDimensionSet> createdDimensions,
            Drawing drawing)
        {
            if (createdDimensions == null || createdDimensions.Count == 0)
                return;

            foreach (StraightDimensionSet dimension in createdDimensions)
            {
                if (dimension == null)
                    continue;

                try
                {
                    dimension.Delete();
                }
                catch
                {
                }
            }

            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch
            {
            }
        }

        private static List<Point> GetProjectedSolidPoints(Solid solid)
        {
            List<Point> result = new List<Point>();

            if (solid != null)
                CollectSolidPoints(solid, result, 0);

            return result;
        }

        private static void CollectSolidPoints(
            object source,
            List<Point> result,
            int depth)
        {
            if (source == null || result == null || depth > 8)
                return;

            Point directPoint = source as Point;
            if (directPoint != null)
            {
                AddUniquePoint(
                    result,
                    new Point(directPoint.X, directPoint.Y, 0));
                return;
            }

            TryCollectEnumerator(source, result, depth, "GetFaceEnumerator");
            TryCollectEnumerator(source, result, depth, "GetLoopEnumerator");
            TryCollectEnumerator(source, result, depth, "GetVertexEnumerator");
            TryCollectEnumerator(source, result, depth, "GetEdgeEnumerator");
            TryCollectEnumerator(source, result, depth, "GetPointEnumerator");

            TryCollectPointProperty(source, result, "Point");
            TryCollectPointProperty(source, result, "Position");
            TryCollectPointProperty(source, result, "StartPoint");
            TryCollectPointProperty(source, result, "EndPoint");
            TryCollectPointProperty(source, result, "CenterPoint");
            TryCollectPointProperty(source, result, "ArcMiddlePoint");

            IEnumerable enumerable = source as IEnumerable;
            if (enumerable != null && !(source is string))
            {
                foreach (object item in enumerable)
                    CollectSolidPoints(item, result, depth + 1);
            }
        }

        private static void TryCollectEnumerator(
            object source,
            List<Point> result,
            int depth,
            string methodName)
        {
            try
            {
                MethodInfo method = source.GetType().GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (method == null || method.GetParameters().Length != 0)
                    return;

                object enumerator = method.Invoke(source, null);
                if (enumerator == null)
                    return;

                MethodInfo moveNext = enumerator.GetType().GetMethod(
                    "MoveNext",
                    BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo current = enumerator.GetType().GetProperty(
                    "Current",
                    BindingFlags.Public | BindingFlags.Instance);

                if (moveNext == null || current == null)
                    return;

                int guard = 0;
                while (guard < MAX_ENUMERATOR_ITEMS)
                {
                    guard++;
                    object moved = moveNext.Invoke(enumerator, null);
                    if (!(moved is bool) || !(bool)moved)
                        break;

                    CollectSolidPoints(
                        current.GetValue(enumerator, null),
                        result,
                        depth + 1);
                }
            }
            catch
            {
            }
        }

        private static void TryCollectPointProperty(
            object source,
            List<Point> result,
            string propertyName)
        {
            try
            {
                PropertyInfo property = source.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (property == null || !property.CanRead ||
                    property.PropertyType != typeof(Point))
                    return;

                Point point = property.GetValue(source, null) as Point;
                if (point != null)
                {
                    AddUniquePoint(
                        result,
                        new Point(point.X, point.Y, 0));
                }
            }
            catch
            {
            }
        }

        private static void AddUniquePoint(
            List<Point> points,
            Point point)
        {
            if (points == null || point == null)
                return;

            double toleranceSquared =
                POINT_UNIQUE_TOLERANCE * POINT_UNIQUE_TOLERANCE;

            foreach (Point existing in points)
            {
                double dx = existing.X - point.X;
                double dy = existing.Y - point.Y;
                if (dx * dx + dy * dy <= toleranceSquared)
                    return;
            }

            points.Add(point);
        }

        private static Point FindNearestPointForX(
            List<Point> points,
            double targetX,
            double preferredY)
        {
            Point best = null;
            double bestPrimary = double.MaxValue;
            double bestSecondary = double.MaxValue;

            if (points == null)
                return null;

            foreach (Point point in points)
            {
                if (point == null)
                    continue;

                double primary = Math.Abs(point.X - targetX);
                double secondary = Math.Abs(point.Y - preferredY);

                if (primary < bestPrimary - POINT_UNIQUE_TOLERANCE ||
                    (Math.Abs(primary - bestPrimary) <= POINT_UNIQUE_TOLERANCE &&
                     secondary < bestSecondary))
                {
                    best = point;
                    bestPrimary = primary;
                    bestSecondary = secondary;
                }
            }

            return best;
        }

        private static Point FindNearestPointForY(
            List<Point> points,
            double targetY,
            double preferredX)
        {
            Point best = null;
            double bestPrimary = double.MaxValue;
            double bestSecondary = double.MaxValue;

            if (points == null)
                return null;

            foreach (Point point in points)
            {
                if (point == null)
                    continue;

                double primary = Math.Abs(point.Y - targetY);
                double secondary = Math.Abs(point.X - preferredX);

                if (primary < bestPrimary - POINT_UNIQUE_TOLERANCE ||
                    (Math.Abs(primary - bestPrimary) <= POINT_UNIQUE_TOLERANCE &&
                     secondary < bestSecondary))
                {
                    best = point;
                    bestPrimary = primary;
                    bestSecondary = secondary;
                }
            }

            return best;
        }

        private static View FindViewByViewTypeForUnknown(
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
                    if (ViewTypeMatchesForUnknown(
                        view,
                        exactViewTypeName,
                        fallbackText))
                        return view;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ViewTypeMatchesForUnknown(
            View view,
            string exactViewTypeName,
            string fallbackText)
        {
            try
            {
                if (view == null)
                    return false;

                string text = view.ViewType.ToString();
                if (!string.IsNullOrEmpty(exactViewTypeName) &&
                    string.Equals(text, exactViewTypeName, StringComparison.OrdinalIgnoreCase))
                    return true;

                return !string.IsNullOrEmpty(fallbackText) &&
                       text.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ClassifySectionViewsForUnknown(
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
                    if (!ViewTypeMatchesForUnknown(view, "SectionView", "Section"))
                        continue;

                    if (object.ReferenceEquals(view, topViewByType) ||
                        object.ReferenceEquals(view, bottomViewByType))
                        continue;

                    bool isSpecial = false;
                    if (frontView != null &&
                        IsSectionWidthCloseToFrontForUnknown(view, frontView))
                    {
                        if (view.Origin.Y > frontView.Origin.Y)
                        {
                            AddUniqueViewForUnknown(specialTopSections, view);
                            isSpecial = true;
                        }
                        else if (view.Origin.Y < frontView.Origin.Y)
                        {
                            AddUniqueViewForUnknown(specialBottomSections, view);
                            isSpecial = true;
                        }
                    }

                    if (!isSpecial)
                        AddUniqueViewForUnknown(exactSectionViews, view);
                }
            }
            catch
            {
            }
        }

        private static bool IsSectionWidthCloseToFrontForUnknown(
            View sectionView,
            View frontView)
        {
            try
            {
                double sectionWidth = GetViewRestrictionBoxWidthForUnknown(sectionView);
                double frontWidth = GetViewRestrictionBoxWidthForUnknown(frontView);
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

        private static double GetViewRestrictionBoxWidthForUnknown(View view)
        {
            try
            {
                AABB box = view != null ? view.RestrictionBox : null;
                if (box == null || box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                return Math.Abs(box.MaxPoint.X - box.MinPoint.X);
            }
            catch
            {
                return 0.0;
            }
        }

        private static List<View> BuildDimViewsByViewTypeForUnknown(
            View topView,
            View frontView,
            View bottomViewByType,
            List<View> specialBottomSections)
        {
            List<View> result = new List<View>();
            AddUniqueViewForUnknown(result, topView);
            AddUniqueViewForUnknown(result, frontView);

            if (bottomViewByType != null)
            {
                AddUniqueViewForUnknown(result, bottomViewByType);
            }
            else if (specialBottomSections != null)
            {
                foreach (View view in specialBottomSections)
                    AddUniqueViewForUnknown(result, view);
            }

            return result;
        }

        private static void AddUniqueViewForUnknown(List<View> views, View view)
        {
            if (views == null || view == null)
                return;

            foreach (View existing in views)
            {
                if (object.ReferenceEquals(existing, view))
                    return;
            }

            views.Add(view);
        }

        private static void ApplyAutoScaleByPartLength(
            Drawing drawing,
            Model model,
            ModelPart part,
            View referenceView,
            List<View> views)
        {
            try
            {
                if (drawing == null || model == null || part == null ||
                    referenceView == null || views == null)
                    return;

                double sheetWidth;
                double sheetHeight;
                if (!TryGetDrawingSheetSizeUnknown(drawing, out sheetWidth, out sheetHeight))
                    return;

                double beamLength = GetBeamLengthInViewUnknown(model, part, referenceView);
                if (beamLength <= 1.0)
                    return;

                double scale = GetAutoViewScaleByPartLengthUnknown(
                    beamLength,
                    sheetWidth,
                    sheetHeight);
                LastAppliedAutoScale = scale;

                foreach (View view in views)
                    SetViewScaleUnknown(view, scale);
            }
            catch
            {
            }
        }

        private static double GetBeamLengthInViewUnknown(
            Model model,
            ModelPart part,
            View view)
        {
            TransformationPlane oldPlane = null;
            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(view.DisplayCoordinateSystem));

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

        private static double GetAutoViewScaleByPartLengthUnknown(
            double beamLength,
            double sheetWidth,
            double sheetHeight)
        {
            double paperLength = Math.Max(sheetWidth, sheetHeight);
            double usablePaperLength = paperLength -
                GetScaleMarginBySheetSizeUnknown(sheetWidth, sheetHeight);
            if (usablePaperLength <= 1.0)
                return 30.0;

            double requiredScale = (beamLength + AUTO_SCALE_RESERVE) / usablePaperLength;
            double[] allowedScales = new double[] { 5.0, 10.0, 15.0, 20.0, 30.0 };
            foreach (double scale in allowedScales)
            {
                if (scale >= requiredScale)
                    return scale;
            }

            return 30.0;
        }

        private static double GetScaleMarginBySheetSizeUnknown(double width, double height)
        {
            return IsSheetSizeUnknown(width, height, A3_SHEET_WIDTH, A3_SHEET_HEIGHT)
                ? A3_SHEET_MARGIN
                : DEFAULT_SHEET_MARGIN;
        }

        private static bool IsSheetSizeUnknown(
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

        private static bool TryGetDrawingSheetSizeUnknown(
            Drawing drawing,
            out double width,
            out double height)
        {
            width = 0.0;
            height = 0.0;
            try
            {
                object layout = TryGetObjectPropertyUnknown(drawing, "Layout");
                object sheetSize = TryGetObjectPropertyUnknown(layout, "SheetSize");
                object w = TryGetObjectPropertyUnknown(sheetSize, "Width");
                object h = TryGetObjectPropertyUnknown(sheetSize, "Height");
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

        private static void SetViewScaleUnknown(View view, double scale)
        {
            if (view == null)
                return;

            try
            {
                object attributes = null;
                try { attributes = view.Attributes; }
                catch { attributes = null; }

                if (attributes != null)
                {
                    SetScalePropertiesUnknown(attributes, scale);
                    try
                    {
                        PropertyInfo attributeProperty = view.GetType().GetProperty(
                            "Attributes",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (attributeProperty != null && attributeProperty.CanWrite)
                            attributeProperty.SetValue(view, attributes, null);
                    }
                    catch
                    {
                    }
                }

                SetScalePropertiesUnknown(view, scale);
                try { view.Modify(); }
                catch { }
            }
            catch
            {
            }
        }

        private static void SetScalePropertiesUnknown(object obj, double scale)
        {
            if (obj == null)
                return;

            try
            {
                PropertyInfo[] properties = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);
                foreach (PropertyInfo property in properties)
                {
                    if (property == null)
                        continue;

                    string name = property.Name.ToUpperInvariant();
                    if (name.IndexOf("SCALE", StringComparison.Ordinal) < 0)
                        continue;

                    try
                    {
                        if (property.CanWrite && property.PropertyType == typeof(double))
                        {
                            property.SetValue(obj, scale, null);
                        }
                        else if (property.CanWrite && property.PropertyType == typeof(int))
                        {
                            property.SetValue(obj, Convert.ToInt32(scale), null);
                        }
                        else if (property.CanWrite && property.PropertyType == typeof(float))
                        {
                            property.SetValue(obj, Convert.ToSingle(scale), null);
                        }
                        else if (property.CanRead)
                        {
                            object value = property.GetValue(obj, null);
                            TrySetObjectPropertyUnknown(value, "Denominator", Convert.ToInt32(scale));
                            TrySetObjectPropertyUnknown(value, "Numerator", 1);
                            TrySetObjectPropertyUnknown(value, "X", 1.0);
                            TrySetObjectPropertyUnknown(value, "Y", scale);
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

        private static object TryGetObjectPropertyUnknown(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                PropertyInfo property = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return property != null && property.CanRead
                    ? property.GetValue(obj, null)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetObjectPropertyUnknown(
            object obj,
            string propertyName,
            object value)
        {
            try
            {
                if (obj == null || value == null)
                    return false;

                PropertyInfo property = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                    return false;

                Type type = property.PropertyType;
                if (type == typeof(double))
                    property.SetValue(obj, Convert.ToDouble(value), null);
                else if (type == typeof(float))
                    property.SetValue(obj, Convert.ToSingle(value), null);
                else if (type == typeof(int))
                    property.SetValue(obj, Convert.ToInt32(value), null);
                else
                    property.SetValue(obj, value, null);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double GetCurrentDrawingScaleUnknown(View referenceView)
        {
            if (LastAppliedAutoScale > 0.0)
                return LastAppliedAutoScale;

            return TryGetViewScaleUnknown(referenceView);
        }

        private static double TryGetViewScaleUnknown(View view)
        {
            try
            {
                double scale = TryGetScaleFromObjectUnknown(view);
                if (scale > 0.0)
                    return scale;

                object attributes = view != null ? view.Attributes : null;
                return TryGetScaleFromObjectUnknown(attributes);
            }
            catch
            {
                return 0.0;
            }
        }

        private static double TryGetScaleFromObjectUnknown(object obj)
        {
            try
            {
                if (obj == null)
                    return 0.0;

                PropertyInfo[] properties = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);
                foreach (PropertyInfo property in properties)
                {
                    if (property == null || !property.CanRead ||
                        property.Name.ToUpperInvariant().IndexOf("SCALE", StringComparison.Ordinal) < 0)
                        continue;

                    object value = property.GetValue(obj, null);
                    double direct;
                    if (TryConvertScaleValueUnknown(value, out direct))
                        return direct;

                    object denominator = TryGetObjectPropertyUnknown(value, "Denominator");
                    if (TryConvertScaleValueUnknown(denominator, out direct))
                        return direct;

                    object y = TryGetObjectPropertyUnknown(value, "Y");
                    if (TryConvertScaleValueUnknown(y, out direct))
                        return direct;
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static bool TryConvertScaleValueUnknown(object value, out double result)
        {
            result = 0.0;
            try
            {
                if (value == null)
                    return false;

                Type type = value.GetType();
                if (type != typeof(double) && type != typeof(float) &&
                    type != typeof(int) && type != typeof(short) && type != typeof(long))
                    return false;

                result = Convert.ToDouble(value);
                return result > 0.0;
            }
            catch
            {
                return false;
            }
        }

        private static void ResizeViewBoundaryKeepDepthUnknown(
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            try
            {
                AABB oldBox = view != null ? view.RestrictionBox : null;
                if (oldBox == null || oldBox.MinPoint == null || oldBox.MaxPoint == null)
                    return;

                view.RestrictionBox = new AABB(
                    new Point(minX - VIEW_PADDING, minY - VIEW_PADDING, oldBox.MinPoint.Z),
                    new Point(maxX + VIEW_PADDING, maxY + VIEW_PADDING, oldBox.MaxPoint.Z));
                view.Modify();
            }
            catch
            {
            }
        }

        private static void AlignMainViewsByGeometryUnknown(
            ViewDimensionPlan basePlan,
            ViewDimensionPlan targetPlan)
        {
            try
            {
                if (basePlan == null || targetPlan == null ||
                    basePlan.View == null || targetPlan.View == null)
                    return;

                double scale = GetCurrentDrawingScaleUnknown(basePlan.View);
                if (scale <= 0.0)
                    scale = 1.0;

                Point baseOrigin = basePlan.View.Origin;
                Point targetOrigin = targetPlan.View.Origin;
                if (baseOrigin == null || targetOrigin == null)
                    return;

                double targetSheetLeft = baseOrigin.X + basePlan.MinX / scale;
                double currentSheetLeft = targetOrigin.X + targetPlan.MinX / scale;
                double targetSheetTop = baseOrigin.Y + basePlan.MinY / scale -
                                        (DIMENSION_OFFSET * 2.0) / scale;
                double currentSheetTop = targetOrigin.Y + targetPlan.MaxY / scale;

                TryMoveViewUnknown(
                    targetPlan.View,
                    targetSheetLeft - currentSheetLeft,
                    targetSheetTop - currentSheetTop);
            }
            catch
            {
            }
        }

        private static void ArrangeSectionViewRightOfFrontUnknown(
            View sectionView,
            View frontView,
            double greenBoxGap)
        {
            try
            {
                if (sectionView == null || frontView == null)
                    return;

                ViewPaperBox frontBox;
                ViewPaperBox sectionBox;
                if (!TryGetViewGreenPaperBoxForUnknown(frontView, out frontBox) ||
                    !TryGetViewGreenPaperBoxForUnknown(sectionView, out sectionBox))
                    return;

                if (greenBoxGap < 0.0)
                    greenBoxGap = 0.0;

                double deltaX =
                    frontBox.MaxX + greenBoxGap - sectionBox.MinX;
                Point frontOrigin = frontView.Origin;
                Point sectionOrigin = sectionView.Origin;
                if (frontOrigin == null || sectionOrigin == null)
                    return;

                TryMoveViewUnknown(
                    sectionView,
                    deltaX,
                    frontOrigin.Y - sectionOrigin.Y);
            }
            catch
            {
            }
        }

        private static bool TryGetViewPurplePaperBoxForUnknown(
            View view,
            out ViewPaperBox box)
        {
            box = null;
            try
            {
                AABB restrictionBox = view != null ? view.RestrictionBox : null;
                Point origin = view != null ? view.Origin : null;
                if (restrictionBox == null || restrictionBox.MinPoint == null ||
                    restrictionBox.MaxPoint == null || origin == null)
                    return false;

                double scale = GetCurrentDrawingScaleUnknown(view);
                if (scale <= 0.0)
                    scale = 1.0;

                double x1 = origin.X + restrictionBox.MinPoint.X / scale;
                double x2 = origin.X + restrictionBox.MaxPoint.X / scale;
                double y1 = origin.Y + restrictionBox.MinPoint.Y / scale;
                double y2 = origin.Y + restrictionBox.MaxPoint.Y / scale;

                box = CreateViewPaperBoxUnknown(
                    view,
                    Math.Min(x1, x2),
                    Math.Max(x1, x2),
                    Math.Min(y1, y2),
                    Math.Max(y1, y2));

                return box.Width > 0.5 && box.Height > 0.5 &&
                       box.Width <= 1000.0 && box.Height <= 1000.0;
            }
            catch
            {
                box = null;
                return false;
            }
        }

        private static bool TryGetViewGreenPaperBoxForUnknown(
            View view,
            out ViewPaperBox box)
        {
            box = null;
            try
            {
                AABB boundingBox = view != null ? view.GetAxisAlignedBoundingBox() : null;
                if (boundingBox == null || boundingBox.MinPoint == null ||
                    boundingBox.MaxPoint == null)
                    return false;

                box = CreateViewPaperBoxUnknown(
                    view,
                    Math.Min(boundingBox.MinPoint.X, boundingBox.MaxPoint.X),
                    Math.Max(boundingBox.MinPoint.X, boundingBox.MaxPoint.X),
                    Math.Min(boundingBox.MinPoint.Y, boundingBox.MaxPoint.Y),
                    Math.Max(boundingBox.MinPoint.Y, boundingBox.MaxPoint.Y));

                return box.Width > 0.5 && box.Height > 0.5;
            }
            catch
            {
                box = null;
                return false;
            }
        }

        private static ViewPaperBox CreateViewPaperBoxUnknown(
            View view,
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            ViewPaperBox box = new ViewPaperBox();
            box.View = view;
            box.MinX = minX;
            box.MaxX = maxX;
            box.MinY = minY;
            box.MaxY = maxY;
            box.Width = Math.Abs(maxX - minX);
            box.Height = Math.Abs(maxY - minY);
            box.CenterY = (minY + maxY) * 0.5;
            return box;
        }

        private static void TryMoveViewUnknown(View view, double deltaX, double deltaY)
        {
            try
            {
                if (view == null ||
                    (Math.Abs(deltaX) <= 0.01 && Math.Abs(deltaY) <= 0.01))
                    return;

                Point origin = view.Origin;
                if (origin == null)
                    return;

                PropertyInfo property = view.GetType().GetProperty(
                    "Origin",
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                    return;

                property.SetValue(
                    view,
                    new Point(origin.X + deltaX, origin.Y + deltaY, origin.Z),
                    null);
                view.Modify();
            }
            catch
            {
            }
        }

        private static void CenterShapeViewsByPurpleBoxOnSheetUnknown(
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
                if (!TryGetDrawingSheetSizeUnknown(drawing, out sheetWidth, out sheetHeight))
                    return;

                double margin = GetScaleMarginBySheetSizeUnknown(sheetWidth, sheetHeight) * 0.5;
                double usableMinX = margin;
                double usableMaxX = sheetWidth - margin;
                double usableMinY = margin;
                double usableMaxY = sheetHeight - margin;

                ApplyTopBottomSheetBlockLimitForCenterUnknown(
                    drawing,
                    sheetWidth,
                    sheetHeight,
                    margin,
                    ref usableMinY,
                    ref usableMaxY);
                ApplyForcedTopBottomBlockLimitForCenterUnknown(
                    sheetWidth,
                    sheetHeight,
                    ref usableMinY,
                    ref usableMaxY);

                if (usableMaxX <= usableMinX + 1.0 ||
                    usableMaxY <= usableMinY + 1.0)
                    return;

                List<View> views = new List<View>();
                AddUniqueViewForUnknown(views, topView);
                AddUniqueViewForUnknown(views, frontView);
                AddUniqueViewForUnknown(views, sectionView);
                if (bottomViews != null)
                {
                    foreach (View bottomView in bottomViews)
                        AddUniqueViewForUnknown(views, bottomView);
                }

                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minY = double.MaxValue;
                double maxY = double.MinValue;
                int count = 0;
                foreach (View view in views)
                {
                    ViewPaperBox box;
                    if (!TryGetViewPurplePaperBoxForUnknown(view, out box))
                        continue;

                    if (box.MinX < minX) minX = box.MinX;
                    if (box.MaxX > maxX) maxX = box.MaxX;
                    if (box.MinY < minY) minY = box.MinY;
                    if (box.MaxY > maxY) maxY = box.MaxY;
                    count++;
                }

                if (count == 0 || maxX <= minX + 1.0 || maxY <= minY + 1.0)
                    return;

                double deltaX = (usableMinX + usableMaxX) * 0.5 - (minX + maxX) * 0.5;
                double deltaY = (usableMinY + usableMaxY) * 0.5 - (minY + maxY) * 0.5;
                if (Math.Abs(deltaX) > sheetWidth * 2.0 ||
                    Math.Abs(deltaY) > sheetHeight * 2.0)
                    return;

                foreach (View view in views)
                    TryMoveViewUnknown(view, deltaX, deltaY);
            }
            catch
            {
            }
        }

        private static void ApplyForcedTopBottomBlockLimitForCenterUnknown(
            double sheetWidth,
            double sheetHeight,
            ref double usableMinY,
            ref double usableMaxY)
        {
            try
            {
                if (!FORCE_CENTER_BY_TOP_BOTTOM_BLOCKS ||
                    sheetWidth <= 1.0 || sheetHeight <= 1.0)
                    return;

                double bottomReserved = sheetHeight * CENTER_BOTTOM_BLOCK_HEIGHT_RATIO +
                                        CENTER_BLOCK_EXTRA_GAP;
                double topReserved = sheetHeight * CENTER_TOP_BLOCK_HEIGHT_RATIO +
                                     CENTER_BLOCK_EXTRA_GAP;
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

        private static void ApplyTopBottomSheetBlockLimitForCenterUnknown(
            Drawing drawing,
            double sheetWidth,
            double sheetHeight,
            double margin,
            ref double usableMinY,
            ref double usableMaxY)
        {
            try
            {
                ContainerView sheet = drawing != null ? drawing.GetSheet() : null;
                if (sheet == null)
                    return;

                double bottomLimit = usableMinY;
                double topLimit = usableMaxY;
                double bottomBandMaxY = sheetHeight * 0.35;
                double topBandMinY = sheetHeight * 0.65;
                DrawingObjectEnumerator objects = sheet.GetAllObjects();

                while (objects != null && objects.MoveNext())
                {
                    DrawingObject drawingObject = objects.Current as DrawingObject;
                    if (drawingObject == null || drawingObject is View)
                        continue;

                    AABB box;
                    if (!TryGetDrawingObjectPaperBoxForCenterUnknown(drawingObject, out box))
                        continue;

                    double minX = Math.Min(box.MinPoint.X, box.MaxPoint.X);
                    double maxX = Math.Max(box.MinPoint.X, box.MaxPoint.X);
                    double minY = Math.Min(box.MinPoint.Y, box.MaxPoint.Y);
                    double maxY = Math.Max(box.MinPoint.Y, box.MaxPoint.Y);
                    double width = Math.Abs(maxX - minX);
                    double height = Math.Abs(maxY - minY);
                    if ((width < 2.0 && height < 2.0) ||
                        (width > sheetWidth * 0.95 && height > sheetHeight * 0.90))
                        continue;

                    if (minX < -sheetWidth || maxX > sheetWidth * 2.0 ||
                        minY < -sheetHeight || maxY > sheetHeight * 2.0)
                        continue;

                    double centerY = (minY + maxY) * 0.5;
                    if (centerY <= bottomBandMaxY && minY <= bottomBandMaxY &&
                        maxY > bottomLimit && maxY < sheetHeight * 0.55)
                        bottomLimit = maxY;

                    if (centerY >= topBandMinY && maxY >= topBandMinY &&
                        minY < topLimit && minY > sheetHeight * 0.45)
                        topLimit = minY;
                }

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

        private static bool TryGetDrawingObjectPaperBoxForCenterUnknown(
            DrawingObject drawingObject,
            out AABB box)
        {
            box = null;
            try
            {
                if (drawingObject == null)
                    return false;

                MethodInfo method = drawingObject.GetType().GetMethod(
                    "GetAxisAlignedBoundingBox",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                    return false;

                box = method.Invoke(drawingObject, null) as AABB;
                return box != null && box.MinPoint != null && box.MaxPoint != null;
            }
            catch
            {
                box = null;
                return false;
            }
        }

        private static void ForceFinalEqualArrangeShapeTopFrontBottomGap15Unknown(
            View topView,
            View frontView,
            List<View> bottomViews,
            double gap)
        {
            try
            {
                List<View> stackViews = new List<View>();
                AddUniqueViewForUnknown(stackViews, topView);
                AddUniqueViewForUnknown(stackViews, frontView);
                if (bottomViews != null)
                {
                    foreach (View bottomView in bottomViews)
                        AddUniqueViewForUnknown(stackViews, bottomView);
                }

                if (stackViews.Count < 2)
                    return;

                List<ViewPaperBox> boxes = new List<ViewPaperBox>();
                foreach (View view in stackViews)
                {
                    ViewPaperBox box;
                    if (TryGetViewGreenPaperBoxForUnknown(view, out box) &&
                        box.Width > 1.0 && box.Height > 1.0)
                        boxes.Add(box);
                }

                if (boxes.Count < 2)
                    return;

                boxes.Sort(delegate (ViewPaperBox a, ViewPaperBox b)
                {
                    return b.CenterY.CompareTo(a.CenterY);
                });

                double totalHeight = 0.0;
                double currentMinY = double.MaxValue;
                double currentMaxY = double.MinValue;
                foreach (ViewPaperBox box in boxes)
                {
                    totalHeight += box.Height;
                    if (box.MinY < currentMinY) currentMinY = box.MinY;
                    if (box.MaxY > currentMaxY) currentMaxY = box.MaxY;
                }

                double currentCenter = (currentMinY + currentMaxY) * 0.5;
                double totalStackHeight = totalHeight + gap * (boxes.Count - 1);
                double cursorMaxY = currentCenter + totalStackHeight * 0.5;

                foreach (ViewPaperBox box in boxes)
                {
                    double desiredCenterY =
                        (cursorMaxY + cursorMaxY - box.Height) * 0.5;
                    double deltaY = desiredCenterY -
                                    (box.MinY + box.MaxY) * 0.5;
                    if (Math.Abs(deltaY) > 300.0)
                        return;

                    TryMoveViewUnknown(box.View, 0.0, deltaY);
                    cursorMaxY -= box.Height + gap;
                }
            }
            catch
            {
            }
        }

        private static void UpdateDrawingTitle3ScaleUnknown(
            Drawing drawing,
            View referenceView)
        {
            try
            {
                if (drawing == null)
                    return;

                double scale = GetCurrentDrawingScaleUnknown(referenceView);
                if (scale <= 0.0)
                    return;

                string scaleText = "1:" +
                    Convert.ToInt32(Math.Round(scale)).ToString();
                bool changed = false;
                object attributes = TryGetObjectPropertyUnknown(drawing, "Attributes");
                changed = SetTitle3TextUnknown(attributes, scaleText) || changed;
                changed = SetTitle3TextUnknown(drawing, scaleText) || changed;

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

        private static bool SetTitle3TextUnknown(object obj, string scaleText)
        {
            bool changed = false;
            try
            {
                if (obj == null || string.IsNullOrEmpty(scaleText))
                    return false;

                PropertyInfo[] properties = obj.GetType().GetProperties(
                    BindingFlags.Public | BindingFlags.Instance);
                foreach (PropertyInfo property in properties)
                {
                    if (property == null || !property.CanWrite ||
                        property.PropertyType != typeof(string))
                        continue;

                    string name = property.Name.ToUpperInvariant();
                    if (name.IndexOf("TITLE", StringComparison.Ordinal) < 0 ||
                        name.IndexOf("3", StringComparison.Ordinal) < 0)
                        continue;

                    property.SetValue(obj, scaleText, null);
                    changed = true;
                }
            }
            catch
            {
            }

            return changed;
        }

        private static void SelectViewsUnknown(List<View> views)
        {
            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();
                DrawingObjectSelector selector =
                    drawingHandler.GetDrawingObjectSelector();
                DrawingObjectEnumerator.AutoFetch = true;
                ArrayList selected = new ArrayList();

                if (views != null)
                {
                    foreach (View view in views)
                    {
                        if (view != null)
                            selected.Add(view);
                    }
                }

                selector.SelectObjects(selected, false);
            }
            catch
            {
            }
        }

        private static void CommitAndWait(Drawing drawing, int milliseconds)
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
                System.Threading.Thread.Sleep(milliseconds);
            }
            catch
            {
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
