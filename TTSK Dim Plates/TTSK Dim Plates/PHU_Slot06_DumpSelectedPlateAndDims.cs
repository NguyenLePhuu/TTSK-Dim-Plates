#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Point = Tekla.Structures.Geometry3d.Point;
using Size = System.Drawing.Size;
using View = Tekla.Structures.Drawing.View;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 06: DIM SPACING live audit.
    // READ ONLY: this file never calls Modify, CommitChanges, Delete, Insert or LoadAttributes.
    public class PHU_AutoDimSlot06
    {
        public static void Run()
        {
            PHU_DimSpacingLiveAudit.Run();
        }
    }

    internal static class PHU_DimSpacingLiveAudit
    {
        private const double AuditSpacing = 50.0;
        private const double EdgeTolerance = 1.0;
        private const double InternalNearEdgeLimit = 50.0;

        private sealed class Vec2
        {
            public double X;
            public double Y;

            public Vec2()
            {
            }

            public Vec2(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double Length
            {
                get { return Math.Sqrt(X * X + Y * Y); }
            }

            public bool Normalize()
            {
                double length = Length;
                if (length < 0.000001 || Double.IsNaN(length) || Double.IsInfinity(length))
                    return false;

                X /= length;
                Y /= length;
                return true;
            }
        }

        private sealed class ViewInfo
        {
            public View View;
            public int Index;
            public string Description;
            public double MinX = Double.PositiveInfinity;
            public double MaxX = Double.NegativeInfinity;
            public double MinY = Double.PositiveInfinity;
            public double MaxY = Double.NegativeInfinity;

            public bool HasBounds
            {
                get
                {
                    return IsFinite(MinX) && IsFinite(MaxX) &&
                           IsFinite(MinY) && IsFinite(MaxY) &&
                           MaxX > MinX + 0.001 && MaxY > MinY + 0.001;
                }
            }

            public void AddPoints(List<Point> points)
            {
                if (points == null)
                    return;

                foreach (Point point in points)
                {
                    if (point == null)
                        continue;

                    MinX = Math.Min(MinX, point.X);
                    MaxX = Math.Max(MaxX, point.X);
                    MinY = Math.Min(MinY, point.Y);
                    MaxY = Math.Max(MaxY, point.Y);
                }
            }
        }

        private sealed class DimInfo
        {
            public object DimSet;
            public ViewInfo ViewInfo;
            public int CollectionIndex;
            public int RuntimeId;
            public double Distance;
            public Vec2 RawOffset;
            public Vec2 VisualOffset;
            public string Kind;
            public string Side;
            public double RoundedAngle;
            public string GroupKey;
            public bool IsInternalAtAuditSpacing;
            public List<Point> Points;
            public Point FirstPoint;
            public double VisualLineLevel;
        }

        public static void Run()
        {
            try
            {
                DrawingHandler handler = new DrawingHandler();
                if (!handler.GetConnectionStatus())
                {
                    ShowLog(
                        "DIM SPACING LIVE AUDIT - READ ONLY\r\n\r\n" +
                        "Khong ket noi duoc Tekla DrawingHandler.");
                    return;
                }

                Drawing drawing = handler.GetActiveDrawing();
                if (drawing == null)
                {
                    ShowLog(
                        "DIM SPACING LIVE AUDIT - READ ONLY\r\n\r\n" +
                        "Khong co ban ve Tekla active. Mo Drawing Editor roi chay lai Slot 06.");
                    return;
                }

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                {
                    ShowLog(
                        "DIM SPACING LIVE AUDIT - READ ONLY\r\n\r\n" +
                        "Khong lay duoc Sheet cua ban ve active.");
                    return;
                }

                List<ViewInfo> views = ReadViews(sheet);
                List<DimInfo> dims = ReadStraightDimensionSets(views);
                MarkInternalCandidates(dims);
                BuildGroupKeys(dims);

                ShowLog(BuildReport(drawing, views, dims));
            }
            catch (Exception ex)
            {
                ShowLog(
                    "DIM SPACING LIVE AUDIT - READ ONLY\r\n\r\n" +
                    "Audit failed:\r\n" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static List<ViewInfo> ReadViews(ContainerView sheet)
        {
            List<ViewInfo> views = new List<ViewInfo>();
            DrawingObjectEnumerator enumerator = sheet.GetAllViews();
            int index = 0;

            while (enumerator.MoveNext())
            {
                View view = enumerator.Current as View;
                if (view == null)
                    continue;

                ViewInfo info = new ViewInfo();
                info.View = view;
                info.Index = ++index;
                info.Description = GetViewDescription(view);
                views.Add(info);
            }

            return views;
        }

        private static List<DimInfo> ReadStraightDimensionSets(List<ViewInfo> views)
        {
            List<DimInfo> result = new List<DimInfo>();
            Type straightDimensionSetType = Type.GetType(
                "Tekla.Structures.Drawing.StraightDimensionSet, Tekla.Structures.Drawing");

            if (straightDimensionSetType == null || views == null)
                return result;

            foreach (ViewInfo viewInfo in views)
            {
                if (viewInfo == null || viewInfo.View == null)
                    continue;

                try
                {
                    DrawingObjectEnumerator enumerator =
                        viewInfo.View.GetAllObjects(straightDimensionSetType);
                    int collectionIndex = 0;

                    while (enumerator.MoveNext())
                    {
                        object dimSet = enumerator.Current;
                        int currentIndex = collectionIndex++;
                        DimInfo info = CreateDimInfo(dimSet, viewInfo, currentIndex);
                        if (info == null)
                            continue;

                        result.Add(info);
                        viewInfo.AddPoints(info.Points);
                    }
                }
                catch
                {
                    // One unreadable view must not hide the readable DIM sets.
                }
            }

            return result;
        }

        private static DimInfo CreateDimInfo(
            object dimSet,
            ViewInfo viewInfo,
            int collectionIndex)
        {
            if (dimSet == null || viewInfo == null)
                return null;

            double distance;
            if (!TryGetDouble(dimSet, "Distance", out distance))
                return null;

            Vec2 rawOffset;
            if (!TryReadOffset(dimSet, out rawOffset))
                return null;

            if (!rawOffset.Normalize())
                return null;

            Vec2 visualOffset = new Vec2(rawOffset.X, rawOffset.Y);
            if (distance < 0.0)
            {
                visualOffset.X *= -1.0;
                visualOffset.Y *= -1.0;
            }
            if (!visualOffset.Normalize())
                return null;

            DimInfo info = new DimInfo();
            info.DimSet = dimSet;
            info.ViewInfo = viewInfo;
            info.CollectionIndex = collectionIndex;
            info.RuntimeId = RuntimeHelpers.GetHashCode(dimSet);
            info.Distance = distance;
            info.RawOffset = rawOffset;
            info.VisualOffset = visualOffset;
            info.Kind = GetKind(visualOffset);
            info.Side = GetSide(visualOffset, info.Kind);
            info.RoundedAngle = RoundAngle(GetAngleDegrees(visualOffset), 5.0);
            info.Points = ReadDimensionPoints(dimSet);
            info.FirstPoint = ReadFirstDimensionPoint(dimSet);

            if (info.FirstPoint == null && info.Points != null && info.Points.Count > 0)
                info.FirstPoint = info.Points[0];

            info.VisualLineLevel = GetVisualLineLevel(info);
            return info;
        }

        private static void MarkInternalCandidates(List<DimInfo> dims)
        {
            if (dims == null)
                return;

            foreach (DimInfo item in dims)
            {
                if (item != null)
                    item.IsInternalAtAuditSpacing = IsInternalAtAuditSpacing(item);
            }
        }

        private static void BuildGroupKeys(List<DimInfo> dims)
        {
            if (dims == null)
                return;

            foreach (DimInfo item in dims)
            {
                if (item == null || item.ViewInfo == null)
                    continue;

                string viewPrefix = "V" + item.ViewInfo.Index.ToString("00");
                if (item.IsInternalAtAuditSpacing)
                {
                    item.GroupKey = viewPrefix + "_INTERNAL_" + item.RuntimeId;
                }
                else if (item.Kind == "V")
                {
                    item.GroupKey = viewPrefix + "_VERT_OFF_" +
                        item.RoundedAngle.ToString("0") + "_SIDE_" + item.Side;
                }
                else if (item.Kind == "H")
                {
                    item.GroupKey = viewPrefix + "_HORIZ_OFF_" +
                        item.RoundedAngle.ToString("0") + "_SIDE_" + item.Side;
                }
                else
                {
                    item.GroupKey = viewPrefix + "_SLOPE_OFF_" +
                        item.RoundedAngle.ToString("0") + "_SIDE_" + item.Side;
                }
            }
        }

        private static string BuildReport(
            Drawing drawing,
            List<ViewInfo> views,
            List<DimInfo> dims)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("DIM SPACING LIVE AUDIT - READ ONLY");
            text.AppendLine("No Modify / CommitChanges / Delete / Insert is called by Slot 06.");
            text.AppendLine("Audit spacing for the internal-DIM check: " +
                AuditSpacing.ToString("0.0") + " mm.");
            text.AppendLine("Tekla Distance reference inspected: DimensionPoints[0].");
            text.AppendLine();
            text.AppendLine("Drawing: " + drawing.GetType().FullName);
            text.AppendLine("Views: " + (views == null ? 0 : views.Count));
            text.AppendLine("StraightDimensionSets read: " + (dims == null ? 0 : dims.Count));
            text.AppendLine();

            if (views != null)
            {
                foreach (ViewInfo view in views)
                {
                    text.Append("VIEW V").Append(view.Index.ToString("00"))
                        .Append(": ").Append(view.Description ?? "<unknown>")
                        .Append(" | point bounds: ");

                    if (view.HasBounds)
                    {
                        text.Append("X[").Append(Format(view.MinX)).Append(", ")
                            .Append(Format(view.MaxX)).Append("] Y[")
                            .Append(Format(view.MinY)).Append(", ")
                            .Append(Format(view.MaxY)).Append("]");
                    }
                    else
                    {
                        text.Append("<not available>");
                    }

                    text.AppendLine();
                }
            }

            Dictionary<string, List<DimInfo>> groups = MakeGroups(dims);
            List<string> groupKeys = new List<string>(groups.Keys);
            groupKeys.Sort(StringComparer.Ordinal);

            text.AppendLine();
            text.AppendLine("GROUP SUMMARY (this matches PHU_DimSpacing grouping):");
            foreach (string key in groupKeys)
            {
                List<DimInfo> group = groups[key];
                SortByCurrentVisualTier(group);

                text.Append("- ").Append(key)
                    .Append(" | count=").Append(group.Count)
                    .Append(" | action=").Append(GetPredictedAction(group))
                    .AppendLine();

                for (int i = 0; i < group.Count; i++)
                {
                    DimInfo item = group[i];
                    text.Append("    tier ").Append(i + 1)
                        .Append(" | set#").Append(item.RuntimeId)
                        .Append(" | lineLevel=").Append(Format(item.VisualLineLevel))
                        .Append(" | distance=").Append(Format(item.Distance))
                        .Append(" | p0=").Append(FormatPoint(item.FirstPoint))
                        .AppendLine();
                }
            }

            AppendSplitWarnings(text, dims);

            text.AppendLine();
            text.AppendLine("FULL DIM DATA:");
            if (dims != null)
            {
                List<DimInfo> ordered = new List<DimInfo>(dims);
                ordered.Sort(delegate (DimInfo a, DimInfo b)
                {
                    int byView = a.ViewInfo.Index.CompareTo(b.ViewInfo.Index);
                    if (byView != 0)
                        return byView;
                    return a.CollectionIndex.CompareTo(b.CollectionIndex);
                });

                foreach (DimInfo item in ordered)
                {
                    text.Append("V").Append(item.ViewInfo.Index.ToString("00"))
                        .Append(" / collection#").Append(item.CollectionIndex)
                        .Append(" / set#").Append(item.RuntimeId).AppendLine();
                    text.Append("  group=").Append(item.GroupKey).AppendLine();
                    text.Append("  kind=").Append(item.Kind)
                        .Append(" side=").Append(item.Side)
                        .Append(" internal@50=").Append(item.IsInternalAtAuditSpacing)
                        .Append(" points=").Append(item.Points == null ? 0 : item.Points.Count)
                        .AppendLine();
                    text.Append("  Distance=").Append(Format(item.Distance))
                        .Append(" raw UpDirection=").Append(FormatVector(item.RawOffset))
                        .Append(" visual direction=").Append(FormatVector(item.VisualOffset))
                        .Append(" visual angle=").Append(item.RoundedAngle.ToString("0"))
                        .AppendLine();
                    text.Append("  DimensionPoints[0]=").Append(FormatPoint(item.FirstPoint))
                        .Append(" visualLineLevel=").Append(Format(item.VisualLineLevel))
                        .AppendLine();
                }
            }

            text.AppendLine();
            text.AppendLine("SEND BACK:");
            text.AppendLine("1. Click Copy all.");
            text.AppendLine("2. Paste the entire report into Codex.");
            text.AppendLine("3. Do not run DIM SPACING again before the report is reviewed.");
            return text.ToString();
        }

        private static Dictionary<string, List<DimInfo>> MakeGroups(List<DimInfo> dims)
        {
            Dictionary<string, List<DimInfo>> groups =
                new Dictionary<string, List<DimInfo>>(StringComparer.Ordinal);

            if (dims == null)
                return groups;

            foreach (DimInfo item in dims)
            {
                if (item == null || String.IsNullOrEmpty(item.GroupKey))
                    continue;

                List<DimInfo> group;
                if (!groups.TryGetValue(item.GroupKey, out group))
                {
                    group = new List<DimInfo>();
                    groups.Add(item.GroupKey, group);
                }

                group.Add(item);
            }

            return groups;
        }

        private static void SortByCurrentVisualTier(List<DimInfo> group)
        {
            if (group == null)
                return;

            group.Sort(delegate (DimInfo a, DimInfo b)
            {
                bool aBad = !IsFinite(a.VisualLineLevel);
                bool bBad = !IsFinite(b.VisualLineLevel);
                if (aBad && bBad)
                    return a.RuntimeId.CompareTo(b.RuntimeId);
                if (aBad)
                    return 1;
                if (bBad)
                    return -1;

                double delta = a.VisualLineLevel - b.VisualLineLevel;
                if (Math.Abs(delta) > 0.01)
                    return delta < 0.0 ? -1 : 1;

                return a.RuntimeId.CompareTo(b.RuntimeId);
            });
        }

        private static string GetPredictedAction(List<DimInfo> group)
        {
            if (group == null || group.Count == 0)
                return "SKIP";

            if (group[0].IsInternalAtAuditSpacing)
                return "INTERNAL: own Distance would become signed 50";

            if (group.Count == 1)
                return "EXTERNAL SINGLE: current code enters anchor branch";

            return "EXTERNAL MULTI: preserve current tier 1; tiers 2..n are +50 outward";
        }

        private static void AppendSplitWarnings(StringBuilder text, List<DimInfo> dims)
        {
            Dictionary<string, List<string>> candidateGroups =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (dims != null)
            {
                foreach (DimInfo item in dims)
                {
                    if (item == null || item.IsInternalAtAuditSpacing)
                        continue;

                    string candidateKey = "V" + item.ViewInfo.Index.ToString("00") +
                        "_" + item.Kind + "_" + item.Side;
                    List<string> keys;
                    if (!candidateGroups.TryGetValue(candidateKey, out keys))
                    {
                        keys = new List<string>();
                        candidateGroups.Add(candidateKey, keys);
                    }

                    if (!keys.Contains(item.GroupKey))
                        keys.Add(item.GroupKey);
                }
            }

            text.AppendLine();
            text.AppendLine("POTENTIAL GROUP-SPLIT WARNINGS:");
            bool foundWarning = false;
            foreach (KeyValuePair<string, List<string>> pair in candidateGroups)
            {
                if (pair.Value.Count <= 1)
                    continue;

                foundWarning = true;
                text.Append("- ").Append(pair.Key)
                    .Append(" is split into ").Append(pair.Value.Count)
                    .Append(" group keys: ").Append(string.Join(" | ", pair.Value.ToArray()))
                    .AppendLine();
            }

            if (!foundWarning)
                text.AppendLine("- None.");
        }

        private static bool IsInternalAtAuditSpacing(DimInfo item)
        {
            if (item == null || item.ViewInfo == null || !item.ViewInfo.HasBounds ||
                item.Points == null || item.Points.Count < 2)
                return false;

            ViewInfo box = item.ViewInfo;
            if (item.FirstPoint == null || item.RawOffset == null)
                return false;

            double lineX = item.FirstPoint.X + item.Distance * item.RawOffset.X;
            double lineY = item.FirstPoint.Y + item.Distance * item.RawOffset.Y;

            if (item.Kind == "H" &&
                (lineY <= box.MinY + EdgeTolerance || lineY >= box.MaxY - EdgeTolerance))
                return false;

            if (item.Kind == "V" &&
                (lineX <= box.MinX + EdgeTolerance || lineX >= box.MaxX - EdgeTolerance))
                return false;

            foreach (Point point in item.Points)
            {
                if (point == null)
                    return false;

                if (item.Kind == "H")
                {
                    if (point.Y <= box.MinY + EdgeTolerance || point.Y >= box.MaxY - EdgeTolerance ||
                        point.Y <= box.MinY + InternalNearEdgeLimit ||
                        point.Y >= box.MaxY - InternalNearEdgeLimit)
                        return false;
                }
                else if (item.Kind == "V")
                {
                    if (point.X <= box.MinX + EdgeTolerance || point.X >= box.MaxX - EdgeTolerance ||
                        point.X <= box.MinX + InternalNearEdgeLimit ||
                        point.X >= box.MaxX - InternalNearEdgeLimit)
                        return false;
                }
                else
                {
                    return false;
                }
            }

            return item.Kind == "H" || item.Kind == "V";
        }

        private static double GetVisualLineLevel(DimInfo item)
        {
            if (item == null || item.FirstPoint == null || item.RawOffset == null ||
                item.VisualOffset == null)
                return Double.NaN;

            double anchor = item.FirstPoint.X * item.VisualOffset.X +
                            item.FirstPoint.Y * item.VisualOffset.Y;
            double dot = item.RawOffset.X * item.VisualOffset.X +
                         item.RawOffset.Y * item.VisualOffset.Y;
            double level = anchor + item.Distance * dot;
            return IsFinite(level) ? level : Double.NaN;
        }

        private static bool TryReadOffset(object dimSet, out Vec2 offset)
        {
            offset = null;
            object value = GetPropertyOrField(dimSet, "UpDirection") ??
                           GetPropertyOrField(dimSet, "OffsetDirection");
            if (value == null)
                return false;

            double x;
            double y;
            if (!TryReadCoordinate(value, "X", out x) || !TryReadCoordinate(value, "Y", out y))
                return false;

            offset = new Vec2(x, y);
            return true;
        }

        private static List<Point> ReadDimensionPoints(object dimSet)
        {
            object raw = GetPropertyOrField(dimSet, "DimensionPoints");
            List<Point> points = new List<Point>();
            if (raw == null)
                return points;

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                foreach (object value in enumerable)
                {
                    Point point = value as Point;
                    if (point != null)
                        points.Add(point);
                }
            }

            if (points.Count > 0)
                return points;

            IList list = raw as IList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Point point = list[i] as Point;
                    if (point != null)
                        points.Add(point);
                }
            }

            return points;
        }

        private static Point ReadFirstDimensionPoint(object dimSet)
        {
            object raw = GetPropertyOrField(dimSet, "DimensionPoints");
            if (raw == null)
                return null;

            IList list = raw as IList;
            if (list != null && list.Count > 0)
                return list[0] as Point;

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                IEnumerator iterator = enumerable.GetEnumerator();
                if (iterator != null && iterator.MoveNext())
                    return iterator.Current as Point;
            }

            return null;
        }

        private static object GetPropertyOrField(object value, string memberName)
        {
            if (value == null || String.IsNullOrEmpty(memberName))
                return null;

            try
            {
                Type type = value.GetType();
                PropertyInfo property = type.GetProperty(
                    memberName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(value, null);

                FieldInfo field = type.GetField(
                    memberName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field == null ? null : field.GetValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetDouble(object value, string memberName, out double number)
        {
            number = Double.NaN;
            object raw = GetPropertyOrField(value, memberName);
            if (raw == null)
                return false;

            try
            {
                number = Convert.ToDouble(raw);
                return IsFinite(number);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadCoordinate(object value, string memberName, out double coordinate)
        {
            coordinate = Double.NaN;
            object raw = GetPropertyOrField(value, memberName);
            if (raw == null)
                return false;

            try
            {
                coordinate = Convert.ToDouble(raw);
                return IsFinite(coordinate);
            }
            catch
            {
                return false;
            }
        }

        private static string GetViewDescription(View view)
        {
            if (view == null)
                return "<null>";

            object viewType = GetPropertyOrField(view, "ViewType");
            string typeText = viewType == null ? view.GetType().Name : viewType.ToString();
            return typeText + " (runtime#" + RuntimeHelpers.GetHashCode(view) + ")";
        }

        private static string GetKind(Vec2 offset)
        {
            double ax = Math.Abs(offset.X);
            double ay = Math.Abs(offset.Y);
            if (ay >= ax * 2.0)
                return "H";
            if (ax >= ay * 2.0)
                return "V";
            return "S";
        }

        private static string GetSide(Vec2 offset, string kind)
        {
            if (kind == "H")
                return offset.Y >= 0.0 ? "TOP" : "BOTTOM";
            if (kind == "V")
                return offset.X >= 0.0 ? "RIGHT" : "LEFT";

            double angle = GetAngleDegrees(offset);
            if (angle >= 45.0 && angle < 135.0)
                return "SLOPE_TOP";
            if (angle >= 135.0 && angle < 225.0)
                return "SLOPE_LEFT";
            if (angle >= 225.0 && angle < 315.0)
                return "SLOPE_BOTTOM";
            return "SLOPE_RIGHT";
        }

        private static double GetAngleDegrees(Vec2 value)
        {
            double angle = Math.Atan2(value.Y, value.X) * 180.0 / Math.PI;
            if (angle < 0.0)
                angle += 360.0;
            return angle;
        }

        private static double RoundAngle(double angle, double increment)
        {
            double rounded = Math.Round(angle / increment) * increment;
            if (rounded >= 360.0)
                rounded -= 360.0;
            return rounded;
        }

        private static string Format(double value)
        {
            return IsFinite(value) ? value.ToString("0.###") : "NA";
        }

        private static string FormatPoint(Point point)
        {
            return point == null ? "NA" : "(" + Format(point.X) + ", " + Format(point.Y) + ")";
        }

        private static string FormatVector(Vec2 value)
        {
            return value == null ? "NA" : "(" + Format(value.X) + ", " + Format(value.Y) + ")";
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static void ShowLog(string text)
        {
            using (Form dialog = new Form())
            using (TextBox output = new TextBox())
            using (Button copy = new Button())
            using (Button close = new Button())
            using (Label note = new Label())
            {
                dialog.Text = "DIM SPACING LIVE AUDIT - READ ONLY";
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.Size = new Size(1120, 760);
                dialog.MinimumSize = new Size(780, 520);
                dialog.ShowInTaskbar = true;

                output.ReadOnly = true;
                output.Multiline = true;
                output.WordWrap = false;
                output.ScrollBars = ScrollBars.Both;
                output.Font = new Font("Consolas", 9.0F, FontStyle.Regular);
                output.Text = text ?? String.Empty;
                output.Dock = DockStyle.Fill;

                copy.Text = "Copy all";
                copy.Width = 110;
                copy.Height = 32;
                copy.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                copy.Click += delegate
                {
                    try
                    {
                        Clipboard.SetText(output.Text ?? String.Empty);
                        copy.Text = "Copied";
                    }
                    catch
                    {
                        copy.Text = "Copy failed";
                    }
                };

                close.Text = "Close";
                close.Width = 90;
                close.Height = 32;
                close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                close.DialogResult = DialogResult.OK;

                note.Text = "Read-only audit. Copy all and paste the report into Codex.";
                note.AutoSize = false;
                note.TextAlign = ContentAlignment.MiddleLeft;
                note.Dock = DockStyle.Fill;

                TableLayoutPanel footer = new TableLayoutPanel();
                footer.Dock = DockStyle.Bottom;
                footer.Height = 44;
                footer.Padding = new Padding(8, 6, 8, 6);
                footer.ColumnCount = 3;
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0F));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120.0F));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100.0F));
                footer.Controls.Add(note, 0, 0);
                footer.Controls.Add(copy, 1, 0);
                footer.Controls.Add(close, 2, 0);

                dialog.Controls.Add(output);
                dialog.Controls.Add(footer);
                dialog.AcceptButton = close;
                dialog.ShowDialog();
            }
        }
    }
}
