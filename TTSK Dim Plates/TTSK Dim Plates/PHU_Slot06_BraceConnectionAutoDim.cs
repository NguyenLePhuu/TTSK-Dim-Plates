// Slot 06: DIM liên kết giằng, tự nhận topology Type 1 hoặc Type 2.
// LUỒNG: Run → AnalyzeDrawing → BuildPlans/ValidatePlans → chụp và xóa DIM thay thế → tạo DIM thẳng/góc → CommitChanges.
// ĐIỂM SỬA: Type 1 trong phần đầu; Type 2 ở region cuối; chọn topology trong AnalyzeDrawing.
// THAM SỐ: ExpectedPlanCount và Type2ExpectedPlanCount là số kiểm chứng topology, không phải nút tăng/giảm DIM.
// LƯU Ý: luồng hiện tại xóa DIM cũ trước khi tạo; catch dọn DIM mới, không tự khôi phục toàn bộ DIM cũ.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_Slot06_BraceConnectionAutoDim.cs
//   PHU_Slot06_BraceConnectionAutoDim.Type2.cs

#region PHU_Slot06_BraceConnectionAutoDim - Điểm gọi và thuật toán liên kết giằng
#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
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

    /// <summary>
    /// Slot 06 - lien ket giang xeo: ba thanh L va mot plate lien ket o nut giua.
    /// </summary>
    public class PHU_AutoDimSlot06
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunMessage = String.Empty;
            string message = PHU_Slot06DiagonalBraceDimensionEngine.Run();
            if (!String.IsNullOrWhiteSpace(message))
            {
                LastRunSucceeded = true;
                LastRunMessage = message;
            }
        }

        /// <summary>Read-only audit. No Tekla drawing object is mutated.</summary>
        public static string AuditPlan()
        {
            return PHU_Slot06DiagonalBraceDimensionEngine.AuditPlan();
        }
    }

    internal static partial class PHU_Slot06DiagonalBraceDimensionEngine
    {
        // KIỂM CHỨNG TYPE 1: tổng 31 plan thẳng = 30 plan view + 1 section; DIM góc kiểm riêng.
        // Đổi số chỉ khi đã đổi topology/BuildPlans và kiểm lại đủ chân; không dùng để bỏ qua thiếu DIM.
        private const int ExpectedPlanCount = 31;
        private const int PlanViewPlanCount = 30;
        private const int SectionViewPlanCount = 1;
        // CHỈNH NGƯỠNG: PointTolerance/MatchTolerance/MinimumMeasuredSpan tính mm view;
        // DirectionTolerance là cosine hướng, không phải độ. Nới MatchTolerance có thể đổi tập DIM thay thế.
        private const double PointTolerance = 0.75;
        private const double MatchTolerance = 5.0;
        private const double DirectionTolerance = 0.985;
        private const double MinimumMeasuredSpan = 0.5;

        private enum TopologyVariant
        {
            Unknown,
            Type1,
            Type2
        }

        // All placement values are paper millimetres. Geometry resolvers never
        // consume these values. Edit/debug the visual hierarchy only here.
        private enum TierBand
        {
            SectionProfile,
            MainMajorInner,
            MainMajorMiddle,
            MainMajorOuter,
            CrossMajorInner,
            CrossMajorOuter,
            CrossLocalEdge,
            CrossLocalBolt,
            MainLocalStartEdge,
            MainLocalJointBolts,
            MainLocalEndEdge,
            MainEndWidth,
            MainEndWidthWithBolt,
            MainStartWidthWithBolt,
            MainStartWidth,
            CrossStartWidthWithBolt,
            CrossStartWidth,
            CrossStartJointWidth,
            CrossEndJointWidth,
            CrossEndWidthWithBolt,
            CrossEndWidth,
            BoundaryTopInner,
            BoundaryTopOuter,
            BoundaryBottomInner,
            BoundaryBottomOuter,
            BoundaryLeftInner,
            BoundaryLeftOuter,
            BoundaryRightInner,
            BoundaryRightOuter
        }

        private static double PaperDistance(TierBand band)
        {
            switch (band)
            {
                case TierBand.SectionProfile:
                    return 13.55;
                case TierBand.MainMajorInner:
                    return 63.55;
                case TierBand.MainMajorMiddle:
                    return 79.40;
                case TierBand.MainMajorOuter:
                    return 91.95;
                case TierBand.CrossMajorInner:
                    return 72.15;
                case TierBand.CrossMajorOuter:
                    return 84.15;
                case TierBand.CrossLocalEdge:
                    return 15.25;
                case TierBand.CrossLocalBolt:
                    return 17.25;
                case TierBand.MainLocalStartEdge:
                    return 11.90;
                case TierBand.MainLocalJointBolts:
                    return 14.05;
                case TierBand.MainLocalEndEdge:
                    return 17.20;
                case TierBand.MainEndWidth:
                    return 14.10;
                case TierBand.MainEndWidthWithBolt:
                    return 7.85;
                case TierBand.MainStartWidthWithBolt:
                    return 15.60;
                case TierBand.MainStartWidth:
                    return 24.45;
                case TierBand.CrossStartWidthWithBolt:
                    return 14.10;
                case TierBand.CrossStartWidth:
                    return 22.30;
                case TierBand.CrossStartJointWidth:
                    return 29.20;
                case TierBand.CrossEndJointWidth:
                    return 23.80;
                case TierBand.CrossEndWidthWithBolt:
                    return 37.30;
                case TierBand.CrossEndWidth:
                    return 45.20;
                case TierBand.BoundaryTopInner:
                    return 52.00;
                case TierBand.BoundaryTopOuter:
                    return 62.75;
                case TierBand.BoundaryBottomInner:
                    return 29.70;
                case TierBand.BoundaryBottomOuter:
                    return 40.75;
                case TierBand.BoundaryLeftInner:
                    return 97.40;
                case TierBand.BoundaryLeftOuter:
                    return 110.75;
                case TierBand.BoundaryRightInner:
                    return 120.55;
                case TierBand.BoundaryRightOuter:
                    return 132.85;
                default:
                    throw new ArgumentOutOfRangeException("band");
            }
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

        private sealed class Segment2
        {
            public P2 A;
            public P2 B;

            public Segment2(P2 a, P2 b)
            {
                A = a;
                B = b;
            }
        }

        private sealed class BoltData
        {
            public int Id;
            public readonly List<P2> Points = new List<P2>();
        }

        private sealed class PartData
        {
            public TSM.Part ModelPart;
            public int ModelId;
            public int AssemblyId;
            public bool IsMain;
            public string Name;
            public string Profile;
            public readonly List<P2> Reference = new List<P2>();
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<Segment2> Segments = new List<Segment2>();
            public readonly Dictionary<int, BoltData> BoltGroups = new Dictionary<int, BoltData>();
        }

        private sealed class ViewData
        {
            public TSD.View View;
            public string Label;
            public double Scale;
            public readonly List<PartData> Parts = new List<PartData>();
            public PartData Main;
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes DimensionAttributes;
        }

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public TSM.TransformationPlane OriginalPlane;
            public readonly List<ViewData> Views = new List<ViewData>();
            public ViewData PlanView;
            public ViewData SectionView;
            public Topology Topology;
            public Type2Topology Type2Topology;
            public TopologyVariant Variant;
        }

        private sealed class TerminalFeature
        {
            public P2 Low;
            public P2 High;
            public P2 ReferenceIntersection;
        }

        private sealed class Topology
        {
            public PartData Main;
            public PartData CrossStart;
            public PartData CrossEnd;
            public PartData Plate;
            public P2 A;
            public P2 B;
            public P2 C;
            public P2 D;
            public P2 Joint;
            public P2 MainAxis;
            public P2 MainNormal;
            public P2 CrossAxis;
            public P2 CrossNormal;
        }

        private sealed class DimPlan
        {
            public string Name;
            public string Semantic;
            public ViewData View;
            public readonly List<P2> Points = new List<P2>();
            public P2 MeasurementAxis;
            public P2 PlacementNormal;
            public TierBand Tier;
            public bool DisableCombine = true;
            public double? DistanceOverride;

            public double Distance
            {
                get { return DistanceOverride ?? PaperDistance(Tier) * View.Scale; }
            }
        }

        private sealed class ReplacementSnapshot
        {
            public readonly List<TSD.DrawingObject> Matched =
                new List<TSD.DrawingObject>();
            public int ExistingCount;
            public int ProtectedCount;
        }

        internal static string Run()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            List<TSD.AngleDimension> createdAngles = new List<TSD.AngleDimension>();
            int deleted = 0;
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildPlans(context);
                ValidatePlans(context, plans);
                List<BraceAnglePlan> angles = BuildBraceAnglePlans(context);
                ReplacementSnapshot replacement = SnapshotReplaceableDimensions(context, plans);

                // Hình học và kế hoạch của topology đã chọn đã qua preflight.
                // Thứ tự đang chạy: xóa toàn bộ DIM thuộc snapshot thay thế trước, rồi tạo DIM mới.
                for (int i = 0; i < replacement.Matched.Count; i++)
                {
                    if (replacement.Matched[i] == null || !replacement.Matched[i].Delete())
                        throw new InvalidOperationException("Khong xoa duoc DIM cu; dung truoc khi tao DIM moi.");
                    deleted++;
                }
                if (deleted != replacement.Matched.Count)
                    throw new InvalidOperationException(
                        "Khong xoa duoc day du dimension Slot 06 cu; dung truoc CommitChanges."
                    );

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList pointList = new TSD.PointList();
                    for (int p = 0; p < plan.Points.Count; p++)
                        pointList.Add(plan.Points[p].ToPoint());

                    TSG.Vector normal = new TSG.Vector(
                        plan.PlacementNormal.X,
                        plan.PlacementNormal.Y,
                        0.0
                    );
                    TSD.StraightDimensionSet dimension =
                        plan.View.DimensionAttributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                pointList,
                                normal,
                                plan.Distance
                            )
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                pointList,
                                normal,
                                plan.Distance,
                                plan.View.DimensionAttributes
                            );
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla khong tao duoc " + plan.Name + "."
                        );

                    created.Add(dimension);
                    if (plan.DisableCombine)
                        DisableCombineAndVerify(plan, dimension);
                }

                if (created.Count != plans.Count)
                    throw new InvalidOperationException(
                        "So dimension tao duoc khong khop plan da preflight."
                    );

                foreach (BraceAnglePlan angle in angles)
                {
                    TSD.AngleDimension dimension = new TSD.AngleDimension(
                        angle.View.View, angle.Origin.ToPoint(), angle.Point1.ToPoint(),
                        angle.Point2.ToPoint(), 0.0, CreateBraceAngleAttributes());
                    if (!dimension.Insert())
                        throw new InvalidOperationException("Khong tao duoc " + angle.Name);
                    createdAngles.Add(dimension);
                    if (!dimension.Select()
                        || dimension.Attributes.Type != TSD.AngleTypes.TriangleWithDegrees
                        || dimension.Attributes.TriangleBase != 1000
                        || Math.Abs(dimension.GetAngle() - angle.Degrees) > 0.001)
                        throw new InvalidOperationException("Dim goc read-back sai: " + angle.Name);
                }
                context.Drawing.CommitChanges();
                return "Slot 06: tao "
                    + created.Count
                    + " dim thang, " + createdAngles.Count + " dim goc, thay "
                    + deleted
                    + " dim cu tren toan bo ban ve";
            }
            catch (Exception ex)
            {
                foreach (TSD.AngleDimension angle in createdAngles)
                {
                    try { angle.Delete(); } catch { }
                }
                for (int i = 0; i < created.Count; i++)
                {
                    try
                    {
                        if (created[i] != null)
                            created[i].Delete();
                    }
                    catch { }
                }

                ShowWarning("Slot 06 - lien ket giang xeo da dung.\r\n"
                    + "Da xoa " + deleted + " DIM cu; chua commit. "
                    + "Neu da xoa DIM, can Undo/kiem tra ban ve truoc khi luu.\r\n\r\n" + ex.Message);
                return null;
            }
        }

        internal static string AuditPlan()
        {
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildPlans(context);
                ValidatePlans(context, plans);
                ReplacementSnapshot replacement = SnapshotReplaceableDimensions(context, plans);

                StringBuilder text = new StringBuilder();
                text.AppendLine("SLOT 06 DIAGONAL BRACE PLAN AUDIT - READ ONLY");
                text.AppendLine("No dimension was created, modified, deleted or committed.");
                if (context.Variant == TopologyVariant.Type2)
                {
                    text.Append("Variant=Type2 MainPartId=")
                        .Append(context.Type2Topology.Main.ModelId)
                        .Append(" CrossId=")
                        .Append(context.Type2Topology.Cross.ModelId)
                        .Append(" PlateId=")
                        .Append(context.Type2Topology.Plate.ModelId)
                        .AppendLine();
                }
                else
                {
                    text.Append("MainPartId=")
                        .Append(context.Topology.Main.ModelId)
                        .Append(" CrossStartId=")
                        .Append(context.Topology.CrossStart.ModelId)
                        .Append(" CrossEndId=")
                        .Append(context.Topology.CrossEnd.ModelId)
                        .Append(" PlateId=")
                        .Append(context.Topology.Plate.ModelId)
                        .AppendLine();
                }
                text.Append("PlanView=")
                    .Append(context.PlanView.Label)
                    .Append(" scale=")
                    .Append(Format(context.PlanView.Scale))
                    .Append(" SectionView=")
                    .Append(context.SectionView.Label)
                    .Append(" scale=")
                    .Append(Format(context.SectionView.Scale))
                    .AppendLine();
                text.Append("PlanCount=")
                    .Append(plans.Count)
                    .Append(" ExistingDimensions=")
                    .Append(replacement.ExistingCount)
                    .Append(" DeleteBeforeCreate=")
                    .Append(replacement.Matched.Count)
                    .Append(" Protected=")
                    .Append(replacement.ProtectedCount)
                    .AppendLine();
                if (context.Variant == TopologyVariant.Type2)
                {
                    text.Append("Reference A=")
                        .Append(FormatPoint(context.Type2Topology.A))
                        .Append(" B=")
                        .Append(FormatPoint(context.Type2Topology.B))
                        .Append(" C=")
                        .Append(FormatPoint(context.Type2Topology.C))
                        .Append(" D=")
                        .Append(FormatPoint(context.Type2Topology.D))
                        .Append(" Joint=")
                        .Append(FormatPoint(context.Type2Topology.Joint))
                        .AppendLine();
                }
                else
                {
                    text.Append("Reference A=")
                        .Append(FormatPoint(context.Topology.A))
                        .Append(" B=")
                        .Append(FormatPoint(context.Topology.B))
                        .Append(" C=")
                        .Append(FormatPoint(context.Topology.C))
                        .Append(" D=")
                        .Append(FormatPoint(context.Topology.D))
                        .Append(" Joint=")
                        .Append(FormatPoint(context.Topology.Joint))
                        .AppendLine();
                }

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append(plan.Name)
                        .Append(" view=")
                        .Append(plan.View.Label)
                        .Append(" semantic=")
                        .Append(plan.Semantic)
                        .Append(" axis=")
                        .Append(FormatPoint(plan.MeasurementAxis))
                        .Append(" normal=")
                        .Append(FormatPoint(plan.PlacementNormal))
                        .Append(" tier=")
                        .Append(plan.Tier)
                        .Append(" paperMm=")
                        .Append(Format(plan.Distance / plan.View.Scale))
                        .Append(" distance=")
                        .Append(Format(plan.Distance))
                        .Append(" points=");
                    for (int p = 0; p < plan.Points.Count; p++)
                    {
                        if (p > 0)
                            text.Append(";");
                        text.Append(FormatPoint(plan.Points[p]));
                    }
                    text.AppendLine();
                }
                foreach (BraceAnglePlan angle in BuildBraceAnglePlans(context))
                    text.AppendLine(angle.Name + " origin=" + FormatPoint(angle.Origin)
                        + " p1=" + FormatPoint(angle.Point1) + " p2=" + FormatPoint(angle.Point2)
                        + " degrees=" + Format(angle.Degrees)
                        + " type=TriangleWithDegrees base=1000 distance=0");
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "SLOT 06 DIAGONAL BRACE PLAN AUDIT FAILED\r\n" + ex;
            }
        }

        private static Context AnalyzeDrawing()
        {
            Context context = new Context();
            context.Model = new TSM.Model();
            TSD.DrawingHandler drawingHandler = new TSD.DrawingHandler();
            if (!context.Model.GetConnectionStatus() || !drawingHandler.GetConnectionStatus())
                throw new InvalidOperationException("Khong ket noi duoc Tekla Model/Drawing API.");

            context.Drawing = drawingHandler.GetActiveDrawing();
            if (context.Drawing == null)
                throw new InvalidOperationException("Khong co ban ve dang mo.");

            context.MainPart = PHU_MainPartResolver.Resolve(context.Model, context.Drawing);
            if (context.MainPart == null)
                throw new InvalidOperationException(
                    "Khong xac dinh duoc Assembly MainPart cua ban ve."
                );

            context.OriginalPlane = context
                .Model.GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();
            TSG.Matrix currentToGlobal = context.OriginalPlane.TransformationMatrixToGlobal;

            TSD.DrawingObjectEnumerator views = context.Drawing.GetSheet().GetAllViews();
            int index = 0;
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null)
                    continue;
                ViewData data = ReadView(
                    context.Model,
                    context.MainPart,
                    view,
                    currentToGlobal,
                    ++index
                );
                if (data.Main != null)
                    context.Views.Add(data);
            }

            if (context.Views.Count < 2)
                throw new InvalidOperationException("Can toi thieu hai view co chua MainPart.");

            List<Tuple<ViewData, Topology>> supported = new List<Tuple<ViewData, Topology>>();
            List<Tuple<ViewData, Type2Topology>> supportedType2 =
                new List<Tuple<ViewData, Type2Topology>>();
            for (int i = 0; i < context.Views.Count; i++)
            {
                Topology topology;
                if (TryResolvePlanTopology(context.Views[i], out topology))
                    supported.Add(Tuple.Create(context.Views[i], topology));
                Type2Topology type2Topology;
                if (TryResolvePlanTopologyType2(context.Views[i], out type2Topology))
                    supportedType2.Add(Tuple.Create(context.Views[i], type2Topology));
            }
            if (supported.Count + supportedType2.Count != 1)
                throw new InvalidOperationException(
                    "Khong xac dinh duy nhat topology Slot 06. Type1="
                        + supported.Count
                        + ", Type2="
                        + supportedType2.Count
                        + "."
                );

            if (supported.Count == 1)
            {
                context.Variant = TopologyVariant.Type1;
                context.PlanView = supported[0].Item1;
                context.Topology = supported[0].Item2;
                for (int i = 0; i < context.Views.Count; i++)
                {
                    ViewData candidate = context.Views[i];
                    if (Object.ReferenceEquals(candidate, context.PlanView))
                        continue;
                    if (
                        FindPart(candidate, context.Topology.CrossStart.ModelId) != null
                        && FindPart(candidate, context.Topology.CrossEnd.ModelId) != null
                        && FindPart(candidate, context.Topology.Plate.ModelId) != null
                    )
                    {
                        if (context.SectionView != null)
                            throw new InvalidOperationException(
                                "Co nhieu hon mot view tiet dien phu hop Slot 06."
                            );
                        context.SectionView = candidate;
                    }
                }
                if (context.SectionView == null)
                    throw new InvalidOperationException(
                        "Khong tim thay view tiet dien cua lien ket giang xeo."
                    );
            }
            else
            {
                context.Variant = TopologyVariant.Type2;
                context.PlanView = supportedType2[0].Item1;
                context.Type2Topology = supportedType2[0].Item2;
                context.SectionView = ResolveType2SectionView(context);
            }

            return context;
        }

        private static ViewData ReadView(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal,
            int index
        )
        {
            ViewData result = new ViewData();
            result.View = view;
            result.Label =
                "V"
                + index.ToString("00", CultureInfo.InvariantCulture)
                + "["
                + SafeViewName(view)
                + "]";
            result.Scale = ReadScale(view);
            if (!IsFinite(result.Scale) || result.Scale <= 0.0)
                throw new InvalidOperationException(
                    "Khong doc duoc scale cua " + result.Label + "."
                );
            result.DimensionAttributes = ReadDimensionAttributes(view);

            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem
            );
            TSD.DrawingObjectEnumerator drawingParts = view.GetAllObjects(typeof(TSD.Part));
            while (drawingParts != null && drawingParts.MoveNext())
            {
                TSD.Part drawingPart = drawingParts.Current as TSD.Part;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;
                TSM.Part modelPart =
                    model.SelectModelObject(drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null)
                    continue;

                PartData part = ReadPart(modelPart, mainPart, currentToGlobal, globalToView);
                if (part.Vertices.Count == 0)
                    continue;
                result.Parts.Add(part);
                if (part.IsMain)
                    result.Main = part;
            }
            return result;
        }

        private static PartData ReadPart(
            TSM.Part modelPart,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            PartData result = new PartData();
            result.ModelPart = modelPart;
            result.ModelId = modelPart.Identifier == null ? 0 : modelPart.Identifier.ID;
            result.IsMain = SameIdentifier(modelPart, mainPart);
            result.Name = SafeUpper(modelPart.Name);
            result.Profile = SafeUpper(
                modelPart.Profile == null ? "" : modelPart.Profile.ProfileString
            );
            try
            {
                TSM.Assembly assembly = modelPart.GetAssembly();
                result.AssemblyId =
                    assembly == null || assembly.Identifier == null ? 0 : assembly.Identifier.ID;
            }
            catch
            {
                result.AssemblyId = 0;
            }

            try
            {
                ArrayList reference = modelPart.GetReferenceLine(false);
                if (reference != null)
                {
                    foreach (object value in reference)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUnique(
                                result.Reference,
                                Transform(point, currentToGlobal, globalToView)
                            );
                    }
                }
            }
            catch { }

            try
            {
                TSM.Solid solid = modelPart.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge = edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null || edge.StartPoint == null || edge.EndPoint == null)
                        continue;
                    P2 a = Transform(edge.StartPoint, currentToGlobal, globalToView);
                    P2 b = Transform(edge.EndPoint, currentToGlobal, globalToView);
                    AddUnique(result.Vertices, a);
                    AddUnique(result.Vertices, b);
                    AddUniqueSegment(result.Segments, a, b);
                }
            }
            catch { }

            try
            {
                TSM.ModelObjectEnumerator bolts = modelPart.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    TSM.BoltGroup group = bolts.Current as TSM.BoltGroup;
                    if (group == null || group.Identifier == null || group.BoltPositions == null)
                        continue;
                    int id = group.Identifier.ID;
                    BoltData data;
                    if (!result.BoltGroups.TryGetValue(id, out data))
                    {
                        data = new BoltData();
                        data.Id = id;
                        result.BoltGroups.Add(id, data);
                    }
                    foreach (object value in group.BoltPositions)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUnique(data.Points, Transform(point, currentToGlobal, globalToView));
                    }
                }
            }
            catch { }
            return result;
        }

        private static bool TryResolvePlanTopology(ViewData view, out Topology topology)
        {
            topology = null;
            if (view == null || view.Main == null)
                return false;

            P2 main0;
            P2 main1;
            if (!TryFarthestReferencePair(view.Main, out main0, out main1))
                return false;
            P2 mainDirection = Normalize(Subtract(main1, main0));
            double mainLength = Distance(main0, main1);
            if (mainDirection == null || mainLength <= PointTolerance)
                return false;

            List<PartData> candidates = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.IsMain || part.AssemblyId != view.Main.AssemblyId)
                    continue;
                P2 a;
                P2 b;
                if (!TryFarthestReferencePair(part, out a, out b))
                    continue;
                double length = Distance(a, b);
                if (length < mainLength * 0.05)
                    continue;
                candidates.Add(part);
            }

            List<Topology> matches = new List<Topology>();
            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    PartData first = candidates[i];
                    PartData second = candidates[j];
                    P2 f0;
                    P2 f1;
                    P2 s0;
                    P2 s1;
                    if (
                        !TryFarthestReferencePair(first, out f0, out f1)
                        || !TryFarthestReferencePair(second, out s0, out s1)
                    )
                        continue;

                    P2 jointFirst;
                    P2 outerFirst;
                    P2 jointSecond;
                    P2 outerSecond;
                    if (
                        !TrySharedEndpoint(
                            f0,
                            f1,
                            s0,
                            s1,
                            out jointFirst,
                            out outerFirst,
                            out jointSecond,
                            out outerSecond
                        )
                    )
                        continue;

                    P2 crossDirection = Normalize(Subtract(outerSecond, outerFirst));
                    if (
                        crossDirection == null
                        || Math.Abs(Dot(mainDirection, crossDirection)) >= DirectionTolerance
                    )
                        continue;

                    P2 joint = Midpoint(jointFirst, jointSecond);
                    P2 intersection;
                    if (
                        !TryInfiniteLineIntersection(
                            main0,
                            main1,
                            outerFirst,
                            outerSecond,
                            out intersection
                        )
                        || Distance(intersection, joint) > Math.Max(2.0, mainLength * 0.002)
                    )
                        continue;

                    bool direct =
                        Distance(main0, outerFirst) + Distance(main1, outerSecond)
                        <= Distance(main0, outerSecond) + Distance(main1, outerFirst);

                    Topology item = new Topology();
                    if (direct)
                    {
                        item.A = main0;
                        item.B = outerFirst;
                        item.D = main1;
                        item.C = outerSecond;
                        item.CrossStart = first;
                        item.CrossEnd = second;
                    }
                    else
                    {
                        item.A = main0;
                        item.B = outerSecond;
                        item.D = main1;
                        item.C = outerFirst;
                        item.CrossStart = second;
                        item.CrossEnd = first;
                    }
                    item.Main = view.Main;
                    item.Joint = joint;
                    item.MainAxis = Normalize(Subtract(item.D, item.A));
                    item.CrossAxis = Normalize(Subtract(item.C, item.B));
                    if (item.MainAxis == null || item.CrossAxis == null)
                        continue;

                    P2 mainPerpendicular = PerpendicularLeft(item.MainAxis);
                    if (Dot(Subtract(item.C, item.Joint), mainPerpendicular) < 0.0)
                        mainPerpendicular = Scale(mainPerpendicular, -1.0);
                    item.MainNormal = mainPerpendicular;

                    P2 crossPerpendicular = PerpendicularLeft(item.CrossAxis);
                    if (Dot(Subtract(item.D, item.Joint), crossPerpendicular) < 0.0)
                        crossPerpendicular = Scale(crossPerpendicular, -1.0);
                    item.CrossNormal = crossPerpendicular;

                    item.Plate = ResolveConnectionPlate(
                        view,
                        item.Main,
                        item.CrossStart,
                        item.CrossEnd
                    );
                    if (item.Plate == null)
                        continue;
                    matches.Add(item);
                }
            }

            if (matches.Count != 1)
                return false;
            topology = matches[0];
            return true;
        }

        private static PartData ResolveConnectionPlate(
            ViewData view,
            PartData main,
            PartData crossStart,
            PartData crossEnd
        )
        {
            List<PartData> matches = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (
                    candidate.ModelId == main.ModelId
                    || candidate.ModelId == crossStart.ModelId
                    || candidate.ModelId == crossEnd.ModelId
                    || candidate.AssemblyId != main.AssemblyId
                )
                    continue;
                if (
                    SharesBoltGroup(candidate, main)
                    && SharesBoltGroup(candidate, crossStart)
                    && SharesBoltGroup(candidate, crossEnd)
                )
                    matches.Add(candidate);
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool SharesBoltGroup(PartData first, PartData second)
        {
            foreach (KeyValuePair<int, BoltData> pair in first.BoltGroups)
            {
                if (second.BoltGroups.ContainsKey(pair.Key))
                    return true;
            }
            return false;
        }

        private static List<DimPlan> BuildPlans(Context context)
        {
            if (context.Variant == TopologyVariant.Type2)
                return BuildType2Plans(context);
            if (context.Variant != TopologyVariant.Type1)
                throw new InvalidOperationException("Topology Slot 06 chua duoc phan loai.");
            List<DimPlan> plans = new List<DimPlan>();
            BuildPlanViewPlans(context, plans);
            BuildSectionViewPlan(context, plans);
            return plans;
        }

        private static void BuildPlanViewPlans(Context context, List<DimPlan> plans)
        {
            Topology t = context.Topology;
            ViewData view = context.PlanView;

            TerminalFeature mainA = ResolveTerminal(t.Main, t.A, t.MainNormal);
            TerminalFeature mainD = ResolveTerminal(t.Main, t.D, t.MainNormal);
            TerminalFeature crossBOuter = ResolveTerminal(t.CrossStart, t.B, t.CrossNormal);
            TerminalFeature crossBJoint = ResolveTerminal(t.CrossStart, t.Joint, t.CrossNormal);
            TerminalFeature crossCOuter = ResolveTerminal(t.CrossEnd, t.C, t.CrossNormal);
            TerminalFeature crossCJoint = ResolveTerminal(t.CrossEnd, t.Joint, t.CrossNormal);

            P2 mainABolt = ResolveTerminalBolt(t.Main, t.A);
            P2 mainDBolt = ResolveTerminalBolt(t.Main, t.D);
            P2 mainJointBolt = ResolveTerminalBolt(t.Main, t.Joint);
            P2 crossBOuterBolt = ResolveTerminalBolt(t.CrossStart, t.B);
            P2 crossBJointBolt = ResolveTerminalBolt(t.CrossStart, t.Joint);
            P2 crossCOuterBolt = ResolveTerminalBolt(t.CrossEnd, t.C);
            P2 crossCJointBolt = ResolveTerminalBolt(t.CrossEnd, t.Joint);
            List<P2> mainJointBolts = ResolveBoltGroupPoints(t.Main, t.Joint);
            // Keep the exact geometric feet, then project the dimension onto
            // the dominant view coordinate (X or Y).  The old geometric normal
            // is used only to retain the correct outside placement side.
            P2 boundaryHorizontal = Normalize(Subtract(t.B, t.A));
            P2 boundaryVertical = PerpendicularLeft(boundaryHorizontal);
            if (Dot(Subtract(t.D, t.B), boundaryVertical) < 0.0)
                boundaryVertical = Scale(boundaryVertical, -1.0);

            AddPlan(
                plans,
                view,
                "P-01",
                "main REF A -> REF D",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorOuter,
                t.A,
                t.D
            );
            AddPlan(
                plans,
                view,
                "P-02",
                "main exact terminal edge -> joint bolt -> exact terminal edge",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorInner,
                mainA.High,
                mainJointBolt,
                mainD.High
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-03",
                "top REF C -> REF D",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopOuter,
                t.C,
                t.D
            );
            AddPlan(
                plans,
                view,
                "P-04",
                "cross REF-edge-joint-edge-REF semantic chain",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossMajorInner,
                t.C,
                crossCOuter.High,
                crossCJoint.High,
                mainJointBolt,
                crossBJoint.High,
                crossBOuter.High,
                t.B
            );

            AddPlan(
                plans,
                view,
                "P-05",
                "cross C outer exact edge -> nearest bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalEdge,
                crossCOuter.High,
                crossCOuterBolt
            );
            AddPlan(
                plans,
                view,
                "P-06",
                "cross C joint exact edge -> nearest bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalBolt,
                crossCJoint.High,
                crossCJointBolt
            );
            AddPlan(
                plans,
                view,
                "P-07",
                "cross B joint exact edge -> nearest bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalEdge,
                crossBJoint.High,
                crossBJointBolt
            );
            AddPlan(
                plans,
                view,
                "P-08",
                "cross B outer exact edge -> nearest bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalBolt,
                crossBOuter.High,
                crossBOuterBolt
            );

            AddWidthPlan(
                plans,
                view,
                "P-09",
                "cross B outer profile width with nearest bolt",
                t.CrossNormal,
                Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidthWithBolt,
                crossBOuter,
                crossBOuterBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "P-10",
                "main D terminal profile width",
                t.MainNormal,
                t.MainAxis,
                TierBand.MainEndWidth,
                mainD,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "P-11",
                "main A terminal profile width with nearest bolt",
                t.MainNormal,
                Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidthWithBolt,
                mainA,
                mainABolt,
                true
            );

            AddPlan(
                plans,
                view,
                "P-12",
                "main A exact edge -> nearest terminal bolt",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainLocalStartEdge,
                mainA.High,
                mainABolt
            );

            AddPlanList(
                plans,
                view,
                "P-13",
                "all actual bolts of the main/plate joint group",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainLocalJointBolts,
                SortAlong(mainJointBolts, t.MainAxis)
            );

            AddPlan(
                plans,
                view,
                "P-14",
                "main D exact terminal edge -> nearest bolt",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainLocalEndEdge,
                mainD.High,
                mainDBolt
            );
            AddWidthPlan(
                plans,
                view,
                "P-15",
                "main D terminal profile width with nearest bolt",
                t.MainNormal,
                t.MainAxis,
                TierBand.MainEndWidthWithBolt,
                mainD,
                mainDBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "P-16",
                "cross B outer profile width",
                t.CrossNormal,
                Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidth,
                crossBOuter,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "P-17",
                "cross B joint profile width with nearest bolt",
                t.CrossNormal,
                Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartJointWidth,
                crossBJoint,
                crossBJointBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "P-18",
                "cross C joint profile width with nearest bolt",
                t.CrossNormal,
                t.CrossAxis,
                TierBand.CrossEndJointWidth,
                crossCJoint,
                crossCJointBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "P-19",
                "main A terminal profile width",
                t.MainNormal,
                Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidth,
                mainA,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "P-20",
                "cross C outer profile width with nearest bolt",
                t.CrossNormal,
                t.CrossAxis,
                TierBand.CrossEndWidthWithBolt,
                crossCOuter,
                crossCOuterBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "P-21",
                "cross C outer profile width",
                t.CrossNormal,
                t.CrossAxis,
                TierBand.CrossEndWidth,
                crossCOuter,
                null,
                false
            );

            AddPlan(
                plans,
                view,
                "P-22",
                "cross REF C -> joint -> REF B",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossMajorOuter,
                t.C,
                t.Joint,
                t.B
            );
            AddPlan(
                plans,
                view,
                "P-23",
                "main REF-edge-edge-REF semantic chain",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorMiddle,
                t.A,
                mainA.High,
                mainD.High,
                t.D
            );

            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-24",
                "top REF -> cross reference/solid intersection -> main reference/solid intersection -> REF",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopInner,
                t.C,
                crossCOuter.ReferenceIntersection,
                mainD.ReferenceIntersection,
                t.D
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-25",
                "bottom REF A -> REF B",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomOuter,
                t.A,
                t.B
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-26",
                "bottom REF -> main reference/solid intersection -> cross reference/solid intersection -> REF",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomInner,
                t.A,
                mainA.ReferenceIntersection,
                crossBOuter.ReferenceIntersection,
                t.B
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-27",
                "left REF A -> REF C",
                OutwardNormalForAxis(t.A, t.C, t.Joint, boundaryVertical),
                TierBand.BoundaryLeftOuter,
                t.A,
                t.C
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-28",
                "left REF -> main reference/solid intersection -> cross reference/solid intersection -> REF",
                OutwardNormalForAxis(t.A, t.C, t.Joint, boundaryVertical),
                TierBand.BoundaryLeftInner,
                t.A,
                mainA.ReferenceIntersection,
                crossCOuter.ReferenceIntersection,
                t.C
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-29",
                "right REF B -> REF D",
                OutwardNormalForAxis(t.B, t.D, t.Joint, boundaryVertical),
                TierBand.BoundaryRightOuter,
                t.B,
                t.D
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "P-30",
                "right REF -> cross reference/solid intersection -> main reference/solid intersection -> REF",
                OutwardNormalForAxis(t.B, t.D, t.Joint, boundaryVertical),
                TierBand.BoundaryRightInner,
                t.B,
                crossBOuter.ReferenceIntersection,
                mainD.ReferenceIntersection,
                t.D
            );
        }

        private static void BuildSectionViewPlan(Context context, List<DimPlan> plans)
        {
            ViewData view = context.SectionView;
            PartData main = FindPart(view, context.Topology.Main.ModelId);
            if (main == null)
                throw new InvalidOperationException("View tiet dien khong chua dung MainPart.");
            P2 ref0;
            P2 ref1;
            if (!TryFarthestReferencePair(main, out ref0, out ref1))
                throw new InvalidOperationException(
                    "View tiet dien khong co MainPart reference line."
                );
            P2 axis = Normalize(Subtract(ref1, ref0));
            if (axis == null)
                throw new InvalidOperationException(
                    "MainPart reference line trong view tiet dien khong hop le."
                );
            if (axis.X < -PointTolerance || (Math.Abs(axis.X) <= PointTolerance && axis.Y < 0.0))
                axis = Scale(axis, -1.0);
            P2 normal = PerpendicularLeft(axis);
            P2 targetReference = Dot(ref0, axis) <= Dot(ref1, axis) ? ref0 : ref1;
            TerminalFeature target = ResolveTerminal(main, targetReference, normal);
            AddPlan(
                plans,
                view,
                "S-01",
                "main L exact profile width in section view, top -> bottom",
                normal,
                Scale(axis, -1.0),
                TierBand.SectionProfile,
                target.High,
                target.Low
            );
        }

        private static TerminalFeature ResolveTerminal(
            PartData part,
            P2 referenceTarget,
            P2 semanticNormal
        )
        {
            P2 ref0;
            P2 ref1;
            if (!TryFarthestReferencePair(part, out ref0, out ref1))
                throw new InvalidOperationException(
                    "Part " + part.ModelId + " khong co reference line hop le."
                );
            P2 axis = Normalize(Subtract(ref1, ref0));
            if (axis == null)
                throw new InvalidOperationException(
                    "Reference line cua part " + part.ModelId + " qua ngan."
                );

            double min = Double.PositiveInfinity;
            double max = Double.NegativeInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double value = Dot(part.Vertices[i], axis);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            bool useMinimum =
                Math.Abs(Dot(referenceTarget, axis) - min)
                <= Math.Abs(Dot(referenceTarget, axis) - max);
            double targetProjection = useMinimum ? min : max;

            List<P2> terminal = new List<P2>();
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                if (Math.Abs(Dot(part.Vertices[i], axis) - targetProjection) <= PointTolerance)
                    AddUnique(terminal, part.Vertices[i]);
            }
            if (terminal.Count < 2)
                throw new InvalidOperationException(
                    "Khong tim duoc exact terminal edge cua part " + part.ModelId + "."
                );

            TerminalFeature result = new TerminalFeature();
            result.Low = Extreme(terminal, semanticNormal, false);
            result.High = Extreme(terminal, semanticNormal, true);
            result.ReferenceIntersection = ResolveReferenceEdgeIntersection(
                part,
                ref0,
                ref1,
                referenceTarget
            );
            if (result.Low == null || result.High == null || result.ReferenceIntersection == null)
                throw new InvalidOperationException(
                    "Khong resolve duoc terminal feature cua part " + part.ModelId + "."
                );
            return result;
        }

        private static P2 ResolveReferenceEdgeIntersection(
            PartData part,
            P2 referenceA,
            P2 referenceB,
            P2 target
        )
        {
            P2 best = null;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < part.Segments.Count; i++)
            {
                Segment2 segment = part.Segments[i];
                P2 intersection;
                double segmentParameter;
                if (
                    !TryLineSegmentIntersection(
                        referenceA,
                        referenceB,
                        segment.A,
                        segment.B,
                        out intersection,
                        out segmentParameter
                    )
                )
                    continue;
                if (segmentParameter < -0.01 || segmentParameter > 1.01)
                    continue;
                double distance = Distance(intersection, target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = intersection;
                }
            }
            return best;
        }

        private static P2 ResolveTerminalBolt(PartData part, P2 target)
        {
            List<P2> points = ResolveBoltGroupPoints(part, target);
            P2 best = null;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double distance = Distance(points[i], target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = points[i];
                }
            }
            if (best == null)
                throw new InvalidOperationException(
                    "Part " + part.ModelId + " khong co bolt tai feature yeu cau."
                );
            return best;
        }

        private static List<P2> ResolveBoltGroupPoints(PartData part, P2 target)
        {
            BoltData best = null;
            double bestDistance = Double.PositiveInfinity;
            foreach (KeyValuePair<int, BoltData> pair in part.BoltGroups)
            {
                BoltData group = pair.Value;
                if (group == null || group.Points.Count == 0)
                    continue;
                double distance = Double.PositiveInfinity;
                for (int i = 0; i < group.Points.Count; i++)
                    distance = Math.Min(distance, Distance(group.Points[i], target));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = group;
                }
            }
            if (best == null)
                throw new InvalidOperationException(
                    "Part " + part.ModelId + " khong co BoltGroup hop le."
                );
            return new List<P2>(best.Points);
        }

        private static void AddWidthPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 widthAxis,
            P2 placementNormal,
            TierBand tier,
            TerminalFeature terminal,
            P2 bolt,
            bool includeBolt
        )
        {
            List<P2> points = new List<P2>();
            points.Add(terminal.Low);
            if (includeBolt && bolt != null)
                points.Add(bolt);
            points.Add(terminal.High);
            AddPlanList(
                plans,
                view,
                name,
                semantic,
                widthAxis,
                placementNormal,
                tier,
                SortAlong(points, widthAxis)
            );
        }

        private static void AddSortedPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 measurementAxis,
            P2 placementNormal,
            TierBand tier,
            params P2[] points
        )
        {
            List<P2> list = new List<P2>();
            for (int i = 0; points != null && i < points.Length; i++)
                AddUnique(list, points[i]);
            AddPlanList(
                plans,
                view,
                name,
                semantic,
                measurementAxis,
                placementNormal,
                tier,
                SortAlong(list, measurementAxis)
            );
        }

        private static void AddViewProjectedBoundaryPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 outsideNormal,
            TierBand tier,
            params P2[] points
        )
        {
            // Every boundary caller supplies the two REF endpoints first/last.
            // Inner edge feet must not change the axis chosen by that REF pair.
            // Tier names are topology labels, not guaranteed view-space sides.
            if (points == null || points.Length < 2 || points[0] == null
                || points[points.Length - 1] == null)
                throw new InvalidOperationException(name + " thieu cap REF bien.");
            P2 delta = Subtract(points[points.Length - 1], points[0]);
            bool measureX = Math.Abs(delta.X) >= Math.Abs(delta.Y);
            P2 normal = Normalize(outsideNormal);
            if (normal == null)
                throw new InvalidOperationException(name + " khong co huong dat dim bien.");
            P2 measurementAxis = measureX ? new P2(1.0, 0.0) : new P2(0.0, 1.0);
            P2 placementNormal = measureX
                ? new P2(0.0, normal.Y >= 0.0 ? 1.0 : -1.0)
                : new P2(normal.X >= 0.0 ? 1.0 : -1.0, 0.0);

            AddPlan(plans, view, name, semantic, measurementAxis, placementNormal, tier, points);
        }

        private static void AddPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 measurementAxis,
            P2 placementNormal,
            TierBand tier,
            params P2[] points
        )
        {
            List<P2> list = new List<P2>();
            for (int i = 0; points != null && i < points.Length; i++)
                AddUnique(list, points[i]);
            AddPlanList(plans, view, name, semantic, measurementAxis, placementNormal, tier, list);
        }

        private static void AddPlanList(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 measurementAxis,
            P2 placementNormal,
            TierBand tier,
            List<P2> points
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.Semantic = semantic;
            plan.View = view;
            plan.MeasurementAxis = Normalize(measurementAxis);
            plan.PlacementNormal = Normalize(placementNormal);
            plan.Tier = tier;
            for (int i = 0; points != null && i < points.Count; i++)
                AddUnique(plan.Points, points[i]);
            plans.Add(plan);
        }

        private static void ValidatePlans(Context context, List<DimPlan> plans)
        {
            if (context.Variant == TopologyVariant.Type2)
            {
                ValidateType2Plans(context, plans);
                return;
            }
            if (context.Variant != TopologyVariant.Type1)
                throw new InvalidOperationException("Topology Slot 06 chua duoc phan loai.");
            ValidateType1Plans(context, plans);
        }

        private static void ValidateType1Plans(Context context, List<DimPlan> plans)
        {
            if (plans == null || plans.Count != ExpectedPlanCount)
                throw new InvalidOperationException(
                    "Slot 06 phai co dung "
                        + ExpectedPlanCount
                        + " dimension plans; thuc te="
                        + (plans == null ? 0 : plans.Count)
                        + "."
                );

            int planCount = 0;
            int sectionCount = 0;
            HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || plan.View == null
                    || plan.MeasurementAxis == null
                    || plan.PlacementNormal == null
                )
                    throw new InvalidOperationException("Plan Slot 06 bi null.");
                if (plan.Points.Count < 2)
                    throw new InvalidOperationException(plan.Name + " co it hon hai chan dim.");
                if (!IsFinite(plan.Distance) || plan.Distance <= PointTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " co khoang dat dim khong hop le."
                    );

                double min = Double.PositiveInfinity;
                double max = Double.NegativeInfinity;
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    P2 point = plan.Points[p];
                    if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                        throw new InvalidOperationException(
                            plan.Name + " co chan dim khong huu han."
                        );
                    double projection = Dot(point, plan.MeasurementAxis);
                    min = Math.Min(min, projection);
                    max = Math.Max(max, projection);
                }
                if (max - min <= MinimumMeasuredSpan)
                    throw new InvalidOperationException(plan.Name + " co measured span bang 0.");

                string signature =
                    RuntimeHelpers.GetHashCode(plan.View).ToString(CultureInfo.InvariantCulture)
                    + "|"
                    + plan.Name;
                if (!signatures.Add(signature))
                    throw new InvalidOperationException(
                        "Trung semantic signature " + plan.Name + "."
                    );
                if (Object.ReferenceEquals(plan.View, context.PlanView))
                    planCount++;
                else if (Object.ReferenceEquals(plan.View, context.SectionView))
                    sectionCount++;
                else
                    throw new InvalidOperationException(plan.Name + " thuoc view khong duoc phep.");
            }
            if (planCount != PlanViewPlanCount || sectionCount != SectionViewPlanCount)
                throw new InvalidOperationException(
                    "Sai so plan theo view: main=" + planCount + ", section=" + sectionCount + "."
                );
        }

        // Slot06 is an explicit full-DIM replacement command for either topology.
        // Snapshot only: AuditPlan calls this method without mutating the drawing.
        private static ReplacementSnapshot SnapshotReplaceableDimensions(
            Context context,
            List<DimPlan> plans
        )
        {
            ReplacementSnapshot result = new ReplacementSnapshot();
            TSD.ContainerView sheet = context.Drawing.GetSheet();
            SnapshotDimensionsInView(sheet, result);
            TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
            while (views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view != null)
                    SnapshotDimensionsInView(view, result);
            }
            result.ExistingCount = result.Matched.Count;
            return result;
        }

        private static void SnapshotDimensionsInView(
            TSD.ViewBase view,
            ReplacementSnapshot result
        )
        {
            // Typed enumeration avoids deserializing unrelated drawing objects.
            Type[] types = {
                typeof(TSD.StraightDimensionSet),
                typeof(TSD.CurvedDimensionSetRadial),
                typeof(TSD.CurvedDimensionSetOrthogonal),
                typeof(TSD.AngleDimension),
                typeof(TSD.RadiusDimension),
                typeof(TSD.StraightDimension),
                typeof(TSD.CurvedDimensionRadial),
                typeof(TSD.CurvedDimensionOrthogonal)
            };
            foreach (Type type in types)
            {
                TSD.DrawingObjectEnumerator objects = view.GetAllObjects(type);
                while (objects.MoveNext())
                {
                    TSD.DrawingObject dimension = objects.Current as TSD.DrawingObject;
                    if (dimension == null) continue;
                    // Normalize child dimensions to their owning set, so a set is
                    // deleted once even when also enumerated from the sheet/view.
                    TSD.DimensionBase child = dimension as TSD.DimensionBase;
                    // Angle/Radius dimensions are standalone: GetDimensionSet()
                    // throws for them. Only normalize actual set child types.
                    if (child is TSD.StraightDimension
                        || child is TSD.CurvedDimensionRadial
                        || child is TSD.CurvedDimensionOrthogonal)
                    {
                        TSD.DimensionSetBase owner = child.GetDimensionSet();
                        if (owner != null) dimension = owner;
                    }
                    if (!result.Matched.Exists(previous => previous.IsSameDatabaseObject(dimension)))
                        result.Matched.Add(dimension);
                }
            }
        }

        private static bool TrySnapshotType1BoundaryDirectionMigration(
            Context context,
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> all,
            ReplacementSnapshot result
        )
        {
            if (
                context == null
                || context.Variant != TopologyVariant.Type1
                || context.Topology == null
                || plans == null
                || plans.Count != ExpectedPlanCount
                || all == null
                || result == null
            )
                return false;

            List<TSD.StraightDimensionSet> matched = new List<TSD.StraightDimensionSet>();
            HashSet<int> used = new HashSet<int>();
            for (int p = 0; p < plans.Count; p++)
            {
                DimPlan plan = plans[p];
                P2 legacyNormal = LegacyType1BoundaryPlacementNormal(context.Topology, plan.Name);
                TSD.StraightDimensionSet found = null;
                for (int i = 0; i < all.Count; i++)
                {
                    TSD.StraightDimensionSet set = all[i];
                    int key = RuntimeHelpers.GetHashCode(set);
                    if (
                        used.Contains(key)
                        || !DimensionBelongsToView(set, plan.View.View)
                        || !PointChainsMatch(ReadDimensionPoints(set), plan.Points, MatchTolerance)
                    )
                        continue;

                    bool currentDirection = DimensionDirectionMatches(set, plan.PlacementNormal);
                    bool legacyDirection =
                        legacyNormal != null && DimensionDirectionMatches(set, legacyNormal);
                    if (!currentDirection && !legacyDirection)
                        continue;

                    found = set;
                    used.Add(key);
                    break;
                }
                if (found == null)
                    return false;
                matched.Add(found);
            }

            if (matched.Count != ExpectedPlanCount)
                return false;
            result.Matched.Clear();
            result.Matched.AddRange(matched);
            result.ProtectedCount = all.Count - matched.Count;
            return true;
        }

        private static P2 LegacyType1BoundaryPlacementNormal(Topology topology, string planName)
        {
            if (topology == null || String.IsNullOrEmpty(planName))
                return null;
            if (
                String.Equals(planName, "P-03", StringComparison.Ordinal)
                || String.Equals(planName, "P-24", StringComparison.Ordinal)
            )
                return BoundaryNormal(topology.C, topology.D, topology.Joint);
            if (
                String.Equals(planName, "P-25", StringComparison.Ordinal)
                || String.Equals(planName, "P-26", StringComparison.Ordinal)
            )
                return BoundaryNormal(topology.A, topology.B, topology.Joint);

            P2 horizontal = Normalize(Subtract(topology.B, topology.A));
            if (horizontal == null)
                return null;
            P2 vertical = PerpendicularLeft(horizontal);
            if (Dot(Subtract(topology.D, topology.B), vertical) < 0.0)
                vertical = Scale(vertical, -1.0);

            if (
                String.Equals(planName, "P-27", StringComparison.Ordinal)
                || String.Equals(planName, "P-28", StringComparison.Ordinal)
            )
                return OutwardNormalForAxis(topology.A, topology.C, topology.Joint, vertical);
            if (
                String.Equals(planName, "P-29", StringComparison.Ordinal)
                || String.Equals(planName, "P-30", StringComparison.Ordinal)
            )
                return OutwardNormalForAxis(topology.B, topology.D, topology.Joint, vertical);
            return null;
        }

        private static bool DimensionBelongsToView(TSD.StraightDimensionSet set, TSD.View view)
        {
            try
            {
                TSD.ViewBase owner = set.GetView();
                if (owner == null || view == null)
                    return false;
                if (Object.ReferenceEquals(owner, view))
                    return true;
                return ReadIdentifier(owner) == ReadIdentifier(view);
            }
            catch
            {
                return true;
            }
        }

        private static List<P2> ReadDimensionPoints(object dimension)
        {
            List<P2> result = new List<P2>();
            try
            {
                PropertyInfo property = dimension
                    .GetType()
                    .GetProperty(
                        "DimensionPoints",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                object value = property == null ? null : property.GetValue(dimension, null);
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object item in enumerable)
                    {
                        TSG.Point point = item as TSG.Point;
                        if (point != null)
                            result.Add(new P2(point.X, point.Y));
                    }
                }
            }
            catch { }
            return result;
        }

        private static bool DimensionDirectionMatches(object dimension, P2 expectedNormal)
        {
            P2 actual = ReadPointLikeMember(dimension, "UpDirection");
            if (actual == null)
                actual = ReadPointLikeMember(dimension, "OffsetDirection");
            actual = Normalize(actual);
            P2 expected = Normalize(expectedNormal);
            return actual != null && expected != null && Dot(actual, expected) >= 0.95;
        }

        private static P2 ReadPointLikeMember(object value, string name)
        {
            try
            {
                PropertyInfo property = value
                    .GetType()
                    .GetProperty(
                        name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                object member = property == null ? null : property.GetValue(value, null);
                TSG.Point point = member as TSG.Point;
                if (point != null)
                    return new P2(point.X, point.Y);
                TSG.Vector vector = member as TSG.Vector;
                return vector == null ? null : new P2(vector.X, vector.Y);
            }
            catch
            {
                return null;
            }
        }

        private static bool PointChainsMatch(List<P2> first, List<P2> second, double tolerance)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            bool forward = true;
            bool reverse = true;
            for (int i = 0; i < first.Count; i++)
            {
                if (Distance(first[i], second[i]) > tolerance)
                    forward = false;
                if (Distance(first[i], second[second.Count - 1 - i]) > tolerance)
                    reverse = false;
            }
            return forward || reverse;
        }

        private static void DisableCombineAndVerify(
            DimPlan plan,
            TSD.StraightDimensionSet dimension
        )
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            if (attributes == null)
                throw new InvalidOperationException(
                    "Khong doc duoc attributes cua " + plan.Name + "."
                );
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension
                ?? new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format = TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            combined.MinimumNumberToCombine = Math.Max(5, plan.Points.Count);
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;
            if (!dimension.Modify())
                throw new InvalidOperationException(
                    "Khong tat duoc combined dimension cua " + plan.Name + "."
                );

            dimension.Select();
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes verified =
                dimension.Attributes == null ? null : dimension.Attributes.CombinedDimension;
            if (
                verified == null
                || (
                    verified.Format != TSD.DimensionSetBaseAttributes.CombineFormats.Off
                    && verified.MinimumNumberToCombine <= plan.Points.Count - 1
                )
            )
                throw new InvalidOperationException(
                    "Tekla ghi de CombinedDimension cua " + plan.Name + "."
                );
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes ReadDimensionAttributes(
            TSD.View view
        )
        {
            try
            {
                TSD.DrawingObjectEnumerator dimensions = view.GetAllObjects(
                    typeof(TSD.StraightDimensionSet)
                );
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set = dimensions.Current as TSD.StraightDimensionSet;
                    if (set != null && set.Attributes != null)
                        return set.Attributes;
                }
            }
            catch { }
            return null;
        }

        private static bool TryFarthestReferencePair(PartData part, out P2 first, out P2 second)
        {
            first = null;
            second = null;
            double best = 0.0;
            if (part == null)
                return false;
            for (int i = 0; i < part.Reference.Count; i++)
            {
                for (int j = i + 1; j < part.Reference.Count; j++)
                {
                    double distance = Distance(part.Reference[i], part.Reference[j]);
                    if (distance > best)
                    {
                        best = distance;
                        first = part.Reference[i];
                        second = part.Reference[j];
                    }
                }
            }
            return first != null && second != null && best > PointTolerance;
        }

        private static bool TrySharedEndpoint(
            P2 first0,
            P2 first1,
            P2 second0,
            P2 second1,
            out P2 jointFirst,
            out P2 outerFirst,
            out P2 jointSecond,
            out P2 outerSecond
        )
        {
            jointFirst = null;
            outerFirst = null;
            jointSecond = null;
            outerSecond = null;
            double d00 = Distance(first0, second0);
            double d01 = Distance(first0, second1);
            double d10 = Distance(first1, second0);
            double d11 = Distance(first1, second1);
            double best = Math.Min(Math.Min(d00, d01), Math.Min(d10, d11));
            if (best > PointTolerance)
                return false;
            if (best == d00)
            {
                jointFirst = first0;
                outerFirst = first1;
                jointSecond = second0;
                outerSecond = second1;
            }
            else if (best == d01)
            {
                jointFirst = first0;
                outerFirst = first1;
                jointSecond = second1;
                outerSecond = second0;
            }
            else if (best == d10)
            {
                jointFirst = first1;
                outerFirst = first0;
                jointSecond = second0;
                outerSecond = second1;
            }
            else
            {
                jointFirst = first1;
                outerFirst = first0;
                jointSecond = second1;
                outerSecond = second0;
            }
            return true;
        }

        private static bool TryInfiniteLineIntersection(
            P2 a0,
            P2 a1,
            P2 b0,
            P2 b1,
            out P2 intersection
        )
        {
            intersection = null;
            P2 r = Subtract(a1, a0);
            P2 s = Subtract(b1, b0);
            double denominator = Cross(r, s);
            if (Math.Abs(denominator) <= 1e-9)
                return false;
            double parameter = Cross(Subtract(b0, a0), s) / denominator;
            intersection = Add(a0, Scale(r, parameter));
            return IsFinite(intersection.X) && IsFinite(intersection.Y);
        }

        private static bool TryLineSegmentIntersection(
            P2 line0,
            P2 line1,
            P2 segment0,
            P2 segment1,
            out P2 intersection,
            out double segmentParameter
        )
        {
            intersection = null;
            segmentParameter = Double.NaN;
            P2 r = Subtract(line1, line0);
            P2 s = Subtract(segment1, segment0);
            double denominator = Cross(r, s);
            if (Math.Abs(denominator) <= 1e-9)
                return false;
            P2 delta = Subtract(segment0, line0);
            double lineParameter = Cross(delta, s) / denominator;
            segmentParameter = Cross(delta, r) / denominator;
            intersection = Add(line0, Scale(r, lineParameter));
            return IsFinite(intersection.X) && IsFinite(intersection.Y);
        }

        private static P2 BoundaryNormal(P2 first, P2 second, P2 center)
        {
            P2 direction = Normalize(Subtract(second, first));
            if (direction == null)
                throw new InvalidOperationException("Boundary reference qua ngan.");
            P2 normal = PerpendicularLeft(direction);
            P2 midpoint = Midpoint(first, second);
            if (Dot(Subtract(midpoint, center), normal) < 0.0)
                normal = Scale(normal, -1.0);
            return normal;
        }

        private static P2 OutwardNormalForAxis(P2 first, P2 second, P2 center, P2 measurementAxis)
        {
            P2 axis = Normalize(measurementAxis);
            if (axis == null)
                throw new InvalidOperationException("Reference measurement axis qua ngan.");
            P2 normal = PerpendicularLeft(axis);
            P2 midpoint = Midpoint(first, second);
            if (Dot(Subtract(midpoint, center), normal) < 0.0)
                normal = Scale(normal, -1.0);
            return normal;
        }

        private static List<P2> SortAlong(List<P2> points, P2 axis)
        {
            List<P2> result = new List<P2>();
            for (int i = 0; points != null && i < points.Count; i++)
                AddUnique(result, points[i]);
            P2 direction = Normalize(axis);
            if (direction == null)
                return result;
            result.Sort(
                delegate(P2 a, P2 b)
                {
                    int compare = Dot(a, direction).CompareTo(Dot(b, direction));
                    if (compare != 0)
                        return compare;
                    compare = a.X.CompareTo(b.X);
                    return compare != 0 ? compare : a.Y.CompareTo(b.Y);
                }
            );
            return result;
        }

        private static P2 Extreme(List<P2> points, P2 axis, bool maximum)
        {
            P2 best = null;
            double value = maximum ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                double projection = Dot(points[i], axis);
                if ((maximum && projection > value) || (!maximum && projection < value))
                {
                    value = projection;
                    best = points[i];
                }
            }
            return best;
        }

        private static PartData FindPart(ViewData view, int modelId)
        {
            for (int i = 0; view != null && i < view.Parts.Count; i++)
            {
                if (view.Parts[i].ModelId == modelId)
                    return view.Parts[i];
            }
            return null;
        }

        private static void AddUnique(List<P2> points, P2 point)
        {
            if (point == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], point) <= 0.01)
                    return;
            }
            points.Add(new P2(point.X, point.Y));
        }

        private static void AddUniqueSegment(List<Segment2> segments, P2 a, P2 b)
        {
            if (a == null || b == null || Distance(a, b) <= 0.01)
                return;
            for (int i = 0; i < segments.Count; i++)
            {
                Segment2 old = segments[i];
                bool same = Distance(old.A, a) <= 0.01 && Distance(old.B, b) <= 0.01;
                bool reverse = Distance(old.A, b) <= 0.01 && Distance(old.B, a) <= 0.01;
                if (same || reverse)
                    return;
            }
            segments.Add(new Segment2(new P2(a.X, a.Y), new P2(b.X, b.Y)));
        }

        private static P2 Transform(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            TSG.Point global = currentToGlobal.Transform(point);
            TSG.Point local = globalToView.Transform(global);
            return new P2(local.X, local.Y);
        }

        private static double ReadScale(TSD.View view)
        {
            try
            {
                return view.Attributes.Scale;
            }
            catch
            {
                return Double.NaN;
            }
        }

        private static string SafeViewName(TSD.View view)
        {
            try
            {
                PropertyInfo property = view.GetType().GetProperty("Name");
                string value =
                    property == null
                        ? ""
                        : Convert.ToString(
                            property.GetValue(view, null),
                            CultureInfo.InvariantCulture
                        );
                return String.IsNullOrWhiteSpace(value) ? view.GetType().Name : value;
            }
            catch
            {
                return view.GetType().Name;
            }
        }

        private static string SafeUpper(string value)
        {
            return String.IsNullOrWhiteSpace(value)
                ? String.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static bool SameIdentifier(TSM.Part first, TSM.Part second)
        {
            return first != null
                && second != null
                && first.Identifier != null
                && second.Identifier != null
                && first.Identifier.ID == second.Identifier.ID;
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                PropertyInfo property = value.GetType().GetProperty("Identifier");
                Tekla.Structures.Identifier id =
                    property == null
                        ? null
                        : property.GetValue(value, null) as Tekla.Structures.Identifier;
                return id == null ? 0 : id.ID;
            }
            catch
            {
                return 0;
            }
        }

        private static P2 Add(P2 first, P2 second)
        {
            return new P2(first.X + second.X, first.Y + second.Y);
        }

        private static P2 Subtract(P2 first, P2 second)
        {
            return new P2(first.X - second.X, first.Y - second.Y);
        }

        private static P2 Scale(P2 value, double scale)
        {
            return new P2(value.X * scale, value.Y * scale);
        }

        private static P2 Midpoint(P2 first, P2 second)
        {
            return new P2((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5);
        }

        private static P2 Normalize(P2 value)
        {
            if (value == null)
                return null;
            double length = Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return length <= 1e-9 ? null : new P2(value.X / length, value.Y / length);
        }

        private static P2 PerpendicularLeft(P2 value)
        {
            return new P2(-value.Y, value.X);
        }

        private static double Dot(P2 first, P2 second)
        {
            return first.X * second.X + first.Y * second.Y;
        }

        private static double Cross(P2 first, P2 second)
        {
            return first.X * second.Y - first.Y * second.X;
        }

        private static double Distance(P2 first, P2 second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatPoint(P2 point)
        {
            return point == null ? "?" : "(" + Format(point.X) + "," + Format(point.Y) + ")";
        }

        private static void ShowWarning(string message)
        {
            try
            {
                MessageBox.Show(
                    message,
                    "Slot 06 - Lien ket giang xeo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
            catch { }
        }
    }
}
#pragma warning restore
#endregion

#region PHU_Slot06_BraceConnectionAutoDim.Type2 - Lập kế hoạch liên kết giằng Type 2


namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using TSD = Tekla.Structures.Drawing;

    internal static partial class PHU_Slot06DiagonalBraceDimensionEngine
    {
        // KIỂM CHỨNG TYPE 2: 27 plan thẳng = 26 plan view + 1 section; không dùng count Type 1 cho nhánh này.
        private const int Type2ExpectedPlanCount = 27;
        private const int Type2PlanViewPlanCount = 26;
        private const int Type2SectionViewPlanCount = 1;

        private sealed class Type2Topology
        {
            public PartData Main;
            public PartData Cross;
            public PartData Plate;
            public P2 A;
            public P2 B;
            public P2 C;
            public P2 D;
            public P2 Joint;
            public P2 MainAxis;
            public P2 MainNormal;
            public P2 CrossAxis;
            public P2 CrossNormal;
        }

        private static bool TryResolvePlanTopologyType2(ViewData view, out Type2Topology topology)
        {
            topology = null;
            if (view == null || view.Main == null)
                return false;

            P2 main0;
            P2 main1;
            if (!TryFarthestReferencePair(view.Main, out main0, out main1))
                return false;
            CanonicalizeAxisPair(ref main0, ref main1);
            P2 mainDirection = Normalize(Subtract(main1, main0));
            double mainLength = Distance(main0, main1);
            if (mainDirection == null || mainLength <= PointTolerance)
                return false;

            List<PartData> longMembers = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.AssemblyId != view.Main.AssemblyId)
                    continue;
                P2 first;
                P2 second;
                if (
                    !TryFarthestReferencePair(part, out first, out second)
                    || Distance(first, second) < mainLength * 0.05
                )
                    continue;
                longMembers.Add(part);
            }
            if (longMembers.Count != 2)
                return false;

            PartData cross = longMembers[0].IsMain ? longMembers[1] : longMembers[0];
            if (cross == null || cross.IsMain)
                return false;

            P2 cross0;
            P2 cross1;
            if (!TryFarthestReferencePair(cross, out cross0, out cross1))
                return false;
            P2 crossDirection = Normalize(Subtract(cross1, cross0));
            if (
                crossDirection == null
                || Math.Abs(Dot(mainDirection, crossDirection)) >= DirectionTolerance
            )
                return false;

            P2 joint;
            if (
                !TryInfiniteLineIntersection(main0, main1, cross0, cross1, out joint)
                || !IsStrictlyInsideReference(main0, main1, joint)
                || !IsStrictlyInsideReference(cross0, cross1, joint)
            )
                return false;

            PartData plate = ResolveConnectionPlateType2(view, view.Main, cross, mainLength);
            if (plate == null || !SharesBoltGroup(view.Main, cross))
                return false;

            P2 a = main0;
            P2 d = main1;
            P2 b;
            P2 c;
            if (Cross(mainDirection, crossDirection) > 0.0)
            {
                b = cross0;
                c = cross1;
            }
            else
            {
                b = cross1;
                c = cross0;
            }

            Type2Topology item = new Type2Topology();
            item.Main = view.Main;
            item.Cross = cross;
            item.Plate = plate;
            item.A = a;
            item.B = b;
            item.C = c;
            item.D = d;
            item.Joint = joint;
            item.MainAxis = Normalize(Subtract(item.D, item.A));
            item.CrossAxis = Normalize(Subtract(item.C, item.B));
            if (item.MainAxis == null || item.CrossAxis == null)
                return false;

            item.MainNormal = PerpendicularLeft(item.MainAxis);
            if (Dot(Subtract(item.C, item.Joint), item.MainNormal) < 0.0)
                item.MainNormal = Scale(item.MainNormal, -1.0);
            item.CrossNormal = PerpendicularLeft(item.CrossAxis);
            if (Dot(Subtract(item.D, item.Joint), item.CrossNormal) < 0.0)
                item.CrossNormal = Scale(item.CrossNormal, -1.0);

            topology = item;
            return true;
        }

        private static PartData ResolveConnectionPlateType2(
            ViewData view,
            PartData main,
            PartData cross,
            double mainLength
        )
        {
            List<PartData> matches = new List<PartData>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (
                    candidate.ModelId == main.ModelId
                    || candidate.ModelId == cross.ModelId
                    || candidate.AssemblyId != main.AssemblyId
                )
                    continue;
                if (!SharesBoltGroup(candidate, main) || !SharesBoltGroup(candidate, cross))
                    continue;

                int connectedLongMembers = 0;
                for (int p = 0; p < view.Parts.Count; p++)
                {
                    PartData member = view.Parts[p];
                    P2 first;
                    P2 second;
                    if (
                        member.AssemblyId == main.AssemblyId
                        && TryFarthestReferencePair(member, out first, out second)
                        && Distance(first, second) >= mainLength * 0.05
                        && SharesBoltGroup(candidate, member)
                    )
                        connectedLongMembers++;
                }
                if (connectedLongMembers == 2)
                    matches.Add(candidate);
            }
            return matches.Count == 1 ? matches[0] : null;
        }

        private static bool IsStrictlyInsideReference(P2 first, P2 second, P2 point)
        {
            P2 span = Subtract(second, first);
            double denominator = Dot(span, span);
            if (denominator <= PointTolerance * PointTolerance)
                return false;
            double parameter = Dot(Subtract(point, first), span) / denominator;
            double clearance = Math.Max(PointTolerance / Math.Sqrt(denominator), 0.02);
            return parameter > clearance && parameter < 1.0 - clearance;
        }

        private static void CanonicalizeAxisPair(ref P2 first, ref P2 second)
        {
            P2 direction = Subtract(second, first);
            if (
                direction.X < -PointTolerance
                || (Math.Abs(direction.X) <= PointTolerance && direction.Y < 0.0)
            )
            {
                P2 swap = first;
                first = second;
                second = swap;
            }
        }

        private static ViewData ResolveType2SectionView(Context context)
        {
            List<ViewData> preferred = new List<ViewData>();
            for (int i = 0; i < context.Views.Count; i++)
            {
                ViewData candidate = context.Views[i];
                if (Object.ReferenceEquals(candidate, context.PlanView))
                    continue;
                PartData main = FindPart(candidate, context.Type2Topology.Main.ModelId);
                PartData cross = FindPart(candidate, context.Type2Topology.Cross.ModelId);
                PartData plate = FindPart(candidate, context.Type2Topology.Plate.ModelId);
                if (main == null || cross == null || plate == null)
                    continue;

                P2 main0;
                P2 main1;
                P2 cross0;
                P2 cross1;
                if (
                    !TryFarthestReferencePair(main, out main0, out main1)
                    || !TryFarthestReferencePair(cross, out cross0, out cross1)
                )
                    continue;
                CanonicalizeAxisPair(ref main0, ref main1);
                P2 mainAxis = Normalize(Subtract(main1, main0));
                P2 crossAxis = Normalize(Subtract(cross1, cross0));
                if (
                    mainAxis == null
                    || crossAxis == null
                    || Math.Abs(Dot(mainAxis, crossAxis)) < DirectionTolerance
                )
                    continue;

                P2 normal = PerpendicularLeft(mainAxis);
                double mainLevel = Dot(Centroid(main), normal);
                double plateLevel = Dot(Centroid(plate), normal);
                double crossLevel = Dot(Centroid(cross), normal);
                if (
                    mainLevel > plateLevel + PointTolerance
                    && plateLevel > crossLevel + PointTolerance
                )
                    preferred.Add(candidate);
            }
            if (preferred.Count != 1)
                throw new InvalidOperationException(
                    "Khong xac dinh duy nhat view tiet dien Type2 co thu tu main-plate-cross. So view="
                        + preferred.Count
                        + "."
                );
            return preferred[0];
        }

        private static P2 Centroid(PartData part)
        {
            if (part == null || part.Vertices.Count == 0)
                return null;
            double x = 0.0;
            double y = 0.0;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                x += part.Vertices[i].X;
                y += part.Vertices[i].Y;
            }
            return new P2(x / part.Vertices.Count, y / part.Vertices.Count);
        }

        private static List<DimPlan> BuildType2Plans(Context context)
        {
            List<DimPlan> plans = new List<DimPlan>();
            BuildType2PlanViewPlans(context, plans);
            BuildType2SectionViewPlan(context, plans);
            SpaceType2InnerChain(plans, "T2-P-11", "T2-P-12", "T2-P-13");
            SpaceType2InnerChain(plans, "T2-P-16", "T2-P-17", "T2-P-18");
            return plans;
        }

        private static void SpaceType2InnerChain(List<DimPlan> plans, string innerName,
            string middleName, string outerName)
        {
            DimPlan inner = plans.Find(p => p.Name == innerName);
            DimPlan middle = plans.Find(p => p.Name == middleName);
            DimPlan outer = plans.Find(p => p.Name == outerName);
            P2 n = inner.PlacementNormal;
            double middleLevel = Dot(middle.Points[0], n) + middle.Distance;
            double outerLevel = Dot(outer.Points[0], n) + outer.Distance;
            double gap = outerLevel - middleLevel;
            double distance = middleLevel - gap - Dot(inner.Points[0], n);
            if (gap <= PointTolerance || distance <= PointTolerance
                || Double.IsNaN(distance) || Double.IsInfinity(distance))
                throw new InvalidOperationException("Khoang cach tang dim Type2 khong hop le.");
            inner.DistanceOverride = distance;
        }

        private sealed class BraceAnglePlan
        {
            public string Name;
            public ViewData View;
            public P2 Origin, Point1, Point2;
            public double Degrees;
        }

        private static List<BraceAnglePlan> BuildBraceAnglePlans(Context context)
        {
            List<BraceAnglePlan> result = new List<BraceAnglePlan>();
            if (context.Variant == TopologyVariant.Type1)
            {
                Topology type1 = context.Topology;
                result.Add(BuildType2BraceAngle(context.PlanView, type1.Main,
                    type1.MainAxis, "T1-A-01"));
                bool startIsUpper = type1.B.Y > type1.C.Y;
                PartData upper = startIsUpper ? type1.CrossStart : type1.CrossEnd;
                PartData lower = startIsUpper ? type1.CrossEnd : type1.CrossStart;
                P2 upperRef = startIsUpper ? type1.B : type1.C;
                result.Add(BuildType2BraceAngle(context.PlanView, lower,
                    type1.CrossAxis, "T1-A-02"));
                // Upper split brace: approved angle is measured from the downward
                // vertical at the OUTER terminal's lower flange vertex, not from X.
                TerminalFeature terminal = ResolveTerminal(upper, upperRef, type1.CrossNormal);
                P2 origin = terminal.Low.Y <= terminal.High.Y ? terminal.Low : terminal.High;
                result.Add(BuildBraceAngleAtVertex(context.PlanView, upper,
                    type1.CrossAxis, "T1-A-03", origin, true));
                return result;
            }
            if (context.Variant != TopologyVariant.Type2) return result;
            Type2Topology t = context.Type2Topology;
            result.Add(BuildType2BraceAngle(context.PlanView, t.Main, t.MainAxis, "T2-A-01"));
            result.Add(BuildType2BraceAngle(context.PlanView, t.Cross, t.CrossAxis, "T2-A-02"));
            return result;
        }

        private static BraceAnglePlan BuildType2BraceAngle(ViewData view, PartData part,
            P2 referenceAxis, string name)
        {
            // Approved Type2 sample: lowest exact flange vertex, longitudinal
            // solid edge rising inward, and the horizontal ray toward that edge.
            P2 origin = null;
            foreach (P2 vertex in part.Vertices)
                if (origin == null || vertex.Y < origin.Y
                    || (vertex.Y == origin.Y && vertex.X < origin.X)) origin = vertex;
            return BuildBraceAngleAtVertex(view, part, referenceAxis, name, origin, false);
        }

        private static BraceAnglePlan BuildBraceAngleAtVertex(ViewData view, PartData part,
            P2 referenceAxis, string name, P2 origin, bool verticalBaseline)
        {
            P2 direction = null;
            if (origin == null)
                throw new InvalidOperationException(name + " khong co dinh solid.");
            double longest = 0.0;
            foreach (Segment2 edge in part.Segments)
            {
                P2 other = Distance(edge.A, origin) <= PointTolerance ? edge.B
                    : Distance(edge.B, origin) <= PointTolerance ? edge.A : null;
                if (other == null) continue;
                P2 delta = Subtract(other, origin);
                P2 axis = Normalize(delta);
                double length = Distance(other, origin);
                if (axis != null && Math.Abs(Dot(axis, referenceAxis)) > 0.999
                    && length > longest) { direction = axis; longest = length; }
            }
            if (direction == null || (verticalBaseline ? direction.Y >= -1e-6 : direction.Y <= 1e-6)
                || Math.Abs(direction.X) <= 1e-6 || view.Scale <= 0.0)
                throw new InvalidOperationException(name + " khong xac dinh duoc mep doc de dim goc.");
            return new BraceAnglePlan {
                Name = name, View = view, Origin = origin,
                // Ray lengths are presentation only (5/6 mm on paper).
                Point1 = Add(origin, Scale(direction, 5.0 * view.Scale)),
                Point2 = Add(origin, verticalBaseline ? new P2(0.0, -6.0 * view.Scale)
                    : new P2(Math.Sign(direction.X) * 6.0 * view.Scale, 0.0)),
                Degrees = (verticalBaseline
                    ? Math.Atan2(Math.Abs(direction.X), -direction.Y)
                    : Math.Atan2(direction.Y, Math.Abs(direction.X))) * 180.0 / Math.PI
            };
        }

        private static TSD.AngleDimensionAttributes CreateBraceAngleAttributes()
        {
            // Captured from approved Type1 and Type2 angle samples, independent of
            // whether a previous run's dimensions still exist in the drawing.
            TSD.AngleDimensionAttributes a = new TSD.AngleDimensionAttributes();
            a.Type = TSD.AngleTypes.TriangleWithDegrees;
            a.TriangleBase = 1000;
            a.Color = TSD.DrawingColors.Black;
            a.TransparentBackground = false;
            a.Arrowhead.ArrowPosition = TSD.ArrowheadPositions.Both;
            a.Arrowhead.Head = (TSD.ArrowheadTypes)11;
            a.Arrowhead.Height = 1; a.Arrowhead.Width = 1;
            a.Format.Precision = TSD.DimensionSetBaseAttributes.DimensionValuePrecisions.OnePerThousand;
            a.Format.Format = TSD.DimensionSetBaseAttributes.DimensionValueFormats.OneOptionalDecimal;
            a.Format.Unit = TSD.DimensionSetBaseAttributes.DimensionValueUnits.Automatic;
            a.Format.UseDigitGrouping = false;
            a.Placing.Placing = TSD.DimensionSetBaseAttributes.Placings.Fixed;
            a.Placing.Direction.Positive = true; a.Placing.Direction.Negative = false;
            a.Placing.Distance.SearchMargin = 5;
            a.Placing.Distance.MinimalDistance = 10;
            a.Placing.Distance.MaximalDistance = 0;
            a.Text.Font.Name = "MS UI Gothic"; a.Text.Font.Height = 3;
            a.Text.Font.Color = TSD.DrawingColors.Black;
            a.Text.Font.Bold = false; a.Text.Font.Italic = false;
            a.Text.Frame = TSD.DimensionSetBaseAttributes.FrameTypes.None;
            a.Text.TextPlacing = TSD.DimensionSetBaseAttributes.DimensionTextPlacings.AboveDimensionLine;
            return a;
        }

        private static void BuildType2PlanViewPlans(Context context, List<DimPlan> plans)
        {
            Type2Topology t = context.Type2Topology;
            ViewData view = context.PlanView;
            TerminalFeature mainA = ResolveTerminal(t.Main, t.A, t.MainNormal);
            TerminalFeature mainD = ResolveTerminal(t.Main, t.D, t.MainNormal);
            TerminalFeature crossB = ResolveTerminal(t.Cross, t.B, t.CrossNormal);
            TerminalFeature crossC = ResolveTerminal(t.Cross, t.C, t.CrossNormal);
            P2 mainABolt = ResolveTerminalBolt(t.Main, t.A);
            P2 mainDBolt = ResolveTerminalBolt(t.Main, t.D);
            P2 crossBBolt = ResolveTerminalBolt(t.Cross, t.B);
            P2 crossCBolt = ResolveTerminalBolt(t.Cross, t.C);
            P2 jointBolt = ResolveTerminalBolt(t.Main, t.Joint);

            AddWidthPlan(
                plans,
                view,
                "T2-P-01",
                "cross B terminal profile width",
                t.CrossNormal,
                Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidth,
                crossB,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-02",
                "cross B terminal profile width with nearest bolt",
                t.CrossNormal,
                Scale(t.CrossAxis, -1.0),
                TierBand.CrossStartWidthWithBolt,
                crossB,
                crossBBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-03",
                "main D terminal profile width",
                Scale(t.MainNormal, -1.0),
                t.MainAxis,
                TierBand.MainEndWidth,
                mainD,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-04",
                "main D terminal profile width with nearest bolt",
                Scale(t.MainNormal, -1.0),
                t.MainAxis,
                TierBand.MainEndWidthWithBolt,
                mainD,
                mainDBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-05",
                "cross C terminal profile width",
                t.CrossNormal,
                t.CrossAxis,
                TierBand.CrossEndWidth,
                crossC,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-06",
                "cross C terminal profile width with nearest bolt",
                t.CrossNormal,
                t.CrossAxis,
                TierBand.CrossEndWidthWithBolt,
                crossC,
                crossCBolt,
                true
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-07",
                "main A terminal profile width",
                Scale(t.MainNormal, -1.0),
                Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidth,
                mainA,
                null,
                false
            );
            AddWidthPlan(
                plans,
                view,
                "T2-P-08",
                "main A terminal profile width with nearest bolt",
                Scale(t.MainNormal, -1.0),
                Scale(t.MainAxis, -1.0),
                TierBand.MainStartWidthWithBolt,
                mainA,
                mainABolt,
                true
            );

            AddPlan(
                plans,
                view,
                "T2-P-09",
                "main D exact reference/solid edge -> nearest terminal bolt",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainLocalEndEdge,
                mainD.ReferenceIntersection,
                mainDBolt
            );
            AddPlan(
                plans,
                view,
                "T2-P-10",
                "main A exact reference/solid edge -> nearest terminal bolt",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainLocalStartEdge,
                mainA.ReferenceIntersection,
                mainABolt
            );
            AddPlan(
                plans,
                view,
                "T2-P-11",
                "main outer flange terminal vertex -> joint bolt -> outer flange terminal vertex",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorInner,
                mainA.High,
                jointBolt,
                mainD.High
            );
            AddPlan(
                plans,
                view,
                "T2-P-12",
                "main REF-outer flange vertices-REF semantic chain",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorMiddle,
                t.A,
                mainA.High,
                mainD.High,
                t.D
            );
            AddPlan(
                plans,
                view,
                "T2-P-13",
                "main REF A -> REF D",
                t.MainAxis,
                t.MainNormal,
                TierBand.MainMajorOuter,
                t.A,
                t.D
            );

            AddPlan(
                plans,
                view,
                "T2-P-14",
                "cross B exact reference/solid edge -> nearest terminal bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalBolt,
                crossB.ReferenceIntersection,
                crossBBolt
            );
            AddPlan(
                plans,
                view,
                "T2-P-15",
                "cross C exact reference/solid edge -> nearest terminal bolt",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossLocalEdge,
                crossC.ReferenceIntersection,
                crossCBolt
            );
            AddPlan(
                plans,
                view,
                "T2-P-16",
                "cross outer flange terminal vertex -> joint bolt -> outer flange terminal vertex",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.MainMajorInner,
                crossC.High,
                jointBolt,
                crossB.High
            );
            AddPlan(
                plans,
                view,
                "T2-P-17",
                "cross REF-outer flange vertices-REF semantic chain",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossMajorInner,
                t.C,
                crossC.High,
                crossB.High,
                t.B
            );
            AddPlan(
                plans,
                view,
                "T2-P-18",
                "cross REF C -> REF B",
                Scale(t.CrossAxis, -1.0),
                t.CrossNormal,
                TierBand.CrossMajorOuter,
                t.C,
                t.B
            );

            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-19",
                "right REF -> exact-edge -> REF",
                BoundaryNormal(t.B, t.D, t.Joint),
                TierBand.BoundaryRightInner,
                t.B,
                crossB.ReferenceIntersection,
                mainD.ReferenceIntersection,
                t.D
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-20",
                "right REF B -> REF D",
                BoundaryNormal(t.B, t.D, t.Joint),
                TierBand.BoundaryRightOuter,
                t.B,
                t.D
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-21",
                "left REF -> exact-edge -> REF",
                BoundaryNormal(t.A, t.C, t.Joint),
                TierBand.BoundaryLeftInner,
                t.A,
                mainA.ReferenceIntersection,
                crossC.ReferenceIntersection,
                t.C
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-22",
                "left REF A -> REF C",
                BoundaryNormal(t.A, t.C, t.Joint),
                TierBand.BoundaryLeftOuter,
                t.A,
                t.C
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-23",
                "bottom REF -> exact-edge -> REF",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomInner,
                t.A,
                mainA.ReferenceIntersection,
                crossB.ReferenceIntersection,
                t.B
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-24",
                "bottom REF A -> REF B",
                BoundaryNormal(t.A, t.B, t.Joint),
                TierBand.BoundaryBottomOuter,
                t.A,
                t.B
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-25",
                "top REF -> exact-edge -> REF",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopInner,
                t.C,
                crossC.ReferenceIntersection,
                mainD.ReferenceIntersection,
                t.D
            );
            AddViewProjectedBoundaryPlan(
                plans,
                view,
                "T2-P-26",
                "top REF C -> REF D",
                BoundaryNormal(t.C, t.D, t.Joint),
                TierBand.BoundaryTopOuter,
                t.C,
                t.D
            );
        }

        private static void BuildType2SectionViewPlan(Context context, List<DimPlan> plans)
        {
            ViewData view = context.SectionView;
            PartData main = FindPart(view, context.Type2Topology.Main.ModelId);
            PartData cross = FindPart(view, context.Type2Topology.Cross.ModelId);
            if (main == null || cross == null)
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong chua du hai thanh giang."
                );

            P2 main0;
            P2 main1;
            if (!TryFarthestReferencePair(main, out main0, out main1))
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong co main reference line."
                );
            CanonicalizeAxisPair(ref main0, ref main1);
            P2 axis = Normalize(Subtract(main1, main0));
            if (axis == null)
                throw new InvalidOperationException(
                    "Main reference line trong view tiet dien Type2 khong hop le."
                );
            P2 normal = PerpendicularLeft(axis);
            P2 targetReference = Dot(main0, axis) <= Dot(main1, axis) ? main0 : main1;
            TerminalFeature mainTerminal = ResolveSectionProfileAtReference(
                main,
                targetReference,
                normal
            );

            P2 cross0;
            P2 cross1;
            if (!TryFarthestReferencePair(cross, out cross0, out cross1))
                throw new InvalidOperationException(
                    "View tiet dien Type2 khong co cross reference line."
                );
            P2 crossTarget = Dot(cross0, axis) <= Dot(cross1, axis) ? cross0 : cross1;
            TerminalFeature crossTerminal = ResolveSectionProfileAtReference(
                cross,
                crossTarget,
                normal
            );

            AddPlan(
                plans,
                view,
                "T2-S-01",
                "two L profile layers in section view, top -> bottom",
                normal,
                Scale(axis, -1.0),
                TierBand.SectionProfile,
                mainTerminal.High,
                mainTerminal.Low,
                crossTerminal.High,
                crossTerminal.Low
            );
        }

        private static TerminalFeature ResolveSectionProfileAtReference(
            PartData part,
            P2 referenceTarget,
            P2 semanticNormal
        )
        {
            double min = Double.PositiveInfinity;
            double max = Double.NegativeInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double value = Dot(part.Vertices[i], semanticNormal);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            List<P2> lowCandidates = new List<P2>();
            List<P2> highCandidates = new List<P2>();
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double value = Dot(part.Vertices[i], semanticNormal);
                if (Math.Abs(value - min) <= PointTolerance)
                    AddUnique(lowCandidates, part.Vertices[i]);
                if (Math.Abs(value - max) <= PointTolerance)
                    AddUnique(highCandidates, part.Vertices[i]);
            }
            if (lowCandidates.Count == 0 || highCandidates.Count == 0)
                throw new InvalidOperationException(
                    "Khong tim duoc section profile cua part " + part.ModelId + "."
                );

            TerminalFeature result = new TerminalFeature();
            result.Low = NearestPoint(lowCandidates, referenceTarget);
            result.High = NearestPoint(highCandidates, referenceTarget);
            if (result.Low == null || result.High == null)
                throw new InvalidOperationException(
                    "Khong resolve duoc section profile cua part " + part.ModelId + "."
                );
            return result;
        }

        private static P2 NearestPoint(List<P2> points, P2 target)
        {
            P2 best = null;
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                double distance = Distance(points[i], target);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = points[i];
                }
            }
            return best;
        }

        private static int ExpectedPlanCountFor(Context context)
        {
            return context != null && context.Variant == TopologyVariant.Type2
                ? Type2ExpectedPlanCount
                : ExpectedPlanCount;
        }

        private static bool TrySnapshotType2FootMigration(
            Context context,
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> all,
            ReplacementSnapshot result
        )
        {
            if (
                context == null
                || context.Variant != TopologyVariant.Type2
                || plans == null
                || plans.Count != Type2ExpectedPlanCount
                || all == null
                || result == null
            )
                return false;

            Type2Topology t = context.Type2Topology;
            TerminalFeature mainA = ResolveTerminal(t.Main, t.A, t.MainNormal);
            TerminalFeature mainD = ResolveTerminal(t.Main, t.D, t.MainNormal);
            TerminalFeature crossB = ResolveTerminal(t.Cross, t.B, t.CrossNormal);
            TerminalFeature crossC = ResolveTerminal(t.Cross, t.C, t.CrossNormal);
            P2 jointBolt = ResolveTerminalBolt(t.Main, t.Joint);

            Dictionary<string, List<P2>> legacyFeet = new Dictionary<string, List<P2>>(
                StringComparer.Ordinal
            );
            legacyFeet.Add(
                "T2-P-11",
                new List<P2> { mainA.ReferenceIntersection, jointBolt, mainD.ReferenceIntersection }
            );
            legacyFeet.Add(
                "T2-P-12",
                new List<P2> { t.A, mainA.ReferenceIntersection, mainD.ReferenceIntersection, t.D }
            );
            legacyFeet.Add(
                "T2-P-16",
                new List<P2>
                {
                    crossC.ReferenceIntersection,
                    jointBolt,
                    crossB.ReferenceIntersection
                }
            );
            legacyFeet.Add(
                "T2-P-17",
                new List<P2>
                {
                    t.C,
                    crossC.ReferenceIntersection,
                    crossB.ReferenceIntersection,
                    t.B
                }
            );

            List<TSD.StraightDimensionSet> matched = new List<TSD.StraightDimensionSet>();
            HashSet<int> used = new HashSet<int>();
            for (int p = 0; p < plans.Count; p++)
            {
                DimPlan plan = plans[p];
                List<P2> legacy;
                legacyFeet.TryGetValue(plan.Name, out legacy);
                TSD.StraightDimensionSet found = null;
                for (int i = 0; i < all.Count; i++)
                {
                    TSD.StraightDimensionSet set = all[i];
                    int key = RuntimeHelpers.GetHashCode(set);
                    if (used.Contains(key) || !DimensionBelongsToView(set, plan.View.View))
                        continue;
                    List<P2> existing = ReadDimensionPoints(set);
                    bool currentMatch = PointChainsMatch(existing, plan.Points, MatchTolerance);
                    bool legacyMatch =
                        legacy != null && PointChainsMatch(existing, legacy, MatchTolerance);
                    if (
                        (!currentMatch && !legacyMatch)
                        || !Type2MigrationDirectionMatches(set, plan)
                    )
                        continue;
                    found = set;
                    used.Add(key);
                    break;
                }
                if (found == null)
                    return false;
                matched.Add(found);
            }

            if (matched.Count != Type2ExpectedPlanCount)
                return false;
            result.Matched.Clear();
            result.Matched.AddRange(matched);
            result.ProtectedCount = all.Count - matched.Count;
            return true;
        }

        private static bool Type2MigrationDirectionMatches(
            TSD.StraightDimensionSet set,
            DimPlan plan
        )
        {
            if (DimensionDirectionMatches(set, plan.PlacementNormal))
                return true;
            if (!String.Equals(plan.Name, "T2-P-19", StringComparison.Ordinal))
                return false;

            P2 actual = ReadPointLikeMember(set, "UpDirection");
            if (actual == null)
                actual = ReadPointLikeMember(set, "OffsetDirection");
            actual = Normalize(actual);
            P2 expected = Normalize(plan.PlacementNormal);
            return actual != null && expected != null && Math.Abs(Dot(actual, expected)) >= 0.95;
        }

        private static void ValidateType2Plans(Context context, List<DimPlan> plans)
        {
            if (plans == null || plans.Count != Type2ExpectedPlanCount)
                throw new InvalidOperationException(
                    "Slot 06 Type2 phai co dung "
                        + Type2ExpectedPlanCount
                        + " dimension plans; thuc te="
                        + (plans == null ? 0 : plans.Count)
                        + "."
                );

            int planCount = 0;
            int sectionCount = 0;
            HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || plan.View == null
                    || plan.MeasurementAxis == null
                    || plan.PlacementNormal == null
                )
                    throw new InvalidOperationException("Plan Slot 06 Type2 bi null.");
                if (plan.Points.Count < 2)
                    throw new InvalidOperationException(plan.Name + " co it hon hai chan dim.");
                if (!IsFinite(plan.Distance) || plan.Distance <= PointTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " co khoang dat dim khong hop le."
                    );

                double min = Double.PositiveInfinity;
                double max = Double.NegativeInfinity;
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    P2 point = plan.Points[p];
                    if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                        throw new InvalidOperationException(
                            plan.Name + " co chan dim khong huu han."
                        );
                    double projection = Dot(point, plan.MeasurementAxis);
                    min = Math.Min(min, projection);
                    max = Math.Max(max, projection);
                }
                if (max - min <= MinimumMeasuredSpan)
                    throw new InvalidOperationException(plan.Name + " co measured span bang 0.");

                string signature =
                    RuntimeHelpers.GetHashCode(plan.View).ToString(CultureInfo.InvariantCulture)
                    + "|"
                    + plan.Name;
                if (!signatures.Add(signature))
                    throw new InvalidOperationException(
                        "Trung semantic signature " + plan.Name + "."
                    );
                if (Object.ReferenceEquals(plan.View, context.PlanView))
                    planCount++;
                else if (Object.ReferenceEquals(plan.View, context.SectionView))
                    sectionCount++;
                else
                    throw new InvalidOperationException(plan.Name + " thuoc view khong duoc phep.");
            }
            if (planCount != Type2PlanViewPlanCount || sectionCount != Type2SectionViewPlanCount)
                throw new InvalidOperationException(
                    "Sai so plan Type2 theo view: main="
                        + planCount
                        + ", section="
                        + sectionCount
                        + "."
                );
        }
    }
}
#endregion
