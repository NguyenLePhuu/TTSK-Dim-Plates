#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Reflection;
using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Scoped execution options for Slot09 Data Center. The scope never writes
    /// MainForm controls or persistent user settings and is always released by
    /// IDisposable/finally.
    /// </summary>
    public static class PHU_Slot09_DataCenterContext
    {
        private const double BELOW_GRID_TOP_FRONT_GAP = 50.0;

        [ThreadStatic]
        private static int _depth;

        [ThreadStatic]
        private static TSD.View _registeredTopView;

        [ThreadStatic]
        private static TSD.View _registeredFrontView;

        [ThreadStatic]
        private static double _registeredTopFrontGap;

        [ThreadStatic]
        private static bool _hasRegisteredTopFront;

        [ThreadStatic]
        private static bool _reserveFrontSlot04Tier;

        [ThreadStatic]
        private static TSD.View _registeredSlot04FrontView;

        [ThreadStatic]
        private static int _registeredSlot04FrontViewId;

        [ThreadStatic]
        private static double _registeredSlot04AbsoluteLine;

        [ThreadStatic]
        private static double _registeredSlot04DirectionX;

        [ThreadStatic]
        private static double _registeredSlot04DirectionY;

        [ThreadStatic]
        private static bool _hasRegisteredSlot04Tier;

        public static bool IsActive
        {
            get { return _depth > 0; }
        }

        public static IDisposable Begin()
        {
            return Begin(false);
        }

        public static IDisposable Begin(bool reserveFrontSlot04Tier)
        {
            if (_depth == 0)
            {
                _registeredTopView = null;
                _registeredFrontView = null;
                _registeredTopFrontGap = 0.0;
                _hasRegisteredTopFront = false;
                _reserveFrontSlot04Tier = reserveFrontSlot04Tier;
                _registeredSlot04FrontView = null;
                _registeredSlot04FrontViewId = int.MaxValue;
                _registeredSlot04AbsoluteLine = 0.0;
                _registeredSlot04DirectionX = 0.0;
                _registeredSlot04DirectionY = 0.0;
                _hasRegisteredSlot04Tier = false;
            }

            _depth++;
            return new Scope();
        }

        public static bool ReserveFrontSlot04Tier
        {
            get { return IsActive && _reserveFrontSlot04Tier; }
        }

        public static void RegisterFrontSlot04Tier(
            TSD.View frontView,
            double absoluteLine,
            double directionX,
            double directionY
        )
        {
            if (
                !ReserveFrontSlot04Tier
                || frontView == null
                || !IsFinite(absoluteLine)
                || !IsFinite(directionX)
                || !IsFinite(directionY)
            )
                return;

            double length = Math.Sqrt((directionX * directionX) + (directionY * directionY));
            if (!IsFinite(length) || length <= 0.0001)
                return;

            _registeredSlot04FrontView = frontView;
            _registeredSlot04FrontViewId = GetViewIdentifier(frontView);
            _registeredSlot04AbsoluteLine = absoluteLine;
            _registeredSlot04DirectionX = directionX / length;
            _registeredSlot04DirectionY = directionY / length;
            _hasRegisteredSlot04Tier = true;
        }

        public static bool TryResolveFrontSlot04Placement(
            TSD.View frontView,
            TSG.Point firstPoint,
            out TSG.Vector direction,
            out double distance
        )
        {
            direction = null;
            distance = 0.0;
            if (
                !_hasRegisteredSlot04Tier
                || frontView == null
                || firstPoint == null
                || !SameView(frontView)
            )
                return false;

            double pointLine =
                (firstPoint.X * _registeredSlot04DirectionX)
                + (firstPoint.Y * _registeredSlot04DirectionY);
            distance = _registeredSlot04AbsoluteLine - pointLine;
            if (!IsFinite(distance) || distance <= 0.0001)
                return false;

            direction = new TSG.Vector(
                _registeredSlot04DirectionX,
                _registeredSlot04DirectionY,
                0.0
            );
            return true;
        }

        public static double ResolveTopFrontGap(TSD.View frontView, double safeDefaultGap)
        {
            if (!IsActive || frontView == null)
                return safeDefaultGap;

            PHU_BeamGridDimensionEngine.FrontCrossGridPosition position =
                PHU_BeamGridDimensionEngine.GetPreparedFrontCrossGridPosition(frontView);

            return position == PHU_BeamGridDimensionEngine.FrontCrossGridPosition.BelowBeam
                ? BELOW_GRID_TOP_FRONT_GAP
                : safeDefaultGap;
        }

        public static void RegisterFinalTopFrontArrangement(
            TSD.View topView,
            TSD.View frontView,
            double expectedGap
        )
        {
            if (!IsActive || topView == null || frontView == null)
                return;

            _registeredTopView = topView;
            _registeredFrontView = frontView;
            _registeredTopFrontGap = expectedGap;
            _hasRegisteredTopFront = true;
        }

        public static bool VerifyRegisteredTopFrontArrangement(out string message)
        {
            message = string.Empty;
            if (
                !_hasRegisteredTopFront
                || _registeredTopView == null
                || _registeredFrontView == null
            )
            {
                message = "DIM không đăng ký được cặp TOP/FRONT cuối cùng.";
                return false;
            }

            global::PHU_OpenGridView.FitGridOriginArrangeResult verification =
                global::PHU_OpenGridView.VerifyTopFrontGreenBoxGap(
                    _registeredTopView,
                    _registeredFrontView,
                    _registeredTopFrontGap
                );
            message =
                verification == null
                    ? "Không có kết quả verify TOP/FRONT sau DIM."
                    : verification.Message;
            return verification != null && verification.Success;
        }

        /// <summary>
        /// Slot09-only automation helper. It replaces the current selection
        /// with the single semantic TopView (the green view frame). The
        /// Neighbor Grid writer itself remains unchanged in PHU_OpenGridView.
        /// </summary>
        public static bool TrySelectSingleTopViewForNeighborGrid(out string message)
        {
            message = string.Empty;

            try
            {
                TSD.DrawingHandler drawingHandler = new TSD.DrawingHandler();
                if (!drawingHandler.GetConnectionStatus())
                {
                    message = "DrawingHandler chưa kết nối.";
                    return false;
                }

                TSD.Drawing drawing = drawingHandler.GetActiveDrawing();
                if (drawing == null)
                {
                    message = "Không có active drawing.";
                    return false;
                }

                List<TSD.View> topViews = GetTopViews(drawing);
                if (topViews.Count != 1)
                {
                    message =
                        "Slot09 Neighbor Grid yêu cầu đúng 1 TopView; tìm thấy "
                        + topViews.Count
                        + ".";
                    return false;
                }

                System.Collections.ArrayList selection = new System.Collections.ArrayList();
                selection.Add(topViews[0]);
                drawingHandler.GetDrawingObjectSelector().SelectObjects(selection, false);

                return VerifySingleTopViewSelection(drawingHandler, topViews[0], out message);
            }
            catch (Exception ex)
            {
                message = "Slot09 chọn TopView lỗi: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Slot09 read-back guard used immediately before calling the original
        /// PHU_OpenGridView.RunNeighborGrid overload.
        /// </summary>
        public static bool VerifySingleTopViewSelectionForNeighborGrid(out string message)
        {
            message = string.Empty;

            try
            {
                TSD.DrawingHandler drawingHandler = new TSD.DrawingHandler();
                if (!drawingHandler.GetConnectionStatus())
                {
                    message = "DrawingHandler chưa kết nối.";
                    return false;
                }

                return VerifySingleTopViewSelection(drawingHandler, null, out message);
            }
            catch (Exception ex)
            {
                message = "Slot09 kiểm tra TopView selection lỗi: " + ex.Message;
                return false;
            }
        }

        private static bool VerifySingleTopViewSelection(
            TSD.DrawingHandler drawingHandler,
            TSD.View expectedTopView,
            out string message
        )
        {
            message = string.Empty;
            if (drawingHandler == null)
            {
                message = "DrawingHandler không hợp lệ.";
                return false;
            }

            List<TSD.View> selectedViews = GetSelectedViews(drawingHandler);
            if (selectedViews.Count != 1 || !IsTopView(selectedViews[0]))
            {
                message = "Slot09 Neighbor Grid bị chặn: selection không phải đúng 1 TopView.";
                return false;
            }

            if (expectedTopView != null && !SameDrawingView(selectedViews[0], expectedTopView))
            {
                message = "Slot09 Neighbor Grid bị chặn: TopView được chọn không đúng mục tiêu.";
                return false;
            }

            message = "Đã xác nhận selection chỉ chứa đúng 1 TopView.";
            return true;
        }

        private static List<TSD.View> GetTopViews(TSD.Drawing drawing)
        {
            List<TSD.View> result = new List<TSD.View>();
            if (drawing == null)
                return result;

            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (!IsTopView(view))
                        continue;

                    bool duplicate = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (SameDrawingView(result[i], view))
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        result.Add(view);
                }
            }
            catch { }

            return result;
        }

        private static List<TSD.View> GetSelectedViews(TSD.DrawingHandler drawingHandler)
        {
            List<TSD.View> result = new List<TSD.View>();

            try
            {
                TSD.DrawingObjectEnumerator selected = drawingHandler
                    .GetDrawingObjectSelector()
                    .GetSelected();
                while (selected != null && selected.MoveNext())
                {
                    TSD.View view = selected.Current as TSD.View;
                    if (view != null)
                        result.Add(view);
                }
            }
            catch { }

            return result;
        }

        private static bool IsTopView(TSD.View view)
        {
            try
            {
                if (view == null)
                    return false;

                string viewType = view.ViewType.ToString();
                return string.Equals(viewType, "TopView", StringComparison.OrdinalIgnoreCase)
                    || viewType.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SameDrawingView(TSD.View first, TSD.View second)
        {
            if (Object.ReferenceEquals(first, second))
                return true;

            int firstId = GetViewIdentifier(first);
            int secondId = GetViewIdentifier(second);
            return firstId != int.MaxValue && secondId != int.MaxValue && firstId == secondId;
        }

        private static bool SameView(TSD.View view)
        {
            if (Object.ReferenceEquals(view, _registeredSlot04FrontView))
                return true;

            int id = GetViewIdentifier(view);
            return id != int.MaxValue
                && _registeredSlot04FrontViewId != int.MaxValue
                && id == _registeredSlot04FrontViewId;
        }

        private static int GetViewIdentifier(TSD.View view)
        {
            try
            {
                if (view == null)
                    return int.MaxValue;

                PropertyInfo identifierProperty = view.GetType()
                    .GetProperty(
                        "Identifier",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                object identifier =
                    identifierProperty == null ? null : identifierProperty.GetValue(view, null);
                PropertyInfo idProperty =
                    identifier == null ? null : identifier.GetType().GetProperty("ID");
                object id = idProperty == null ? null : idProperty.GetValue(identifier, null);
                return id == null ? int.MaxValue : Convert.ToInt32(id);
            }
            catch
            {
                return int.MaxValue;
            }
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private sealed class Scope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_depth > 0)
                    _depth--;
            }
        }
    }
}
