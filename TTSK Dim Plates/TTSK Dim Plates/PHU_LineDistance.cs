using System;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TTSK_AutoDim_Plates
{
    public static class PHU_LineDistance
    {
        public class Result
        {
            public bool Success;
            public double Distance;
            public string Message;

            public string ToDisplayText()
            {
                if (Success)
                    return "Đã tạo line: " + Distance.ToString("0.###") + " mm";

                return string.IsNullOrWhiteSpace(Message)
                    ? "Không tạo được line."
                    : Message;
            }
        }

        public static Result Run(double distance)
        {
            Result result = new Result();
            result.Distance = distance;

            try
            {
                if (distance <= 0.0)
                    throw new Exception("Chiều dài line phải lớn hơn 0.");

                DrawingHandler drawingHandler = new DrawingHandler();

                if (!drawingHandler.GetConnectionStatus())
                    throw new Exception("Chưa kết nối được Tekla Drawing.");

                Drawing drawing = drawingHandler.GetActiveDrawing();
                if (drawing == null)
                    throw new Exception("Hãy mở drawing trước khi chạy Line Distance.");

                object picker = GetPicker(drawingHandler);
                if (picker == null)
                    throw new Exception("Không lấy được Drawing Picker.");

                PickedPoint startPick = PickPoint(
                    picker,
                    "Chọn điểm bắt đầu line");

                if (startPick == null || startPick.View == null || startPick.Point == null)
                    throw new Exception("Không lấy được điểm bắt đầu line.");

                PickedPoint directionPick = PickPoint(
                    picker,
                    "Chọn điểm định hướng line");

                if (directionPick == null || directionPick.Point == null)
                    throw new Exception("Không lấy được điểm định hướng line.");

                Point startPoint = startPick.Point;
                Point directionPoint = directionPick.Point;

                double dx = directionPoint.X - startPoint.X;
                double dy = directionPoint.Y - startPoint.Y;
                double dz = directionPoint.Z - startPoint.Z;

                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (length < 0.0001)
                    throw new Exception("Điểm định hướng trùng điểm bắt đầu. Hãy chọn 2 điểm khác nhau.");

                double factor = distance / length;

                Point endPoint = new Point(
                    startPoint.X + dx * factor,
                    startPoint.Y + dy * factor,
                    startPoint.Z + dz * factor);

                Tekla.Structures.Drawing.Line.LineAttributes attributes =
                    BuildLineDistanceAttributes();

                Tekla.Structures.Drawing.Line line =
                    new Tekla.Structures.Drawing.Line(
                        startPick.View,
                        startPoint,
                        endPoint,
                        0.0,
                        attributes);

                if (!line.Insert())
                    throw new Exception("Tekla không insert được line.");

                // Một số môi trường Tekla chỉ ăn đủ Line Type / Color sau Insert.
                // Vì vậy ép lại Attributes trực tiếp trên object rồi Modify thêm một lần.
                try
                {
                    object insertedAttributes = GetProp(line, "Attributes");
                    ApplyLineDistanceAttributes(insertedAttributes);
                    line.Modify();
                }
                catch
                {
                }

                drawing.CommitChanges();

                result.Success = true;
                result.Message = "OK";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
        }

        public static Result RunPickTwoPointsLine()
        {
            Result result = new Result();

            try
            {
                DrawingHandler drawingHandler = new DrawingHandler();

                if (!drawingHandler.GetConnectionStatus())
                    throw new Exception("Chưa kết nối được Tekla Drawing.");

                Drawing drawing = drawingHandler.GetActiveDrawing();
                if (drawing == null)
                    throw new Exception("Hãy mở drawing trước khi chạy Line Distance.");

                object picker = GetPicker(drawingHandler);
                if (picker == null)
                    throw new Exception("Không lấy được Drawing Picker.");

                PickedPoint startPick = PickPoint(
                    picker,
                    "Chọn điểm bắt đầu line");

                if (startPick == null || startPick.View == null || startPick.Point == null)
                    throw new Exception("Không lấy được điểm bắt đầu line.");

                PickedPoint endPick = PickPoint(
                    picker,
                    "Chọn điểm kết thúc line");

                if (endPick == null || endPick.Point == null)
                    throw new Exception("Không lấy được điểm kết thúc line.");

                // Không kiểm tra object.ReferenceEquals giữa 2 ViewBase.
                // Trong Tekla Drawing Picker, cùng một view vẫn có thể trả về 2 wrapper ViewBase khác nhau,
                // khiến ReferenceEquals = false dù người dùng pick đúng trong cùng một view.
                // Line sẽ được insert theo view của điểm đầu; điểm cuối dùng đúng tọa độ picker trả về.

                Point startPoint = startPick.Point;
                Point endPoint = endPick.Point;

                double dx = endPoint.X - startPoint.X;
                double dy = endPoint.Y - startPoint.Y;
                double dz = endPoint.Z - startPoint.Z;

                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (length < 0.0001)
                    throw new Exception("Điểm kết thúc trùng điểm bắt đầu. Hãy chọn 2 điểm khác nhau.");

                Tekla.Structures.Drawing.Line.LineAttributes attributes =
                    BuildLineDistanceAttributes();

                Tekla.Structures.Drawing.Line line =
                    new Tekla.Structures.Drawing.Line(
                        startPick.View,
                        startPoint,
                        endPoint,
                        0.0,
                        attributes);

                if (!line.Insert())
                    throw new Exception("Tekla không insert được line.");

                // Một số môi trường Tekla chỉ ăn đủ Line Type / Color sau Insert.
                // Vì vậy ép lại Attributes trực tiếp trên object rồi Modify thêm một lần.
                try
                {
                    object insertedAttributes = GetProp(line, "Attributes");
                    ApplyLineDistanceAttributes(insertedAttributes);
                    line.Modify();
                }
                catch
                {
                }

                drawing.CommitChanges();

                result.Success = true;
                result.Distance = length;
                result.Message = "OK";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
        }

        public static bool InsertLineWithLineDistanceAttributes(
            ViewBase view,
            Point startPoint,
            Point endPoint)
        {
            try
            {
                if (view == null || startPoint == null || endPoint == null)
                    return false;

                double dx = endPoint.X - startPoint.X;
                double dy = endPoint.Y - startPoint.Y;
                double dz = endPoint.Z - startPoint.Z;

                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < 0.0001)
                    return false;

                Tekla.Structures.Drawing.Line.LineAttributes attributes =
                    BuildLineDistanceAttributes();

                Tekla.Structures.Drawing.Line line =
                    new Tekla.Structures.Drawing.Line(
                        view,
                        startPoint,
                        endPoint,
                        0.0,
                        attributes);

                if (!line.Insert())
                    return false;

                try
                {
                    object insertedAttributes = GetProp(line, "Attributes");
                    ApplyLineDistanceAttributes(insertedAttributes);
                    line.Modify();
                }
                catch
                {
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Tekla.Structures.Drawing.Line.LineAttributes BuildLineDistanceAttributes()
        {
            Tekla.Structures.Drawing.Line.LineAttributes attributes =
                new Tekla.Structures.Drawing.Line.LineAttributes();

            ApplyLineDistanceAttributes(attributes);
            return attributes;
        }

        private static void ApplyLineDistanceAttributes(object attributes)
        {
            if (attributes == null)
                return;

            // Theo Line property bạn gửi:
            // Line type = XKITLINE04 giống file PHU_AllPartsDeleted, Color = Black, Bulge = 0.00,
            // Arrow Position = None, Arrow height = 2.0, Arrow length = 3.0.
            TryLoadAttributes(attributes, "XKITLINE04");
            SetStringField(attributes, "AttributeFilename", "XKITLINE04");
            SetDoubleProp(attributes, "Bulge", 0.0);

            object lineAttr = GetProp(attributes, "Line");
            if (lineAttr != null)
            {
                SetEnumProp(lineAttr, "Color", "Black");
                SetEnumField(lineAttr, "_Color", "Black");

                SetLineTypeField(lineAttr, "_LineType", "XKITLINE04", 4);

                object typeObj = GetProp(lineAttr, "Type");
                SetLineTypeField(typeObj, "_LineType", "XKITLINE04", 4);
                SetLineTypeProp(lineAttr, "Type", "XKITLINE04", 4);
            }

            object arrow = GetProp(attributes, "Arrowhead");
            if (arrow != null)
            {
                SetEnumProp(arrow, "ArrowPosition", "None");
                SetEnumField(arrow, "arrowPosition", "None");
                SetDoubleProp(arrow, "Height", 2.0);
                SetDoubleProp(arrow, "Width", 3.0);
                SetDoubleProp(arrow, "Length", 3.0);
            }
        }

        private static void SetLineTypeProp(object target, string propName, string preferredName, int number)
        {
            if (target == null)
                return;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    propName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property == null || !property.CanWrite)
                    return;

                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (type.IsEnum)
                {
                    object value = GetEnumValueByNameOrNumber(type, preferredName, number);
                    if (value != null)
                        property.SetValue(target, value, null);

                    return;
                }

                object current = null;
                try { current = property.GetValue(target, null); } catch { }

                if (current != null)
                    SetLineTypeField(current, "_LineType", preferredName, number);
            }
            catch
            {
            }
        }

        private static void SetLineTypeField(object target, string fieldName, string preferredName, int number)
        {
            if (target == null)
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                    return;

                Type type = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;

                if (type.IsEnum)
                {
                    object value = GetEnumValueByNameOrNumber(type, preferredName, number);
                    if (value != null)
                        field.SetValue(target, value);

                    return;
                }

                object current = null;
                try { current = field.GetValue(target); } catch { }

                if (current != null && !object.ReferenceEquals(current, target))
                    SetLineTypeField(current, "_LineType", preferredName, number);
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
                        name.IndexOf("LINE03", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("XT03", StringComparison.OrdinalIgnoreCase) >= 0)
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
                return Enum.ToObject(enumType, number);
            }
            catch
            {
                return null;
            }
        }

        private static void TryLoadAttributes(object target, string attributeName)
        {
            if (target == null || string.IsNullOrWhiteSpace(attributeName))
                return;

            try
            {
                MethodInfo method = target.GetType().GetMethod(
                    "LoadAttributes",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (method != null)
                {
                    method.Invoke(target, new object[] { attributeName });
                    return;
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo method = target.GetType().GetMethod(
                    "Load",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (method != null)
                    method.Invoke(target, new object[] { attributeName });
            }
            catch
            {
            }
        }

        private static object GetProp(object target, string name)
        {
            if (target == null)
                return null;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property == null || !property.CanRead)
                    return null;

                return property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static void SetStringField(object target, string fieldName, string value)
        {
            if (target == null)
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null && field.FieldType == typeof(string))
                    field.SetValue(target, value);
            }
            catch
            {
            }
        }

        private static void SetEnumField(object target, string fieldName, string enumName)
        {
            if (target == null)
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(
                    fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (field == null)
                    return;

                Type type = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                if (!type.IsEnum)
                    return;

                foreach (string name in Enum.GetNames(type))
                {
                    if (string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase) ||
                        name.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        field.SetValue(target, Enum.Parse(type, name));
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static void SetEnumProp(object target, string name, string enumName)
        {
            if (target == null)
                return;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property == null || !property.CanWrite)
                    return;

                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!type.IsEnum)
                    return;

                foreach (string enumValue in Enum.GetNames(type))
                {
                    if (string.Equals(enumValue, enumName, StringComparison.OrdinalIgnoreCase) ||
                        enumValue.IndexOf(enumName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        property.SetValue(target, Enum.Parse(type, enumValue), null);
                        return;
                    }
                }
            }
            catch
            {
            }
        }

        private static void SetDoubleProp(object target, string name, double value)
        {
            if (target == null)
                return;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (property == null || !property.CanWrite)
                    return;

                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object converted = Convert.ChangeType(value, type);
                property.SetValue(target, converted, null);
            }
            catch
            {
            }
        }

        private class PickedPoint
        {
            public ViewBase View;
            public Point Point;
        }

        private static object GetPicker(DrawingHandler drawingHandler)
        {
            try
            {
                MethodInfo mi = drawingHandler.GetType().GetMethod("GetPicker");
                if (mi == null)
                    return null;

                return mi.Invoke(drawingHandler, null);
            }
            catch
            {
                return null;
            }
        }

        private static PickedPoint PickPoint(object picker, string prompt)
        {
            if (picker == null)
                return null;

            MethodInfo[] methods = picker.GetType().GetMethods();

            foreach (MethodInfo method in methods)
            {
                if (method == null || method.Name != "PickPoint")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 3)
                    continue;

                if (parameters[0].ParameterType != typeof(string))
                    continue;

                object[] args = new object[parameters.Length];
                args[0] = prompt;

                bool supported = true;

                for (int i = 1; i < parameters.Length; i++)
                {
                    if (!parameters[i].IsOut && !parameters[i].ParameterType.IsByRef)
                    {
                        supported = false;
                        break;
                    }

                    args[i] = null;
                }

                if (!supported)
                    continue;

                try
                {
                    method.Invoke(picker, args);

                    PickedPoint picked = new PickedPoint();

                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] is ViewBase)
                            picked.View = args[i] as ViewBase;
                        else if (args[i] is Point)
                            picked.Point = args[i] as Point;
                    }

                    if (picked.View != null && picked.Point != null)
                        return picked;
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                        throw ex.InnerException;

                    throw;
                }
                catch
                {
                }
            }

            throw new Exception("Không tìm được hàm PickPoint phù hợp trong Tekla Drawing Picker.");
        }
    }
}
