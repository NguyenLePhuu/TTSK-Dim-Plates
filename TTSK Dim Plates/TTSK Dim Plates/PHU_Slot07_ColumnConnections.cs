// Slot 07: scope Data Center, liên kết cột hộp và DIM grid cột hộp.
// LUỒNG: MainForm.RunDataCenterCurrentDrawing phân tuyến; nhánh cột hộp dùng Read → Build/BuildColumnGrid → Execute.
// ĐIỂM SỬA: nhận cột ở IsColumnRoute; chân liên kết ở Build; chân grid ở BuildColumnGrid; DIM trùng ở Matches.
// THAM SỐ: GeometryTolerance/DimensionTolerance là mm view; BELOW_GRID_TOP_FRONT_GAP là mm giấy.
// TRẠNG THÁI: PHU_Slot07_DataCenterContext và PHU_Slot07_DrawingRoute chỉ có hiệu lực trong scope; phải kết thúc cùng lần chạy.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_Slot07_DataCenterContext.cs
//   PHU_Slot07_ColumnConnections.cs
//   PHU_Slot07_ColumnGrid.cs
//   PHU_Slot07_DrawingRoute.cs

#region PHU_Slot07_DataCenterContext - Ngữ cảnh lần chạy Data Center
#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using TSD = Tekla.Structures.Drawing;
    using TSG = Tekla.Structures.Geometry3d;

    /// <summary>
    /// Scoped execution options for Slot09 Data Center. The scope never writes
    /// MainForm controls or persistent user settings and is always released by
    /// IDisposable/finally.
    /// </summary>
    public static class PHU_Slot07_DataCenterContext
    {
        // CHỈNH BỐ CỤC (mm giấy): khoảng Top/Front khi cross-grid ở dưới dầm theo ResolveTopFrontGap.
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
        private static bool _preservePreparedDrawingLayout;

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
            return Begin(false, false);
        }

        public static IDisposable Begin(bool reserveFrontSlot04Tier)
        {
            return Begin(reserveFrontSlot04Tier, false);
        }

        public static IDisposable Begin(
            bool reserveFrontSlot04Tier,
            bool preservePreparedDrawingLayout
        )
        {
            if (_depth == 0)
            {
                _registeredTopView = null;
                _registeredFrontView = null;
                _registeredTopFrontGap = 0.0;
                _hasRegisteredTopFront = false;
                _reserveFrontSlot04Tier = reserveFrontSlot04Tier;
                _preservePreparedDrawingLayout = preservePreparedDrawingLayout;
                _registeredSlot04FrontView = null;
                _registeredSlot04FrontViewId = int.MaxValue;
                _registeredSlot04AbsoluteLine = 0.0;
                _registeredSlot04DirectionX = 0.0;
                _registeredSlot04DirectionY = 0.0;
                _hasRegisteredSlot04Tier = false;
            }
            else
            {
                _reserveFrontSlot04Tier =
                    _reserveFrontSlot04Tier || reserveFrontSlot04Tier;
                _preservePreparedDrawingLayout =
                    _preservePreparedDrawingLayout || preservePreparedDrawingLayout;
            }

            _depth++;
            return new Scope();
        }

        public static bool ReserveFrontSlot04Tier
        {
            get { return IsActive && _reserveFrontSlot04Tier; }
        }

        /// <summary>
        /// ACTIVE Data Center uses the prepared drawing layout for both Type1
        /// and Type2.  Shape/Grid consumers must create dimensions and owned
        /// REF lines without changing scale, view origin, restriction boxes,
        /// selection, or Neighbor Grid marks.
        /// </summary>
        public static bool PreservePreparedDrawingLayout
        {
            get { return IsActive && _preservePreparedDrawingLayout; }
        }

        public static string AuditExecutionPolicy()
        {
            bool preserve = PreservePreparedDrawingLayout;
            return "SLOT09 DATA CENTER ACTIVE POLICY: active=" + IsActive
                + ", preserveLayout=" + preserve
                + ", DIM=True, REF-lines=True, OpenGrid=" + (!preserve)
                + ", FitGrid=" + (!preserve)
                + ", ArrangeViews=" + (!preserve)
                + ", NeighborGridMark=" + (!preserve) + ".";
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
#pragma warning restore
#endregion

#region PHU_Slot07_ColumnConnections - Điểm gọi và điều phối liên kết cột


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using D = Tekla.Structures.Drawing;
    using G = Tekla.Structures.Geometry3d;
    using M = Tekla.Structures.Model;

    /// <summary>Slot 07 connection plans for vertical hollow-column assemblies.
    /// Beam and legacy Inzai shape routes retain their own geometry contracts.</summary>
    internal static partial class PHU_Slot07_ColumnConnections
    {
        // CHỈNH NGƯỠNG (mm view): GeometryTolerance nhận liên kết; DimensionTolerance khớp chân/chuỗi và trục grid.
        // Tăng DimensionTolerance có thể coi DIM đang lệch là đã đủ, khiến Execute bỏ qua tạo bổ sung.
        internal const double GeometryTolerance = 0.75;
        internal const double DimensionTolerance = 0.04;

        // Route from authoritative main-part geometry in GLOBAL coordinates,
        // never the current work plane, drawing name, project name or missing Top.
        internal static bool IsColumnRoute(string profile, IList<G.Point> globalReference)
        {
            return IsHollowProfile(profile) && IsVerticalReference(globalReference);
        }

        internal static bool IsVerticalReference(IList<G.Point> globalReference)
        {
            if (globalReference == null || globalReference.Count != 2
                || globalReference.Any(v => !Valid(v))) return false;
            double dx = globalReference[1].X - globalReference[0].X;
            double dy = globalReference[1].Y - globalReference[0].Y;
            double dz = globalReference[1].Z - globalReference[0].Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return length > 100 && Math.Abs(dz) / length >= 0.999;
        }

        private static bool IsHollowProfile(string profile)
        {
            string p = (profile ?? "").Trim().ToUpperInvariant();
            return p.StartsWith("STKR") || p.StartsWith("SHS") || p.StartsWith("RHS") || p.StartsWith("BOX");
        }

        internal static bool IsCurrentDrawingColumn(D.Drawing drawing)
        {
            if (!(drawing is D.AssemblyDrawing)) return false;
            string profile;
            var reference=ReadGlobalMainReference(drawing,out profile);
            return IsColumnRoute(profile,reference);
        }

        internal static bool IsCurrentDrawingVertical(D.Drawing drawing)
        {
            if (!(drawing is D.AssemblyDrawing)) return false;
            string profile;
            return IsVerticalReference(ReadGlobalMainReference(drawing,out profile));
        }

        private static List<G.Point> ReadGlobalMainReference(D.Drawing drawing,out string profile)
        {
            var model = new M.Model();
            if (!model.GetConnectionStatus()) throw new InvalidOperationException("Chưa kết nối Model API.");
            var main = PHU_MainPartResolver.Resolve(model, drawing);
            if (main == null) throw new InvalidOperationException("Không đọc được MainPart để phân luồng Slot 07.");
            var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var reference = new List<G.Point>();
            foreach (G.Point point in main.GetReferenceLine(false)) reference.Add(toGlobal.Transform(point));
            profile=main.Profile.ProfileString;
            return reference;
        }

        internal sealed class Part
        {
            public int Id, Assembly;
            public string Profile, Position;
            public bool Plate;
            public readonly List<G.Point> Reference = new List<G.Point>();
            public readonly List<G.Point> Vertices = new List<G.Point>();
            // Only bolt groups whose drilling axis faces this view.
            public readonly List<G.Point> WebBolts = new List<G.Point>();
        }

        internal sealed class View
        {
            public D.View Object;
            public int Id;
            public double Scale;
            public Part Main;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Existing> Dimensions = new List<Existing>();
            public readonly List<Grid> Grids = new List<Grid>();
        }

        internal sealed class Existing
        {
            public G.Vector Normal;
            public readonly List<G.Point> Feet = new List<G.Point>();
        }

        internal sealed class Plan
        {
            public View View;
            public string Kind, Evidence;
            public G.Vector Normal;
            public double Distance;
            public double WitnessMin, WitnessMax;
            public List<G.Point> Feet;
            public bool Satisfied;
            public bool RequireExactFeet;
        }

        internal sealed class Result
        {
            public D.Drawing Drawing;
            public int DrawingId;
            public string DrawingName;
            public readonly List<Plan> Plans = new List<Plan>();
            public readonly List<string> Notes = new List<string>();
            public readonly List<string> AuditDetails = new List<string>();
            public string Report()
            {
                var text = new StringBuilder("SLOT 07 - COLUMN CONNECTION PLAN - READ ONLY\r\n");
                text.AppendLine("Drawing=" + DrawingName + " ID=" + DrawingId);
                text.AppendLine("Plans=" + Plans.Count + " Existing=" + Plans.Count(p => p.Satisfied)
                    + " Missing=" + Plans.Count(p => !p.Satisfied));
                foreach (var p in Plans)
                {
                    var axis = new G.Vector(-p.Normal.Y, p.Normal.X, 0);
                    var stations = Stations(p.Feet, axis);
                    text.AppendLine("VIEW=" + p.View.Id + " " + p.Kind + " " + (p.Satisfied ? "EXISTING" : "MISSING")
                        + " mm=" + String.Join(" / ", stations.Skip(1).Select((v, i) => F(v - stations[i])))
                        + " " + p.Evidence);
                    text.AppendLine("  feet=" + String.Join("; ", p.Feet.Select(v => "(" + F(v.X) + "," + F(v.Y) + ")"))
                        + " normal=" + p.Normal + " distance=" + F(p.Distance));
                }
                foreach (string note in Notes) text.AppendLine("NOTE " + note);
                foreach (string detail in AuditDetails) text.AppendLine("AUDIT " + detail);
                return text.ToString();
            }
        }

        private sealed class Arm
        {
            public Part Part;
            public G.Point Node, Low, High, Terminal;
            public G.Vector Out;
        }

        internal static Result Read()
        {
            var model = new M.Model();
            var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
                throw new InvalidOperationException("Chưa kết nối Tekla.");
            D.Drawing drawing = handler.GetActiveDrawing();
            if (!(drawing is D.AssemblyDrawing))
                throw new InvalidOperationException("Slot 07 cần bản vẽ assembly cột đang mở.");
            if (!IsCurrentDrawingColumn(drawing))
                throw new InvalidOperationException("Bản vẽ không thuộc nhánh cột hộp đứng Slot 07; giữ luồng Inzai cũ.");
            M.Part main = PHU_MainPartResolver.Resolve(model, drawing);
            if (main == null) throw new InvalidOperationException("Không đọc được MainPart của bản vẽ.");
            int assembly = main.GetAssembly().Identifier.ID;
            var result = new Result { Drawing = drawing, DrawingId = Id(drawing), DrawingName = drawing.Name };
            result.AuditDetails.Add("Model=" + model.GetInfo().ModelPath + " Mark=" + drawing.Mark
                + " Main=" + main.Identifier.ID + " Profile=" + main.Profile.ProfileString);
            result.AuditDetails.Add("Coordinates: unchanged current work plane -> global -> view. Model/Drawing API connected.");
            var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var views = drawing.GetSheet().GetAllViews();
            while (views.MoveNext())
            {
                var view = views.Current as D.View;
                if (view == null || view.ViewType == D.View.ViewTypes.SectionView) continue;
                var snapshot = new View { Object = view, Id = Id(view), Scale = view.Attributes.Scale };
                result.AuditDetails.Add("VIEW=" + snapshot.Id + " Name=" + view.Name + " Type=" + view.ViewType
                    + " Scale=" + snapshot.Scale + " SheetOrigin=" + view.Origin + " Origin=" + view.DisplayCoordinateSystem.Origin
                    + " X=" + view.DisplayCoordinateSystem.AxisX + " Y=" + view.DisplayCoordinateSystem.AxisY);
                if (!Finite(snapshot.Scale) || snapshot.Scale <= 0) throw new InvalidOperationException("Scale không hợp lệ.");
                var toView = G.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                Func<G.Point, G.Point> transform = p => toView.Transform(toGlobal.Transform(p));
                var objects = view.GetAllObjects(typeof(D.Part));
                var seen = new HashSet<int>();
                while (objects.MoveNext())
                {
                    var dp = objects.Current as D.Part;
                    if (dp == null || !seen.Add(dp.ModelIdentifier.ID)) continue;
                    var part = model.SelectModelObject(dp.ModelIdentifier) as M.Part;
                    if (part == null || part.GetAssembly().Identifier.ID != assembly) continue;
                    try
                    {
                        var data = ReadPart(part, transform, result.Notes);
                        snapshot.Parts.Add(data);
                        if (part.Identifier.ID == main.Identifier.ID) snapshot.Main = data;
                    }
                    catch (Exception ex)
                    {
                        // An incomplete solid cannot supply a dimension witness point.
                        result.Notes.Add("VIEW=" + snapshot.Id + " bỏ P" + part.Identifier.ID + ": " + ex.Message);
                    }
                }
                if (snapshot.Main == null) continue;
                ReadExisting(snapshot);
                var connectionPlans = Build(snapshot, result.Notes);
                if (snapshot.Main.Reference.Count != 2
                    || Length(snapshot.Main.Reference[0], snapshot.Main.Reference[1]) < 100) continue;
                var globalOriginInView=toView.Transform(new G.Point(0,0,0));
                var globalUpInView=toView.Transform(new G.Point(0,0,1));
                if(Math.Abs(globalUpInView.X-globalOriginInView.X)>0.001
                    || Math.Abs(globalUpInView.Y-globalOriginInView.Y-1)>0.001
                    || Math.Abs(globalUpInView.Z-globalOriginInView.Z)>0.001)
                    throw new InvalidOperationException("VIEW="+snapshot.Id+" không có chiều đứng hướng lên theo cột; dừng để tránh đảo đáy/đỉnh plate.");
                ReadGrids(snapshot);
                result.Plans.AddRange(connectionPlans);
                result.Plans.AddRange(BuildColumnGrid(snapshot, connectionPlans));
            }
            if (Id(handler.GetActiveDrawing()) != result.DrawingId)
                throw new InvalidOperationException("Bản vẽ hiện tại đã đổi trong lúc đọc; dừng Slot 07.");
            if (result.Plans.Select(p=>p.View.Id).Distinct().Count()!=2)
                throw new InvalidOperationException("Nhánh cột hộp cần đúng hai hình chiếu đứng X-X/Y-Y rõ ràng.");
            return result;
        }

        private static Part ReadPart(M.Part part, Func<G.Point, G.Point> transform, List<string> notes)
        {
            var data = new Part { Id = part.Identifier.ID, Assembly = part.GetAssembly().Identifier.ID,
                Profile = part.Profile.ProfileString ?? "", Plate = part is M.ContourPlate };
            string position = "";
            part.GetReportProperty("PART_POS", ref position);
            data.Position = position;
            foreach (G.Point point in part.GetReferenceLine(false)) data.Reference.Add(transform(point));
            var edges = part.GetSolid().GetEdgeEnumerator();
            while (edges.MoveNext())
            {
                var edge = edges.Current as Tekla.Structures.Solid.Edge;
                if (edge == null) continue;
                data.Vertices.Add(transform(edge.StartPoint)); data.Vertices.Add(transform(edge.EndPoint));
            }
            if (data.Vertices.Count == 0 || data.Vertices.Any(p => !Valid(p)))
                throw new InvalidOperationException("Không đọc được solid đầy đủ.");
            try
            {
                var bolts = part.GetBolts();
                while (bolts.MoveNext())
                {
                    var group = bolts.Current as M.BoltGroup;
                    if (group == null) continue;
                    var cs = group.GetCoordinateSystem();
                    var drill = G.Vector.Cross(cs.AxisX, cs.AxisY);
                    var start = transform(cs.Origin);
                    var end = transform(new G.Point(cs.Origin.X + drill.X, cs.Origin.Y + drill.Y, cs.Origin.Z + drill.Z));
                    double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) + Math.Pow(end.Z - start.Z, 2));
                    if (length < 0.001 || Math.Abs(end.Z - start.Z) / length < 0.98) continue;
                    foreach (G.Point point in group.BoltPositions) data.WebBolts.Add(transform(point));
                }
            }
            catch (Exception ex)
            {
                data.WebBolts.Clear();
                notes.Add("P" + data.Id + " chưa đọc đủ bu lông, bỏ DIM lỗ: " + ex.Message);
            }
            return data;
        }

        internal static List<Plan> Build(View view, List<string> notes)
        {
            var plans = new List<Plan>();
            Part main = view.Main;
            if (main == null || main.Reference.Count != 2 || main.Vertices.Count == 0) return plans;
            G.Vector up = Unit(main.Reference[0], main.Reference[1]);
            if (up == null) return plans;
            if (up.Y < -0.001 || (Math.Abs(up.Y) < 0.001 && up.X < 0)) up = Neg(up);
            var right = new G.Vector(up.Y, -up.X, 0);
            double width = main.Vertices.Max(p => Dot(p, right)) - main.Vertices.Min(p => Dot(p, right));
            if (Length(main.Reference[0], main.Reference[1]) < Math.Max(100, width * 2)) return plans;
            G.Point origin = main.Reference[0];
            double axis = Dot(origin, right);
            var columns = view.Parts.Where(p => !p.Plate && p.Assembly == main.Assembly && p.Reference.Count == 2
                && p.Reference.All(r => Math.Abs(Dot(r, right) - axis) <= GeometryTolerance)
                && Length(p.Reference[0], p.Reference[1]) > 10).ToList();
            var arms = new List<Arm>();
            foreach (Part part in view.Parts)
            {
                string profile = (part.Profile ?? "").ToUpperInvariant();
                if (part.Plate || part.Assembly != main.Assembly || part.Id == main.Id || part.Reference.Count != 2
                    || part.Vertices.Count == 0 || !(profile.StartsWith("H") || profile.StartsWith("I") || profile.StartsWith("BH"))) continue;
                var ends = part.Reference.OrderBy(p => Math.Abs(Dot(p, right) - axis)).ToArray();
                G.Point node = ends[0], outer = ends[1];
                var outward = Unit(node, outer);
                if (outward == null || Math.Abs(Dot(outward, up)) > 0.02
                    || Math.Abs(Dot(node, right) - axis) > GeometryTolerance
                    || Math.Abs(outer.Z - node.Z) > GeometryTolerance || Length(node, outer) < 100) continue;
                // The node may be on ANY collinear segment of the same column assembly.
                bool attached = columns.Any(c => Between(Dot(node, up), c.Reference.Min(p => Dot(p, up)), c.Reference.Max(p => Dot(p, up)))
                    && c.Vertices.Count > 0 && DepthOverlap(c, part)
                    && c.Reference.Any(p => Math.Abs(p.Z - node.Z) <= GeometryTolerance));
                if (!attached) continue;
                double terminal = part.Vertices.Max(p => Dot(p, outward));
                var face = part.Vertices.Where(p => Math.Abs(Dot(p, outward) - terminal) <= GeometryTolerance).ToList();
                if (face.Count < 2 || terminal - Dot(node, outward) < 100) continue;
                var low = face.OrderBy(p => Dot(p, up)).First();
                var high = face.OrderByDescending(p => Dot(p, up)).First();
                double depth = Dot(high, up) - Dot(low, up);
                if (depth < 10) continue;
                // REF must coincide with an actual terminal edge. A centre-line REF is not silently substituted.
                var endAtNodeLevel = face.OrderBy(p => Math.Abs(Dot(p, up) - Dot(node, up))).First();
                if (Math.Abs(Dot(endAtNodeLevel, up) - Dot(node, up)) > GeometryTolerance)
                { notes.Add("P" + part.Id + " REF không trùng mép ngoài; cần quy tắc riêng."); continue; }
                var arm = new Arm { Part = part, Node = node, Out = outward, Low = low, High = high, Terminal = endAtNodeLevel };
                arms.Add(arm);
                int firstPlan = plans.Count;
                string evidence = "P" + part.Id + "[" + part.Position + "]";
                Add(plans, view, "DEPTH", evidence + " solid terminal edges", outward, 42, low, high);
                var holes = part.WebBolts.Where(p => Valid(p) && Dot(p, up) > Dot(low, up) + GeometryTolerance
                    && Dot(p, up) < Dot(high, up) - GeometryTolerance
                    && Dot(p, outward) < terminal - GeometryTolerance
                    && Dot(p, outward) > Dot(node, outward)
                    && terminal - Dot(p, outward) <= Math.Max(150, Length(node, outer) * 0.3))
                    .OrderByDescending(p => Dot(p, outward)).ThenBy(p => Math.Abs(Dot(p, up) - Dot(node, up))).ToList();
                if (holes.Count > 0)
                {
                    G.Point hole = holes[0];
                    Add(plans, view, "EDGE-BOLT", evidence + " actual web bolt to terminal", up, 6, endAtNodeLevel, hole);
                    Add(plans, view, "REF-BOLT", evidence + " actual web bolt to reference-side edge", outward, 34, hole, endAtNodeLevel);
                }
                else notes.Add(evidence + " không có bu lông mặt bụng phù hợp; giữ DIM mép/định vị.");
                AddPlateGap(plans, view, arm, up, false, notes);
                AddPlateGap(plans, view, arm, up, true, notes);
                foreach (var plan in plans.Skip(firstPlan)) IncludeWitnessRegion(plan, part.Vertices);
            }
            var pending = new List<Arm>(arms);
            while (pending.Count > 0)
            {
                Arm first = pending[0];
                var group = pending.Where(a => Length(a.Node, first.Node) <= GeometryTolerance && Math.Abs(a.Node.Z - first.Node.Z) <= GeometryTolerance).ToList();
                pending.RemoveAll(a => group.Contains(a));
                if (group.Count > 2 || (group.Count == 2 && Dot(group[0].Out, group[1].Out) > -0.98))
                { notes.Add("VIEW=" + view.Id + " có nhiều nhánh cùng phía tại node; bỏ chuỗi định vị mơ hồ."); continue; }
                var feet = group.Select(a => a.Terminal).ToList(); feet.Add(first.Node);
                int before = plans.Count;
                Add(plans, view, "OVERALL", "column REF -> " + String.Join(",", group.Select(a => "P" + a.Part.Id)), up, 20, feet.ToArray());
                if (plans.Count > before)
                    foreach (var arm in group) IncludeWitnessRegion(plans[plans.Count - 1], arm.Part.Vertices);
            }
            foreach (Plan plan in plans) plan.Satisfied = view.Dimensions.Any(d => Matches(plan, d));
            return plans;
        }

        private static void AddPlateGap(List<Plan> plans, View view, Arm arm, G.Vector up, bool upper, List<string> notes)
        {
            G.Point edge = upper ? arm.High : arm.Low;
            double level = Dot(edge, up), node = Dot(arm.Node, arm.Out);
            var candidates = new List<Tuple<Part, G.Point, double>>();
            foreach (Part plate in view.Parts.Where(p => p.Plate && p.Assembly == arm.Part.Assembly && p.Vertices.Count > 0))
            {
                if (!DepthOverlap(plate, arm.Part) || !Between(node, plate.Vertices.Min(p => Dot(p, arm.Out)), plate.Vertices.Max(p => Dot(p, arm.Out)))) continue;
                double lo = plate.Vertices.Min(p => Dot(p, up)), hi = plate.Vertices.Max(p => Dot(p, up));
                if (hi - lo > Math.Min(50, (Dot(arm.High, up) - Dot(arm.Low, up)) / 2)) continue;
                if (!Between(level, lo, hi)) continue;
                double outside = upper ? hi : lo, gap = upper ? outside - level : level - outside;
                if (gap <= DimensionTolerance || gap > 25) continue;
                var face = plate.Vertices.Where(p => Math.Abs(Dot(p, up) - outside) <= GeometryTolerance)
                    .OrderByDescending(p => Dot(p, arm.Out)).First();
                candidates.Add(Tuple.Create(plate, face, gap));
            }
            if (candidates.Count != 1)
            {
                if (candidates.Count > 1) notes.Add("P" + arm.Part.Id + " có nhiều plate cho khe " + (upper ? "trên" : "dưới") + "; bỏ DIM khe.");
                return;
            }
            var match = candidates[0];
            Add(plans, view, upper ? "GAP-HIGH" : "GAP-LOW", "P" + arm.Part.Id + " edge -> PLATE P" + match.Item1.Id + " outside face", arm.Out, 22, edge, match.Item2);
        }

        private static void Add(List<Plan> plans, View view, string kind, string evidence, G.Vector normal, double paperDistance, params G.Point[] points)
        {
            var axis = new G.Vector(-normal.Y, normal.X, 0);
            var sorted = points.OrderBy(p => Dot(p, axis)).ToList();
            if (sorted.Count < 2 || sorted.Any(p => !Valid(p))) return;
            if (sorted.Skip(1).Where((p, i) => Dot(p, axis) - Dot(sorted[i], axis) <= DimensionTolerance).Any()) return;
            double line = sorted.Max(p => Dot(p, normal)) + paperDistance * view.Scale;
            plans.Add(new Plan { View = view, Kind = kind, Evidence = evidence, Normal = normal,
                Feet = sorted.Select(p => new G.Point(p.X, p.Y, 0)).ToList(), Distance = line - Dot(sorted[0], normal),
                WitnessMin = sorted.Min(p => Dot(p, normal)), WitnessMax = sorted.Max(p => Dot(p, normal)) });
        }

        private static void IncludeWitnessRegion(Plan plan, IEnumerable<G.Point> vertices)
        {
            plan.WitnessMin = Math.Min(plan.WitnessMin, vertices.Min(p => Dot(p, plan.Normal)));
            plan.WitnessMax = Math.Max(plan.WitnessMax, vertices.Max(p => Dot(p, plan.Normal)));
        }

        private static void ReadExisting(View view)
        {
            var objects = view.Object.GetAllObjects(typeof(D.StraightDimensionSet));
            while (objects.MoveNext())
            {
                var set = objects.Current as D.StraightDimensionSet;
                if (set == null) continue;
                var data = ReadSet(set);
                if (data != null) view.Dimensions.Add(data);
            }
        }

        private static Existing ReadSet(D.StraightDimensionSet set)
        {
            var normal = Member(set, "UpDirection") as G.Vector;
            if (normal == null) return null;
            var data = new Existing { Normal = normal };
            var children = set.GetObjects();
            while (children.MoveNext())
            {
                var child = children.Current as D.StraightDimension;
                if (child == null) continue;
                data.Feet.Add(child.StartPoint); data.Feet.Add(child.EndPoint);
            }
            return data.Feet.Count > 0 && data.Feet.All(Valid) ? data : null;
        }

        internal static bool Matches(Plan plan, Existing existing)
        {
            if (existing.Normal == null || existing.Feet.Count < 2) return false;
            double length = Math.Sqrt(existing.Normal.X * existing.Normal.X + existing.Normal.Y * existing.Normal.Y);
            if (length <= 0.0001 || Dot(plan.Normal, existing.Normal) / length < 0.999) return false;
            var axis = new G.Vector(-plan.Normal.Y, plan.Normal.X, 0);
            var expected = Stations(plan.Feet, axis); var actual = Stations(existing.Feet, axis);
            int start = actual.FindIndex(v => Math.Abs(v - expected[0]) <= DimensionTolerance);
            if (start < 0 || start + expected.Count > actual.Count
                || expected.Where((v, i) => Math.Abs(v - actual[start + i]) > DimensionTolerance).Any()) return false;
            if(plan.RequireExactFeet && !plan.Feet.All(p=>existing.Feet.Any(q=>Length(p,q)<=DimensionTolerance))) return false;
            // A contiguous part of an existing chain already satisfies the requested segments.
            // Witness feet may slide on the same branch, but not to an unrelated connection.
            var relevant = existing.Feet.Where(p => Dot(p, axis) >= expected[0] - DimensionTolerance
                && Dot(p, axis) <= expected[expected.Count - 1] + DimensionTolerance);
            return relevant.All(p => Dot(p, plan.Normal) >= plan.WitnessMin - 1 && Dot(p, plan.Normal) <= plan.WitnessMax + 1);
        }

        internal static string Execute()
        {
            Result result = Read();
            if (result.Plans.Count == 0) throw new InvalidOperationException("Slot 07 chưa nhận diện được cụm cột–dầm phù hợp. " + String.Join("; ", result.Notes));
            var missing = result.Plans.Where(p => !p.Satisfied).ToList();
            var created = new List<D.StraightDimensionSet>();
            var handler = new D.DrawingHandler();
            try
            {
                var factory = new D.StraightDimensionSetHandler();
                foreach (Plan plan in missing)
                {
                    if (Id(handler.GetActiveDrawing()) != result.DrawingId) throw new InvalidOperationException("Bản vẽ hiện tại đã đổi.");
                    var points = new D.PointList(); foreach (G.Point point in plan.Feet) points.Add(point);
                    var dimension = factory.CreateDimensionSet(plan.View.Object, points, plan.Normal, plan.Distance);
                    if (dimension == null) throw new InvalidOperationException("Không tạo được DIM " + plan.Kind);
                    created.Add(dimension);
                    if (plan.Feet.Count > 2)
                    {
                        var attributes = dimension.Attributes;
                        if (attributes == null) throw new InvalidOperationException("Không đọc được thuộc tính DIM chuỗi.");
                        var combined = attributes.CombinedDimension ?? new D.DimensionSetBaseAttributes.CombinedDimensionAttributes();
                        combined.Format = D.DimensionSetBaseAttributes.CombineFormats.Off;
                        combined.MinimumNumberToCombine = Math.Max(5, plan.Feet.Count);
                        attributes.CombinedDimension = combined;
                        dimension.Attributes = attributes;
                        if (!dimension.Modify() || !dimension.Select()
                            || dimension.Attributes.CombinedDimension.Format != D.DimensionSetBaseAttributes.CombineFormats.Off)
                            throw new InvalidOperationException("Không giữ được từng đoạn của DIM chuỗi " + plan.Kind);
                    }
                    var actual = ReadSet(dimension);
                    if (actual == null || actual.Feet.Count != (plan.Feet.Count - 1) * 2 || !Matches(plan, actual))
                        throw new InvalidOperationException("DIM vừa tạo không khớp số đoạn/chân DIM: " + plan.Kind);
                }
                if (created.Count > 0)
                {
                    if (Id(handler.GetActiveDrawing()) != result.DrawingId) throw new InvalidOperationException("Bản vẽ hiện tại đã đổi.");
                    result.Drawing.CommitChanges();
                }
            }
            catch (Exception ex)
            {
                int remaining = 0;
                foreach (var dimension in created)
                {
                    try { if (!dimension.Delete()) remaining++; } catch { remaining++; }
                }
                throw new InvalidOperationException("Slot 07 dừng: " + ex.Message
                    + (remaining > 0 ? " Còn " + remaining + " DIM mới cần kiểm tra." : " Đã thu hồi DIM vừa tạo; giữ nguyên DIM có sẵn."), ex);
            }
            return "Slot 07: tạo " + created.Count + " DIM, giữ " + (result.Plans.Count - missing.Count)
                + " DIM phù hợp có sẵn. Không đổi model/bố cục view."
                + (result.Notes.Count > 0 ? " " + String.Join("; ", result.Notes) : "");
        }

        private static List<double> Stations(IEnumerable<G.Point> points, G.Vector axis)
        {
            var result = new List<double>();
            foreach (double value in points.Select(p => Dot(p, axis)).OrderBy(v => v))
                if (result.Count == 0 || value - result[result.Count - 1] > DimensionTolerance) result.Add(value);
            return result;
        }
        private static bool DepthOverlap(Part a, Part b)
        { return Math.Min(a.Vertices.Max(p => p.Z), b.Vertices.Max(p => p.Z)) - Math.Max(a.Vertices.Min(p => p.Z), b.Vertices.Min(p => p.Z)) > 1; }
        private static bool Between(double value, double low, double high) { return value >= low - GeometryTolerance && value <= high + GeometryTolerance; }
        private static G.Vector Unit(G.Point a, G.Point b)
        { double length = Length(a, b); return length < 0.001 ? null : new G.Vector((b.X - a.X) / length, (b.Y - a.Y) / length, 0); }
        private static double Length(G.Point a, G.Point b) { return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)); }
        private static G.Vector Neg(G.Vector v) { return new G.Vector(-v.X, -v.Y, 0); }
        private static double Dot(G.Point p, G.Vector v) { return p.X * v.X + p.Y * v.Y; }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Valid(G.Point p) { return p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static object Member(object value, string name)
        { return value == null ? null : value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value, null); }
        private static int Id(object value)
        {
            var identifier = Member(value, "Identifier") as Tekla.Structures.Identifier;
            if (identifier == null || identifier.ID <= 0) throw new InvalidOperationException("Không đọc được ID bản vẽ/view.");
            return identifier.ID;
        }
    }
}
#endregion

