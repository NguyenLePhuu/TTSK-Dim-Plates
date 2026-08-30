#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Slot 10 - dimension a beam-end connection from its actual local REF graph.
    /// No coordinate is treated as a semantic anchor merely because it coincides
    /// with the assembly MainPart centre line.
    /// </summary>
    public class PHU_AutoDimSlot10
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunMessage = String.Empty;
            string message = PHU_Slot10RefConnectionDimensionEngine.Run();
            if (!String.IsNullOrWhiteSpace(message))
            {
                LastRunSucceeded = true;
                LastRunMessage = message;
            }
        }

        /// <summary>Read-only. Does not create, modify, delete or commit.</summary>
        public static string AuditPlan()
        {
            return PHU_Slot10RefConnectionDimensionEngine.AuditPlan();
        }

        /// <summary>Read-only one-side topology regression.</summary>
        public static string AuditSideRegression()
        {
            return PHU_Slot10RefConnectionDimensionEngine.AuditSideRegression();
        }
    }

    internal static class PHU_Slot10RefConnectionDimensionEngine
    {
        private const double PointTolerance = 0.75;
        private const double RefNodeTolerance = 2.0;
        private const double DirectionParallel = 0.97;
        private const double DirectionPerpendicular = 0.20;
        private const double DepthOverlapTolerance = 1.0;

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
            public bool IsDrawingMain;
            public bool IsContourPlate;
            public string Name;
            public string Profile;
            public string Position;
            public double MinViewDepth = Double.PositiveInfinity;
            public double MaxViewDepth = Double.NegativeInfinity;
            public readonly List<P2> Reference = new List<P2>();
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<Segment2> Segments = new List<Segment2>();
            public readonly Dictionary<int, BoltData> BoltGroups =
                new Dictionary<int, BoltData>();
        }

        private sealed class ViewData
        {
            public TSD.View View;
            public int Identifier;
            public string Label;
            public double Scale;
            public double OriginX;
            public bool IsLeftOnSheet;
            public PartData DrawingMain;
            public readonly List<PartData> Parts = new List<PartData>();
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes DimensionAttributes;
        }

        private sealed class ArmData
        {
            public PartData Part;
            public P2 JointRef;
            public P2 OuterRef;
            public P2 Axis;
            public P2 OuterLow;
            public P2 OuterHigh;
            public P2 OuterNodeLevel;
            public P2 TerminalBolt;
            public double LowN;
            public double HighN;
        }

        private sealed class Topology
        {
            public ViewData View;
            public PartData DrawingMain;
            public PartData Connector;
            public P2 MainJoinRef;
            public P2 NodeRef;
            public P2 ConnectorAxis;
            public readonly List<ArmData> Arms = new List<ArmData>();
            public PartData LowerPlate;
            public PartData UpperPlate;
        }

        private sealed class PeripheralNeighborTopology
        {
            public ViewData View;
            public PartData Column;
            public PartData Branch;
            public PartData NodePlate;
            public P2 ColumnAxis;
            public P2 ColumnReferencePoint;
            public P2 JointReference;
            public P2 OuterReference;
            public P2 BranchAxis;
            public P2 TerminalLow;
            public P2 TerminalHigh;
            public P2 TerminalAtReference;
            public P2 ReferenceSideBolt;
            public P2 ColumnEdgeAtLevel;
            public int Side;
        }

        private sealed class TerminalBoltRow
        {
            public BoltData Group;
            public double GapFromTerminal;
            public bool IsContourPlateRow;
            public bool IsContourPlateFaceVisible;
            public readonly List<P2> Points = new List<P2>();
        }

        private sealed class ColumnTerminalTopology
        {
            public ViewData View;
            public PartData Column;
            public P2 ColumnAxis;
            public P2 TransverseAxis;
            public P2 TerminalReference;
            public P2 TerminalFaceLeft;
            public P2 TerminalFaceRight;
            public double TerminalFaceProjection;
            public double CrossDepth;
            public readonly List<TerminalBoltRow> BoltRows =
                new List<TerminalBoltRow>();
        }

        private sealed class AnchorPoint
        {
            public P2 Point;
            public string Semantic;

            public AnchorPoint(P2 point, string semantic)
            {
                Point = point;
                Semantic = semantic;
            }
        }

        private sealed class DimPlan
        {
            public string Name;
            public string Semantic;
            public ViewData View;
            public readonly List<AnchorPoint> Anchors = new List<AnchorPoint>();
            public P2 PlacementNormal;
            public double Distance;
            public bool InheritedDistance;
            public bool AllowInheritedDistance = true;
            public bool DisableCombine;
            public bool RegisterVerticalTier;
            public bool ForcePlannedDistance;
            public double LineCoordinate = Double.NaN;
        }

        private sealed class DistanceRelocation
        {
            public string PlanName;
            public TSD.StraightDimensionSet Dimension;
            public P2 ActualFirst;
            public P2 ActualNormal;
            public double OriginalDistance;
            public double TargetDistance;
            public double TargetProjection;
            public bool Applied;
        }

        internal sealed class InzaiFlowResult
        {
            public bool Applicable;
            public bool Success;
            public int CreatedCount;
            public int ReplacedCount;
            public string Message;
        }

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part DrawingMainPart;
            public TSM.TransformationPlane OriginalPlane;
            public readonly List<ViewData> Views = new List<ViewData>();
            public readonly List<Topology> Topologies = new List<Topology>();
            public readonly List<PeripheralNeighborTopology> PeripheralNeighbors =
                new List<PeripheralNeighborTopology>();
            public readonly List<ColumnTerminalTopology> ColumnTerminals =
                new List<ColumnTerminalTopology>();
        }

        private sealed class ReplacementSnapshot
        {
            public readonly List<TSD.StraightDimensionSet> Matched =
                new List<TSD.StraightDimensionSet>();
            public int ExistingCount;
            public int ProtectedCount;
        }

        internal static string Run()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildPlans(context);
                ValidatePlans(plans);
                ReplacementSnapshot replacement = SnapshotReplaceableDimensions(context, plans);

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList points = new TSD.PointList();
                    for (int p = 0; p < plan.Anchors.Count; p++)
                        points.Add(plan.Anchors[p].Point.ToPoint());

                    TSG.Vector normal = new TSG.Vector(
                        plan.PlacementNormal.X,
                        plan.PlacementNormal.Y,
                        0.0);
                    TSD.StraightDimensionSet dimension =
                        plan.View.DimensionAttributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                normal,
                                plan.Distance)
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                normal,
                                plan.Distance,
                                plan.View.DimensionAttributes);
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla khong tao duoc " + plan.Name + ".");
                    created.Add(dimension);
                    if (plan.DisableCombine)
                        DisableCombine(dimension, plan.Anchors.Count, plan.Name);
                }

                VerifyCreatedPlans(plans, created);

                int deleted = 0;
                for (int i = 0; i < replacement.Matched.Count; i++)
                {
                    if (replacement.Matched[i] != null && replacement.Matched[i].Delete())
                        deleted++;
                }
                if (deleted != replacement.Matched.Count)
                    throw new InvalidOperationException(
                        "Khong xoa duoc day du dimension Slot 10 cu; dung truoc CommitChanges.");

                context.Drawing.CommitChanges();
                return "Slot 10: tao "
                    + created.Count
                    + " dim theo REF/EDGE/BOLT/PLATE, thay "
                    + deleted
                    + " dim cu, bao toan "
                    + replacement.ProtectedCount
                    + " dim khong thuoc Slot 10.";
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
                    catch { }
                }
                ShowWarning("Slot 10 da dung an toan.\r\n\r\n" + ex.Message);
                return null;
            }
        }

        internal static InzaiFlowResult RunInzaiColumnFlow()
        {
            InzaiFlowResult result = new InzaiFlowResult();
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            List<DistanceRelocation> relocations = new List<DistanceRelocation>();
            List<TSD.StraightDimensionSet> obsoleteType1Horizontals =
                new List<TSD.StraightDimensionSet>();
            Context context = null;
            int deletedObsolete = 0;
            try
            {
                int leftViewIdentifier;
                int rightViewIdentifier;
                TSG.Point leftOrigin;
                TSG.Point rightOrigin;
                if (
                    !PHU_VerticalShapeViewLayoutContext.TryGetCapturedViewOrder(
                        out leftViewIdentifier,
                        out rightViewIdentifier,
                        out leftOrigin,
                        out rightOrigin)
                )
                {
                    result.Success = true;
                    result.Applicable = false;
                    result.Message =
                        "Inzai Splice DIM skipped: the two user-positioned column views were not captured.";
                    return result;
                }

                context = AnalyzeDrawing(
                    false,
                    leftViewIdentifier,
                    rightViewIdentifier);
                if (!ContainsBothCapturedViews(
                    context,
                    leftViewIdentifier,
                    rightViewIdentifier))
                {
                    result.Success = true;
                    result.Applicable = false;
                    result.Message =
                        "Inzai Splice DIM skipped: a captured column view is no longer available.";
                    return result;
                }
                if (
                    context.Topologies.Count == 0
                    && context.PeripheralNeighbors.Count == 0
                    && context.ColumnTerminals.Count == 0
                )
                {
                    result.Success = true;
                    result.Applicable = false;
                    result.Message =
                        "Inzai Connection DIM skipped: no certain main-splice, peripheral child-beam or column-terminal topology was found.";
                    return result;
                }

                List<DimPlan> plans = BuildInzaiPlans(context);
                ValidatePlans(plans);
                bool type1Family = IsType1ColumnTerminalFamily(context);
                bool type2Family = IsType2ColumnTerminalFamily(context);
                if (type1Family)
                {
                    obsoleteType1Horizontals.AddRange(
                        SnapshotType1ObsoleteLeftTerminalHorizontals(context));
                }
                if (type1Family || type2Family)
                {
                    obsoleteType1Horizontals.AddRange(
                        SnapshotObsoleteRightTerminalReplacement(context));
                }
                int reused = MarkExistingPlans(context, plans, relocations);
                if (type1Family)
                    AppendType1LowerNearEdgeRelocation(context, relocations);

                List<DimPlan> missing = new List<DimPlan>();
                for (int i = 0; i < plans.Count; i++)
                {
                    if (!plans[i].InheritedDistance)
                        missing.Add(plans[i]);
                }

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < missing.Count; i++)
                {
                    DimPlan plan = missing[i];
                    TSD.PointList points = new TSD.PointList();
                    for (int p = 0; p < plan.Anchors.Count; p++)
                        points.Add(plan.Anchors[p].Point.ToPoint());

                    TSG.Vector normal = new TSG.Vector(
                        plan.PlacementNormal.X,
                        plan.PlacementNormal.Y,
                        0.0);
                    TSD.StraightDimensionSet dimension =
                        plan.View.DimensionAttributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                normal,
                                plan.Distance)
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                normal,
                                plan.Distance,
                                plan.View.DimensionAttributes);
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla did not create " + plan.Name + ".");
                    created.Add(dimension);
                    if (plan.DisableCombine)
                        DisableCombine(dimension, plan.Anchors.Count, plan.Name);
                }

                VerifyCreatedPlans(missing, created);
                ApplyDistanceRelocations(relocations);
                RegisterInzaiVerticalTiers(plans);
                PublishCompositeColumnGeometry(context);
                deletedObsolete = DeleteExactDimensions(obsoleteType1Horizontals);
                if (created.Count > 0
                    || relocations.Count > 0
                    || deletedObsolete > 0)
                    context.Drawing.CommitChanges();

                result.Success = true;
                result.Applicable = true;
                result.CreatedCount = created.Count;
                result.ReplacedCount = reused;
                result.Message =
                    "Inzai Connection DIM satisfied "
                    + plans.Count.ToString(CultureInfo.InvariantCulture)
                    + " plans: created "
                    + created.Count.ToString(CultureInfo.InvariantCulture)
                    + ", reused "
                    + reused.ToString(CultureInfo.InvariantCulture)
                    + " exact existing DIMs, relocated "
                    + relocations.Count.ToString(CultureInfo.InvariantCulture)
                    + " terminal tier(s), removed "
                    + deletedObsolete.ToString(CultureInfo.InvariantCulture)
                    + " exact obsolete Type-1 terminal DIM(s).";
                return result;
            }
            catch (Exception ex)
            {
                RestoreDistanceRelocations(relocations);
                for (int i = 0; i < created.Count; i++)
                {
                    try
                    {
                        if (created[i] != null)
                            created[i].Delete();
                    }
                    catch { }
                }
                result.Success = false;
                result.Applicable = false;
                result.CreatedCount = 0;
                result.Message =
                    "Inzai Connection DIM skipped; uncertain/new DIMs were rolled back: "
                    + ex.Message;
                return result;
            }
        }

        internal static string AuditInzaiColumnPlan()
        {
            try
            {
                int leftViewIdentifier;
                int rightViewIdentifier;
                TSG.Point leftOrigin;
                TSG.Point rightOrigin;
                if (
                    !PHU_VerticalShapeViewLayoutContext.TryGetCapturedViewOrder(
                        out leftViewIdentifier,
                        out rightViewIdentifier,
                        out leftOrigin,
                        out rightOrigin)
                )
                    return "INZAI SPLICE AUDIT not applicable: the two column views were not captured.";

                Context context = AnalyzeDrawing(
                    false,
                    leftViewIdentifier,
                    rightViewIdentifier);
                if (!ContainsBothCapturedViews(
                    context,
                    leftViewIdentifier,
                    rightViewIdentifier))
                    return "INZAI SPLICE AUDIT not applicable: a captured column view is unavailable.";
                if (
                    context.Topologies.Count == 0
                    && context.PeripheralNeighbors.Count == 0
                    && context.ColumnTerminals.Count == 0
                )
                    return "INZAI SPLICE AUDIT not applicable: no certain topology.";
                List<DimPlan> plans = BuildInzaiPlans(context);
                ValidatePlans(plans);
                List<DistanceRelocation> relocations =
                    new List<DistanceRelocation>();
                int reused = MarkExistingPlans(context, plans, relocations);
                bool type1Family = IsType1ColumnTerminalFamily(context);
                bool type2Family = IsType2ColumnTerminalFamily(context);
                if (type1Family)
                    AppendType1LowerNearEdgeRelocation(context, relocations);
                int obsoleteLeftHorizontalCount = type1Family
                    ? SnapshotType1ObsoleteLeftTerminalHorizontals(context).Count
                    : 0;
                int obsoleteRightReplacementCount = type1Family || type2Family
                    ? SnapshotObsoleteRightTerminalReplacement(context).Count
                    : 0;
                StringBuilder text = new StringBuilder();
                text.AppendLine("INZAI SPLICE DIM PLAN - READ ONLY");
                text.AppendLine("No dimension/model object was created, modified, deleted or committed.");
                text.Append("TopologyCount=").Append(context.Topologies.Count)
                    .Append(" PeripheralNeighborCount=")
                    .Append(context.PeripheralNeighbors.Count)
                    .Append(" ColumnTerminalCount=")
                    .Append(context.ColumnTerminals.Count)
                    .Append(" PlanCount=").Append(plans.Count)
                    .Append(" ExistingSatisfied=").Append(reused)
                    .AppendLine();
                text.Append("Type1TerminalFamily=").Append(type1Family)
                    .Append(" Type2TerminalFamily=").Append(type2Family)
                    .Append(" ForcedTierRelocationCount=")
                    .Append(relocations.Count)
                    .Append(" ObsoleteLeftHorizontalCount=")
                    .Append(obsoleteLeftHorizontalCount)
                    .Append(" ObsoleteRightReplacementCount=")
                    .Append(obsoleteRightReplacementCount)
                    .AppendLine();
                for (int i = 0; i < context.Topologies.Count; i++)
                {
                    Topology topology = context.Topologies[i];
                    text.Append("TOPOLOGY ").Append(i + 1)
                        .Append(" view=").Append(topology.View.Label)
                        .Append(" connector=P").Append(topology.Connector.ModelId)
                        .Append(" nodeREF=").Append(FormatPoint(topology.NodeRef))
                        .Append(" arms=").Append(topology.Arms.Count)
                        .Append(" lowerContour=").Append(PartLabel(topology.LowerPlate))
                        .Append(" upperContour=").Append(PartLabel(topology.UpperPlate))
                        .AppendLine();
                    for (int a = 0; a < topology.Arms.Count; a++)
                    {
                        ArmData arm = topology.Arms[a];
                        text.Append("  ARM P").Append(arm.Part.ModelId)
                            .Append(" outerREF=").Append(FormatPoint(arm.OuterRef))
                            .Append(" terminalEDGE=").Append(FormatPoint(arm.OuterNodeLevel))
                            .Append(" REF-side web-hole=").Append(FormatPoint(arm.TerminalBolt))
                            .AppendLine();
                    }
                }
                for (int i = 0; i < context.PeripheralNeighbors.Count; i++)
                {
                    PeripheralNeighborTopology topology = context.PeripheralNeighbors[i];
                    text.Append("PERIPHERAL ").Append(i + 1)
                        .Append(" view=").Append(topology.View.Label)
                        .Append(" column=P").Append(topology.Column.ModelId)
                        .Append(" child=P").Append(topology.Branch.ModelId)
                        .Append(" nodePlate=P").Append(topology.NodePlate.ModelId)
                        .Append(" jointREF=").Append(FormatPoint(topology.JointReference))
                        .Append(" terminal=").Append(FormatPoint(topology.TerminalLow))
                        .Append("->").Append(FormatPoint(topology.TerminalHigh))
                        .Append(" REF-side-hole=")
                        .Append(FormatPoint(topology.ReferenceSideBolt))
                        .Append(" side=").Append(topology.Side)
                        .AppendLine();
                }
                for (int i = 0; i < context.ColumnTerminals.Count; i++)
                {
                    ColumnTerminalTopology topology = context.ColumnTerminals[i];
                    text.Append("TERMINAL ").Append(i + 1)
                        .Append(" view=").Append(topology.View.Label)
                        .Append(" column=P").Append(topology.Column.ModelId)
                        .Append(" terminalREF=").Append(FormatPoint(topology.TerminalReference))
                        .Append(" face=").Append(FormatPoint(topology.TerminalFaceLeft))
                        .Append("->").Append(FormatPoint(topology.TerminalFaceRight))
                        .Append(" rows=").Append(topology.BoltRows.Count)
                        .AppendLine();
                    for (int r = 0; r < topology.BoltRows.Count; r++)
                    {
                        TerminalBoltRow row = topology.BoltRows[r];
                        text.Append("  ROW G").Append(row.Group.Id)
                            .Append(" gap=").Append(Format(row.GapFromTerminal))
                            .Append(" points=");
                        for (int p = 0; p < row.Points.Count; p++)
                        {
                            if (p > 0)
                                text.Append(",");
                            text.Append(FormatPoint(row.Points[p]));
                        }
                        text.Append(" contourRow=").Append(row.IsContourPlateRow)
                            .Append(" contourFaceVisible=")
                            .Append(row.IsContourPlateFaceVisible)
                            .AppendLine();
                    }
                }
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append("PLAN ").Append(plan.Name)
                        .Append(" normal=").Append(FormatPoint(plan.PlacementNormal))
                        .Append(" distance=").Append(Format(plan.Distance))
                        .Append(" lineX=").Append(Format(plan.LineCoordinate))
                        .Append(" semantic=").Append(plan.Semantic)
                        .AppendLine();
                    for (int p = 0; p < plan.Anchors.Count; p++)
                    {
                        text.Append("  foot[").Append(p).Append("]=")
                            .Append(FormatPoint(plan.Anchors[p].Point))
                            .Append(" <- ").Append(plan.Anchors[p].Semantic)
                            .AppendLine();
                    }
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "INZAI SPLICE AUDIT failed: " + ex.Message;
            }
        }

        internal static string AuditInzaiGeometryRegression()
        {
            int passed = 0;
            int total = 24;
            try
            {
                P2 node = new P2(0.0, 0.0);
                P2 connectorAxis = new P2(0.0, 1.0);

                PartData right = BuildTerminalBoltRegressionPart(false, false);
                P2 rightBolt = ResolveTerminalBolt(
                    right,
                    node,
                    new P2(1.0, 0.0),
                    connectorAxis,
                    new P2(900.0, 0.0),
                    -294.0,
                    0.0);
                if (Distance(rightBolt, new P2(860.0, -87.0)) <= PointTolerance)
                    passed++;

                PartData rightReversed = BuildTerminalBoltRegressionPart(false, true);
                P2 rightReversedBolt = ResolveTerminalBolt(
                    rightReversed,
                    node,
                    new P2(1.0, 0.0),
                    connectorAxis,
                    new P2(900.0, 0.0),
                    -294.0,
                    0.0);
                if (Distance(rightReversedBolt, new P2(860.0, -87.0)) <= PointTolerance)
                    passed++;

                PartData left = BuildTerminalBoltRegressionPart(true, false);
                P2 leftBolt = ResolveTerminalBolt(
                    left,
                    node,
                    new P2(-1.0, 0.0),
                    connectorAxis,
                    new P2(-900.0, 0.0),
                    -294.0,
                    0.0);
                if (Distance(leftBolt, new P2(-850.0, -87.0)) <= PointTolerance)
                    passed++;

                PartData beamModelledSplice = new PartData();
                beamModelledSplice.Name = "RENAMED";
                beamModelledSplice.Profile = "RENAMED";
                beamModelledSplice.IsContourPlate = false;
                if (!IsPlateLike(beamModelledSplice))
                    passed++;

                PartData nodeContour = new PartData();
                nodeContour.Name = "ANY";
                nodeContour.Profile = "ANY";
                nodeContour.IsContourPlate = true;
                if (IsPlateLike(nodeContour))
                    passed++;

                ViewData leftPeripheral = BuildPeripheralRegressionView(false, false);
                List<PeripheralNeighborTopology> leftPeripheralResult =
                    ResolvePeripheralNeighborTopologies(leftPeripheral);
                if (
                    leftPeripheralResult.Count == 1
                    && leftPeripheralResult[0].Side == -1
                    && Distance(
                        leftPeripheralResult[0].ReferenceSideBolt,
                        new P2(-160.0, 150.0)) <= PointTolerance
                )
                    passed++;

                ViewData mirroredPeripheral = BuildPeripheralRegressionView(true, true);
                List<PeripheralNeighborTopology> mirroredPeripheralResult =
                    ResolvePeripheralNeighborTopologies(mirroredPeripheral);
                if (
                    mirroredPeripheralResult.Count == 1
                    && mirroredPeripheralResult[0].Side == 1
                    && Distance(
                        mirroredPeripheralResult[0].ReferenceSideBolt,
                        new P2(160.0, 150.0)) <= PointTolerance
                )
                    passed++;

                PartData faceVisiblePlate = new PartData();
                AddRectangle(faceVisiblePlate, -70.0, 70.0, 0.0, 100.0, false);
                if (IsContourPlateFaceVisible(
                    faceVisiblePlate,
                    new P2(1.0, 0.0),
                    294.0))
                    passed++;

                PartData edgeOnPlate = new PartData();
                AddRectangle(edgeOnPlate, -16.0, -4.0, 0.0, 100.0, true);
                if (!IsContourPlateFaceVisible(
                    edgeOnPlate,
                    new P2(1.0, 0.0),
                    200.0))
                    passed++;

                if (AdjacentPlateRegression(false, false, false))
                    passed++;
                if (AdjacentPlateRegression(true, false, false))
                    passed++;
                if (AdjacentPlateRegression(true, true, true))
                    passed++;

                Context type1 = BuildTerminalFamilyRegressionContext(200.0, 200.0);
                Context type2 = BuildTerminalFamilyRegressionContext(294.0, 200.0);
                if (IsType1ColumnTerminalFamily(type1)
                    && !IsType1ColumnTerminalFamily(type2)
                    && !IsType2ColumnTerminalFamily(type1)
                    && IsType2ColumnTerminalFamily(type2))
                    passed++;

                List<DimPlan> type1Plans = new List<DimPlan>();
                BuildColumnTerminalPlans(
                    type1,
                    type1Plans,
                    200.0,
                    200.0,
                    true,
                    true,
                    false);
                if (type1Plans.Count == 6
                    && CountPlansContaining(type1Plans, "V01", "WIDTH-CHAIN") == 0
                    && CountPlansContaining(type1Plans, "V01", "EDGE-REF-EDGE") == 0)
                    passed++;

                DimPlan type1Near = FindPlanContaining(
                    type1Plans,
                    "V01",
                    "ROW-01-TO-END");
                DimPlan type1Far = FindPlanContaining(
                    type1Plans,
                    "V01",
                    "ROW-02-TO-END");
                double nearProjection = type1Near == null
                    ? Double.NaN
                    : Dot(type1Near.Anchors[0].Point, type1Near.PlacementNormal)
                        + type1Near.Distance;
                double farProjection = type1Far == null
                    ? Double.NaN
                    : Dot(type1Far.Anchors[0].Point, type1Far.PlacementNormal)
                        + type1Far.Distance;
                if (type1Near != null
                    && type1Far != null
                    && type1Near.ForcePlannedDistance
                    && type1Far.ForcePlannedDistance
                    && Math.Abs((farProjection - nearProjection) - 200.0)
                        <= PointTolerance)
                    passed++;

                DimPlan type1RightFarChain = FindPlanContaining(
                    type1Plans,
                    "V02",
                    "ROW-02-WIDTH-CHAIN");
                double type1RightFarChainProjection = type1RightFarChain == null
                    ? Double.NaN
                    : Dot(
                        type1RightFarChain.Anchors[0].Point,
                        type1RightFarChain.PlacementNormal)
                        + type1RightFarChain.Distance;
                if (type1RightFarChain != null
                    && Dot(
                        type1RightFarChain.PlacementNormal,
                        type1.ColumnTerminals[1].ColumnAxis) >= DirectionParallel
                    && Math.Abs(type1RightFarChainProjection - 6566.0)
                        <= PointTolerance)
                    passed++;

                double type1LowerNearProjection =
                    ResolveType1NearEdgeTierProjection(
                        type1.ColumnTerminals[0],
                        200.0);
                if (type1Near != null
                    && Math.Abs(type1LowerNearProjection - nearProjection)
                        <= PointTolerance)
                    passed++;

                List<DimPlan> type2Plans = new List<DimPlan>();
                BuildColumnTerminalPlans(
                    type2,
                    type2Plans,
                    200.0,
                    200.0,
                    false,
                    true,
                    true);
                if (type2Plans.Count == 9
                    && CountPlansContaining(type2Plans, "V01", "WIDTH-CHAIN") == 2
                    && CountPlansContaining(type2Plans, "V01", "EDGE-REF-EDGE") == 1
                    && CountForcedPlans(type2Plans) == 4)
                    passed++;

                DimPlan type2TerminalAxis = FindPlanContaining(
                    type2Plans,
                    "V01",
                    "EDGE-REF-EDGE");
                double type2TerminalAxisProjection = type2TerminalAxis == null
                    ? Double.NaN
                    : Dot(
                        type2TerminalAxis.Anchors[0].Point,
                        type2TerminalAxis.PlacementNormal)
                        + type2TerminalAxis.Distance;
                if (type2TerminalAxis != null
                    && type2TerminalAxis.ForcePlannedDistance
                    && Math.Abs(type2TerminalAxisProjection - 6786.0)
                        <= PointTolerance)
                    passed++;

                DimPlan type2LeftNearHole = FindPlanContaining(
                    type2Plans,
                    "V01",
                    "ROW-01-WIDTH-CHAIN");
                DimPlan type2LeftFarHole = FindPlanContaining(
                    type2Plans,
                    "V01",
                    "ROW-02-WIDTH-CHAIN");
                double type2NearHoleProjection = type2LeftNearHole == null
                    ? Double.NaN
                    : Dot(
                        type2LeftNearHole.Anchors[0].Point,
                        type2LeftNearHole.PlacementNormal)
                        + type2LeftNearHole.Distance;
                double type2FarHoleProjection = type2LeftFarHole == null
                    ? Double.NaN
                    : Dot(
                        type2LeftFarHole.Anchors[0].Point,
                        type2LeftFarHole.PlacementNormal)
                        + type2LeftFarHole.Distance;
                if (type2LeftNearHole != null
                    && type2LeftFarHole != null
                    && type2LeftNearHole.ForcePlannedDistance
                    && type2LeftFarHole.ForcePlannedDistance
                    && Math.Abs(type2NearHoleProjection - 6386.0)
                        <= PointTolerance
                    && Math.Abs(type2FarHoleProjection - 6586.0)
                        <= PointTolerance
                    && Math.Abs(type2TerminalAxisProjection - type2FarHoleProjection
                        - 200.0) <= PointTolerance)
                    passed++;

                DimPlan type2RightFarChain = FindPlanContaining(
                    type2Plans,
                    "V02",
                    "ROW-02-WIDTH-CHAIN");
                double type2RightFarChainProjection = type2RightFarChain == null
                    ? Double.NaN
                    : Dot(
                        type2RightFarChain.Anchors[0].Point,
                        type2RightFarChain.PlacementNormal)
                        + type2RightFarChain.Distance;
                if (type2RightFarChain != null
                    && Dot(
                        type2RightFarChain.PlacementNormal,
                        type2.ColumnTerminals[1].ColumnAxis) >= DirectionParallel
                    && type2RightFarChain.ForcePlannedDistance
                    && Math.Abs(type2RightFarChainProjection - 6386.0)
                        <= PointTolerance)
                    passed++;

                Context unsupportedRectangular =
                    BuildTerminalFamilyRegressionContext(250.0, 200.0);
                if (!IsType1ColumnTerminalFamily(unsupportedRectangular)
                    && !IsType2ColumnTerminalFamily(unsupportedRectangular))
                    passed++;

                double type2LeftAxisTier = ResolveType2TerminalTierLine(
                    type2.ColumnTerminals[0],
                    type2.ColumnTerminals[0].ColumnAxis,
                    200.0,
                    200.0,
                    2);
                if (Math.Abs(type2LeftAxisTier - 6586.0) <= PointTolerance)
                    passed++;

                double type2RightAxisTier = ResolveType2TerminalTierLine(
                    type2.ColumnTerminals[1],
                    type2.ColumnTerminals[1].ColumnAxis,
                    200.0,
                    200.0,
                    2);
                if (Math.Abs(type2RightAxisTier - type2LeftAxisTier)
                    <= PointTolerance)
                    passed++;

                return "INZAI SPLICE PURE REGRESSION="
                    + passed.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + total.ToString(CultureInfo.InvariantCulture)
                    + (passed == total ? " PASS" : " FAIL");
            }
            catch (Exception ex)
            {
                return "INZAI SPLICE PURE REGRESSION="
                    + passed.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + total.ToString(CultureInfo.InvariantCulture)
                    + " FAIL "
                    + ex.Message;
            }
        }

        private static Context BuildTerminalFamilyRegressionContext(
            double leftDepth,
            double rightDepth)
        {
            Context context = new Context();
            PartData column = new PartData();
            column.ModelId = 100;
            context.ColumnTerminals.Add(BuildTerminalFamilyRegressionTopology(
                column,
                1,
                "V01",
                true,
                leftDepth));
            context.ColumnTerminals.Add(BuildTerminalFamilyRegressionTopology(
                column,
                2,
                "V02",
                false,
                rightDepth));
            return context;
        }

        private static ColumnTerminalTopology BuildTerminalFamilyRegressionTopology(
            PartData column,
            int viewIdentifier,
            string label,
            bool isLeft,
            double crossDepth)
        {
            ColumnTerminalTopology terminal = new ColumnTerminalTopology();
            terminal.View = new ViewData();
            terminal.View.Identifier = viewIdentifier;
            terminal.View.Label = label;
            terminal.View.IsLeftOnSheet = isLeft;
            terminal.Column = column;
            terminal.ColumnAxis = new P2(0.0, 1.0);
            terminal.TransverseAxis = new P2(1.0, 0.0);
            terminal.TerminalReference = new P2(0.0, 6186.0);
            terminal.TerminalFaceLeft = new P2(-crossDepth * 0.5, 6166.0);
            terminal.TerminalFaceRight = new P2(crossDepth * 0.5, 6166.0);
            terminal.TerminalFaceProjection = 6166.0;
            terminal.CrossDepth = crossDepth;
            terminal.BoltRows.Add(BuildTerminalFamilyRegressionRow(
                1,
                40.0,
                -30.0,
                30.0,
                6126.0));
            terminal.BoltRows.Add(BuildTerminalFamilyRegressionRow(
                2,
                300.0,
                -70.0,
                70.0,
                5866.0));
            return terminal;
        }

        private static TerminalBoltRow BuildTerminalFamilyRegressionRow(
            int identifier,
            double gap,
            double firstX,
            double secondX,
            double y)
        {
            TerminalBoltRow row = new TerminalBoltRow();
            row.Group = new BoltData();
            row.Group.Id = identifier;
            row.GapFromTerminal = gap;
            row.IsContourPlateFaceVisible = true;
            row.Points.Add(new P2(firstX, y));
            row.Points.Add(new P2(secondX, y));
            return row;
        }

        private static int CountPlansContaining(
            List<DimPlan> plans,
            string viewLabel,
            string nameFragment)
        {
            int result = 0;
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                if (plans[i].View != null
                    && String.Equals(
                        plans[i].View.Label,
                        viewLabel,
                        StringComparison.Ordinal)
                    && plans[i].Name.IndexOf(
                        nameFragment,
                        StringComparison.Ordinal) >= 0)
                    result++;
            }
            return result;
        }

        private static DimPlan FindPlanContaining(
            List<DimPlan> plans,
            string viewLabel,
            string nameFragment)
        {
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                if (plans[i].View != null
                    && String.Equals(
                        plans[i].View.Label,
                        viewLabel,
                        StringComparison.Ordinal)
                    && plans[i].Name.IndexOf(
                        nameFragment,
                        StringComparison.Ordinal) >= 0)
                    return plans[i];
            }
            return null;
        }

        private static int CountForcedPlans(List<DimPlan> plans)
        {
            int result = 0;
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                if (plans[i].ForcePlannedDistance)
                    result++;
            }
            return result;
        }

        private static bool AdjacentPlateRegression(
            bool addWrongDepthDecoy,
            bool addWrongFaceDecoy,
            bool reverse)
        {
            ViewData view = new ViewData();
            PartData main = new PartData();
            main.ModelId = 100;
            main.AssemblyId = 1;
            main.IsDrawingMain = true;
            view.DrawingMain = main;
            view.Parts.Add(main);

            PartData connector = BuildAdjacentPlateRegressionPart(
                101,
                -100.0,
                100.0,
                -278.0,
                -16.0,
                -147.0,
                147.0,
                false);
            view.Parts.Add(connector);

            PartData correctLower = BuildAdjacentPlateRegressionPart(
                200,
                -125.0,
                125.0,
                -297.0,
                -278.0,
                -172.0,
                172.0,
                true);
            PartData correctUpper = BuildAdjacentPlateRegressionPart(
                201,
                -125.0,
                125.0,
                -16.0,
                3.0,
                -172.0,
                172.0,
                true);

            List<PartData> candidates = new List<PartData>();
            if (addWrongDepthDecoy)
            {
                candidates.Add(BuildAdjacentPlateRegressionPart(
                    300,
                    -125.0,
                    125.0,
                    -297.0,
                    -278.0,
                    -439.0,
                    -314.0,
                    true));
            }
            if (addWrongFaceDecoy)
            {
                candidates.Add(BuildAdjacentPlateRegressionPart(
                    400,
                    -208.0,
                    0.0,
                    -303.0,
                    -294.0,
                    -147.0,
                    147.0,
                    true));
            }
            candidates.Add(correctLower);
            candidates.Add(correctUpper);
            if (reverse)
                candidates.Reverse();
            view.Parts.AddRange(candidates);

            Topology topology = new Topology();
            topology.View = view;
            topology.DrawingMain = main;
            topology.Connector = connector;
            topology.NodeRef = new P2(0.0, 0.0);
            topology.ConnectorAxis = new P2(0.0, 1.0);
            ArmData arm = new ArmData();
            arm.Part = new PartData();
            arm.Part.ModelId = 102;
            arm.Axis = new P2(1.0, 0.0);
            arm.LowN = -294.0;
            arm.HighN = 0.0;
            topology.Arms.Add(arm);

            ResolveAdjacentPlates(view, topology);
            return topology.LowerPlate == correctLower
                && topology.UpperPlate == correctUpper;
        }

        private static PartData BuildAdjacentPlateRegressionPart(
            int modelId,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double minDepth,
            double maxDepth,
            bool contourPlate)
        {
            PartData part = new PartData();
            part.ModelId = modelId;
            part.AssemblyId = 1;
            part.IsContourPlate = contourPlate;
            part.MinViewDepth = minDepth;
            part.MaxViewDepth = maxDepth;
            AddRectangle(part, minX, maxX, minY, maxY, false);
            return part;
        }

        private static PartData BuildTerminalBoltRegressionPart(bool left, bool reverse)
        {
            double sign = left ? -1.0 : 1.0;
            double webX = left ? -850.0 : 860.0;
            PartData part = new PartData();

            BoltData flange = new BoltData();
            flange.Id = 10;
            flange.Points.Add(new P2(sign * 740.0, -12.0));
            flange.Points.Add(new P2(sign * 800.0, -12.0));
            flange.Points.Add(new P2(sign * 860.0, -12.0));

            BoltData nearNode = new BoltData();
            nearNode.Id = 20;
            nearNode.Points.Add(new P2(sign * 27.0, -252.0));
            nearNode.Points.Add(new P2(sign * 27.0, -42.0));

            BoltData web = new BoltData();
            web.Id = 30;
            if (reverse)
            {
                web.Points.Add(new P2(webX, -87.0));
                web.Points.Add(new P2(webX, -147.0));
                web.Points.Add(new P2(webX, -207.0));
            }
            else
            {
                web.Points.Add(new P2(webX, -207.0));
                web.Points.Add(new P2(webX, -147.0));
                web.Points.Add(new P2(webX, -87.0));
            }

            BoltData beyondTerminal = new BoltData();
            beyondTerminal.Id = 40;
            beyondTerminal.Points.Add(new P2(sign * 950.0, -207.0));
            beyondTerminal.Points.Add(new P2(sign * 950.0, -87.0));

            if (reverse)
            {
                part.BoltGroups.Add(beyondTerminal.Id, beyondTerminal);
                part.BoltGroups.Add(web.Id, web);
                part.BoltGroups.Add(nearNode.Id, nearNode);
                part.BoltGroups.Add(flange.Id, flange);
            }
            else
            {
                part.BoltGroups.Add(flange.Id, flange);
                part.BoltGroups.Add(nearNode.Id, nearNode);
                part.BoltGroups.Add(web.Id, web);
                part.BoltGroups.Add(beyondTerminal.Id, beyondTerminal);
            }
            return part;
        }

        private static ViewData BuildPeripheralRegressionView(bool mirror, bool reverse)
        {
            double sign = mirror ? 1.0 : -1.0;
            ViewData view = new ViewData();

            PartData main = new PartData();
            main.ModelId = 100;
            main.AssemblyId = 1;
            main.IsDrawingMain = true;
            main.Reference.Add(new P2(0.0, reverse ? 1000.0 : 0.0));
            main.Reference.Add(new P2(0.0, reverse ? 0.0 : 1000.0));
            AddRectangle(main, -100.0, 100.0, 0.0, 1000.0, reverse);
            view.DrawingMain = main;
            view.Parts.Add(main);

            PartData branch = new PartData();
            branch.ModelId = 200;
            branch.AssemblyId = 2;
            P2 joint = new P2(sign * 100.0, 200.0);
            P2 outer = new P2(sign * 800.0, 200.0);
            branch.Reference.Add(reverse ? outer : joint);
            branch.Reference.Add(reverse ? joint : outer);
            AddRectangle(
                branch,
                mirror ? 120.0 : -800.0,
                mirror ? 800.0 : -120.0,
                100.0,
                200.0,
                reverse);
            BoltData shared = new BoltData();
            shared.Id = 501;
            shared.Points.Add(new P2(sign * 210.0, 150.0));
            shared.Points.Add(new P2(sign * 160.0, 150.0));
            branch.BoltGroups.Add(shared.Id, shared);
            view.Parts.Add(branch);

            PartData nodePlate = new PartData();
            nodePlate.ModelId = 300;
            nodePlate.AssemblyId = 1;
            nodePlate.IsContourPlate = true;
            AddRectangle(
                nodePlate,
                mirror ? 100.0 : -250.0,
                mirror ? 250.0 : -100.0,
                120.0,
                180.0,
                reverse);
            BoltData plateShared = new BoltData();
            plateShared.Id = shared.Id;
            plateShared.Points.AddRange(shared.Points);
            nodePlate.BoltGroups.Add(plateShared.Id, plateShared);
            view.Parts.Add(nodePlate);
            return view;
        }

        private static void AddRectangle(
            PartData part,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool reverse)
        {
            List<P2> points = new List<P2>
            {
                new P2(minX, minY),
                new P2(maxX, minY),
                new P2(maxX, maxY),
                new P2(minX, maxY)
            };
            if (reverse)
                points.Reverse();
            for (int i = 0; i < points.Count; i++)
            {
                part.Vertices.Add(points[i]);
                part.Segments.Add(new Segment2(points[i], points[(i + 1) % points.Count]));
            }
        }

        internal static string AuditPlan()
        {
            try
            {
                Context context = AnalyzeDrawing();
                List<DimPlan> plans = BuildPlans(context);
                ValidatePlans(plans);
                ReplacementSnapshot replacement = SnapshotReplaceableDimensions(context, plans);

                StringBuilder text = new StringBuilder();
                text.AppendLine("SLOT 10 REF CONNECTION DIM PLAN - READ ONLY");
                text.AppendLine("No dimension was created, modified, deleted or committed.");
                text.Append("TopologyCount=").Append(context.Topologies.Count)
                    .Append(" PlanCount=").Append(plans.Count)
                    .Append(" ExistingStraightSets=").Append(replacement.ExistingCount)
                    .Append(" MatchedReplaceable=").Append(replacement.Matched.Count)
                    .Append(" Protected=").Append(replacement.ProtectedCount)
                    .AppendLine();

                for (int i = 0; i < context.Topologies.Count; i++)
                {
                    Topology t = context.Topologies[i];
                    text.Append("TOPOLOGY ").Append(i + 1)
                        .Append(" view=").Append(t.View.Label)
                        .Append(" drawingMain=P").Append(t.DrawingMain.ModelId)
                        .Append(" connector=P").Append(t.Connector.ModelId)
                        .Append('[').Append(t.Connector.Position).Append(']')
                        .Append(" mainJoinREF=").Append(FormatPoint(t.MainJoinRef))
                        .Append(" nodeREF=").Append(FormatPoint(t.NodeRef))
                        .Append(" arms=").Append(t.Arms.Count)
                        .Append(" lowerPlate=").Append(PartLabel(t.LowerPlate))
                        .Append(" upperPlate=").Append(PartLabel(t.UpperPlate))
                        .AppendLine();
                    for (int a = 0; a < t.Arms.Count; a++)
                    {
                        ArmData arm = t.Arms[a];
                        text.Append("  ARM P").Append(arm.Part.ModelId)
                            .Append('[').Append(arm.Part.Position).Append(']')
                            .Append(" jointREF=").Append(FormatPoint(arm.JointRef))
                            .Append(" outerREF=").Append(FormatPoint(arm.OuterRef))
                            .Append(" outerEDGE=").Append(FormatPoint(arm.OuterNodeLevel))
                            .Append(" bolt=").Append(FormatPoint(arm.TerminalBolt))
                            .AppendLine();
                    }
                }

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append("PLAN ").Append(plan.Name)
                        .Append(" normal=").Append(FormatPoint(plan.PlacementNormal))
                        .Append(" distance=").Append(Format(plan.Distance))
                        .Append(plan.InheritedDistance ? " inherited=yes" : " inherited=no")
                        .Append(" semantic=").Append(plan.Semantic)
                        .AppendLine();
                    for (int p = 0; p < plan.Anchors.Count; p++)
                    {
                        text.Append("  foot[").Append(p).Append("]=")
                            .Append(FormatPoint(plan.Anchors[p].Point))
                            .Append(" <- ").Append(plan.Anchors[p].Semantic)
                            .AppendLine();
                    }
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "SLOT 10 REF CONNECTION DIM PLAN FAILED\r\n" + ex;
            }
        }

        internal static string AuditSideRegression()
        {
            try
            {
                Context context = AnalyzeDrawing();
                List<Topology> originalTopologies =
                    new List<Topology>(context.Topologies);
                StringBuilder text = new StringBuilder();
                text.AppendLine("SLOT 10 ONE-SIDE REGRESSION - READ ONLY");
                text.AppendLine("No drawing/model object is modified.");
                int cases = 0;
                try
                {
                    for (int t = 0; t < originalTopologies.Count; t++)
                    {
                        Topology topology = originalTopologies[t];
                        List<ArmData> originalArms = new List<ArmData>(topology.Arms);
                        for (int a = 0; a < originalArms.Count; a++)
                        {
                            topology.Arms.Clear();
                            topology.Arms.Add(originalArms[a]);
                            context.Topologies.Clear();
                            context.Topologies.Add(topology);
                            List<DimPlan> plans = BuildPlans(context);
                            ValidatePlans(plans);
                            DimPlan chain = null;
                            for (int p = 0; p < plans.Count; p++)
                            {
                                if (plans[p].Name.EndsWith(
                                    "-OVERALL-CHAIN",
                                    StringComparison.Ordinal))
                                {
                                    chain = plans[p];
                                    break;
                                }
                            }
                            bool hasConnectorRef = false;
                            if (chain != null)
                            {
                                for (int p = 0; p < chain.Anchors.Count; p++)
                                {
                                    if (chain.Anchors[p].Semantic.StartsWith(
                                        "REF P" + topology.Connector.ModelId,
                                        StringComparison.Ordinal))
                                        hasConnectorRef = true;
                                }
                            }
                            text.Append("Case=").Append(++cases)
                                .Append(" arm=P").Append(originalArms[a].Part.ModelId)
                                .Append('[').Append(originalArms[a].Part.Position).Append(']')
                                .Append(" planCount=").Append(plans.Count)
                                .Append(" chainFeet=").Append(chain == null ? 0 : chain.Anchors.Count)
                                .Append(" connectorREF=").Append(hasConnectorRef)
                                .Append(" result=")
                                .Append(plans.Count >= 4 && chain != null &&
                                    chain.Anchors.Count == 2 && hasConnectorRef
                                    ? "PASS"
                                    : "FAIL")
                                .AppendLine();
                        }
                        topology.Arms.Clear();
                        topology.Arms.AddRange(originalArms);
                    }
                }
                finally
                {
                    context.Topologies.Clear();
                    context.Topologies.AddRange(originalTopologies);
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "SLOT 10 ONE-SIDE REGRESSION FAILED\r\n" + ex;
            }
        }

        private static Context AnalyzeDrawing()
        {
            return AnalyzeDrawing(true);
        }

        private static Context AnalyzeDrawing(bool requireTopology)
        {
            return AnalyzeDrawing(requireTopology, 0, 0);
        }

        private static Context AnalyzeDrawing(
            bool requireTopology,
            int firstAllowedViewIdentifier,
            int secondAllowedViewIdentifier)
        {
            Context context = new Context();
            context.Model = new TSM.Model();
            TSD.DrawingHandler drawingHandler = new TSD.DrawingHandler();
            if (!context.Model.GetConnectionStatus() || !drawingHandler.GetConnectionStatus())
                throw new InvalidOperationException("Khong ket noi duoc Tekla Model/Drawing API.");

            context.Drawing = drawingHandler.GetActiveDrawing();
            if (context.Drawing == null)
                throw new InvalidOperationException("Khong co ban ve dang mo.");
            context.DrawingMainPart = PHU_MainPartResolver.Resolve(context.Model, context.Drawing);
            if (context.DrawingMainPart == null)
                throw new InvalidOperationException("Khong xac dinh duoc MainPart cua ban ve.");

            context.OriginalPlane = context.Model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            TSG.Matrix currentToGlobal = context.OriginalPlane.TransformationMatrixToGlobal;

            TSD.DrawingObjectEnumerator views = context.Drawing.GetSheet().GetAllViews();
            int index = 0;
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null)
                    continue;
                int viewIdentifier = ReadIdentifierId(view);
                if (
                    firstAllowedViewIdentifier > 0
                    && secondAllowedViewIdentifier > 0
                    && viewIdentifier != firstAllowedViewIdentifier
                    && viewIdentifier != secondAllowedViewIdentifier
                )
                    continue;
                ViewData data = ReadView(
                    context.Model,
                    context.DrawingMainPart,
                    view,
                    currentToGlobal,
                    ++index);
                if (data.DrawingMain == null)
                    continue;
                context.Views.Add(data);
                List<Topology> matches = ResolveTopologies(data);
                for (int m = 0; m < matches.Count; m++)
                    context.Topologies.Add(matches[m]);
                List<PeripheralNeighborTopology> peripheralMatches =
                    ResolvePeripheralNeighborTopologies(data);
                for (int m = 0; m < peripheralMatches.Count; m++)
                    context.PeripheralNeighbors.Add(peripheralMatches[m]);
                ColumnTerminalTopology terminal = ResolveColumnTerminalTopology(data);
                if (terminal != null)
                    context.ColumnTerminals.Add(terminal);
            }

            context.Views.Sort(delegate(ViewData first, ViewData second)
            {
                int byOrigin = first.OriginX.CompareTo(second.OriginX);
                return byOrigin != 0
                    ? byOrigin
                    : first.Identifier.CompareTo(second.Identifier);
            });
            context.Topologies.Sort(delegate(Topology first, Topology second)
            {
                int byView = first.View.Identifier.CompareTo(second.View.Identifier);
                if (byView != 0)
                    return byView;
                int byY = first.NodeRef.Y.CompareTo(second.NodeRef.Y);
                if (byY != 0)
                    return byY;
                int byX = first.NodeRef.X.CompareTo(second.NodeRef.X);
                return byX != 0
                    ? byX
                    : first.Connector.ModelId.CompareTo(second.Connector.ModelId);
            });
            context.PeripheralNeighbors.Sort(
                delegate(PeripheralNeighborTopology first, PeripheralNeighborTopology second)
                {
                    int byView = first.View.Identifier.CompareTo(second.View.Identifier);
                    if (byView != 0)
                        return byView;
                    int byLevel = first.JointReference.Y.CompareTo(second.JointReference.Y);
                    return byLevel != 0
                        ? byLevel
                        : first.Branch.ModelId.CompareTo(second.Branch.ModelId);
                });
            context.ColumnTerminals.Sort(
                delegate(ColumnTerminalTopology first, ColumnTerminalTopology second)
                {
                    return first.View.Identifier.CompareTo(second.View.Identifier);
                });

            if (context.Views.Count > 0)
            {
                double leftOrigin = Double.PositiveInfinity;
                for (int v = 0; v < context.Views.Count; v++)
                    leftOrigin = Math.Min(leftOrigin, context.Views[v].OriginX);
                for (int v = 0; v < context.Views.Count; v++)
                    context.Views[v].IsLeftOnSheet =
                        Math.Abs(context.Views[v].OriginX - leftOrigin) <= PointTolerance;
            }

            if (
                requireTopology
                && context.Topologies.Count == 0
                && context.PeripheralNeighbors.Count == 0
                && context.ColumnTerminals.Count == 0
            )
                throw new InvalidOperationException(
                    "Khong tim thay chuoi REF: MainPart -> dam trung gian -> nhanh lien ket.");
            return context;
        }

        private static ViewData ReadView(
            TSM.Model model,
            TSM.Part drawingMainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal,
            int index)
        {
            ViewData result = new ViewData();
            result.View = view;
            result.Identifier = ReadIdentifierId(view);
            result.Label = "V" + index.ToString("00", CultureInfo.InvariantCulture)
                + "[" + SafeViewName(view) + "]";
            result.Scale = ReadScale(view);
            P2 origin = ReadPointLikeMember(view, "Origin");
            result.OriginX = origin == null ? 0.0 : origin.X;
            if (!IsFinite(result.Scale) || result.Scale <= 0.0)
                throw new InvalidOperationException("Khong doc duoc scale cua " + result.Label + ".");
            result.DimensionAttributes = ReadDimensionAttributes(view);

            TSG.Matrix globalToView =
                TSG.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part drawingPart = parts.Current as TSD.Part;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;
                TSM.Part modelPart =
                    model.SelectModelObject(drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null)
                    continue;
                PartData part = ReadPart(
                    modelPart,
                    drawingMainPart,
                    currentToGlobal,
                    globalToView);
                if (part.Vertices.Count == 0)
                    continue;
                result.Parts.Add(part);
                if (part.IsDrawingMain)
                    result.DrawingMain = part;
            }
            result.Parts.Sort(delegate(PartData first, PartData second)
            {
                return first.ModelId.CompareTo(second.ModelId);
            });
            return result;
        }

        private static bool ContainsBothCapturedViews(
            Context context,
            int leftViewIdentifier,
            int rightViewIdentifier)
        {
            bool hasLeft = false;
            bool hasRight = false;
            for (int i = 0; context != null && i < context.Views.Count; i++)
            {
                ViewData view = context.Views[i];
                if (view.Identifier == leftViewIdentifier)
                    hasLeft = true;
                if (view.Identifier == rightViewIdentifier)
                    hasRight = true;
            }
            return hasLeft && hasRight;
        }

        private static PartData ReadPart(
            TSM.Part modelPart,
            TSM.Part drawingMainPart,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            PartData result = new PartData();
            result.ModelPart = modelPart;
            result.ModelId = modelPart.Identifier == null ? 0 : modelPart.Identifier.ID;
            result.IsDrawingMain = SameIdentifier(modelPart, drawingMainPart);
            result.IsContourPlate = modelPart is TSM.ContourPlate;
            result.Name = SafeUpper(modelPart.Name);
            result.Profile = SafeUpper(
                modelPart.Profile == null ? "" : modelPart.Profile.ProfileString);
            result.Position = ReadPartPosition(modelPart);
            try
            {
                TSM.Assembly assembly = modelPart.GetAssembly();
                result.AssemblyId = assembly == null || assembly.Identifier == null
                    ? 0
                    : assembly.Identifier.ID;
            }
            catch { result.AssemblyId = 0; }

            try
            {
                ArrayList reference = modelPart.GetReferenceLine(false);
                if (reference != null)
                {
                    foreach (object value in reference)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUnique(result.Reference, Transform(point, currentToGlobal, globalToView));
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
                    TSG.Point a3 = TransformToView(
                        edge.StartPoint,
                        currentToGlobal,
                        globalToView);
                    TSG.Point b3 = TransformToView(
                        edge.EndPoint,
                        currentToGlobal,
                        globalToView);
                    P2 a = new P2(a3.X, a3.Y);
                    P2 b = new P2(b3.X, b3.Y);
                    IncludeViewDepth(result, a3.Z);
                    IncludeViewDepth(result, b3.Z);
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
                    BoltData data;
                    if (!result.BoltGroups.TryGetValue(group.Identifier.ID, out data))
                    {
                        data = new BoltData();
                        data.Id = group.Identifier.ID;
                        result.BoltGroups.Add(data.Id, data);
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

        private static List<Topology> ResolveTopologies(ViewData view)
        {
            List<Topology> result = new List<Topology>();
            P2 main0;
            P2 main1;
            if (!TryFarthestPair(view.DrawingMain.Reference, out main0, out main1))
                return result;
            P2 mainAxis = Normalize(Subtract(main1, main0));
            double mainLength = Distance(main0, main1);
            if (mainAxis == null || mainLength <= PointTolerance)
                return result;

            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData connector = view.Parts[i];
                if (connector.IsDrawingMain ||
                    connector.AssemblyId != view.DrawingMain.AssemblyId ||
                    !IsLinearMember(connector))
                    continue;

                P2 c0;
                P2 c1;
                if (!TryFarthestPair(connector.Reference, out c0, out c1))
                    continue;
                P2 connectorDirection = Normalize(Subtract(c1, c0));
                double connectorLength = Distance(c0, c1);
                if (connectorDirection == null || connectorLength <= 10.0 ||
                    connectorLength >= mainLength * 0.40 ||
                    Math.Abs(Dot(connectorDirection, mainAxis)) < DirectionParallel)
                    continue;

                P2 mainJoin;
                P2 node;
                if (!TryAttachToMainEndpoint(main0, main1, c0, c1, out mainJoin, out node))
                    continue;
                P2 connectorAxis = Normalize(Subtract(node, mainJoin));
                if (connectorAxis == null)
                    continue;

                Topology topology = new Topology();
                topology.View = view;
                topology.DrawingMain = view.DrawingMain;
                topology.Connector = connector;
                topology.MainJoinRef = mainJoin;
                topology.NodeRef = node;
                topology.ConnectorAxis = connectorAxis;

                for (int p = 0; p < view.Parts.Count; p++)
                {
                    PartData branch = view.Parts[p];
                    if (branch.ModelId == connector.ModelId || branch.IsDrawingMain ||
                        branch.AssemblyId != view.DrawingMain.AssemblyId ||
                        !IsLinearMember(branch))
                        continue;
                    P2 b0;
                    P2 b1;
                    if (!TryFarthestPair(branch.Reference, out b0, out b1))
                        continue;
                    P2 joint;
                    P2 outer;
                    if (!TryEndpointAtNode(b0, b1, node, out joint, out outer))
                        continue;
                    P2 axis = Normalize(Subtract(outer, joint));
                    if (axis == null || Math.Abs(Dot(axis, connectorAxis)) > DirectionPerpendicular)
                        continue;
                    if (Distance(joint, outer) < Math.Max(100.0, connectorLength * 1.5))
                        continue;
                    ArmData arm = ResolveArm(branch, joint, outer, node, connectorAxis);
                    if (arm != null)
                        topology.Arms.Add(arm);
                }

                if (topology.Arms.Count < 1 || topology.Arms.Count > 2)
                    continue;
                if (topology.Arms.Count == 2 &&
                    Dot(topology.Arms[0].Axis, topology.Arms[1].Axis) > -0.90)
                    continue;

                SortArms(topology.Arms);
                ResolveAdjacentPlates(view, topology);
                if (topology.LowerPlate == null || topology.UpperPlate == null)
                    continue;
                result.Add(topology);
            }

            RemoveDuplicateTopologies(result);
            return result;
        }

        private static List<PeripheralNeighborTopology>
            ResolvePeripheralNeighborTopologies(ViewData view)
        {
            List<PeripheralNeighborTopology> result =
                new List<PeripheralNeighborTopology>();
            P2 main0;
            P2 main1;
            if (
                view == null
                || view.DrawingMain == null
                || !TryFarthestPair(view.DrawingMain.Reference, out main0, out main1)
            )
                return result;
            P2 columnAxis = OrientAxisUp(Normalize(Subtract(main1, main0)));
            if (columnAxis == null)
                return result;
            P2 transverseAxis = new P2(columnAxis.Y, -columnAxis.X);

            for (int b = 0; b < view.Parts.Count; b++)
            {
                PartData branch = view.Parts[b];
                if (
                    branch == null
                    || branch.IsDrawingMain
                    || branch.AssemblyId == view.DrawingMain.AssemblyId
                    || !IsLinearMember(branch)
                )
                    continue;
                P2 branch0;
                P2 branch1;
                if (!TryFarthestPair(branch.Reference, out branch0, out branch1))
                    continue;
                P2 rawBranchAxis = Normalize(Subtract(branch1, branch0));
                if (
                    rawBranchAxis == null
                    || Math.Abs(Dot(rawBranchAxis, columnAxis)) > DirectionPerpendicular
                    || Distance(branch0, branch1) <= 100.0
                )
                    continue;

                PeripheralNeighborTopology best = null;
                double bestJointGap = Double.PositiveInfinity;
                for (int c = 0; c < view.Parts.Count; c++)
                {
                    PartData column = view.Parts[c];
                    if (
                        column == null
                        || column.AssemblyId != view.DrawingMain.AssemblyId
                        || !IsLinearMember(column)
                    )
                        continue;
                    P2 column0;
                    P2 column1;
                    if (!TryFarthestPair(column.Reference, out column0, out column1))
                        continue;
                    P2 candidateColumnAxis = Normalize(Subtract(column1, column0));
                    if (
                        candidateColumnAxis == null
                        || Math.Abs(Dot(candidateColumnAxis, columnAxis)) < DirectionParallel
                    )
                        continue;

                    P2[] endpoints = new P2[] { branch0, branch1 };
                    for (int e = 0; e < endpoints.Length; e++)
                    {
                        P2 joint = endpoints[e];
                        P2 outer = endpoints[1 - e];
                        double level = Dot(Subtract(joint, column0), columnAxis);
                        double columnLength = Dot(Subtract(column1, column0), columnAxis);
                        double levelMin = Math.Min(0.0, columnLength) - RefNodeTolerance;
                        double levelMax = Math.Max(0.0, columnLength) + RefNodeTolerance;
                        if (level < levelMin || level > levelMax)
                            continue;
                        double sideProjection = Dot(Subtract(joint, column0), transverseAxis);
                        int side = sideProjection < 0.0 ? -1 : 1;
                        P2 columnEdge = ResolveSolidEdgeAtAxisLevel(
                            column,
                            column0,
                            columnAxis,
                            transverseAxis,
                            level,
                            side);
                        double jointGap = Distance(joint, columnEdge);
                        if (columnEdge == null || jointGap > RefNodeTolerance)
                            continue;

                        P2 branchAxis = Normalize(Subtract(outer, joint));
                        if (
                            branchAxis == null
                            || Math.Abs(Dot(branchAxis, columnAxis)) > DirectionPerpendicular
                            || Math.Sign(Dot(branchAxis, transverseAxis)) != side
                        )
                            continue;

                        PartData nodePlate;
                        BoltData sharedGroup;
                        if (!TryResolveMainAssemblyNodePlate(
                            view,
                            branch,
                            joint,
                            branchAxis,
                            out nodePlate,
                            out sharedGroup))
                            continue;

                        P2 terminalLow;
                        P2 terminalHigh;
                        P2 terminalAtReference;
                        if (!TryResolveBranchJointFace(
                            branch,
                            joint,
                            branchAxis,
                            columnAxis,
                            out terminalLow,
                            out terminalHigh,
                            out terminalAtReference))
                            continue;
                        P2 bolt = ResolveReferenceSideBolt(
                            sharedGroup,
                            joint,
                            branchAxis,
                            columnAxis,
                            terminalLow,
                            terminalHigh);
                        if (bolt == null)
                            continue;

                        if (
                            best == null
                            || jointGap < bestJointGap - 0.01
                            || (
                                Math.Abs(jointGap - bestJointGap) <= 0.01
                                && column.ModelId < best.Column.ModelId
                            )
                        )
                        {
                            bestJointGap = jointGap;
                            best = new PeripheralNeighborTopology();
                            best.View = view;
                            best.Column = column;
                            best.Branch = branch;
                            best.NodePlate = nodePlate;
                            best.ColumnAxis = columnAxis;
                            best.ColumnReferencePoint = column0;
                            best.JointReference = joint;
                            best.OuterReference = outer;
                            best.BranchAxis = branchAxis;
                            best.TerminalLow = terminalLow;
                            best.TerminalHigh = terminalHigh;
                            best.TerminalAtReference = terminalAtReference;
                            best.ReferenceSideBolt = bolt;
                            best.ColumnEdgeAtLevel = columnEdge;
                            best.Side = side;
                        }
                    }
                }
                if (best != null)
                    result.Add(best);
            }
            return result;
        }

        private static ColumnTerminalTopology ResolveColumnTerminalTopology(ViewData view)
        {
            P2 main0;
            P2 main1;
            if (
                view == null
                || view.DrawingMain == null
                || !TryFarthestPair(view.DrawingMain.Reference, out main0, out main1)
            )
                return null;
            P2 columnAxis = OrientAxisUp(Normalize(Subtract(main1, main0)));
            if (columnAxis == null)
                return null;
            P2 transverseAxis = new P2(columnAxis.Y, -columnAxis.X);

            PartData terminalColumn = null;
            P2 terminalReference = null;
            double bestTerminalProjection = Double.NegativeInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (
                    candidate == null
                    || candidate.AssemblyId != view.DrawingMain.AssemblyId
                    || !IsLinearMember(candidate)
                )
                    continue;
                P2 first;
                P2 second;
                if (!TryFarthestPair(candidate.Reference, out first, out second))
                    continue;
                P2 direction = Normalize(Subtract(second, first));
                if (direction == null || Math.Abs(Dot(direction, columnAxis)) < DirectionParallel)
                    continue;
                double transverseFirst = Math.Abs(Dot(Subtract(first, main0), transverseAxis));
                double transverseSecond = Math.Abs(Dot(Subtract(second, main0), transverseAxis));
                if (Math.Max(transverseFirst, transverseSecond) > RefNodeTolerance)
                    continue;
                double crossSpan = ProjectedSpan(candidate.Vertices, transverseAxis);
                double referenceLength = Distance(first, second);
                if (
                    !IsFinite(crossSpan)
                    || crossSpan <= PointTolerance
                    || referenceLength < Math.Max(100.0, crossSpan * 2.0)
                )
                    continue;
                P2 outer = Dot(first, columnAxis) >= Dot(second, columnAxis) ? first : second;
                double projection = Dot(outer, columnAxis);
                if (
                    terminalColumn == null
                    || projection > bestTerminalProjection + 0.01
                    || (
                        Math.Abs(projection - bestTerminalProjection) <= 0.01
                        && candidate.ModelId < terminalColumn.ModelId
                    )
                )
                {
                    terminalColumn = candidate;
                    terminalReference = outer;
                    bestTerminalProjection = projection;
                }
            }
            if (terminalColumn == null || terminalReference == null)
                return null;

            double terminalFaceProjection = Double.NegativeInfinity;
            for (int i = 0; i < terminalColumn.Vertices.Count; i++)
                terminalFaceProjection = Math.Max(
                    terminalFaceProjection,
                    Dot(terminalColumn.Vertices[i], columnAxis));
            if (!IsFinite(terminalFaceProjection))
                return null;
            P2 left = null;
            P2 right = null;
            double leftProjection = Double.PositiveInfinity;
            double rightProjection = Double.NegativeInfinity;
            for (int i = 0; i < terminalColumn.Vertices.Count; i++)
            {
                P2 point = terminalColumn.Vertices[i];
                if (Math.Abs(Dot(point, columnAxis) - terminalFaceProjection) > 1.0)
                    continue;
                double projection = Dot(point, transverseAxis);
                if (projection < leftProjection) { leftProjection = projection; left = point; }
                if (projection > rightProjection) { rightProjection = projection; right = point; }
            }
            double crossDepth = rightProjection - leftProjection;
            if (left == null || right == null || crossDepth <= PointTolerance)
                return null;

            ColumnTerminalTopology result = new ColumnTerminalTopology();
            result.View = view;
            result.Column = terminalColumn;
            result.ColumnAxis = columnAxis;
            result.TransverseAxis = transverseAxis;
            result.TerminalReference = terminalReference;
            result.TerminalFaceLeft = left;
            result.TerminalFaceRight = right;
            result.TerminalFaceProjection = terminalFaceProjection;
            result.CrossDepth = crossDepth;

            double maximumGap = Math.Max(100.0, crossDepth * 2.0);
            foreach (KeyValuePair<int, BoltData> pair in terminalColumn.BoltGroups)
            {
                BoltData group = pair.Value;
                if (group == null || group.Points.Count == 0)
                    continue;
                double rowProjection = 0.0;
                for (int p = 0; p < group.Points.Count; p++)
                    rowProjection += Dot(group.Points[p], columnAxis);
                rowProjection /= group.Points.Count;
                double gap = terminalFaceProjection - rowProjection;
                if (gap < -RefNodeTolerance || gap > maximumGap)
                    continue;
                TerminalBoltRow row = new TerminalBoltRow();
                row.Group = group;
                row.GapFromTerminal = gap;
                PartData contourOwner = ResolveContourPlateBoltOwner(
                    view,
                    group.Id,
                    terminalColumn.ModelId);
                row.IsContourPlateRow = contourOwner != null;
                row.IsContourPlateFaceVisible =
                    IsContourPlateFaceVisible(
                        contourOwner,
                        transverseAxis,
                        crossDepth);
                for (int p = 0; p < group.Points.Count; p++)
                    AddUnique(row.Points, group.Points[p]);
                row.Points.Sort(delegate(P2 first, P2 second)
                {
                    int byProjection = Dot(first, transverseAxis).CompareTo(
                        Dot(second, transverseAxis));
                    return byProjection != 0 ? byProjection : ComparePoint(first, second);
                });
                result.BoltRows.Add(row);
            }
            result.BoltRows.Sort(delegate(TerminalBoltRow first, TerminalBoltRow second)
            {
                int byGap = first.GapFromTerminal.CompareTo(second.GapFromTerminal);
                return byGap != 0 ? byGap : first.Group.Id.CompareTo(second.Group.Id);
            });
            if (result.BoltRows.Count < 2)
                return null;
            bool hasContourPlateRelation = false;
            for (int r = 0; r < result.BoltRows.Count; r++)
            {
                if (result.BoltRows[r].IsContourPlateRow)
                {
                    hasContourPlateRelation = true;
                    break;
                }
            }
            return hasContourPlateRelation ? result : null;
        }

        private static ArmData ResolveArm(
            PartData part,
            P2 joint,
            P2 outer,
            P2 node,
            P2 connectorAxis)
        {
            P2 axis = Normalize(Subtract(outer, joint));
            if (axis == null || part.Vertices.Count == 0)
                return null;

            double maxS = Double.NegativeInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
                maxS = Math.Max(maxS, Dot(Subtract(part.Vertices[i], node), axis));

            List<P2> terminal = new List<P2>();
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double s = Dot(Subtract(part.Vertices[i], node), axis);
                if (Math.Abs(s - maxS) <= 1.0)
                    AddUnique(terminal, part.Vertices[i]);
            }
            if (terminal.Count < 2)
                return null;

            P2 low = null;
            P2 high = null;
            P2 nodeLevel = null;
            double lowN = Double.PositiveInfinity;
            double highN = Double.NegativeInfinity;
            double bestNodeLevel = Double.PositiveInfinity;
            for (int i = 0; i < terminal.Count; i++)
            {
                double n = Dot(Subtract(terminal[i], node), connectorAxis);
                if (n < lowN) { lowN = n; low = terminal[i]; }
                if (n > highN) { highN = n; high = terminal[i]; }
                if (Math.Abs(n) < bestNodeLevel)
                {
                    bestNodeLevel = Math.Abs(n);
                    nodeLevel = terminal[i];
                }
            }
            if (low == null || high == null || nodeLevel == null || highN - lowN <= 1.0)
                return null;

            P2 bolt = ResolveTerminalBolt(
                part,
                node,
                axis,
                connectorAxis,
                outer,
                lowN,
                highN);
            if (bolt == null)
                return null;

            ArmData result = new ArmData();
            result.Part = part;
            result.JointRef = joint;
            result.OuterRef = outer;
            result.Axis = axis;
            result.OuterLow = low;
            result.OuterHigh = high;
            result.OuterNodeLevel = nodeLevel;
            result.TerminalBolt = bolt;
            result.LowN = lowN;
            result.HighN = highN;
            return result;
        }

        private static P2 ResolveTerminalBolt(
            PartData part,
            P2 node,
            P2 armAxis,
            P2 connectorAxis,
            P2 outerReference,
            double lowN,
            double highN)
        {
            P2 best = null;
            double bestS = Double.NegativeInfinity;
            double bestRefGap = Double.PositiveInfinity;
            int bestGroupId = Int32.MaxValue;
            double outerS = Dot(Subtract(outerReference, node), armAxis);
            double referenceN = Dot(Subtract(outerReference, node), connectorAxis);
            double armDepth = highN - lowN;
            double minimumNormalSpan = Math.Max(10.0, armDepth * 0.15);
            double maximumTerminalGap = Math.Max(150.0, outerS * 0.30);

            foreach (KeyValuePair<int, BoltData> pair in part.BoltGroups)
            {
                BoltData group = pair.Value;
                if (group == null || group.Points.Count < 2)
                    continue;

                double minS = Double.PositiveInfinity;
                double maxS = Double.NegativeInfinity;
                double minN = Double.PositiveInfinity;
                double maxN = Double.NegativeInfinity;
                for (int i = 0; i < group.Points.Count; i++)
                {
                    P2 point = group.Points[i];
                    double s = Dot(Subtract(point, node), armAxis);
                    double n = Dot(Subtract(point, node), connectorAxis);
                    if (s > outerS + RefNodeTolerance)
                        continue;
                    minS = Math.Min(minS, s);
                    maxS = Math.Max(maxS, s);
                    minN = Math.Min(minN, n);
                    maxN = Math.Max(maxN, n);
                }

                if (!IsFinite(maxS) || !IsFinite(minS) || !IsFinite(minN) || !IsFinite(maxN))
                    continue;
                double normalSpan = maxN - minN;
                double axialSpan = maxS - minS;
                double terminalGap = outerS - maxS;
                if (
                    normalSpan < minimumNormalSpan
                    || axialSpan > Math.Max(5.0, normalSpan * 0.75)
                    || terminalGap < -RefNodeTolerance
                    || terminalGap > maximumTerminalGap
                )
                    continue;

                P2 groupPoint = null;
                double groupRefGap = Double.PositiveInfinity;
                for (int i = 0; i < group.Points.Count; i++)
                {
                    P2 point = group.Points[i];
                    double s = Dot(Subtract(point, node), armAxis);
                    if (Math.Abs(s - maxS) > RefNodeTolerance)
                        continue;
                    double n = Dot(Subtract(point, node), connectorAxis);
                    if (n < lowN - RefNodeTolerance || n > highN + RefNodeTolerance)
                        continue;
                    double refGap = Math.Abs(n - referenceN);
                    if (
                        refGap < groupRefGap - 0.01
                        || (
                            Math.Abs(refGap - groupRefGap) <= 0.01
                            && ComparePoint(point, groupPoint) < 0
                        )
                    )
                    {
                        groupRefGap = refGap;
                        groupPoint = point;
                    }
                }

                if (groupPoint == null)
                    continue;
                if (
                    maxS > bestS + 0.01
                    || (
                        Math.Abs(maxS - bestS) <= 0.01
                        && (
                            groupRefGap < bestRefGap - 0.01
                            || (
                                Math.Abs(groupRefGap - bestRefGap) <= 0.01
                                && group.Id < bestGroupId
                            )
                        )
                    )
                )
                {
                    bestS = maxS;
                    bestRefGap = groupRefGap;
                    bestGroupId = group.Id;
                    best = groupPoint;
                }
            }
            return best;
        }

        private static void ResolveAdjacentPlates(ViewData view, Topology topology)
        {
            double lowN = topology.Arms[0].LowN;
            double highN = topology.Arms[0].HighN;
            for (int i = 1; i < topology.Arms.Count; i++)
            {
                if (Math.Abs(topology.Arms[i].LowN - lowN) > 2.0 ||
                    Math.Abs(topology.Arms[i].HighN - highN) > 2.0)
                    return;
            }

            P2 sideAxis = topology.Arms[0].Axis;
            double armDepth = highN - lowN;
            double connectorMinN;
            double connectorMaxN;
            double connectorMinS;
            double connectorMaxS;
            ProjectBounds(
                topology.Connector.Vertices,
                topology.NodeRef,
                topology.ConnectorAxis,
                sideAxis,
                out connectorMinN,
                out connectorMaxN,
                out connectorMinS,
                out connectorMaxS);
            if (
                !IsFinite(connectorMinN)
                || !IsFinite(connectorMaxN)
                || !HasFiniteViewDepth(topology.Connector)
            )
                return;
            double bestLower = Double.PositiveInfinity;
            double bestUpper = Double.PositiveInfinity;

            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.AssemblyId != view.DrawingMain.AssemblyId ||
                    !IsPlateLike(part) || part.Vertices.Count == 0)
                    continue;
                if (ContainsPart(topology, part.ModelId))
                    continue;
                if (!ViewDepthRangesOverlap(
                    part,
                    topology.Connector,
                    DepthOverlapTolerance))
                    continue;

                double minN;
                double maxN;
                double minS;
                double maxS;
                ProjectBounds(part.Vertices, topology.NodeRef, topology.ConnectorAxis,
                    sideAxis, out minN, out maxN, out minS, out maxS);
                if (minS > 1.0 || maxS < -1.0)
                    continue;
                if (maxN - minN > Math.Max(40.0, armDepth * 0.30))
                    continue;

                if (minN - 1.0 <= lowN && maxN + 1.0 >= lowN)
                {
                    if (!ProjectionIntervalContains(
                        minN,
                        maxN,
                        connectorMinN,
                        RefNodeTolerance))
                        continue;
                    double score = Math.Abs(maxN - lowN) + Math.Abs(minN - lowN) * 0.1;
                    if (score < bestLower && minN < lowN - 0.5)
                    {
                        bestLower = score;
                        topology.LowerPlate = part;
                    }
                }
                if (minN - 1.0 <= highN && maxN + 1.0 >= highN)
                {
                    if (!ProjectionIntervalContains(
                        minN,
                        maxN,
                        connectorMaxN,
                        RefNodeTolerance))
                        continue;
                    double score = Math.Abs(minN - highN) + Math.Abs(maxN - highN) * 0.1;
                    if (score < bestUpper && maxN > highN + 0.5)
                    {
                        bestUpper = score;
                        topology.UpperPlate = part;
                    }
                }
            }
        }

        private static List<DimPlan> BuildPlans(Context context)
        {
            List<DimPlan> plans = new List<DimPlan>();
            for (int t = 0; t < context.Topologies.Count; t++)
            {
                Topology topology = context.Topologies[t];
                string prefix = topology.View.Label + "-N" + (t + 1).ToString("00", CultureInfo.InvariantCulture);
                for (int a = 0; a < topology.Arms.Count; a++)
                {
                    ArmData arm = topology.Arms[a];
                    string armPrefix = prefix + "-P" + arm.Part.ModelId;
                    string edgePart = "EDGE P" + arm.Part.ModelId + "[" + arm.Part.Position + "]";
                    string boltPart = "BOLT P" + arm.Part.ModelId + "[" + arm.Part.Position + "]";

                    AddPlan(plans, topology.View, armPrefix + "-DEPTH",
                        "outer profile full depth", arm.Axis, 42.0,
                        new AnchorPoint(arm.OuterLow, edgePart + " outer-low"),
                        new AnchorPoint(arm.OuterHigh, edgePart + " outer-high"));
                    AddPlan(plans, topology.View, armPrefix + "-HALF",
                        "terminal bolt to outer profile edge", arm.Axis, 34.0,
                        new AnchorPoint(arm.TerminalBolt, boltPart + " terminal-row"),
                        new AnchorPoint(arm.OuterHigh, edgePart + " outer-high"));
                    AddPlan(plans, topology.View, armPrefix + "-EDGE-BOLT",
                        "outer edge to nearest terminal bolt", topology.ConnectorAxis, 6.0,
                        new AnchorPoint(arm.OuterNodeLevel, edgePart + " outer-node-level"),
                        new AnchorPoint(arm.TerminalBolt, boltPart + " terminal-row"));

                    if (topology.LowerPlate != null)
                    {
                        P2 anchor = ResolvePlateOuterAnchor(
                            topology.LowerPlate,
                            topology.NodeRef,
                            topology.ConnectorAxis,
                            arm.Axis,
                            false);
                        AddPlan(plans, topology.View, armPrefix + "-PLATE-LOW",
                            "lower plate outside face to arm lower edge", arm.Axis, 22.0,
                            new AnchorPoint(anchor,
                                "PLATE-EDGE P" + topology.LowerPlate.ModelId +
                                "[" + topology.LowerPlate.Position + "] outside-low"),
                            new AnchorPoint(arm.OuterLow, edgePart + " outer-low"));
                    }
                    if (topology.UpperPlate != null)
                    {
                        P2 anchor = ResolvePlateOuterAnchor(
                            topology.UpperPlate,
                            topology.NodeRef,
                            topology.ConnectorAxis,
                            arm.Axis,
                            true);
                        AddPlan(plans, topology.View, armPrefix + "-PLATE-HIGH",
                            "arm upper edge to upper plate outside face", arm.Axis, 22.0,
                            new AnchorPoint(arm.OuterHigh, edgePart + " outer-high"),
                            new AnchorPoint(anchor,
                                "PLATE-EDGE P" + topology.UpperPlate.ModelId +
                                "[" + topology.UpperPlate.Position + "] outside-high"));
                    }
                }

                List<AnchorPoint> chain = new List<AnchorPoint>();
                for (int a = 0; a < topology.Arms.Count; a++)
                {
                    ArmData arm = topology.Arms[a];
                    chain.Add(new AnchorPoint(
                        arm.OuterNodeLevel,
                        "EDGE P" + arm.Part.ModelId + "[" + arm.Part.Position + "] outer-node-level"));
                }
                chain.Add(new AnchorPoint(
                    topology.NodeRef,
                    "REF P" + topology.Connector.ModelId + "[" + topology.Connector.Position + "] node-end"));
                chain.Sort(delegate(AnchorPoint first, AnchorPoint second)
                {
                    int byX = first.Point.X.CompareTo(second.Point.X);
                    return byX != 0 ? byX : first.Point.Y.CompareTo(second.Point.Y);
                });
                AddPlanList(plans, topology.View, prefix + "-OVERALL-CHAIN",
                    "outer link edge -> connector REF -> outer link edge",
                    topology.ConnectorAxis, 20.0, chain);
            }
            return plans;
        }

        private static List<DimPlan> BuildInzaiPlans(Context context)
        {
            double tierBase;
            double tierStep;
            string tierMessage;
            if (
                !PHU_ColumnDimensionTierContext.TryGetShapeSpacing(
                    out tierBase,
                    out tierStep,
                    out tierMessage)
            )
                throw new InvalidOperationException(tierMessage);

            List<DimPlan> plans = new List<DimPlan>();
            for (int t = 0; t < context.Topologies.Count; t++)
            {
                Topology topology = context.Topologies[t];
                if (
                    topology == null
                    || topology.View == null
                    || topology.ConnectorAxis == null
                    || Math.Abs(topology.ConnectorAxis.Y) < DirectionParallel
                    || Math.Abs(topology.ConnectorAxis.X) > DirectionPerpendicular
                )
                    throw new InvalidOperationException(
                        "The Inzai splice connector is not vertical in its captured column view.");

                string prefix =
                    topology.View.Label
                    + "-INZAI-SPLICE-"
                    + (t + 1).ToString("00", CultureInfo.InvariantCulture);
                double edgeProjection =
                    Dot(topology.NodeRef, topology.ConnectorAxis) + tierBase;
                double overallProjection = edgeProjection + tierStep;

                for (int a = 0; a < topology.Arms.Count; a++)
                {
                    ArmData arm = topology.Arms[a];
                    if (
                        arm == null
                        || arm.Axis == null
                        || Math.Abs(arm.Axis.X) < DirectionParallel
                        || Math.Abs(arm.Axis.Y) > DirectionPerpendicular
                    )
                        throw new InvalidOperationException(
                            "A child beam is not horizontal in its captured column view.");

                    int side = arm.Axis.X < 0.0 ? -1 : 1;
                    double terminalProjection = Dot(arm.OuterNodeLevel, arm.Axis);
                    double innerProjection = terminalProjection + tierBase + tierStep;
                    double existingNeighborLine;
                    if (
                        PHU_ColumnDimensionTierContext.TryGetNeighborOutermostVerticalLine(
                            topology.View.Identifier,
                            side,
                            out existingNeighborLine)
                    )
                    {
                        P2 existingLinePoint = new P2(
                            existingNeighborLine,
                            arm.OuterNodeLevel.Y);
                        innerProjection = Math.Max(
                            innerProjection,
                            Dot(existingLinePoint, arm.Axis) + tierStep);
                    }

                    double holeProjection = innerProjection + tierStep;
                    double depthProjection = holeProjection + tierStep;
                    string armPrefix =
                        prefix
                        + "-ARM-"
                        + (a + 1).ToString("00", CultureInfo.InvariantCulture);
                    string child = "child-beam P" + arm.Part.ModelId;

                    DimPlan depth = AddProjectedPlan(
                        plans,
                        topology.View,
                        armPrefix + "-DEPTH",
                        "child-beam terminal low edge -> terminal high edge",
                        arm.Axis,
                        depthProjection,
                        false,
                        new AnchorPoint(arm.OuterLow, child + " terminal-low solid edge"),
                        new AnchorPoint(arm.OuterHigh, child + " terminal-high solid edge"));
                    depth.RegisterVerticalTier = true;

                    AddProjectedPlan(
                        plans,
                        topology.View,
                        armPrefix + "-REF-HOLE",
                        "child-beam REF-side web hole -> child-beam REF-side terminal edge",
                        arm.Axis,
                        holeProjection,
                        false,
                        new AnchorPoint(arm.TerminalBolt, child + " REF-side terminal web-hole"),
                        new AnchorPoint(arm.OuterNodeLevel, child + " REF-side terminal edge"));

                    AddProjectedPlan(
                        plans,
                        topology.View,
                        armPrefix + "-EDGE-HOLE",
                        "child-beam terminal edge -> same child-beam REF-side web hole",
                        topology.ConnectorAxis,
                        edgeProjection,
                        false,
                        new AnchorPoint(arm.OuterNodeLevel, child + " terminal edge"),
                        new AnchorPoint(arm.TerminalBolt, child + " REF-side terminal web-hole"));

                    if (topology.LowerPlate != null)
                    {
                        P2 lower = ResolvePlateOuterAnchor(
                            topology.LowerPlate,
                            topology.NodeRef,
                            topology.ConnectorAxis,
                            arm.Axis,
                            false);
                        AddProjectedPlan(
                            plans,
                            topology.View,
                            armPrefix + "-NODE-LOW",
                            "node contour-plate outside face -> child-beam terminal low edge",
                            arm.Axis,
                            innerProjection,
                            false,
                            new AnchorPoint(lower, "node contour-plate P" + topology.LowerPlate.ModelId + " outside-low"),
                            new AnchorPoint(arm.OuterLow, child + " terminal-low solid edge"));
                    }
                    if (topology.UpperPlate != null)
                    {
                        P2 upper = ResolvePlateOuterAnchor(
                            topology.UpperPlate,
                            topology.NodeRef,
                            topology.ConnectorAxis,
                            arm.Axis,
                            true);
                        AddProjectedPlan(
                            plans,
                            topology.View,
                            armPrefix + "-NODE-HIGH",
                            "child-beam terminal high edge -> node contour-plate outside face",
                            arm.Axis,
                            innerProjection,
                            false,
                            new AnchorPoint(arm.OuterHigh, child + " terminal-high solid edge"),
                            new AnchorPoint(upper, "node contour-plate P" + topology.UpperPlate.ModelId + " outside-high"));
                    }
                }

                List<AnchorPoint> chain = new List<AnchorPoint>();
                for (int a = 0; a < topology.Arms.Count; a++)
                {
                    ArmData arm = topology.Arms[a];
                    chain.Add(new AnchorPoint(
                        arm.OuterNodeLevel,
                        "child-beam P" + arm.Part.ModelId + " terminal edge at REF level"));
                }
                chain.Add(new AnchorPoint(
                    topology.NodeRef,
                    "connector P" + topology.Connector.ModelId + " node REF"));
                chain.Sort(delegate(AnchorPoint first, AnchorPoint second)
                {
                    int byX = first.Point.X.CompareTo(second.Point.X);
                    return byX != 0 ? byX : first.Point.Y.CompareTo(second.Point.Y);
                });
                AddProjectedPlanList(
                    plans,
                    topology.View,
                    prefix + "-REF-OVERALL",
                    "child-beam terminal edge -> connector REF -> child-beam terminal edge",
                    topology.ConnectorAxis,
                    overallProjection,
                    topology.Arms.Count > 1,
                    chain);
            }

            BuildPeripheralNeighborPlans(context, plans, tierBase, tierStep);
            bool type1TerminalFamily = IsType1ColumnTerminalFamily(context);
            bool type2TerminalFamily = IsType2ColumnTerminalFamily(context);
            BuildColumnTerminalPlans(
                context,
                plans,
                tierBase,
                tierStep,
                type1TerminalFamily,
                type1TerminalFamily || type2TerminalFamily,
                type2TerminalFamily);
            return plans;
        }

        private static void BuildPeripheralNeighborPlans(
            Context context,
            List<DimPlan> plans,
            double tierBase,
            double tierStep)
        {
            for (int i = 0; context != null && i < context.PeripheralNeighbors.Count; i++)
            {
                PeripheralNeighborTopology topology = context.PeripheralNeighbors[i];
                if (
                    topology == null
                    || topology.View == null
                    || topology.Branch == null
                    || topology.NodePlate == null
                    || topology.BranchAxis == null
                    || topology.ColumnAxis == null
                )
                    continue;
                string prefix = topology.View.Label
                    + "-INZAI-PERIPHERAL-"
                    + (i + 1).ToString("00", CultureInfo.InvariantCulture);
                string branch = "child-beam P" + topology.Branch.ModelId;
                string column = "column-segment P" + topology.Column.ModelId;

                double nodeExtent = Double.NegativeInfinity;
                for (int p = 0; p < topology.NodePlate.Vertices.Count; p++)
                    nodeExtent = Math.Max(
                        nodeExtent,
                        Dot(topology.NodePlate.Vertices[p], topology.BranchAxis));
                nodeExtent = Math.Max(
                    nodeExtent,
                    Dot(topology.TerminalAtReference, topology.BranchAxis));
                if (!IsFinite(nodeExtent))
                    throw new InvalidOperationException(
                        "The peripheral node plate has no finite projected extent.");

                double halfDepthTier = nodeExtent + tierBase;
                double fullDepthTier = halfDepthTier + tierStep;
                DimPlan fullDepth = AddProjectedPlan(
                    plans,
                    topology.View,
                    prefix + "-CHILD-DEPTH",
                    "child-beam joint-face low edge -> REF-side high edge",
                    topology.BranchAxis,
                    fullDepthTier,
                    false,
                    new AnchorPoint(
                        topology.TerminalLow,
                        branch + " joint-face low solid edge"),
                    new AnchorPoint(
                        topology.TerminalHigh,
                        branch + " joint-face REF-side high edge"));
                fullDepth.RegisterVerticalTier = true;

                AddProjectedPlan(
                    plans,
                    topology.View,
                    prefix + "-REF-HOLE",
                    "child-beam REF -> node-side child-beam hole",
                    topology.BranchAxis,
                    halfDepthTier,
                    false,
                    new AnchorPoint(
                        topology.ReferenceSideBolt,
                        branch + " node-side shared bolt"),
                    new AnchorPoint(
                        topology.TerminalAtReference,
                        branch + " joint REF-side terminal edge"));

                P2 inwardAxis = ResolveColumnInwardAxis(topology);
                double innerHorizontalTier =
                    Dot(topology.JointReference, inwardAxis) + tierBase + tierStep;
                double outerHorizontalTier = innerHorizontalTier + tierStep;
                AddProjectedPlan(
                    plans,
                    topology.View,
                    prefix + "-HOLE-COLUMN-EDGE",
                    "node-side child-beam hole -> column side edge",
                    inwardAxis,
                    innerHorizontalTier,
                    false,
                    new AnchorPoint(
                        topology.ReferenceSideBolt,
                        branch + " node-side shared bolt"),
                    new AnchorPoint(
                        topology.ColumnEdgeAtLevel,
                        column + " side solid edge"));

                P2 columnReference = ProjectPointToInfiniteLine(
                    topology.ReferenceSideBolt,
                    topology.ColumnReferencePoint,
                    topology.ColumnAxis);
                if (columnReference == null)
                    throw new InvalidOperationException(
                        "Could not project the peripheral child-beam hole to the column REF.");
                AddProjectedPlan(
                    plans,
                    topology.View,
                    prefix + "-HOLE-COLUMN-REF",
                    "node-side child-beam hole -> column REF",
                    inwardAxis,
                    outerHorizontalTier,
                    false,
                    new AnchorPoint(
                        topology.ReferenceSideBolt,
                        branch + " node-side shared bolt"),
                    new AnchorPoint(
                        columnReference,
                        column + " infinite REF at child-beam hole level"));
            }
        }

        private static bool IsType1ColumnTerminalFamily(Context context)
        {
            if (context == null || context.ColumnTerminals.Count != 2)
                return false;

            double minimum = Double.PositiveInfinity;
            double maximum = Double.NegativeInfinity;
            HashSet<int> views = new HashSet<int>();
            int columnId = 0;
            for (int i = 0; i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                if (terminal == null
                    || terminal.View == null
                    || terminal.Column == null
                    || !IsFinite(terminal.CrossDepth)
                    || terminal.CrossDepth <= PointTolerance
                    || !views.Add(terminal.View.Identifier))
                    return false;
                if (columnId == 0)
                    columnId = terminal.Column.ModelId;
                else if (columnId != terminal.Column.ModelId)
                    return false;
                minimum = Math.Min(minimum, terminal.CrossDepth);
                maximum = Math.Max(maximum, terminal.CrossDepth);
            }

            // Type 1: the two orthogonal column views expose the same near-square
            // H200 depth (200/200). Type 2 exposes the established rectangular
            // H294 depth pair (294/200), so it never enters this branch.
            return minimum > PointTolerance && maximum / minimum <= 1.10;
        }

        private static bool IsType2ColumnTerminalFamily(Context context)
        {
            if (context == null || context.ColumnTerminals.Count != 2)
                return false;

            double minimum = Double.PositiveInfinity;
            double maximum = Double.NegativeInfinity;
            HashSet<int> views = new HashSet<int>();
            int columnId = 0;
            for (int i = 0; i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                if (terminal == null
                    || terminal.View == null
                    || terminal.Column == null
                    || !IsFinite(terminal.CrossDepth)
                    || terminal.CrossDepth <= PointTolerance
                    || !views.Add(terminal.View.Identifier))
                    return false;
                if (columnId == 0)
                    columnId = terminal.Column.ModelId;
                else if (columnId != terminal.Column.ModelId)
                    return false;
                minimum = Math.Min(minimum, terminal.CrossDepth);
                maximum = Math.Max(maximum, terminal.CrossDepth);
            }

            // Established Type 2 exposes the rectangular H294/H200 depth pair.
            // The bounded ratio keeps other unsupported rectangular families out
            // of this presentation-only replacement branch.
            double ratio = maximum / minimum;
            return minimum > PointTolerance
                && ratio >= 1.35
                && ratio <= 1.60;
        }

        private static void BuildColumnTerminalPlans(
            Context context,
            List<DimPlan> plans,
            double tierBase,
            double tierStep,
            bool type1Family,
            bool replaceRightOverallWithFarHoleChain,
            bool type2Family)
        {
            for (int i = 0; context != null && i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology topology = context.ColumnTerminals[i];
                if (
                    topology == null
                    || topology.View == null
                    || topology.Column == null
                    || topology.ColumnAxis == null
                    || topology.TransverseAxis == null
                    || topology.BoltRows.Count < 2
                )
                    continue;
                string prefix = topology.View.Label
                    + "-INZAI-TERMINAL-"
                    + (i + 1).ToString("00", CultureInfo.InvariantCulture);
                string column = "column-segment P" + topology.Column.ModelId;
                P2 leftNormal = new P2(
                    -topology.TransverseAxis.X,
                    -topology.TransverseAxis.Y);
                double leftFaceProjection = Dot(topology.TerminalFaceLeft, leftNormal);
                bool suppressLeftType1HorizontalPlans =
                    type1Family && topology.View.IsLeftOnSheet;
                int type2HorizontalTierCount = 0;

                for (int r = 0; r < topology.BoltRows.Count; r++)
                {
                    TerminalBoltRow row = topology.BoltRows[r];
                    if (row.IsContourPlateRow && !row.IsContourPlateFaceVisible)
                        continue;
                    P2 representative = SelectMaximumProjectionPoint(
                        row.Points,
                        topology.TransverseAxis);
                    if (representative == null)
                        continue;
                    double verticalTier = leftFaceProjection + tierBase + (r * tierStep);
                    DimPlan vertical = AddProjectedPlan(
                        plans,
                        topology.View,
                        prefix + "-ROW-"
                            + (r + 1).ToString("00", CultureInfo.InvariantCulture)
                            + "-TO-END",
                        "column terminal bolt-row -> terminal solid edge",
                        leftNormal,
                        verticalTier,
                        false,
                        new AnchorPoint(
                            representative,
                            column + " terminal bolt-row G" + row.Group.Id),
                        new AnchorPoint(
                            topology.TerminalFaceLeft,
                            column + " terminal left solid edge"));
                    if (r == topology.BoltRows.Count - 1)
                        vertical.RegisterVerticalTier = true;
                    vertical.ForcePlannedDistance = type1Family;

                    if (suppressLeftType1HorizontalPlans)
                        continue;

                    List<P2> transversePoints = UniqueByProjection(
                        row.Points,
                        topology.TransverseAxis,
                        PointTolerance);
                    if (transversePoints.Count < 2)
                        continue;
                    List<AnchorPoint> chain = new List<AnchorPoint>();
                    chain.Add(new AnchorPoint(
                        topology.TerminalFaceLeft,
                        column + " terminal left solid edge"));
                    for (int p = 0; p < transversePoints.Count; p++)
                    {
                        chain.Add(new AnchorPoint(
                            transversePoints[p],
                            column + " terminal bolt-row G" + row.Group.Id));
                    }
                    chain.Add(new AnchorPoint(
                        topology.TerminalFaceRight,
                        column + " terminal right solid edge"));
                    chain.Sort(delegate(AnchorPoint first, AnchorPoint second)
                    {
                        int byProjection = Dot(first.Point, topology.TransverseAxis).CompareTo(
                            Dot(second.Point, topology.TransverseAxis));
                        return byProjection != 0
                            ? byProjection
                            : ComparePoint(first.Point, second.Point);
                    });
                    bool replaceRightOverall =
                        replaceRightOverallWithFarHoleChain
                        && !topology.View.IsLeftOnSheet
                        && r == topology.BoltRows.Count - 1;
                    P2 horizontalNormal = topology.View.IsLeftOnSheet
                        || replaceRightOverall
                            ? topology.ColumnAxis
                            : new P2(
                                -topology.ColumnAxis.X,
                                -topology.ColumnAxis.Y);
                    double horizontalTier = Dot(
                        topology.TerminalFaceLeft,
                        horizontalNormal)
                        + tierBase
                        + (replaceRightOverall
                            ? tierStep
                            : ((r + 1) * tierStep));
                    bool forceType2HorizontalTier = false;
                    if (type2Family
                        && (topology.View.IsLeftOnSheet || replaceRightOverall))
                    {
                        double type2HoleTier = ResolveType2TerminalTierLine(
                            topology,
                            horizontalNormal,
                            tierBase,
                            tierStep,
                            type2HorizontalTierCount + 1);
                        if (IsFinite(type2HoleTier))
                        {
                            horizontalTier = type2HoleTier;
                            forceType2HorizontalTier = true;
                        }
                    }
                    DimPlan horizontal = AddProjectedPlanList(
                        plans,
                        topology.View,
                        prefix + "-ROW-"
                            + (r + 1).ToString("00", CultureInfo.InvariantCulture)
                            + "-WIDTH-CHAIN",
                        "column terminal edge -> terminal bolt row -> terminal edge",
                        horizontalNormal,
                        horizontalTier,
                        false,
                        chain);
                    horizontal.ForcePlannedDistance = forceType2HorizontalTier;
                    if (forceType2HorizontalTier)
                        type2HorizontalTierCount++;
                }

                if (topology.View.IsLeftOnSheet
                    && !suppressLeftType1HorizontalPlans)
                {
                    List<AnchorPoint> overall = new List<AnchorPoint>();
                    overall.Add(new AnchorPoint(
                        topology.TerminalFaceLeft,
                        column + " terminal left solid edge"));
                    overall.Add(new AnchorPoint(
                        topology.TerminalReference,
                        column + " terminal REF"));
                    overall.Add(new AnchorPoint(
                        topology.TerminalFaceRight,
                        column + " terminal right solid edge"));
                    overall.Sort(delegate(AnchorPoint first, AnchorPoint second)
                    {
                        int byProjection = Dot(first.Point, topology.TransverseAxis).CompareTo(
                            Dot(second.Point, topology.TransverseAxis));
                        return byProjection != 0
                            ? byProjection
                            : ComparePoint(first.Point, second.Point);
                    });
                    double clearance = ResolveColumnTerminalClearanceProjection(topology);
                    double overallTier = clearance + tierBase + tierStep;
                    bool forceType2TerminalAxisLine = false;
                    if (type2Family)
                    {
                        double type2TerminalAxisLine =
                            ResolveType2TerminalTierLine(
                                topology,
                                topology.ColumnAxis,
                                tierBase,
                                tierStep,
                                type2HorizontalTierCount + 1);
                        if (IsFinite(type2TerminalAxisLine))
                        {
                            overallTier = type2TerminalAxisLine;
                            forceType2TerminalAxisLine = true;
                        }
                    }
                    DimPlan overallPlan = AddProjectedPlanList(
                        plans,
                        topology.View,
                        prefix + "-EDGE-REF-EDGE",
                        "column terminal left edge -> terminal REF -> right edge",
                        topology.ColumnAxis,
                        overallTier,
                        false,
                        overall);
                    overallPlan.ForcePlannedDistance =
                        forceType2TerminalAxisLine;
                }
            }
        }

        private static double ResolveType2TerminalTierLine(
            ColumnTerminalTopology topology,
            P2 placementNormal,
            double tierBase,
            double tierStep,
            int tierNumber)
        {
            if (topology == null
                || topology.ColumnAxis == null
                || topology.TerminalReference == null
                || placementNormal == null
                || !IsFinite(tierBase)
                || tierBase <= PointTolerance
                || !IsFinite(tierStep)
                || tierStep <= PointTolerance
                || tierNumber <= 0)
                return Double.NaN;

            return Dot(topology.TerminalReference, placementNormal)
                + tierBase
                + ((tierNumber - 1) * tierStep);
        }

        private static DimPlan AddProjectedPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 placementNormal,
            double targetProjection,
            bool disableCombine,
            params AnchorPoint[] anchors)
        {
            return AddProjectedPlanList(
                plans,
                view,
                name,
                semantic,
                placementNormal,
                targetProjection,
                disableCombine,
                new List<AnchorPoint>(anchors));
        }

        private static DimPlan AddProjectedPlanList(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 placementNormal,
            double targetProjection,
            bool disableCombine,
            List<AnchorPoint> anchors)
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.Semantic = semantic;
            plan.View = view;
            plan.PlacementNormal = Normalize(placementNormal);
            plan.AllowInheritedDistance = false;
            plan.DisableCombine = disableCombine;
            for (int i = 0; i < anchors.Count; i++)
                AddUniqueAnchor(plan.Anchors, anchors[i]);
            if (plan.PlacementNormal == null || plan.Anchors.Count < 2)
                throw new InvalidOperationException("Invalid projected plan " + name + ".");
            plan.Distance =
                targetProjection - Dot(plan.Anchors[0].Point, plan.PlacementNormal);
            if (!IsFinite(plan.Distance) || plan.Distance <= PointTolerance)
                throw new InvalidOperationException(
                    "Projected tier is not outside its geometry for " + name + ".");
            if (Math.Abs(plan.PlacementNormal.X) >= DirectionParallel)
            {
                plan.LineCoordinate =
                    plan.Anchors[0].Point.X
                    + (plan.PlacementNormal.X * plan.Distance);
            }
            plans.Add(plan);
            return plan;
        }

        private static void AddPlan(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 placementNormal,
            double paperDistance,
            params AnchorPoint[] anchors)
        {
            AddPlanList(plans, view, name, semantic, placementNormal,
                paperDistance, new List<AnchorPoint>(anchors));
        }

        private static void AddPlanList(
            List<DimPlan> plans,
            ViewData view,
            string name,
            string semantic,
            P2 placementNormal,
            double paperDistance,
            List<AnchorPoint> anchors)
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.Semantic = semantic;
            plan.View = view;
            plan.PlacementNormal = Normalize(placementNormal);
            plan.Distance = paperDistance * view.Scale;
            for (int i = 0; i < anchors.Count; i++)
                AddUniqueAnchor(plan.Anchors, anchors[i]);
            plans.Add(plan);
        }

        private static void ValidatePlans(List<DimPlan> plans)
        {
            if (plans == null || plans.Count == 0)
                throw new InvalidOperationException("Slot 10 khong tao duoc plan nao.");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (!names.Add(plan.Name))
                    throw new InvalidOperationException("Plan trung ten: " + plan.Name + ".");
                if (plan.View == null || plan.View.View == null ||
                    plan.PlacementNormal == null || plan.Anchors.Count < 2 ||
                    !IsFinite(plan.Distance) || plan.Distance <= 0.0)
                    throw new InvalidOperationException("Plan khong hop le: " + plan.Name + ".");
                double span = 0.0;
                for (int a = 0; a < plan.Anchors.Count; a++)
                {
                    for (int b = a + 1; b < plan.Anchors.Count; b++)
                        span = Math.Max(span, Distance(plan.Anchors[a].Point, plan.Anchors[b].Point));
                }
                if (span <= 0.5)
                    throw new InvalidOperationException("Plan co chan trung nhau: " + plan.Name + ".");
            }
        }

        private static ReplacementSnapshot SnapshotReplaceableDimensions(
            Context context,
            List<DimPlan> plans)
        {
            ReplacementSnapshot snapshot = new ReplacementSnapshot();
            HashSet<int> matchedIds = new HashSet<int>();
            for (int v = 0; v < context.Views.Count; v++)
            {
                ViewData view = context.Views[v];
                TSD.DrawingObjectEnumerator dimensions =
                    view.View.GetAllObjects(typeof(TSD.StraightDimensionSet));
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set = dimensions.Current as TSD.StraightDimensionSet;
                    if (set == null)
                        continue;
                    snapshot.ExistingCount++;
                    List<P2> actual = ReadDimensionPoints(set);
                    P2 actualNormal = Normalize(ReadPointLikeMember(set, "UpDirection"));
                    if (actualNormal == null)
                        actualNormal = Normalize(ReadPointLikeMember(set, "OffsetDirection"));

                    DimPlan match = null;
                    bool reversed = false;
                    for (int p = 0; p < plans.Count; p++)
                    {
                        DimPlan plan = plans[p];
                        if (!Object.ReferenceEquals(plan.View, view) || actualNormal == null ||
                            Dot(actualNormal, plan.PlacementNormal) < 0.95)
                            continue;
                        bool reverse;
                        if (PointChainsMatch(actual, plan.Anchors, PointTolerance, out reverse))
                        {
                            match = plan;
                            reversed = reverse;
                            break;
                        }
                    }
                    if (match == null)
                    {
                        snapshot.ProtectedCount++;
                        continue;
                    }

                    int id = ReadIdentifierId(set);
                    if (id == 0 || matchedIds.Add(id))
                        snapshot.Matched.Add(set);
                    if (!match.InheritedDistance)
                    {
                        double distance = ReadDoubleMember(set, "Distance");
                        if (IsFinite(distance) && distance > 0.0)
                        {
                            match.Distance = distance;
                            match.InheritedDistance = true;
                            if (reversed)
                                match.Anchors.Reverse();
                        }
                    }
                }
            }
            return snapshot;
        }

        private static int MarkExistingPlans(
            Context context,
            List<DimPlan> plans,
            List<DistanceRelocation> relocations)
        {
            int matched = 0;
            HashSet<int> matchedIds = new HashSet<int>();
            for (int v = 0; v < context.Views.Count; v++)
            {
                ViewData view = context.Views[v];
                TSD.DrawingObjectEnumerator dimensions =
                    view.View.GetAllObjects(typeof(TSD.StraightDimensionSet));
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set =
                        dimensions.Current as TSD.StraightDimensionSet;
                    if (set == null)
                        continue;
                    int id = ReadIdentifierId(set);
                    if (id != 0 && matchedIds.Contains(id))
                        continue;

                    List<P2> actual = ReadDimensionPoints(set);
                    P2 actualNormal = Normalize(ReadPointLikeMember(set, "UpDirection"));
                    if (actualNormal == null)
                        actualNormal = Normalize(ReadPointLikeMember(set, "OffsetDirection"));
                    if (actualNormal == null)
                        continue;
                    double actualDistance = ReadDoubleMember(set, "Distance");
                    if (!IsFinite(actualDistance) || actualDistance <= PointTolerance)
                        continue;

                    for (int p = 0; p < plans.Count; p++)
                    {
                        DimPlan plan = plans[p];
                        if (
                            plan.InheritedDistance
                            || !Object.ReferenceEquals(plan.View, view)
                            || Dot(actualNormal, plan.PlacementNormal) < 0.95
                        )
                            continue;
                        bool reversed;
                        if (
                            !ProjectedPointChainsMatch(
                                actual,
                                plan.Anchors,
                                plan.PlacementNormal,
                                PointTolerance,
                                out reversed)
                        )
                            continue;

                        double actualProjection =
                            Dot(actual[0], actualNormal) + actualDistance;

                        if (plan.ForcePlannedDistance)
                        {
                            double targetProjection =
                                Dot(plan.Anchors[0].Point, plan.PlacementNormal)
                                + plan.Distance;
                            double targetDistance =
                                targetProjection - Dot(actual[0], actualNormal);
                            if (!IsFinite(targetProjection)
                                || !IsFinite(targetDistance)
                                || targetDistance <= PointTolerance)
                                continue;

                            plan.InheritedDistance = true;
                            if (Math.Abs(actualProjection - targetProjection)
                                > RefNodeTolerance)
                            {
                                DistanceRelocation relocation =
                                    new DistanceRelocation();
                                relocation.PlanName = plan.Name;
                                relocation.Dimension = set;
                                relocation.ActualFirst = new P2(
                                    actual[0].X,
                                    actual[0].Y);
                                relocation.ActualNormal = new P2(
                                    actualNormal.X,
                                    actualNormal.Y);
                                relocation.OriginalDistance = actualDistance;
                                relocation.TargetDistance = targetDistance;
                                relocation.TargetProjection = targetProjection;
                                if (relocations != null)
                                    relocations.Add(relocation);
                            }
                            if (id != 0)
                                matchedIds.Add(id);
                            matched++;
                            break;
                        }

                        double plannedDistance =
                            actualProjection
                            - Dot(plan.Anchors[0].Point, plan.PlacementNormal);
                        if (!IsFinite(plannedDistance) || plannedDistance <= PointTolerance)
                            continue;
                        plan.Distance = plannedDistance;
                        plan.InheritedDistance = true;
                        if (Math.Abs(plan.PlacementNormal.X) >= DirectionParallel)
                        {
                            plan.LineCoordinate =
                                plan.Anchors[0].Point.X
                                + (plan.PlacementNormal.X * plan.Distance);
                        }
                        if (id != 0)
                            matchedIds.Add(id);
                        matched++;
                        break;
                    }
                }
            }
            return matched;
        }

        private static List<TSD.StraightDimensionSet>
            SnapshotType1ObsoleteLeftTerminalHorizontals(Context context)
        {
            List<TSD.StraightDimensionSet> result =
                new List<TSD.StraightDimensionSet>();
            HashSet<int> identifiers = new HashSet<int>();
            for (int i = 0; context != null && i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                if (terminal == null
                    || terminal.View == null
                    || !terminal.View.IsLeftOnSheet
                    || terminal.ColumnAxis == null)
                    continue;

                List<AnchorPoint> overall = new List<AnchorPoint>();
                overall.Add(new AnchorPoint(
                    terminal.TerminalFaceLeft,
                    "Type-1 obsolete terminal left edge"));
                overall.Add(new AnchorPoint(
                    terminal.TerminalReference,
                    "Type-1 obsolete terminal REF"));
                overall.Add(new AnchorPoint(
                    terminal.TerminalFaceRight,
                    "Type-1 obsolete terminal right edge"));
                SortAnchorsByProjection(overall, terminal.TransverseAxis);
                AddExactDimensionsBySignature(
                    terminal.View,
                    overall,
                    terminal.ColumnAxis,
                    result,
                    identifiers);

                for (int r = 0; r < terminal.BoltRows.Count; r++)
                {
                    TerminalBoltRow row = terminal.BoltRows[r];
                    if (row == null
                        || (row.IsContourPlateRow
                            && !row.IsContourPlateFaceVisible))
                        continue;
                    List<P2> points = UniqueByProjection(
                        row.Points,
                        terminal.TransverseAxis,
                        PointTolerance);
                    if (points.Count < 2)
                        continue;
                    List<AnchorPoint> chain = new List<AnchorPoint>();
                    chain.Add(new AnchorPoint(
                        terminal.TerminalFaceLeft,
                        "Type-1 obsolete terminal left edge"));
                    for (int p = 0; p < points.Count; p++)
                    {
                        chain.Add(new AnchorPoint(
                            points[p],
                            "Type-1 obsolete terminal bolt-row"));
                    }
                    chain.Add(new AnchorPoint(
                        terminal.TerminalFaceRight,
                        "Type-1 obsolete terminal right edge"));
                    SortAnchorsByProjection(chain, terminal.TransverseAxis);
                    AddExactDimensionsBySignature(
                        terminal.View,
                        chain,
                        terminal.ColumnAxis,
                        result,
                        identifiers);
                }
            }
            return result;
        }

        private static List<TSD.StraightDimensionSet>
            SnapshotObsoleteRightTerminalReplacement(Context context)
        {
            List<TSD.StraightDimensionSet> result =
                new List<TSD.StraightDimensionSet>();
            HashSet<int> identifiers = new HashSet<int>();
            for (int i = 0; context != null && i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                if (terminal == null
                    || terminal.View == null
                    || terminal.View.IsLeftOnSheet
                    || terminal.ColumnAxis == null
                    || terminal.TransverseAxis == null)
                    continue;

                List<AnchorPoint> overall = new List<AnchorPoint>();
                overall.Add(new AnchorPoint(
                    terminal.TerminalFaceLeft,
                    "Type-1 obsolete right-view terminal left edge"));
                overall.Add(new AnchorPoint(
                    terminal.TerminalFaceRight,
                    "Type-1 obsolete right-view terminal right edge"));
                SortAnchorsByProjection(overall, terminal.TransverseAxis);
                AddExactDimensionsBySignature(
                    terminal.View,
                    overall,
                    terminal.ColumnAxis,
                    result,
                    identifiers);

                if (terminal.BoltRows.Count == 0)
                    continue;
                TerminalBoltRow farRow =
                    terminal.BoltRows[terminal.BoltRows.Count - 1];
                if (farRow == null
                    || (farRow.IsContourPlateRow
                        && !farRow.IsContourPlateFaceVisible))
                    continue;
                List<P2> points = UniqueByProjection(
                    farRow.Points,
                    terminal.TransverseAxis,
                    PointTolerance);
                if (points.Count < 2)
                    continue;
                List<AnchorPoint> oldChain = new List<AnchorPoint>();
                oldChain.Add(new AnchorPoint(
                    terminal.TerminalFaceLeft,
                    "Type-1 old right-view terminal left edge"));
                for (int p = 0; p < points.Count; p++)
                {
                    oldChain.Add(new AnchorPoint(
                        points[p],
                        "Type-1 old right-view far terminal bolt-row"));
                }
                oldChain.Add(new AnchorPoint(
                    terminal.TerminalFaceRight,
                    "Type-1 old right-view terminal right edge"));
                SortAnchorsByProjection(oldChain, terminal.TransverseAxis);
                AddExactDimensionsBySignature(
                    terminal.View,
                    oldChain,
                    new P2(-terminal.ColumnAxis.X, -terminal.ColumnAxis.Y),
                    result,
                    identifiers);
            }
            return result;
        }

        private static void AppendType1LowerNearEdgeRelocation(
            Context context,
            List<DistanceRelocation> relocations)
        {
            if (context == null || relocations == null)
                return;
            double tierBase;
            double tierStep;
            string tierMessage;
            if (!PHU_ColumnDimensionTierContext.TryGetShapeSpacing(
                out tierBase,
                out tierStep,
                out tierMessage))
                throw new InvalidOperationException(tierMessage);

            for (int i = 0; i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                if (terminal == null
                    || terminal.View == null
                    || !terminal.View.IsLeftOnSheet
                    || terminal.View.DrawingMain == null
                    || terminal.ColumnAxis == null
                    || terminal.TransverseAxis == null)
                    continue;

                P2 baseLeft = SelectSolidFaceExtreme(
                    terminal.View.DrawingMain,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    false,
                    false);
                BoltData bottomGroup = ResolveBottomTerminalBoltGroup(
                    terminal.View.DrawingMain,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    baseLeft);
                P2 representative = bottomGroup == null
                    ? null
                    : SelectMaximumProjectionPoint(
                        bottomGroup.Points,
                        terminal.TransverseAxis);
                if (baseLeft == null || representative == null)
                    continue;

                P2 leftNormal = new P2(
                    -terminal.TransverseAxis.X,
                    -terminal.TransverseAxis.Y);
                List<AnchorPoint> signature = new List<AnchorPoint>();
                signature.Add(new AnchorPoint(
                    baseLeft,
                    "Type-1 lower terminal solid edge"));
                signature.Add(new AnchorPoint(
                    representative,
                    "Type-1 lower near-edge bolt-row"));
                double targetProjection = ResolveType1NearEdgeTierProjection(
                    terminal,
                    tierBase);
                DistanceRelocation found = null;
                TSD.DrawingObjectEnumerator dimensions =
                    terminal.View.View.GetAllObjects(
                        typeof(TSD.StraightDimensionSet));
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set =
                        dimensions.Current as TSD.StraightDimensionSet;
                    if (set == null)
                        continue;
                    P2 actualNormal = Normalize(
                        ReadPointLikeMember(set, "UpDirection"));
                    if (actualNormal == null)
                        actualNormal = Normalize(
                            ReadPointLikeMember(set, "OffsetDirection"));
                    if (actualNormal == null
                        || Dot(actualNormal, leftNormal) < 0.95)
                        continue;
                    List<P2> actual = ReadDimensionPoints(set);
                    bool reversed;
                    if (!ProjectedPointChainsMatch(
                        actual,
                        signature,
                        leftNormal,
                        PointTolerance,
                        out reversed))
                        continue;
                    double actualDistance = ReadDoubleMember(set, "Distance");
                    if (!IsFinite(actualDistance)
                        || actualDistance <= PointTolerance
                        || actual.Count == 0)
                        continue;
                    double targetDistance = targetProjection
                        - Dot(actual[0], actualNormal);
                    if (!IsFinite(targetDistance)
                        || targetDistance <= PointTolerance)
                        continue;
                    if (found != null)
                        throw new InvalidOperationException(
                            "More than one Type-1 lower near-edge hole DIM matches the exact projected signature.");
                    found = new DistanceRelocation();
                    found.PlanName = terminal.View.Label
                        + "-TYPE1-LOWER-NEAR-EDGE-TO-END";
                    found.Dimension = set;
                    found.ActualFirst = new P2(actual[0].X, actual[0].Y);
                    found.ActualNormal = new P2(
                        actualNormal.X,
                        actualNormal.Y);
                    found.OriginalDistance = actualDistance;
                    found.TargetDistance = targetDistance;
                    found.TargetProjection = targetProjection;
                }
                if (found != null)
                {
                    double actualProjection = Dot(
                        found.ActualFirst,
                        found.ActualNormal) + found.OriginalDistance;
                    if (Math.Abs(actualProjection - targetProjection)
                        > RefNodeTolerance)
                        relocations.Add(found);
                }
            }
        }

        private static double ResolveType1NearEdgeTierProjection(
            ColumnTerminalTopology terminal,
            double tierBase)
        {
            if (terminal == null
                || terminal.TransverseAxis == null
                || terminal.TerminalFaceLeft == null
                || !IsFinite(tierBase))
                return Double.NaN;
            P2 leftNormal = new P2(
                -terminal.TransverseAxis.X,
                -terminal.TransverseAxis.Y);
            return Dot(terminal.TerminalFaceLeft, leftNormal) + tierBase;
        }

        private static void SortAnchorsByProjection(
            List<AnchorPoint> anchors,
            P2 axis)
        {
            anchors.Sort(delegate(AnchorPoint first, AnchorPoint second)
            {
                int byProjection = Dot(first.Point, axis).CompareTo(
                    Dot(second.Point, axis));
                return byProjection != 0
                    ? byProjection
                    : ComparePoint(first.Point, second.Point);
            });
        }

        private static void AddExactDimensionsBySignature(
            ViewData view,
            List<AnchorPoint> signature,
            P2 expectedNormal,
            List<TSD.StraightDimensionSet> result,
            HashSet<int> identifiers)
        {
            if (view == null
                || view.View == null
                || signature == null
                || signature.Count < 2
                || expectedNormal == null)
                return;
            TSD.DrawingObjectEnumerator dimensions =
                view.View.GetAllObjects(typeof(TSD.StraightDimensionSet));
            while (dimensions != null && dimensions.MoveNext())
            {
                TSD.StraightDimensionSet set =
                    dimensions.Current as TSD.StraightDimensionSet;
                if (set == null)
                    continue;
                P2 normal = Normalize(ReadPointLikeMember(set, "UpDirection"));
                if (normal == null)
                    normal = Normalize(ReadPointLikeMember(set, "OffsetDirection"));
                if (normal == null || Dot(normal, expectedNormal) < 0.95)
                    continue;
                bool reversed;
                if (!PointChainsMatch(
                    ReadDimensionPoints(set),
                    signature,
                    PointTolerance,
                    out reversed))
                    continue;
                int id = ReadIdentifierId(set);
                if (id == 0 || identifiers.Add(id))
                    result.Add(set);
            }
        }

        private static void ApplyDistanceRelocations(
            List<DistanceRelocation> relocations)
        {
            for (int i = 0; relocations != null && i < relocations.Count; i++)
            {
                DistanceRelocation relocation = relocations[i];
                if (relocation == null
                    || relocation.Dimension == null
                    || relocation.ActualFirst == null
                    || relocation.ActualNormal == null)
                    throw new InvalidOperationException(
                        "A terminal tier relocation is incomplete.");
                relocation.Dimension.Distance = relocation.TargetDistance;
                if (!relocation.Dimension.Modify())
                    throw new InvalidOperationException(
                        "Tekla could not relocate " + relocation.PlanName + ".");
                relocation.Applied = true;
                try { relocation.Dimension.Select(); }
                catch { }
                double actualDistance = ReadDoubleMember(
                    relocation.Dimension,
                    "Distance");
                double actualProjection = Dot(
                    relocation.ActualFirst,
                    relocation.ActualNormal) + actualDistance;
                if (!IsFinite(actualDistance)
                    || Math.Abs(actualProjection - relocation.TargetProjection)
                        > RefNodeTolerance)
                    throw new InvalidOperationException(
                        "Relocated " + relocation.PlanName
                        + " did not keep its Shape tier.");
            }
        }

        private static void RestoreDistanceRelocations(
            List<DistanceRelocation> relocations)
        {
            for (int i = 0; relocations != null && i < relocations.Count; i++)
            {
                DistanceRelocation relocation = relocations[i];
                if (relocation == null
                    || !relocation.Applied
                    || relocation.Dimension == null)
                    continue;
                try
                {
                    relocation.Dimension.Distance = relocation.OriginalDistance;
                    relocation.Dimension.Modify();
                    relocation.Applied = false;
                }
                catch { }
            }
        }

        private static int DeleteExactDimensions(
            List<TSD.StraightDimensionSet> dimensions)
        {
            int deleted = 0;
            for (int i = 0; dimensions != null && i < dimensions.Count; i++)
            {
                TSD.StraightDimensionSet dimension = dimensions[i];
                if (dimension == null || !dimension.Delete())
                    throw new InvalidOperationException(
                        "Tekla could not remove an exact obsolete Type-1 terminal DIM.");
                deleted++;
            }
            return deleted;
        }

        private static void RegisterInzaiVerticalTiers(List<DimPlan> plans)
        {
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || !plan.RegisterVerticalTier
                    || plan.View == null
                    || plan.PlacementNormal == null
                    || !IsFinite(plan.LineCoordinate)
                    || Math.Abs(plan.PlacementNormal.X) < DirectionParallel
                )
                    continue;
                PHU_ColumnDimensionTierContext.RegisterNeighborOutermostVerticalLine(
                    plan.View.Identifier,
                    plan.PlacementNormal.X < 0.0 ? -1 : 1,
                    plan.LineCoordinate);
            }
        }

        private static void PublishCompositeColumnGeometry(Context context)
        {
            PHU_InzaiColumnCompositeDimensionContext.Reset();
            bool type2TerminalFamily = IsType2ColumnTerminalFamily(context);
            double tierBase = Double.NaN;
            double tierStep = Double.NaN;
            string tierMessage;
            if (type2TerminalFamily)
            {
                PHU_ColumnDimensionTierContext.TryGetShapeSpacing(
                    out tierBase,
                    out tierStep,
                    out tierMessage);
            }
            for (int i = 0; context != null && i < context.ColumnTerminals.Count; i++)
            {
                ColumnTerminalTopology terminal = context.ColumnTerminals[i];
                Topology splice = null;
                for (int t = 0; t < context.Topologies.Count; t++)
                {
                    if (Object.ReferenceEquals(context.Topologies[t].View, terminal.View))
                    {
                        splice = context.Topologies[t];
                        break;
                    }
                }
                if (
                    splice == null
                    || splice.LowerPlate == null
                    || splice.UpperPlate == null
                    || terminal.View.DrawingMain == null
                )
                    continue;

                P2 baseLeft = SelectSolidFaceExtreme(
                    terminal.View.DrawingMain,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    false,
                    false);
                P2 baseRight = SelectSolidFaceExtreme(
                    terminal.View.DrawingMain,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    false,
                    true);
                P2 spliceLow = SelectSolidFaceExtreme(
                    splice.LowerPlate,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    false,
                    false);
                P2 spliceHigh = SelectSolidFaceExtreme(
                    splice.UpperPlate,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    true,
                    false);
                if (baseLeft == null || baseRight == null || spliceLow == null || spliceHigh == null)
                    continue;

                PHU_InzaiCompositeViewGeometry geometry =
                    new PHU_InzaiCompositeViewGeometry();
                geometry.ViewIdentifier = terminal.View.Identifier;
                geometry.RefX = terminal.TerminalReference.X;
                geometry.BaseY = baseLeft.Y;
                geometry.BaseLeftX = baseLeft.X;
                geometry.BaseRightX = baseRight.X;
                geometry.SpliceLowY = spliceLow.Y;
                geometry.SpliceLowLeftX = spliceLow.X;
                geometry.SpliceHighY = spliceHigh.Y;
                geometry.SpliceHighLeftX = spliceHigh.X;
                geometry.TerminalY = terminal.TerminalFaceLeft.Y;
                geometry.TerminalLeftX = terminal.TerminalFaceLeft.X;
                geometry.TerminalRightX = terminal.TerminalFaceRight.X;
                geometry.TerminalRefY = terminal.TerminalReference.Y;
                geometry.HorizontalClearanceY = ResolveColumnTerminalClearanceProjection(terminal);
                geometry.Type2TerminalFamily = type2TerminalFamily;
                if (type2TerminalFamily)
                {
                    double type2TerminalAxisLine =
                        ResolveType2TerminalTierLine(
                            terminal,
                            terminal.ColumnAxis,
                            tierBase,
                            tierStep,
                            2);
                    if (IsFinite(type2TerminalAxisLine))
                        geometry.Type2TerminalAxisLine = type2TerminalAxisLine;
                }

                BoltData bottomGroup = ResolveBottomTerminalBoltGroup(
                    terminal.View.DrawingMain,
                    terminal.ColumnAxis,
                    terminal.TransverseAxis,
                    baseLeft);
                for (int p = 0; bottomGroup != null && p < bottomGroup.Points.Count; p++)
                {
                    geometry.BottomBoltPoints.Add(
                        new TSG.Point(
                            bottomGroup.Points[p].X,
                            bottomGroup.Points[p].Y,
                            0.0));
                }
                PHU_InzaiColumnCompositeDimensionContext.Publish(geometry);
            }
        }

        private static P2 SelectSolidFaceExtreme(
            PartData part,
            P2 axis,
            P2 transverseAxis,
            bool maximumAxis,
            bool maximumTransverse)
        {
            if (part == null || part.Vertices.Count == 0)
                return null;
            double face = maximumAxis
                ? Double.NegativeInfinity
                : Double.PositiveInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                double projection = Dot(part.Vertices[i], axis);
                face = maximumAxis ? Math.Max(face, projection) : Math.Min(face, projection);
            }
            P2 best = null;
            double transverse = maximumTransverse
                ? Double.NegativeInfinity
                : Double.PositiveInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                P2 point = part.Vertices[i];
                if (Math.Abs(Dot(point, axis) - face) > 1.0)
                    continue;
                double projection = Dot(point, transverseAxis);
                if (
                    best == null
                    || (maximumTransverse && projection > transverse + 0.01)
                    || (!maximumTransverse && projection < transverse - 0.01)
                )
                {
                    best = point;
                    transverse = projection;
                }
            }
            return best;
        }

        private static BoltData ResolveBottomTerminalBoltGroup(
            PartData column,
            P2 axis,
            P2 transverseAxis,
            P2 basePoint)
        {
            double crossDepth = ProjectedSpan(column.Vertices, transverseAxis);
            double maximumGap = Math.Max(100.0, crossDepth * 2.0);
            double baseProjection = Dot(basePoint, axis);
            BoltData best = null;
            double bestGap = Double.PositiveInfinity;
            foreach (KeyValuePair<int, BoltData> pair in column.BoltGroups)
            {
                BoltData group = pair.Value;
                if (group == null || group.Points.Count < 2)
                    continue;
                double average = 0.0;
                for (int i = 0; i < group.Points.Count; i++)
                    average += Dot(group.Points[i], axis);
                average /= group.Points.Count;
                double gap = average - baseProjection;
                if (gap < -RefNodeTolerance || gap > maximumGap)
                    continue;
                if (
                    best == null
                    || gap < bestGap - 0.01
                    || (
                        Math.Abs(gap - bestGap) <= 0.01
                        && group.Id < best.Id
                    )
                )
                {
                    best = group;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static void VerifyCreatedPlans(
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> created)
        {
            if (plans.Count != created.Count)
                throw new InvalidOperationException(
                    "Created dimension count does not match the validated plan count.");
            for (int i = 0; i < plans.Count; i++)
            {
                try { created[i].Select(); }
                catch { }
                List<P2> actual = ReadDimensionPoints(created[i]);
                bool reversed;
                if (!PointChainsMatch(actual, plans[i].Anchors, 2.0, out reversed))
                    throw new InvalidOperationException(
                        "Read-back feet do not match " + plans[i].Name + ".");
                if (plans[i].DisableCombine && actual.Count > 2)
                {
                    TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                        created[i].Attributes;
                    if (
                        attributes == null
                        || attributes.CombinedDimension == null
                        || attributes.CombinedDimension.Format
                            != TSD.DimensionSetBaseAttributes.CombineFormats.Off
                    )
                        throw new InvalidOperationException(
                            "CombinedDimension read-back failed for " + plans[i].Name + ".");
                }
            }
        }

        private static void DisableCombine(
            TSD.StraightDimensionSet dimension,
            int pointCount,
            string name)
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            if (attributes == null)
                throw new InvalidOperationException(
                    "Created dimension has no attributes for " + name + ".");
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension
                ?? new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format = TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            combined.MinimumNumberToCombine = Math.Max(5, pointCount);
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;
            if (!dimension.Modify())
                throw new InvalidOperationException(
                    "Could not disable combine for " + name + ".");
        }

        private static bool PointChainsMatch(
            List<P2> actual,
            List<AnchorPoint> expected,
            double tolerance,
            out bool reversed)
        {
            reversed = false;
            if (actual == null || expected == null || actual.Count != expected.Count)
                return false;
            bool forward = true;
            bool reverse = true;
            for (int i = 0; i < actual.Count; i++)
            {
                if (Distance(actual[i], expected[i].Point) > tolerance)
                    forward = false;
                if (Distance(actual[i], expected[expected.Count - 1 - i].Point) > tolerance)
                    reverse = false;
            }
            reversed = !forward && reverse;
            return forward || reverse;
        }

        private static bool ProjectedPointChainsMatch(
            List<P2> actual,
            List<AnchorPoint> expected,
            P2 placementNormal,
            double tolerance,
            out bool reversed)
        {
            reversed = false;
            if (
                actual == null
                || expected == null
                || placementNormal == null
                || actual.Count != expected.Count
            )
                return false;
            P2 measurementAxis = Normalize(
                new P2(-placementNormal.Y, placementNormal.X));
            if (measurementAxis == null)
                return false;

            bool forward = true;
            bool reverse = true;
            for (int i = 0; i < actual.Count; i++)
            {
                double actualProjection = Dot(actual[i], measurementAxis);
                double forwardProjection = Dot(expected[i].Point, measurementAxis);
                double reverseProjection = Dot(
                    expected[expected.Count - 1 - i].Point,
                    measurementAxis);
                if (Math.Abs(actualProjection - forwardProjection) > tolerance)
                    forward = false;
                if (Math.Abs(actualProjection - reverseProjection) > tolerance)
                    reverse = false;
            }
            reversed = !forward && reverse;
            return forward || reverse;
        }

        private static List<P2> ReadDimensionPoints(object dimension)
        {
            List<P2> result = new List<P2>();
            try
            {
                PropertyInfo property = dimension.GetType().GetProperty(
                    "DimensionPoints",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                IEnumerable values = property == null
                    ? null
                    : property.GetValue(dimension, null) as IEnumerable;
                if (values != null)
                {
                    foreach (object value in values)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            result.Add(new P2(point.X, point.Y));
                    }
                }
            }
            catch { }
            return result;
        }

        private static P2 ReadPointLikeMember(object value, string name)
        {
            try
            {
                PropertyInfo property = value.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object member = property == null ? null : property.GetValue(value, null);
                TSG.Point point = member as TSG.Point;
                if (point != null)
                    return new P2(point.X, point.Y);
                TSG.Vector vector = member as TSG.Vector;
                return vector == null ? null : new P2(vector.X, vector.Y);
            }
            catch { return null; }
        }

        private static double ReadDoubleMember(object value, string name)
        {
            try
            {
                PropertyInfo property = value.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object raw = property == null ? null : property.GetValue(value, null);
                if (raw != null)
                    return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                FieldInfo field = value.GetType().GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                raw = field == null ? null : field.GetValue(value);
                return raw == null
                    ? Double.NaN
                    : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch { return Double.NaN; }
        }

        private static int ReadIdentifierId(TSD.DrawingObject value)
        {
            try
            {
                Tekla.Structures.Identifier identifier =
                    GetMember(value, "Identifier") as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch { return 0; }
        }

        private static P2 ResolvePlateOuterAnchor(
            PartData plate,
            P2 node,
            P2 connectorAxis,
            P2 sideAxis,
            bool upper)
        {
            double targetN = upper ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; i < plate.Vertices.Count; i++)
            {
                double n = Dot(Subtract(plate.Vertices[i], node), connectorAxis);
                targetN = upper ? Math.Max(targetN, n) : Math.Min(targetN, n);
            }
            P2 best = null;
            double bestSide = Double.NegativeInfinity;
            for (int i = 0; i < plate.Vertices.Count; i++)
            {
                P2 point = plate.Vertices[i];
                double n = Dot(Subtract(point, node), connectorAxis);
                if (Math.Abs(n - targetN) > 1.0)
                    continue;
                double side = Dot(Subtract(point, node), sideAxis);
                if (side > bestSide)
                {
                    bestSide = side;
                    best = point;
                }
            }
            if (best == null)
                throw new InvalidOperationException("Khong resolve duoc outside face cua plate P" + plate.ModelId + ".");
            return best;
        }

        private static bool IsLinearMember(PartData part)
        {
            if (part == null || part.Reference.Count < 2)
                return false;
            if (IsPlateLike(part))
                return false;
            P2 first;
            P2 second;
            return TryFarthestPair(part.Reference, out first, out second) &&
                Distance(first, second) > PointTolerance;
        }

        private static bool IsPlateLike(PartData part)
        {
            // Node-face feet may only be owned by an actual contour plate.
            // Beam-modelled splice plates can collapse onto the node in a thin
            // projection, so profile/name text is deliberately not evidence.
            return part != null && part.IsContourPlate;
        }

        private static P2 OrientAxisUp(P2 axis)
        {
            if (axis == null)
                return null;
            if (axis.Y < -DirectionPerpendicular
                || (Math.Abs(axis.Y) <= DirectionPerpendicular && axis.X < 0.0))
                return new P2(-axis.X, -axis.Y);
            return axis;
        }

        private static double ProjectedSpan(List<P2> points, P2 axis)
        {
            if (points == null || points.Count == 0 || axis == null)
                return Double.NaN;
            double minimum = Double.PositiveInfinity;
            double maximum = Double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double projection = Dot(points[i], axis);
                minimum = Math.Min(minimum, projection);
                maximum = Math.Max(maximum, projection);
            }
            return maximum - minimum;
        }

        private static bool IsContourPlateFaceVisible(
            PartData plate,
            P2 transverseAxis,
            double columnCrossDepth)
        {
            double span = plate == null
                ? Double.NaN
                : ProjectedSpan(plate.Vertices, transverseAxis);
            return IsFinite(span)
                && IsFinite(columnCrossDepth)
                && columnCrossDepth > PointTolerance
                && span >= columnCrossDepth * 0.25;
        }

        private static P2 ResolveSolidEdgeAtAxisLevel(
            PartData part,
            P2 origin,
            P2 axis,
            P2 transverseAxis,
            double level,
            int side)
        {
            P2 best = null;
            double bestProjection = side < 0
                ? Double.PositiveInfinity
                : Double.NegativeInfinity;
            for (int i = 0; part != null && i < part.Segments.Count; i++)
            {
                Segment2 segment = part.Segments[i];
                if (segment == null || segment.A == null || segment.B == null)
                    continue;
                double firstLevel = Dot(Subtract(segment.A, origin), axis);
                double secondLevel = Dot(Subtract(segment.B, origin), axis);
                double low = Math.Min(firstLevel, secondLevel) - RefNodeTolerance;
                double high = Math.Max(firstLevel, secondLevel) + RefNodeTolerance;
                if (level < low || level > high)
                    continue;
                double denominator = secondLevel - firstLevel;
                double ratio = Math.Abs(denominator) <= 0.000001
                    ? 0.0
                    : (level - firstLevel) / denominator;
                ratio = Math.Max(0.0, Math.Min(1.0, ratio));
                P2 point = new P2(
                    segment.A.X + ((segment.B.X - segment.A.X) * ratio),
                    segment.A.Y + ((segment.B.Y - segment.A.Y) * ratio));
                double projection = Dot(Subtract(point, origin), transverseAxis);
                if (
                    best == null
                    || (side < 0 && projection < bestProjection - 0.01)
                    || (side > 0 && projection > bestProjection + 0.01)
                    || (
                        Math.Abs(projection - bestProjection) <= 0.01
                        && ComparePoint(point, best) < 0
                    )
                )
                {
                    best = point;
                    bestProjection = projection;
                }
            }
            return best;
        }

        private static bool TryResolveMainAssemblyNodePlate(
            ViewData view,
            PartData branch,
            P2 joint,
            P2 branchAxis,
            out PartData nodePlate,
            out BoltData sharedGroup)
        {
            nodePlate = null;
            sharedGroup = null;
            double branchLength = 0.0;
            P2 branch0;
            P2 branch1;
            if (TryFarthestPair(branch.Reference, out branch0, out branch1))
                branchLength = Distance(branch0, branch1);
            double maximumJointDistance = Math.Max(200.0, branchLength * 0.35);
            double bestDistance = Double.PositiveInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData plate = view.Parts[i];
                if (
                    plate == null
                    || !plate.IsContourPlate
                    || plate.AssemblyId != view.DrawingMain.AssemblyId
                )
                    continue;
                foreach (KeyValuePair<int, BoltData> pair in branch.BoltGroups)
                {
                    BoltData plateGroup;
                    if (!plate.BoltGroups.TryGetValue(pair.Key, out plateGroup))
                        continue;
                    BoltData group = pair.Value;
                    double nearest = Double.PositiveInfinity;
                    for (int p = 0; group != null && p < group.Points.Count; p++)
                    {
                        double projection = Dot(Subtract(group.Points[p], joint), branchAxis);
                        if (projection >= -RefNodeTolerance)
                            nearest = Math.Min(nearest, projection);
                    }
                    if (!IsFinite(nearest) || nearest > maximumJointDistance)
                        continue;
                    if (
                        nodePlate == null
                        || nearest < bestDistance - 0.01
                        || (
                            Math.Abs(nearest - bestDistance) <= 0.01
                            && plate.ModelId < nodePlate.ModelId
                        )
                    )
                    {
                        bestDistance = nearest;
                        nodePlate = plate;
                        sharedGroup = group;
                    }
                }
            }
            return nodePlate != null && sharedGroup != null;
        }

        private static bool TryResolveBranchJointFace(
            PartData branch,
            P2 joint,
            P2 branchAxis,
            P2 columnAxis,
            out P2 terminalLow,
            out P2 terminalHigh,
            out P2 terminalAtReference)
        {
            terminalLow = null;
            terminalHigh = null;
            terminalAtReference = null;
            double minimumS = Double.PositiveInfinity;
            for (int i = 0; branch != null && i < branch.Vertices.Count; i++)
            {
                double projection = Dot(Subtract(branch.Vertices[i], joint), branchAxis);
                if (projection >= -RefNodeTolerance)
                    minimumS = Math.Min(minimumS, projection);
            }
            if (!IsFinite(minimumS))
                return false;

            double low = Double.PositiveInfinity;
            double high = Double.NegativeInfinity;
            double nearestReference = Double.PositiveInfinity;
            for (int i = 0; i < branch.Vertices.Count; i++)
            {
                P2 point = branch.Vertices[i];
                double s = Dot(Subtract(point, joint), branchAxis);
                if (Math.Abs(s - minimumS) > 1.0)
                    continue;
                double n = Dot(Subtract(point, joint), columnAxis);
                if (n < low) { low = n; terminalLow = point; }
                if (n > high) { high = n; terminalHigh = point; }
                if (Math.Abs(n) < nearestReference)
                {
                    nearestReference = Math.Abs(n);
                    terminalAtReference = point;
                }
            }
            return terminalLow != null
                && terminalHigh != null
                && terminalAtReference != null
                && high - low > PointTolerance;
        }

        private static P2 ResolveReferenceSideBolt(
            BoltData group,
            P2 joint,
            P2 branchAxis,
            P2 columnAxis,
            P2 terminalLow,
            P2 terminalHigh)
        {
            if (group == null || joint == null)
                return null;
            double low = Dot(Subtract(terminalLow, joint), columnAxis) - RefNodeTolerance;
            double high = Dot(Subtract(terminalHigh, joint), columnAxis) + RefNodeTolerance;
            P2 best = null;
            double bestS = Double.PositiveInfinity;
            double bestReferenceGap = Double.PositiveInfinity;
            for (int i = 0; i < group.Points.Count; i++)
            {
                P2 point = group.Points[i];
                double s = Dot(Subtract(point, joint), branchAxis);
                double n = Dot(Subtract(point, joint), columnAxis);
                if (s < -RefNodeTolerance || n < low || n > high)
                    continue;
                double referenceGap = Math.Abs(n);
                if (
                    best == null
                    || s < bestS - 0.01
                    || (
                        Math.Abs(s - bestS) <= 0.01
                        && (
                            referenceGap < bestReferenceGap - 0.01
                            || (
                                Math.Abs(referenceGap - bestReferenceGap) <= 0.01
                                && ComparePoint(point, best) < 0
                            )
                        )
                    )
                )
                {
                    best = point;
                    bestS = s;
                    bestReferenceGap = referenceGap;
                }
            }
            return best;
        }

        private static PartData ResolveContourPlateBoltOwner(
            ViewData view,
            int boltGroupId,
            int excludedPartId)
        {
            for (int i = 0; view != null && i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (
                    part != null
                    && part.ModelId != excludedPartId
                    && part.IsContourPlate
                    && part.BoltGroups.ContainsKey(boltGroupId)
                )
                    return part;
            }
            return null;
        }

        private static P2 ResolveColumnInwardAxis(PeripheralNeighborTopology topology)
        {
            P2 first;
            P2 second;
            if (
                topology == null
                || topology.Column == null
                || !TryFarthestPair(topology.Column.Reference, out first, out second)
            )
                return topology == null ? null : topology.ColumnAxis;
            double center = 0.5 * (
                Dot(first, topology.ColumnAxis)
                + Dot(second, topology.ColumnAxis));
            double level = Dot(topology.JointReference, topology.ColumnAxis);
            return level <= center
                ? topology.ColumnAxis
                : new P2(-topology.ColumnAxis.X, -topology.ColumnAxis.Y);
        }

        private static P2 ProjectPointToInfiniteLine(P2 point, P2 origin, P2 axis)
        {
            P2 normalized = Normalize(axis);
            if (point == null || origin == null || normalized == null)
                return null;
            double projection = Dot(Subtract(point, origin), normalized);
            return new P2(
                origin.X + (normalized.X * projection),
                origin.Y + (normalized.Y * projection));
        }

        private static P2 SelectMaximumProjectionPoint(List<P2> points, P2 axis)
        {
            P2 best = null;
            double bestProjection = Double.NegativeInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                P2 point = points[i];
                double projection = Dot(point, axis);
                if (
                    best == null
                    || projection > bestProjection + 0.01
                    || (
                        Math.Abs(projection - bestProjection) <= 0.01
                        && ComparePoint(point, best) < 0
                    )
                )
                {
                    best = point;
                    bestProjection = projection;
                }
            }
            return best;
        }

        private static List<P2> UniqueByProjection(
            List<P2> points,
            P2 axis,
            double tolerance)
        {
            List<P2> sorted = new List<P2>();
            for (int i = 0; points != null && i < points.Count; i++)
                sorted.Add(points[i]);
            sorted.Sort(delegate(P2 first, P2 second)
            {
                int byProjection = Dot(first, axis).CompareTo(Dot(second, axis));
                return byProjection != 0 ? byProjection : ComparePoint(first, second);
            });
            List<P2> result = new List<P2>();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (
                    result.Count == 0
                    || Math.Abs(
                        Dot(sorted[i], axis)
                        - Dot(result[result.Count - 1], axis)) > tolerance
                )
                    result.Add(sorted[i]);
            }
            return result;
        }

        private static double ResolveColumnTerminalClearanceProjection(
            ColumnTerminalTopology topology)
        {
            double left = Dot(topology.TerminalFaceLeft, topology.TransverseAxis);
            double right = Dot(topology.TerminalFaceRight, topology.TransverseAxis);
            if (left > right)
            {
                double swap = left;
                left = right;
                right = swap;
            }
            double clearance = topology.TerminalFaceProjection;
            double terminalReferenceProjection = Dot(
                topology.TerminalReference,
                topology.ColumnAxis);
            for (int i = 0; i < topology.View.Parts.Count; i++)
            {
                PartData part = topology.View.Parts[i];
                if (part == null || part.Vertices.Count == 0)
                    continue;
                double partLeft = Double.PositiveInfinity;
                double partRight = Double.NegativeInfinity;
                double partBottom = Double.PositiveInfinity;
                double partTop = Double.NegativeInfinity;
                for (int p = 0; p < part.Vertices.Count; p++)
                {
                    partLeft = Math.Min(
                        partLeft,
                        Dot(part.Vertices[p], topology.TransverseAxis));
                    partRight = Math.Max(
                        partRight,
                        Dot(part.Vertices[p], topology.TransverseAxis));
                    partBottom = Math.Min(
                        partBottom,
                        Dot(part.Vertices[p], topology.ColumnAxis));
                    partTop = Math.Max(
                        partTop,
                        Dot(part.Vertices[p], topology.ColumnAxis));
                }
                if (partRight < left - RefNodeTolerance || partLeft > right + RefNodeTolerance)
                    continue;
                // Only geometry starting at the terminal node may extend the
                // clearance. A separate upper column happens to overlap the
                // same X corridor but must not push this DIM to another storey.
                if (partBottom > terminalReferenceProjection + RefNodeTolerance)
                    continue;
                if (partTop >= topology.TerminalFaceProjection - RefNodeTolerance)
                    clearance = Math.Max(clearance, partTop);
            }
            return clearance;
        }

        private static bool ContainsPart(Topology topology, int modelId)
        {
            if (topology.DrawingMain.ModelId == modelId || topology.Connector.ModelId == modelId)
                return true;
            for (int i = 0; i < topology.Arms.Count; i++)
            {
                if (topology.Arms[i].Part.ModelId == modelId)
                    return true;
            }
            return false;
        }

        private static bool TryAttachToMainEndpoint(
            P2 main0,
            P2 main1,
            P2 candidate0,
            P2 candidate1,
            out P2 mainJoin,
            out P2 node)
        {
            mainJoin = null;
            node = null;
            double d00 = Distance(main0, candidate0);
            double d01 = Distance(main0, candidate1);
            double d10 = Distance(main1, candidate0);
            double d11 = Distance(main1, candidate1);
            double best = Math.Min(Math.Min(d00, d01), Math.Min(d10, d11));
            if (best > RefNodeTolerance)
                return false;
            if (best == d00) { mainJoin = candidate0; node = candidate1; }
            else if (best == d01) { mainJoin = candidate1; node = candidate0; }
            else if (best == d10) { mainJoin = candidate0; node = candidate1; }
            else { mainJoin = candidate1; node = candidate0; }
            return true;
        }

        private static bool TryEndpointAtNode(
            P2 first,
            P2 second,
            P2 node,
            out P2 joint,
            out P2 outer)
        {
            joint = null;
            outer = null;
            double firstDistance = Distance(first, node);
            double secondDistance = Distance(second, node);
            if (Math.Min(firstDistance, secondDistance) > RefNodeTolerance)
                return false;
            if (firstDistance <= secondDistance) { joint = first; outer = second; }
            else { joint = second; outer = first; }
            return true;
        }

        private static void SortArms(List<ArmData> arms)
        {
            if (arms.Count < 2)
                return;
            arms.Sort(delegate(ArmData first, ArmData second)
            {
                int x = first.OuterNodeLevel.X.CompareTo(second.OuterNodeLevel.X);
                if (x != 0) return x;
                int y = first.OuterNodeLevel.Y.CompareTo(second.OuterNodeLevel.Y);
                return y != 0 ? y : first.Part.ModelId.CompareTo(second.Part.ModelId);
            });
        }

        private static void RemoveDuplicateTopologies(List<Topology> values)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (values[i].View == values[j].View &&
                        Distance(values[i].NodeRef, values[j].NodeRef) <= RefNodeTolerance)
                    {
                        values.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private static void ProjectBounds(
            List<P2> points,
            P2 origin,
            P2 nAxis,
            P2 sAxis,
            out double minN,
            out double maxN,
            out double minS,
            out double maxS)
        {
            minN = Double.PositiveInfinity;
            maxN = Double.NegativeInfinity;
            minS = Double.PositiveInfinity;
            maxS = Double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                P2 delta = Subtract(points[i], origin);
                double n = Dot(delta, nAxis);
                double s = Dot(delta, sAxis);
                minN = Math.Min(minN, n);
                maxN = Math.Max(maxN, n);
                minS = Math.Min(minS, s);
                maxS = Math.Max(maxS, s);
            }
        }

        private static bool TryFarthestPair(List<P2> points, out P2 first, out P2 second)
        {
            first = null;
            second = null;
            double best = 0.0;
            if (points == null)
                return false;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = Distance(points[i], points[j]);
                    if (distance > best)
                    {
                        best = distance;
                        first = points[i];
                        second = points[j];
                    }
                }
            }
            return first != null && second != null && best > PointTolerance;
        }

        private static void AddUnique(List<P2> values, P2 point)
        {
            if (point == null)
                return;
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i], point) <= 0.01)
                    return;
            }
            values.Add(point);
        }

        private static int ComparePoint(P2 first, P2 second)
        {
            if (Object.ReferenceEquals(first, second))
                return 0;
            if (first == null)
                return 1;
            if (second == null)
                return -1;
            int byX = first.X.CompareTo(second.X);
            return byX != 0 ? byX : first.Y.CompareTo(second.Y);
        }

        private static void AddUniqueSegment(List<Segment2> values, P2 a, P2 b)
        {
            if (a == null || b == null || Distance(a, b) <= 0.01)
                return;
            for (int i = 0; i < values.Count; i++)
            {
                bool same = Distance(values[i].A, a) <= 0.01 && Distance(values[i].B, b) <= 0.01;
                bool reverse = Distance(values[i].A, b) <= 0.01 && Distance(values[i].B, a) <= 0.01;
                if (same || reverse)
                    return;
            }
            values.Add(new Segment2(a, b));
        }

        private static void AddUniqueAnchor(List<AnchorPoint> values, AnchorPoint anchor)
        {
            if (anchor == null || anchor.Point == null)
                return;
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].Point, anchor.Point) <= 0.01)
                    return;
            }
            values.Add(anchor);
        }

        private static P2 Transform(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            TSG.Point view = TransformToView(point, currentToGlobal, globalToView);
            return new P2(view.X, view.Y);
        }

        private static TSG.Point TransformToView(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            TSG.Point global = currentToGlobal.Transform(point);
            return globalToView.Transform(global);
        }

        private static void IncludeViewDepth(PartData part, double depth)
        {
            if (part == null || !IsFinite(depth))
                return;
            part.MinViewDepth = Math.Min(part.MinViewDepth, depth);
            part.MaxViewDepth = Math.Max(part.MaxViewDepth, depth);
        }

        private static bool HasFiniteViewDepth(PartData part)
        {
            return part != null
                && IsFinite(part.MinViewDepth)
                && IsFinite(part.MaxViewDepth)
                && part.MinViewDepth <= part.MaxViewDepth;
        }

        private static bool ViewDepthRangesOverlap(
            PartData first,
            PartData second,
            double tolerance)
        {
            return HasFiniteViewDepth(first)
                && HasFiniteViewDepth(second)
                && first.MaxViewDepth >= second.MinViewDepth - tolerance
                && second.MaxViewDepth >= first.MinViewDepth - tolerance;
        }

        private static bool ProjectionIntervalContains(
            double minimum,
            double maximum,
            double target,
            double tolerance)
        {
            return IsFinite(minimum)
                && IsFinite(maximum)
                && IsFinite(target)
                && target >= minimum - tolerance
                && target <= maximum + tolerance;
        }

        private static P2 Subtract(P2 first, P2 second)
        {
            return new P2(first.X - second.X, first.Y - second.Y);
        }

        private static P2 Normalize(P2 value)
        {
            if (value == null)
                return null;
            double length = Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return length <= 0.000001 ? null : new P2(value.X / length, value.Y / length);
        }

        private static double Dot(P2 first, P2 second)
        {
            return first.X * second.X + first.Y * second.Y;
        }

        private static double Distance(P2 first, P2 second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool SameIdentifier(TSM.Part first, TSM.Part second)
        {
            return first != null && second != null && first.Identifier != null &&
                second.Identifier != null && first.Identifier.ID == second.Identifier.ID;
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes ReadDimensionAttributes(
            TSD.View view)
        {
            try
            {
                TSD.DrawingObjectEnumerator dimensions =
                    view.GetAllObjects(typeof(TSD.StraightDimensionSet));
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

        private static double ReadScale(TSD.View view)
        {
            try { return view.Attributes.Scale; }
            catch { return Double.NaN; }
        }

        private static string SafeViewName(TSD.View view)
        {
            try
            {
                object type = GetMember(view, "ViewType");
                return type == null ? view.GetType().Name : Convert.ToString(type, CultureInfo.InvariantCulture);
            }
            catch { return "View"; }
        }

        private static string ReadPartPosition(TSM.Part part)
        {
            string value = "";
            try { part.GetReportProperty("PART_POS", ref value); }
            catch { }
            return String.IsNullOrWhiteSpace(value) ? "?" : value.Trim();
        }

        private static object GetMember(object value, string name)
        {
            if (value == null)
                return null;
            try
            {
                PropertyInfo property = value.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(value, null);
                FieldInfo field = value.GetType().GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field == null ? null : field.GetValue(value);
            }
            catch { return null; }
        }

        private static string SafeUpper(string value)
        {
            return String.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToUpperInvariant();
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
            return point == null ? "<null>" : "(" + Format(point.X) + "," + Format(point.Y) + ")";
        }

        private static string PartLabel(PartData part)
        {
            return part == null ? "<none>" : "P" + part.ModelId + "[" + part.Position + "]";
        }

        private static void ShowWarning(string message)
        {
            try
            {
                MessageBox.Show(message, "PHU Slot 10", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }
}
