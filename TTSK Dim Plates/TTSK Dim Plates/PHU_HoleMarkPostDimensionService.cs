#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;
using ModelPart = Tekla.Structures.Model.Part;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// MainForm-owned final pass for Hole Marks. Shape drawings are handled
    /// entirely here and only on FrontView. Plate keeps its proven legacy
    /// planner behind this service boundary.
    /// </summary>
    public static class PHU_HoleMarkPostDimensionService
    {
        private const double BoundaryGapPaper = 2.0;
        private const double SearchStepPaper = 2.0;
        private const double MaxSearchPaper = 40.0;
        private const double CollisionGapPaper = 1.5;
        private const double AnchorMatchPaper = 4.0;

        private sealed class Rect
        {
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private sealed class Segment
        {
            public Point A;
            public Point B;
            public bool IsDimensionLine;
        }

        private sealed class Item
        {
            public Mark Mark;
            public Point OriginalAnchor;
            public Point TargetAnchor;
            public Point OriginalCenter;
            public Point TargetCenter;
            public double Width;
            public double Height;
            public List<Point> Group = new List<Point>();
        }

        public static bool Run(
            Model model,
            Drawing drawing,
            ModelPart part,
            bool shapeFrontViewOnly,
            out string message
        )
        {
            if (!shapeFrontViewOnly)
            {
                return Script.RunPlateHoleMarkPostDimensionPass(
                    model,
                    drawing,
                    part,
                    out message
                );
            }

            return RunShapeFront(model, drawing, part, out message);
        }

        internal static int RunWorker(string[] args)
        {
            string message = "";
            string resultPath = args != null && args.Length >= 4 ? args[3] : "";
            try
            {
                if (
                    args == null
                    || args.Length < 3
                    || !String.Equals(
                        args[0],
                        "--hole-mark-post-dim-worker",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return 2;

                bool shapeFrontOnly = String.Equals(
                    args[1],
                    "shape",
                    StringComparison.OrdinalIgnoreCase
                );
                int expectedPartId;
                if (!Int32.TryParse(args[2], out expectedPartId) || expectedPartId <= 0)
                    return 3;

                Model model = new Model();
                DrawingHandler handler = new DrawingHandler();
                Drawing drawing = handler.GetActiveDrawing();
                ModelPart part = ResolveWorkerMainPart(model, drawing);
                if (
                    !model.GetConnectionStatus()
                    || !handler.GetConnectionStatus()
                    || drawing == null
                    || part == null
                    || part.Identifier == null
                    || part.Identifier.ID != expectedPartId
                )
                {
                    message = "Worker active Drawing/MainPart identity changed.";
                    WriteWorkerResult(resultPath, message);
                    return 4;
                }

                bool succeeded = Run(
                    model,
                    drawing,
                    part,
                    shapeFrontOnly,
                    out message
                );
                WriteWorkerResult(resultPath, message);
                return succeeded ? 0 : 5;
            }
            catch (Exception ex)
            {
                message = ex.GetType().Name + ": " + ex.Message;
                WriteWorkerResult(resultPath, message);
                return 6;
            }
        }

        private static ModelPart ResolveWorkerMainPart(Model model, Drawing drawing)
        {
            if (model == null || drawing == null)
                return null;
            SinglePartDrawing single = drawing as SinglePartDrawing;
            if (single != null)
                return model.SelectModelObject(single.PartIdentifier) as ModelPart;
            AssemblyDrawing assemblyDrawing = drawing as AssemblyDrawing;
            if (assemblyDrawing != null)
            {
                Tekla.Structures.Model.Assembly assembly = model.SelectModelObject(
                    assemblyDrawing.AssemblyIdentifier
                ) as Tekla.Structures.Model.Assembly;
                return assembly == null ? null : assembly.GetMainPart() as ModelPart;
            }
            return null;
        }

        private static void WriteWorkerResult(string path, string message)
        {
            if (String.IsNullOrWhiteSpace(path))
                return;
            try { File.WriteAllText(path, message ?? ""); }
            catch { }
        }

        private static bool RunShapeFront(
            Model model,
            Drawing drawing,
            ModelPart part,
            out string message
        )
        {
            message = "";
            if (
                model == null
                || !model.GetConnectionStatus()
                || drawing == null
                || part == null
                || part.Identifier == null
            )
            {
                message = "Shape Hole Mark post-DIM thieu Model, Drawing hoac MainPart.";
                return false;
            }

            View front;
            if (!TryResolveSingleFrontView(drawing, part.Identifier, out front, out message))
                return false;

            TransformationPlane oldPlane = null;
            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(front.DisplayCoordinateSystem)
                );

                ModelPart viewPart = model.SelectModelObject(part.Identifier) as ModelPart;
                if (viewPart == null)
                {
                    message = "Khong reselect duoc MainPart trong FrontView.";
                    return false;
                }

                Solid solid = viewPart.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                {
                    message = "Khong doc duoc Solid MainPart trong FrontView.";
                    return false;
                }

                Rect partBox = MakeRect(solid.MinimumPoint, solid.MaximumPoint);
                double scale = GetViewScale(front);
                if (scale <= 0.0)
                {
                    message = "Khong doc duoc scale FrontView.";
                    return false;
                }

                List<List<Point>> groups = ReadBoltGroups(viewPart, partBox);
                List<Mark> allMarks;
                List<Item> items = ReadHoleMarkItems(front, groups, scale, out allMarks);
                if (items.Count == 0)
                {
                    message = "FrontView khong co Hole Mark hop le de move.";
                    return true;
                }

                items.Sort(delegate(Item first, Item second)
                {
                    return first.OriginalAnchor.X.CompareTo(second.OriginalAnchor.X);
                });

                Item left = items[0];
                Item right = items[items.Count - 1];
                left.TargetAnchor = SelectGroupEdge(left, true);
                right.TargetAnchor = SelectGroupEdge(right, false);
                if (left.TargetAnchor == null || right.TargetAnchor == null)
                {
                    message = "Khong xac dinh duoc lo gan mep trai/phai.";
                    return false;
                }

                List<Segment> dimensionSegments = ReadDimensionSegments(front, scale);
                Point guideLeft;
                Point guideRight;
                int verticalSign;
                bool hasGuide = TryResolveNearestHorizontalGuide(
                    dimensionSegments,
                    partBox,
                    scale,
                    out guideLeft,
                    out guideRight,
                    out verticalSign
                );

                List<Rect> fixedBoxes = ReadFixedMarkBoxes(allMarks, items);
                List<Rect> plannedBoxes = new List<Rect>();
                double boundaryGap = BoundaryGapPaper * scale;
                double step = Math.Max(1.0, SearchStepPaper * scale);
                double maxSearch = MaxSearchPaper * scale;

                for (int index = 0; index < items.Count; index++)
                {
                    Item item = items[index];
                    if (item.TargetAnchor == null)
                        item.TargetAnchor = Clone(item.OriginalAnchor);

                    bool isLeft = System.Object.ReferenceEquals(item, left);
                    bool isRight = System.Object.ReferenceEquals(item, right);
                    Point baseCenter = hasGuide
                        ? BuildGuideCenter(
                            item,
                            isLeft,
                            isRight,
                            guideLeft,
                            guideRight,
                            verticalSign,
                            boundaryGap,
                            partBox
                        )
                        : BuildBottomCenter(
                            item,
                            isLeft,
                            isRight,
                            partBox,
                            boundaryGap
                        );

                    Point accepted = FindSafeCenter(
                        item,
                        baseCenter,
                        hasGuide ? verticalSign : -1,
                        step,
                        maxSearch,
                        partBox,
                        boundaryGap,
                        dimensionSegments,
                        CollisionGapPaper * scale,
                        fixedBoxes,
                        plannedBoxes
                    );
                    if (accepted == null)
                    {
                        message =
                            "Khong tim duoc vi tri an toan cho Hole Mark anchor X="
                            + item.OriginalAnchor.X.ToString("0.###")
                            + ".";
                        return false;
                    }

                    item.TargetCenter = accepted;
                    plannedBoxes.Add(MakeCenteredRect(accepted, item.Width, item.Height));
                }

                if (!ApplyAndVerify(drawing, items, out message))
                    return false;

                message =
                    "MainForm final Hole Mark pass: Shape FrontView da move "
                    + items.Count.ToString()
                    + " mark; guide="
                    + (hasGuide ? "nearest horizontal DIM endpoint" : "bottom fallback")
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                Exception current = ex;
                List<string> errors = new List<string>();
                while (current != null && errors.Count < 6)
                {
                    if (!String.IsNullOrWhiteSpace(current.Message))
                        errors.Add(current.GetType().Name + ": " + current.Message);
                    current = current.InnerException;
                }
                message = "Shape Hole Mark post-DIM loi: " + String.Join(" -> ", errors);
                return false;
            }
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

        private static bool TryResolveSingleFrontView(
            Drawing drawing,
            Identifier partId,
            out View front,
            out string message
        )
        {
            front = null;
            message = "";
            int count = 0;
            DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
            while (views != null && views.MoveNext())
            {
                View view = views.Current as View;
                if (
                    view == null
                    || !String.Equals(
                        view.ViewType.ToString(),
                        "FrontView",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || !ViewContainsPart(view, partId)
                )
                    continue;
                count++;
                front = view;
            }

            if (count != 1)
            {
                message =
                    "Shape Hole Mark post-DIM yeu cau dung mot FrontView, tim thay "
                    + count.ToString()
                    + ".";
                front = null;
                return false;
            }
            return true;
        }

        private static bool ViewContainsPart(View view, Identifier partId)
        {
            DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
            while (parts != null && parts.MoveNext())
            {
                DrawingPart drawingPart = parts.Current as DrawingPart;
                if (
                    drawingPart != null
                    && drawingPart.ModelIdentifier != null
                    && drawingPart.ModelIdentifier.ID == partId.ID
                )
                    return true;
            }
            return false;
        }

        private static List<List<Point>> ReadBoltGroups(ModelPart part, Rect partBox)
        {
            List<List<Point>> result = new List<List<Point>>();
            ModelObjectEnumerator bolts = part.GetBolts();
            while (bolts != null && bolts.MoveNext())
            {
                ModelBoltGroup group = bolts.Current as ModelBoltGroup;
                if (group == null || group.BoltPositions == null)
                    continue;
                List<Point> points = new List<Point>();
                foreach (object value in group.BoltPositions)
                {
                    Point point = value as Point;
                    if (
                        point == null
                        || point.X < partBox.MinX - 5.0
                        || point.X > partBox.MaxX + 5.0
                        || point.Y < partBox.MinY - 5.0
                        || point.Y > partBox.MaxY + 5.0
                    )
                        continue;
                    AddUnique(points, new Point(point.X, point.Y, 0), 1.0);
                }
                if (points.Count > 0)
                    result.Add(points);
            }
            return result;
        }

        private static List<Item> ReadHoleMarkItems(
            View front,
            List<List<Point>> groups,
            double scale,
            out List<Mark> allMarks
        )
        {
            allMarks = new List<Mark>();
            List<Item> result = new List<Item>();
            DrawingObjectEnumerator objects = front.GetAllObjects();
            while (objects != null && objects.MoveNext())
            {
                Mark mark = objects.Current as Mark;
                if (mark == null)
                    continue;
                allMarks.Add(mark);
                if (!IsHoleMark(mark))
                    continue;

                try
                {
                    mark.Select();
                    LeaderLinePlacing placing = mark.Placing as LeaderLinePlacing;
                    Point min;
                    Point max;
                    if (
                        placing == null
                        || placing.StartPoint == null
                        || !TryGetBox(mark, out min, out max)
                    )
                        continue;

                    Item item = new Item();
                    item.Mark = mark;
                    item.OriginalAnchor = Clone(placing.StartPoint);
                    item.TargetAnchor = Clone(item.OriginalAnchor);
                    item.OriginalCenter = new Point(
                        (min.X + max.X) * 0.5,
                        (min.Y + max.Y) * 0.5,
                        0
                    );
                    item.Width = Math.Abs(max.X - min.X);
                    item.Height = Math.Abs(max.Y - min.Y);
                    item.Group = ResolveClosestGroup(
                        item.OriginalAnchor,
                        groups,
                        AnchorMatchPaper * scale
                    );
                    if (item.Group.Count == 0)
                        item.Group.Add(Clone(item.OriginalAnchor));
                    if (item.Width > 0.1 && item.Height > 0.1)
                        result.Add(item);
                }
                catch { }
            }
            return result;
        }

        private static List<Point> ResolveClosestGroup(
            Point anchor,
            List<List<Point>> groups,
            double tolerance
        )
        {
            List<Point> result = new List<Point>();
            List<Point> best = null;
            double bestDistance = 999999999.0;
            foreach (List<Point> group in groups)
            {
                double distance = 999999999.0;
                foreach (Point point in group)
                    distance = Math.Min(distance, Distance(anchor, point));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = group;
                }
            }
            if (best != null && bestDistance <= tolerance)
            {
                foreach (Point point in best)
                    result.Add(Clone(point));
            }
            return result;
        }

        private static Point SelectGroupEdge(Item item, bool left)
        {
            Point selected = null;
            foreach (Point point in item.Group)
            {
                if (
                    selected == null
                    || (left && point.X < selected.X)
                    || (!left && point.X > selected.X)
                )
                    selected = point;
            }
            return Clone(selected);
        }

        private static List<Segment> ReadDimensionSegments(View view, double scale)
        {
            List<Segment> result = new List<Segment>();
            DrawingObjectEnumerator objects = view.GetAllObjects();
            while (objects != null && objects.MoveNext())
            {
                StraightDimensionSet set = objects.Current as StraightDimensionSet;
                if (set == null)
                    continue;
                List<Point> points = ReadDimensionPoints(set);
                Vector up = ReadMember(set, "UpDirection") as Vector;
                object rawDistance = ReadMember(set, "Distance");
                if (points.Count < 2 || up == null || rawDistance == null)
                    continue;
                double distance;
                try { distance = Convert.ToDouble(rawDistance); }
                catch { continue; }
                double length = Math.Sqrt(up.X * up.X + up.Y * up.Y);
                if (length <= 0.0001)
                    continue;
                double nx = up.X / length;
                double ny = up.Y / length;
                double tx = -ny;
                double ty = nx;
                Point first = points[0];
                Point origin = new Point(
                    first.X + nx * distance,
                    first.Y + ny * distance,
                    0
                );
                double minProjection = 999999999.0;
                double maxProjection = -999999999.0;
                foreach (Point point in points)
                {
                    double projection =
                        (point.X - origin.X) * tx + (point.Y - origin.Y) * ty;
                    minProjection = Math.Min(minProjection, projection);
                    maxProjection = Math.Max(maxProjection, projection);
                    Point onLine = new Point(
                        origin.X + tx * projection,
                        origin.Y + ty * projection,
                        0
                    );
                    AddSegment(result, point, onLine, false);
                }
                double overrun = 2.0 * scale;
                AddSegment(
                    result,
                    new Point(
                        origin.X + tx * (minProjection - overrun),
                        origin.Y + ty * (minProjection - overrun),
                        0
                    ),
                    new Point(
                        origin.X + tx * (maxProjection + overrun),
                        origin.Y + ty * (maxProjection + overrun),
                        0
                    ),
                    true
                );
            }
            return result;
        }

        private static bool TryResolveNearestHorizontalGuide(
            List<Segment> segments,
            Rect partBox,
            double scale,
            out Point left,
            out Point right,
            out int verticalSign
        )
        {
            left = null;
            right = null;
            verticalSign = 0;
            double tolerance = Math.Max(0.5, 0.1 * scale);
            double requiredSpan = (partBox.MaxX - partBox.MinX) * 0.70;
            double nearest = 999999999.0;
            foreach (Segment segment in segments)
            {
                if (segment == null || !segment.IsDimensionLine)
                    continue;
                double spanX = Math.Abs(segment.B.X - segment.A.X);
                double spanY = Math.Abs(segment.B.Y - segment.A.Y);
                if (spanY > tolerance || spanX < requiredSpan)
                    continue;
                double y = (segment.A.Y + segment.B.Y) * 0.5;
                bool above = y > partBox.MaxY + tolerance;
                bool below = y < partBox.MinY - tolerance;
                if (!above && !below)
                    continue;
                double distance = above ? y - partBox.MaxY : partBox.MinY - y;
                if (distance >= nearest - 0.000001)
                    continue;
                nearest = distance;
                Point min = segment.A.X <= segment.B.X ? segment.A : segment.B;
                Point max = segment.A.X <= segment.B.X ? segment.B : segment.A;
                left = Clone(min);
                right = Clone(max);
                verticalSign = above ? 1 : -1;
            }
            return left != null && right != null && verticalSign != 0;
        }

        private static Point BuildGuideCenter(
            Item item,
            bool isLeft,
            bool isRight,
            Point guideLeft,
            Point guideRight,
            int verticalSign,
            double gap,
            Rect partBox
        )
        {
            // Endpoint DIM only controls left/right X. Vertical placement stays
            // close to the member: the mark box clears the nearest part edge by
            // exactly the configured paper-space gap.
            double y = verticalSign > 0
                ? partBox.MaxY + gap + item.Height * 0.5
                : partBox.MinY - gap - item.Height * 0.5;
            if (isLeft)
                return new Point(guideLeft.X - gap - item.Width * 0.5, y, 0);
            if (isRight)
                return new Point(guideRight.X + gap + item.Width * 0.5, y, 0);
            return new Point(item.TargetAnchor.X, y, 0);
        }

        private static Point BuildBottomCenter(
            Item item,
            bool isLeft,
            bool isRight,
            Rect partBox,
            double gap
        )
        {
            double x = item.TargetAnchor.X;
            if (isLeft)
                x += item.Width * 0.5;
            else if (isRight)
                x -= item.Width * 0.5;
            return new Point(x, partBox.MinY - gap - item.Height * 0.5, 0);
        }

        private static Point FindSafeCenter(
            Item item,
            Point baseCenter,
            int searchSign,
            double step,
            double maxSearch,
            Rect partBox,
            double boundaryGap,
            List<Segment> dimensions,
            double collisionGap,
            List<Rect> fixedBoxes,
            List<Rect> plannedBoxes
        )
        {
            for (double extra = 0.0; extra <= maxSearch + 0.001; extra += step)
            {
                Point center = new Point(
                    baseCenter.X,
                    baseCenter.Y + searchSign * extra,
                    0
                );
                Rect box = MakeCenteredRect(center, item.Width, item.Height);
                if (RectsOverlap(box, partBox, boundaryGap))
                    continue;
                if (AnySegmentHitsBox(dimensions, box, collisionGap))
                    continue;
                if (AnyRectOverlap(fixedBoxes, box, collisionGap))
                    continue;
                if (AnyRectOverlap(plannedBoxes, box, collisionGap))
                    continue;
                return center;
            }
            return null;
        }

        private static List<Rect> ReadFixedMarkBoxes(List<Mark> allMarks, List<Item> items)
        {
            List<Rect> result = new List<Rect>();
            HashSet<Mark> movable = new HashSet<Mark>();
            foreach (Item item in items)
                movable.Add(item.Mark);
            foreach (Mark mark in allMarks)
            {
                if (movable.Contains(mark))
                    continue;
                Point min;
                Point max;
                if (TryGetBox(mark, out min, out max))
                    result.Add(MakeRect(min, max));
            }
            return result;
        }

        private static bool ApplyAndVerify(
            Drawing drawing,
            List<Item> items,
            out string message
        )
        {
            message = "";
            foreach (Item item in items)
            {
                try
                {
                    Vector move = new Vector(
                        item.TargetCenter.X - item.OriginalCenter.X,
                        item.TargetCenter.Y - item.OriginalCenter.Y,
                        0
                    );
                    if (
                        (Math.Abs(move.X) > 0.01 || Math.Abs(move.Y) > 0.01)
                        && !TryMove(item.Mark, move)
                    )
                    {
                        Restore(items, drawing);
                        message = "MoveObjectRelative Hole Mark that bai.";
                        return false;
                    }
                    if (!TrySetAnchor(item.Mark, item.TargetAnchor) || !TryLimitedModify(item.Mark))
                    {
                        Restore(items, drawing);
                        message = "Modify Hole Mark that bai.";
                        return false;
                    }
                }
                catch
                {
                    Restore(items, drawing);
                    message = "Exception khi apply Hole Mark; da rollback.";
                    return false;
                }
            }

            if (!drawing.CommitChanges())
            {
                Restore(items, drawing);
                message = "Commit Hole Mark that bai; da rollback.";
                return false;
            }
            Thread.Sleep(80);

            foreach (Item item in items)
            {
                item.Mark.Select();
                Point min;
                Point max;
                LeaderLinePlacing placing = item.Mark.Placing as LeaderLinePlacing;
                if (
                    placing == null
                    || placing.StartPoint == null
                    || !TryGetBox(item.Mark, out min, out max)
                    || Distance(placing.StartPoint, item.TargetAnchor) > 0.5
                    || Distance(
                        new Point((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, 0),
                        item.TargetCenter
                    ) > 0.5
                )
                {
                    Restore(items, drawing);
                    message = "Read-back Hole Mark sai target; da rollback.";
                    return false;
                }
            }
            return true;
        }

        private static void Restore(List<Item> items, Drawing drawing)
        {
            foreach (Item item in items)
            {
                try
                {
                    item.Mark.Select();
                    Point min;
                    Point max;
                    if (TryGetBox(item.Mark, out min, out max))
                    {
                        Point center = new Point(
                            (min.X + max.X) * 0.5,
                            (min.Y + max.Y) * 0.5,
                            0
                        );
                        TryMove(
                            item.Mark,
                            new Vector(
                                item.OriginalCenter.X - center.X,
                                item.OriginalCenter.Y - center.Y,
                                0
                            )
                        );
                    }
                    TrySetAnchor(item.Mark, item.OriginalAnchor);
                    TryLimitedModify(item.Mark);
                }
                catch { }
            }
            try { drawing.CommitChanges(); }
            catch { }
        }

        private static bool IsHoleMark(Mark mark)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (mark != null && mark.Attributes != null)
                    CollectContentNames(mark.Attributes.Content, names, 0);
            }
            catch { }
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

        private static void CollectContentNames(
            object content,
            HashSet<string> names,
            int depth
        )
        {
            if (content == null || names == null || depth > 6)
                return;
            object name = ReadMember(content, "Name");
            if (name != null && !String.IsNullOrWhiteSpace(name.ToString()))
                names.Add(name.ToString().Trim().ToUpperInvariant());
            object child = ReadMember(content, "Content");
            if (child != null && !System.Object.ReferenceEquals(child, content))
                CollectContentNames(child, names, depth + 1);
            IEnumerable values = content as IEnumerable;
            if (values == null || content is string)
                return;
            foreach (object value in values)
                CollectContentNames(value, names, depth + 1);
        }

        private static List<Point> ReadDimensionPoints(object dimension)
        {
            List<Point> result = new List<Point>();
            IEnumerable values = ReadMember(dimension, "DimensionPoints") as IEnumerable;
            if (values == null)
                return result;
            foreach (object value in values)
            {
                Point point = value as Point;
                if (point != null)
                    result.Add(Clone(point));
            }
            return result;
        }

        private static object ReadMember(object owner, string name)
        {
            if (owner == null)
                return null;
            try
            {
                PropertyInfo property = owner.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (property != null && property.CanRead)
                    return property.GetValue(owner, null);
            }
            catch { }
            return null;
        }

        private static bool TryGetBox(DrawingObject obj, out Point min, out Point max)
        {
            min = null;
            max = null;
            try
            {
                MethodInfo method = obj.GetType().GetMethod(
                    "GetAxisAlignedBoundingBox",
                    BindingFlags.Public | BindingFlags.Instance
                );
                object box = method == null ? null : method.Invoke(obj, null);
                if (box == null)
                    return false;
                Point rawMin = ReadMember(box, "MinPoint") as Point;
                Point rawMax = ReadMember(box, "MaxPoint") as Point;
                if (rawMin == null || rawMax == null)
                    return false;
                min = Clone(rawMin);
                max = Clone(rawMax);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryMove(DrawingObject obj, Vector move)
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
            catch { return false; }
        }

        private static bool TrySetAnchor(Mark mark, Point anchor)
        {
            try
            {
                LeaderLinePlacing placing = mark.Placing as LeaderLinePlacing;
                if (placing == null || placing.StartPoint == null || anchor == null)
                    return false;
                placing.StartPoint.X = anchor.X;
                placing.StartPoint.Y = anchor.Y;
                placing.StartPoint.Z = 0;
                mark.Placing = placing;
                return true;
            }
            catch { return false; }
        }

        private static bool TryLimitedModify(Mark mark)
        {
            try
            {
                MethodInfo method = mark.GetType().GetMethod(
                    "LimitedModify",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                object value = method == null ? null : method.Invoke(mark, null);
                return value is bool && (bool)value;
            }
            catch { return false; }
        }

        private static double GetViewScale(View view)
        {
            try
            {
                return view != null && view.Attributes != null ? view.Attributes.Scale : 0.0;
            }
            catch { return 0.0; }
        }

        private static Rect MakeRect(Point first, Point second)
        {
            Rect result = new Rect();
            result.MinX = Math.Min(first.X, second.X);
            result.MaxX = Math.Max(first.X, second.X);
            result.MinY = Math.Min(first.Y, second.Y);
            result.MaxY = Math.Max(first.Y, second.Y);
            return result;
        }

        private static Rect MakeCenteredRect(Point center, double width, double height)
        {
            return MakeRect(
                new Point(center.X - width * 0.5, center.Y - height * 0.5, 0),
                new Point(center.X + width * 0.5, center.Y + height * 0.5, 0)
            );
        }

        private static bool RectsOverlap(Rect first, Rect second, double gap)
        {
            return first.MinX < second.MaxX + gap
                && first.MaxX > second.MinX - gap
                && first.MinY < second.MaxY + gap
                && first.MaxY > second.MinY - gap;
        }

        private static bool AnyRectOverlap(List<Rect> rects, Rect target, double gap)
        {
            foreach (Rect rect in rects)
            {
                if (RectsOverlap(rect, target, gap))
                    return true;
            }
            return false;
        }

        private static bool AnySegmentHitsBox(
            List<Segment> segments,
            Rect box,
            double gap
        )
        {
            foreach (Segment segment in segments)
            {
                if (SegmentIntersectsRect(segment.A, segment.B, box, gap))
                    return true;
            }
            return false;
        }

        private static bool SegmentIntersectsRect(
            Point a,
            Point b,
            Rect box,
            double gap
        )
        {
            Rect expanded = new Rect();
            expanded.MinX = box.MinX - gap;
            expanded.MaxX = box.MaxX + gap;
            expanded.MinY = box.MinY - gap;
            expanded.MaxY = box.MaxY + gap;
            if (PointInRect(a, expanded) || PointInRect(b, expanded))
                return true;
            Point bl = new Point(expanded.MinX, expanded.MinY, 0);
            Point br = new Point(expanded.MaxX, expanded.MinY, 0);
            Point tr = new Point(expanded.MaxX, expanded.MaxY, 0);
            Point tl = new Point(expanded.MinX, expanded.MaxY, 0);
            return SegmentsIntersect(a, b, bl, br)
                || SegmentsIntersect(a, b, br, tr)
                || SegmentsIntersect(a, b, tr, tl)
                || SegmentsIntersect(a, b, tl, bl);
        }

        private static bool PointInRect(Point point, Rect rect)
        {
            return point != null
                && point.X >= rect.MinX
                && point.X <= rect.MaxX
                && point.Y >= rect.MinY
                && point.Y <= rect.MaxY;
        }

        private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
        {
            double o1 = Orientation(a, b, c);
            double o2 = Orientation(a, b, d);
            double o3 = Orientation(c, d, a);
            double o4 = Orientation(c, d, b);
            return ((o1 > 0.0001 && o2 < -0.0001) || (o1 < -0.0001 && o2 > 0.0001))
                && ((o3 > 0.0001 && o4 < -0.0001) || (o3 < -0.0001 && o4 > 0.0001));
        }

        private static double Orientation(Point a, Point b, Point c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static void AddSegment(
            List<Segment> segments,
            Point a,
            Point b,
            bool dimensionLine
        )
        {
            if (a == null || b == null || Distance(a, b) <= 0.01)
                return;
            Segment segment = new Segment();
            segment.A = Clone(a);
            segment.B = Clone(b);
            segment.IsDimensionLine = dimensionLine;
            segments.Add(segment);
        }

        private static void AddUnique(List<Point> points, Point point, double tolerance)
        {
            foreach (Point existing in points)
            {
                if (Distance(existing, point) <= tolerance)
                    return;
            }
            points.Add(point);
        }

        private static Point Clone(Point point)
        {
            return point == null ? null : new Point(point.X, point.Y, point.Z);
        }

        private static double Distance(Point first, Point second)
        {
            if (first == null || second == null)
                return 999999999.0;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