#region PHU_Slot07_ColumnGrid - Lập kế hoạch chuỗi DIM grid và tấm nối cột hộp


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using D = Tekla.Structures.Drawing;
    using G = Tekla.Structures.Geometry3d;

    internal static partial class PHU_Slot07_ColumnConnections
    {
        internal sealed class Grid
        {
            public G.Point Start, End;
        }

        private static void ReadGrids(View view)
        {
            var objects = view.Object.GetAllObjects(typeof(D.GridLine));
            while (objects.MoveNext())
            {
                var line = objects.Current as D.GridLine;
                // GridPoint is already in view coordinates, as in the legacy Grid reader.
                if (line?.StartLabel?.GridPoint == null || line.EndLabel?.GridPoint == null) continue;
                view.Grids.Add(new Grid { Start = line.StartLabel.GridPoint, End = line.EndLabel.GridPoint });
            }
        }

        internal static List<Plan> BuildColumnGrid(View view, List<Plan> connections)
        {
            var plans = new List<Plan>();
            var main = view.Main;
            if (main == null || main.Reference.Count != 2) throw new InvalidOperationException("Column REF unavailable.");
            double axis = main.Reference[0].X, depth = main.Reference[0].Z;
            // This adapter is for the existing upright X-X/Y-Y views only.
            if (Math.Abs(main.Reference[1].X-axis) > GeometryTolerance
                || Math.Abs(main.Reference[1].Z-depth) > GeometryTolerance)
                throw new InvalidOperationException("Column Grid cần hình chiếu cột đứng, không xiên.");
            var columns = view.Parts.Where(p => !p.Plate && p.Assembly == main.Assembly
                && IsHollowProfile(p.Profile)
                && p.Reference.Count == 2 && p.Vertices.Count > 0
                && p.Reference.All(r => Math.Abs(r.X-axis) <= GeometryTolerance && Math.Abs(r.Z-depth) <= GeometryTolerance))
                .OrderBy(p => p.Reference.Min(r=>r.Y)).ToList();
            if (!columns.Contains(main)) throw new InvalidOperationException("MainPart không thuộc chuỗi cột.");
            for (int i=1; i<columns.Count; i++)
                if (Math.Abs(columns[i-1].Reference.Max(r=>r.Y)-columns[i].Reference.Min(r=>r.Y)) > GeometryTolerance)
                    throw new InvalidOperationException("Các đoạn REF cột không nối tiếp duy nhất; dừng trước khi tạo DIM.");
            var nodes = Stations(columns.SelectMany(p=>p.Reference), new G.Vector(0,1,0));
            if (nodes.Count<2) throw new InvalidOperationException("Column REF has no span.");
            var plateFeet=ResolveColumnPlateFeet(view, columns, nodes, axis, depth);
            double bottom=plateFeet.First().Point.Y, top=plateFeet.Last().Point.Y;
            var bottomAxis=new G.Point(axis,bottom,0);
            var topPlate=plateFeet.Last().Point;
            var vertical = view.Grids.Where(g => Valid(g.Start) && Valid(g.End)
                && Math.Abs(g.End.X-g.Start.X)<=DimensionTolerance
                && Math.Min(g.Start.Y,g.End.Y)<top && Math.Max(g.Start.Y,g.End.Y)>bottom).ToList();
            var axes=Stations(vertical.Select(g=>g.Start),new G.Vector(1,0,0));
            axes=axes.OrderBy(x=>Math.Abs(x-axis)).ToList();
            if (axes.Count==0 || (axes.Count>1 && Math.Abs(Math.Abs(axes[0]-axis)-Math.Abs(axes[1]-axis))<=DimensionTolerance))
                throw new InvalidOperationException("Không có trục đứng gần cột duy nhất; không tự chọn trục mơ hồ.");
            var floors=Stations(view.Grids.Where(g => Valid(g.Start) && Valid(g.End)
                && Math.Abs(g.End.Y-g.Start.Y)<=DimensionTolerance
                && Math.Min(g.Start.X,g.End.X)<=axis && Math.Max(g.Start.X,g.End.X)>=axis)
                .Select(g=>g.Start),new G.Vector(0,1,0));
            if (floors.Count!=2) throw new InvalidOperationException("Column Grid cần hai cao độ tầng rõ ràng; có " + floors.Count + ".");

            var left=new G.Vector(-1,0,0); var up=new G.Vector(0,1,0);
            Add(plans,view,"COLUMN-PLATE-CHAIN",String.Join(" -> ",plateFeet.Select(f=>f.Evidence)),left,10,plateFeet.Select(f=>f.Point).ToArray());
            Add(plans,view,"COLUMN-PLATE-TOTAL","base underside on column axis -> " + plateFeet.Last().Evidence,left,20,bottomAxis,topPlate);
            var levels=Stations(new[] { new G.Point(0,floors[0],0),new G.Point(0,floors[1],0),new G.Point(0,bottom,0),new G.Point(0,top,0) },new G.Vector(0,1,0));
            Add(plans,view,"COLUMN-FLOOR-CHAIN","floor grids, base underside and terminal plate outside face",left,30,
                levels.Select(y=>Math.Abs(y-top)<=DimensionTolerance?topPlate:new G.Point(axis,y,0)).ToArray());
            Add(plans,view,"FLOOR-TOTAL","lower floor grid -> upper floor grid",left,40,new G.Point(axis,floors[0],0),new G.Point(axis,floors[1],0));
            var terminal=columns.Last();
            double terminalTop=terminal.Vertices.Max(p=>p.Y);
            // Upper bevels expose the inner wall at the very highest Y. Resolve
            // actual OUTER side vertices first, then their highest exact point.
            double minX=terminal.Vertices.Min(p=>p.X), maxX=terminal.Vertices.Max(p=>p.X);
            var edgeLeft=terminal.Vertices.Where(p=>Math.Abs(p.X-minX)<=DimensionTolerance).OrderByDescending(p=>p.Y).First();
            var edgeRight=terminal.Vertices.Where(p=>Math.Abs(p.X-maxX)<=DimensionTolerance).OrderByDescending(p=>p.Y).First();
            if (edgeLeft.X>=axis-DimensionTolerance || edgeRight.X<=axis+DimensionTolerance)
                throw new InvalidOperationException("REF không nằm giữa hai mép đầu cột.");
            if (Math.Abs(axes[0]-axis)<=DimensionTolerance)
                Add(plans,view,"COLUMN-EDGE-AXIS-EDGE","terminal solid edges -> coincident column/grid axis",up,10,edgeLeft,new G.Point(axis,terminalTop,0),edgeRight);
            else
                Add(plans,view,"COLUMN-REF-GRID","column REF -> actual vertical grid",up,10,new G.Point(axis,terminalTop,0),new G.Point(axes[0],terminalTop,0));
            // Continue outside the connection envelope; model geometry never gets a display offset.
            double leftLine=Math.Min(view.Parts.SelectMany(p=>p.Vertices).Min(p=>p.X),axis);
            double topLine=Math.Max(view.Parts.SelectMany(p=>p.Vertices).Max(p=>p.Y),top);
            foreach(var p in connections)
            {
                double line=Dot(p.Feet[0],p.Normal)+p.Distance;
                if(p.Normal.X < -0.99) leftLine=Math.Min(leftLine,-line);
                if(p.Normal.Y > 0.99) topLine=Math.Max(topLine,line);
            }
            foreach(var p in plans)
            {
                double paper=p.Kind=="COLUMN-PLATE-CHAIN"?10:p.Kind=="COLUMN-PLATE-TOTAL"?20:p.Kind=="COLUMN-FLOOR-CHAIN"?30:p.Kind=="FLOOR-TOTAL"?40:10;
                double line=p.Normal.X<0?-leftLine+paper*view.Scale:topLine+paper*view.Scale;
                p.Distance=line-Dot(p.Feet[0],p.Normal);
                p.RequireExactFeet=p.Kind=="COLUMN-PLATE-CHAIN" || p.Kind=="COLUMN-PLATE-TOTAL" || p.Kind=="COLUMN-FLOOR-CHAIN";
                p.Satisfied=view.Dimensions.Any(d=>Matches(p,d));
            }
            // Older builds left no ownership tag. Recognize their exact REF
            // footprints only to STOP, never delete an unowned/manual object.
            var old=new List<Plan>();
            Add(old,view,"OLD-REF-CHAIN","",left,10,nodes.Select(y=>new G.Point(axis,y,0)).ToArray());
            Add(old,view,"OLD-REF-TOTAL","",left,20,new G.Point(axis,nodes.First(),0),new G.Point(axis,nodes.Last(),0));
            var oldLevels=Stations(new[] { new G.Point(0,floors[0],0),new G.Point(0,floors[1],0),new G.Point(0,nodes.First(),0),new G.Point(0,nodes.Last(),0) },new G.Vector(0,1,0));
            Add(old,view,"OLD-FLOOR-CHAIN","",left,30,oldLevels.Select(y=>new G.Point(axis,y,0)).ToArray());
            foreach(var prior in old)
            {
                prior.RequireExactFeet=true;
                if(view.Dimensions.Any(d=>Matches(prior,d) && !plans.Any(p=>Matches(p,d))))
                    throw new InvalidOperationException("VIEW="+view.Id+" còn DIM chuỗi cũ bám REF ("+prior.Kind+"). Cần thay DIM này bằng chân mép plate; dừng để tránh chồng DIM hoặc xóa DIM thủ công.");
            }
            return plans;
        }

        private sealed class PlateFoot
        {
            public G.Point Point;
            public string Evidence;
        }

        // REF identifies connection stations only. The measured feet belong to
        // actual base/diaphragm plate faces, including for an end-on child beam.
        private static List<PlateFoot> ResolveColumnPlateFeet(View view, List<Part> columns,
            List<double> nodes, double axis, double depth)
        {
            var roles=new Dictionary<int,bool>(); // true = upper outside face
            Action<double,bool> register=(station,upper)=>
            {
                int index=nodes.FindIndex(y=>Math.Abs(y-station)<=GeometryTolerance);
                if(index<0) throw new InvalidOperationException("Mép nhánh không khớp node cột; chưa đủ bằng chứng chọn plate.");
                bool previous;
                if(roles.TryGetValue(index,out previous) && previous!=upper)
                    throw new InvalidOperationException("Một node có hai vai trò mép plate trái ngược; dừng trước khi DIM.");
                roles[index]=upper;
            };
            foreach(var beam in view.Parts)
            {
                string profile=(beam.Profile??"").Trim().ToUpperInvariant();
                if(beam.Plate || beam.Assembly!=view.Main.Assembly || beam.Reference.Count!=2
                    || beam.Vertices.Count==0 || !(profile.StartsWith("H") || profile.StartsWith("I") || profile.StartsWith("BH"))) continue;
                var ends=beam.Reference.OrderBy(p=>(p.X-axis)*(p.X-axis)+(p.Z-depth)*(p.Z-depth)).ToArray();
                var node=ends[0]; var end=ends[1];
                if(Math.Abs(node.X-axis)>GeometryTolerance || Math.Abs(node.Z-depth)>GeometryTolerance
                    || Math.Abs(node.Y-end.Y)>GeometryTolerance || !Between(node.Y,nodes.First(),nodes.Last())) continue;
                double dx=end.X-node.X, dz=end.Z-node.Z, length=Math.Sqrt(dx*dx+dz*dz);
                if(length<100) continue;
                dx/=length; dz/=length;
                Func<G.Point,double> projection=p=>(p.X-node.X)*dx+(p.Z-node.Z)*dz;
                double terminal=beam.Vertices.Max(projection);
                if(terminal<100) continue;
                var face=beam.Vertices.Where(p=>Math.Abs(projection(p)-terminal)<=GeometryTolerance).ToList();
                double low=face.Min(p=>p.Y), high=face.Max(p=>p.Y);
                if(high-low<=GeometryTolerance) continue;
                if(Math.Min(Math.Abs(node.Y-low),Math.Abs(node.Y-high))>GeometryTolerance)
                    throw new InvalidOperationException("REF nhánh P"+beam.Id+" không trùng mép tiết diện; cần quy tắc hình học riêng.");
                register(low,false); register(high,true);
            }
            if(roles.Count==0) throw new InvalidOperationException("Không chứng minh được cặp diaphragm trên/dưới của liên kết.");
            if(roles.ContainsKey(0) && roles[0]) throw new InvalidOperationException("Base plate role conflicts with connection.");
            roles[0]=false;
            if(!roles.ContainsKey(nodes.Count-1)) roles[nodes.Count-1]=true;
            var result=new List<PlateFoot>(); var owners=new HashSet<int>();
            for(int i=0;i<nodes.Count;i++)
            {
                bool upper;
                if(!roles.TryGetValue(i,out upper)) throw new InvalidOperationException("Node cột chưa có vai trò diaphragm rõ ràng: "+F(nodes[i]));
                var adjacent=columns.Where(c=>Between(nodes[i],c.Reference.Min(p=>p.Y),c.Reference.Max(p=>p.Y))).ToList();
                double colMinX=adjacent.Min(c=>c.Vertices.Min(p=>p.X)), colMaxX=adjacent.Max(c=>c.Vertices.Max(p=>p.X));
                double colMinZ=adjacent.Min(c=>c.Vertices.Min(p=>p.Z)), colMaxZ=adjacent.Max(c=>c.Vertices.Max(p=>p.Z));
                var candidates=view.Parts.Where(p=>p.Plate && p.Assembly==view.Main.Assembly && p.Vertices.Count>0)
                    .Where(p=>
                    {
                        double lo=p.Vertices.Min(v=>v.Y), hi=p.Vertices.Max(v=>v.Y);
                        double x0=p.Vertices.Min(v=>v.X), x1=p.Vertices.Max(v=>v.X);
                        double z0=p.Vertices.Min(v=>v.Z), z1=p.Vertices.Max(v=>v.Z);
                        return Between(nodes[i],lo,hi) && hi-lo>DimensionTolerance
                            && hi-lo<0.5*Math.Min(x1-x0,z1-z0)
                            && x0<=colMinX+GeometryTolerance && x1>=colMaxX-GeometryTolerance
                            && z0<=colMinZ+GeometryTolerance && z1>=colMaxZ-GeometryTolerance;
                    }).ToList();
                if(candidates.Count!=1) throw new InvalidOperationException("Node "+F(nodes[i])+" cần đúng một plate ngang bao cột; tìm thấy "+candidates.Count+".");
                var plate=candidates[0];
                if(!owners.Add(plate.Id)) throw new InvalidOperationException("Hai node cùng chọn một plate; dừng DIM chuỗi.");
                double y=upper?plate.Vertices.Max(p=>p.Y):plate.Vertices.Min(p=>p.Y);
                var foot=plate.Vertices.Where(p=>Math.Abs(p.Y-y)<=DimensionTolerance)
                    .OrderBy(p=>p.X).ThenBy(p=>Math.Abs(p.Z-depth)).ThenBy(p=>p.Z).First();
                if(result.Count>0 && foot.Y-result.Last().Point.Y<=GeometryTolerance)
                    throw new InvalidOperationException("Các mép plate không theo thứ tự tăng dọc cột.");
                result.Add(new PlateFoot { Point=new G.Point(foot.X,foot.Y,0),
                    Evidence="P"+plate.Id+" "+(i==0?"base underside":upper?"upper outside face":"lower outside face") });
            }
            return result;
        }
    }
}
#endregion

#region PHU_Slot07_DrawingRoute - Kiểu tuyến và scope phân tuyến bản vẽ
namespace Tekla.Technology.Akit.UserScript
{


    internal enum Slot07DrawingRoute
    {
        Beam,
        LegacyColumn,
        HollowColumn,
        UnsupportedColumn
    }

    /// <summary>Single Slot 07 decision made before any beam context or writer.
    /// Orientation comes from authoritative MainPart REF in global coordinates;
    /// profile families come from the existing product profile classifier.</summary>
    internal static class PHU_Slot07_DrawingRoute
    {
        internal static Slot07DrawingRoute Resolve(bool vertical, bool legacyShape, bool hollow)
        {
            if (!vertical) return Slot07DrawingRoute.Beam;
            if (legacyShape) return Slot07DrawingRoute.LegacyColumn;
            if (hollow) return Slot07DrawingRoute.HollowColumn;
            return Slot07DrawingRoute.UnsupportedColumn;
        }
    }
}
#endregion
