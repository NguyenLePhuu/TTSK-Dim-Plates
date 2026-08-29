#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Optional Inzai-only section-view dimension flow.  This engine owns only
    /// dimensions in proven SectionView topologies and deliberately shares no
    /// geometry state with Shape, Neighbor, Splice, or Column Grid engines.
    /// Unsupported/ambiguous sections are a no-op so the established column
    /// flow remains usable on drawings without these sections.
    /// </summary>
    public static class PHU_InzaiColumnSectionDimensionEngine
    {
        private const double GeometryTolerance = 0.75;
        private const double MatchTolerance = 2.0;
        private const double DirectionCosineTolerance = 0.985;
        private const double ReferenceDepthTolerance = 1.0;

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

        private sealed class P3
        {
            public double X;
            public double Y;
            public double Z;

            public P3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public P2 XY
            {
                get { return new P2(X, Y); }
            }
        }

        private sealed class Bounds2
        {
            public bool IsValid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public double Width
            {
                get { return IsValid ? MaxX - MinX : 0.0; }
            }

            public double Height
            {
                get { return IsValid ? MaxY - MinY : 0.0; }
            }

            public P2 Center
            {
                get { return new P2((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5); }
            }

            public void Add(P2 point)
            {
                if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                    return;
                if (!IsValid)
                {
                    MinX = MaxX = point.X;
                    MinY = MaxY = point.Y;
                    IsValid = true;
                    return;
                }
                MinX = Math.Min(MinX, point.X);
                MaxX = Math.Max(MaxX, point.X);
                MinY = Math.Min(MinY, point.Y);
                MaxY = Math.Max(MaxY, point.Y);
            }
        }

        private sealed class DepthRange
        {
            public bool IsValid;
            public double Min;
            public double Max;

            public void Add(double value)
            {
                if (!IsFinite(value))
                    return;
                if (!IsValid)
                {
                    Min = Max = value;
                    IsValid = true;
                    return;
                }
                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
            }

            public bool Contains(double value, double tolerance)
            {
                return IsValid && value >= Min - tolerance && value <= Max + tolerance;
            }
        }

        private sealed class BoltData
        {
            public int Identifier;
            public readonly List<P3> Points = new List<P3>();
        }

        private sealed class PartData
        {
            public TSM.Part ModelPart;
            public int Identifier;
            public bool IsDrawingMain;
            public bool SameMainAssembly;
            public bool IsContourPlate;
            public readonly Bounds2 Bounds = new Bounds2();
            public readonly DepthRange SolidDepth = new DepthRange();
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<P3> Reference = new List<P3>();
            public readonly Dictionary<int, BoltData> Bolts =
                new Dictionary<int, BoltData>();
        }

        private sealed class ExistingDimension
        {
            public TSD.StraightDimensionSet Dimension;
            public readonly List<P2> Points = new List<P2>();
            public P2 Direction;
        }

        private sealed class ViewData
        {
            public TSD.View View;
            public int Identifier;
            public string Label;
            public double Scale;
            public double DepthMin;
            public double DepthMax;
            public readonly List<PartData> Parts = new List<PartData>();
            public readonly List<ExistingDimension> Dimensions =
                new List<ExistingDimension>();
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes Attributes;
        }

        private sealed class ReferenceArm
        {
            public PartData Part;
            public P2 Node;
            public P2 Far;
            public double Depth;
        }

        private sealed class NestedTopology
        {
            public PartData Core;
            public PartData Outer;
            public P2 Node;
        }

        private sealed class CTopology
        {
            public NestedTopology Nested;
            public ReferenceArm Horizontal;
            public ReferenceArm Upper;
            public ReferenceArm Lower;
            public ReferenceArm NeighborUpper;
            public ReferenceArm NeighborLower;
            public P2 SecondaryNode;
            public PartData UpperPlate;
            public PartData LowerPlate;
        }

        /// <summary>
        /// A proven subset of the complete C topology.  Every member is
        /// optional after Nested, so one absent upper/lower connection does not
        /// suppress the independent relations that still have exact geometry.
        /// </summary>
        private sealed class PartialCTopology
        {
            public NestedTopology Nested;
            public ReferenceArm Horizontal;
            public ReferenceArm Upper;
            public ReferenceArm Lower;
            public ReferenceArm NeighborUpper;
            public ReferenceArm NeighborLower;
            public P2 SecondaryNode;
            public PartData UpperPlate;
            public PartData LowerPlate;
        }

        private enum Type1BLinkSide
        {
            Upper = 0,
            Lower = 1,
            Left = 2,
            Right = 3
        }

        /// <summary>
        /// One independently proven Type-1 section-B connection.  The neighbor
        /// reference supplies the axis foot; the shared bolt group proves which
        /// main-assembly plate belongs to that neighbor; all remaining feet are
        /// exact solid vertices of the main or plate.
        /// </summary>
        private sealed class Type1BLink
        {
            public Type1BLinkSide Side;
            public PartData Neighbor;
            public PartData Plate;
            public P2 ReferenceFoot;
            public P2 PlateEdge;
            public P2 MainBoundaryFirst;
            public P2 MainBoundarySecond;
        }

        private sealed class Type1BTopology
        {
            public PartData Main;
            public P2 MainReference;
            public readonly List<Type1BLink> Links = new List<Type1BLink>();
        }

        private sealed class DTopology
        {
            public PartData Core;
            public ReferenceArm Left;
            public ReferenceArm Right;
            public P2 Node;
            public P3 LeftHole;
            public P3 RightHole;
            public double InteriorSignY;
        }

        /// <summary>
        /// One or two independently proven sides of the D topology.  A missing
        /// opposite arm or hole removes only the plans that need that feature.
        /// </summary>
        private sealed class PartialDTopology
        {
            public PartData Core;
            public P2 Node;
            public ReferenceArm Left;
            public ReferenceArm Right;
            public P3 LeftHole;
            public P3 RightHole;
            public double InteriorSignY;
            public double ReferenceDepth;
            public bool Ambiguous;
        }

        private sealed class PartialDArmCandidate
        {
            public PartData Core;
            public ReferenceArm Arm;
            public P2 Node;
            public P3 Hole;
            public bool IsLeft;
            public double InteriorSignY;
        }

        private sealed class DimPlan
        {
            public string Name;
            public string Topology;
            public ViewData View;
            public P2 Direction;
            public double PaperDistance;
            public readonly List<P2> Points = new List<P2>();

            public double Distance
            {
                get { return PaperDistance * View.Scale; }
            }
        }

        private sealed class ViewPlans
        {
            public ViewData View;
            public string Topology;
            public readonly List<DimPlan> Plans = new List<DimPlan>();
        }

        private enum ExistingPlanStatus
        {
            Missing,
            Exact,
            FootConflict
        }

        private static bool _enabled;

        public static bool LastRunApplicable { get; private set; }
        public static bool LastRunSucceeded { get; private set; }
        public static int LastCreatedCount { get; private set; }
        public static int LastReusedCount { get; private set; }
        public static int LastConflictCount { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Configure(bool enabled)
        {
            ResetResult();
            _enabled = enabled;
        }

        public static void Reset()
        {
            _enabled = false;
            ResetResult();
        }

        /// <summary>
        /// Runs after the established column engines.  It never throws into
        /// their coordinator: an uncertain section is skipped locally.
        /// </summary>
        public static bool ExecuteAfterColumnFlow()
        {
            ResetResult();
            if (!_enabled)
                return true;

            List<TSD.StraightDimensionSet> created =
                new List<TSD.StraightDimensionSet>();
            TSD.Drawing drawing = null;
            try
            {
                TSM.Model model;
                TSM.Part mainPart;
                List<ViewPlans> viewPlans;
                string analysisMessage;
                if (!TryAnalyzeCurrentDrawing(
                    out model,
                    out drawing,
                    out mainPart,
                    out viewPlans,
                    out analysisMessage))
                {
                    LastRunSucceeded = true;
                    LastRunMessage = "Inzai Section DIM skipped: " + analysisMessage;
                    return true;
                }

                LastRunApplicable = viewPlans.Count > 0;
                if (!LastRunApplicable)
                {
                    LastRunSucceeded = true;
                    LastRunMessage =
                        "Inzai Section DIM skipped: no supported B/C/D section topology was found.";
                    return true;
                }

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();
                int planCount = 0;
                for (int v = 0; v < viewPlans.Count; v++)
                {
                    ViewPlans group = viewPlans[v];
                    ValidatePlans(group);
                    planCount += group.Plans.Count;
                    for (int p = 0; p < group.Plans.Count; p++)
                    {
                        DimPlan plan = group.Plans[p];
                        ExistingPlanStatus status = FindExistingPlan(plan);
                        if (status == ExistingPlanStatus.Exact)
                        {
                            LastReusedCount++;
                            continue;
                        }
                        if (status == ExistingPlanStatus.FootConflict)
                        {
                            LastConflictCount++;
                            continue;
                        }

                        TSD.PointList points = new TSD.PointList();
                        for (int i = 0; i < plan.Points.Count; i++)
                            points.Add(plan.Points[i].ToPoint());
                        TSG.Vector direction = new TSG.Vector(
                            plan.Direction.X,
                            plan.Direction.Y,
                            0.0);
                        TSD.StraightDimensionSet dimension =
                            group.View.Attributes == null
                                ? handler.CreateDimensionSet(
                                    group.View.View,
                                    points,
                                    direction,
                                    plan.Distance)
                                : handler.CreateDimensionSet(
                                    group.View.View,
                                    points,
                                    direction,
                                    plan.Distance,
                                    group.View.Attributes);
                        if (dimension == null)
                            throw new InvalidOperationException(
                                "Tekla could not create " + plan.Name + ".");
                        created.Add(dimension);
                        DisableCombineAndVerify(plan, dimension);
                        LastCreatedCount++;
                    }
                }

                if (created.Count > 0)
                    drawing.CommitChanges();

                LastRunSucceeded = true;
                LastRunMessage =
                    "Inzai Section DIM: "
                    + viewPlans.Count.ToString(CultureInfo.InvariantCulture)
                    + " supported section(s), "
                    + planCount.ToString(CultureInfo.InvariantCulture)
                    + " plan(s), created "
                    + LastCreatedCount.ToString(CultureInfo.InvariantCulture)
                    + ", reused "
                    + LastReusedCount.ToString(CultureInfo.InvariantCulture)
                    + ", protected conflicts "
                    + LastConflictCount.ToString(CultureInfo.InvariantCulture)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                TryCommit(drawing);
                LastRunSucceeded = false;
                LastCreatedCount = 0;
                LastRunMessage =
                    "Inzai Section DIM skipped without blocking the column flow: "
                    + ex.Message;
                return true;
            }
        }

        /// <summary>Read-only live audit; no drawing mutation or CommitChanges.</summary>
        public static string AuditCurrentDrawingPlan()
        {
            try
            {
                TSM.Model model;
                TSD.Drawing drawing;
                TSM.Part mainPart;
                List<ViewPlans> groups;
                string message;
                if (!TryAnalyzeCurrentDrawing(
                    out model,
                    out drawing,
                    out mainPart,
                    out groups,
                    out message))
                    return "INZAI SECTION PLAN AUDIT SKIPPED\r\n" + message;

                StringBuilder text = new StringBuilder();
                text.AppendLine("INZAI SECTION PLAN AUDIT - READ ONLY");
                text.AppendLine("SupportedViews=" + groups.Count.ToString(
                    CultureInfo.InvariantCulture));
                int total = 0;
                int exact = 0;
                int missing = 0;
                int conflicts = 0;
                for (int v = 0; v < groups.Count; v++)
                {
                    ViewPlans group = groups[v];
                    ValidatePlans(group);
                    text.AppendLine(
                        "VIEW id=" + group.View.Identifier.ToString(
                            CultureInfo.InvariantCulture)
                        + " label=" + Quote(group.View.Label)
                        + " topology=" + group.Topology
                        + " scale=" + Format(group.View.Scale)
                        + " plans=" + group.Plans.Count.ToString(
                            CultureInfo.InvariantCulture));
                    for (int p = 0; p < group.Plans.Count; p++)
                    {
                        DimPlan plan = group.Plans[p];
                        ExistingPlanStatus status = FindExistingPlan(plan);
                        total++;
                        if (status == ExistingPlanStatus.Exact) exact++;
                        else if (status == ExistingPlanStatus.Missing) missing++;
                        else conflicts++;
                        text.Append("  ").Append(plan.Name)
                            .Append(" status=").Append(status)
                            .Append(" direction=").Append(FormatPoint(plan.Direction))
                            .Append(" paperDistance=").Append(Format(plan.PaperDistance))
                            .Append(" feet=").Append(FormatPoints(plan.Points))
                            .AppendLine();
                    }
                }
                text.AppendLine(
                    "TOTAL plans=" + total.ToString(CultureInfo.InvariantCulture)
                    + " exact=" + exact.ToString(CultureInfo.InvariantCulture)
                    + " missing=" + missing.ToString(CultureInfo.InvariantCulture)
                    + " conflicts=" + conflicts.ToString(CultureInfo.InvariantCulture));
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "INZAI SECTION PLAN AUDIT FAILED\r\n" + ex;
            }
        }

        /// <summary>Pure plan regression; no Tekla Model or Drawing API mutation.</summary>
        public static string AuditGeometryRegression()
        {
            try
            {
                ViewData view = new ViewData();
                view.Scale = 20.0;
                NestedTopology nested = new NestedTopology();
                nested.Node = new P2(0.0, 0.0);
                nested.Core = SyntheticPart(-147, 147, -100, 100);
                nested.Outer = SyntheticPart(-172, 172, -125, 125);

                List<DimPlan> b = BuildBPlans(view, nested);
                if (b.Count != 4 || !ContainsPlan(
                    b,
                    new P2(-172, 125),
                    new P2(-147, 100),
                    new P2(147, 100),
                    new P2(172, 125)))
                    throw new InvalidOperationException("B regression failed.");

                ViewData type1BAllView = BuildSyntheticType1BView(
                    true, true, true, true, false, "B", false);
                Type1BTopology type1BAll;
                if (!TryResolveType1B(type1BAllView, out type1BAll)
                    || type1BAll.Links.Count != 4)
                    throw new InvalidOperationException(
                        "Type1 B four-side resolver regression failed.");
                List<DimPlan> type1BAllPlans = BuildType1BPlans(
                    type1BAllView,
                    type1BAll);
                if (type1BAllPlans.Count != 8
                    || !ContainsPlan(
                        type1BAllPlans,
                        new P2(-100, 100),
                        new P2(-37.5, 0),
                        new P2(100, 100))
                    || !ContainsPlan(
                        type1BAllPlans,
                        new P2(-34.25, 235),
                        new P2(-37.5, 0))
                    || !ContainsPlan(
                        type1BAllPlans,
                        new P2(100, -100),
                        new P2(0, 0),
                        new P2(100, 100))
                    || !ContainsPlan(
                        type1BAllPlans,
                        new P2(245, -3.25),
                        new P2(0, 0)))
                    throw new InvalidOperationException(
                        "Type1 B exact edge/reference plan regression failed.");

                ViewData type1BUpperOnlyView = BuildSyntheticType1BView(
                    true, false, false, false, false, "B", false);
                Type1BTopology type1BUpperOnly;
                if (!TryResolveType1B(type1BUpperOnlyView, out type1BUpperOnly)
                    || BuildType1BPlans(type1BUpperOnlyView, type1BUpperOnly).Count != 2)
                    throw new InvalidOperationException(
                        "Type1 B one-link partial regression failed.");

                ViewData type1BShuffledView = BuildSyntheticType1BView(
                    true, true, false, true, true, "B", false);
                Type1BTopology type1BShuffled;
                if (!TryResolveType1B(type1BShuffledView, out type1BShuffled)
                    || BuildType1BPlans(type1BShuffledView, type1BShuffled).Count != 6)
                    throw new InvalidOperationException(
                        "Type1 B missing-side/shuffled-order regression failed.");

                int type1BMatrixPassed = 0;
                for (int mask = 1; mask < 16; mask++)
                {
                    ViewData matrixView = BuildSyntheticType1BView(
                        (mask & 1) != 0,
                        (mask & 2) != 0,
                        (mask & 4) != 0,
                        (mask & 8) != 0,
                        (mask & 1) == 0,
                        "B",
                        false);
                    Type1BTopology matrixTopology;
                    int expectedLinks = CountSetBits(mask);
                    if (!TryResolveType1B(matrixView, out matrixTopology)
                        || matrixTopology.Links.Count != expectedLinks
                        || BuildType1BPlans(matrixView, matrixTopology).Count
                            != expectedLinks * 2)
                        throw new InvalidOperationException(
                            "Type1 B cardinal matrix regression failed at mask "
                            + mask.ToString(CultureInfo.InvariantCulture) + ".");
                    type1BMatrixPassed++;
                }
                if (type1BMatrixPassed != 15)
                    throw new InvalidOperationException(
                        "Type1 B cardinal matrix did not test all 15 non-empty subsets.");

                Type1BTopology rejectedType1B;
                if (TryResolveType1B(
                        BuildSyntheticType1BView(
                            true, true, true, true, false, "A", false),
                        out rejectedType1B)
                    || TryResolveType1B(
                        BuildSyntheticType1BView(
                            true, true, true, true, false, "B", true),
                        out rejectedType1B))
                    throw new InvalidOperationException(
                        "Type1 B routing/Type2-isolation regression failed.");

                CTopology c = new CTopology();
                c.Nested = nested;
                c.Horizontal = SyntheticArm(
                    SyntheticPart(-625, -147, -100, 100),
                    nested.Node,
                    new P2(-625, 0));
                c.Upper = SyntheticArm(
                    SyntheticPart(-100, 100, 4, 945),
                    nested.Node,
                    new P2(0, 945));
                c.Lower = SyntheticArm(
                    SyntheticPart(-100, 100, -890, -4),
                    nested.Node,
                    new P2(0, -900));
                c.SecondaryNode = new P2(-300, 0);
                c.NeighborUpper = SyntheticArm(
                    SyntheticPart(-362.5, -237.5, 110, 1780),
                    c.SecondaryNode,
                    new P2(-300, 1890));
                c.NeighborLower = SyntheticArm(
                    SyntheticPart(-362.5, -237.5, -1690, -110),
                    c.SecondaryNode,
                    new P2(-300, -1800));
                c.UpperPlate = SyntheticPart(-296.75, -287.75, 4, 235);
                c.LowerPlate = SyntheticPart(-296.75, -287.75, -235, -4);
                List<DimPlan> cPlans = BuildCPlans(view, c);
                if (cPlans.Count != 13 || !ContainsPlan(
                    cPlans,
                    new P2(-625, 100),
                    new P2(-300, 0),
                    new P2(0, 0)))
                    throw new InvalidOperationException("C regression failed.");

                DTopology d = new DTopology();
                d.Node = new P2(0, -10.51);
                d.Core = SyntheticPart(-100, 100, -304.51, -10.51);
                d.Left = SyntheticArm(
                    SyntheticPart(-1690, -110, -135.51, -10.51),
                    d.Node,
                    new P2(-1800, -10.51));
                d.Right = SyntheticArm(
                    SyntheticPart(110, 1780, -135.51, -10.51),
                    d.Node,
                    new P2(1890, -10.51));
                d.LeftHole = new P3(-150, -73.01, 0);
                d.RightHole = new P3(150, -73.01, 0);
                d.InteriorSignY = -1.0;
                List<DimPlan> dPlans = BuildDPlans(view, d);
                if (dPlans.Count != 5 || !ContainsPlan(
                    dPlans,
                    new P2(-150, -73.01),
                    new P2(0, -10.51),
                    new P2(150, -73.01)))
                    throw new InvalidOperationException("D regression failed.");

                PartialCTopology partialUpper = new PartialCTopology();
                partialUpper.Nested = nested;
                partialUpper.Horizontal = c.Horizontal;
                partialUpper.Upper = c.Upper;
                partialUpper.NeighborUpper = c.NeighborUpper;
                partialUpper.SecondaryNode = c.SecondaryNode;
                partialUpper.UpperPlate = c.UpperPlate;
                List<DimPlan> partialUpperPlans = BuildPartialCPlans(
                    view,
                    partialUpper);
                if (partialUpperPlans.Count != 10
                    || ContainsPlan(
                        partialUpperPlans,
                        new P2(-147, -100),
                        new P2(0, -890),
                        new P2(147, -100))
                    || !ContainsPlan(
                        partialUpperPlans,
                        new P2(0, 0),
                        new P2(100, 945)))
                    throw new InvalidOperationException(
                        "Partial C upper-only regression failed.");

                PartialCTopology partialLower = new PartialCTopology();
                partialLower.Nested = nested;
                partialLower.Horizontal = c.Horizontal;
                partialLower.Lower = c.Lower;
                partialLower.NeighborLower = c.NeighborLower;
                partialLower.SecondaryNode = c.SecondaryNode;
                partialLower.LowerPlate = c.LowerPlate;
                List<DimPlan> partialLowerPlans = BuildPartialCPlans(
                    view,
                    partialLower);
                if (partialLowerPlans.Count != 10
                    || ContainsPlan(
                        partialLowerPlans,
                        new P2(-147, 100),
                        new P2(0, 945),
                        new P2(147, 100))
                    || !ContainsPlan(
                        partialLowerPlans,
                        new P2(100, -890),
                        new P2(0, 0)))
                    throw new InvalidOperationException(
                        "Partial C lower-only regression failed.");

                PartialCTopology partialVerticalOnly = new PartialCTopology();
                partialVerticalOnly.Nested = nested;
                partialVerticalOnly.Upper = c.Upper;
                List<DimPlan> partialVerticalPlans = BuildPartialCPlans(
                    view,
                    partialVerticalOnly);
                if (partialVerticalPlans.Count != 5
                    || !ContainsPlan(
                        partialVerticalPlans,
                        new P2(-147, 100),
                        new P2(0, 945),
                        new P2(147, 100)))
                    throw new InvalidOperationException(
                        "Partial C vertical-only regression failed.");

                PartialCTopology partialMirror = new PartialCTopology();
                partialMirror.Nested = nested;
                partialMirror.Horizontal = SyntheticArm(
                    SyntheticPart(147, 625, -100, 100),
                    nested.Node,
                    new P2(625, 0));
                partialMirror.Upper = c.Upper;
                partialMirror.SecondaryNode = new P2(300, 0);
                partialMirror.NeighborUpper = SyntheticArm(
                    SyntheticPart(237.5, 362.5, 110, 1780),
                    partialMirror.SecondaryNode,
                    new P2(300, 1890));
                partialMirror.UpperPlate = SyntheticPart(
                    287.75,
                    296.75,
                    4,
                    235);
                List<DimPlan> partialMirrorPlans = BuildPartialCPlans(
                    view,
                    partialMirror);
                if (partialMirrorPlans.Count != 10
                    || !ContainsPlan(
                        partialMirrorPlans,
                        new P2(172, -125),
                        new P2(625, 0),
                        new P2(172, 125))
                    || !ContainsPlan(
                        partialMirrorPlans,
                        new P2(0, 0),
                        new P2(-100, 945)))
                    throw new InvalidOperationException(
                        "Partial C mirrored regression failed.");

                PartialDTopology partialLeftD = new PartialDTopology();
                partialLeftD.Core = d.Core;
                partialLeftD.Node = d.Node;
                partialLeftD.Left = d.Left;
                partialLeftD.LeftHole = d.LeftHole;
                partialLeftD.InteriorSignY = d.InteriorSignY;
                if (BuildPartialDPlans(view, partialLeftD).Count != 2)
                    throw new InvalidOperationException(
                        "Partial D left-only regression failed.");

                PartialDTopology partialRightD = new PartialDTopology();
                partialRightD.Core = d.Core;
                partialRightD.Node = d.Node;
                partialRightD.Right = d.Right;
                partialRightD.InteriorSignY = d.InteriorSignY;
                if (BuildPartialDPlans(view, partialRightD).Count != 1)
                    throw new InvalidOperationException(
                        "Partial D right-without-hole regression failed.");

                return "INZAI SECTION GEOMETRY REGRESSION PASS: "
                    + "B=4, B1 full/one-link/missing-side=8/2/6, "
                    + "B1 cardinal-matrix=15/15, shuffled/type2-isolation=PASS, "
                    + "C=13, C-partial upper/lower/vertical/mirror=10/10/5/10, "
                    + "D=5, D-partial=2/1, no-section=0.";
            }
            catch (Exception ex)
            {
                return "INZAI SECTION GEOMETRY REGRESSION FAILED: " + ex.Message;
            }
        }

        private static bool TryAnalyzeCurrentDrawing(
            out TSM.Model model,
            out TSD.Drawing drawing,
            out TSM.Part mainPart,
            out List<ViewPlans> result,
            out string message)
        {
            model = new TSM.Model();
            drawing = null;
            mainPart = null;
            result = new List<ViewPlans>();
            message = String.Empty;

            TSD.DrawingHandler handler = new TSD.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
            {
                message = "Tekla Model/Drawing API is unavailable.";
                return false;
            }
            drawing = handler.GetActiveDrawing();
            if (drawing == null)
            {
                message = "No active drawing.";
                return false;
            }

            string projectKey;
            string routeMessage;
            if (!PHU_ColumnProjectRouter.TryResolve(
                model,
                out projectKey,
                out routeMessage)
                || !String.Equals(
                    projectKey,
                    PHU_ColumnProjectRouter.InzaiDataCenterKey,
                    StringComparison.Ordinal))
            {
                message = "project route is not Inzai Data Center. " + routeMessage;
                return false;
            }

            mainPart = PHU_MainPartResolver.Resolve(model, drawing);
            if (mainPart == null)
            {
                message = "Assembly MainPart could not be resolved.";
                return false;
            }

            TSM.TransformationPlane originalPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();
            TSG.Matrix currentToGlobal = originalPlane.TransformationMatrixToGlobal;
            TSD.ContainerView sheet = drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            int index = 0;
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null || !IsSectionView(view))
                    continue;
                ViewData data = ReadView(
                    model,
                    mainPart,
                    view,
                    currentToGlobal,
                    ++index);
                if (data == null)
                    continue;

                ViewPlans plans = new ViewPlans();
                plans.View = data;
                DTopology d;
                PartialDTopology partialD;
                CTopology c;
                PartialCTopology partialC;
                NestedTopology b;
                Type1BTopology type1B;
                if (TryResolveD(data, out d))
                {
                    plans.Topology = "D-REF-OPPOSING-NEIGHBORS";
                    plans.Plans.AddRange(BuildDPlans(data, d));
                }
                else if (TryResolveC(data, out c))
                {
                    plans.Topology = "C-CROSS-REF-AND-SECONDARY-PAIR";
                    plans.Plans.AddRange(BuildCPlans(data, c));
                }
                else if (TryResolvePartialC(data, out partialC))
                {
                    plans.Topology = DescribePartialC(partialC);
                    plans.Plans.AddRange(BuildPartialCPlans(data, partialC));
                }
                else if (TryResolvePartialD(data, out partialD))
                {
                    plans.Topology = DescribePartialD(partialD);
                    plans.Plans.AddRange(BuildPartialDPlans(data, partialD));
                }
                else if (TryResolveNested(data, out b))
                {
                    plans.Topology = "B-NESTED-MAIN-DIAPHRAGM";
                    plans.Plans.AddRange(BuildBPlans(data, b));
                }
                else if (TryResolveType1B(data, out type1B))
                {
                    plans.Topology = DescribeType1B(type1B);
                    plans.Plans.AddRange(BuildType1BPlans(data, type1B));
                }
                if (plans.Plans.Count > 0)
                    result.Add(plans);
            }
            return true;
        }

        private static ViewData ReadView(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal,
            int index)
        {
            object restriction = GetMember(view, "RestrictionBox");
            TSG.Point min = GetMember(restriction, "MinPoint") as TSG.Point;
            TSG.Point max = GetMember(restriction, "MaxPoint") as TSG.Point;
            if (min == null || max == null)
                return null;

            ViewData result = new ViewData();
            result.View = view;
            result.Identifier = ReadIdentifier(view);
            result.Label = SafeViewName(view);
            result.Scale = ReadScale(view);
            result.DepthMin = Math.Min(min.Z, max.Z);
            result.DepthMax = Math.Max(min.Z, max.Z);
            if (!IsFinite(result.Scale) || result.Scale <= 0.0)
                return null;

            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem);
            HashSet<int> seen = new HashSet<int>();
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part drawingPart = parts.Current as TSD.Part;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;
                int id = drawingPart.ModelIdentifier.ID;
                if (id <= 0 || !seen.Add(id))
                    continue;
                TSM.Part modelPart =
                    model.SelectModelObject(drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null)
                    continue;
                PartData data = ReadPart(
                    modelPart,
                    mainPart,
                    currentToGlobal,
                    globalToView);
                if (data.Bounds.IsValid)
                    result.Parts.Add(data);
            }
            ReadExistingDimensions(result);
            return result;
        }

        private static PartData ReadPart(
            TSM.Part part,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            PartData result = new PartData();
            result.ModelPart = part;
            result.Identifier = part.Identifier == null ? 0 : part.Identifier.ID;
            result.IsDrawingMain = SameIdentifier(part, mainPart);
            result.IsContourPlate = part is TSM.ContourPlate;
            try
            {
                TSM.Assembly assembly = part.GetAssembly();
                TSM.Part assemblyMain =
                    assembly == null ? null : assembly.GetMainPart() as TSM.Part;
                result.SameMainAssembly = SameIdentifier(assemblyMain, mainPart);
            }
            catch { }

            try
            {
                ArrayList reference = part.GetReferenceLine(false);
                if (reference != null)
                {
                    foreach (object value in reference)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUnique(result.Reference,
                                Transform3(point, currentToGlobal, globalToView));
                    }
                }
            }
            catch { }

            try
            {
                TSM.Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;
                    AddSolidPoint(result,
                        Transform3(edge.StartPoint, currentToGlobal, globalToView));
                    AddSolidPoint(result,
                        Transform3(edge.EndPoint, currentToGlobal, globalToView));
                }
            }
            catch { }

            try
            {
                TSM.ModelObjectEnumerator bolts = part.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    TSM.BoltGroup group = bolts.Current as TSM.BoltGroup;
                    if (group == null || group.Identifier == null
                        || group.BoltPositions == null)
                        continue;
                    BoltData data;
                    if (!result.Bolts.TryGetValue(group.Identifier.ID, out data))
                    {
                        data = new BoltData();
                        data.Identifier = group.Identifier.ID;
                        result.Bolts.Add(data.Identifier, data);
                    }
                    foreach (object value in group.BoltPositions)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUnique(data.Points,
                                Transform3(point, currentToGlobal, globalToView));
                    }
                }
            }
            catch { }
            return result;
        }

        private static void ReadExistingDimensions(ViewData view)
        {
            try
            {
                TSD.DrawingObjectEnumerator dimensions = view.View.GetAllObjects(
                    typeof(TSD.StraightDimensionSet));
                while (dimensions != null && dimensions.MoveNext())
                {
                    TSD.StraightDimensionSet set =
                        dimensions.Current as TSD.StraightDimensionSet;
                    if (set == null)
                        continue;
                    ExistingDimension item = new ExistingDimension();
                    item.Dimension = set;
                    item.Points.AddRange(ReadDimensionPoints(set));
                    item.Direction = ReadDirection(set);
                    view.Dimensions.Add(item);
                    if (view.Attributes == null && set.Attributes != null)
                        view.Attributes = set.Attributes;
                }
            }
            catch { }
        }

        private static bool TryResolveNested(
            ViewData view,
            out NestedTopology topology)
        {
            topology = null;
            List<NestedTopology> matches = new List<NestedTopology>();
            for (int o = 0; o < view.Parts.Count; o++)
            {
                PartData outer = view.Parts[o];
                if (!outer.SameMainAssembly || !outer.IsContourPlate
                    || !HasReferenceInsideDepth(view, outer))
                    continue;
                for (int c = 0; c < view.Parts.Count; c++)
                {
                    PartData core = view.Parts[c];
                    if (!core.SameMainAssembly || core.IsContourPlate
                        || !core.Bounds.IsValid || !outer.Bounds.IsValid)
                        continue;
                    P3 node3;
                    if (!TryCollapsedReferencePointInDepth(view, core, out node3))
                        continue;
                    P2 node = node3.XY;
                    if (Distance(core.Bounds.Center, node) > GeometryTolerance * 3.0
                        || Distance(outer.Bounds.Center, node) > GeometryTolerance * 3.0)
                        continue;
                    double gapLeft = core.Bounds.MinX - outer.Bounds.MinX;
                    double gapRight = outer.Bounds.MaxX - core.Bounds.MaxX;
                    double gapBottom = core.Bounds.MinY - outer.Bounds.MinY;
                    double gapTop = outer.Bounds.MaxY - core.Bounds.MaxY;
                    if (gapLeft <= 1.0 || gapRight <= 1.0
                        || gapBottom <= 1.0 || gapTop <= 1.0)
                        continue;
                    if (Math.Abs(gapLeft - gapRight) > GeometryTolerance * 3.0
                        || Math.Abs(gapBottom - gapTop) > GeometryTolerance * 3.0)
                        continue;
                    if (outer.Bounds.Width > core.Bounds.Width * 1.75
                        || outer.Bounds.Height > core.Bounds.Height * 1.75)
                        continue;
                    NestedTopology candidate = new NestedTopology();
                    candidate.Core = core;
                    candidate.Outer = outer;
                    candidate.Node = node;
                    AddUniqueNested(matches, candidate);
                }
            }
            if (matches.Count != 1)
                return false;
            topology = matches[0];
            return true;
        }

        private static bool TryResolveType1B(
            ViewData view,
            out Type1BTopology topology)
        {
            topology = null;
            if (view == null
                || !String.Equals(
                    (view.Label ?? String.Empty).Trim(),
                    "B",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            PartData main = null;
            P3 mainReference = null;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                P3 reference;
                if (candidate == null
                    || !candidate.IsDrawingMain
                    || candidate.IsContourPlate
                    || !candidate.Bounds.IsValid
                    || !TryCollapsedReferencePointAtDepth(
                        candidate,
                        (view.DepthMin + view.DepthMax) * 0.5,
                        out reference))
                    continue;
                if (main != null)
                    return false;
                main = candidate;
                mainReference = reference;
            }
            if (main == null || mainReference == null)
                return false;

            // Data Center Type 1 is the near-square H200 column section.  The
            // established Type-2 nested/rectangular H294 topology is resolved
            // earlier and is also excluded geometrically here, keeping its path
            // completely unchanged if a Type-2 section becomes incomplete.
            double minimumSide = Math.Min(main.Bounds.Width, main.Bounds.Height);
            double maximumSide = Math.Max(main.Bounds.Width, main.Bounds.Height);
            if (minimumSide <= GeometryTolerance
                || maximumSide / minimumSide > 1.25)
                return false;

            Type1BTopology result = new Type1BTopology();
            result.Main = main;
            result.MainReference = mainReference.XY;
            double minimumNeighborLength = maximumSide * 1.35;

            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData neighbor = view.Parts[i];
                if (neighbor == null
                    || neighbor.SameMainAssembly
                    || neighbor.IsContourPlate
                    || !neighbor.Bounds.IsValid)
                    continue;

                P3 first;
                P3 second;
                if (!TryReferencePairInsideDepth(view, neighbor, out first, out second)
                    || Distance(first.XY, second.XY) < minimumNeighborLength)
                    continue;

                Type1BLinkSide side;
                P2 referenceFoot;
                if (!TryResolveType1BLinkSideAndReference(
                    main,
                    result.MainReference,
                    neighbor,
                    first.XY,
                    second.XY,
                    out side,
                    out referenceFoot))
                    continue;

                PartData plate = FindType1BConnectionPlate(
                    view,
                    main,
                    neighbor,
                    side);
                if (plate == null)
                    continue;

                P2 plateEdge;
                P2 boundaryFirst;
                P2 boundarySecond;
                if (!TryResolveType1BPlateEdge(
                        plate,
                        side,
                        referenceFoot,
                        out plateEdge)
                    || !TryResolveType1BMainBoundary(
                        main,
                        side,
                        out boundaryFirst,
                        out boundarySecond))
                    continue;

                Type1BLink link = new Type1BLink();
                link.Side = side;
                link.Neighbor = neighbor;
                link.Plate = plate;
                link.ReferenceFoot = referenceFoot;
                link.PlateEdge = plateEdge;
                link.MainBoundaryFirst = boundaryFirst;
                link.MainBoundarySecond = boundarySecond;
                AddUniqueType1BLink(result.Links, link);
            }

            if (result.Links.Count == 0)
                return false;
            result.Links.Sort(CompareType1BLinks);
            topology = result;
            return true;
        }

        private static bool TryResolveType1BLinkSideAndReference(
            PartData main,
            P2 mainReference,
            PartData neighbor,
            P2 first,
            P2 second,
            out Type1BLinkSide side,
            out P2 referenceFoot)
        {
            side = Type1BLinkSide.Upper;
            referenceFoot = null;
            P2 direction = Normalize(Subtract(second, first));
            if (main == null || mainReference == null || neighbor == null
                || direction == null)
                return false;

            double contactTolerance = Math.Max(
                25.0,
                Math.Min(main.Bounds.Width, main.Bounds.Height) * 0.15);

            if (Math.Abs(direction.X) >= DirectionCosineTolerance)
            {
                if (!TryPointOnSegmentAtX(
                    first,
                    second,
                    mainReference.X,
                    out referenceFoot)
                    || referenceFoot.Y < main.Bounds.MinY - GeometryTolerance * 3.0
                    || referenceFoot.Y > main.Bounds.MaxY + GeometryTolerance * 3.0)
                    return false;

                if (neighbor.Bounds.MinX >= main.Bounds.MaxX - contactTolerance)
                    side = Type1BLinkSide.Right;
                else if (neighbor.Bounds.MaxX <= main.Bounds.MinX + contactTolerance)
                    side = Type1BLinkSide.Left;
                else
                    return false;
                return true;
            }

            if (Math.Abs(direction.Y) < DirectionCosineTolerance
                || !TryPointOnSegmentAtY(
                    first,
                    second,
                    mainReference.Y,
                    out referenceFoot)
                || referenceFoot.X < main.Bounds.MinX - GeometryTolerance * 3.0
                || referenceFoot.X > main.Bounds.MaxX + GeometryTolerance * 3.0)
                return false;

            if (neighbor.Bounds.MinY >= main.Bounds.MaxY - contactTolerance)
                side = Type1BLinkSide.Upper;
            else if (neighbor.Bounds.MaxY <= main.Bounds.MinY + contactTolerance)
                side = Type1BLinkSide.Lower;
            else
                return false;
            return true;
        }

        private static PartData FindType1BConnectionPlate(
            ViewData view,
            PartData main,
            PartData neighbor,
            Type1BLinkSide side)
        {
            PartData selected = null;
            for (int i = 0; view != null && i < view.Parts.Count; i++)
            {
                PartData plate = view.Parts[i];
                if (plate == null
                    || !plate.SameMainAssembly
                    || !plate.IsContourPlate
                    || !plate.Bounds.IsValid
                    || !SharesBolt(plate, neighbor)
                    || !PlateExtendsFromMainOnSide(plate, main, side))
                    continue;
                if (selected != null
                    && selected.Identifier != plate.Identifier)
                    return null;
                selected = plate;
            }
            return selected;
        }

        private static bool PlateExtendsFromMainOnSide(
            PartData plate,
            PartData main,
            Type1BLinkSide side)
        {
            if (plate == null || main == null)
                return false;
            switch (side)
            {
                case Type1BLinkSide.Upper:
                    return plate.Bounds.MaxY > main.Bounds.MaxY + GeometryTolerance
                        && plate.Bounds.MinY <= main.Bounds.MaxY + GeometryTolerance * 3.0;
                case Type1BLinkSide.Lower:
                    return plate.Bounds.MinY < main.Bounds.MinY - GeometryTolerance
                        && plate.Bounds.MaxY >= main.Bounds.MinY - GeometryTolerance * 3.0;
                case Type1BLinkSide.Left:
                    return plate.Bounds.MinX < main.Bounds.MinX - GeometryTolerance
                        && plate.Bounds.MaxX >= main.Bounds.MinX - GeometryTolerance * 3.0;
                case Type1BLinkSide.Right:
                    return plate.Bounds.MaxX > main.Bounds.MaxX + GeometryTolerance
                        && plate.Bounds.MinX <= main.Bounds.MaxX + GeometryTolerance * 3.0;
                default:
                    return false;
            }
        }

        private static bool TryResolveType1BPlateEdge(
            PartData plate,
            Type1BLinkSide side,
            P2 referenceFoot,
            out P2 plateEdge)
        {
            plateEdge = null;
            if (plate == null || referenceFoot == null || plate.Vertices.Count == 0)
                return false;

            double closest = Double.PositiveInfinity;
            for (int i = 0; i < plate.Vertices.Count; i++)
            {
                P2 point = plate.Vertices[i];
                double delta = side == Type1BLinkSide.Upper
                    || side == Type1BLinkSide.Lower
                        ? Math.Abs(point.X - referenceFoot.X)
                        : Math.Abs(point.Y - referenceFoot.Y);
                closest = Math.Min(closest, delta);
            }
            if (!IsFinite(closest))
                return false;

            for (int i = 0; i < plate.Vertices.Count; i++)
            {
                P2 point = plate.Vertices[i];
                double delta = side == Type1BLinkSide.Upper
                    || side == Type1BLinkSide.Lower
                        ? Math.Abs(point.X - referenceFoot.X)
                        : Math.Abs(point.Y - referenceFoot.Y);
                if (delta > closest + GeometryTolerance)
                    continue;
                if (plateEdge == null
                    || IsFartherOnType1BSide(point, plateEdge, side))
                    plateEdge = point;
            }
            return plateEdge != null
                && Distance(plateEdge, referenceFoot) > GeometryTolerance;
        }

        private static bool IsFartherOnType1BSide(
            P2 candidate,
            P2 current,
            Type1BLinkSide side)
        {
            switch (side)
            {
                case Type1BLinkSide.Upper:
                    return candidate.Y > current.Y;
                case Type1BLinkSide.Lower:
                    return candidate.Y < current.Y;
                case Type1BLinkSide.Left:
                    return candidate.X < current.X;
                case Type1BLinkSide.Right:
                    return candidate.X > current.X;
                default:
                    return false;
            }
        }

        private static bool TryResolveType1BMainBoundary(
            PartData main,
            Type1BLinkSide side,
            out P2 first,
            out P2 second)
        {
            first = null;
            second = null;
            if (main == null || main.Vertices.Count < 2)
                return false;

            double extreme;
            if (side == Type1BLinkSide.Upper)
                extreme = main.Bounds.MaxY;
            else if (side == Type1BLinkSide.Lower)
                extreme = main.Bounds.MinY;
            else if (side == Type1BLinkSide.Left)
                extreme = main.Bounds.MinX;
            else
                extreme = main.Bounds.MaxX;

            double firstProjection = Double.PositiveInfinity;
            double secondProjection = Double.NegativeInfinity;
            for (int i = 0; i < main.Vertices.Count; i++)
            {
                P2 point = main.Vertices[i];
                double sideCoordinate = side == Type1BLinkSide.Upper
                    || side == Type1BLinkSide.Lower ? point.Y : point.X;
                if (Math.Abs(sideCoordinate - extreme) > GeometryTolerance)
                    continue;
                double projection = side == Type1BLinkSide.Upper
                    || side == Type1BLinkSide.Lower ? point.X : point.Y;
                if (projection < firstProjection)
                {
                    firstProjection = projection;
                    first = point;
                }
                if (projection > secondProjection)
                {
                    secondProjection = projection;
                    second = point;
                }
            }
            return first != null && second != null
                && Distance(first, second) > GeometryTolerance;
        }

        private static bool TryPointOnSegmentAtX(
            P2 first,
            P2 second,
            double x,
            out P2 point)
        {
            point = null;
            double delta = second.X - first.X;
            if (Math.Abs(delta) <= GeometryTolerance)
                return false;
            double t = (x - first.X) / delta;
            if (t < -0.01 || t > 1.01)
                return false;
            point = new P2(x, first.Y + (second.Y - first.Y) * t);
            return IsFinite(point.X) && IsFinite(point.Y);
        }

        private static bool TryPointOnSegmentAtY(
            P2 first,
            P2 second,
            double y,
            out P2 point)
        {
            point = null;
            double delta = second.Y - first.Y;
            if (Math.Abs(delta) <= GeometryTolerance)
                return false;
            double t = (y - first.Y) / delta;
            if (t < -0.01 || t > 1.01)
                return false;
            point = new P2(first.X + (second.X - first.X) * t, y);
            return IsFinite(point.X) && IsFinite(point.Y);
        }

        private static void AddUniqueType1BLink(
            List<Type1BLink> links,
            Type1BLink candidate)
        {
            if (links == null || candidate == null)
                return;
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Side == candidate.Side
                    && Distance(links[i].ReferenceFoot, candidate.ReferenceFoot)
                        <= GeometryTolerance
                    && Distance(links[i].PlateEdge, candidate.PlateEdge)
                        <= GeometryTolerance)
                    return;
            }
            links.Add(candidate);
        }

        private static int CompareType1BLinks(Type1BLink first, Type1BLink second)
        {
            int bySide = first.Side.CompareTo(second.Side);
            if (bySide != 0)
                return bySide;
            int byX = first.ReferenceFoot.X.CompareTo(second.ReferenceFoot.X);
            return byX != 0
                ? byX
                : first.ReferenceFoot.Y.CompareTo(second.ReferenceFoot.Y);
        }

        private static bool TryResolveC(ViewData view, out CTopology topology)
        {
            topology = null;
            NestedTopology nested;
            if (!TryResolveNested(view, out nested))
                return false;

            List<ReferenceArm> arms = new List<ReferenceArm>();
            double minimumLength = Math.Max(
                nested.Core.Bounds.Width,
                nested.Core.Bounds.Height) * 1.35;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (!part.SameMainAssembly || part.IsContourPlate
                    || SameGeometry(part.Bounds, nested.Core.Bounds))
                    continue;
                ReferenceArm arm;
                if (TryReferenceArmAtNode(
                    view,
                    part,
                    nested.Node,
                    minimumLength,
                    out arm))
                    AddUniqueArm(arms, arm);
            }

            List<CTopology> candidates = new List<CTopology>();
            for (int i = 0; i < arms.Count; i++)
            {
                P2 di = Normalize(Subtract(arms[i].Far, nested.Node));
                if (di == null || Math.Abs(di.Y) < DirectionCosineTolerance)
                    continue;
                for (int j = i + 1; j < arms.Count; j++)
                {
                    P2 dj = Normalize(Subtract(arms[j].Far, nested.Node));
                    if (dj == null || Dot(di, dj) > -DirectionCosineTolerance)
                        continue;
                    ReferenceArm upper = arms[i].Far.Y > nested.Node.Y ? arms[i] : arms[j];
                    ReferenceArm lower = Object.ReferenceEquals(upper, arms[i])
                        ? arms[j] : arms[i];
                    if (upper.Far.Y <= nested.Node.Y + GeometryTolerance
                        || lower.Far.Y >= nested.Node.Y - GeometryTolerance)
                        continue;
                    for (int h = 0; h < arms.Count; h++)
                    {
                        if (h == i || h == j)
                            continue;
                        P2 dh = Normalize(Subtract(arms[h].Far, nested.Node));
                        if (dh == null || Math.Abs(dh.X) < DirectionCosineTolerance
                            || Math.Abs(Dot(dh, di)) > 0.10)
                            continue;
                        ReferenceArm neighborUpper;
                        ReferenceArm neighborLower;
                        P2 secondary;
                        if (!TryFindSecondaryPair(
                            view,
                            nested,
                            arms[h],
                            di,
                            out neighborUpper,
                            out neighborLower,
                            out secondary))
                            continue;
                        PartData upperPlate = FindConnectedPlate(
                            view,
                            nested,
                            neighborUpper,
                            secondary,
                            true);
                        PartData lowerPlate = FindConnectedPlate(
                            view,
                            nested,
                            neighborLower,
                            secondary,
                            false);
                        if (upperPlate == null || lowerPlate == null)
                            continue;
                        CTopology item = new CTopology();
                        item.Nested = nested;
                        item.Horizontal = arms[h];
                        item.Upper = upper;
                        item.Lower = lower;
                        item.NeighborUpper = neighborUpper;
                        item.NeighborLower = neighborLower;
                        item.SecondaryNode = secondary;
                        item.UpperPlate = upperPlate;
                        item.LowerPlate = lowerPlate;
                        AddUniqueC(candidates, item);
                    }
                }
            }
            if (candidates.Count != 1)
                return false;
            topology = candidates[0];
            return true;
        }

        private static bool TryResolvePartialC(
            ViewData view,
            out PartialCTopology topology)
        {
            topology = null;
            NestedTopology nested;
            if (!TryResolveNested(view, out nested))
                return false;

            double minimumLength = Math.Max(
                nested.Core.Bounds.Width,
                nested.Core.Bounds.Height) * 1.35;
            List<ReferenceArm> horizontal = new List<ReferenceArm>();
            List<ReferenceArm> upper = new List<ReferenceArm>();
            List<ReferenceArm> lower = new List<ReferenceArm>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (!part.SameMainAssembly || part.IsContourPlate
                    || SameGeometry(part.Bounds, nested.Core.Bounds))
                    continue;
                ReferenceArm arm;
                if (!TryReferenceArmAtNode(
                    view,
                    part,
                    nested.Node,
                    minimumLength,
                    out arm))
                    continue;
                P2 direction = Normalize(Subtract(arm.Far, nested.Node));
                if (direction == null)
                    continue;
                if (Math.Abs(direction.X) >= DirectionCosineTolerance)
                    AddUniqueArm(horizontal, arm);
                else if (Math.Abs(direction.Y) >= DirectionCosineTolerance)
                {
                    if (arm.Far.Y > nested.Node.Y)
                        AddUniqueArm(upper, arm);
                    else
                        AddUniqueArm(lower, arm);
                }
            }

            PartialCTopology item = new PartialCTopology();
            item.Nested = nested;
            item.Horizontal = SelectOnlyArm(horizontal);
            item.Upper = SelectOnlyArm(upper);
            item.Lower = SelectOnlyArm(lower);
            if (item.Horizontal == null && item.Upper == null && item.Lower == null)
                return false;

            if (item.Horizontal != null)
            {
                ResolvePartialSecondaryArms(
                    view,
                    nested,
                    item.Horizontal,
                    out item.SecondaryNode,
                    out item.NeighborUpper,
                    out item.NeighborLower);
                if (item.SecondaryNode != null && item.NeighborUpper != null)
                {
                    item.UpperPlate = FindConnectedPlate(
                        view,
                        nested,
                        item.NeighborUpper,
                        item.SecondaryNode,
                        true);
                }
                if (item.SecondaryNode != null && item.NeighborLower != null)
                {
                    item.LowerPlate = FindConnectedPlate(
                        view,
                        nested,
                        item.NeighborLower,
                        item.SecondaryNode,
                        false);
                }
            }

            // A completely populated topology belongs to the unchanged full-C
            // resolver.  If that resolver rejected it, do not weaken its
            // ambiguity protection through the partial path.
            if (item.Horizontal != null
                && item.Upper != null
                && item.Lower != null
                && item.NeighborUpper != null
                && item.NeighborLower != null
                && item.UpperPlate != null
                && item.LowerPlate != null)
                return false;

            topology = item;
            return true;
        }

        private static ReferenceArm SelectOnlyArm(List<ReferenceArm> candidates)
        {
            return candidates != null && candidates.Count == 1
                ? candidates[0]
                : null;
        }

        private static void ResolvePartialSecondaryArms(
            ViewData view,
            NestedTopology nested,
            ReferenceArm horizontal,
            out P2 secondaryNode,
            out ReferenceArm upper,
            out ReferenceArm lower)
        {
            secondaryNode = null;
            upper = null;
            lower = null;
            List<ReferenceArm> candidates = new List<ReferenceArm>();
            List<P2> nodes = new List<P2>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.SameMainAssembly || part.IsContourPlate)
                    continue;
                P3 a;
                P3 b;
                if (!TryReferencePairInsideDepth(view, part, out a, out b))
                    continue;
                P2 direction = Normalize(Subtract(b.XY, a.XY));
                if (direction == null
                    || Math.Abs(direction.Y) < DirectionCosineTolerance
                    || Distance(a.XY, b.XY) < 300.0)
                    continue;

                P2 node = null;
                P2 far = null;
                if (IsSecondaryNodeCandidate(a.XY, horizontal, nested))
                {
                    node = a.XY;
                    far = b.XY;
                }
                else if (IsSecondaryNodeCandidate(b.XY, horizontal, nested))
                {
                    node = b.XY;
                    far = a.XY;
                }
                if (node == null || far == null
                    || Math.Abs(far.Y - node.Y) < 300.0)
                    continue;

                ReferenceArm arm = new ReferenceArm();
                arm.Part = part;
                arm.Node = node;
                arm.Far = far;
                arm.Depth = (a.Z + b.Z) * 0.5;
                AddUniqueArm(candidates, arm);
                AddUnique(nodes, node);
            }

            if (nodes.Count != 1)
                return;
            secondaryNode = nodes[0];
            List<ReferenceArm> upperCandidates = new List<ReferenceArm>();
            List<ReferenceArm> lowerCandidates = new List<ReferenceArm>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (Distance(candidates[i].Node, secondaryNode)
                    > GeometryTolerance * 3.0)
                    continue;
                if (candidates[i].Far.Y > secondaryNode.Y)
                    AddUniqueArm(upperCandidates, candidates[i]);
                else
                    AddUniqueArm(lowerCandidates, candidates[i]);
            }
            upper = SelectOnlyArm(upperCandidates);
            lower = SelectOnlyArm(lowerCandidates);
            if (upper == null && lower == null)
                secondaryNode = null;
        }

        private static bool IsSecondaryNodeCandidate(
            P2 point,
            ReferenceArm horizontal,
            NestedTopology nested)
        {
            return point != null
                && horizontal != null
                && nested != null
                && Math.Abs(point.Y - nested.Node.Y) <= GeometryTolerance * 3.0
                && PointOnSegment(point, horizontal.Far, nested.Node, 3.0)
                && Distance(point, nested.Node)
                    >= nested.Core.Bounds.Width * 0.35;
        }

        private static bool TryResolveD(ViewData view, out DTopology topology)
        {
            topology = null;
            List<ReferenceArm> candidates = new List<ReferenceArm>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.SameMainAssembly || part.IsContourPlate)
                    continue;
                P3 a;
                P3 b;
                if (!TryReferencePairInsideDepth(view, part, out a, out b))
                    continue;
                P2 direction = Normalize(Subtract(b.XY, a.XY));
                if (direction == null || Math.Abs(direction.X) < DirectionCosineTolerance
                    || Distance(a.XY, b.XY) < 300.0)
                    continue;
                ReferenceArm arm = new ReferenceArm();
                arm.Part = part;
                arm.Node = a.XY;
                arm.Far = b.XY;
                arm.Depth = (a.Z + b.Z) * 0.5;
                candidates.Add(arm);
            }

            List<DTopology> matches = new List<DTopology>();
            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    P2 node;
                    P2 farI;
                    P2 farJ;
                    if (!TrySharedReferenceEndpoint(
                        candidates[i], candidates[j], out node, out farI, out farJ))
                        continue;
                    P2 firstDir = Normalize(Subtract(farI, node));
                    P2 secondDir = Normalize(Subtract(farJ, node));
                    if (firstDir == null || secondDir == null
                        || Dot(firstDir, secondDir) > -DirectionCosineTolerance
                        || Math.Abs(firstDir.X) < DirectionCosineTolerance)
                        continue;

                    ReferenceArm left;
                    ReferenceArm right;
                    if (farI.X < node.X)
                    {
                        left = CloneArm(candidates[i], node, farI);
                        right = CloneArm(candidates[j], node, farJ);
                    }
                    else
                    {
                        left = CloneArm(candidates[j], node, farJ);
                        right = CloneArm(candidates[i], node, farI);
                    }
                    if (left.Part.Bounds.MaxX >= node.X - 1.0
                        || right.Part.Bounds.MinX <= node.X + 1.0)
                        continue;
                    double leftCenterY = left.Part.Bounds.Center.Y;
                    double rightCenterY = right.Part.Bounds.Center.Y;
                    double interior = ((leftCenterY + rightCenterY) * 0.5) - node.Y;
                    if (Math.Abs(interior) <= GeometryTolerance
                        || Math.Sign(leftCenterY - node.Y)
                            != Math.Sign(rightCenterY - node.Y))
                        continue;

                    double refDepth = (left.Depth + right.Depth) * 0.5;
                    PartData core = FindDepthCore(view, node, refDepth, left, right);
                    if (core == null)
                        continue;
                    P3 leftHole;
                    P3 rightHole;
                    if (!TryNearestConnectionHole(
                        view,
                        left,
                        node,
                        out leftHole)
                        || !TryNearestConnectionHole(
                            view,
                            right,
                            node,
                            out rightHole))
                        continue;
                    DTopology item = new DTopology();
                    item.Core = core;
                    item.Left = left;
                    item.Right = right;
                    item.Node = node;
                    item.LeftHole = leftHole;
                    item.RightHole = rightHole;
                    item.InteriorSignY = Math.Sign(interior);
                    AddUniqueD(matches, item);
                }
            }
            if (matches.Count != 1)
                return false;
            topology = matches[0];
            return true;
        }

        private static bool TryResolvePartialD(
            ViewData view,
            out PartialDTopology topology)
        {
            topology = null;

            // A nested diaphragm belongs to B/C.  Do not let an unrelated
            // external horizontal member steal that section as partial D.
            NestedTopology nested;
            if (TryResolveNested(view, out nested))
                return false;

            List<PartialDArmCandidate> candidates =
                new List<PartialDArmCandidate>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.SameMainAssembly || part.IsContourPlate)
                    continue;
                P3 a;
                P3 b;
                if (!TryReferencePairInsideDepth(view, part, out a, out b))
                    continue;
                P2 direction = Normalize(Subtract(b.XY, a.XY));
                if (direction == null
                    || Math.Abs(direction.X) < DirectionCosineTolerance
                    || Distance(a.XY, b.XY) < 300.0)
                    continue;
                TryAddPartialDArmCandidate(view, part, a, b, candidates);
                TryAddPartialDArmCandidate(view, part, b, a, candidates);
            }

            List<PartialDTopology> groups = new List<PartialDTopology>();
            for (int i = 0; i < candidates.Count; i++)
                MergePartialDArm(groups, candidates[i]);
            PartialDTopology selected = null;
            for (int i = 0; i < groups.Count; i++)
            {
                PartialDTopology item = groups[i];
                if (item.Ambiguous || (item.Left == null && item.Right == null))
                    continue;
                if (selected != null)
                    return false;
                selected = item;
            }
            if (selected == null)
                return false;

            // Both complete sides are owned by the unchanged full-D resolver.
            // If it rejected them, keep the ambiguity rejection intact.
            if (selected.Left != null && selected.Right != null
                && selected.LeftHole != null && selected.RightHole != null)
                return false;
            topology = selected;
            return true;
        }

        private static void TryAddPartialDArmCandidate(
            ViewData view,
            PartData part,
            P3 nodePoint,
            P3 farPoint,
            List<PartialDArmCandidate> target)
        {
            if (nodePoint == null || farPoint == null || target == null)
                return;
            P2 direction = Normalize(Subtract(farPoint.XY, nodePoint.XY));
            if (direction == null || Math.Abs(direction.X) < DirectionCosineTolerance)
                return;
            bool isLeft = farPoint.X < nodePoint.X;
            if (isLeft)
            {
                if (part.Bounds.MaxX >= nodePoint.X - 1.0)
                    return;
            }
            else if (part.Bounds.MinX <= nodePoint.X + 1.0)
                return;

            double depth = (nodePoint.Z + farPoint.Z) * 0.5;
            PartData core = FindDepthCoreForSingleArm(
                view,
                nodePoint.XY,
                depth,
                part,
                isLeft);
            if (core == null)
                return;
            double interior = part.Bounds.Center.Y - nodePoint.Y;
            if (Math.Abs(interior) <= GeometryTolerance)
                return;

            ReferenceArm arm = new ReferenceArm();
            arm.Part = part;
            arm.Node = nodePoint.XY;
            arm.Far = farPoint.XY;
            arm.Depth = depth;
            P3 hole;
            if (!TryNearestConnectionHole(view, arm, nodePoint.XY, out hole))
                hole = null;

            PartialDArmCandidate candidate = new PartialDArmCandidate();
            candidate.Core = core;
            candidate.Arm = arm;
            candidate.Node = nodePoint.XY;
            candidate.Hole = hole;
            candidate.IsLeft = isLeft;
            candidate.InteriorSignY = Math.Sign(interior);
            target.Add(candidate);
        }

        private static PartData FindDepthCoreForSingleArm(
            ViewData view,
            P2 node,
            double referenceDepth,
            PartData armPart,
            bool armIsLeft)
        {
            PartData best = null;
            double bestScore = Double.PositiveInfinity;
            bool ambiguous = false;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (!part.SameMainAssembly || part.IsContourPlate
                    || !part.Bounds.IsValid)
                    continue;
                P3 collapsed;
                if (!TryCollapsedReferencePointAtDepth(
                    part,
                    referenceDepth,
                    out collapsed)
                    || Distance(collapsed.XY, node) > GeometryTolerance * 3.0)
                    continue;
                if (node.X < part.Bounds.MinX - GeometryTolerance
                    || node.X > part.Bounds.MaxX + GeometryTolerance)
                    continue;
                double edgeDistance = Math.Min(
                    Math.Abs(node.Y - part.Bounds.MinY),
                    Math.Abs(node.Y - part.Bounds.MaxY));
                if (edgeDistance > GeometryTolerance * 3.0)
                    continue;
                double sideGap = armIsLeft
                    ? part.Bounds.MinX - armPart.Bounds.MaxX
                    : armPart.Bounds.MinX - part.Bounds.MaxX;
                if (sideGap < -GeometryTolerance || sideGap > 100.0)
                    continue;
                double score = edgeDistance
                    + Math.Abs(part.Bounds.Center.X - node.X)
                    + Math.Abs(sideGap) * 0.1;
                if (score < bestScore - GeometryTolerance)
                {
                    best = part;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (Math.Abs(score - bestScore) <= GeometryTolerance
                    && best != null && !SameGeometry(best.Bounds, part.Bounds))
                    ambiguous = true;
            }
            return ambiguous ? null : best;
        }

        private static void MergePartialDArm(
            List<PartialDTopology> groups,
            PartialDArmCandidate candidate)
        {
            if (candidate == null || candidate.Core == null || candidate.Arm == null)
                return;
            PartialDTopology group = null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Core.Identifier == candidate.Core.Identifier
                    && Distance(groups[i].Node, candidate.Node)
                        <= GeometryTolerance * 3.0
                    && Math.Sign(groups[i].InteriorSignY)
                        == Math.Sign(candidate.InteriorSignY)
                    && Math.Abs(groups[i].ReferenceDepth - candidate.Arm.Depth)
                        <= ReferenceDepthTolerance)
                {
                    group = groups[i];
                    break;
                }
            }
            if (group == null)
            {
                group = new PartialDTopology();
                group.Core = candidate.Core;
                group.Node = candidate.Node;
                group.InteriorSignY = candidate.InteriorSignY;
                group.ReferenceDepth = candidate.Arm.Depth;
                groups.Add(group);
            }

            ReferenceArm existing = candidate.IsLeft ? group.Left : group.Right;
            if (existing != null
                && existing.Part.Identifier != candidate.Arm.Part.Identifier)
            {
                group.Ambiguous = true;
                return;
            }
            if (candidate.IsLeft)
            {
                group.Left = candidate.Arm;
                group.LeftHole = candidate.Hole;
            }
            else
            {
                group.Right = candidate.Arm;
                group.RightHole = candidate.Hole;
            }
        }

        private static bool TryFindSecondaryPair(
            ViewData view,
            NestedTopology nested,
            ReferenceArm horizontal,
            P2 verticalDirection,
            out ReferenceArm upper,
            out ReferenceArm lower,
            out P2 secondary)
        {
            upper = null;
            lower = null;
            secondary = null;
            List<Tuple<ReferenceArm, ReferenceArm, P2>> matches =
                new List<Tuple<ReferenceArm, ReferenceArm, P2>>();
            List<ReferenceArm> arms = new List<ReferenceArm>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part.SameMainAssembly || part.IsContourPlate)
                    continue;
                P3 a;
                P3 b;
                if (!TryReferencePairInsideDepth(view, part, out a, out b))
                    continue;
                P2 direction = Normalize(Subtract(b.XY, a.XY));
                if (direction == null
                    || Math.Abs(Dot(direction, verticalDirection))
                        < DirectionCosineTolerance
                    || Distance(a.XY, b.XY) < 300.0)
                    continue;
                ReferenceArm arm = new ReferenceArm();
                arm.Part = part;
                arm.Node = a.XY;
                arm.Far = b.XY;
                arm.Depth = (a.Z + b.Z) * 0.5;
                arms.Add(arm);
            }
            for (int i = 0; i < arms.Count; i++)
            {
                for (int j = i + 1; j < arms.Count; j++)
                {
                    P2 node;
                    P2 farI;
                    P2 farJ;
                    if (!TrySharedReferenceEndpoint(
                        arms[i], arms[j], out node, out farI, out farJ))
                        continue;
                    P2 di = Normalize(Subtract(farI, node));
                    P2 dj = Normalize(Subtract(farJ, node));
                    if (di == null || dj == null
                        || Dot(di, dj) > -DirectionCosineTolerance)
                        continue;
                    if (Math.Abs(node.Y - nested.Node.Y) > GeometryTolerance * 3.0
                        || !PointOnSegment(node, horizontal.Far, nested.Node, 3.0)
                        || Distance(node, nested.Node)
                            < nested.Core.Bounds.Width * 0.35)
                        continue;
                    ReferenceArm top;
                    ReferenceArm bottom;
                    if (farI.Y > node.Y)
                    {
                        top = CloneArm(arms[i], node, farI);
                        bottom = CloneArm(arms[j], node, farJ);
                    }
                    else
                    {
                        top = CloneArm(arms[j], node, farJ);
                        bottom = CloneArm(arms[i], node, farI);
                    }
                    matches.Add(Tuple.Create(top, bottom, node));
                }
            }
            if (matches.Count != 1)
                return false;
            upper = matches[0].Item1;
            lower = matches[0].Item2;
            secondary = matches[0].Item3;
            return true;
        }

        private static PartData FindConnectedPlate(
            ViewData view,
            NestedTopology nested,
            ReferenceArm neighbor,
            P2 node,
            bool upper)
        {
            PartData best = null;
            double bestScore = Double.PositiveInfinity;
            bool ambiguous = false;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (!part.SameMainAssembly || !part.IsContourPlate
                    || Object.ReferenceEquals(part, nested.Outer)
                    || !SharesBolt(part, neighbor.Part))
                    continue;
                if (upper)
                {
                    if (part.Bounds.MaxY <= node.Y + GeometryTolerance)
                        continue;
                }
                else if (part.Bounds.MinY >= node.Y - GeometryTolerance)
                    continue;
                double dx = node.X < part.Bounds.MinX
                    ? part.Bounds.MinX - node.X
                    : node.X > part.Bounds.MaxX
                        ? node.X - part.Bounds.MaxX
                        : 0.0;
                if (dx > 25.0)
                    continue;
                double dy = upper
                    ? Math.Abs(part.Bounds.MinY - node.Y)
                    : Math.Abs(part.Bounds.MaxY - node.Y);
                double score = dx * 10.0 + dy;
                if (score < bestScore - GeometryTolerance)
                {
                    best = part;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (Math.Abs(score - bestScore) <= GeometryTolerance
                    && best != null && !SameGeometry(best.Bounds, part.Bounds))
                    ambiguous = true;
            }
            return ambiguous ? null : best;
        }

        private static PartData FindDepthCore(
            ViewData view,
            P2 node,
            double referenceDepth,
            ReferenceArm left,
            ReferenceArm right)
        {
            PartData best = null;
            double bestScore = Double.PositiveInfinity;
            bool ambiguous = false;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (!part.SameMainAssembly || part.IsContourPlate
                    || !part.Bounds.IsValid)
                    continue;
                P3 collapsed;
                if (!TryCollapsedReferencePointAtDepth(
                    part,
                    referenceDepth,
                    out collapsed)
                    || Distance(collapsed.XY, node) > GeometryTolerance * 3.0)
                    continue;
                if (part.Bounds.MinX < left.Part.Bounds.MaxX - GeometryTolerance
                    || part.Bounds.MaxX > right.Part.Bounds.MinX + GeometryTolerance)
                    continue;
                if (node.X < part.Bounds.MinX - GeometryTolerance
                    || node.X > part.Bounds.MaxX + GeometryTolerance)
                    continue;
                double edgeDistance = Math.Min(
                    Math.Abs(node.Y - part.Bounds.MinY),
                    Math.Abs(node.Y - part.Bounds.MaxY));
                if (edgeDistance > GeometryTolerance * 3.0)
                    continue;
                double score = edgeDistance + Math.Abs(part.Bounds.Center.X - node.X);
                if (score < bestScore - GeometryTolerance)
                {
                    best = part;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (Math.Abs(score - bestScore) <= GeometryTolerance
                    && best != null && !SameGeometry(best.Bounds, part.Bounds))
                    ambiguous = true;
            }
            return ambiguous ? null : best;
        }

        private static bool TryNearestConnectionHole(
            ViewData view,
            ReferenceArm arm,
            P2 node,
            out P3 hole)
        {
            hole = null;
            P2 axis = Normalize(Subtract(arm.Far, node));
            if (axis == null)
                return false;
            P2 normal = new P2(-axis.Y, axis.X);
            double depth = arm.Part.Bounds.Height;
            double best = Double.PositiveInfinity;
            bool ambiguous = false;
            foreach (KeyValuePair<int, BoltData> pair in arm.Part.Bolts)
            {
                BoltData group = pair.Value;
                for (int i = 0; i < group.Points.Count; i++)
                {
                    P3 point = group.Points[i];
                    if (!DepthContains(view, point.Z))
                        continue;
                    P2 relative = Subtract(point.XY, node);
                    double along = Dot(relative, axis);
                    double transverse = Math.Abs(Dot(relative, normal));
                    if (along <= GeometryTolerance
                        || transverse < Math.Max(5.0, depth * 0.15))
                        continue;
                    if (along < best - GeometryTolerance)
                    {
                        best = along;
                        hole = point;
                        ambiguous = false;
                    }
                    else if (Math.Abs(along - best) <= GeometryTolerance
                        && hole != null
                        && Distance(point.XY, hole.XY) > GeometryTolerance)
                        ambiguous = true;
                }
            }
            return hole != null && !ambiguous;
        }

        private static List<DimPlan> BuildBPlans(
            ViewData view,
            NestedTopology topology)
        {
            List<DimPlan> result = new List<DimPlan>();
            Bounds2 core = topology.Core.Bounds;
            Bounds2 outer = topology.Outer.Bounds;
            result.Add(Plan(view, "B-01", "B", 58.20, 0, 1,
                Pt(outer.MinX, outer.MaxY), Pt(outer.MaxX, outer.MaxY)));
            result.Add(Plan(view, "B-02", "B", 48.65, 0, 1,
                Pt(outer.MinX, outer.MaxY), Pt(core.MinX, core.MaxY),
                Pt(core.MaxX, core.MaxY), Pt(outer.MaxX, outer.MaxY)));
            result.Add(Plan(view, "B-03", "B", 23.90, 1, 0,
                Pt(outer.MaxX, outer.MinY), Pt(outer.MaxX, outer.MaxY)));
            result.Add(Plan(view, "B-04", "B", 14.80, 1, 0,
                Pt(outer.MaxX, outer.MinY), Pt(core.MaxX, core.MinY),
                Pt(core.MaxX, core.MaxY), Pt(outer.MaxX, outer.MaxY)));
            return result;
        }

        private static List<DimPlan> BuildType1BPlans(
            ViewData view,
            Type1BTopology topology)
        {
            List<DimPlan> result = new List<DimPlan>();
            for (int i = 0; topology != null && i < topology.Links.Count; i++)
            {
                Type1BLink link = topology.Links[i];
                P2 direction = Type1BSideDirection(link.Side);
                string side = Type1BSideName(link.Side);
                string prefix = "B1-" + side + "-"
                    + (i + 1).ToString("00", CultureInfo.InvariantCulture);

                // Main true boundary -> neighbor REF -> opposite true boundary.
                // The REF point intentionally retains its actual perpendicular
                // coordinate; it is not projected onto a fabricated box edge.
                result.Add(Plan(
                    view,
                    prefix + "-MAIN-REF-CHAIN",
                    "B1-CARDINAL-LINKS",
                    Type1BBoundaryPaperDistance(link.Side),
                    direction.X,
                    direction.Y,
                    link.MainBoundaryFirst,
                    link.ReferenceFoot,
                    link.MainBoundarySecond));

                // Exact far plate vertex -> exact neighbor reference-axis foot.
                // Placing from the plate keeps a constant paper-space clearance
                // beyond the outer end even when the connection length changes.
                result.Add(Plan(
                    view,
                    prefix + "-PLATE-REF",
                    "B1-CARDINAL-LINKS",
                    20.0,
                    direction.X,
                    direction.Y,
                    link.PlateEdge,
                    link.ReferenceFoot));
            }
            return result;
        }

        private static P2 Type1BSideDirection(Type1BLinkSide side)
        {
            switch (side)
            {
                case Type1BLinkSide.Upper:
                    return new P2(0.0, 1.0);
                case Type1BLinkSide.Lower:
                    return new P2(0.0, -1.0);
                case Type1BLinkSide.Left:
                    return new P2(-1.0, 0.0);
                default:
                    return new P2(1.0, 0.0);
            }
        }

        private static double Type1BBoundaryPaperDistance(Type1BLinkSide side)
        {
            return side == Type1BLinkSide.Left || side == Type1BLinkSide.Right
                ? 37.55
                : 36.0;
        }

        private static string Type1BSideName(Type1BLinkSide side)
        {
            switch (side)
            {
                case Type1BLinkSide.Upper:
                    return "UPPER";
                case Type1BLinkSide.Lower:
                    return "LOWER";
                case Type1BLinkSide.Left:
                    return "LEFT";
                default:
                    return "RIGHT";
            }
        }

        private static string DescribeType1B(Type1BTopology topology)
        {
            StringBuilder text = new StringBuilder("B1-CARDINAL");
            for (int i = 0; topology != null && i < topology.Links.Count; i++)
                text.Append('-').Append(Type1BSideName(topology.Links[i].Side));
            return text.ToString();
        }

        private static List<DimPlan> BuildCPlans(ViewData view, CTopology topology)
        {
            List<DimPlan> result = new List<DimPlan>();
            Bounds2 core = topology.Nested.Core.Bounds;
            Bounds2 outer = topology.Nested.Outer.Bounds;
            P2 node = topology.Nested.Node;
            bool armLeft = topology.Horizontal.Far.X < node.X;
            double armFarX = armLeft
                ? topology.Horizontal.Part.Bounds.MinX
                : topology.Horizontal.Part.Bounds.MaxX;
            double armSideOuterX = armLeft ? outer.MinX : outer.MaxX;
            double oppositeOuterX = armLeft ? outer.MaxX : outer.MinX;
            double oppositeCoreX = armLeft ? core.MaxX : core.MinX;
            double verticalSideX = armLeft
                ? Math.Max(
                    topology.Upper.Part.Bounds.MaxX,
                    topology.Lower.Part.Bounds.MaxX)
                : Math.Min(
                    topology.Upper.Part.Bounds.MinX,
                    topology.Lower.Part.Bounds.MinX);
            double upperFarY = topology.Upper.Part.Bounds.MaxY;
            double lowerFarY = topology.Lower.Part.Bounds.MinY;
            double plateX = armLeft
                ? topology.UpperPlate.Bounds.MinX
                : topology.UpperPlate.Bounds.MaxX;
            double lowerPlateX = armLeft
                ? topology.LowerPlate.Bounds.MinX
                : topology.LowerPlate.Bounds.MaxX;
            double armDirection = armLeft ? -1.0 : 1.0;
            double oppositeDirection = -armDirection;

            result.Add(Plan(view, "C-01", "C", 79.70, 0, 1,
                Pt(outer.MinX, outer.MaxY), Pt(outer.MaxX, outer.MaxY)));
            result.Add(Plan(view, "C-02", "C", 69.70, 0, 1,
                Pt(outer.MinX, outer.MaxY), Pt(core.MinX, core.MaxY),
                Pt(core.MaxX, core.MaxY), Pt(outer.MaxX, outer.MaxY)));
            result.Add(Plan(view, "C-03", "C", 34.15, armDirection, 0,
                Pt(armSideOuterX, outer.MinY), Pt(armFarX, node.Y),
                Pt(armSideOuterX, outer.MaxY)));
            result.Add(Plan(view, "C-04", "C", 60.60, 0, -1,
                Pt(armFarX, topology.Horizontal.Part.Bounds.MinY),
                topology.SecondaryNode, node));
            result.Add(Plan(view, "C-05", "C", 60.95, 0, 1,
                Pt(core.MinX, core.MaxY), Pt(node.X, upperFarY),
                Pt(core.MaxX, core.MaxY)));
            result.Add(Plan(view, "C-06", "C", 69.55, 0, -1,
                Pt(core.MinX, core.MinY), Pt(node.X, lowerFarY),
                Pt(core.MaxX, core.MinY)));
            result.Add(Plan(view, "C-07", "C", 37.55, oppositeDirection, 0,
                Pt(verticalSideX, lowerFarY), node,
                Pt(verticalSideX, upperFarY)));
            result.Add(Plan(view, "C-08", "C", 91.65, 0, 1,
                Pt(armFarX, topology.Horizontal.Part.Bounds.MaxY), node));
            result.Add(Plan(view, "C-09", "C", 34.90, 0, 1,
                topology.SecondaryNode,
                Pt(plateX, topology.UpperPlate.Bounds.MaxY)));
            result.Add(Plan(view, "C-10", "C", 33.30, 0, -1,
                topology.SecondaryNode,
                Pt(lowerPlateX, topology.LowerPlate.Bounds.MinY)));
            result.Add(Plan(view, "C-11", "C", 25.30, oppositeDirection, 0,
                Pt(oppositeOuterX, outer.MinY),
                Pt(oppositeOuterX, outer.MaxY)));
            result.Add(Plan(view, "C-12", "C", 15.90, oppositeDirection, 0,
                Pt(oppositeOuterX, outer.MinY),
                Pt(oppositeCoreX, core.MinY),
                Pt(oppositeCoreX, core.MaxY),
                Pt(oppositeOuterX, outer.MaxY)));
            result.Add(Plan(view, "C-13", "C", 50.70, 0, 1,
                Pt(armFarX, topology.Horizontal.Part.Bounds.MaxY),
                topology.SecondaryNode, node));
            return result;
        }

        private static List<DimPlan> BuildPartialCPlans(
            ViewData view,
            PartialCTopology topology)
        {
            List<DimPlan> result = BuildPartialCNestedPlans(view, topology);
            Bounds2 core = topology.Nested.Core.Bounds;
            Bounds2 outer = topology.Nested.Outer.Bounds;
            P2 node = topology.Nested.Node;

            bool hasHorizontal = topology.Horizontal != null;
            bool armLeft = hasHorizontal && topology.Horizontal.Far.X < node.X;
            double armDirection = armLeft ? -1.0 : 1.0;
            double oppositeDirection = -armDirection;
            double armFarX = 0.0;
            double armSideOuterX = 0.0;
            if (hasHorizontal)
            {
                armFarX = armLeft
                    ? topology.Horizontal.Part.Bounds.MinX
                    : topology.Horizontal.Part.Bounds.MaxX;
                armSideOuterX = armLeft ? outer.MinX : outer.MaxX;
                result.Add(Plan(view, "C-03", "C-PARTIAL", 34.15,
                    armDirection, 0,
                    Pt(armSideOuterX, outer.MinY), Pt(armFarX, node.Y),
                    Pt(armSideOuterX, outer.MaxY)));
            }

            if (hasHorizontal && topology.NeighborLower != null
                && topology.SecondaryNode != null)
            {
                result.Add(Plan(view, "C-04", "C-PARTIAL", 60.60, 0, -1,
                    Pt(armFarX, topology.Horizontal.Part.Bounds.MinY),
                    topology.SecondaryNode, node));
            }
            if (topology.Upper != null)
            {
                result.Add(Plan(view, "C-05", "C-PARTIAL", 60.95, 0, 1,
                    Pt(core.MinX, core.MaxY),
                    Pt(node.X, topology.Upper.Part.Bounds.MaxY),
                    Pt(core.MaxX, core.MaxY)));
            }
            if (topology.Lower != null)
            {
                result.Add(Plan(view, "C-06", "C-PARTIAL", 69.55, 0, -1,
                    Pt(core.MinX, core.MinY),
                    Pt(node.X, topology.Lower.Part.Bounds.MinY),
                    Pt(core.MaxX, core.MinY)));
            }
            if (hasHorizontal && (topology.Upper != null || topology.Lower != null))
            {
                List<P2> points = new List<P2>();
                double verticalSideX;
                if (armLeft)
                {
                    verticalSideX = topology.Upper != null
                        ? topology.Upper.Part.Bounds.MaxX
                        : topology.Lower.Part.Bounds.MaxX;
                    if (topology.Lower != null)
                        verticalSideX = Math.Max(
                            verticalSideX,
                            topology.Lower.Part.Bounds.MaxX);
                }
                else
                {
                    verticalSideX = topology.Upper != null
                        ? topology.Upper.Part.Bounds.MinX
                        : topology.Lower.Part.Bounds.MinX;
                    if (topology.Lower != null)
                        verticalSideX = Math.Min(
                            verticalSideX,
                            topology.Lower.Part.Bounds.MinX);
                }
                if (topology.Lower != null)
                    points.Add(Pt(verticalSideX, topology.Lower.Part.Bounds.MinY));
                points.Add(node);
                if (topology.Upper != null)
                    points.Add(Pt(verticalSideX, topology.Upper.Part.Bounds.MaxY));
                result.Add(PlanFromPoints(
                    view,
                    "C-07",
                    "C-PARTIAL",
                    37.55,
                    new P2(oppositeDirection, 0.0),
                    points));
            }
            if (hasHorizontal)
            {
                result.Add(Plan(view, "C-08", "C-PARTIAL", 91.65, 0, 1,
                    Pt(armFarX, topology.Horizontal.Part.Bounds.MaxY), node));
            }
            if (topology.UpperPlate != null && topology.SecondaryNode != null)
            {
                double plateX = armLeft
                    ? topology.UpperPlate.Bounds.MinX
                    : topology.UpperPlate.Bounds.MaxX;
                result.Add(Plan(view, "C-09", "C-PARTIAL", 34.90, 0, 1,
                    topology.SecondaryNode,
                    Pt(plateX, topology.UpperPlate.Bounds.MaxY)));
            }
            if (topology.LowerPlate != null && topology.SecondaryNode != null)
            {
                double plateX = armLeft
                    ? topology.LowerPlate.Bounds.MinX
                    : topology.LowerPlate.Bounds.MaxX;
                result.Add(Plan(view, "C-10", "C-PARTIAL", 33.30, 0, -1,
                    topology.SecondaryNode,
                    Pt(plateX, topology.LowerPlate.Bounds.MinY)));
            }
            if (hasHorizontal && topology.NeighborUpper != null
                && topology.SecondaryNode != null)
            {
                result.Add(Plan(view, "C-13", "C-PARTIAL", 50.70, 0, 1,
                    Pt(armFarX, topology.Horizontal.Part.Bounds.MaxY),
                    topology.SecondaryNode, node));
            }
            return result;
        }

        private static List<DimPlan> BuildPartialCNestedPlans(
            ViewData view,
            PartialCTopology topology)
        {
            if (topology.Horizontal == null)
                return BuildBPlans(view, topology.Nested);

            List<DimPlan> result = new List<DimPlan>();
            Bounds2 core = topology.Nested.Core.Bounds;
            Bounds2 outer = topology.Nested.Outer.Bounds;
            bool armLeft = topology.Horizontal.Far.X < topology.Nested.Node.X;

            ExistingDimension widthTotal;
            ExistingDimension widthChain;
            if (TryFindExistingNestedWidthPair(
                view,
                core,
                outer,
                out widthTotal,
                out widthChain))
            {
                result.Add(PlanFromExisting(
                    view, "C-01", "C-PARTIAL", 79.70, widthTotal));
                result.Add(PlanFromExisting(
                    view, "C-02", "C-PARTIAL", 69.70, widthChain));
            }
            else
            {
                result.Add(Plan(view, "C-01", "C-PARTIAL", 79.70, 0, 1,
                    Pt(outer.MinX, outer.MaxY), Pt(outer.MaxX, outer.MaxY)));
                result.Add(Plan(view, "C-02", "C-PARTIAL", 69.70, 0, 1,
                    Pt(outer.MinX, outer.MaxY), Pt(core.MinX, core.MaxY),
                    Pt(core.MaxX, core.MaxY), Pt(outer.MaxX, outer.MaxY)));
            }

            double outerX = armLeft ? outer.MaxX : outer.MinX;
            double coreX = armLeft ? core.MaxX : core.MinX;
            double direction = armLeft ? 1.0 : -1.0;
            ExistingDimension heightTotal;
            ExistingDimension heightChain;
            if (TryFindExistingNestedHeightPair(
                view,
                core,
                outer,
                outerX,
                coreX,
                out heightTotal,
                out heightChain))
            {
                result.Add(PlanFromExisting(
                    view, "C-11", "C-PARTIAL", 25.30, heightTotal));
                result.Add(PlanFromExisting(
                    view, "C-12", "C-PARTIAL", 15.90, heightChain));
            }
            else
            {
                result.Add(Plan(view, "C-11", "C-PARTIAL", 25.30,
                    direction, 0,
                    Pt(outerX, outer.MinY), Pt(outerX, outer.MaxY)));
                result.Add(Plan(view, "C-12", "C-PARTIAL", 15.90,
                    direction, 0,
                    Pt(outerX, outer.MinY), Pt(coreX, core.MinY),
                    Pt(coreX, core.MaxY), Pt(outerX, outer.MaxY)));
            }
            return result;
        }

        private static bool TryFindExistingNestedWidthPair(
            ViewData view,
            Bounds2 core,
            Bounds2 outer,
            out ExistingDimension total,
            out ExistingDimension chain)
        {
            total = null;
            chain = null;
            ExistingDimension selectedTotal = null;
            ExistingDimension selectedChain = null;
            int matches = 0;
            double[] sides = new double[] { outer.MaxY, outer.MinY };
            for (int i = 0; i < sides.Length; i++)
            {
                double outerY = sides[i];
                double coreY = i == 0 ? core.MaxY : core.MinY;
                ExistingDimension candidateTotal = FindExistingDimension(
                    view,
                    NewPoints(Pt(outer.MinX, outerY), Pt(outer.MaxX, outerY)),
                    false);
                ExistingDimension candidateChain = FindExistingDimension(
                    view,
                    NewPoints(
                        Pt(outer.MinX, outerY), Pt(core.MinX, coreY),
                        Pt(core.MaxX, coreY), Pt(outer.MaxX, outerY)),
                    false);
                if (candidateTotal == null || candidateChain == null
                    || !DirectionsMatch(
                        candidateTotal.Direction,
                        candidateChain.Direction))
                    continue;
                matches++;
                selectedTotal = candidateTotal;
                selectedChain = candidateChain;
            }
            if (matches != 1)
                return false;
            total = selectedTotal;
            chain = selectedChain;
            return true;
        }

        private static bool TryFindExistingNestedHeightPair(
            ViewData view,
            Bounds2 core,
            Bounds2 outer,
            double outerX,
            double coreX,
            out ExistingDimension total,
            out ExistingDimension chain)
        {
            total = FindExistingDimension(
                view,
                NewPoints(Pt(outerX, outer.MinY), Pt(outerX, outer.MaxY)),
                true);
            chain = FindExistingDimension(
                view,
                NewPoints(
                    Pt(outerX, outer.MinY), Pt(coreX, core.MinY),
                    Pt(coreX, core.MaxY), Pt(outerX, outer.MaxY)),
                true);
            return total != null && chain != null
                && DirectionsMatch(total.Direction, chain.Direction);
        }

        private static ExistingDimension FindExistingDimension(
            ViewData view,
            List<P2> points,
            bool horizontalDirection)
        {
            ExistingDimension match = null;
            for (int i = 0; view != null && i < view.Dimensions.Count; i++)
            {
                ExistingDimension item = view.Dimensions[i];
                P2 direction = Normalize(item.Direction);
                if (direction == null
                    || (horizontalDirection
                        ? Math.Abs(direction.X) < DirectionCosineTolerance
                        : Math.Abs(direction.Y) < DirectionCosineTolerance)
                    || !PointChainsMatch(item.Points, points, MatchTolerance))
                    continue;
                if (match != null)
                    return null;
                match = item;
            }
            return match;
        }

        private static DimPlan PlanFromExisting(
            ViewData view,
            string name,
            string topology,
            double paperDistance,
            ExistingDimension existing)
        {
            return PlanFromPoints(
                view,
                name,
                topology,
                paperDistance,
                existing.Direction,
                existing.Points);
        }

        private static DimPlan PlanFromPoints(
            ViewData view,
            string name,
            string topology,
            double paperDistance,
            P2 direction,
            List<P2> points)
        {
            DimPlan result = new DimPlan();
            result.View = view;
            result.Name = name;
            result.Topology = topology;
            result.PaperDistance = paperDistance;
            result.Direction = Normalize(direction);
            for (int i = 0; points != null && i < points.Count; i++)
                result.Points.Add(Clone(points[i]));
            return result;
        }

        private static List<P2> NewPoints(params P2[] points)
        {
            List<P2> result = new List<P2>();
            for (int i = 0; points != null && i < points.Length; i++)
                result.Add(points[i]);
            return result;
        }

        private static List<DimPlan> BuildDPlans(ViewData view, DTopology topology)
        {
            List<DimPlan> result = new List<DimPlan>();
            double farY = topology.InteriorSignY < 0.0
                ? Math.Min(topology.Left.Part.Bounds.MinY,
                    topology.Right.Part.Bounds.MinY)
                : Math.Max(topology.Left.Part.Bounds.MaxY,
                    topology.Right.Part.Bounds.MaxY);
            double topDirection = -topology.InteriorSignY;
            double leftInnerX = topology.Left.Part.Bounds.MaxX;
            double rightInnerX = topology.Right.Part.Bounds.MinX;
            double leftCoreX = topology.Core.Bounds.MinX;
            double rightCoreX = topology.Core.Bounds.MaxX;

            result.Add(Plan(view, "D-01", "D", 30.30, 1, 0,
                Pt(rightInnerX, farY), Pt(rightInnerX, topology.Node.Y)));
            result.Add(Plan(view, "D-02", "D", 18.25, 1, 0,
                topology.RightHole.XY, Pt(rightCoreX, topology.Node.Y)));
            result.Add(Plan(view, "D-03", "D", 12.90, 0, topDirection,
                topology.LeftHole.XY, topology.Node, topology.RightHole.XY));
            result.Add(Plan(view, "D-04", "D", 27.30, -1, 0,
                Pt(leftInnerX, farY), Pt(leftInnerX, topology.Node.Y)));
            result.Add(Plan(view, "D-05", "D", 16.40, -1, 0,
                topology.LeftHole.XY, Pt(leftCoreX, topology.Node.Y)));
            return result;
        }

        private static List<DimPlan> BuildPartialDPlans(
            ViewData view,
            PartialDTopology topology)
        {
            List<DimPlan> result = new List<DimPlan>();
            double topDirection = -topology.InteriorSignY;
            if (topology.Right != null)
            {
                double farY = topology.InteriorSignY < 0.0
                    ? topology.Right.Part.Bounds.MinY
                    : topology.Right.Part.Bounds.MaxY;
                double innerX = topology.Right.Part.Bounds.MinX;
                result.Add(Plan(view, "D-01", "D-PARTIAL", 30.30, 1, 0,
                    Pt(innerX, farY), Pt(innerX, topology.Node.Y)));
                if (topology.RightHole != null)
                {
                    result.Add(Plan(view, "D-02", "D-PARTIAL", 18.25, 1, 0,
                        topology.RightHole.XY,
                        Pt(topology.Core.Bounds.MaxX, topology.Node.Y)));
                }
            }
            if (topology.Left != null)
            {
                double farY = topology.InteriorSignY < 0.0
                    ? topology.Left.Part.Bounds.MinY
                    : topology.Left.Part.Bounds.MaxY;
                double innerX = topology.Left.Part.Bounds.MaxX;
                result.Add(Plan(view, "D-04", "D-PARTIAL", 27.30, -1, 0,
                    Pt(innerX, farY), Pt(innerX, topology.Node.Y)));
                if (topology.LeftHole != null)
                {
                    result.Add(Plan(view, "D-05", "D-PARTIAL", 16.40, -1, 0,
                        topology.LeftHole.XY,
                        Pt(topology.Core.Bounds.MinX, topology.Node.Y)));
                }
            }
            if (topology.LeftHole != null && topology.RightHole != null)
            {
                result.Add(Plan(view, "D-03", "D-PARTIAL", 12.90,
                    0, topDirection,
                    topology.LeftHole.XY, topology.Node, topology.RightHole.XY));
            }
            return result;
        }

        private static string DescribePartialC(PartialCTopology topology)
        {
            StringBuilder text = new StringBuilder("C-PARTIAL");
            if (topology.Horizontal != null) text.Append("-H");
            if (topology.Upper != null) text.Append("-U");
            if (topology.Lower != null) text.Append("-L");
            if (topology.NeighborUpper != null) text.Append("-NU");
            if (topology.NeighborLower != null) text.Append("-NL");
            if (topology.UpperPlate != null) text.Append("-PU");
            if (topology.LowerPlate != null) text.Append("-PL");
            return text.ToString();
        }

        private static string DescribePartialD(PartialDTopology topology)
        {
            StringBuilder text = new StringBuilder("D-PARTIAL");
            if (topology.Left != null) text.Append("-L");
            if (topology.LeftHole != null) text.Append("H");
            if (topology.Right != null) text.Append("-R");
            if (topology.RightHole != null) text.Append("H");
            return text.ToString();
        }

        private static DimPlan Plan(
            ViewData view,
            string name,
            string topology,
            double paperDistance,
            double directionX,
            double directionY,
            params P2[] points)
        {
            DimPlan result = new DimPlan();
            result.View = view;
            result.Name = name;
            result.Topology = topology;
            result.PaperDistance = paperDistance;
            result.Direction = Normalize(new P2(directionX, directionY));
            for (int i = 0; points != null && i < points.Length; i++)
                result.Points.Add(Clone(points[i]));
            return result;
        }

        private static void ValidatePlans(ViewPlans group)
        {
            if (group == null || group.View == null || group.View.View == null
                || group.Plans.Count == 0)
                throw new InvalidOperationException("Section plan group is empty.");
            int minimum;
            int maximum;
            if (group.Topology.StartsWith("B1-CARDINAL", StringComparison.Ordinal))
            {
                minimum = 2;
                maximum = 8;
            }
            else if (group.Topology.StartsWith("C-PARTIAL", StringComparison.Ordinal))
            {
                minimum = 5;
                maximum = 12;
            }
            else if (group.Topology.StartsWith("D-PARTIAL", StringComparison.Ordinal))
            {
                minimum = 1;
                maximum = 4;
            }
            else if (group.Topology.StartsWith("B-", StringComparison.Ordinal))
                minimum = maximum = 4;
            else if (group.Topology.StartsWith("C-", StringComparison.Ordinal))
                minimum = maximum = 13;
            else if (group.Topology.StartsWith("D-", StringComparison.Ordinal))
                minimum = maximum = 5;
            else
                throw new InvalidOperationException(
                    "Unknown section topology " + group.Topology + ".");
            if (group.Plans.Count < minimum || group.Plans.Count > maximum)
                throw new InvalidOperationException(
                    group.Topology + " plan count is " + group.Plans.Count
                    + "; expected range " + minimum + ".." + maximum + ".");
            if (group.Topology.StartsWith("B1-CARDINAL", StringComparison.Ordinal)
                && group.Plans.Count % 2 != 0)
                throw new InvalidOperationException(
                    group.Topology + " must contain one boundary and one plate plan per link.");
            HashSet<string> names = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < group.Plans.Count; i++)
            {
                DimPlan plan = group.Plans[i];
                if (plan.Direction == null || plan.Points.Count < 2
                    || !IsFinite(plan.Distance) || plan.Distance <= 0.0)
                    throw new InvalidOperationException(plan.Name + " is invalid.");
                if (!names.Add(plan.Name))
                    throw new InvalidOperationException(
                        plan.Name + " is duplicated in one section plan group.");
                P2 measurement = new P2(-plan.Direction.Y, plan.Direction.X);
                double min = Double.PositiveInfinity;
                double max = Double.NegativeInfinity;
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    double value = Dot(plan.Points[p], measurement);
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }
                if (max - min <= GeometryTolerance)
                    throw new InvalidOperationException(
                        plan.Name + " has no measurable span.");
            }
        }

        private static ExistingPlanStatus FindExistingPlan(DimPlan plan)
        {
            bool sameFeet = false;
            for (int i = 0; i < plan.View.Dimensions.Count; i++)
            {
                ExistingDimension existing = plan.View.Dimensions[i];
                if (!PointChainsMatch(existing.Points, plan.Points, MatchTolerance))
                    continue;
                sameFeet = true;
                if (DirectionsMatch(existing.Direction, plan.Direction))
                    return ExistingPlanStatus.Exact;
            }
            return sameFeet
                ? ExistingPlanStatus.FootConflict
                : ExistingPlanStatus.Missing;
        }

        private static bool TryReferenceArmAtNode(
            ViewData view,
            PartData part,
            P2 node,
            double minimumLength,
            out ReferenceArm arm)
        {
            arm = null;
            P3 a;
            P3 b;
            if (!TryReferencePairInsideDepth(view, part, out a, out b))
                return false;
            P3 near;
            P3 far;
            if (Distance(a.XY, node) <= GeometryTolerance * 3.0)
            {
                near = a;
                far = b;
            }
            else if (Distance(b.XY, node) <= GeometryTolerance * 3.0)
            {
                near = b;
                far = a;
            }
            else
                return false;
            if (Distance(near.XY, far.XY) < minimumLength)
                return false;
            arm = new ReferenceArm();
            arm.Part = part;
            arm.Node = node;
            arm.Far = far.XY;
            arm.Depth = (near.Z + far.Z) * 0.5;
            return true;
        }

        private static bool TryReferencePairInsideDepth(
            ViewData view,
            PartData part,
            out P3 first,
            out P3 second)
        {
            first = null;
            second = null;
            double best = 0.0;
            for (int i = 0; i < part.Reference.Count; i++)
            {
                if (!DepthContains(view, part.Reference[i].Z))
                    continue;
                for (int j = i + 1; j < part.Reference.Count; j++)
                {
                    if (!DepthContains(view, part.Reference[j].Z))
                        continue;
                    double distance = Distance(
                        part.Reference[i].XY,
                        part.Reference[j].XY);
                    if (distance > best)
                    {
                        best = distance;
                        first = part.Reference[i];
                        second = part.Reference[j];
                    }
                }
            }
            return first != null && second != null && best > GeometryTolerance;
        }

        private static bool TryCollapsedReferencePointInDepth(
            ViewData view,
            PartData part,
            out P3 point)
        {
            point = null;
            List<P3> inside = new List<P3>();
            for (int i = 0; i < part.Reference.Count; i++)
            {
                if (DepthContains(view, part.Reference[i].Z))
                    inside.Add(part.Reference[i]);
            }
            if (inside.Count == 0)
                return false;
            P2 projected = inside[0].XY;
            for (int i = 1; i < inside.Count; i++)
            {
                if (Distance(projected, inside[i].XY) > GeometryTolerance * 3.0)
                    return false;
            }
            point = inside[0];
            return true;
        }

        private static bool TryCollapsedReferencePointAtDepth(
            PartData part,
            double depth,
            out P3 point)
        {
            point = null;
            if (part.Reference.Count == 0)
                return false;
            P2 projected = part.Reference[0].XY;
            double minZ = part.Reference[0].Z;
            double maxZ = part.Reference[0].Z;
            for (int i = 1; i < part.Reference.Count; i++)
            {
                if (Distance(projected, part.Reference[i].XY)
                    > GeometryTolerance * 3.0)
                    return false;
                minZ = Math.Min(minZ, part.Reference[i].Z);
                maxZ = Math.Max(maxZ, part.Reference[i].Z);
            }
            if (depth < minZ - ReferenceDepthTolerance
                || depth > maxZ + ReferenceDepthTolerance)
                return false;
            point = new P3(projected.X, projected.Y, depth);
            return true;
        }

        private static bool TrySharedReferenceEndpoint(
            ReferenceArm first,
            ReferenceArm second,
            out P2 node,
            out P2 farFirst,
            out P2 farSecond)
        {
            node = null;
            farFirst = null;
            farSecond = null;
            List<Tuple<P3, P3>> pairs = new List<Tuple<P3, P3>>();
            for (int i = 0; i < first.Part.Reference.Count; i++)
            {
                for (int j = 0; j < second.Part.Reference.Count; j++)
                {
                    if (Distance(first.Part.Reference[i].XY,
                        second.Part.Reference[j].XY) <= GeometryTolerance * 3.0
                        && Math.Abs(first.Part.Reference[i].Z
                            - second.Part.Reference[j].Z)
                            <= ReferenceDepthTolerance * 3.0)
                        pairs.Add(Tuple.Create(
                            first.Part.Reference[i], second.Part.Reference[j]));
                }
            }
            if (pairs.Count == 0)
                return false;
            P3 sharedA = pairs[0].Item1;
            P3 sharedB = pairs[0].Item2;
            P3 otherA = FarthestReferenceFrom(first.Part, sharedA.XY);
            P3 otherB = FarthestReferenceFrom(second.Part, sharedB.XY);
            if (otherA == null || otherB == null)
                return false;
            node = Midpoint(sharedA.XY, sharedB.XY);
            farFirst = otherA.XY;
            farSecond = otherB.XY;
            return true;
        }

        private static P3 FarthestReferenceFrom(PartData part, P2 point)
        {
            P3 best = null;
            double distance = 0.0;
            for (int i = 0; i < part.Reference.Count; i++)
            {
                double candidate = Distance(part.Reference[i].XY, point);
                if (candidate > distance)
                {
                    distance = candidate;
                    best = part.Reference[i];
                }
            }
            return best;
        }

        private static bool HasReferenceInsideDepth(ViewData view, PartData part)
        {
            for (int i = 0; i < part.Reference.Count; i++)
            {
                if (DepthContains(view, part.Reference[i].Z))
                    return true;
            }
            return false;
        }

        private static bool DepthContains(ViewData view, double depth)
        {
            return depth >= view.DepthMin - ReferenceDepthTolerance
                && depth <= view.DepthMax + ReferenceDepthTolerance;
        }

        private static bool SharesBolt(PartData first, PartData second)
        {
            foreach (int id in first.Bolts.Keys)
            {
                if (second.Bolts.ContainsKey(id))
                    return true;
            }
            return false;
        }

        private static void AddSolidPoint(PartData part, P3 point)
        {
            if (point == null)
                return;
            part.Bounds.Add(point.XY);
            part.SolidDepth.Add(point.Z);
            AddUnique(part.Vertices, point.XY);
        }

        private static void AddUnique(List<P2> values, P2 point)
        {
            if (point == null)
                return;
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i], point) <= GeometryTolerance)
                    return;
            }
            values.Add(point);
        }

        private static void AddUnique(List<P3> values, P3 point)
        {
            if (point == null)
                return;
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].XY, point.XY) <= GeometryTolerance
                    && Math.Abs(values[i].Z - point.Z) <= ReferenceDepthTolerance)
                    return;
            }
            values.Add(point);
        }

        private static void AddUniqueNested(
            List<NestedTopology> values,
            NestedTopology item)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].Node, item.Node) <= GeometryTolerance
                    && SameGeometry(values[i].Core.Bounds, item.Core.Bounds)
                    && SameGeometry(values[i].Outer.Bounds, item.Outer.Bounds))
                    return;
            }
            values.Add(item);
        }

        private static void AddUniqueArm(List<ReferenceArm> values, ReferenceArm item)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].Node, item.Node) <= GeometryTolerance
                    && Distance(values[i].Far, item.Far) <= GeometryTolerance)
                    return;
            }
            values.Add(item);
        }

        private static void AddUniqueC(List<CTopology> values, CTopology item)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].Nested.Node, item.Nested.Node)
                        <= GeometryTolerance
                    && Distance(values[i].Horizontal.Far, item.Horizontal.Far)
                        <= GeometryTolerance
                    && Distance(values[i].SecondaryNode, item.SecondaryNode)
                        <= GeometryTolerance)
                    return;
            }
            values.Add(item);
        }

        private static void AddUniqueD(List<DTopology> values, DTopology item)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Distance(values[i].Node, item.Node) <= GeometryTolerance
                    && Distance(values[i].Left.Far, item.Left.Far)
                        <= GeometryTolerance
                    && Distance(values[i].Right.Far, item.Right.Far)
                        <= GeometryTolerance)
                    return;
            }
            values.Add(item);
        }

        private static bool SameGeometry(Bounds2 first, Bounds2 second)
        {
            return first != null && second != null
                && first.IsValid && second.IsValid
                && Math.Abs(first.MinX - second.MinX) <= GeometryTolerance
                && Math.Abs(first.MaxX - second.MaxX) <= GeometryTolerance
                && Math.Abs(first.MinY - second.MinY) <= GeometryTolerance
                && Math.Abs(first.MaxY - second.MaxY) <= GeometryTolerance;
        }

        private static bool PointOnSegment(
            P2 point,
            P2 first,
            P2 second,
            double tolerance)
        {
            P2 direction = Subtract(second, first);
            double length2 = Dot(direction, direction);
            if (length2 <= GeometryTolerance * GeometryTolerance)
                return false;
            double t = Dot(Subtract(point, first), direction) / length2;
            if (t < -0.01 || t > 1.01)
                return false;
            P2 projected = Add(first, Scale(direction, t));
            return Distance(point, projected) <= tolerance;
        }

        private static bool PointChainsMatch(
            List<P2> first,
            List<P2> second,
            double tolerance)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            bool forward = true;
            bool reverse = true;
            for (int i = 0; i < first.Count; i++)
            {
                forward &= Distance(first[i], second[i]) <= tolerance;
                reverse &= Distance(first[i], second[second.Count - 1 - i])
                    <= tolerance;
            }
            return forward || reverse;
        }

        private static bool DirectionsMatch(P2 first, P2 second)
        {
            P2 a = Normalize(first);
            P2 b = Normalize(second);
            return a != null && b != null
                && Dot(a, b) >= DirectionCosineTolerance;
        }

        private static List<P2> ReadDimensionPoints(object dimension)
        {
            List<P2> result = new List<P2>();
            IEnumerable values = GetMember(dimension, "DimensionPoints") as IEnumerable;
            if (values == null)
                return result;
            foreach (object value in values)
            {
                double x = ReadDouble(value, "X");
                double y = ReadDouble(value, "Y");
                if (IsFinite(x) && IsFinite(y))
                    result.Add(new P2(x, y));
            }
            return result;
        }

        private static P2 ReadDirection(object dimension)
        {
            object value = GetMember(dimension, "UpDirection")
                ?? GetMember(dimension, "OffsetDirection");
            double x = ReadDouble(value, "X");
            double y = ReadDouble(value, "Y");
            return Normalize(new P2(x, y));
        }

        private static void DisableCombineAndVerify(
            DimPlan plan,
            TSD.StraightDimensionSet dimension)
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            if (attributes == null)
                throw new InvalidOperationException(
                    "Cannot read attributes of " + plan.Name + ".");
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension
                ?? new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format = TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            combined.MinimumNumberToCombine = Math.Max(5, plan.Points.Count);
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;
            if (!dimension.Modify())
                throw new InvalidOperationException(
                    "Cannot disable combined dimension for " + plan.Name + ".");
            dimension.Select();
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes verified =
                dimension.Attributes == null
                    ? null
                    : dimension.Attributes.CombinedDimension;
            if (verified == null
                || (verified.Format
                        != TSD.DimensionSetBaseAttributes.CombineFormats.Off
                    && verified.MinimumNumberToCombine
                        <= plan.Points.Count - 1))
                throw new InvalidOperationException(
                    "Tekla overwrote CombinedDimension of " + plan.Name + ".");
        }

        private static void DeleteCreated(List<TSD.StraightDimensionSet> created)
        {
            for (int i = 0; created != null && i < created.Count; i++)
            {
                try
                {
                    if (created[i] != null)
                        created[i].Delete();
                }
                catch { }
            }
        }

        private static void TryCommit(TSD.Drawing drawing)
        {
            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch { }
        }

        private static bool IsSectionView(TSD.View view)
        {
            string runtime = view == null ? String.Empty : view.GetType().Name;
            string viewType = Convert.ToString(
                GetMember(view, "ViewType"), CultureInfo.InvariantCulture)
                ?? String.Empty;
            return runtime.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0
                || viewType.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double ReadScale(TSD.View view)
        {
            try { return view.Attributes.Scale; }
            catch { return Double.NaN; }
        }

        private static string SafeViewName(TSD.View view)
        {
            try { return view.Name ?? String.Empty; }
            catch { return String.Empty; }
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                Tekla.Structures.Identifier identifier =
                    value == null
                        ? null
                        : GetMember(value, "Identifier")
                            as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch { return 0; }
        }

        private static object GetMember(object value, string name)
        {
            if (value == null || String.IsNullOrEmpty(name))
                return null;
            try
            {
                Type type = value.GetType();
                PropertyInfo property = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(value, null);
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance);
                return field == null ? null : field.GetValue(value);
            }
            catch { return null; }
        }

        private static double ReadDouble(object value, string name)
        {
            object member = GetMember(value, name);
            if (member == null)
                return Double.NaN;
            try { return Convert.ToDouble(member, CultureInfo.InvariantCulture); }
            catch { return Double.NaN; }
        }

        private static P3 Transform3(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView)
        {
            if (point == null)
                return null;
            TSG.Point global = currentToGlobal.Transform(point);
            TSG.Point transformed = globalToView.Transform(global);
            return new P3(transformed.X, transformed.Y, transformed.Z);
        }

        private static bool SameIdentifier(
            TSM.ModelObject first,
            TSM.ModelObject second)
        {
            return first != null && second != null
                && first.Identifier != null && second.Identifier != null
                && first.Identifier.ID == second.Identifier.ID;
        }

        private static P2 Pt(double x, double y)
        {
            return new P2(x, y);
        }

        private static P2 Clone(P2 point)
        {
            return point == null ? null : new P2(point.X, point.Y);
        }

        private static ReferenceArm CloneArm(
            ReferenceArm source,
            P2 node,
            P2 far)
        {
            ReferenceArm result = new ReferenceArm();
            result.Part = source.Part;
            result.Node = Clone(node);
            result.Far = Clone(far);
            result.Depth = source.Depth;
            return result;
        }

        private static P2 Add(P2 first, P2 second)
        {
            return new P2(first.X + second.X, first.Y + second.Y);
        }

        private static P2 Subtract(P2 first, P2 second)
        {
            return new P2(first.X - second.X, first.Y - second.Y);
        }

        private static P2 Scale(P2 point, double factor)
        {
            return new P2(point.X * factor, point.Y * factor);
        }

        private static P2 Midpoint(P2 first, P2 second)
        {
            return new P2((first.X + second.X) * 0.5,
                (first.Y + second.Y) * 0.5);
        }

        private static P2 Normalize(P2 value)
        {
            if (value == null || !IsFinite(value.X) || !IsFinite(value.Y))
                return null;
            double length = Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return length <= GeometryTolerance
                ? null
                : new P2(value.X / length, value.Y / length);
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

        private static string FormatPoint(P2 point)
        {
            return point == null
                ? "<null>"
                : "(" + Format(point.X) + "," + Format(point.Y) + ")";
        }

        private static string FormatPoints(List<P2> points)
        {
            StringBuilder text = new StringBuilder();
            text.Append('[');
            for (int i = 0; points != null && i < points.Count; i++)
            {
                if (i > 0) text.Append(';');
                text.Append(FormatPoint(points[i]));
            }
            text.Append(']');
            return text.ToString();
        }

        private static string Format(double value)
        {
            return IsFinite(value)
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : "NA";
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static PartData SyntheticPart(
            double minX,
            double maxX,
            double minY,
            double maxY)
        {
            PartData result = new PartData();
            result.Bounds.Add(new P2(minX, minY));
            result.Bounds.Add(new P2(maxX, maxY));
            return result;
        }

        private static ViewData BuildSyntheticType1BView(
            bool upper,
            bool lower,
            bool left,
            bool right,
            bool reverse,
            string label,
            bool rectangularType2Main)
        {
            ViewData view = new ViewData();
            view.Label = label;
            view.Scale = 20.0;
            view.DepthMin = -1.0;
            view.DepthMax = 1.0;

            PartData main = SyntheticType1BPart(
                1,
                rectangularType2Main ? -147.0 : -100.0,
                rectangularType2Main ? 147.0 : 100.0,
                -100.0,
                100.0,
                true,
                false,
                true);
            main.Reference.Add(new P3(0.0, 0.0, 0.0));
            view.Parts.Add(main);

            if (upper)
            {
                AddSyntheticType1BConnection(
                    view,
                    10,
                    110,
                    -100, 25, 110, 1690,
                    new P2(-37.5, 0), new P2(-37.5, 1800),
                    -34.25, -25.25, 4, 235);
            }
            if (lower)
            {
                AddSyntheticType1BConnection(
                    view,
                    20,
                    120,
                    -100, 25, -1690, -110,
                    new P2(-37.5, -1800), new P2(-37.5, 0),
                    -34.25, -25.25, -235, -4);
            }
            if (left)
            {
                AddSyntheticType1BConnection(
                    view,
                    30,
                    130,
                    -1119, -120, -62.5, 62.5,
                    new P2(-1250, 25), new P2(0, 25),
                    -245, -100, 28.25, 37.25);
            }
            if (right)
            {
                AddSyntheticType1BConnection(
                    view,
                    40,
                    140,
                    120, 1119, -62.5, 62.5,
                    new P2(0, 0), new P2(1250, 0),
                    100, 245, -12.25, -3.25);
            }
            if (reverse)
                view.Parts.Reverse();
            return view;
        }

        private static void AddSyntheticType1BConnection(
            ViewData view,
            int neighborIdentifier,
            int boltIdentifier,
            double neighborMinX,
            double neighborMaxX,
            double neighborMinY,
            double neighborMaxY,
            P2 referenceStart,
            P2 referenceEnd,
            double plateMinX,
            double plateMaxX,
            double plateMinY,
            double plateMaxY)
        {
            PartData neighbor = SyntheticType1BPart(
                neighborIdentifier,
                neighborMinX,
                neighborMaxX,
                neighborMinY,
                neighborMaxY,
                false,
                false,
                false);
            neighbor.Reference.Add(new P3(
                referenceStart.X,
                referenceStart.Y,
                0.0));
            neighbor.Reference.Add(new P3(
                referenceEnd.X,
                referenceEnd.Y,
                0.0));
            BoltData neighborBolt = new BoltData();
            neighborBolt.Identifier = boltIdentifier;
            neighbor.Bolts.Add(boltIdentifier, neighborBolt);

            PartData plate = SyntheticType1BPart(
                neighborIdentifier + 1,
                plateMinX,
                plateMaxX,
                plateMinY,
                plateMaxY,
                false,
                true,
                true);
            BoltData plateBolt = new BoltData();
            plateBolt.Identifier = boltIdentifier;
            plate.Bolts.Add(boltIdentifier, plateBolt);

            view.Parts.Add(neighbor);
            view.Parts.Add(plate);
        }

        private static PartData SyntheticType1BPart(
            int identifier,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool isMain,
            bool isContourPlate,
            bool sameMainAssembly)
        {
            PartData result = new PartData();
            result.Identifier = identifier;
            result.IsDrawingMain = isMain;
            result.IsContourPlate = isContourPlate;
            result.SameMainAssembly = sameMainAssembly;
            result.Bounds.Add(new P2(minX, minY));
            result.Bounds.Add(new P2(maxX, maxY));
            result.Vertices.Add(new P2(minX, minY));
            result.Vertices.Add(new P2(minX, maxY));
            result.Vertices.Add(new P2(maxX, minY));
            result.Vertices.Add(new P2(maxX, maxY));
            return result;
        }

        private static ReferenceArm SyntheticArm(
            PartData part,
            P2 node,
            P2 far)
        {
            ReferenceArm result = new ReferenceArm();
            result.Part = part;
            result.Node = node;
            result.Far = far;
            return result;
        }

        private static bool ContainsPlan(List<DimPlan> plans, params P2[] points)
        {
            List<P2> expected = new List<P2>(points);
            for (int i = 0; i < plans.Count; i++)
            {
                if (PointChainsMatch(plans[i].Points, expected, 0.01))
                    return true;
            }
            return false;
        }

        private static int CountSetBits(int value)
        {
            int result = 0;
            while (value > 0)
            {
                result += value & 1;
                value = value >> 1;
            }
            return result;
        }

        private static void ResetResult()
        {
            LastRunApplicable = false;
            LastRunSucceeded = false;
            LastCreatedCount = 0;
            LastReusedCount = 0;
            LastConflictCount = 0;
            LastRunMessage = String.Empty;
        }
    }
}
