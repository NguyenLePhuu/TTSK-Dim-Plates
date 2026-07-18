// PHU_OpenGridView.cs
// Tool độc lập: mở RestrictionBox của View tới Grid gần nhất ở 4 hướng.
// Dùng cho MainForm gọi:
//     PHU_OpenGridView.Result result = PHU_OpenGridView.Run();
//
// Cách dùng:
// 1. Mở drawing.
// 2. Bật Grid thủ công trong View Attribute nếu cần.
// 3. Chọn 1 hoặc nhiều View.
// 4. Chạy tool.
// 5. Tool sẽ bung tạm RestrictionBox rất lớn để Tekla trả Drawing.Grid/GridLine,
//    sau đó thu lại tới Grid gần nhất ở 4 hướng.
//
// Lưu ý:
// - Tool này có Modify view.

#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using static Tekla.Structures.Drawing.Text;

public static class PHU_OpenGridView
{
    // ================= USER CONFIG =================

    // Bung lũy tiến để tránh lag:
    // mở ít trước, nếu chưa thấy đủ grid 4 hướng thì mới mở tiếp.
    private static readonly double[] TEMP_EXPAND_STEPS = new double[]
    { 2000.0, 5000.0,10000.0,20000.0, 35000.0 };

    // Cộng thêm để tránh grid / label / nét line bị hụt sát mép.
    private const double FINAL_EXTRA_MARGIN = 250.0;

    // Bù thêm cho biên visual của label/gridline.
    private const double VISUAL_EXTRA_MARGIN = 180.0;

    // Nếu một hướng không tìm thấy grid ngoài box ban đầu:
    // true = giữ cạnh cũ
    // false = dùng biên grid min/max bắt được.
    private const bool KEEP_OLD_SIDE_IF_NO_GRID_FOUND = true;

    // Chỉ đọc grid không bị hidden.
    private const bool IGNORE_HIDDEN_GRID = true;

    // Sai số để nhận line đứng/ngang.
    private const double LINE_AXIS_TOLERANCE = 1.0;

    // Lưu vị trí khung xanh trước khi Open Grid bung view.
    // Fit View sẽ dùng lại vị trí này để quay về đúng layout ban đầu.
    private static readonly Dictionary<string, ViewSheetBox> FIT_ORIGINAL_SHEET_BOXES = new Dictionary<string, ViewSheetBox>();

    // Lưu RestrictionBox gốc trước khi Open Grid bung view.
    // Fit View sẽ restore đúng RestrictionBox ban đầu này.
    private static readonly Dictionary<string, AABB> FIT_ORIGINAL_RESTRICTION_BOXES = new Dictionary<string, AABB>();

    // =================================================

    public class Result
    {
        public int ViewCount;
        public int SuccessCount;
        public int FailedCount;
        public string Message;

        public string ToDisplayText()
        {
            string s = "";
            s += "Open Grid View\n";
            s += "Views: " + ViewCount + "\n";
            s += "OK: " + SuccessCount + "\n";
            s += "Failed: " + FailedCount;
            if (!string.IsNullOrEmpty(Message))
                s += "\n" + Message;
            return s;
        }
    }

    private class GridSeg
    {
        public string Label;
        public double X1;
        public double Y1;
        public double X2;
        public double Y2;
        public bool IsVertical;
        public bool IsHorizontal;
        public double ConstX;
        public double ConstY;

        // Biên thật gồm GridPoint + GridLabelPoint + CenterPoint + frame/text margin.
        public double VisualMinX;
        public double VisualMaxX;
        public double VisualMinY;
        public double VisualMaxY;
    }

    public static Result Run()
    {
        Result result = new Result();

        try
        {

            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                result.FailedCount++;
                result.Message = "DrawingHandler chưa kết nối.";

                return result;
            }

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                result.FailedCount++;
                result.Message = "Không có active drawing.";

                return result;
            }

            List<View> views = GetTargetViews(dh, drawing);
            if (views.Count == 0)
            {
                result.FailedCount++;
                result.Message = "Không tìm thấy View để xử lý.";

                return result;
            }

            result.ViewCount = views.Count;

            // Lưu layout ban đầu trước khi Open Grid làm khung xanh thay đổi kích thước/vị trí.
            // Không dùng vị trí sau Open Grid làm chuẩn cho Fit View.
            CaptureFitOriginalSheetBoxes(views);

            foreach (View v in views)
            {
                try
                {
                    bool ok = ProcessView(v, drawing);
                    if (ok) result.SuccessCount++;
                    else result.FailedCount++;
                }
                catch (Exception exView)
                {
                    result.FailedCount++;
                }
            }

            if (views.Count > 1)
            {
                try { drawing.CommitChanges(); }
                catch (Exception exCommit) {; }

                KeepSelectedViewsNonOverlapping(views, 20.0);
            }

            try { drawing.CommitChanges(); }
            catch (Exception exCommit) {; }

            result.Message = "Done.";

            return result;
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Message = "Fatal error: " + ex.Message;

