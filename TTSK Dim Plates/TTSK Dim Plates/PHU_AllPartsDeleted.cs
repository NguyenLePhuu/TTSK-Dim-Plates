// PHU_AllPartsDeletedMarker.cs
// Tekla Structures 2025+ / .NET Framework 4.8
// Purpose: When CHANGES == "All Parts Deleted", clear views and mark drawing with red X + MARK text.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

public static class PHU_AllPartsDeletedMarker
{
    private const double DefaultLeftMargin = 10.0;
    private const double DefaultRightMargin = 10.0;
    private const double DefaultBottomMargin = 10.0;
    private const double DefaultTopMargin = 10.0;
    private const double TextAboveCenter = 80.0;

    public static bool IsAllPartsDeleted(string changes)
    {
        return string.Equals((changes ?? string.Empty).Trim(), "All Parts Deleted", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Run(string mark)
    {
        return Run(mark, DefaultLeftMargin, DefaultRightMargin, DefaultBottomMargin, DefaultTopMargin);
    }

    public static bool Run(string mark, double leftMargin, double rightMargin, double bottomMargin, double topMargin)
    {
        DrawingHandler dh = new DrawingHandler();
        if (!dh.GetConnectionStatus()) return false;

        Drawing drawing = dh.GetActiveDrawing();
        if (drawing == null) return false;

        return Run(drawing, mark, leftMargin, rightMargin, bottomMargin, topMargin);
    }

    public static bool Run(Drawing drawing, string mark)
    {
        return Run(drawing, mark, DefaultLeftMargin, DefaultRightMargin, DefaultBottomMargin, DefaultTopMargin);
    }

    public static bool Run(Drawing drawing, string mark, double leftMargin, double rightMargin, double bottomMargin, double topMargin)
    {
        if (drawing == null) return false;

        ContainerView sheet = drawing.GetSheet();
        if (sheet == null) return false;

        // Tìm khung trong trước khi vẽ X. Sau khi xóa view, title/frame vẫn còn nên vẫn quét được.
        double paperW, paperH;
        GetPaperSize(drawing, sheet, out paperW, out paperH);

        UsableRect rect;

        // Ưu tiên khổ giấy đã chốt để 4 đầu line bắt đúng góc khung trong:
        // 420 x 297: Trái/Trên/Dưới = 5, Phải = 15
        // 841 x 594: Trái/Trên/Dưới = 10, Phải = 17.3
        // Các khổ khác mới quay về cách dò khung trong như file nền.
        if (!TryGetReservedRectByPaperSize(paperW, paperH, out rect))
        {
            if (!TryFindInnerFrameRect(sheet, paperW, paperH, out rect))
            {
                rect = new UsableRect();
                rect.Left = 5.0;
                rect.Bottom = 5.0;
                rect.Right = Math.Max(rect.Left + 1.0, paperW - 15.0);
                rect.Top = Math.Max(rect.Bottom + 1.0, paperH - 5.0);
            }
        }

        DeleteAllViews(sheet);
        DeleteAllSheetLinesAndTexts(sheet);

        // Điểm neo line phải nằm đúng trên khung trong màu xanh.
        // Nếu đã dò được khung trong thì dùng trực tiếp 4 cạnh đó, không cộng reserve lần nữa.
        // Nếu không dò được, rect fallback bên trên đã được tính theo khoảng hở:
        // Trên = 5, Dưới = 5, Trái = 5, Phải = 15.
        double xLeft = rect.Left;
        double xRight = rect.Right;
        double yBottom = rect.Bottom;
        double yTop = rect.Top;

        if (xRight <= xLeft)
        {
            xLeft = rect.Left;
            xRight = rect.Right;
        }

        if (yTop <= yBottom)
        {
            yBottom = rect.Bottom;
            yTop = rect.Top;
        }

        Point pBottomLeft = new Point(xLeft, yBottom, 0);
        Point pTopRight = new Point(xRight, yTop, 0);
        Point pTopLeft = new Point(xLeft, yTop, 0);
        Point pBottomRight = new Point(xRight, yBottom, 0);

        InsertRedDashDotLine(sheet, pBottomLeft, pTopRight);
        InsertRedDashDotLine(sheet, pTopLeft, pBottomRight);

        double cx = (xLeft + xRight) / 2.0;
        double cy = (yBottom + yTop) / 2.0;

        bool isAssembly = drawing is AssemblyDrawing;
        string cleanMark = CleanMarkText(mark);
        double textAboveCenter = GetTextAboveCenterByPaperSize(paperW, paperH);
        InsertMarkText(sheet, new Point(cx, cy + textAboveCenter, 0), cleanMark + "  ", isAssembly);

        drawing.CommitChanges();
        return true;
    }

    private class UsableRect
    {
        public double Left;
        public double Right;
        public double Bottom;
        public double Top;
    }


    private static double GetTextAboveCenterByPaperSize(double paperW, double paperH)
    {
        // A3 420 x 297: giữ nguyên vị trí đã chạy ổn.
        if (IsPaperSize(paperW, paperH, 420.0, 297.0))
            return 80.0;

        // A1 841 x 594: khung lớn hơn, nếu giữ 80 thì 2 đường chéo đi ngang khung text.
        // Dịch text lên cao hơn để tránh bị line che.
        if (IsPaperSize(paperW, paperH, 841.0, 594.0))
            return 150.0;

        return TextAboveCenter;
    }

    private static bool TryGetReservedRectByPaperSize(double paperW, double paperH, out UsableRect rect)
    {
        rect = null;

        double w = Math.Round(paperW, 1);
        double h = Math.Round(paperH, 1);

        // Chấp nhận sai số nhỏ vì Tekla có thể trả 420.0 / 419.999...
        if (IsPaperSize(paperW, paperH, 420.0, 297.0))
        {
            rect = BuildReservedRect(paperW, paperH, 5.0, 15.0, 5.0, 5.0);
            return true;
        }

        if (IsPaperSize(paperW, paperH, 841.0, 594.0))
        {
            rect = BuildReservedRect(paperW, paperH, 10.0, 17.3, 10.0, 10.0);
            return true;
        }

        return false;
    }

    private static bool IsPaperSize(double paperW, double paperH, double targetW, double targetH)
    {
        const double tol = 2.0;

        bool same = Math.Abs(paperW - targetW) <= tol && Math.Abs(paperH - targetH) <= tol;
        bool swapped = Math.Abs(paperW - targetH) <= tol && Math.Abs(paperH - targetW) <= tol;

        return same || swapped;
    }

    private static UsableRect BuildReservedRect(
        double paperW,
        double paperH,
        double leftReserve,
        double rightReserve,
        double bottomReserve,
        double topReserve)
    {
        UsableRect rect = new UsableRect();
        rect.Left = leftReserve;
        rect.Bottom = bottomReserve;
        rect.Right = Math.Max(rect.Left + 1.0, paperW - rightReserve);
        rect.Top = Math.Max(rect.Bottom + 1.0, paperH - topReserve);
        return rect;
    }


    private static void DeleteAllViews(ContainerView sheet)
    {
        DrawingObjectEnumerator e = sheet.GetAllObjects(typeof(View));
        ArrayList views = new ArrayList();

        while (e.MoveNext())
        {
            View v = e.Current as View;
            if (v != null) views.Add(v);
        }

        foreach (View v in views)
        {
            try { v.Delete(); }
            catch { }
        }
    }

    private static void DeleteAllSheetLinesAndTexts(ContainerView sheet)
    {
        try { DeleteAllSheetObjects(sheet, typeof(Tekla.Structures.Drawing.Line)); }
        catch { }

        try { DeleteAllSheetObjects(sheet, typeof(Text)); }
        catch { }
    }

    private static void DeleteAllSheetObjects(ContainerView sheet, Type objectType)
    {
        DrawingObjectEnumerator e = sheet.GetAllObjects(objectType);
        ArrayList objects = new ArrayList();

        while (e.MoveNext())
        {
            DrawingObject drawingObject = e.Current as DrawingObject;
            if (drawingObject != null) objects.Add(drawingObject);
        }

        foreach (DrawingObject drawingObject in objects)
        {
            try { drawingObject.Delete(); }
            catch { }
        }
    }

    private static void InsertRedDashDotLine(ContainerView sheet, Point a, Point b)
    {
        Tekla.Structures.Drawing.Line.LineAttributes attr = new Tekla.Structures.Drawing.Line.LineAttributes();

        // Ép đúng cấu hình line theo yêu cầu:
        // Color = Red, Line type/style = giá trị 4, Arrow position = None.
        ApplyDeletedLineAttributes(attr);

        Tekla.Structures.Drawing.Line line = new Tekla.Structures.Drawing.Line(sheet, a, b, 0.0, attr);
        line.Insert();

        // Một số môi trường Tekla không ăn LineType nếu chỉ set trước Insert,
        // nên set lại trực tiếp lên object sau Insert rồi Modify.
        try
        {
            object insertedAttr = GetProp(line, "Attributes");
            ApplyDeletedLineAttributes(insertedAttr);
            line.Modify();
        }
        catch
        {
        }
    }

    private static void ApplyDeletedLineAttributes(object attr)
    {
        if (attr == null)
            return;

        // Tekla 2025 SP7 thực tế lưu line type tại:
        // LineAttributes.Line.Type._LineType
        // và LineAttributes.Line._LineType
        // Dump của bạn cho thấy AttributeFilename = XKITLINE04 nhưng Type vẫn = SolidLine,
        // nên chỉ LoadAttributes/AttributeFilename là chưa đủ. Phải ép trực tiếp nested field này.

        TryLoadAttributes(attr, "XKITLINE04");
        SetStringField(attr, "AttributeFilename", "XKITLINE04");

        object lineAttr = GetProp(attr, "Line");
        if (lineAttr != null)
        {
            // Color = Red
            SetEnumProp(lineAttr, "Color", "Red");
            SetEnumField(lineAttr, "_Color", "Red");

            // Cách 1: ép field _LineType trực tiếp trên LineTypeAttributes.
            // Dump: FIELD _LineType : Tekla.Structures.Drawing.LineTypes = SolidLine
            SetLineTypeFieldTo3OrXkit(lineAttr, "_LineType");

            // Cách 2: ép sâu object Line.Type.
            // Dump: PROP Type : Tekla.Structures.Drawing.LineTypes = SolidLine
            //       Type TYPE = Tekla.Structures.Drawing.NormalLineType
            //       FIELD _LineType : Tekla.Structures.Drawing.LineTypesEnum = SolidLine
            object typeObj = GetProp(lineAttr, "Type");
            SetLineTypeFieldTo3OrXkit(typeObj, "_LineType");

            // Cách 3: nếu property Type có setter và nhận enum/object thì thử set bằng index 3.
            SetLineTypePropTo3OrXkit(lineAttr, "Type");
        }

        object arrow = GetProp(attr, "Arrowhead");
        if (arrow != null)
        {
            // Dump đúng property là ArrowPosition / Head / Height / Width.
            SetEnumProp(arrow, "ArrowPosition", "None");
            SetEnumField(arrow, "arrowPosition", "None");
            SetDoubleProp(arrow, "Height", 2.0);
            SetDoubleProp(arrow, "Width", 3.0);
        }
    }

    private static void SetLineTypePropTo3OrXkit(object target, string propName)
    {
        if (target == null) return;

        try
        {
            PropertyInfo p = target.GetType().GetProperty(
                propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (p == null || !p.CanWrite)
                return;

            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

            if (t.IsEnum)
            {
                object v = GetEnumValueByNameOrNumber(t, "XKITLINE04", 4);
                if (v != null) p.SetValue(target, v, null);
                return;
            }

            // Nếu Type là class LineTypes/NormalLineType, thử lấy object hiện tại rồi ép field bên trong.
            object current = null;
            try { current = p.GetValue(target, null); } catch { }
            if (current != null)
                SetLineTypeFieldTo3OrXkit(current, "_LineType");
        }
        catch
        {
        }
    }

    private static void SetLineTypeFieldTo3OrXkit(object target, string fieldName)
    {
        if (target == null) return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (f == null)
                return;

            Type t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;

            if (t.IsEnum)
            {
                object v = GetEnumValueByNameOrNumber(t, "XKITLINE04", 4);
                if (v != null) f.SetValue(target, v);
                return;
            }

            // Có trường hợp field type là wrapper class Tekla.Structures.Drawing.LineTypes.
            // Khi đó field hiện tại thường là NormalLineType, ta ép sâu field _LineType của object hiện tại.
            object current = null;
            try { current = f.GetValue(target); } catch { }
            if (current != null && !object.ReferenceEquals(current, target))
                SetLineTypeFieldTo3OrXkit(current, "_LineType");
        }
        catch
        {
        }
    }

    private static object GetEnumValueByNameOrNumber(Type enumType, string preferredName, int number)
    {
        if (enumType == null || !enumType.IsEnum)
            return null;

        try
        {
            foreach (string name in Enum.GetNames(enumType))
            {
                if (string.Equals(name, preferredName, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse(enumType, name);
            }
        }
        catch
        {
        }

        try
        {
            foreach (string name in Enum.GetNames(enumType))
            {
                if (name.IndexOf("XKITLINE04", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("LINE04", StringComparison.OrdinalIgnoreCase) >= 0)
                    return Enum.Parse(enumType, name);
            }
        }
        catch
        {
        }

        try
        {
            return Enum.ToObject(enumType, number);
        }
        catch
        {
            return null;
        }
    }

    private static void SetStringField(object target, string fieldName, string value)
    {
        if (target == null) return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (f != null && f.FieldType == typeof(string))
                f.SetValue(target, value);
        }
        catch
        {
        }
    }

    private static void SetEnumField(object target, string fieldName, string enumName)
    {
        if (target == null) return;

        try
        {
            FieldInfo f = target.GetType().GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (f == null)
                return;

            Type t = Nullable.GetUnderlyingType(f.FieldType) ?? f.FieldType;
            if (!t.IsEnum)
                return;

            foreach (string n in Enum.GetNames(t))
            {
                if (string.Equals(n, enumName, StringComparison.OrdinalIgnoreCase) ||
                    n.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    f.SetValue(target, Enum.Parse(t, n));
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private static void InsertMarkText(ContainerView sheet, Point p, string markText, bool isAssembly)
    {
        Text.TextAttributes attr = new Text.TextAttributes();

        double fontHeight = isAssembly ? 30.0 : 15.0;

        object font = GetProp(attr, "Font");
        SetEnumProp(font, "Color", "Black");
        SetStringProp(font, "Name", "MS UI Gothic");
        SetStringProp(font, "FontName", "MS UI Gothic");
        SetDoubleProp(font, "Height", fontHeight);

        object frame = GetProp(attr, "Frame");
        SetEnumProp(frame, "Type", "Rectangular");
        SetEnumProp(frame, "FrameType", "Rectangular");
        SetEnumProp(frame, "Color", "Black");

        SetBoolProp(attr, "UseWordWrapping", false);
        SetDoubleProp(attr, "RulerWidth", 0.0);
        SetBoolProp(attr, "TransparentBackground", false);

        Text t = new Text(sheet, p, markText, new PointPlacing(), attr);
        t.Insert();
    }

    private static bool TryFindInnerFrameRect(ContainerView sheet, double paperW, double paperH, out UsableRect rect)
    {
        rect = null;

        try
        {
            List<double> xs = new List<double>();
            List<double> ys = new List<double>();

            DrawingObjectEnumerator e = sheet.GetAllObjects(typeof(Tekla.Structures.Drawing.Line));
            while (e.MoveNext())
            {
                Tekla.Structures.Drawing.Line ln = e.Current as Tekla.Structures.Drawing.Line;
                if (ln == null) continue;

                Point a, b;
                if (!TryGetLinePoints(ln, out a, out b)) continue;

                bool vertical = Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 20.0;
                bool horizontal = Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.X - b.X) > 20.0;

                if (vertical)
                {
                    double x = (a.X + b.X) / 2.0;
                    if (x > 0.5 && x < paperW - 0.5) AddUniqueNear(xs, x, 1.0);
                }
                else if (horizontal)
                {
                    double y = (a.Y + b.Y) / 2.0;
                    if (y > 0.5 && y < paperH - 0.5) AddUniqueNear(ys, y, 1.0);
                }
            }

            if (xs.Count < 2 || ys.Count < 2) return false;

            xs.Sort();
            ys.Sort();

            double left = xs[0];
            double right = xs[xs.Count - 1];
            double bottom = ys[0];
            double top = ys[ys.Count - 1];

            // Chỉ nhận nếu khung tìm được đủ lớn, tránh bắt nhầm title block nhỏ.
            if ((right - left) < paperW * 0.60) return false;
            if ((top - bottom) < paperH * 0.60) return false;

            rect = new UsableRect();
            rect.Left = left;
            rect.Right = right;
            rect.Bottom = bottom;
            rect.Top = top;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddUniqueNear(List<double> values, double value, double tol)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (Math.Abs(values[i] - value) <= tol) return;
        }
        values.Add(value);
    }

    private static bool TryGetLinePoints(Tekla.Structures.Drawing.Line line, out Point a, out Point b)
    {
        a = null;
        b = null;

        string[] aNames = new string[] { "StartPoint", "Start", "Point1", "FirstPoint", "P1" };
        string[] bNames = new string[] { "EndPoint", "End", "Point2", "SecondPoint", "P2" };

        for (int i = 0; i < aNames.Length; i++)
        {
            object av = GetProp(line, aNames[i]);
            object bv = GetProp(line, bNames[i]);
            a = av as Point;
            b = bv as Point;
            if (a != null && b != null) return true;
        }

        return false;
    }

    private static void GetPaperSize(Drawing drawing, ContainerView sheet, out double width, out double height)
    {
        width = 420.0;
        height = 297.0;

        object[] sources = new object[] { drawing, sheet, GetProp(drawing, "Layout"), GetProp(drawing, "DrawingAttributes"), GetProp(drawing, "Attributes") };
        string[] wNames = new string[] { "Width", "PaperWidth", "SheetWidth", "DrawingWidth" };
        string[] hNames = new string[] { "Height", "PaperHeight", "SheetHeight", "DrawingHeight" };

        foreach (object s in sources)
        {
            if (s == null) continue;
            double w, h;
            if (TryReadDoubleAny(s, wNames, out w) && TryReadDoubleAny(s, hNames, out h) && w > 50 && h > 50)
            {
                width = w;
                height = h;
                return;
            }
        }

        string paperName = Convert.ToString(GetProp(GetProp(drawing, "Layout"), "Name") ?? "").ToUpperInvariant();
        if (paperName.Contains("A1")) { width = 841; height = 594; return; }
        if (paperName.Contains("A2")) { width = 594; height = 420; return; }
        if (paperName.Contains("A3")) { width = 420; height = 297; return; }
        if (paperName.Contains("A4")) { width = 297; height = 210; return; }
    }

    private static string CleanMarkText(string mark)
    {
        if (string.IsNullOrWhiteSpace(mark))
            return string.Empty;

        mark = mark.Trim();

        // Bỏ toàn bộ ngoặc vuông đầu/cuối, kể cả trường hợp [[MARK]].
        while (mark.Length >= 2 && mark.StartsWith("[") && mark.EndsWith("]"))
        {
            mark = mark.Substring(1, mark.Length - 2).Trim();
        }

        return mark;
    }

    private static void TryLoadAttributes(object target, string attributeName)
    {
        if (target == null || string.IsNullOrWhiteSpace(attributeName))
            return;

        try
        {
            MethodInfo m = target.GetType().GetMethod(
                "LoadAttributes",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
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
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
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

    private static object GetProp(object target, string name)
    {
        if (target == null) return null;
        PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null || !p.CanRead) return null;
        try { return p.GetValue(target, null); } catch { return null; }
    }

    private static bool TryReadDoubleAny(object target, string[] names, out double value)
    {
        value = 0;
        foreach (string n in names)
        {
            object v = GetProp(target, n);
            if (v == null) continue;
            try
            {
                value = Convert.ToDouble(v);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static void SetStringProp(object target, string name, string value)
    {
        SetProp(target, name, value);
    }

    private static void SetBoolProp(object target, string name, bool value)
    {
        SetProp(target, name, value);
    }

    private static void SetDoubleProp(object target, string name, double value)
    {
        SetProp(target, name, value);
    }

    private static void SetEnumProp(object target, string name, string enumName)
    {
        if (target == null) return;
        PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null || !p.CanWrite) return;

        try
        {
            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t.IsEnum)
            {
                foreach (string n in Enum.GetNames(t))
                {
                    if (string.Equals(n, enumName, StringComparison.OrdinalIgnoreCase) ||
                        n.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        p.SetValue(target, Enum.Parse(t, n), null);
                        return;
                    }
                }
            }
        }
        catch { }
    }

    private static void SetIntOrEnumProp(object target, string name, int value)
    {
        if (target == null) return;
        PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null || !p.CanWrite) return;

        try
        {
            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

            if (t.IsEnum)
            {
                object enumValue = Enum.ToObject(t, value);
                p.SetValue(target, enumValue, null);
                return;
            }

            if (t == typeof(int) || t == typeof(short) || t == typeof(long) || t == typeof(byte))
            {
                p.SetValue(target, Convert.ChangeType(value, t), null);
                return;
            }
        }
        catch { }
    }

    private static void SetProp(object target, string name, object value)
    {
        if (target == null || value == null) return;
        PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p == null || !p.CanWrite) return;
        try
        {
            object v = value;
            Type t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (t != value.GetType()) v = Convert.ChangeType(value, t);
            p.SetValue(target, v, null);
        }
        catch { }
    }
}
