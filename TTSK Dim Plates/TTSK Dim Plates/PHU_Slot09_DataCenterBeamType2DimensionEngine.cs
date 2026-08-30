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
    /// Supplemental dimensions owned only by the geometry-proven Slot09 Data
    /// Center Beam Type-2 route.  Shape H keeps ownership of the MainPart edge
    /// and hole dimensions, BeamGrid keeps Grid/REF totals, and Slot04 keeps
    /// the independent top-plate chain.  This engine owns only the transverse
    /// member relations and the two true section-view families missing from
    /// those stable engines.
    /// </summary>
    public static class PHU_Slot09_DataCenterBeamType2DimensionEngine
    {
        private const double GeometryTolerance = 0.75;
        private const double MatchTolerance = 2.0;
        private const double DirectionCosineTolerance = 0.985;

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

            public bool Overlaps(double minimum, double maximum, double tolerance)
            {
                return IsValid && Max >= minimum - tolerance && Min <= maximum + tolerance;
            }
        }

        private sealed class Edge2
        {
            public P2 Start;
            public P2 End;
        }

        private sealed class BoltData
        {
            public int Identifier;
            public double Diameter;
            public readonly List<P3> Points = new List<P3>();
        }

        private sealed class PartData
        {
            public TSM.Part ModelPart;
            public int Identifier;
            public bool IsMain;
            public bool SameMainAssembly;
            public bool IsContourPlate;
            public readonly Bounds2 Bounds = new Bounds2();
            public readonly DepthRange Depth = new DepthRange();
            public readonly List<P3> Vertices = new List<P3>();
            public readonly List<Edge2> Edges = new List<Edge2>();
            public readonly List<P3> Reference = new List<P3>();
            public readonly Dictionary<int, BoltData> Bolts =
                new Dictionary<int, BoltData>();
        }

        private sealed class ExistingDimension
        {
            public TSD.StraightDimensionSet Dimension;
            public readonly List<P2> Points = new List<P2>();
            public P2 EffectiveDirection;
        }

        private sealed class ViewData
        {
            public TSD.View View;
            public int Identifier;
            public string Label;
            public double Scale;
            public double DepthMin;
            public double DepthMax;
            public PartData Main;
            public P2 MainAxis;
            public P2 UpAxis;
            public P2 RefLeft;
            public P2 RefRight;
            public readonly List<PartData> Parts = new List<PartData>();
            public readonly List<ExistingDimension> Dimensions =
                new List<ExistingDimension>();
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes Attributes;
        }

        private sealed class DimPlan
        {
            public string Name;
            public string Family;
            public ViewData View;
            public P2 Direction;
            public int Tier;
            public readonly List<P2> Points = new List<P2>();

            public double Distance
            {
                get
                {
                    double tierBase;
                    double tierStep;
                    ResolveShapeXTierSpacing(View.Scale, out tierBase, out tierStep);
                    return tierBase + Math.Max(0, Tier) * tierStep;
                }
            }
        }

        private sealed class Analysis
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public ViewData Top;
            public ViewData Front;
            public readonly List<ViewData> Sections = new List<ViewData>();
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
        public static int LastDeletedRedundantTopCount { get; private set; }
        public static int LastDeletedRedundantFrontSlot04Count { get; private set; }
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
        /// Runs after Shape H/BeamGrid and before Slot04.  Existing exact
        /// dimensions are reused.  A same-foot/different-direction object is
        /// protected as a conflict instead of being silently duplicated.
        /// </summary>
        public static bool ExecuteAfterShape()
        {
            ResetResult();
            if (!_enabled || !PHU_Slot09_DataCenterBeamType2Context.IsActive)
            {
                LastRunSucceeded = true;
                LastRunMessage = "Data Center Beam Type2 DIM skipped: route is inactive.";
                return true;
            }

            List<TSD.StraightDimensionSet> created =
                new List<TSD.StraightDimensionSet>();
            TSD.Drawing drawing = null;
            try
            {
                Analysis analysis;
                string message;
                if (!TryAnalyze(out analysis, out message))
                {
                    LastRunMessage = "Data Center Beam Type2 DIM failed preflight: " + message;
                    return false;
                }

                drawing = analysis.Drawing;
                ValidatePlans(analysis.Plans);
                List<ExistingDimension> redundantTopDimensions =
                    FindRedundantTopHoleDimensions(analysis);
                List<ExistingDimension> redundantFrontSlot04Dimensions =
                    FindRedundantFrontSlot04Dimensions(analysis);
                LastRunApplicable = analysis.Plans.Count > 0;
                if (!LastRunApplicable)
                {
                    LastRunMessage = "Data Center Beam Type2 DIM produced no proven plan.";
                    return false;
                }

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < analysis.Plans.Count; i++)
                {
                    DimPlan plan = analysis.Plans[i];
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
                    for (int p = 0; p < plan.Points.Count; p++)
                        points.Add(plan.Points[p].ToPoint());
                    TSG.Vector direction = new TSG.Vector(
                        plan.Direction.X,
                        plan.Direction.Y,
                        0.0);
                    TSD.StraightDimensionSet dimension =
                        plan.View.Attributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                direction,
                                plan.Distance)
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                direction,
                                plan.Distance,
                                plan.View.Attributes);
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla could not create " + plan.Name + ".");
                    created.Add(dimension);
                    DisableCombine(dimension);
                    LastCreatedCount++;
                }

                if (LastConflictCount > 0)
                    throw new InvalidOperationException(
                        LastConflictCount.ToString(CultureInfo.InvariantCulture)
                        + " same-foot direction conflict(s) were protected.");

                for (int i = 0; i < redundantTopDimensions.Count; i++)
                {
                    ExistingDimension redundant = redundantTopDimensions[i];
                    if (redundant == null || redundant.Dimension == null
                        || !redundant.Dimension.Delete())
                        throw new InvalidOperationException(
                            "Tekla rejected redundant TOP DIM deletion at index "
                                + i.ToString(CultureInfo.InvariantCulture) + ".");
                    LastDeletedRedundantTopCount++;
                }

                for (int i = 0; i < redundantFrontSlot04Dimensions.Count; i++)
                {
                    ExistingDimension redundant = redundantFrontSlot04Dimensions[i];
                    if (redundant == null || redundant.Dimension == null
                        || !redundant.Dimension.Delete())
                        throw new InvalidOperationException(
                            "Tekla rejected redundant FRONT Slot04 DIM deletion at index "
                                + i.ToString(CultureInfo.InvariantCulture) + ".");
                    LastDeletedRedundantFrontSlot04Count++;
                }

                if (created.Count > 0 || LastDeletedRedundantTopCount > 0
                    || LastDeletedRedundantFrontSlot04Count > 0)
                    analysis.Drawing.CommitChanges();

                LastRunSucceeded = true;
                LastRunMessage = "Data Center Beam Type2 supplemental DIM: plans="
                    + analysis.Plans.Count.ToString(CultureInfo.InvariantCulture)
                    + ", created=" + LastCreatedCount.ToString(CultureInfo.InvariantCulture)
                    + ", reused=" + LastReusedCount.ToString(CultureInfo.InvariantCulture)
                    + ", deleted-redundant-top="
                    + LastDeletedRedundantTopCount.ToString(CultureInfo.InvariantCulture)
                    + ", deleted-redundant-front-slot04="
                    + LastDeletedRedundantFrontSlot04Count.ToString(
                        CultureInfo.InvariantCulture)
                    + ", conflicts=0.";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                TryCommit(drawing);
                LastCreatedCount = 0;
                LastRunSucceeded = false;
                LastRunMessage = "Data Center Beam Type2 supplemental DIM rolled back: "
                    + ex.Message
                    + (LastDeletedRedundantTopCount > 0
                        || LastDeletedRedundantFrontSlot04Count > 0
                        ? " DIM replacement/deletion had already started; inspect drawing before save."
                        : String.Empty);
                return false;
            }
        }

        /// <summary>Read-only plan-to-current-drawing comparison.</summary>
        public static string AuditCurrentDrawingPlan()
        {
            try
            {
                Analysis analysis;
                string message;
                if (!TryAnalyze(out analysis, out message))
                    return "DATA CENTER BEAM TYPE2 DIM AUDIT SKIPPED\r\n" + message;

                ValidatePlans(analysis.Plans);
                StringBuilder text = new StringBuilder();
                text.AppendLine("DATA CENTER BEAM TYPE2 DIM PLAN - READ ONLY");
                text.AppendLine("TopView=" + analysis.Top.Identifier
                    .ToString(CultureInfo.InvariantCulture));
                text.AppendLine("FrontView=" + analysis.Front.Identifier
                    .ToString(CultureInfo.InvariantCulture));
                text.AppendLine("SectionViews=" + analysis.Sections.Count
                    .ToString(CultureInfo.InvariantCulture));
                int exact = 0;
                int missing = 0;
                int conflicts = 0;
                for (int i = 0; i < analysis.Plans.Count; i++)
                {
                    DimPlan plan = analysis.Plans[i];
                    ExistingPlanStatus status = FindExistingPlan(plan);
                    if (status == ExistingPlanStatus.Exact)
                        exact++;
                    else if (status == ExistingPlanStatus.Missing)
                        missing++;
                    else
                        conflicts++;
                    text.Append("  ").Append(plan.Name)
                        .Append(" family=").Append(plan.Family)
                        .Append(" view=").Append(plan.View.Identifier)
                        .Append(" status=").Append(status)
                        .Append(" direction=").Append(FormatPoint(plan.Direction))
                        .Append(" tier=").Append(plan.Tier)
                        .Append(" feet=").Append(FormatPoints(plan.Points))
                        .AppendLine();
                }
                text.AppendLine("TOTAL plans=" + analysis.Plans.Count
                    .ToString(CultureInfo.InvariantCulture)
                    + " exact=" + exact.ToString(CultureInfo.InvariantCulture)
                    + " missing=" + missing.ToString(CultureInfo.InvariantCulture)
                    + " conflicts=" + conflicts.ToString(CultureInfo.InvariantCulture));
                text.AppendLine(
                    "POINT ORDER=semantic order is supplied to the writer; Tekla 2025 stores canonical coordinate order.");
                List<ExistingDimension> redundantTopDimensions =
                    FindRedundantTopHoleDimensions(analysis);
                text.AppendLine("TOP REDUNDANT HOLE DIMS="
                    + redundantTopDimensions.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < redundantTopDimensions.Count; i++)
                {
                    ExistingDimension redundant = redundantTopDimensions[i];
                    text.Append("  REDUNDANT TOP ")
                        .Append(i + 1)
                        .Append(" direction=")
                        .Append(FormatPoint(redundant.EffectiveDirection))
                        .Append(" feet=")
                        .Append(FormatPoints(redundant.Points))
                        .AppendLine();
                }
                List<ExistingDimension> redundantFrontSlot04Dimensions =
                    FindRedundantFrontSlot04Dimensions(analysis);
                text.AppendLine("FRONT REDUNDANT SLOT04 DIMS="
                    + redundantFrontSlot04Dimensions.Count.ToString(
                        CultureInfo.InvariantCulture));
                for (int i = 0; i < redundantFrontSlot04Dimensions.Count; i++)
                {
                    ExistingDimension redundant = redundantFrontSlot04Dimensions[i];
                    text.Append("  REDUNDANT FRONT SLOT04 ")
                        .Append(i + 1)
                        .Append(" direction=")
                        .Append(FormatPoint(redundant.EffectiveDirection))
                        .Append(" feet=")
                        .Append(FormatPoints(redundant.Points))
                        .AppendLine();
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "DATA CENTER BEAM TYPE2 DIM AUDIT FAILED\r\n" + ex;
            }
        }

        /// <summary>Pure geometry regression; no Tekla API mutation.</summary>
        public static string AuditGeometryRegression()
        {
            try
            {
                // Tier contract must remain byte-for-byte equivalent to Shape X.
                double tierBase;
                double tierStep;
                ResolveShapeXTierSpacing(5.0, out tierBase, out tierStep);
                if (tierBase != 50.0 || tierStep != 50.0)
                    throw new InvalidOperationException("Scale 5 tier regression failed.");
                ResolveShapeXTierSpacing(15.0, out tierBase, out tierStep);
                if (tierBase != 150.0 || tierStep != 150.0)
                    throw new InvalidOperationException("Scale 15 tier regression failed.");
                ResolveShapeXTierSpacing(30.0, out tierBase, out tierStep);
                if (tierBase != 300.0 || tierStep != 300.0)
                    throw new InvalidOperationException("Scale 30 tier regression failed.");
                ResolveShapeXTierSpacing(7.0, out tierBase, out tierStep);
                if (tierBase != 150.0 || tierStep != 150.0)
                    throw new InvalidOperationException(
                        "Unsupported-scale Shape X fallback regression failed.");

                P2 intersection;
                if (!TryLineIntersection(
                        new P2(-10, 0),
                        new P2(2390, 0),
                        new P2(1190, 0),
                        new P2(1190, -625),
                        out intersection)
                    || Distance(intersection, new P2(1190, 0)) > 0.001)
                    throw new InvalidOperationException("Transverse REF intersection failed.");

                // Mirroring the topology must preserve a real intersection;
                // no absolute sample coordinate is part of the relation.
                if (!TryLineIntersection(
                        new P2(10, 0),
                        new P2(-2390, 0),
                        new P2(-1190, 0),
                        new P2(-1190, 625),
                        out intersection)
                    || Distance(intersection, new P2(-1190, 0)) > 0.001)
                    throw new InvalidOperationException("Mirrored transverse REF failed.");

                List<P2> projectedA = new List<P2>();
                projectedA.Add(new P2(0.0, 147.0));
                projectedA.Add(new P2(1190.0, 147.0));
                projectedA.Add(new P2(2390.0, 147.0));
                List<P2> projectedB = new List<P2>();
                projectedB.Add(new P2(0.0, -500.0));
                projectedB.Add(new P2(1190.0, 900.0));
                projectedB.Add(new P2(2390.0, 0.0));
                if (!ProjectedFeetMatch(
                        projectedA,
                        projectedB,
                        new P2(0.0, 1.0),
                        0.001))
                    throw new InvalidOperationException(
                        "Dimension-foot projection invariance failed.");
                projectedB[1].X += 3.0;
                if (ProjectedFeetMatch(
                        projectedA,
                        projectedB,
                        new P2(0.0, 1.0),
                        MatchTolerance))
                    throw new InvalidOperationException(
                        "Dimension-foot mismatch tolerance failed.");

                ViewData topFilterView = BuildSyntheticTopFilterView();
                ExistingDimension redundantHorizontalChain = SyntheticDimension(
                    new P2(0, 1),
                    new P2(0, 100), new P2(40, -38),
                    new P2(100, -38), new P2(160, -38),
                    new P2(2230, -38), new P2(2290, -38),
                    new P2(2350, -38), new P2(2390, 100));
                ExistingDimension redundantHorizontalTotal = SyntheticDimension(
                    new P2(0, 1), new P2(0, 100), new P2(2390, 100));
                ExistingDimension keepLiftHorizontal = SyntheticDimension(
                    new P2(0, -1), new P2(0, -100), new P2(500, -89));
                ExistingDimension redundantVerticalChain = SyntheticDimension(
                    new P2(-1, 0),
                    new P2(0, -100), new P2(138, -60),
                    new P2(138, 60), new P2(0, 100));
                ExistingDimension keepLiftVertical = SyntheticDimension(
                    new P2(1, 0), new P2(524, -100), new P2(524, -65));
                ExistingDimension keepGridChain = SyntheticDimension(
                    new P2(0, 1),
                    new P2(-10, 0), new P2(0, 100), new P2(2390, 100));
                if (!IsRedundantTopHoleDimension(topFilterView, redundantHorizontalChain)
                    || !IsRedundantTopHoleDimension(topFilterView, redundantHorizontalTotal)
                    || IsRedundantTopHoleDimension(topFilterView, keepLiftHorizontal)
                    || !IsRedundantTopHoleDimension(topFilterView, redundantVerticalChain)
                    || IsRedundantTopHoleDimension(topFilterView, keepLiftVertical)
                    || IsRedundantTopHoleDimension(topFilterView, keepGridChain))
                    throw new InvalidOperationException(
                        "TOP redundant-hole DIM filter regression failed.");

                ViewData frontSlot04FilterView = BuildSyntheticFrontSlot04FilterView();
                ExistingDimension wrongLocalPlateAndReference = SyntheticDimension(
                    new P2(0, 1),
                    new P2(0, 147), new P2(951, 247),
                    new P2(1180, 247), new P2(1190, 2927),
                    new P2(1431, 247), new P2(2390, 147));
                ExistingDimension wrongLocalPlateOnly = SyntheticDimension(
                    new P2(0, 1),
                    new P2(0, 147), new P2(1180, 247),
                    new P2(2390, 147));
                ExistingDimension correctIndependentPlateChain = SyntheticDimension(
                    new P2(0, 1),
                    new P2(0, 147), new P2(951, 247),
                    new P2(1431, 247), new P2(2390, 147));
                ExistingDimension keepTransverseMainFaceChain = SyntheticDimension(
                    new P2(0, 1),
                    new P2(0, 147), new P2(1190, 147),
                    new P2(2390, 147));
                ExistingDimension keepBottomChain = SyntheticDimension(
                    new P2(0, -1),
                    new P2(0, -147), new P2(1180, -247),
                    new P2(2390, -147));
                if (!IsRedundantFrontSlot04Dimension(
                        frontSlot04FilterView, wrongLocalPlateAndReference, 1190.0)
                    || !IsRedundantFrontSlot04Dimension(
                        frontSlot04FilterView, wrongLocalPlateOnly, 1190.0)
                    || IsRedundantFrontSlot04Dimension(
                        frontSlot04FilterView, correctIndependentPlateChain, 1190.0)
                    || IsRedundantFrontSlot04Dimension(
                        frontSlot04FilterView, keepTransverseMainFaceChain, 1190.0)
                    || IsRedundantFrontSlot04Dimension(
                        frontSlot04FilterView, keepBottomChain, 1190.0))
                    throw new InvalidOperationException(
                        "FRONT Slot04 stale-chain filter regression failed.");

                PartData notched = SyntheticPolygonPart(new P2[]
                {
                    new P2(0, 0), new P2(10, 0), new P2(10, 4),
                    new P2(6, 4), new P2(6, 2), new P2(4, 2),
                    new P2(4, 4), new P2(0, 4)
                }, true, true, false);
                P2 realBoundary = BoundaryAtStation(
                    notched,
                    new P2(1, 0),
                    new P2(0, 1),
                    5.0,
                    true);
                if (realBoundary == null || Math.Abs(realBoundary.Y - 2.0) > 0.001)
                    throw new InvalidOperationException(
                        "Real solid edge at a notched station was not preserved.");

                Analysis plateAnalysis = new Analysis();
                ViewData plateSection = BuildSyntheticPlateSection();
                BuildSectionPlans(plateAnalysis, plateSection);
                ValidatePlans(plateAnalysis.Plans);
                if (plateAnalysis.Plans.Count != 3)
                    throw new InvalidOperationException(
                        "Section plate/hole partial topology failed.");
                DimPlan sectionAOrder = FindPlanByName(
                    plateAnalysis.Plans,
                    "T2-SECTION-A-03-MAIN-HOLE-Y");
                if (sectionAOrder == null
                    || sectionAOrder.Points.Count != 2
                    || Distance(sectionAOrder.Points[0], new P2(100, 202)) > 0.001
                    || Distance(sectionAOrder.Points[1], new P2(0, 147)) > 0.001)
                    throw new InvalidOperationException(
                        "Section A hole-to-beam point order failed.");

                Analysis bracketOnly = new Analysis();
                ViewData bracketOnlyView = BuildSyntheticConnectionSection(
                    false, false, false, false);
                BuildSectionPlans(bracketOnly, bracketOnlyView);
                ValidatePlans(bracketOnly.Plans);
                if (bracketOnly.Plans.Count != 6)
                    throw new InvalidOperationException(
                        "Section bracket-only topology failed.");

                Analysis oneLink = new Analysis();
                ViewData oneLinkView = BuildSyntheticConnectionSection(
                    false, true, false, false);
                BuildSectionPlans(oneLink, oneLinkView);
                ValidatePlans(oneLink.Plans);
                if (oneLink.Plans.Count != 8)
                    throw new InvalidOperationException(
                        "Section one-neighbor topology failed.");

                Analysis lowerOnly = new Analysis();
                ViewData lowerOnlyView = BuildSyntheticConnectionSection(
                    true, false, false, false);
                BuildSectionPlans(lowerOnly, lowerOnlyView);
                ValidatePlans(lowerOnly.Plans);
                if (lowerOnly.Plans.Count != 8)
                    throw new InvalidOperationException(
                        "Section lower-only neighbor topology failed.");

                Analysis linksWithoutBracket = new Analysis();
                ViewData linksWithoutBracketView = BuildSyntheticConnectionSection(
                    true, true, false, true);
                RemoveSyntheticBracket(linksWithoutBracketView);
                BuildSectionPlans(linksWithoutBracket, linksWithoutBracketView);
                ValidatePlans(linksWithoutBracket.Plans);
                if (linksWithoutBracket.Plans.Count != 6)
                    throw new InvalidOperationException(
                        "Section missing-bracket topology failed.");

                Analysis bareMain = new Analysis();
                ViewData bareMainView = BuildSyntheticConnectionSection(
                    false, false, false, true);
                RemoveSyntheticBracket(bareMainView);
                BuildSectionPlans(bareMain, bareMainView);
                ValidatePlans(bareMain.Plans);
                if (bareMain.Plans.Count != 2)
                    throw new InvalidOperationException(
                        "Section no-connection topology failed.");

                Analysis full = new Analysis();
                ViewData fullView = BuildSyntheticConnectionSection(
                    true, true, false, false);
                BuildSectionPlans(full, fullView);
                ValidatePlans(full.Plans);
                if (full.Plans.Count != 10)
                    throw new InvalidOperationException(
                        "Section full topology failed.");
                DimPlan upperOrder = FindPlanByName(
                    full.Plans,
                    "T2-SECTION-B-UPPER-HOLE-Y");
                DimPlan leftBracketOrder = FindPlanByName(
                    full.Plans,
                    "T2-SECTION-B-BRACKET-04-HOLE-Y");
                if (upperOrder == null || upperOrder.Points.Count != 2
                    || upperOrder.Points[0].Y <= upperOrder.Points[1].Y)
                    throw new InvalidOperationException(
                        "Section B right hole-to-beam point order failed.");
                if (leftBracketOrder == null
                    || leftBracketOrder.Points.Count != 2
                    || leftBracketOrder.Points[0].Y <= leftBracketOrder.Points[1].Y)
                    throw new InvalidOperationException(
                        "Section B left beam-to-hole point order failed.");

                Analysis shuffled = new Analysis();
                ViewData shuffledView = BuildSyntheticConnectionSection(
                    true, true, false, true);
                BuildSectionPlans(shuffled, shuffledView);
                ValidatePlans(shuffled.Plans);
                if (!String.Equals(
                        PlanFingerprint(full.Plans),
                        PlanFingerprint(shuffled.Plans),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Section part-order invariance failed.");

                Analysis mirrored = new Analysis();
                ViewData mirroredView = BuildSyntheticConnectionSection(
                    true, true, true, true);
                BuildSectionPlans(mirrored, mirroredView);
                ValidatePlans(mirrored.Plans);
                if (mirrored.Plans.Count != 10)
                    throw new InvalidOperationException(
                        "Mirrored section topology failed.");

                return "DATA CENTER BEAM TYPE2 GEOMETRY REGRESSION PASS: "
                    + "ShapeX tiers/fallback=3/3/1, transverse/mirror=2/2, "
                    + "projected-feet=2/2, top-extra-filter=6/6, "
                    + "front-slot04-filter=5/5, "
                    + "section-point-order=3/3, real-notch-edge=1/1, "
                    + "section plate/bracket/upper/lower/no-bracket/bare/full/"
                    + "shuffle/mirror=3/6/8/8/6/2/10/PASS/10.";
            }
            catch (Exception ex)
            {
                return "DATA CENTER BEAM TYPE2 GEOMETRY REGRESSION FAILED: "
                    + ex.Message;
            }
        }

        private static bool TryAnalyze(out Analysis analysis, out string message)
        {
            analysis = null;
            message = String.Empty;
            if (!PHU_Slot09_DataCenterBeamType2Context.IsActive)
            {
                message = "Type2 geometry scope is inactive.";
                return false;
            }

            TSM.Model model = new TSM.Model();
            TSD.DrawingHandler handler = new TSD.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
            {
                message = "Tekla Model/Drawing API is unavailable.";
                return false;
            }
            TSD.Drawing drawing = handler.GetActiveDrawing();
            if (drawing == null)
            {
                message = "No active drawing.";
                return false;
            }
            TSM.Part mainPart = PHU_MainPartResolver.Resolve(model, drawing);
            if (mainPart == null)
            {
                message = "Assembly MainPart could not be resolved.";
                return false;
            }

            TSM.TransformationPlane original = model.GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();
            TSG.Matrix currentToGlobal = original.TransformationMatrixToGlobal;
            Analysis result = new Analysis();
            result.Model = model;
            result.Drawing = drawing;
            result.MainPart = mainPart;
            result.Top = ReadView(
                model,
                mainPart,
                PHU_Slot09_DataCenterBeamType2Context.TopView,
                currentToGlobal);
            result.Front = ReadView(
                model,
                mainPart,
                PHU_Slot09_DataCenterBeamType2Context.FrontView,
                currentToGlobal);
            if (result.Top == null || result.Front == null)
            {
                message = "The proven Top/Front geometry could not be read.";
                return false;
            }

            IList<TSD.View> sectionViews =
                PHU_Slot09_DataCenterBeamType2Context.SectionViews;
            for (int i = 0; i < sectionViews.Count; i++)
            {
                ViewData section = ReadView(
                    model,
                    mainPart,
                    sectionViews[i],
                    currentToGlobal);
                if (section != null)
                    result.Sections.Add(section);
            }

            BuildTopPlans(result, result.Top);
            BuildFrontPlans(result, result.Front);
            for (int i = 0; i < result.Sections.Count; i++)
                BuildSectionPlans(result, result.Sections[i]);

            analysis = result;
            message = "Type2 supplemental plan resolved.";
            return true;
        }

        private static ViewData ReadView(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal
        )
        {
            if (view == null)
                return null;
            object restriction = GetMember(view, "RestrictionBox");
            TSG.Point minimum = GetMember(restriction, "MinPoint") as TSG.Point;
            TSG.Point maximum = GetMember(restriction, "MaxPoint") as TSG.Point;
            if (minimum == null || maximum == null)
                return null;

            ViewData result = new ViewData();
            result.View = view;
            result.Identifier = ReadIdentifier(view);
            result.Label = SafeViewName(view);
            result.Scale = ReadScale(view);
            result.DepthMin = Math.Min(minimum.Z, maximum.Z);
            result.DepthMax = Math.Max(minimum.Z, maximum.Z);
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
                TSM.Part modelPart = model.SelectModelObject(
                    drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null)
                    continue;
                PartData data = ReadPart(
                    modelPart,
                    mainPart,
                    currentToGlobal,
                    globalToView);
                if (!data.Bounds.IsValid)
                    continue;
                result.Parts.Add(data);
                if (data.IsMain)
                    result.Main = data;
            }
            if (result.Main == null)
                return null;

            ResolveMainAxes(result);
            ReadExistingDimensions(result);
            return result.MainAxis == null || result.UpAxis == null ? null : result;
        }

        private static PartData ReadPart(
            TSM.Part part,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            PartData result = new PartData();
            result.ModelPart = part;
            result.Identifier = part.Identifier == null ? 0 : part.Identifier.ID;
            result.IsMain = SameIdentifier(part, mainPart);
            result.IsContourPlate = part is TSM.ContourPlate;
            result.SameMainAssembly = IsSameMainAssembly(part, mainPart);

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
                    P3 start = Transform3(edge.StartPoint, currentToGlobal, globalToView);
                    P3 end = Transform3(edge.EndPoint, currentToGlobal, globalToView);
                    AddSolidPoint(result, start);
                    AddSolidPoint(result, end);
                    if (start != null && end != null
                        && Distance(start.XY, end.XY) > GeometryTolerance)
                    {
                        Edge2 item = new Edge2();
                        item.Start = start.XY;
                        item.End = end.XY;
                        AddUniqueEdge(result, item);
                    }
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
                        try { data.Diameter = group.BoltSize; }
                        catch { data.Diameter = 0.0; }
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

        private static void ResolveMainAxes(ViewData view)
        {
            P3 first;
            P3 second;
            FindFarthestPair(view.Main.Reference, out first, out second);
            P2 direction = first == null || second == null
                ? null
                : Normalize(Subtract(second.XY, first.XY));
            if (direction == null)
            {
                direction = view.Main.Bounds.Width >= view.Main.Bounds.Height
                    ? new P2(1.0, 0.0)
                    : new P2(0.0, 1.0);
            }
            if (direction.X < -GeometryTolerance
                || (Math.Abs(direction.X) <= GeometryTolerance && direction.Y < 0.0))
                direction = Scale(direction, -1.0);
            P2 up = Normalize(new P2(-direction.Y, direction.X));
            if (up != null && up.Y < 0.0)
                up = Scale(up, -1.0);
            view.MainAxis = direction;
            view.UpAxis = up;

            if (first != null && second != null
                && Distance(first.XY, second.XY) > GeometryTolerance)
            {
                bool firstIsLeft = Dot(first.XY, direction)
                    <= Dot(second.XY, direction);
                view.RefLeft = firstIsLeft ? first.XY : second.XY;
                view.RefRight = firstIsLeft ? second.XY : first.XY;
            }
            else
            {
                view.RefLeft = view.Main.Reference.Count > 0
                    ? view.Main.Reference[0].XY
                    : view.Main.Bounds.Center;
                view.RefRight = view.RefLeft;
            }
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
                    item.EffectiveDirection = ReadEffectiveDirection(set);
                    view.Dimensions.Add(item);
                    if (view.Attributes == null && set.Attributes != null)
                        view.Attributes = set.Attributes;
                }
            }
            catch { }
        }

        private static void BuildTopPlans(Analysis analysis, ViewData view)
        {
            PartData transverse;
            P2 node;
            P2 farDirection;
            if (!TryResolveLongitudinalTransverse(view, out transverse, out node, out farDirection))
                return;

            P2 mainLeftBottom = Corner(view.Main, view.MainAxis, view.UpAxis, false, false);
            P2 mainRightBottom = Corner(view.Main, view.MainAxis, view.UpAxis, true, false);
            P2 transverseFar = ExtremeVertex(
                transverse,
                farDirection,
                true,
                view.MainAxis,
                false);
            P2 farReference = FindOuterSpliceReference(
                view,
                transverse,
                node,
                farDirection);
            P3 hole = FindSingletonHoleInside(view, transverse, node, farDirection);

            if (mainLeftBottom != null && farReference != null && mainRightBottom != null)
                analysis.Plans.Add(Plan(
                    view,
                    "T2-TOP-01-MAIN-CROSS-STATION",
                    "TOP-TRANSVERSE",
                    Scale(view.UpAxis, -1.0),
                    2,
                    mainLeftBottom,
                    farReference,
                    mainRightBottom));

            if (hole != null)
            {
                double side = Dot(Subtract(hole.XY, node), view.MainAxis) >= 0.0 ? 1.0 : -1.0;
                P2 sideDirection = Scale(view.MainAxis, side);
                P2 sideEdge = ExtremeVertexNear(
                    transverse,
                    sideDirection,
                    hole.XY);
                P2 farSideCorner = ExtremeCorner(
                    transverse,
                    farDirection,
                    sideDirection);
                if (sideEdge != null)
                    analysis.Plans.Add(Plan(
                        view,
                        "T2-TOP-02-BRACKET-HOLE-X",
                        "TOP-TRANSVERSE",
                        view.UpAxis,
                        0,
                        hole.XY,
                        sideEdge));
                if (transverseFar != null)
                    analysis.Plans.Add(Plan(
                        view,
                        "T2-TOP-03-BRACKET-REF-LENGTH",
                        "TOP-TRANSVERSE",
                        Scale(view.MainAxis, -1.0),
                        1,
                        transverseFar,
                        node));
                if (farSideCorner != null)
                    analysis.Plans.Add(Plan(
                        view,
                        "T2-TOP-04-BRACKET-HOLE-Y",
                        "TOP-TRANSVERSE",
                        sideDirection,
                        0,
                        farSideCorner,
                        hole.XY));
            }
        }

        private static void BuildFrontPlans(Analysis analysis, ViewData view)
        {
            double station;
            if (!TryResolveFrontTransverseStation(view, out station))
                return;
            int transverseTier = 1;
            int registeredTransverseTier;
            if (PHU_Slot09_DataCenterBeamType2Context
                    .TryGetFrontTransverseDimensionTier(
                        view.View,
                        out registeredTransverseTier))
                transverseTier = registeredTransverseTier;

            P2 leftTop = Corner(view.Main, view.MainAxis, view.UpAxis, false, true);
            P2 rightTop = Corner(view.Main, view.MainAxis, view.UpAxis, true, true);
            P2 leftBottom = Corner(view.Main, view.MainAxis, view.UpAxis, false, false);
            P2 rightBottom = Corner(view.Main, view.MainAxis, view.UpAxis, true, false);
            P2 stationTop = BoundaryAtStation(view.Main, view.MainAxis, view.UpAxis, station, true);
            P2 stationBottom = BoundaryAtStation(view.Main, view.MainAxis, view.UpAxis, station, false);

            if (leftTop != null && stationTop != null && rightTop != null)
                analysis.Plans.Add(Plan(
                    view,
                    "T2-FRONT-01-MAIN-CROSS-TOP",
                    "FRONT-TRANSVERSE",
                    view.UpAxis,
                    transverseTier,
                    leftTop,
                    stationTop,
                    rightTop));
            if (leftBottom != null && stationBottom != null && rightBottom != null)
                analysis.Plans.Add(Plan(
                    view,
                    "T2-FRONT-02-MAIN-CROSS-BOTTOM",
                    "FRONT-TRANSVERSE",
                    Scale(view.UpAxis, -1.0),
                    transverseTier,
                    leftBottom,
                    stationBottom,
                    rightBottom));

            PartData topPlate = FindLocalTransversePlate(view, station, true);
            PartData bottomPlate = FindLocalTransversePlate(view, station, false);
            P2 topEdge = NearestOuterPlateCorner(
                topPlate,
                view.MainAxis,
                view.UpAxis,
                station,
                true);
            P2 bottomEdge = NearestOuterPlateCorner(
                bottomPlate,
                view.MainAxis,
                view.UpAxis,
                station,
                false);
            if (topEdge != null && stationTop != null)
                analysis.Plans.Add(Plan(
                    view,
                    "T2-FRONT-03-TOP-PLATE-EDGE-REF",
                    "FRONT-TRANSVERSE",
                    view.UpAxis,
                    0,
                    topEdge,
                    stationTop));
            if (bottomEdge != null && stationBottom != null)
                analysis.Plans.Add(Plan(
                    view,
                    "T2-FRONT-04-BOTTOM-PLATE-EDGE-REF",
                    "FRONT-TRANSVERSE",
                    Scale(view.UpAxis, -1.0),
                    0,
                    bottomEdge,
                    stationBottom));
        }

        private static void BuildSectionPlans(Analysis analysis, ViewData view)
        {
            int before = analysis.Plans.Count;
            BuildSectionPlateHolePlans(analysis, view);
            if (analysis.Plans.Count > before)
                return;
            BuildSectionConnectionPlans(analysis, view);
        }

        private static void BuildSectionPlateHolePlans(Analysis analysis, ViewData view)
        {
            PartData plate = FindCutTopPlateWithHole(view);
            P3 hole = plate == null ? null : FindVisibleSingletonHole(view, plate);
            if (plate == null || hole == null)
                return;

            P2 mainLeftTop = Corner(view.Main, new P2(1, 0), new P2(0, 1), false, true);
            P2 mainRightTop = Corner(view.Main, new P2(1, 0), new P2(0, 1), true, true);
            P2 plateLeftTop = Corner(plate, new P2(1, 0), new P2(0, 1), false, true);
            P2 plateRightTop = Corner(plate, new P2(1, 0), new P2(0, 1), true, true);
            if (mainLeftTop == null || mainRightTop == null
                || plateLeftTop == null || plateRightTop == null)
                return;

            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-A-01-MAIN-PLATE-CHAIN",
                "SECTION-PLATE-HOLE",
                new P2(0, 1),
                2,
                mainLeftTop,
                plateLeftTop,
                plateRightTop,
                mainRightTop));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-A-02-PLATE-HOLE-CHAIN",
                "SECTION-PLATE-HOLE",
                new P2(0, 1),
                0,
                plateLeftTop,
                hole.XY,
                plateRightTop));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-A-03-MAIN-HOLE-Y",
                "SECTION-PLATE-HOLE",
                new P2(-1, 0),
                0,
                hole.XY,
                mainLeftTop));
        }

        private static void BuildSectionConnectionPlans(Analysis analysis, ViewData view)
        {
            P2 xAxis = new P2(1, 0);
            P2 yAxis = new P2(0, 1);
            P2 mainLeftTop = Corner(view.Main, xAxis, yAxis, false, true);
            P2 mainRightTop = Corner(view.Main, xAxis, yAxis, true, true);
            P2 mainLeftBottom = Corner(view.Main, xAxis, yAxis, false, false);
            P2 mainRightBottom = Corner(view.Main, xAxis, yAxis, true, false);
            if (mainLeftTop == null || mainRightTop == null
                || mainLeftBottom == null || mainRightBottom == null)
                return;

            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-01-MAIN-WIDTH-TOP",
                "SECTION-CONNECTION",
                yAxis,
                1,
                mainLeftTop,
                mainRightTop));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-02-MAIN-WIDTH-BOTTOM",
                "SECTION-CONNECTION",
                Scale(yAxis, -1.0),
                2,
                mainLeftBottom,
                mainRightBottom));

            BuildSectionVerticalNeighborLink(
                analysis,
                view,
                false,
                mainRightBottom,
                mainRightTop);
            BuildSectionVerticalNeighborLink(
                analysis,
                view,
                true,
                mainRightBottom,
                mainRightTop);
            BuildSectionBracketLink(analysis, view);
        }

        private static void BuildSectionVerticalNeighborLink(
            Analysis analysis,
            ViewData view,
            bool upper,
            P2 mainRightBottom,
            P2 mainRightTop
        )
        {
            PartData neighbor;
            PartData plate;
            P3 hole;
            if (!TryResolveVerticalNeighborLink(view, upper, out neighbor, out plate, out hole))
                return;

            double centerX = view.Main.Bounds.Center.X;
            bool rightSide = hole.X >= centerX;
            P2 horizontalDirection = upper ? new P2(0, 1) : new P2(0, -1);
            P2 sideDirection = rightSide ? new P2(1, 0) : new P2(-1, 0);
            P2 mainBoundary = upper ? mainRightTop : mainRightBottom;
            if (!rightSide)
            {
                mainBoundary = upper
                    ? Corner(view.Main, new P2(1, 0), new P2(0, 1), false, true)
                    : Corner(view.Main, new P2(1, 0), new P2(0, 1), false, false);
            }

            P2 horizontalTarget = mainBoundary;
            if (upper)
            {
                double nearY = neighbor.Bounds.MinY;
                horizontalTarget = new P2(
                    rightSide ? neighbor.Bounds.MaxX : neighbor.Bounds.MinX,
                    nearY);
            }

            string side = upper ? "UPPER" : "LOWER";
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-" + side + "-HOLE-X",
                "SECTION-CONNECTION",
                horizontalDirection,
                0,
                hole.XY,
                horizontalTarget));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-" + side + "-HOLE-Y",
                "SECTION-CONNECTION",
                sideDirection,
                upper ? 1 : 0,
                upper ? hole.XY : mainBoundary,
                upper ? mainBoundary : hole.XY));
        }

        private static void BuildSectionBracketLink(Analysis analysis, ViewData view)
        {
            PartData bracket;
            P2 node;
            P2 farDirection;
            if (!TryResolveSectionBracket(view, out bracket, out node, out farDirection))
                return;
            P2 farTop = ExtremeCorner(bracket, farDirection, new P2(0, 1));
            P2 farBottom = ExtremeCorner(bracket, farDirection, new P2(0, -1));
            P3 hole = FindWebHoleNearFarEnd(view, bracket, farDirection);
            if (farTop == null || farBottom == null || hole == null)
                return;

            P2 sideDirection = farDirection.X < 0.0 ? new P2(-1, 0) : new P2(1, 0);
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-BRACKET-01-REF-LENGTH",
                "SECTION-BRACKET",
                new P2(0, 1),
                2,
                farTop,
                node));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-BRACKET-02-HOLE-X",
                "SECTION-BRACKET",
                new P2(0, 1),
                0,
                farTop,
                hole.XY));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-BRACKET-03-DEPTH",
                "SECTION-BRACKET",
                sideDirection,
                2,
                farBottom,
                farTop));
            analysis.Plans.Add(Plan(
                view,
                "T2-SECTION-B-BRACKET-04-HOLE-Y",
                "SECTION-BRACKET",
                sideDirection,
                1,
                farTop,
                hole.XY));
        }

        private static bool TryResolveLongitudinalTransverse(
            ViewData view,
            out PartData transverse,
            out P2 node,
            out P2 farDirection)
        {
            transverse = null;
            node = null;
            farDirection = null;
            if (view == null || view.Main == null)
                return false;

            double minimumLength = Math.Max(
                500.0,
                Math.Min(view.Main.Bounds.Width, view.Main.Bounds.Height) * 2.5);
            double bestScore = Double.NegativeInfinity;
            bool ambiguous = false;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || part.IsMain || !part.SameMainAssembly
                    || part.IsContourPlate)
                    continue;

                P3 first;
                P3 second;
                FindFarthestPair(part.Reference, out first, out second);
                if (first == null || second == null)
                    continue;
                P2 referenceDirection = Normalize(Subtract(second.XY, first.XY));
                double referenceLength = Distance(first.XY, second.XY);
                if (referenceDirection == null || referenceLength < minimumLength
                    || Math.Abs(Dot(referenceDirection, view.UpAxis))
                        < DirectionCosineTolerance)
                    continue;

                P2 intersection;
                if (!TryLineIntersection(
                        view.RefLeft,
                        view.RefRight,
                        first.XY,
                        second.XY,
                        out intersection))
                    continue;
                double station = Dot(intersection, view.MainAxis);
                double mainMin = Math.Min(
                    Dot(Corner(view.Main, view.MainAxis, view.UpAxis, false, false),
                        view.MainAxis),
                    Dot(Corner(view.Main, view.MainAxis, view.UpAxis, true, false),
                        view.MainAxis));
                double mainMax = Math.Max(
                    Dot(Corner(view.Main, view.MainAxis, view.UpAxis, false, false),
                        view.MainAxis),
                    Dot(Corner(view.Main, view.MainAxis, view.UpAxis, true, false),
                        view.MainAxis));
                if (station < mainMin - GeometryTolerance
                    || station > mainMax + GeometryTolerance)
                    continue;

                P2 farther = Distance(first.XY, intersection)
                    >= Distance(second.XY, intersection)
                        ? first.XY
                        : second.XY;
                P2 candidateFar = Normalize(Subtract(farther, intersection));
                if (candidateFar == null)
                    continue;

                double solidSpan = ProjectionSpan(part.Vertices, candidateFar);
                if (solidSpan < minimumLength * 0.75)
                    continue;
                double shortSpan = ProjectionSpan(part.Vertices, view.MainAxis);
                double score = referenceLength + solidSpan - shortSpan * 0.05;
                if (score > bestScore + GeometryTolerance)
                {
                    bestScore = score;
                    transverse = part;
                    node = intersection;
                    farDirection = candidateFar;
                    ambiguous = false;
                }
                else if (Math.Abs(score - bestScore) <= GeometryTolerance
                    && transverse != null && transverse.Identifier != part.Identifier)
                {
                    ambiguous = true;
                }
            }
            return transverse != null && node != null && farDirection != null
                && !ambiguous;
        }

        private static P2 FindOuterSpliceReference(
            ViewData view,
            PartData transverse,
            P2 node,
            P2 farDirection)
        {
            if (view == null || transverse == null || node == null
                || farDirection == null)
                return null;
            double transverseFar = MaximumProjection(transverse.Vertices, farDirection);
            P2 result = null;
            double bestFar = transverseFar + GeometryTolerance;
            double bestAxisOffset = Double.PositiveInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || part.IsMain || !part.SameMainAssembly
                    || part.Identifier == transverse.Identifier)
                    continue;
                P3 first;
                P3 second;
                FindFarthestPair(part.Reference, out first, out second);
                if (first == null || second == null)
                    continue;
                P2 direction = Normalize(Subtract(second.XY, first.XY));
                double length = Distance(first.XY, second.XY);
                if (direction == null || length < 300.0
                    || Math.Abs(Dot(direction, farDirection))
                        < DirectionCosineTolerance)
                    continue;
                P2 candidate = Dot(first.XY, farDirection)
                    >= Dot(second.XY, farDirection) ? first.XY : second.XY;
                double far = Dot(candidate, farDirection);
                if (far <= transverseFar + GeometryTolerance)
                    continue;
                double offset = Math.Abs(
                    Dot(Subtract(candidate, node), view.MainAxis));
                double allowableOffset = Math.Max(
                    25.0,
                    ProjectionSpan(transverse.Vertices, view.MainAxis) * 0.55);
                if (offset > allowableOffset)
                    continue;
                if (offset < bestAxisOffset - GeometryTolerance
                    || (Math.Abs(offset - bestAxisOffset) <= GeometryTolerance
                        && far > bestFar))
                {
                    bestAxisOffset = offset;
                    bestFar = far;
                    result = candidate;
                }
            }
            return result;
        }

        private static P3 FindSingletonHoleInside(
            ViewData view,
            PartData part,
            P2 node,
            P2 farDirection)
        {
            if (view == null || part == null || node == null || farDirection == null)
                return null;
            P3 best = null;
            double bestDiameter = Double.NegativeInfinity;
            double bestDistance = Double.NegativeInfinity;
            foreach (BoltData bolt in part.Bolts.Values)
            {
                List<P3> visible = VisibleProjectedPoints(view, bolt);
                if (visible.Count != 1)
                    continue;
                P3 point = visible[0];
                if (!PointInside(part.Bounds, point.XY, GeometryTolerance * 3.0))
                    continue;
                double along = Dot(Subtract(point.XY, node), farDirection);
                double span = ProjectionSpan(part.Vertices, farDirection);
                if (along <= Math.Max(20.0, span * 0.15))
                    continue;
                if (bolt.Diameter > bestDiameter + GeometryTolerance
                    || (Math.Abs(bolt.Diameter - bestDiameter) <= GeometryTolerance
                        && along > bestDistance))
                {
                    best = point;
                    bestDiameter = bolt.Diameter;
                    bestDistance = along;
                }
            }
            return best;
        }

        private static bool TryResolveFrontTransverseStation(
            ViewData view,
            out double station)
        {
            station = Double.NaN;
            double routed;
            if (view == null
                || !PHU_Slot09_DataCenterBeamType2Context.TryGetFrontTransverseStation(
                    out routed))
                return false;
            double minimum = MinimumProjection(view.Main.Vertices, view.MainAxis);
            double maximum = MaximumProjection(view.Main.Vertices, view.MainAxis);
            if (routed < minimum - GeometryTolerance
                || routed > maximum + GeometryTolerance)
                return false;
            station = routed;
            return true;
        }

        private static P2 BoundaryAtStation(
            PartData part,
            P2 axis,
            P2 up,
            double station,
            bool upper)
        {
            if (part == null || axis == null || up == null)
                return null;
            List<P2> intersections = new List<P2>();
            for (int i = 0; i < part.Edges.Count; i++)
            {
                Edge2 edge = part.Edges[i];
                double first = Dot(edge.Start, axis) - station;
                double second = Dot(edge.End, axis) - station;
                if (Math.Abs(first) <= GeometryTolerance)
                    AddUnique(intersections, edge.Start);
                if (Math.Abs(second) <= GeometryTolerance)
                    AddUnique(intersections, edge.End);
                double denominator = first - second;
                if (Math.Abs(denominator) <= 0.000001)
                    continue;
                double t = first / denominator;
                if (t < -0.0001 || t > 1.0001)
                    continue;
                AddUnique(intersections, new P2(
                    edge.Start.X + (edge.End.X - edge.Start.X) * t,
                    edge.Start.Y + (edge.End.Y - edge.Start.Y) * t));
            }
            return Extreme(intersections, up, upper);
        }

        private static PartData FindLocalTransversePlate(
            ViewData view,
            double station,
            bool upper)
        {
            if (view == null || view.Main == null)
                return null;
            double mainUpper = MaximumProjection(view.Main.Vertices, view.UpAxis);
            double mainLower = MinimumProjection(view.Main.Vertices, view.UpAxis);
            PartData best = null;
            double bestStationGap = Double.PositiveInfinity;
            double bestOutward = Double.NegativeInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || !part.SameMainAssembly || !part.IsContourPlate
                    || part.IsMain)
                    continue;
                double partMinAxis = MinimumProjection(part.Vertices, view.MainAxis);
                double partMaxAxis = MaximumProjection(part.Vertices, view.MainAxis);
                double stationGap = IntervalDistance(station, partMinAxis, partMaxAxis);
                if (stationGap > 25.0)
                    continue;
                double partUpper = MaximumProjection(part.Vertices, view.UpAxis);
                double partLower = MinimumProjection(part.Vertices, view.UpAxis);
                double outward = upper
                    ? partUpper - mainUpper
                    : mainLower - partLower;
                bool touchesFace = upper
                    ? partLower <= mainUpper + GeometryTolerance * 3.0
                        && partUpper > mainUpper + GeometryTolerance
                    : partUpper >= mainLower - GeometryTolerance * 3.0
                        && partLower < mainLower - GeometryTolerance;
                if (!touchesFace || outward <= GeometryTolerance)
                    continue;
                if (stationGap < bestStationGap - GeometryTolerance
                    || (Math.Abs(stationGap - bestStationGap) <= GeometryTolerance
                        && outward > bestOutward))
                {
                    best = part;
                    bestStationGap = stationGap;
                    bestOutward = outward;
                }
            }
            return best;
        }

        private static P2 NearestOuterPlateCorner(
            PartData plate,
            P2 axis,
            P2 up,
            double station,
            bool upper)
        {
            if (plate == null || axis == null || up == null)
                return null;
            double targetUp = upper
                ? MaximumProjection(plate.Vertices, up)
                : MinimumProjection(plate.Vertices, up);
            P2 best = null;
            double bestGap = Double.PositiveInfinity;
            for (int i = 0; i < plate.Vertices.Count; i++)
            {
                P2 point = plate.Vertices[i].XY;
                if (Math.Abs(Dot(point, up) - targetUp) > GeometryTolerance * 2.0)
                    continue;
                double gap = Math.Abs(Dot(point, axis) - station);
                if (gap < bestGap)
                {
                    best = point;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static PartData FindCutTopPlateWithHole(ViewData view)
        {
            if (view == null || view.Main == null)
                return null;
            double mainTop = view.Main.Bounds.MaxY;
            double mainWidth = view.Main.Bounds.Width;
            PartData best = null;
            double bestDepthCenter = Double.PositiveInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || !part.SameMainAssembly || !part.IsContourPlate
                    || part.Bounds.MinY > part.Bounds.MaxY
                    || part.Bounds.MaxY <= mainTop + GeometryTolerance
                    || part.Bounds.MinY > mainTop + GeometryTolerance * 4.0
                    || part.Bounds.Width < mainWidth * 0.25
                    || part.Bounds.Width > mainWidth * 0.90
                    || !part.Depth.Overlaps(
                        view.DepthMin,
                        view.DepthMax,
                        GeometryTolerance * 2.0)
                    || FindVisibleSingletonHole(view, part) == null)
                    continue;
                double depthCenter = Math.Abs((part.Depth.Min + part.Depth.Max) * 0.5);
                if (depthCenter < bestDepthCenter)
                {
                    best = part;
                    bestDepthCenter = depthCenter;
                }
            }
            return best;
        }

        private static P3 FindVisibleSingletonHole(ViewData view, PartData part)
        {
            if (view == null || part == null)
                return null;
            P3 result = null;
            int count = 0;
            foreach (BoltData bolt in part.Bolts.Values)
            {
                List<P3> visible = VisibleProjectedPoints(view, bolt);
                if (visible.Count != 1)
                    continue;
                P3 point = visible[0];
                if (!PointInside(part.Bounds, point.XY, GeometryTolerance * 4.0))
                    continue;
                result = point;
                count++;
            }
            return count == 1 ? result : null;
        }

        private static bool TryResolveVerticalNeighborLink(
            ViewData view,
            bool upper,
            out PartData neighbor,
            out PartData plate,
            out P3 hole)
        {
            neighbor = null;
            plate = null;
            hole = null;
            if (view == null || view.Main == null)
                return false;
            double mainTop = view.Main.Bounds.MaxY;
            double mainBottom = view.Main.Bounds.MinY;
            double bestGap = Double.PositiveInfinity;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (candidate == null || candidate.IsMain
                    || candidate.SameMainAssembly || candidate.IsContourPlate
                    || candidate.Bounds.Height < view.Main.Bounds.Height * 3.0
                    || candidate.Bounds.Width < view.Main.Bounds.Width * 0.50
                    || candidate.Bounds.MaxX < view.Main.Bounds.MinX - GeometryTolerance
                    || candidate.Bounds.MinX > view.Main.Bounds.MaxX + GeometryTolerance)
                    continue;
                double gap = upper
                    ? candidate.Bounds.MinY - mainTop
                    : mainBottom - candidate.Bounds.MaxY;
                if (gap < -GeometryTolerance || gap > view.Main.Bounds.Height)
                    continue;
                if (gap < bestGap)
                {
                    bestGap = gap;
                    neighbor = candidate;
                }
            }
            if (neighbor == null)
                return false;

            BoltData shared = null;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData candidate = view.Parts[i];
                if (candidate == null || !candidate.SameMainAssembly
                    || !candidate.IsContourPlate)
                    continue;
                bool correctSide = upper
                    ? candidate.Bounds.MaxY > mainTop + GeometryTolerance
                        && candidate.Bounds.MinY <= mainTop + GeometryTolerance * 4.0
                    : candidate.Bounds.MinY < mainBottom - GeometryTolerance
                        && candidate.Bounds.MaxY >= mainBottom - GeometryTolerance * 4.0;
                if (!correctSide)
                    continue;
                foreach (int id in neighbor.Bolts.Keys)
                {
                    BoltData candidateBolt;
                    if (!candidate.Bolts.TryGetValue(id, out candidateBolt))
                        continue;
                    List<P3> visible = VisibleProjectedPoints(view, candidateBolt);
                    if (visible.Count < 1)
                        continue;
                    if (plate != null && plate.Identifier != candidate.Identifier)
                        return false;
                    plate = candidate;
                    shared = candidateBolt;
                    break;
                }
            }
            if (plate == null || shared == null)
                return false;

            List<P3> points = VisibleProjectedPoints(view, shared);
            double mainRight = view.Main.Bounds.MaxX;
            double bestRightGap = Double.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double gap = Math.Abs(points[i].X - mainRight);
                if (gap < bestRightGap)
                {
                    hole = points[i];
                    bestRightGap = gap;
                }
            }
            return hole != null;
        }

        private static bool TryResolveSectionBracket(
            ViewData view,
            out PartData bracket,
            out P2 node,
            out P2 farDirection)
        {
            bracket = null;
            node = null;
            farDirection = null;
            if (view == null || view.Main == null)
                return false;
            P2 mainReference = view.Main.Reference.Count > 0
                ? view.Main.Reference[0].XY
                : view.Main.Bounds.Center;
            double bestLength = Double.NegativeInfinity;
            bool ambiguous = false;
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || part.IsMain || !part.SameMainAssembly
                    || part.IsContourPlate
                    || part.Bounds.Width < view.Main.Bounds.Width * 1.5
                    || part.Bounds.Height < view.Main.Bounds.Height * 0.75)
                    continue;
                P3 first;
                P3 second;
                FindFarthestPair(part.Reference, out first, out second);
                if (first == null || second == null)
                    continue;
                double length = Distance(first.XY, second.XY);
                P2 direction = Normalize(Subtract(second.XY, first.XY));
                if (direction == null || length < view.Main.Bounds.Width * 2.0
                    || Math.Abs(direction.Y) > 0.10)
                    continue;
                bool firstNear = Distance(first.XY, mainReference)
                    <= Distance(second.XY, mainReference);
                P2 near = firstNear ? first.XY : second.XY;
                P2 far = firstNear ? second.XY : first.XY;
                if (Distance(near, mainReference) > view.Main.Bounds.Width * 0.15)
                    continue;
                P2 candidateDirection = Normalize(Subtract(far, near));
                if (candidateDirection == null)
                    continue;
                if (length > bestLength + GeometryTolerance)
                {
                    bestLength = length;
                    bracket = part;
                    node = near;
                    farDirection = candidateDirection;
                    ambiguous = false;
                }
                else if (Math.Abs(length - bestLength) <= GeometryTolerance
                    && bracket != null && bracket.Identifier != part.Identifier)
                {
                    ambiguous = true;
                }
            }
            return bracket != null && node != null && farDirection != null
                && !ambiguous;
        }

        private static P3 FindWebHoleNearFarEnd(
            ViewData view,
            PartData bracket,
            P2 farDirection)
        {
            if (view == null || bracket == null || farDirection == null)
                return null;
            P2 perpendicular = new P2(-farDirection.Y, farDirection.X);
            double solidFar = MaximumProjection(bracket.Vertices, farDirection);
            List<P3> best = null;
            double bestGap = Double.PositiveInfinity;
            foreach (BoltData bolt in bracket.Bolts.Values)
            {
                List<P3> points = VisibleProjectedPoints(view, bolt);
                if (points.Count < 2)
                    continue;
                double alongSpan = ProjectionSpan(points, farDirection);
                double acrossSpan = ProjectionSpan(points, perpendicular);
                if (alongSpan > GeometryTolerance * 4.0
                    || acrossSpan < 40.0)
                    continue;
                double along = 0.0;
                for (int i = 0; i < points.Count; i++)
                    along += Dot(points[i].XY, farDirection);
                along /= points.Count;
                double gap = Math.Abs(solidFar - along);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = points;
                }
            }
            if (best == null)
                return null;
            P3 result = best[0];
            for (int i = 1; i < best.Count; i++)
            {
                if (best[i].Y > result.Y)
                    result = best[i];
            }
            return result;
        }

        private static P2 Corner(
            PartData part,
            P2 horizontal,
            P2 vertical,
            bool right,
            bool upper)
        {
            if (part == null || horizontal == null || vertical == null
                || part.Vertices.Count == 0)
                return null;
            double horizontalExtreme = right
                ? MaximumProjection(part.Vertices, horizontal)
                : MinimumProjection(part.Vertices, horizontal);
            P2 best = null;
            double verticalValue = upper
                ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                P2 point = part.Vertices[i].XY;
                if (Math.Abs(Dot(point, horizontal) - horizontalExtreme)
                    > GeometryTolerance * 2.0)
                    continue;
                double value = Dot(point, vertical);
                if ((upper && value > verticalValue)
                    || (!upper && value < verticalValue))
                {
                    best = point;
                    verticalValue = value;
                }
            }
            return best;
        }

        private static P2 ExtremeCorner(
            PartData part,
            P2 primaryDirection,
            P2 secondaryDirection)
        {
            if (part == null || primaryDirection == null || secondaryDirection == null)
                return null;
            double primary = MaximumProjection(part.Vertices, primaryDirection);
            P2 best = null;
            double secondary = Double.NegativeInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                P2 point = part.Vertices[i].XY;
                if (Math.Abs(Dot(point, primaryDirection) - primary)
                    > GeometryTolerance * 2.0)
                    continue;
                double value = Dot(point, secondaryDirection);
                if (value > secondary)
                {
                    best = point;
                    secondary = value;
                }
            }
            return best;
        }

        private static P2 ExtremeVertex(
            PartData part,
            P2 primaryDirection,
            bool maximum,
            P2 secondaryDirection,
            bool secondaryMaximum)
        {
            if (part == null || primaryDirection == null || secondaryDirection == null)
                return null;
            double primary = maximum
                ? MaximumProjection(part.Vertices, primaryDirection)
                : MinimumProjection(part.Vertices, primaryDirection);
            P2 best = null;
            double secondary = secondaryMaximum
                ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                P2 point = part.Vertices[i].XY;
                if (Math.Abs(Dot(point, primaryDirection) - primary)
                    > GeometryTolerance * 2.0)
                    continue;
                double value = Dot(point, secondaryDirection);
                if ((secondaryMaximum && value > secondary)
                    || (!secondaryMaximum && value < secondary))
                {
                    best = point;
                    secondary = value;
                }
            }
            return best;
        }

        private static P2 ExtremeVertexNear(
            PartData part,
            P2 direction,
            P2 near)
        {
            if (part == null || direction == null || near == null)
                return null;
            double extreme = MaximumProjection(part.Vertices, direction);
            P2 best = null;
            double distance = Double.PositiveInfinity;
            for (int i = 0; i < part.Vertices.Count; i++)
            {
                P2 point = part.Vertices[i].XY;
                if (Math.Abs(Dot(point, direction) - extreme)
                    > GeometryTolerance * 2.0)
                    continue;
                double candidate = Distance(point, near);
                if (candidate < distance)
                {
                    best = point;
                    distance = candidate;
                }
            }
            return best;
        }

        private static DimPlan Plan(
            ViewData view,
            string name,
            string family,
            P2 direction,
            int tier,
            params P2[] points)
        {
            DimPlan result = new DimPlan();
            result.View = view;
            result.Name = name ?? String.Empty;
            result.Family = family ?? String.Empty;
            result.Direction = Normalize(direction);
            result.Tier = Math.Max(0, tier);
            for (int i = 0; points != null && i < points.Length; i++)
            {
                if (points[i] != null)
                    result.Points.Add(points[i]);
            }
            return result;
        }

        private static void ValidatePlans(List<DimPlan> plans)
        {
            if (plans == null || plans.Count == 0)
                throw new InvalidOperationException("No supplemental DIM plan was proven.");
            HashSet<string> names = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (plan == null || plan.View == null || plan.Direction == null
                    || plan.Points.Count < 2 || !names.Add(plan.Name))
                    throw new InvalidOperationException("Invalid/duplicate plan at index " + i + ".");
                P2 measurement = new P2(-plan.Direction.Y, plan.Direction.X);
                double span = ProjectionSpan(plan.Points, measurement);
                if (span <= GeometryTolerance)
                    throw new InvalidOperationException(plan.Name + " has no measurable span.");
            }
        }

        private static ExistingPlanStatus FindExistingPlan(DimPlan plan)
        {
            bool sameFeet = false;
            for (int i = 0; i < plan.View.Dimensions.Count; i++)
            {
                ExistingDimension existing = plan.View.Dimensions[i];
                if (!ProjectedFeetMatch(
                        existing.Points,
                        plan.Points,
                        plan.Direction,
                        MatchTolerance))
                    continue;
                sameFeet = true;
                if (DirectionsMatch(existing.EffectiveDirection, plan.Direction))
                {
                    return ExistingPlanStatus.Exact;
                }
            }
            return sameFeet ? ExistingPlanStatus.FootConflict : ExistingPlanStatus.Missing;
        }

        /// <summary>
        /// Post-filter owned only by the proven Data Center Beam Type2 route.
        /// It removes the Shape-generated end splice-hole chains and redundant
        /// main width/depth totals that are intentionally absent from this
        /// project's approved TOP view. Shape itself remains unchanged.
        /// </summary>
        private static List<ExistingDimension> FindRedundantTopHoleDimensions(
            Analysis analysis)
        {
            List<ExistingDimension> result = new List<ExistingDimension>();
            if (analysis == null || analysis.Top == null)
                return result;

            for (int i = 0; i < analysis.Top.Dimensions.Count; i++)
            {
                ExistingDimension existing = analysis.Top.Dimensions[i];
                if (existing == null || existing.Dimension == null)
                    continue;
                if (MatchesProtectedTopSupplementalPlan(analysis, existing))
                    continue;
                if (IsRedundantTopHoleDimension(analysis.Top, existing))
                    result.Add(existing);
            }
            return result;
        }

        private static bool MatchesProtectedTopSupplementalPlan(
            Analysis analysis,
            ExistingDimension existing)
        {
            if (analysis == null || analysis.Top == null || existing == null)
                return false;
            for (int i = 0; i < analysis.Plans.Count; i++)
            {
                DimPlan plan = analysis.Plans[i];
                if (plan == null || !Object.ReferenceEquals(plan.View, analysis.Top))
                    continue;
                if (ProjectedFeetMatch(
                        existing.Points,
                        plan.Points,
                        plan.Direction,
                        MatchTolerance)
                    && DirectionsMatch(existing.EffectiveDirection, plan.Direction))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Type-2-only replacement detector for an old Slot04 global chain.
        /// Before the Type2 route was geometry-proven, Slot04 could insert the
        /// local transverse-connection plate (and, in some drawings, the
        /// connected member REF) into the independent top-plate chain.  The
        /// current Slot04 detector excludes that local target.  This method
        /// removes only the stale chain so Slot04, which runs immediately
        /// afterwards, can recreate the correct main-edge/independent-plate
        /// relation.
        /// </summary>
        private static List<ExistingDimension> FindRedundantFrontSlot04Dimensions(
            Analysis analysis)
        {
            List<ExistingDimension> result = new List<ExistingDimension>();
            if (analysis == null || analysis.Front == null)
                return result;

            double transverseStation;
            if (!PHU_Slot09_DataCenterBeamType2Context.TryGetFrontTransverseStation(
                    out transverseStation))
                return result;

            for (int i = 0; i < analysis.Front.Dimensions.Count; i++)
            {
                ExistingDimension existing = analysis.Front.Dimensions[i];
                if (existing == null || existing.Dimension == null)
                    continue;
                if (MatchesProtectedFrontSupplementalPlan(analysis, existing))
                    continue;
                if (IsRedundantFrontSlot04Dimension(
                        analysis.Front,
                        existing,
                        transverseStation))
                    result.Add(existing);
            }
            return result;
        }

        private static bool MatchesProtectedFrontSupplementalPlan(
            Analysis analysis,
            ExistingDimension existing)
        {
            if (analysis == null || analysis.Front == null || existing == null)
                return false;
            for (int i = 0; i < analysis.Plans.Count; i++)
            {
                DimPlan plan = analysis.Plans[i];
                if (plan == null || !Object.ReferenceEquals(plan.View, analysis.Front))
                    continue;
                if (ProjectedFeetMatch(
                        existing.Points,
                        plan.Points,
                        plan.Direction,
                        MatchTolerance)
                    && DirectionsMatch(existing.EffectiveDirection, plan.Direction))
                    return true;
            }
            return false;
        }

        private static bool IsRedundantFrontSlot04Dimension(
            ViewData view,
            ExistingDimension dimension,
            double transverseStation)
        {
            if (view == null || view.Main == null || view.MainAxis == null
                || view.UpAxis == null || dimension == null
                || dimension.EffectiveDirection == null
                || dimension.Points.Count < 3 || !IsFinite(transverseStation))
                return false;

            // Slot04's FRONT chain is the upper, longitudinal chain.  Requiring
            // the signed placement direction prevents a lower-face chain from
            // ever entering this Type2 replacement path.
            if (!DirectionsMatch(dimension.EffectiveDirection, view.UpAxis))
                return false;

            double mainMin = MinimumProjection(view.Main.Vertices, view.MainAxis);
            double mainMax = MaximumProjection(view.Main.Vertices, view.MainAxis);
            double upMin = MinimumProjection(view.Main.Vertices, view.UpAxis);
            double upMax = MaximumProjection(view.Main.Vertices, view.UpAxis);
            double mainLength = mainMax - mainMin;
            double mainDepth = upMax - upMin;
            if (!IsFinite(mainLength) || !IsFinite(mainDepth)
                || mainLength <= GeometryTolerance || mainDepth <= GeometryTolerance
                || transverseStation < mainMin - MatchTolerance
                || transverseStation > mainMax + MatchTolerance)
                return false;

            double edgeTolerance = Math.Max(3.0, GeometryTolerance * 4.0);
            double contactZone = Math.Max(25.0, mainDepth * 0.2);
            bool hasTopStart = false;
            bool hasTopEnd = false;
            bool hasProtrudingLocalFoot = false;
            for (int i = 0; i < dimension.Points.Count; i++)
            {
                P2 point = dimension.Points[i];
                double along = Dot(point, view.MainAxis);
                double normal = Dot(point, view.UpAxis);
                if (Math.Abs(along - mainMin) <= edgeTolerance
                    && Math.Abs(normal - upMax) <= edgeTolerance)
                    hasTopStart = true;
                if (Math.Abs(along - mainMax) <= edgeTolerance
                    && Math.Abs(normal - upMax) <= edgeTolerance)
                    hasTopEnd = true;
                if (Math.Abs(along - transverseStation) <= contactZone
                    && normal > upMax + edgeTolerance)
                    hasProtrudingLocalFoot = true;
            }

            return hasTopStart && hasTopEnd && hasProtrudingLocalFoot;
        }

        private static bool IsRedundantTopHoleDimension(
            ViewData view,
            ExistingDimension dimension)
        {
            if (view == null || view.Main == null || view.MainAxis == null
                || view.UpAxis == null || dimension == null
                || dimension.EffectiveDirection == null
                || dimension.Points.Count < 2)
                return false;

            P2 measurement = Normalize(new P2(
                -dimension.EffectiveDirection.Y,
                dimension.EffectiveDirection.X));
            if (measurement == null)
                return false;

            double mainMin = MinimumProjection(view.Main.Vertices, view.MainAxis);
            double mainMax = MaximumProjection(view.Main.Vertices, view.MainAxis);
            double upMin = MinimumProjection(view.Main.Vertices, view.UpAxis);
            double upMax = MaximumProjection(view.Main.Vertices, view.UpAxis);
            double mainLength = mainMax - mainMin;
            double mainDepth = upMax - upMin;
            if (!IsFinite(mainLength) || !IsFinite(mainDepth)
                || mainLength <= GeometryTolerance || mainDepth <= GeometryTolerance)
                return false;

            double edgeTolerance = Math.Max(3.0, GeometryTolerance * 4.0);
            double endZone = Math.Max(mainLength * 0.18, mainDepth * 2.0);
            bool measuresAlongMain =
                Math.Abs(Dot(measurement, view.MainAxis))
                    >= DirectionCosineTolerance;
            bool measuresAlongUp =
                Math.Abs(Dot(measurement, view.UpAxis))
                    >= DirectionCosineTolerance;

            bool touchesMainStart = false;
            bool touchesMainEnd = false;
            bool touchesBottom = false;
            bool touchesTop = false;
            for (int i = 0; i < dimension.Points.Count; i++)
            {
                double along = Dot(dimension.Points[i], view.MainAxis);
                double normal = Dot(dimension.Points[i], view.UpAxis);
                if (Math.Abs(along - mainMin) <= edgeTolerance)
                    touchesMainStart = true;
                if (Math.Abs(along - mainMax) <= edgeTolerance)
                    touchesMainEnd = true;
                if (Math.Abs(normal - upMin) <= edgeTolerance)
                    touchesBottom = true;
                if (Math.Abs(normal - upMax) <= edgeTolerance)
                    touchesTop = true;
            }

            if (measuresAlongMain && touchesMainStart && touchesMainEnd)
            {
                if (dimension.Points.Count == 2)
                {
                    double firstUp = Dot(dimension.Points[0], view.UpAxis);
                    double secondUp = Dot(dimension.Points[1], view.UpAxis);
                    bool sameMainFace = Math.Abs(firstUp - secondUp)
                        <= edgeTolerance * 2.0;
                    bool onOuterFace = Math.Abs(firstUp - upMin) <= edgeTolerance
                        || Math.Abs(firstUp - upMax) <= edgeTolerance;
                    return sameMainFace && onOuterFace;
                }

                if (dimension.Points.Count >= 4)
                {
                    bool hasStartCluster = false;
                    bool hasEndCluster = false;
                    for (int i = 0; i < dimension.Points.Count; i++)
                    {
                        double along = Dot(dimension.Points[i], view.MainAxis);
                        if (along <= mainMin + endZone)
                            hasStartCluster = true;
                        else if (along >= mainMax - endZone)
                            hasEndCluster = true;
                        else
                            return false;
                    }
                    return hasStartCluster && hasEndCluster;
                }
            }

            if (measuresAlongUp && touchesBottom && touchesTop)
            {
                bool allAtStart = true;
                bool allAtEnd = true;
                for (int i = 0; i < dimension.Points.Count; i++)
                {
                    double along = Dot(dimension.Points[i], view.MainAxis);
                    if (along > mainMin + endZone)
                        allAtStart = false;
                    if (along < mainMax - endZone)
                        allAtEnd = false;
                }
                return allAtStart || allAtEnd;
            }

            return false;
        }

        private static bool ProjectedFeetMatch(
            List<P2> first,
            List<P2> second,
            P2 placementDirection,
            double tolerance)
        {
            if (first == null || second == null || placementDirection == null
                || first.Count != second.Count)
                return false;
            P2 measurement = Normalize(new P2(
                -placementDirection.Y,
                placementDirection.X));
            if (measurement == null)
                return false;
            List<double> a = new List<double>();
            List<double> b = new List<double>();
            for (int i = 0; i < first.Count; i++)
                a.Add(Dot(first[i], measurement));
            for (int i = 0; i < second.Count; i++)
                b.Add(Dot(second[i], measurement));
            a.Sort();
            b.Sort();
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i] - b[i]) > tolerance)
                    return false;
            }
            return true;
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

        private static P2 ReadEffectiveDirection(object dimension)
        {
            object value = GetMember(dimension, "UpDirection")
                ?? GetMember(dimension, "OffsetDirection");
            P2 result = Normalize(new P2(
                ReadDouble(value, "X"),
                ReadDouble(value, "Y")));
            double distance = ReadDouble(dimension, "Distance");
            if (result != null && IsFinite(distance) && distance < 0.0)
                result = Scale(result, -1.0);
            return result;
        }

        private static void ResolveShapeXTierSpacing(
            double scale,
            out double tierBase,
            out double tierStep)
        {
            int rounded = IsFinite(scale) && scale > 0.0
                ? Convert.ToInt32(Math.Round(scale)) : 15;
            switch (rounded)
            {
                case 5: tierBase = tierStep = 50.0; return;
                case 10: tierBase = tierStep = 100.0; return;
                case 15: tierBase = tierStep = 150.0; return;
                case 20: tierBase = tierStep = 200.0; return;
                case 30: tierBase = tierStep = 300.0; return;
                default:
                    // ShapeScript initializes every unsupported scale with its
                    // established scale-15 default.  Keep this supplement on
                    // that exact contract instead of inventing a new spacing.
                    tierBase = tierStep = 150.0;
                    return;
            }
        }

        private static bool TryLineIntersection(
            P2 firstStart,
            P2 firstEnd,
            P2 secondStart,
            P2 secondEnd,
            out P2 intersection)
        {
            intersection = null;
            if (firstStart == null || firstEnd == null
                || secondStart == null || secondEnd == null)
                return false;
            P2 r = Subtract(firstEnd, firstStart);
            P2 s = Subtract(secondEnd, secondStart);
            double cross = Cross(r, s);
            if (Math.Abs(cross) <= 0.000001)
                return false;
            double t = Cross(Subtract(secondStart, firstStart), s) / cross;
            intersection = Add(firstStart, Scale(r, t));
            return IsFinite(intersection.X) && IsFinite(intersection.Y);
        }

        private static List<P3> VisibleProjectedPoints(ViewData view, BoltData bolt)
        {
            List<P3> result = new List<P3>();
            if (view == null || bolt == null)
                return result;
            for (int i = 0; i < bolt.Points.Count; i++)
            {
                P3 point = bolt.Points[i];
                if (point.Z < view.DepthMin - GeometryTolerance * 3.0
                    || point.Z > view.DepthMax + GeometryTolerance * 3.0)
                    continue;
                bool duplicate = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (Distance(result[j].XY, point.XY) <= GeometryTolerance)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    result.Add(point);
            }
            return result;
        }

        private static bool PointInside(Bounds2 bounds, P2 point, double tolerance)
        {
            return bounds != null && bounds.IsValid && point != null
                && point.X >= bounds.MinX - tolerance
                && point.X <= bounds.MaxX + tolerance
                && point.Y >= bounds.MinY - tolerance
                && point.Y <= bounds.MaxY + tolerance;
        }

        private static double IntervalDistance(double value, double minimum, double maximum)
        {
            if (value < minimum) return minimum - value;
            if (value > maximum) return value - maximum;
            return 0.0;
        }

        private static double ProjectionSpan(List<P3> points, P2 direction)
        {
            return MaximumProjection(points, direction)
                - MinimumProjection(points, direction);
        }

        private static double ProjectionSpan(List<P2> points, P2 direction)
        {
            if (points == null || points.Count == 0 || direction == null)
                return 0.0;
            double minimum = Double.PositiveInfinity;
            double maximum = Double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double value = Dot(points[i], direction);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
            return maximum - minimum;
        }

        private static double MinimumProjection(List<P3> points, P2 direction)
        {
            double result = Double.PositiveInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
                result = Math.Min(result, Dot(points[i].XY, direction));
            return result;
        }

        private static double MaximumProjection(List<P3> points, P2 direction)
        {
            double result = Double.NegativeInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
                result = Math.Max(result, Dot(points[i].XY, direction));
            return result;
        }

        private static P2 Extreme(List<P2> points, P2 direction, bool maximum)
        {
            P2 result = null;
            double best = maximum ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                double value = Dot(points[i], direction);
                if ((maximum && value > best) || (!maximum && value < best))
                {
                    result = points[i];
                    best = value;
                }
            }
            return result;
        }

        private static void AddSolidPoint(PartData part, P3 point)
        {
            if (part == null || point == null)
                return;
            part.Bounds.Add(point.XY);
            part.Depth.Add(point.Z);
            AddUnique(part.Vertices, point);
        }

        private static void AddUnique(List<P3> points, P3 point)
        {
            if (points == null || point == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i].XY, point.XY) <= GeometryTolerance
                    && Math.Abs(points[i].Z - point.Z) <= GeometryTolerance)
                    return;
            }
            points.Add(point);
        }

        private static void AddUnique(List<P2> points, P2 point)
        {
            if (points == null || point == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], point) <= GeometryTolerance)
                    return;
            }
            points.Add(point);
        }

        private static void AddUniqueEdge(PartData part, Edge2 edge)
        {
            if (part == null || edge == null || edge.Start == null || edge.End == null)
                return;
            for (int i = 0; i < part.Edges.Count; i++)
            {
                Edge2 current = part.Edges[i];
                bool forward = Distance(current.Start, edge.Start) <= GeometryTolerance
                    && Distance(current.End, edge.End) <= GeometryTolerance;
                bool reverse = Distance(current.Start, edge.End) <= GeometryTolerance
                    && Distance(current.End, edge.Start) <= GeometryTolerance;
                if (forward || reverse)
                    return;
            }
            part.Edges.Add(edge);
        }

        private static void FindFarthestPair(
            List<P3> points,
            out P3 first,
            out P3 second)
        {
            first = null;
            second = null;
            double best = Double.NegativeInfinity;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = Distance(points[i].XY, points[j].XY);
                    if (distance > best)
                    {
                        best = distance;
                        first = points[i];
                        second = points[j];
                    }
                }
            }
        }

        private static bool IsSameMainAssembly(TSM.Part part, TSM.Part mainPart)
        {
            try
            {
                TSM.Assembly assembly = part == null ? null : part.GetAssembly();
                TSM.Part assemblyMain = assembly == null
                    ? null : assembly.GetMainPart() as TSM.Part;
                return SameIdentifier(assemblyMain, mainPart);
            }
            catch { return false; }
        }

        private static bool SameIdentifier(
            TSM.ModelObject first,
            TSM.ModelObject second)
        {
            return first != null && second != null
                && first.Identifier != null && second.Identifier != null
                && first.Identifier.ID == second.Identifier.ID;
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
                Tekla.Structures.Identifier identifier = value == null
                    ? null : GetMember(value, "Identifier")
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
            object raw = GetMember(value, name);
            if (raw == null)
                return Double.NaN;
            try { return Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
            catch { return Double.NaN; }
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
            return point == null ? null : new P2(point.X * factor, point.Y * factor);
        }

        private static P2 Normalize(P2 point)
        {
            if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                return null;
            double length = Math.Sqrt(point.X * point.X + point.Y * point.Y);
            return length <= GeometryTolerance
                ? null : new P2(point.X / length, point.Y / length);
        }

        private static double Dot(P2 first, P2 second)
        {
            return first == null || second == null
                ? Double.NaN : first.X * second.X + first.Y * second.Y;
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

        private static void DisableCombine(TSD.StraightDimensionSet dimension)
        {
            if (dimension == null || dimension.Attributes == null)
                return;
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension
                ?? new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format = TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            combined.MinimumNumberToCombine = 5;
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;
            dimension.Modify();
        }

        private static void DeleteCreated(List<TSD.StraightDimensionSet> created)
        {
            for (int i = 0; created != null && i < created.Count; i++)
            {
                try { if (created[i] != null) created[i].Delete(); }
                catch { }
            }
        }

        private static void TryCommit(TSD.Drawing drawing)
        {
            try { if (drawing != null) drawing.CommitChanges(); }
            catch { }
        }

        private static void ResetResult()
        {
            LastRunApplicable = false;
            LastRunSucceeded = false;
            LastCreatedCount = 0;
            LastReusedCount = 0;
            LastConflictCount = 0;
            LastDeletedRedundantTopCount = 0;
            LastDeletedRedundantFrontSlot04Count = 0;
            LastRunMessage = String.Empty;
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

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static PartData SyntheticPolygonPart(
            P2[] polygon,
            bool isMain,
            bool sameAssembly,
            bool contourPlate)
        {
            PartData result = new PartData();
            result.IsMain = isMain;
            result.SameMainAssembly = sameAssembly;
            result.IsContourPlate = contourPlate;
            result.Identifier = isMain ? 1 : 2;
            if (polygon == null)
                return result;
            for (int i = 0; i < polygon.Length; i++)
            {
                P2 point = polygon[i];
                P3 vertex = new P3(point.X, point.Y, 0.0);
                AddSolidPoint(result, vertex);
                P2 next = polygon[(i + 1) % polygon.Length];
                Edge2 edge = new Edge2();
                edge.Start = new P2(point.X, point.Y);
                edge.End = new P2(next.X, next.Y);
                AddUniqueEdge(result, edge);
            }
            return result;
        }

        private static PartData SyntheticRectanglePart(
            int identifier,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool isMain,
            bool sameAssembly,
            bool contourPlate,
            double depth)
        {
            PartData result = SyntheticPolygonPart(new P2[]
            {
                new P2(minX, minY), new P2(maxX, minY),
                new P2(maxX, maxY), new P2(minX, maxY)
            }, isMain, sameAssembly, contourPlate);
            result.Identifier = identifier;
            result.Depth.Min = depth;
            result.Depth.Max = depth;
            result.Depth.IsValid = true;
            for (int i = 0; i < result.Vertices.Count; i++)
                result.Vertices[i].Z = depth;
            return result;
        }

        private static BoltData SyntheticBolt(
            int identifier,
            double diameter,
            params P3[] points)
        {
            BoltData result = new BoltData();
            result.Identifier = identifier;
            result.Diameter = diameter;
            for (int i = 0; points != null && i < points.Length; i++)
                AddUnique(result.Points, points[i]);
            return result;
        }

        private static ViewData BuildSyntheticPlateSection()
        {
            ViewData view = new ViewData();
            view.Identifier = 101;
            view.Scale = 15.0;
            view.DepthMin = -10.0;
            view.DepthMax = 10.0;
            PartData main = SyntheticRectanglePart(
                1, 0, 200, -147, 147, true, true, false, 0.0);
            main.Reference.Add(new P3(100, 147, 0));
            PartData plate = SyntheticRectanglePart(
                2, 37.5, 162.5, 147, 247, false, true, true, 0.0);
            plate.Bolts.Add(201, SyntheticBolt(
                201, 18.0, new P3(100, 202, 0)));
            PartData outOfCut = SyntheticRectanglePart(
                3, 37.5, 162.5, 147, 247, false, true, true, 100.0);
            outOfCut.Bolts.Add(202, SyntheticBolt(
                202, 18.0, new P3(100, 202, 100)));
            view.Main = main;
            view.Parts.Add(main);
            view.Parts.Add(outOfCut);
            view.Parts.Add(plate);
            return view;
        }

        private static ViewData BuildSyntheticTopFilterView()
        {
            ViewData view = new ViewData();
            view.Identifier = 100;
            view.Scale = 15.0;
            view.MainAxis = new P2(1, 0);
            view.UpAxis = new P2(0, 1);
            view.Main = SyntheticRectanglePart(
                1, 0, 2390, -100, 100, true, true, false, 0.0);
            view.Parts.Add(view.Main);
            return view;
        }

        private static ViewData BuildSyntheticFrontSlot04FilterView()
        {
            ViewData view = new ViewData();
            view.Identifier = 102;
            view.Scale = 15.0;
            view.MainAxis = new P2(1, 0);
            view.UpAxis = new P2(0, 1);
            view.Main = SyntheticRectanglePart(
                1, 0, 2390, -147, 147, true, true, false, 0.0);
            view.Parts.Add(view.Main);
            return view;
        }

        private static ExistingDimension SyntheticDimension(
            P2 direction,
            params P2[] points)
        {
            ExistingDimension result = new ExistingDimension();
            result.EffectiveDirection = Normalize(direction);
            for (int i = 0; points != null && i < points.Length; i++)
            {
                if (points[i] != null)
                    result.Points.Add(points[i]);
            }
            return result;
        }

        private static ViewData BuildSyntheticConnectionSection(
            bool lower,
            bool upper,
            bool mirror,
            bool shuffle)
        {
            Func<double, double> mx = delegate(double x)
            {
                return mirror ? 200.0 - x : x;
            };
            ViewData view = new ViewData();
            view.Identifier = mirror ? 203 : 202;
            view.Scale = 15.0;
            view.DepthMin = -10.0;
            view.DepthMax = 10.0;
            PartData main = SyntheticRectanglePart(
                1, 0, 200, -147, 147, true, true, false, 0.0);
            main.Reference.Add(new P3(100, 147, 0));
            view.Main = main;
            view.Parts.Add(main);

            double bracketMin = Math.Min(mx(-515), mx(96));
            double bracketMax = Math.Max(mx(-515), mx(96));
            PartData bracket = SyntheticRectanglePart(
                2, bracketMin, bracketMax, -147, 147,
                false, true, false, 0.0);
            bracket.Reference.Add(new P3(mx(100), 147, 0));
            bracket.Reference.Add(new P3(mx(-525), 147, 0));
            bracket.Bolts.Add(301, SyntheticBolt(
                301,
                20.0,
                new P3(mx(-475), -60, 0),
                new P3(mx(-475), 0, 0),
                new P3(mx(-475), 60, 0)));
            view.Parts.Add(bracket);

            if (lower)
                AddSyntheticVerticalLink(view, false, mx);
            if (upper)
                AddSyntheticVerticalLink(view, true, mx);
            if (shuffle)
                view.Parts.Reverse();
            return view;
        }

        private static void AddSyntheticVerticalLink(
            ViewData view,
            bool upper,
            Func<double, double> mx)
        {
            int baseId = upper ? 500 : 400;
            double neighborMinY = upper ? 167 : -2000;
            double neighborMaxY = upper ? 2000 : -167;
            PartData neighbor = SyntheticRectanglePart(
                baseId,
                0,
                200,
                neighborMinY,
                neighborMaxY,
                false,
                false,
                false,
                0.0);
            double holeY = upper ? 207 : -207;
            BoltData shared = SyntheticBolt(
                baseId + 1,
                20.0,
                new P3(mx(70), holeY, 0),
                new P3(mx(130), holeY, 0));
            neighbor.Bolts.Add(shared.Identifier, shared);

            PartData plate = SyntheticRectanglePart(
                baseId + 2,
                30,
                170,
                upper ? 147 : -247,
                upper ? 247 : -147,
                false,
                true,
                true,
                0.0);
            BoltData plateBolt = SyntheticBolt(
                shared.Identifier,
                20.0,
                new P3(mx(70), holeY, 0),
                new P3(mx(130), holeY, 0));
            plate.Bolts.Add(plateBolt.Identifier, plateBolt);
            view.Parts.Add(neighbor);
            view.Parts.Add(plate);
        }

        private static void RemoveSyntheticBracket(ViewData view)
        {
            if (view == null)
                return;
            for (int i = view.Parts.Count - 1; i >= 0; i--)
            {
                PartData part = view.Parts[i];
                if (part != null && part.Identifier == 2)
                    view.Parts.RemoveAt(i);
            }
        }

        private static string PlanFingerprint(List<DimPlan> plans)
        {
            List<string> items = new List<string>();
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                P2 measurement = new P2(-plan.Direction.Y, plan.Direction.X);
                List<double> feet = new List<double>();
                for (int p = 0; p < plan.Points.Count; p++)
                    feet.Add(Dot(plan.Points[p], measurement));
                feet.Sort();
                StringBuilder item = new StringBuilder();
                item.Append(plan.Name).Append('|')
                    .Append(Format(plan.Direction.X)).Append(',')
                    .Append(Format(plan.Direction.Y)).Append('|');
                for (int p = 0; p < feet.Count; p++)
                {
                    if (p > 0) item.Append(',');
                    item.Append(Format(feet[p]));
                }
                items.Add(item.ToString());
            }
            items.Sort(StringComparer.Ordinal);
            return String.Join(";", items.ToArray());
        }

        private static DimPlan FindPlanByName(
            List<DimPlan> plans,
            string name)
        {
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                if (plans[i] != null && String.Equals(
                        plans[i].Name,
                        name,
                        StringComparison.Ordinal))
                    return plans[i];
            }
            return null;
        }
    }
}