            return result;
        }
    }

    private static bool ProcessView(View view, Drawing drawing)
    {

        if (view == null)
        {
            return false;
        }

        AABB oldBox = view.RestrictionBox;
        if (oldBox == null || oldBox.MinPoint == null || oldBox.MaxPoint == null)
        {
            return false;
        }

        ViewSheetBox originalSheetBox;
        bool hasOriginalSheetBox = TryGetViewSheetBox(view, out originalSheetBox);

        double oldMinX = Math.Min(oldBox.MinPoint.X, oldBox.MaxPoint.X);
        double oldMaxX = Math.Max(oldBox.MinPoint.X, oldBox.MaxPoint.X);
        double oldMinY = Math.Min(oldBox.MinPoint.Y, oldBox.MaxPoint.Y);
        double oldMaxY = Math.Max(oldBox.MinPoint.Y, oldBox.MaxPoint.Y);
        double oldMinZ = Math.Min(oldBox.MinPoint.Z, oldBox.MaxPoint.Z);
        double oldMaxZ = Math.Max(oldBox.MinPoint.Z, oldBox.MaxPoint.Z);


        // PASS 1 + 2: bung lũy tiến, đọc grid, dừng ngay khi đủ grid 4 hướng.
        List<GridSeg> segs = null;
        double usedExpand = 0.0;

        bool enough = ProgressiveExpandUntilEnoughGrid(
            view,
            drawing,
            oldMinX, oldMaxX, oldMinY, oldMaxY, oldMinZ, oldMaxZ,
            out segs,
            out usedExpand);


        if (segs == null || segs.Count == 0)
        {
            view.RestrictionBox = oldBox;
            SafeModify(view, "restore old");
            return false;
        }

        bool hasLeft = false, hasRight = false, hasBottom = false, hasTop = false;
        double left = 0, right = 0, bottom = 0, top = 0;
        double leftLineX = 0, rightLineX = 0, bottomLineY = 0, topLineY = 0;

        double allMinX = double.PositiveInfinity;
        double allMaxX = double.NegativeInfinity;
        double allMinY = double.PositiveInfinity;
        double allMaxY = double.NegativeInfinity;

        // Grid da nam trong RestrictionBox cu van phai duoc tinh cho huong cua no.
        // Dung tam box lam moc chia 4 huong thay vi chi nhan grid nam ngoai 4 canh.
        double oldCenterX = (oldMinX + oldMaxX) * 0.5;
        double oldCenterY = (oldMinY + oldMaxY) * 0.5;

        foreach (GridSeg g in segs)
        {
            double gxMin = g.VisualMinX;
            double gxMax = g.VisualMaxX;
            double gyMin = g.VisualMinY;
            double gyMax = g.VisualMaxY;

            if (gxMin < allMinX) allMinX = gxMin;
            if (gxMax > allMaxX) allMaxX = gxMax;
            if (gyMin < allMinY) allMinY = gyMin;
            if (gyMax > allMaxY) allMaxY = gyMax;

            if (g.IsVertical)
            {
                double x = g.ConstX;

                // Mot grid gan dung tam khong duoc tinh dong thoi cho ca trai va phai.
                if (x < oldCenterX - LINE_AXIS_TOLERANCE)
                {
                    // Chọn gridline gần cạnh trái nhất, nhưng mở tới biên visual thật của nó.
                    if (!hasLeft || x > leftLineX)
                    {
                        leftLineX = x;
                        left = g.VisualMinX;
                        hasLeft = true;
                    }
                }

                if (x > oldCenterX + LINE_AXIS_TOLERANCE)
                {
                    // Chọn gridline gần cạnh phải nhất, nhưng mở tới biên visual thật của nó.
                    if (!hasRight || x < rightLineX)
                    {
                        rightLineX = x;
                        right = g.VisualMaxX;
                        hasRight = true;
                    }
                }
            }

            if (g.IsHorizontal)
            {
                double y = g.ConstY;

                // Mot grid gan dung tam khong duoc tinh dong thoi cho ca duoi va tren.
                if (y < oldCenterY - LINE_AXIS_TOLERANCE)
                {
                    // Chọn gridline gần cạnh dưới nhất, nhưng mở tới biên visual thật của nó.
                    if (!hasBottom || y > bottomLineY)
                    {
                        bottomLineY = y;
                        bottom = g.VisualMinY;
                        hasBottom = true;
                    }
                }

                if (y > oldCenterY + LINE_AXIS_TOLERANCE)
                {
                    // Chọn gridline gần cạnh trên nhất, nhưng mở tới biên visual thật của nó.
                    if (!hasTop || y < topLineY)
                    {
                        topLineY = y;
                        top = g.VisualMaxY;
                        hasTop = true;
                    }
                }
            }
        }

        if (!IsFinite(allMinX) || !IsFinite(allMaxX) || !IsFinite(allMinY) || !IsFinite(allMaxY))
        {
            view.RestrictionBox = oldBox;
            SafeModify(view, "restore old invalid");
            return false;
        }

        double finalMinX = hasLeft ? left - FINAL_EXTRA_MARGIN : (KEEP_OLD_SIDE_IF_NO_GRID_FOUND ? oldMinX : allMinX - FINAL_EXTRA_MARGIN);
        double finalMaxX = hasRight ? right + FINAL_EXTRA_MARGIN : (KEEP_OLD_SIDE_IF_NO_GRID_FOUND ? oldMaxX : allMaxX + FINAL_EXTRA_MARGIN);
        double finalMinY = hasBottom ? bottom - FINAL_EXTRA_MARGIN : (KEEP_OLD_SIDE_IF_NO_GRID_FOUND ? oldMinY : allMinY - FINAL_EXTRA_MARGIN);
        double finalMaxY = hasTop ? top + FINAL_EXTRA_MARGIN : (KEEP_OLD_SIDE_IF_NO_GRID_FOUND ? oldMaxY : allMaxY + FINAL_EXTRA_MARGIN);

        // Bảo vệ: nếu cạnh bị đảo hoặc quá nhỏ thì giữ cũ.
        if (finalMaxX <= finalMinX + 1.0)
        {
            finalMinX = oldMinX;
            finalMaxX = oldMaxX;
        }

        if (finalMaxY <= finalMinY + 1.0)
        {
            finalMinY = oldMinY;
            finalMaxY = oldMaxY;
        }


        AABB finalBox = new AABB(
            new Point(finalMinX, finalMinY, oldMinZ),
            new Point(finalMaxX, finalMaxY, oldMaxZ));

        view.RestrictionBox = finalBox;
        SafeModify(view, "final box");

        if (hasOriginalSheetBox)
        {
            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch { }

            KeepViewAtOriginalCenter(view, originalSheetBox);
        }

        return true;
    }

    private static bool ProgressiveExpandUntilEnoughGrid(
        View view,
        Drawing drawing,
        double oldMinX,
        double oldMaxX,
        double oldMinY,
        double oldMaxY,
        double oldMinZ,
        double oldMaxZ,
        out List<GridSeg> bestSegs,
        out double usedExpand)
    {
        bestSegs = new List<GridSeg>();
        usedExpand = 0.0;

        bool bestEnough = false;
        int bestScore = -1;

        bool lockLeft = false;
        bool lockRight = false;
        bool lockBottom = false;
        bool lockTop = false;

        double curMinX = oldMinX;
        double curMaxX = oldMaxX;
        double curMinY = oldMinY;
        double curMaxY = oldMaxY;

        for (int i = 0; i < TEMP_EXPAND_STEPS.Length; i++)
        {
            double step = TEMP_EXPAND_STEPS[i];
            usedExpand = step;

            // Lần đầu bung đều nhỏ để dò hướng.
            // Các lần sau chỉ bung những hướng còn thiếu.
            if (i == 0)
            {
                curMinX = oldMinX - step;
                curMaxX = oldMaxX + step;
                curMinY = oldMinY - step;
                curMaxY = oldMaxY + step;
            }
            else
            {
                if (!lockLeft) curMinX = oldMinX - step;
                if (!lockRight) curMaxX = oldMaxX + step;

                if (!lockBottom)
                    curMinY = oldMinY - step;

                if (!lockTop) curMaxY = oldMaxY + step;
            }


            // Chỉ mở X min/max và Y min/max.
            // Tuyệt đối giữ nguyên Depth up/down (Z min/max) để tránh Tekla regenerate sâu gây lag.
            AABB tempBox = new AABB(
                new Point(curMinX, curMinY, oldMinZ),
                new Point(curMaxX, curMaxY, oldMaxZ));

            view.RestrictionBox = tempBox;
            SafeModify(view, "directional expand " + R(step));

            try
            {
                if (drawing != null)
                {
                    drawing.CommitChanges();
                }
            }
            catch (Exception exCommit)
            {
            }

            List<GridSeg> segs = ReadGridSegments(view);
            DirectionStatus st = AnalyzeDirections(segs, oldMinX, oldMaxX, oldMinY, oldMaxY);


            if (st.HasLeft) lockLeft = true;
            if (st.HasRight) lockRight = true;
            if (st.HasBottom && st.BottomDistance <= step + LINE_AXIS_TOLERANCE) lockBottom = true;
            if (st.HasTop) lockTop = true;

            if (st.Score > bestScore || (st.Score == bestScore && (bestSegs == null || segs.Count < bestSegs.Count)))
            {
                bestScore = st.Score;
                bestSegs = segs;
                bestEnough = st.IsEnough;
            }

            // Dừng khi 4 hướng đã xử lý xong.
            // Với Bottom không tồn tại, lockBottom vẫn được tính là đã xử lý,
            // còn final box sẽ giữ oldMinY vì hasBottom=false trong bước tính cuối.
            if (lockLeft && lockRight && lockBottom && lockTop)
            {
                return true;
            }

            if (st.IsEnough)
            {
                return true;
            }
        }

        return bestEnough;
    }

    private class DirectionStatus
    {
        public bool HasLeft;
        public bool HasRight;
        public bool HasBottom;
        public bool HasTop;
        public double BottomDistance;
        public int Score;
        public bool IsEnough;
    }

    private static DirectionStatus AnalyzeDirections(
        List<GridSeg> segs,
        double oldMinX,
        double oldMaxX,
        double oldMinY,
        double oldMaxY)
    {
        DirectionStatus st = new DirectionStatus();
        st.BottomDistance = double.PositiveInfinity;

        // Chia box ban dau thanh 4 nua. Nhu vay grid co san trong khung
        // duoc ghi nhan ngay va huong do khong bi bung de tim grid ke tiep.
        double oldCenterX = (oldMinX + oldMaxX) * 0.5;
        double oldCenterY = (oldMinY + oldMaxY) * 0.5;

        if (segs == null)
            return st;

        foreach (GridSeg g in segs)
        {
            if (g == null) continue;

            if (g.IsVertical)
            {
                double x = g.ConstX;
                // Hai huong doi dien bat buoc phai la hai grid khac nhau.
                // Grid nam trong dai tolerance quanh tam khong khoa huong nao.
                if (x < oldCenterX - LINE_AXIS_TOLERANCE) st.HasLeft = true;
                if (x > oldCenterX + LINE_AXIS_TOLERANCE) st.HasRight = true;
            }

            if (g.IsHorizontal)
            {
                double y = g.ConstY;

                if (y < oldCenterY - LINE_AXIS_TOLERANCE)
                {
                    st.HasBottom = true;

                    double d = oldMinY - y;
                    if (d < 0.0) d = 0.0;
                    if (d < st.BottomDistance) st.BottomDistance = d;
                }

                if (y > oldCenterY + LINE_AXIS_TOLERANCE) st.HasTop = true;
            }
        }

        st.Score = 0;
        if (st.HasLeft) st.Score++;
        if (st.HasRight) st.Score++;
        if (st.HasBottom) st.Score++;
        if (st.HasTop) st.Score++;
        st.IsEnough = st.Score >= 4;

        return st;
    }

    private static List<GridSeg> ReadGridSegments(View view)
    {
        List<GridSeg> list = new List<GridSeg>();

        try
        {
            // Cách 1: quét toàn bộ object trong view
            DrawingObjectEnumerator e = view.GetAllObjects();
            while (e != null && e.MoveNext())
            {
                object o = e.Current;
                if (o == null) continue;

                string tn = TypeName(o);

                if (tn == "Tekla.Structures.Drawing.Grid")
                {
                    ReadGridObject(o, list);
                    continue;
                }

                if (tn == "Tekla.Structures.Drawing.GridLine")
                {
                    GridSeg seg = MakeSegFromGridLine(o);
                    AddUnique(list, seg);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
        }

        try
        {
            // Cách 2: fallback gọi GetObjects(Type[]) nếu Tekla bản này hỗ trợ.
            Type gridType = typeof(Tekla.Structures.Drawing.Grid);
            Type gridLineType = typeof(Tekla.Structures.Drawing.GridLine);
            Type[] types = new Type[] { gridType, gridLineType };

            MethodInfo m = view.GetType().GetMethod(
                "GetObjects",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(Type[]) },
                null);

            if (m != null)
            {
                object enumObj = m.Invoke(view, new object[] { types });
                DrawingObjectEnumerator de = enumObj as DrawingObjectEnumerator;
                while (de != null && de.MoveNext())
                {
                    object o = de.Current;
                    string tn = TypeName(o);

                    if (tn == "Tekla.Structures.Drawing.Grid")
                        ReadGridObject(o, list);
                    else if (tn == "Tekla.Structures.Drawing.GridLine")
                        AddUnique(list, MakeSegFromGridLine(o));
                }
            }
        }
        catch (Exception ex)
        {
        }

        return list;
    }

    private static void ReadGridObject(object grid, List<GridSeg> list)
    {
        if (grid == null) return;

        if (IGNORE_HIDDEN_GRID && IsHidden(grid))
        {
            return;
        }

        object enumObj = Invoke0(grid, "GetObjects");
        DrawingObjectEnumerator doe = enumObj as DrawingObjectEnumerator;

        if (doe != null)
        {
            while (doe.MoveNext())
            {
                GridSeg seg = MakeSegFromGridLine(doe.Current);
                AddUnique(list, seg);
            }
            return;
        }

        System.Collections.IEnumerator ie = enumObj as System.Collections.IEnumerator;
        if (ie != null)
        {
            while (ie.MoveNext())
            {
                GridSeg seg = MakeSegFromGridLine(ie.Current);
                AddUnique(list, seg);
            }
        }
    }

    private static GridSeg MakeSegFromGridLine(object gl)
    {
        if (gl == null) return null;

        if (IGNORE_HIDDEN_GRID && IsHidden(gl))
            return null;

        object startLabel = GetProp(gl, "StartLabel");
        object endLabel = GetProp(gl, "EndLabel");

        Point p1 = GetPoint(GetProp(startLabel, "GridPoint"));
        Point p2 = GetPoint(GetProp(endLabel, "GridPoint"));

        string label = "";
        object l1 = GetProp(startLabel, "GridLabelText");
        object l2 = GetProp(endLabel, "GridLabelText");
        if (l1 != null) label = l1.ToString();
        else if (l2 != null) label = l2.ToString();

        if (p1 == null || p2 == null)
            return null;

        GridSeg g = new GridSeg();
        g.Label = label;
        g.X1 = p1.X;
        g.Y1 = p1.Y;
        g.X2 = p2.X;
        g.Y2 = p2.Y;

        g.IsVertical = Math.Abs(g.X1 - g.X2) <= LINE_AXIS_TOLERANCE && Math.Abs(g.Y1 - g.Y2) > LINE_AXIS_TOLERANCE;
        g.IsHorizontal = Math.Abs(g.Y1 - g.Y2) <= LINE_AXIS_TOLERANCE && Math.Abs(g.X1 - g.X2) > LINE_AXIS_TOLERANCE;

        g.ConstX = (g.X1 + g.X2) * 0.5;
        g.ConstY = (g.Y1 + g.Y2) * 0.5;

        // Trường hợp grid hơi nghiêng rất nhỏ sau transform thì nhận theo hướng trội.
        if (!g.IsVertical && !g.IsHorizontal)
        {
            double dx = Math.Abs(g.X1 - g.X2);
            double dy = Math.Abs(g.Y1 - g.Y2);

            if (dy > dx * 5.0)
            {
                g.IsVertical = true;
                g.ConstX = (g.X1 + g.X2) * 0.5;
            }
            else if (dx > dy * 5.0)
            {
                g.IsHorizontal = true;
                g.ConstY = (g.Y1 + g.Y2) * 0.5;
            }
        }

        // Biên visual: lấy cả đường grid, GridLabelPoint, OffsetGridPoint, CenterPoint,
        // cộng thêm nửa frame/text để không hụt bubble/text như ảnh đường đỏ.
        double minX = Math.Min(g.X1, g.X2);
        double maxX = Math.Max(g.X1, g.X2);
        double minY = Math.Min(g.Y1, g.Y2);
        double maxY = Math.Max(g.Y1, g.Y2);

        ExpandByLabelVisual(startLabel, ref minX, ref maxX, ref minY, ref maxY);
        ExpandByLabelVisual(endLabel, ref minX, ref maxX, ref minY, ref maxY);

        // Dự phòng thêm offset line trong attribute nếu có.
        object attr = GetProp(gl, "Attributes");
        double offsetStart = ToDouble(GetProp(attr, "OffsetAtStartOfLine"), 0.0);
        double offsetEnd = ToDouble(GetProp(attr, "OffsetAtEndOfLine"), 0.0);
        double reserve = Math.Max(0.0, Math.Max(offsetStart, offsetEnd));

        // Không bung reserve quá lớn theo mọi hướng, chỉ cộng nhẹ để chống cắt mép.
        // Label visual phía trên đã là nguồn chính xác hơn.
        double safe = Math.Min(Math.Max(reserve, 20.0), 80.0) + VISUAL_EXTRA_MARGIN;
        minX -= safe;
        maxX += safe;
        minY -= safe;
        maxY += safe;

        g.VisualMinX = minX;
        g.VisualMaxX = maxX;
        g.VisualMinY = minY;
        g.VisualMaxY = maxY;

        return g;
    }

    private static void ExpandByLabelVisual(object labelObj, ref double minX, ref double maxX, ref double minY, ref double maxY)
    {
        if (labelObj == null) return;

        ExpandByPoint(GetPoint(GetProp(labelObj, "GridPoint")), ref minX, ref maxX, ref minY, ref maxY, 0, 0);
        ExpandByPoint(GetPoint(GetProp(labelObj, "GridLabelPoint")), ref minX, ref maxX, ref minY, ref maxY, 0, 0);
        ExpandByPoint(GetPoint(GetProp(labelObj, "OffsetGridPoint")), ref minX, ref maxX, ref minY, ref maxY, 0, 0);

        Point center = GetPoint(GetProp(labelObj, "CenterPoint"));
        double frameW = ToDouble(GetProp(labelObj, "FrameWidth"), 0.0);
        double frameH = ToDouble(GetProp(labelObj, "FrameHeight"), 0.0);
        double textW = ToDouble(GetProp(labelObj, "TextWidth"), 0.0);
        double textH = ToDouble(GetProp(labelObj, "TextHeight"), 0.0);

        double halfW = Math.Max(frameW, textW) * 0.5;
        double halfH = Math.Max(frameH, textH) * 0.5;

        // cộng thêm 10 để tránh Tekla crop anti-alias/frame sát mép.
        ExpandByPoint(center, ref minX, ref maxX, ref minY, ref maxY, halfW + 10.0, halfH + 10.0);
    }

    private static void ExpandByPoint(Point p, ref double minX, ref double maxX, ref double minY, ref double maxY, double halfW, double halfH)
    {
        if (p == null) return;

        double a = p.X - halfW;
        double b = p.X + halfW;
        double c = p.Y - halfH;
        double d = p.Y + halfH;

        if (a < minX) minX = a;
        if (b > maxX) maxX = b;
        if (c < minY) minY = c;
        if (d > maxY) maxY = d;
    }

    private static double ToDouble(object obj, double fallback)
    {
        try
        {
            if (obj == null) return fallback;
            return Convert.ToDouble(obj);
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddUnique(List<GridSeg> list, GridSeg seg)
    {
        if (seg == null) return;
        if (!seg.IsVertical && !seg.IsHorizontal) return;

        foreach (GridSeg old in list)
        {
            if (old == null) continue;
            if (old.Label == seg.Label &&
                Math.Abs(old.X1 - seg.X1) < 0.5 &&
                Math.Abs(old.Y1 - seg.Y1) < 0.5 &&
                Math.Abs(old.X2 - seg.X2) < 0.5 &&
                Math.Abs(old.Y2 - seg.Y2) < 0.5)
                return;
        }

        list.Add(seg);
    }

    private static bool IsHidden(object drawingObject)
    {
        try
        {
            object hideable = GetProp(drawingObject, "Hideable");
            object isHidden = GetProp(hideable, "IsHidden");
            if (isHidden is bool) return (bool)isHidden;
        }
        catch { }

        return false;
    }

    public static bool PrepareTargetViewSelectionForMacro(out string message)
    {
        message = string.Empty;

        try
        {
            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                message = "DrawingHandler chưa kết nối.";
                return false;
            }

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                message = "Không có active drawing.";
                return false;
            }

            List<View> manuallySelectedViews = GetSelectedViews(dh);
            List<View> targetViews = GetTargetViews(dh, drawing);

            if (targetViews.Count == 0)
            {
                message = "Không tìm thấy View để xử lý.";
                return false;
            }

            // Người dùng đã chọn View: giữ nguyên selection hiện tại.
            // Section thường/đặc biệt chỉ đi vào flow theo cách chọn thủ công này.
            if (manuallySelectedViews.Count > 0)
                return true;

            // Không có View được chọn: GetTargetViews chỉ trả về
            // Front/Top/Bottom/Back và không tự động đưa Section vào.
            System.Collections.ArrayList viewsToSelect =
                new System.Collections.ArrayList();

            foreach (View view in targetViews)
                viewsToSelect.Add(view);

            dh.GetDrawingObjectSelector().SelectObjects(viewsToSelect, false);
            message = "Đã tự động chọn " + targetViews.Count + " View.";
            return true;
        }
        catch (Exception ex)
        {
            message = "Chọn View cho Grid Visibility lỗi: " + ex.Message;
            return false;
        }
    }

    private static List<View> GetTargetViews(DrawingHandler dh, Drawing drawing)
    {
        List<View> selectedViews = GetSelectedViews(dh);
        List<View> result = new List<View>();

        if (selectedViews.Count > 0)
        {
            // View thuong chi nhan 4 ViewType cho phep.
            // SectionView chi duoc chay khi nguoi dung chu dong click chon view do.
            foreach (View view in selectedViews)
            {
                if (IsAutomaticGridViewType(view) || IsSectionViewType(view))
                    AddUniqueView(result, view);
            }

            return result;
        }

        // Khong co view duoc chon: chi tu dong mo grid cho Front/Top/Bottom/Back.
        // Tuyet doi khong tu dong dua SectionView vao danh sach.
        List<View> allViews = GetAllViewsInDrawing(drawing);
        foreach (View view in allViews)
        {
            if (IsAutomaticGridViewType(view))
                AddUniqueView(result, view);
        }

        return result;
    }

    private static bool IsAutomaticGridViewType(View view)
    {
        return ViewTypeMatches(view, "FrontView", "Front") ||
               ViewTypeMatches(view, "TopView", "Top") ||
               ViewTypeMatches(view, "BottomView", "Bottom") ||
               ViewTypeMatches(view, "BackView", "Back");
    }

    private static bool IsSectionViewType(View view)
    {
        return ViewTypeMatches(view, "SectionView", "Section");
    }

    // Cung cach xet ViewType dang dung trong PHU_Shape_L:
    // chi doc thuoc tinh ViewType, khong doan theo vi tri/kich thuoc view tren giay.
    private static bool ViewTypeMatches(View view, string exactViewTypeName, string fallbackText)
    {
        try
        {
            if (view == null)
                return false;

            string text = "";
            try { text = view.ViewType.ToString(); }
            catch { text = ""; }

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

    private static List<View> GetSelectedViews(DrawingHandler dh)
    {
        List<View> views = new List<View>();

        try
        {
            DrawingObjectEnumerator e = dh.GetDrawingObjectSelector().GetSelected();
            while (e != null && e.MoveNext())
            {
                View v = e.Current as View;
                if (v != null)
                    AddUniqueView(views, v);
            }
        }
        catch { }

        return views;
    }

    private static List<View> GetAllViewsInDrawing(Drawing drawing)
    {
        List<View> views = new List<View>();

        try
        {
            if (drawing == null)
                return views;

            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                return views;

            DrawingObjectEnumerator e = sheet.GetAllViews();
            while (e != null && e.MoveNext())
            {
                View v = e.Current as View;
                if (v != null)
                    AddUniqueView(views, v);
            }
        }
        catch { }

        return views;
    }

    private static void AddUniqueView(List<View> views, View view)
    {
        if (views == null || view == null)
            return;

        string key = GetViewStableKey(view);

        foreach (View old in views)
        {
            if (old == null)
                continue;

            string oldKey = GetViewStableKey(old);
            if (!string.IsNullOrEmpty(key) && key == oldKey)
                return;

            if (object.ReferenceEquals(old, view))
                return;
        }

        views.Add(view);
    }

    private static void SafeModify(View view, string step)
    {
        try
        {
            bool ok = view.Modify();
        }
        catch (Exception ex)
        {
        }
    }

    private static object GetProp(object obj, string name)
    {
        if (obj == null) return null;

        try
        {
            PropertyInfo p = obj.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p == null) return null;
            if (p.GetIndexParameters().Length > 0) return null;

            return p.GetValue(obj, null);
        }
        catch
        {
            return null;
        }
    }

    private static object Invoke0(object obj, string name)
    {
        if (obj == null) return null;

        try
        {
            MethodInfo m = obj.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            if (m == null) return null;
            return m.Invoke(obj, null);
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    private static Point GetPoint(object obj)
    {
        Point p = obj as Point;
        if (p != null) return p;

        try
        {
            if (obj == null) return null;
            object ox = GetProp(obj, "X");
            object oy = GetProp(obj, "Y");
            object oz = GetProp(obj, "Z");

            if (ox == null || oy == null) return null;

            double x = Convert.ToDouble(ox);
            double y = Convert.ToDouble(oy);
            double z = oz == null ? 0.0 : Convert.ToDouble(oz);
            return new Point(x, y, z);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeViewName(View v)
    {
        try
        {
            if (v == null) return "";
            if (!string.IsNullOrEmpty(v.Name)) return v.Name;
        }
        catch { }

        return "";
    }

    private static string SafeValue(object o)
    {
        if (o == null) return "";
        try { return o.ToString(); }
        catch { return ""; }
    }

    private static string TypeName(object o)
    {
        if (o == null) return "";
        try { return o.GetType().FullName; }
        catch { return ""; }
    }

    private static bool IsFinite(double v)
    {
        return !(double.IsNaN(v) || double.IsInfinity(v));
    }

    private static string R(double v)
    {
        return Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }


    // =================================================
    // PHU KEEP VIEW POSITION / GAP 20
    // Giữ vị trí khung xanh gần tâm ban đầu sau khi bung RestrictionBox.
    // Nếu chọn nhiều view thì chỉ đẩy view bị chồng sang phải, gap tối thiểu 20.
    // =================================================

    private class ViewSheetBox
    {
        public View View;
        public double MinX;
        public double MaxX;
        public double MinY;
        public double MaxY;
        public double CenterX;
        public double CenterY;
    }

    private static bool TryGetViewSheetBox(View view, out ViewSheetBox box)
    {
        box = null;

        try
        {
            if (view == null)
                return false;

            AABB bb = view.GetAxisAlignedBoundingBox();
            if (bb == null || bb.MinPoint == null || bb.MaxPoint == null)
                return false;

            double minX = Math.Min(bb.MinPoint.X, bb.MaxPoint.X);
            double maxX = Math.Max(bb.MinPoint.X, bb.MaxPoint.X);
            double minY = Math.Min(bb.MinPoint.Y, bb.MaxPoint.Y);
            double maxY = Math.Max(bb.MinPoint.Y, bb.MaxPoint.Y);

            if (!IsFinite(minX) || !IsFinite(maxX) || !IsFinite(minY) || !IsFinite(maxY))
                return false;

            if (maxX <= minX + 0.01 || maxY <= minY + 0.01)
                return false;

            box = new ViewSheetBox();
            box.View = view;
            box.MinX = minX;
            box.MaxX = maxX;
            box.MinY = minY;
            box.MaxY = maxY;
            box.CenterX = (minX + maxX) * 0.5;
            box.CenterY = (minY + maxY) * 0.5;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetRestrictionBoxClone(View view, out AABB box)
    {
        box = null;

        try
        {
            if (view == null)
                return false;

            AABB rb = view.RestrictionBox;
            if (rb == null || rb.MinPoint == null || rb.MaxPoint == null)
                return false;

            Point min = new Point(rb.MinPoint.X, rb.MinPoint.Y, rb.MinPoint.Z);
            Point max = new Point(rb.MaxPoint.X, rb.MaxPoint.Y, rb.MaxPoint.Z);

            box = new AABB(min, max);
            return true;
        }
        catch
        {
            box = null;
            return false;
        }
    }

    private static void CaptureFitOriginalSheetBoxes(List<View> views)
    {
        try
        {
            if (views == null)
                return;

            foreach (View v in views)
            {
                if (v == null)
                    continue;

                string key = GetViewStableKey(v);
                if (string.IsNullOrEmpty(key))
                    continue;

                ViewSheetBox b;
                if (TryGetViewSheetBox(v, out b))
                    FIT_ORIGINAL_SHEET_BOXES[key] = b;

                AABB rb;
                if (TryGetRestrictionBoxClone(v, out rb))
                    FIT_ORIGINAL_RESTRICTION_BOXES[key] = rb;
            }
        }
        catch
        {
        }
    }

    private static bool TryGetFitOriginalSheetBox(View view, out ViewSheetBox box)
    {
        box = null;

        try
        {
            string key = GetViewStableKey(view);
            if (string.IsNullOrEmpty(key))
                return false;

            return FIT_ORIGINAL_SHEET_BOXES.TryGetValue(key, out box) && box != null;
        }
        catch
        {
            box = null;
            return false;
        }
    }

    private static bool TryGetFitOriginalRestrictionBox(View view, out AABB box)
    {
        box = null;

        try
        {
            string key = GetViewStableKey(view);
            if (string.IsNullOrEmpty(key))
                return false;

            AABB saved;
            if (!FIT_ORIGINAL_RESTRICTION_BOXES.TryGetValue(key, out saved) || saved == null || saved.MinPoint == null || saved.MaxPoint == null)
                return false;

            Point min = new Point(saved.MinPoint.X, saved.MinPoint.Y, saved.MinPoint.Z);
            Point max = new Point(saved.MaxPoint.X, saved.MaxPoint.Y, saved.MaxPoint.Z);
            box = new AABB(min, max);
            return true;
        }
        catch
        {
            box = null;
            return false;
        }
    }

    private static string GetViewStableKey(View view)
    {
        try
        {
            if (view == null)
                return "";

            object identifier = GetProp(view, "Identifier");
            object id = GetProp(identifier, "ID");
            if (id != null)
                return "ID:" + id.ToString();

            string name = SafeViewName(view);
            if (!string.IsNullOrEmpty(name))
                return "NAME:" + name;
        }
        catch
        {
        }

        return "";
    }

    private static void KeepViewAtOriginalCenter(View view, ViewSheetBox originalBox)
    {
        try
        {
            if (view == null || originalBox == null)
                return;

            ViewSheetBox newBox;
            if (!TryGetViewSheetBox(view, out newBox))
                return;

            MoveViewBySheetDelta(view, originalBox.CenterX - newBox.CenterX, originalBox.CenterY - newBox.CenterY);
        }
        catch
        {
        }
    }

    private static void KeepSelectedViewsNonOverlapping(List<View> views, double gap)
    {
        try
        {
            if (views == null || views.Count <= 1)
                return;

            if (gap < 0.0)
                gap = 0.0;

            List<ViewSheetBox> boxes = new List<ViewSheetBox>();
            foreach (View v in views)
            {
                ViewSheetBox b;
                if (TryGetViewSheetBox(v, out b))
                    boxes.Add(b);
            }

            boxes.Sort(delegate (ViewSheetBox a, ViewSheetBox b)
            {
                int x = a.MinX.CompareTo(b.MinX);
                if (x != 0) return x;
                return b.MaxY.CompareTo(a.MaxY);
            });

            for (int i = 0; i < boxes.Count; i++)
            {
                ViewSheetBox cur = boxes[i];
                if (cur == null || cur.View == null)
                    continue;

                double dx = 0.0;
                for (int j = 0; j < i; j++)
                {
                    ViewSheetBox prev = boxes[j];
                    if (prev == null)
                        continue;

                    double testMinX = cur.MinX + dx;
                    double testMaxX = cur.MaxX + dx;
                    bool overlapY = !(cur.MaxY + gap <= prev.MinY || cur.MinY - gap >= prev.MaxY);
                    bool overlapX = !(testMaxX + gap <= prev.MinX || testMinX - gap >= prev.MaxX);

                    if (overlapX && overlapY)
                    {
                        double needDx = prev.MaxX + gap - cur.MinX;
                        if (needDx > dx)
                            dx = needDx;
                    }
                }

                if (dx > 0.01)
                {
                    MoveViewBySheetDelta(cur.View, dx, 0.0);
                    cur.MinX += dx;
                    cur.MaxX += dx;
                    cur.CenterX += dx;
                }
            }
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

            if (Math.Abs(dx) <= 0.01 && Math.Abs(dy) <= 0.01)
                return;

            Point origin = view.Origin;
            if (origin == null)
                return;

            Point newOrigin = new Point(origin.X + dx, origin.Y + dy, origin.Z);
            if (TrySetViewOrigin(view, newOrigin))
                SafeModify(view, "move view");
        }
        catch
        {
        }
    }

    private static bool TrySetViewOrigin(View view, Point origin)
    {
        try
        {
            if (view == null || origin == null)
                return false;

            PropertyInfo prop = view.GetType().GetProperty(
                "Origin",
                BindingFlags.Public | BindingFlags.Instance);

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



    // =================================================
    // PHU FIT MODEL PADDING 20
    // Sau khi dùng Open Grid View để kiểm trục thủ công,
    // chạy hàm này để thu RestrictionBox lại quanh model chính trong View.
    // Dùng Solid thật của Model Part theo hệ tọa độ View, padding 20 giống ResizeViewBoundary.
    // Không lấy Dimension / Mark / Text / Grid.
    // =================================================

    private const double FIT_PADDING_DEFAULT = 20.0;

    public static Result RunFitPadding20()
    {
        return RunFitPadding(FIT_PADDING_DEFAULT);
    }

    public static Result RunFitPadding(double padding)
    {
        Result result = new Result();

        try
        {
            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                result.FailedCount++;
                result.Message = "DrawingHandler chưa kết nối.";
                return result;
            }

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                result.FailedCount++;
                result.Message = "Không có active drawing.";
                return result;
            }

            List<View> views = GetTargetViews(dh, drawing);
            if (views.Count == 0)
            {
                result.FailedCount++;
                result.Message = "Không tìm thấy View để xử lý.";
                return result;
            }

            result.ViewCount = views.Count;

            foreach (View v in views)
            {
                try
                {
                    AABB originalRestrictionBox;
                    bool hasOriginalRestrictionBox = TryGetFitOriginalRestrictionBox(v, out originalRestrictionBox);

                    ViewSheetBox originalSheetBox;
                    bool hasOriginalSheetBox = TryGetFitOriginalSheetBox(v, out originalSheetBox);

                    if (!hasOriginalRestrictionBox)
                    {
                        result.FailedCount++;
                        continue;
                    }

                    v.RestrictionBox = originalRestrictionBox;
                    SafeModify(v, "restore original restriction box");

                    if (hasOriginalSheetBox)
                    {
                        try { drawing.CommitChanges(); }
                        catch { }

                        KeepViewAtOriginalCenter(v, originalSheetBox);
                    }

                    result.SuccessCount++;
                }
                catch
                {
                    result.FailedCount++;
                }
            }

            try { drawing.CommitChanges(); }
            catch { }

            result.Message = "Restore original RestrictionBox done.";
            return result;
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Message = "Fatal error: " + ex.Message;
            return result;
        }
    }

    private static bool FitViewRestrictionBoxByModelSolid(View view, Tekla.Structures.Model.Model model, double padding)
    {
        if (view == null || model == null)
            return false;

        AABB oldBox = view.RestrictionBox;
        if (oldBox == null || oldBox.MinPoint == null || oldBox.MaxPoint == null)
            return false;

        double oldMinZ = Math.Min(oldBox.MinPoint.Z, oldBox.MaxPoint.Z);
        double oldMaxZ = Math.Max(oldBox.MinPoint.Z, oldBox.MaxPoint.Z);

        if (oldMaxZ <= oldMinZ + 5.0)
        {
            oldMinZ = -100.0;
            oldMaxZ = 100.0;
        }

        bool found = false;
        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;

        Tekla.Structures.Model.TransformationPlane oldPlane = null;

        try
        {
            oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                new Tekla.Structures.Model.TransformationPlane(view.DisplayCoordinateSystem));

            DrawingObjectEnumerator e = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Part));
            while (e != null && e.MoveNext())
            {
                Tekla.Structures.Drawing.Part drawingPart = e.Current as Tekla.Structures.Drawing.Part;
                if (drawingPart == null)
                    continue;

                Tekla.Structures.Model.ModelObject modelObject = null;
                try
                {
                    modelObject = model.SelectModelObject(drawingPart.ModelIdentifier);
                }
                catch
                {
                    modelObject = null;
                }

                Tekla.Structures.Model.Part modelPart = modelObject as Tekla.Structures.Model.Part;
                if (modelPart == null)
                    continue;

                Tekla.Structures.Model.Solid solid = null;
                try
                {
                    solid = modelPart.GetSolid();
                }
                catch
                {
                    solid = null;
                }

                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    continue;

                double sx1 = Math.Min(solid.MinimumPoint.X, solid.MaximumPoint.X);
                double sx2 = Math.Max(solid.MinimumPoint.X, solid.MaximumPoint.X);
                double sy1 = Math.Min(solid.MinimumPoint.Y, solid.MaximumPoint.Y);
                double sy2 = Math.Max(solid.MinimumPoint.Y, solid.MaximumPoint.Y);

                if (!IsFinite(sx1) || !IsFinite(sx2) || !IsFinite(sy1) || !IsFinite(sy2))
                    continue;

                if (sx2 <= sx1 + 0.01 || sy2 <= sy1 + 0.01)
                    continue;

                if (sx1 < minX) minX = sx1;
                if (sx2 > maxX) maxX = sx2;
                if (sy1 < minY) minY = sy1;
                if (sy2 > maxY) maxY = sy2;
                found = true;
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                if (oldPlane != null)
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
            }
            catch
            {
            }
        }

        if (!found)
            return false;

        Point newMin = new Point(minX - padding, minY - padding, oldMinZ);
        Point newMax = new Point(maxX + padding, maxY + padding, oldMaxZ);

        if (!IsValidFitBoundaryBox(newMin, newMax))
            return false;

        try
        {
            view.RestrictionBox = new AABB(newMin, newMax);
            SafeModify(view, "fit model padding " + R(padding));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidFitBoundaryBox(Point min, Point max)
    {
        if (min == null || max == null)
            return false;

        if (!IsFinite(min.X) || !IsFinite(min.Y) || !IsFinite(min.Z) ||
            !IsFinite(max.X) || !IsFinite(max.Y) || !IsFinite(max.Z))
            return false;

        if (max.X <= min.X + 1.0)
            return false;

        if (max.Y <= min.Y + 1.0)
            return false;

        double w = Math.Abs(max.X - min.X);
        double h = Math.Abs(max.Y - min.Y);
        if (w > 3000.0 || h > 3000.0)
            return false;

        return true;
    }




    // =================================================
    // PHU NEIGHBOR GRIDS - CREATE DIRECT
    // Port chức năng chính Neighboring grids: tạo mark lân cận theo Grid trong Drawing.
    // Không mở UI extension.
    // MainForm gọi:
    //     PHU_OpenGridView.RunNeighborGrid(30.0, 0.0);
    //     PHU_OpenGridView.RunNeighborGrid30();
    // =================================================

    private const string PHU_NG_UDA_FIELD = "CREATED_BY";
    private const string PHU_NG_UDA_VALUE_GRID = "CUSTOM_OBJECT_CREATED_BY_NEIGHBORING_GRID_SYMBOLS";

    public static Result RunNeighborGrid30()
    {
        return RunNeighborGrid(30.0, 0.0);
    }

    public static Result RunNeighborGrid(double xOffset)
    {
        return RunNeighborGrid(xOffset, 0.0);
    }

    public static Result RunNeighborGrid(double xOffset, double yOffset)
    {
        Result result = new Result();

        try
        {
            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                result.FailedCount++;
                result.Message = "DrawingHandler chưa kết nối.";
                return result;
            }

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                result.FailedCount++;
                result.Message = "Không có active drawing.";
                return result;
            }

            Tekla.Structures.Model.Model model = new Tekla.Structures.Model.Model();
            if (!model.GetConnectionStatus())
            {
                result.FailedCount++;
                result.Message = "Model chưa kết nối.";
                return result;
            }

            List<View> views = GetTargetViews(dh, drawing);
            if (views.Count == 0)
            {
                result.FailedCount++;
                result.Message = "Không tìm thấy View để xử lý.";
                return result;
            }

            result.ViewCount = views.Count;

            int created = 0;

            foreach (View view in views)
            {
                try
                {
                    int c = CreateNeighborGridInView(view, model, xOffset, yOffset);
                    if (c > 0)
                    {
                        created += c;
                        result.SuccessCount++;
                    }
                    else
                    {
                        result.FailedCount++;
                    }
                }
                catch
                {
                    result.FailedCount++;
                }
            }

            try { drawing.CommitChanges(); }
            catch { }

            result.Message = "Neighbor grids created: " + created;
            return result;
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Message = "Fatal error: " + ex.Message;
            return result;
        }
    }

    private static int CreateNeighborGridInView(View view, Tekla.Structures.Model.Model model, double xOffset, double yOffset)
    {
        if (view == null || model == null)
            return 0;

        int created = 0;

        try
        {
            DrawingObjectEnumerator grids = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Grid));
            while (grids != null && grids.MoveNext())
            {
                Tekla.Structures.Drawing.Grid drawingGrid = grids.Current as Tekla.Structures.Drawing.Grid;
                if (drawingGrid == null)
                    continue;

                Tekla.Structures.Model.Grid modelGrid = GetModelGridFromDrawingGrid(drawingGrid, model);
                if (modelGrid == null)
                    continue;

                created += CreateNeighborMarksForDrawingGrid(view, drawingGrid, modelGrid, model, xOffset, yOffset);
            }
        }
        catch
        {
        }

        return created;
    }

    private static Tekla.Structures.Model.Grid GetModelGridFromDrawingGrid(Tekla.Structures.Drawing.Grid drawingGrid, Tekla.Structures.Model.Model model)
    {
        try
        {
            if (drawingGrid == null || model == null)
                return null;

            object modelIdentifier = GetProp(drawingGrid, "ModelIdentifier");
            Tekla.Structures.Identifier id = modelIdentifier as Tekla.Structures.Identifier;
            if (id == null)
                return null;

            Tekla.Structures.Model.ModelObject obj = model.SelectModelObject(id);
            return obj as Tekla.Structures.Model.Grid;
        }
        catch
        {
            return null;
        }
    }

    private static int CreateNeighborMarksForDrawingGrid(
        View view,
        Tekla.Structures.Drawing.Grid drawingGrid,
        Tekla.Structures.Model.Grid modelGrid,
        Tekla.Structures.Model.Model model,
        double xOffset,
        double yOffset)
    {
        int created = 0;

        List<string> visibleLabels = GetDrawingGridVisibleLabels(drawingGrid, model);
        if (visibleLabels.Count == 0)
            return 0;

        List<string[]> modelLabelGroups = GetModelGridLabelGroups(modelGrid);

        List<string> firstVisibleLabels = new List<string>();
        List<string> lastVisibleLabels = new List<string>();

        foreach (string[] group in modelLabelGroups)
            AddFirstLastVisibleInGroup(visibleLabels, group, firstVisibleLabels, lastVisibleLabels);

        DrawingObjectEnumerator lines = drawingGrid.GetObjects();
        while (lines != null && lines.MoveNext())
        {
            Tekla.Structures.Drawing.GridLine line = lines.Current as Tekla.Structures.Drawing.GridLine;
            if (line == null)
                continue;

            string currentLabel = GetGridLineModelLabel(line, model);
            if (string.IsNullOrEmpty(currentLabel))
                currentLabel = GetGridLineDrawingLabel(line);

            if (string.IsNullOrEmpty(currentLabel))
                continue;

            string previousLabel;
            string nextLabel;
            if (!FindNeighborLabels(modelLabelGroups, currentLabel, out previousLabel, out nextLabel))
                continue;

            bool showPrevious = firstVisibleLabels.Contains(currentLabel) && !string.IsNullOrEmpty(previousLabel);
            bool showNext = lastVisibleLabels.Contains(currentLabel) && !string.IsNullOrEmpty(nextLabel);

            if (!showPrevious && !showNext)
                continue;

            created += CreateNeighborMarksForGridLine(
                view,
                line,
                modelGrid,
                model,
                currentLabel,
                previousLabel,
                nextLabel,
                showPrevious,
                showNext,
                xOffset,
                yOffset);
        }

        return created;
    }

    private static List<string> GetDrawingGridVisibleLabels(Tekla.Structures.Drawing.Grid drawingGrid, Tekla.Structures.Model.Model model)
    {
        List<string> labels = new List<string>();

        try
        {
            DrawingObjectEnumerator lines = drawingGrid.GetObjects();
            while (lines != null && lines.MoveNext())
            {
                Tekla.Structures.Drawing.GridLine line = lines.Current as Tekla.Structures.Drawing.GridLine;
                if (line == null)
                    continue;

                string label = GetGridLineModelLabel(line, model);
                if (string.IsNullOrEmpty(label))
                    label = GetGridLineDrawingLabel(line);

                AddUniqueString(labels, label);
            }
        }
        catch
        {
        }

        return labels;
    }

    private static List<string[]> GetModelGridLabelGroups(Tekla.Structures.Model.Grid modelGrid)
    {
        List<string[]> groups = new List<string[]>();

        try { groups.Add(SplitGridLabels(modelGrid.LabelX)); } catch { }
        try { groups.Add(SplitGridLabels(modelGrid.LabelY)); } catch { }
        try { groups.Add(SplitGridLabels(modelGrid.LabelZ)); } catch { }

        return groups;
    }

    private static string[] SplitGridLabels(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new string[0];

        return text.Split(
            new char[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static void AddFirstLastVisibleInGroup(
        List<string> visibleLabels,
        string[] modelLabels,
        List<string> firstVisibleLabels,
        List<string> lastVisibleLabels)
    {
        try
        {
            if (visibleLabels == null || modelLabels == null || modelLabels.Length == 0)
                return;

            bool found = false;
            string first = "";
            string last = "";

            for (int i = 0; i < modelLabels.Length; i++)
            {
                string label = modelLabels[i];
                if (string.IsNullOrEmpty(label))
                    continue;

                if (visibleLabels.Contains(label))
                {
                    if (!found)
                    {
                        first = label;
                        found = true;
                    }

                    last = label;
                }
            }

            if (found)
            {
                AddUniqueString(firstVisibleLabels, first);
                AddUniqueString(lastVisibleLabels, last);
            }
        }
        catch
        {
        }
    }

    private static bool FindNeighborLabels(List<string[]> groups, string currentLabel, out string previousLabel, out string nextLabel)
    {
        previousLabel = "";
        nextLabel = "";

        try
        {
            if (groups == null || string.IsNullOrEmpty(currentLabel))
                return false;

            foreach (string[] group in groups)
            {
                if (group == null)
                    continue;

                for (int i = 0; i < group.Length; i++)
                {
                    if (group[i] != currentLabel)
                        continue;

                    if (i > 0)
                        previousLabel = group[i - 1];

                    if (i + 1 < group.Length)
                        nextLabel = group[i + 1];

                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static int CreateNeighborMarksForGridLine(
        View view,
        Tekla.Structures.Drawing.GridLine line,
        Tekla.Structures.Model.Grid modelGrid,
        Tekla.Structures.Model.Model model,
        string currentLabel,
        string previousLabel,
        string nextLabel,
        bool showPrevious,
        bool showNext,
        double xOffset,
        double yOffset)
    {
        int created = 0;

        try
        {
            if (view == null || line == null || modelGrid == null)
                return 0;

            Point currentGridPoint = GetModelGridPlanePointInView(modelGrid, currentLabel, view);
            Point previousGridPoint = GetModelGridPlanePointInView(modelGrid, previousLabel, view);
            Point nextGridPoint = GetModelGridPlanePointInView(modelGrid, nextLabel, view);

            Vector gridDirection = new LineSegment(line.StartLabel.GridPoint, line.EndLabel.GridPoint).GetDirectionVector();

            bool isVerticalGridLine = Math.Abs(gridDirection.Y) > Math.Abs(gridDirection.X);

            bool hasStartLabel = GetBool(GetProp(line.Attributes, "DrawTextAtStartOfGridLine"), true);
            bool hasEndLabel = GetBool(GetProp(line.Attributes, "DrawTextAtEndOfGridLine"), true);

            if (hasStartLabel)
            {
                Point labelCenter = SafeCopyPoint(line.StartLabel.CenterPoint);
                Point[] labelBox = GetRectanglePointsSafe(line.StartLabel.GetAxisAlignedBoundingBox());

                Vector sideDirection = isVerticalGridLine
                    ? new Vector(0.0, -1.0, 0.0)
                    : new Vector(-1.0, 0.0, 0.0);

                created += CreateNeighborTextPairAtLabel(
                    view,
                    line,
                    labelCenter,
                    labelBox,
                    currentGridPoint,
                    previousGridPoint,
                    nextGridPoint,
                    previousLabel,
                    nextLabel,
                    showPrevious,
                    showNext,
                    sideDirection,
                    xOffset,
                    yOffset);
            }

            if (hasEndLabel)
            {
                Point labelCenter = SafeCopyPoint(line.EndLabel.CenterPoint);
                Point[] labelBox = GetRectanglePointsSafe(line.EndLabel.GetAxisAlignedBoundingBox());

                Vector sideDirection = isVerticalGridLine
                    ? new Vector(0.0, 1.0, 0.0)
                    : new Vector(1.0, 0.0, 0.0);

                created += CreateNeighborTextPairAtLabel(
                    view,
                    line,
                    labelCenter,
                    labelBox,
                    currentGridPoint,
                    previousGridPoint,
                    nextGridPoint,
                    previousLabel,
                    nextLabel,
                    showPrevious,
                    showNext,
                    sideDirection,
                    xOffset,
                    yOffset);
            }
        }
        catch
        {
        }

        return created;
    }

    private static int CreateNeighborTextPairAtLabel(
        View view,
        Tekla.Structures.Drawing.GridLine line,
        Point labelCenter,
        Point[] labelBox,
        Point currentGridPoint,
        Point previousGridPoint,
        Point nextGridPoint,
        string previousLabel,
        string nextLabel,
        bool showPrevious,
        bool showNext,
        Vector sideDirection,
        double xOffset,
        double yOffset)
    {
        int created = 0;

        if (view == null || line == null || labelCenter == null || currentGridPoint == null)
            return 0;

        try
        {
            Point sheetDelta = new Point(labelCenter.X - currentGridPoint.X, labelCenter.Y - currentGridPoint.Y, 0.0);

            Point previousBase = null;
            Point nextBase = null;

            if (previousGridPoint != null)
                previousBase = new Point(previousGridPoint.X + sheetDelta.X, previousGridPoint.Y + sheetDelta.Y, 0.0);

            if (nextGridPoint != null)
                nextBase = new Point(nextGridPoint.X + sheetDelta.X, nextGridPoint.Y + sheetDelta.Y, 0.0);

            if (showPrevious && previousBase != null && !string.IsNullOrEmpty(previousLabel))
            {
                Point pos = GetNeighborMarkPoint(labelCenter, previousBase, sideDirection, xOffset, yOffset, view);
                if (InsertNeighborTextAndLeader(view, line, previousLabel, labelCenter, labelBox, pos))
                    created++;
            }

            if (showNext && nextBase != null && !string.IsNullOrEmpty(nextLabel))
            {
                Point pos = GetNeighborMarkPoint(labelCenter, nextBase, sideDirection, xOffset, yOffset, view);
                if (InsertNeighborTextAndLeader(view, line, nextLabel, labelCenter, labelBox, pos))
                    created++;
            }
        }
        catch
        {
        }

        return created;
    }

    private static Point GetNeighborMarkPoint(Point labelCenter, Point neighborBase, Vector sideDirection, double xOffset, double yOffset, View view)
    {
        try
        {
            double scale = 1.0;
            try
            {
                if (view != null && view.Attributes != null)
                    scale = view.Attributes.Scale;
            }
            catch
            {
                scale = 1.0;
            }

            if (scale <= 0.0)
                scale = 1.0;

            double x = xOffset * scale;
            double y = yOffset * scale;

            // GIỐNG TOOL GỐC:
            // X offset không đặt mark tại trục lân cận thật.
            // Nó đặt mark cách label grid hiện tại một đoạn X theo hướng về trục lân cận.
            LineSegment seg = new LineSegment(labelCenter, neighborBase);
            Vector dir = seg.GetDirectionVector();

            if (seg.Length() < 0.001)
                dir = new Vector(1.0, 0.0, 0.0);

            Point basePoint = labelCenter + (Point)(object)(x * dir);

            Vector side = sideDirection;
            try { side.Normalize(); } catch { }

            return basePoint + (Point)(object)(y * side);
        }
        catch
        {
            return new Point(labelCenter.X, labelCenter.Y, 0.0);
        }
    }

    private static bool InsertNeighborTextAndLeader(
        View view,
        Tekla.Structures.Drawing.GridLine line,
        string label,
        Point gridLabelPoint,
        Point[] gridLabelBox,
        Point textPoint)
    {
        try
        {
            if (view == null || string.IsNullOrEmpty(label) || textPoint == null)
                return false;

            TextAttributes attr = CreateNeighborTextAttributes(line);

            Text text = new Text((ViewBase)(object)view, textPoint, label, attr);
            bool textOk = false;
            try { textOk = ((DatabaseObject)text).Insert(); }
            catch { textOk = false; }

            if (!textOk)
                return false;

            try { ((DatabaseObject)text).SetUserProperty(PHU_NG_UDA_FIELD, PHU_NG_UDA_VALUE_GRID); }
            catch { }

            Point end = textPoint;
            Point start = gridLabelPoint;

            try
            {
                Point[] textBox = GetRectanglePointsSafe(text.Attributes.Frame.GetAxisAlignedBoundingBox());
                Point p1 = IntersectLineWithRectangle(new LineSegment(gridLabelPoint, textPoint), gridLabelBox, textPoint);
                Point p2 = IntersectLineWithRectangle(new LineSegment(gridLabelPoint, textPoint), textBox, gridLabelPoint);

                if (p1 != null) start = p1;
                if (p2 != null) end = p2;
            }
            catch
            {
            }

            if (Distance2D(start, end) > 0.01)
            {
                Tekla.Structures.Drawing.Line leader = new Tekla.Structures.Drawing.Line((ViewBase)(object)view, start, end, 0.0);
                TryCopyLineAttributes(line, leader);

                bool lineOk = false;
                try { lineOk = ((DatabaseObject)leader).Insert(); }
                catch { lineOk = false; }

                if (lineOk)
                {
                    try { ((DatabaseObject)leader).SetUserProperty(PHU_NG_UDA_FIELD, PHU_NG_UDA_VALUE_GRID); }
                    catch { }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextAttributes CreateNeighborTextAttributes(Tekla.Structures.Drawing.GridLine line)
    {
        TextAttributes attr = new TextAttributes();

        try
        {
            attr.PreferredPlacing = PreferredTextPlacingTypes.PointPlacingType();
        }
        catch
        {
        }

        try
        {
            // Tool gốc lấy Font + Frame qua DrawingGridProperties.
            // Nguồn chính xác nhất là Grid label đang có sẵn trên drawing, không tự dựng style mới.
            object copied = null;

            copied = TryCopyTextStyleFromGridLabel(GetProp(line, "StartLabel"), attr);
            if (copied == null)
                copied = TryCopyTextStyleFromGridLabel(GetProp(line, "EndLabel"), attr);

            // Fallback: lấy từ GridLine.Attributes giống decompile DrawingGridProperties.
            if (copied == null)
            {
                object lineAttr = GetProp(line, "Attributes");
                CopyTextStyleMembers(lineAttr, attr);
            }

            // Chốt lại các thuộc tính tool gốc đang dùng/ảnh dump yêu cầu:
            // font đen, height nếu chưa có thì 4, frame round/line màu đen, background opaque nếu API expose.
            object font = GetProp(attr, "Font");
            if (font != null)
            {
                TrySetEnumByName(font, "Color", "Black");
                TrySetProp(font, "Name", "MS UI Gothic");
                TrySetProp(font, "FontName", "MS UI Gothic");
                TrySetProp(font, "Height", 4.0);
            }

            object frame = GetProp(attr, "Frame");
            if (frame != null)
            {
                // Ưu tiên Round đúng như property ảnh; nếu enum của Tekla không có Round thì giữ style đã copy.
                TrySetEnumByName(frame, "Type", "Round");
                TrySetEnumByName(frame, "Color", "Black");
            }

            object bg = GetProp(attr, "Background");
            if (bg != null)
                TrySetEnumValueByName(attr, "Background", "Opaque");
        }
        catch
        {
        }

        return attr;
    }

    private static object TryCopyTextStyleFromGridLabel(object labelObj, TextAttributes target)
    {
        try
        {
            if (labelObj == null || target == null)
                return null;

            object labelAttr = GetProp(labelObj, "Attributes");
            if (labelAttr != null)
            {
                CopyTextStyleMembers(labelAttr, target);
                return labelAttr;
            }

            // Một số bản Tekla expose Font / Frame trực tiếp trên label.
            CopyTextStyleMembers(labelObj, target);
            return labelObj;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyTextStyleMembers(object source, TextAttributes target)
    {
        try
        {
            if (source == null || target == null)
                return;

            object font = GetProp(source, "Font");
            if (font != null)
                TrySetProp(target, "Font", font);

            object frame = GetProp(source, "Frame");
            if (frame != null)
                TrySetProp(target, "Frame", frame);

            object placing = GetProp(source, "PreferredPlacing");
            if (placing != null)
                TrySetProp(target, "PreferredPlacing", placing);

            object background = GetProp(source, "Background");
            if (background != null)
                TrySetProp(target, "Background", background);
        }
        catch
        {
        }
    }

    private static void TryCopyLineAttributes(Tekla.Structures.Drawing.GridLine source, Tekla.Structures.Drawing.Line target)
    {
        try
        {
            if (target == null || target.Attributes == null)
                return;

            object targetLine = GetProp(target.Attributes, "Line");

            // Tool gốc dùng GridLineProperties.GridLineType; mặc định lấy từ GridLine.Attributes.Line.
            if (source != null)
            {
                object srcAttr = GetProp(source, "Attributes");
                object srcLine = GetProp(srcAttr, "Line");

                if (srcLine != null)
                    TrySetProp(target.Attributes, "Line", srcLine);

                targetLine = GetProp(target.Attributes, "Line");
            }

            // Chốt màu đen theo ảnh property Line.
            if (targetLine != null)
            {
                TrySetEnumByName(targetLine, "Color", "Black");

                // Ép Line type L04 giống cấu hình XKITLINE04 trong file AllPartsDeleted.
                ApplyNeighborGridLineTypeL04(target.Attributes, targetLine);
            }

            object arrow = GetProp(target.Attributes, "Arrow");
            if (arrow != null)
            {
                TrySetEnumByName(arrow, "Position", "None");
            }
        }
        catch
        {
        }
    }

    private static void ApplyNeighborGridLineTypeL04(object lineAttributes, object lineObject)
    {
        try
        {
            if (lineAttributes != null)
            {
                TryLoadNeighborGridAttributes(lineAttributes, "XKITLINE04");
                TrySetProp(lineAttributes, "AttributeFilename", "XKITLINE04");
                SetStringFieldNeighborGrid(lineAttributes, "AttributeFilename", "XKITLINE04");
            }

            if (lineObject != null)
            {
                TrySetEnumByName(lineObject, "Color", "Black");
                SetEnumFieldNeighborGrid(lineObject, "_Color", "Black");

                SetLineTypeL04OnObject(lineObject);

                object typeObj = GetProp(lineObject, "Type");
                SetLineTypeL04OnObject(typeObj);

                TrySetLineTypePropertyL04(lineObject, "Type");
            }
        }
        catch
        {
        }
    }

    private static void TryLoadNeighborGridAttributes(object target, string attributeName)
    {
        if (target == null || string.IsNullOrEmpty(attributeName))
            return;

        try
        {
            MethodInfo m = target.GetType().GetMethod(
                "LoadAttributes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string) },
                null);

            if (m != null)
            {
                m.Invoke(target, new object[] { attributeName });
                return;
            }
        }
        catch
        {
        }

        try
        {
            MethodInfo m = target.GetType().GetMethod(
                "Load",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] { typeof(string) },
                null);

            if (m != null)
                m.Invoke(target, new object[] { attributeName });
        }
        catch
        {
        }
    }

    private static void TrySetLineTypePropertyL04(object target, string propName)
    {
        if (target == null || string.IsNullOrEmpty(propName))
            return;

        try
        {
            PropertyInfo p = target.GetType().GetProperty(
                propName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p == null || !p.CanWrite || p.GetIndexParameters().Length > 0)
                return;

            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

            if (t.IsEnum)
            {
                object v = GetLineTypeL04EnumValue(t);
                if (v != null)
                    p.SetValue(target, v, null);
                return;
            }

            object current = null;
            try { current = p.GetValue(target, null); }
            catch { current = null; }

            if (current != null)
                SetLineTypeL04OnObject(current);
        }
        catch
        {
        }
    }

    private static void SetLineTypeL04OnObject(object target)
    {
        if (target == null)
            return;

        SetLineTypeFieldL04(target, "_LineType");

        object typeObj = GetProp(target, "Type");
        if (typeObj != null && !object.ReferenceEquals(typeObj, target))
            SetLineTypeFieldL04(typeObj, "_LineType");
    }

    private static void SetLineTypeFieldL04(object target, string fieldName)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (f == null)
                return;

            Type t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;

            if (t.IsEnum)
            {
                object v = GetLineTypeL04EnumValue(t);
                if (v != null)
                    f.SetValue(target, v);
                return;
            }

            object current = null;
            try { current = f.GetValue(target); }
            catch { current = null; }

            if (current != null && !object.ReferenceEquals(current, target))
                SetLineTypeL04OnObject(current);
        }
        catch
        {
        }
    }

    private static object GetLineTypeL04EnumValue(Type enumType)
    {
        if (enumType == null || !enumType.IsEnum)
            return null;

        try
        {
            foreach (string name in Enum.GetNames(enumType))
            {
                if (string.Equals(name, "XKITLINE04", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "L04", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("XKITLINE04", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LINE04", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Enum.Parse(enumType, name);
                }
            }
        }
        catch
        {
        }

        try
        {
            return Enum.ToObject(enumType, 4);
        }
        catch
        {
            return null;
        }
    }

    private static void SetStringFieldNeighborGrid(object target, string fieldName, string value)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (f != null && f.FieldType == typeof(string))
                f.SetValue(target, value);
        }
        catch
        {
        }
    }

    private static void SetEnumFieldNeighborGrid(object target, string fieldName, string enumName)
    {
        if (target == null || string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(enumName))
            return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (f == null)
                return;

            Type t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
            if (!t.IsEnum)
                return;

            foreach (string name in Enum.GetNames(t))
            {
                if (string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    f.SetValue(target, Enum.Parse(t, name));
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private static Point GetModelGridPlanePointInView(Tekla.Structures.Model.Grid modelGrid, string label, View view)
    {
        try
        {
            if (modelGrid == null || string.IsNullOrEmpty(label) || view == null)
                return null;

            Tekla.Structures.Model.ModelObjectEnumerator children = ((Tekla.Structures.Model.ModelObject)modelGrid).GetChildren();
            while (children != null && children.MoveNext())
            {
                Tekla.Structures.Model.GridPlane plane = children.Current as Tekla.Structures.Model.GridPlane;
                if (plane == null)
                    continue;

                if (plane.Label != label)
                    continue;

                Point origin = plane.Plane.Origin;
                return MatrixFactory.ToCoordinateSystem(view.ViewCoordinateSystem).Transform(origin);
            }
        }
        catch
        {
        }

        return null;
    }

    private static string GetGridLineModelLabel(Tekla.Structures.Drawing.GridLine line, Tekla.Structures.Model.Model model)
    {
        try
        {
            if (line == null || model == null)
                return "";

            object modelIdentifier = GetProp(line, "ModelIdentifier");
            Tekla.Structures.Identifier id = modelIdentifier as Tekla.Structures.Identifier;
            if (id == null)
                return "";

            Tekla.Structures.Model.ModelObject obj = model.SelectModelObject(id);
            Tekla.Structures.Model.GridPlane plane = obj as Tekla.Structures.Model.GridPlane;
            if (plane != null)
                return plane.Label;
        }
        catch
        {
        }

        return "";
    }

    private static string GetGridLineDrawingLabel(Tekla.Structures.Drawing.GridLine line)
    {
        try
        {
            object startLabel = GetProp(line, "StartLabel");
            object endLabel = GetProp(line, "EndLabel");

            object a = GetProp(startLabel, "GridLabelText");
            if (a != null && !string.IsNullOrEmpty(a.ToString()))
                return a.ToString();

            object b = GetProp(endLabel, "GridLabelText");
            if (b != null && !string.IsNullOrEmpty(b.ToString()))
                return b.ToString();
        }
        catch
        {
        }

        return "";
    }

    private static Point SafeCopyPoint(Point p)
    {
        if (p == null)
            return null;

        try { return new Point(p.X, p.Y, p.Z); }
        catch { return null; }
    }

    private static Point[] GetRectanglePointsSafe(RectangleBoundingBox box)
    {
        Point[] pts = new Point[4];

        try
        {
            if (box == null)
                return pts;

            pts[0] = box.LowerLeft;
            pts[1] = box.LowerRight;
            pts[2] = box.UpperLeft;
            pts[3] = box.UpperRight;
        }
        catch
        {
        }

        return pts;
    }

    private static Point IntersectLineWithRectangle(LineSegment line, Point[] rect, Point referencePoint)
    {
        try
        {
            if (line == null || rect == null || rect.Length < 4)
                return null;

            if (rect[0] == null || rect[1] == null || rect[2] == null || rect[3] == null)
                return null;

            Point best = null;
            double bestDistance = double.MaxValue;

            TryIntersectRectEdge(line, rect[0], rect[1], referencePoint, ref best, ref bestDistance);
            TryIntersectRectEdge(line, rect[1], rect[3], referencePoint, ref best, ref bestDistance);
            TryIntersectRectEdge(line, rect[3], rect[2], referencePoint, ref best, ref bestDistance);
            TryIntersectRectEdge(line, rect[2], rect[0], referencePoint, ref best, ref bestDistance);

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static void TryIntersectRectEdge(
        LineSegment baseLine,
        Point a,
        Point b,
        Point referencePoint,
        ref Point best,
        ref double bestDistance)
    {
        try
        {
            LineSegment edge = new LineSegment(a, b);
            LineSegment intersection = Intersection.LineToLine(new Tekla.Structures.Geometry3d.Line(baseLine), new Tekla.Structures.Geometry3d.Line(edge));

            if (intersection == null || intersection.Point1 == null)
                return;

            Point p = intersection.Point1;
            if (!IsPointOnSegment(p, edge))
                return;

            double d = Distance2D(p, referencePoint);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
        catch
        {
        }
    }

    private static bool IsPointOnSegment(Point p, LineSegment seg)
    {
        try
        {
            return Distance.PointToLineSegment(p, seg) <= 0.1;
        }
        catch
        {
            return false;
        }
    }

    private static double Distance2D(Point a, Point b)
    {
        if (a == null || b == null)
            return 0.0;

        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool GetBool(object obj, bool fallback)
    {
        try
        {
            if (obj is bool)
                return (bool)obj;

            if (obj == null)
                return fallback;

            return Convert.ToBoolean(obj);
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddUniqueString(List<string> list, string value)
    {
        if (list == null || string.IsNullOrEmpty(value))
            return;

        if (!list.Contains(value))
            list.Add(value);
    }

    private static bool TrySetEnumByName(object obj, string propName, string enumName)
    {
        try
        {
            if (obj == null || string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(enumName))
                return false;

            PropertyInfo p = obj.GetType().GetProperty(
                propName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p == null || !p.CanWrite || p.GetIndexParameters().Length > 0)
                return false;

            Type t = p.PropertyType;
            if (!t.IsEnum)
                return false;

            object value = Enum.Parse(t, enumName, true);
            p.SetValue(obj, value, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetEnumValueByName(object obj, string propName, string enumName)
    {
        return TrySetEnumByName(obj, propName, enumName);
    }

    private static bool TrySetProp(object obj, string name, object value)
    {
        try
        {
            if (obj == null || string.IsNullOrEmpty(name))
                return false;

            PropertyInfo p = obj.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (p == null || !p.CanWrite || p.GetIndexParameters().Length > 0)
                return false;

            p.SetValue(obj, value, null);
            return true;
        }
        catch
        {
            return false;
        }
    }


}
