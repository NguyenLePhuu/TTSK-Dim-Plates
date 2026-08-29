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
    /// Inzai-only column-to-neighbor dimension engine.  It is intentionally
    /// independent from PHU_ColumnGridDimensionEngine: geometry and DIM feet
    /// remain isolated.  The engines share only project routing, the one-click
    /// coordinator, and the active PHU_Shape_X tier contract.
    /// </summary>
    public static class PHU_InzaiColumnNeighborDimensionEngine
    {
        private const double GeometryTolerance = 0.20;
        private const double MatchTolerance = 2.0;
        private const double DirectionCosineTolerance = 0.985;
        private const double MinimumNeighborReferenceLength = 250.0;

        private const string ConnectionBolt = "Geo_13-Col_To_Gir(Bolt)";
        private const string ConnectionFlange = "Geo_11-Col_To_Gir(FLG)";

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

        private sealed class Bounds2
        {
            public bool IsValid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

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

        private sealed class ViewCapture
        {
            public int Identifier;
            public TSD.View View;
            public TSG.Point OriginalOrigin;
            public bool IsLeftOnSheet;
        }

        private sealed class ExistingDimension
        {
            public TSD.StraightDimensionSet Dimension;
            public readonly List<P2> Points = new List<P2>();
            public P2 Direction;
            public double Distance;
            public double LineCoordinate;
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes Attributes;
        }

        private sealed class NeighborLink
        {
            public int NeighborIdentifier;
            public int PlateIdentifier;
            public int BoltIdentifier;
            public string ConnectionName;
            public int Side;
            public double ReferenceY;
            public double OppositeEdgeY;
            public double NearSolidX;
            public double PlateOuterX;
            public P2 ReferenceAtColumnAxis;
            public P2 ReferenceAtNeighborEdge;
            public P2 FirstHole;
            public bool RequiresColumnEdgeDimension;
            public Bounds2 NeighborBounds;
            public Bounds2 PlateBounds;
        }

        private sealed class NeighborLevel
        {
            public double ReferenceY;
            public NeighborLink Left;
            public NeighborLink Right;

            public int LinkCount
            {
                get { return (Left == null ? 0 : 1) + (Right == null ? 0 : 1); }
            }
        }

        private sealed class ViewGeometry
        {
            public ViewCapture Capture;
            public TSD.View View;
            public double Scale;
            public double MainRefX;
            public Bounds2 MainBounds;
            public readonly List<NeighborLink> Links = new List<NeighborLink>();
            public readonly List<NeighborLevel> Levels = new List<NeighborLevel>();
            public readonly List<ExistingDimension> Dimensions = new List<ExistingDimension>();
        }

        private sealed class DimPlan
        {
            public string Name;
            public ViewGeometry View;
            public P2 Direction;
            public double Distance;
            public bool DisableCombine;
            public TSD.StraightDimensionSet.StraightDimensionSetAttributes Attributes;
            public readonly List<P2> Points = new List<P2>();
        }

        private sealed class OverallTotalRelocation
        {
            public ViewGeometry View;
            public ExistingDimension Existing;
            public double OriginalDistance;
            public double TargetDistance;
            public double TargetLine;
            public bool Applied;
        }

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public string ProjectKey;
            public ViewCapture Left;
            public ViewCapture Right;
            public bool Applicable;
            public int SupportedRelationEvidenceCount;
            public int SkippedRelationCount;
            public readonly List<string> SkipReasons = new List<string>();
            public readonly List<string> ExpectedTopology = new List<string>();
        }

        private static bool _enabled;
        private static Context _context;

        public static bool LastRunApplicable { get; private set; }
        public static bool LastRunSucceeded { get; private set; }
        public static int LastCreatedCount { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Configure(bool enabled)
        {
            ClearState();
            PHU_ColumnDimensionTierContext.ClearNeighborTiers();
            _enabled = enabled;
        }

        public static void Reset()
        {
            ClearState();
            PHU_ColumnDimensionTierContext.ClearNeighborTiers();
        }

        /// <summary>
        /// Read-only preflight before Shape.  It captures the user's view order
        /// and proves every supported beam -> bolt -> column-assembly plate
        /// relation.  No drawing object is mutated here.
        /// </summary>
        public static bool Begin(TSM.Model model, TSD.Drawing drawing, TSM.Part mainPart)
        {
            if (!_enabled)
                return false;

            Context context = new Context();
            PHU_ColumnDimensionTierContext.ClearNeighborTiers();
            context.Model = model;
            context.Drawing = drawing;
            context.MainPart = mainPart;
            _context = context;
            LastRunApplicable = false;
            LastRunSucceeded = false;
            LastCreatedCount = 0;

            try
            {
                if (
                    model == null
                    || drawing == null
                    || mainPart == null
                    || !model.GetConnectionStatus()
                )
                    throw new InvalidOperationException(
                        "Active Model, Drawing, or MainPart is unavailable."
                    );

                string projectKey;
                string routeMessage;
                if (
                    !PHU_ColumnProjectRouter.TryResolve(model, out projectKey, out routeMessage)
                    || !String.Equals(
                        projectKey,
                        PHU_ColumnProjectRouter.InzaiDataCenterKey,
                        StringComparison.Ordinal
                    )
                )
                    throw new InvalidOperationException(
                        "Neighbor DIM is not routed to Inzai Data Center. " + routeMessage
                    );
                context.ProjectKey = projectKey;

                TSG.Matrix currentToGlobal = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                List<ViewCapture> captures = FindTwoVerticalMainViews(
                    drawing,
                    mainPart,
                    currentToGlobal
                );
                if (captures.Count != 2)
                    throw new InvalidOperationException(
                        "Inzai Neighbor DIM requires exactly two vertical MainPart views; found "
                            + captures.Count.ToString(CultureInfo.InvariantCulture)
                            + "."
                    );
                ApplyCapturedUserOrder(captures);
                context.Left = captures[0];
                context.Right = captures[1];
                context.Left.IsLeftOnSheet = true;
                context.Right.IsLeftOnSheet = false;

                context.SupportedRelationEvidenceCount = CountSupportedRelations(
                    context,
                    currentToGlobal
                );
                if (context.SupportedRelationEvidenceCount == 0)
                {
                    context.Applicable = false;
                    LastRunApplicable = false;
                    LastRunSucceeded = true;
                    LastRunMessage =
                        "Inzai Neighbor DIM skipped: no Geo_11/Geo_13 column-neighbor relation was found.";
                    return true;
                }

                ViewGeometry left = ReadViewGeometry(context, context.Left, currentToGlobal, false);
                ViewGeometry right = ReadViewGeometry(
                    context,
                    context.Right,
                    currentToGlobal,
                    false
                );
                int validRelationCount = left.Links.Count + right.Links.Count;
                if (validRelationCount == 0)
                {
                    context.Applicable = false;
                    LastRunApplicable = false;
                    LastRunSucceeded = true;
                    LastRunMessage =
                        "Inzai Neighbor DIM skipped: supported relations were found, but none had certain drawing geometry."
                        + DescribeSkippedRelations(context);
                    return true;
                }
                ValidateSupportedTopology(left, right);
                CaptureTopology(context, left, right);

                context.Applicable = true;
                LastRunApplicable = true;
                LastRunMessage =
                    "Inzai Neighbor preflight passed: "
                    + DescribeViewTopology(left)
                    + " | "
                    + DescribeViewTopology(right)
                    + "."
                    + DescribeSkippedRelations(context);
                return true;
            }
            catch (Exception ex)
            {
                LastRunApplicable = false;
                LastRunSucceeded = true;
                context.Applicable = false;
                LastRunMessage = "Inzai Neighbor DIM skipped: " + ex.Message;
                return true;
            }
        }

        /// <summary>
        /// Mutating phase.  MainForm invokes this after Shape and before the
        /// Column Grid DIM.  Shape cannot erase these dimensions and Grid uses
        /// the shared Shape tier handoff to remain the final/farthest tier.
        /// </summary>
        public static bool ExecuteAfterShape()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            List<OverallTotalRelocation> relocations = new List<OverallTotalRelocation>();
            PHU_ColumnDimensionTierContext.ClearNeighborTiers();
            try
            {
                if (!_enabled)
                    return true;
                if (_context == null)
                    throw new InvalidOperationException(
                        "Neighbor preflight context is unavailable."
                    );
                if (!_context.Applicable)
                    return LastRunSucceeded;

                List<DimPlan> plans = BuildPreparedPlans(true);
                ValidatePlans(plans);
                relocations = BuildOverallTotalRelocations(plans);

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList points = new TSD.PointList();
                    for (int p = 0; p < plan.Points.Count; p++)
                        points.Add(plan.Points[p].ToPoint());

                    TSD.StraightDimensionSet dimension =
                        plan.Attributes == null
                            ? handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                new TSG.Vector(plan.Direction.X, plan.Direction.Y, 0.0),
                                plan.Distance
                            )
                            : handler.CreateDimensionSet(
                                plan.View.View,
                                points,
                                new TSG.Vector(plan.Direction.X, plan.Direction.Y, 0.0),
                                plan.Distance,
                                plan.Attributes
                            );
                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla did not create " + plan.Name + "."
                        );
                    created.Add(dimension);
                    if (plan.DisableCombine)
                        DisableCombine(dimension, plan.Points.Count, plan.Name);
                }

                if (created.Count != plans.Count)
                    throw new InvalidOperationException(
                        "Created Neighbor DIM count does not match the validated plan count."
                    );
                VerifyCreatedDimensions(plans, created);
                ApplyOverallTotalRelocations(relocations);
                _context.Drawing.CommitChanges();
                RegisterOutermostVerticalTiers(plans);

                LastRunSucceeded = true;
                LastCreatedCount = created.Count;
                LastRunMessage =
                    "Inzai Neighbor created "
                    + created.Count.ToString(CultureInfo.InvariantCulture)
                    + " DIMs and relocated "
                    + relocations.Count.ToString(CultureInfo.InvariantCulture)
                    + " surviving Shape totals after Shape and before Grid.";
                return true;
            }
            catch (Exception ex)
            {
                PHU_ColumnDimensionTierContext.ClearNeighborTiers();
                DeleteCreated(created);
                RestoreOverallTotalRelocations(relocations);
                TryCommit(_context == null ? null : _context.Drawing);
                // Neighbor is an optional project rule.  An uncertain or
                // changed relation is skipped without blocking Shape/Grid.
                LastRunSucceeded = true;
                LastCreatedCount = 0;
                LastRunApplicable = false;
                LastRunMessage =
                    "Inzai Neighbor DIM skipped and its partial DIMs were rolled back: "
                    + ex.Message;
                return true;
            }
        }

        /// <summary>
        /// Read-only live audit.  It rebuilds the exact final plans and prints
        /// every semantic foot without Modify/Insert/Delete/CommitChanges.
        /// </summary>
        public static string AuditPreparedPlans()
        {
            if (_context == null)
                return "INZAI NEIGHBOR AUDIT unavailable: Begin has not run.";
            if (!_context.Applicable)
                return "INZAI NEIGHBOR AUDIT not applicable: " + LastRunMessage;

            try
            {
                List<DimPlan> plans = BuildPreparedPlans(true);
                ValidatePlans(plans);
                List<OverallTotalRelocation> relocations = BuildOverallTotalRelocations(plans);
                StringBuilder text = new StringBuilder();
                text.AppendLine("INZAI COLUMN NEIGHBOR PLAN AUDIT - READ ONLY");
                text.AppendLine(
                    "No Modify / Insert / Delete / CommitChanges / LoadAttributes calls."
                );
                text.AppendLine("Project=" + _context.ProjectKey);
                text.AppendLine("PlanCount=" + plans.Count.ToString(CultureInfo.InvariantCulture));
                text.AppendLine(
                    "OverallTotalRelocationCount="
                        + relocations.Count.ToString(CultureInfo.InvariantCulture)
                );
                for (int r = 0; r < relocations.Count; r++)
                {
                    OverallTotalRelocation relocation = relocations[r];
                    text.Append("RELOCATE view=")
                        .Append(relocation.View.Capture.Identifier)
                        .Append(" originalLine=")
                        .Append(Format(relocation.Existing.LineCoordinate))
                        .Append(" targetLine=")
                        .Append(Format(relocation.TargetLine))
                        .Append(" feet=");
                    for (int p = 0; p < relocation.Existing.Points.Count; p++)
                    {
                        if (p > 0)
                            text.Append(';');
                        text.Append(FormatPoint(relocation.Existing.Points[p]));
                    }
                    text.AppendLine();
                }
                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    text.Append("PLAN ")
                        .Append(i + 1)
                        .Append(" view=")
                        .Append(plan.View.Capture.Identifier)
                        .Append(" name=")
                        .Append(plan.Name)
                        .Append(" dir=")
                        .Append(FormatPoint(plan.Direction))
                        .Append(" distance=")
                        .Append(Format(plan.Distance))
                        .Append(" line=")
                        .Append(Format(GetPlanLineCoordinate(plan)))
                        .Append(" feet=");
                    for (int p = 0; p < plan.Points.Count; p++)
                    {
                        if (p > 0)
                            text.Append(';');
                        text.Append(FormatPoint(plan.Points[p]));
                    }
                    text.AppendLine();
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "INZAI NEIGHBOR AUDIT failed: " + ex.Message;
            }
        }

        private static List<DimPlan> BuildPreparedPlans(bool readDimensions)
        {
            RebindCapturedViews(_context);
            TSG.Matrix currentToGlobal = _context
                .Model.GetWorkPlaneHandler()
                .GetCurrentTransformationPlane()
                .TransformationMatrixToGlobal;
            ViewGeometry left = ReadViewGeometry(
                _context,
                _context.Left,
                currentToGlobal,
                readDimensions
            );
            ViewGeometry right = ReadViewGeometry(
                _context,
                _context.Right,
                currentToGlobal,
                readDimensions
            );
            ValidateSupportedTopology(left, right);
            VerifyTopologyUnchanged(_context, left, right);

            List<DimPlan> plans = new List<DimPlan>();
            plans.AddRange(BuildViewPlans(left));
            plans.AddRange(BuildViewPlans(right));
            return plans;
        }

        private static List<DimPlan> BuildViewPlans(ViewGeometry view)
        {
            List<DimPlan> plans = new List<DimPlan>();
            if (view == null || view.Links.Count == 0)
                return plans;

            double tierBase;
            double tierStep;
            string tierMessage;
            if (
                !PHU_ColumnDimensionTierContext.TryGetShapeSpacing(
                    out tierBase,
                    out tierStep,
                    out tierMessage
                )
            )
                throw new InvalidOperationException(tierMessage);

            bool hasLeft = HasSide(view, -1);
            bool hasRight = HasSide(view, 1);
            Dictionary<int, double> firstVerticalLines = new Dictionary<int, double>();
            if (hasLeft)
                firstVerticalLines[-1] = ResolveFirstNeighborVerticalLine(
                    view,
                    -1,
                    FindConnectionExtent(view, -1),
                    tierBase,
                    tierStep
                );
            if (hasRight)
                firstVerticalLines[1] = ResolveFirstNeighborVerticalLine(
                    view,
                    1,
                    FindConnectionExtent(view, 1),
                    tierBase,
                    tierStep
                );

            if (hasLeft && hasRight)
            {
                plans.Add(BuildOuterChain(view, -1, firstVerticalLines[-1] - (2.0 * tierStep)));
                plans.Add(BuildOuterChain(view, 1, firstVerticalLines[1] + (2.0 * tierStep)));
            }
            else
            {
                // A single-sided Neighbor chain follows the physical
                // Neighbor side, never the view's left/right sheet position.
                int outerSide = hasLeft ? -1 : 1;
                double firstLine;
                if (!firstVerticalLines.TryGetValue(outerSide, out firstLine))
                    throw new InvalidOperationException(
                        "The single-sided Neighbor has no matching vertical tier."
                    );
                double outerLine = firstLine + (outerSide * 2.0 * tierStep);
                plans.Add(BuildOuterChain(view, outerSide, outerLine));
            }

            for (int i = 0; i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                double holeLine = firstVerticalLines[link.Side];
                double depthLine = holeLine + (link.Side * tierStep);
                plans.Add(BuildVerticalHolePlan(view, link, holeLine));
                plans.Add(BuildNeighborDepthPlan(view, link, depthLine));
            }

            for (int i = 0; i < view.Levels.Count; i++)
            {
                NeighborLevel level = view.Levels[i];
                bool hasEdgeDim =
                    (level.Left != null && level.Left.RequiresColumnEdgeDimension)
                    || (level.Right != null && level.Right.RequiresColumnEdgeDimension);
                double edgeLine = level.ReferenceY + tierBase;
                if (level.Left != null && level.Left.RequiresColumnEdgeDimension)
                    plans.Add(BuildColumnEdgeHolePlan(view, level.Left, edgeLine));
                if (level.Right != null && level.Right.RequiresColumnEdgeDimension)
                    plans.Add(BuildColumnEdgeHolePlan(view, level.Right, edgeLine));
                plans.Add(
                    BuildHorizontalReferenceHolePlan(
                        view,
                        level,
                        hasEdgeDim ? edgeLine + tierStep : edgeLine
                    )
                );
            }

            int edgeCount = 0;
            for (int i = 0; i < view.Links.Count; i++)
            {
                if (view.Links[i].RequiresColumnEdgeDimension)
                    edgeCount++;
            }
            int expected =
                (hasLeft && hasRight ? 2 : 1)
                + (view.Links.Count * 2)
                + view.Levels.Count
                + edgeCount;
            if (plans.Count != expected)
                throw new InvalidOperationException(
                    "Neighbor plan signature mismatch in view "
                        + view.Capture.Identifier.ToString(CultureInfo.InvariantCulture)
                        + "."
                );
            ValidateVerticalNeighborPlanSides(view, plans);
            return plans;
        }

        private static void ValidateVerticalNeighborPlanSides(
            ViewGeometry view,
            List<DimPlan> plans
        )
        {
            bool hasLeftPlan = false;
            bool hasRightPlan = false;
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || plan.Direction == null
                    || Math.Abs(plan.Direction.X) < DirectionCosineTolerance
                )
                    continue;

                int side = plan.Direction.X < 0.0 ? -1 : 1;
                if (!HasSide(view, side))
                    throw new InvalidOperationException(
                        "Neighbor vertical DIM side mismatch in view "
                            + view.Capture.Identifier.ToString(CultureInfo.InvariantCulture)
                            + ": "
                            + plan.Name
                            + "."
                    );
                if (side < 0)
                    hasLeftPlan = true;
                else
                    hasRightPlan = true;
            }

            if (hasLeftPlan != HasSide(view, -1) || hasRightPlan != HasSide(view, 1))
                throw new InvalidOperationException(
                    "Neighbor vertical DIM side coverage mismatch in view "
                        + view.Capture.Identifier.ToString(CultureInfo.InvariantCulture)
                        + "."
                );
        }

        private static DimPlan BuildOuterChain(ViewGeometry view, int side, double targetLine)
        {
            double columnX = side < 0 ? view.MainBounds.MinX : view.MainBounds.MaxX;

            DimPlan plan = NewPlan(
                (side < 0 ? "LEFT" : "RIGHT") + " OUTER COLUMN-REF CHAIN",
                view,
                new P2(side, 0.0),
                FindVerticalAttributes(view, side),
                true
            );
            AddVerticalFoot(plan.Points, new P2(columnX, view.MainBounds.MinY));
            for (int i = 0; i < view.Levels.Count; i++)
            {
                AddVerticalFoot(plan.Points, new P2(view.MainRefX, view.Levels[i].ReferenceY));
            }
            AddVerticalFoot(plan.Points, new P2(columnX, view.MainBounds.MaxY));
            SortVertical(plan.Points);
            plan.Distance = side * (targetLine - plan.Points[0].X);
            return plan;
        }

        private static double ResolveFirstNeighborVerticalLine(
            ViewGeometry view,
            int side,
            double geometryExtent,
            double tierBase,
            double tierStep
        )
        {
            double targetLine = geometryExtent + (side * tierBase);
            ExistingDimension releasedTotal = FindReleasedShapeVerticalTotal(view, side);
            double occupiedLine;
            if (
                TryFindOutermostExistingVerticalLine(view, side, releasedTotal, out occupiedLine)
                && (occupiedLine - targetLine) * side >= -GeometryTolerance
            )
                targetLine = occupiedLine + (side * tierStep);

            // The exact Shape total releases this left tier in both flows:
            // Grid replaces it on the captured LEFT view, while Neighbor
            // relocates it outside the Neighbor tiers on the other view.
            // Reusing that tier keeps the Shape rhythm compact and gap-free.
            if (
                releasedTotal != null
                && (releasedTotal.LineCoordinate - targetLine) * side > GeometryTolerance
            )
                targetLine = releasedTotal.LineCoordinate;
            return targetLine;
        }

        private static ExistingDimension FindReleasedShapeVerticalTotal(ViewGeometry view, int side)
        {
            if (view == null || view.Capture == null || side >= 0 || !HasSide(view, -1))
                return null;

            return FindExactShapeVerticalTotal(view, side);
        }

        private static ExistingDimension FindExactShapeVerticalTotal(ViewGeometry view, int side)
        {
            if (view == null || view.Capture == null || (side != -1 && side != 1))
                return null;

            ExistingDimension best = null;
            double columnEdge = side < 0 ? view.MainBounds.MinX : view.MainBounds.MaxX;
            double bestGap = Double.PositiveInfinity;
            for (int i = 0; i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (
                    dimension.Points.Count != 2
                    || dimension.Direction == null
                    || Math.Abs(dimension.Direction.X) < DirectionCosineTolerance
                )
                    continue;
                double y0 = Math.Min(dimension.Points[0].Y, dimension.Points[1].Y);
                double y1 = Math.Max(dimension.Points[0].Y, dimension.Points[1].Y);
                if (
                    Math.Abs(y0 - view.MainBounds.MinY) > MatchTolerance
                    || Math.Abs(y1 - view.MainBounds.MaxY) > MatchTolerance
                    || (dimension.LineCoordinate - columnEdge) * side <= GeometryTolerance
                )
                    continue;
                double gap = Math.Abs(dimension.LineCoordinate - columnEdge);
                if (gap < bestGap)
                {
                    best = dimension;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static List<OverallTotalRelocation> BuildOverallTotalRelocations(
            List<DimPlan> plans
        )
        {
            List<OverallTotalRelocation> result = new List<OverallTotalRelocation>();
            double tierBase;
            double tierStep;
            string tierMessage;
            if (
                !PHU_ColumnDimensionTierContext.TryGetShapeSpacing(
                    out tierBase,
                    out tierStep,
                    out tierMessage
                )
            )
                throw new InvalidOperationException(tierMessage);

            HashSet<int> visitedViews = new HashSet<int>();
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan source = plans[i];
                ViewGeometry view = source == null ? null : source.View;
                if (
                    view == null
                    || view.Capture == null
                    || view.Capture.IsLeftOnSheet
                    || !HasSide(view, -1)
                    || !visitedViews.Add(view.Capture.Identifier)
                )
                    continue;

                ExistingDimension total = FindExactShapeVerticalTotal(view, -1);
                if (
                    total == null
                    || total.Dimension == null
                    || total.Direction == null
                    || total.Points.Count != 2
                    || Math.Abs(total.Direction.X) < DirectionCosineTolerance
                )
                    continue;

                bool foundLeftNeighborLine = false;
                double outermostLeftNeighborLine = 0.0;
                for (int p = 0; p < plans.Count; p++)
                {
                    DimPlan candidate = plans[p];
                    if (
                        candidate == null
                        || !Object.ReferenceEquals(candidate.View, view)
                        || candidate.Direction == null
                        || candidate.Direction.X >= -DirectionCosineTolerance
                    )
                        continue;
                    double line = GetPlanLineCoordinate(candidate);
                    if (!IsFinite(line))
                        continue;
                    if (
                        !foundLeftNeighborLine
                        || line < outermostLeftNeighborLine - GeometryTolerance
                    )
                    {
                        foundLeftNeighborLine = true;
                        outermostLeftNeighborLine = line;
                    }
                }
                if (!foundLeftNeighborLine)
                    continue;

                double targetLine = outermostLeftNeighborLine - tierStep;
                if (targetLine >= total.LineCoordinate - GeometryTolerance)
                    continue;
                double targetDistance = (targetLine - total.Points[0].X) / total.Direction.X;
                if (!IsFinite(targetDistance) || targetDistance <= GeometryTolerance)
                    throw new InvalidOperationException(
                        "The surviving RIGHT-view Shape total has an invalid outer tier."
                    );

                OverallTotalRelocation relocation = new OverallTotalRelocation();
                relocation.View = view;
                relocation.Existing = total;
                relocation.OriginalDistance = total.Distance;
                relocation.TargetDistance = targetDistance;
                relocation.TargetLine = targetLine;
                result.Add(relocation);
            }
            return result;
        }

        private static void ApplyOverallTotalRelocations(List<OverallTotalRelocation> relocations)
        {
            for (int i = 0; relocations != null && i < relocations.Count; i++)
            {
                OverallTotalRelocation relocation = relocations[i];
                TSD.StraightDimensionSet dimension =
                    relocation == null || relocation.Existing == null
                        ? null
                        : relocation.Existing.Dimension;
                if (dimension == null)
                    throw new InvalidOperationException(
                        "A Shape total selected for relocation is unavailable."
                    );
                dimension.Distance = relocation.TargetDistance;
                if (!dimension.Modify())
                    throw new InvalidOperationException(
                        "Tekla could not relocate the surviving RIGHT-view Shape total."
                    );
                relocation.Applied = true;
                try
                {
                    dimension.Select();
                }
                catch { }
                double actualDistance = ReadDoubleMember(dimension, "Distance");
                double actualLine =
                    relocation.Existing.Points[0].X
                    + (relocation.Existing.Direction.X * actualDistance);
                if (
                    !IsFinite(actualDistance)
                    || Math.Abs(actualLine - relocation.TargetLine) > MatchTolerance
                )
                    throw new InvalidOperationException(
                        "Relocated Shape total did not keep its planned outer tier."
                    );
            }
        }

        private static void RestoreOverallTotalRelocations(List<OverallTotalRelocation> relocations)
        {
            for (int i = 0; relocations != null && i < relocations.Count; i++)
            {
                OverallTotalRelocation relocation = relocations[i];
                if (
                    relocation == null
                    || !relocation.Applied
                    || relocation.Existing == null
                    || relocation.Existing.Dimension == null
                )
                    continue;
                try
                {
                    relocation.Existing.Dimension.Distance = relocation.OriginalDistance;
                    relocation.Existing.Dimension.Modify();
                    relocation.Applied = false;
                }
                catch { }
            }
        }

        private static DimPlan BuildVerticalHolePlan(
            ViewGeometry view,
            NeighborLink link,
            double targetLine
        )
        {
            DimPlan plan = NewPlan(
                SideName(link.Side) + " REF-FIRST HOLE V " + Format(link.ReferenceY),
                view,
                new P2(link.Side, 0.0),
                FindVerticalAttributes(view, link.Side),
                false
            );
            plan.Points.Add(Clone(link.ReferenceAtNeighborEdge));
            plan.Points.Add(Clone(link.FirstHole));
            plan.Distance = link.Side * (targetLine - plan.Points[0].X);
            return plan;
        }

        private static DimPlan BuildNeighborDepthPlan(
            ViewGeometry view,
            NeighborLink link,
            double targetLine
        )
        {
            DimPlan plan = NewPlan(
                SideName(link.Side) + " REF-NEIGHBOR EDGE " + Format(link.ReferenceY),
                view,
                new P2(link.Side, 0.0),
                FindVerticalAttributes(view, link.Side),
                false
            );
            plan.Points.Add(Clone(link.ReferenceAtColumnAxis));
            plan.Points.Add(new P2(link.NearSolidX, link.OppositeEdgeY));
            plan.Distance = link.Side * (targetLine - plan.Points[0].X);
            return plan;
        }

        private static DimPlan BuildHorizontalReferenceHolePlan(
            ViewGeometry view,
            NeighborLevel level,
            double targetLine
        )
        {
            DimPlan plan = NewPlan(
                "REF-FIRST HOLE H " + Format(level.ReferenceY),
                view,
                new P2(0.0, 1.0),
                FindHorizontalAttributes(view),
                level.LinkCount > 1
            );
            if (level.LinkCount == 1)
            {
                plan.Points.Add(new P2(view.MainRefX, level.ReferenceY));
                plan.Points.Add(
                    Clone(level.Left != null ? level.Left.FirstHole : level.Right.FirstHole)
                );
            }
            else
            {
                plan.Points.Add(Clone(level.Left.FirstHole));
                plan.Points.Add(new P2(view.MainRefX, level.ReferenceY));
                plan.Points.Add(Clone(level.Right.FirstHole));
                SortHorizontal(plan.Points);
            }
            plan.Distance = targetLine - plan.Points[0].Y;
            return plan;
        }

        private static DimPlan BuildColumnEdgeHolePlan(
            ViewGeometry view,
            NeighborLink link,
            double targetLine
        )
        {
            double columnEdgeX = link.Side < 0 ? view.MainBounds.MinX : view.MainBounds.MaxX;
            DimPlan plan = NewPlan(
                SideName(link.Side) + " COLUMN EDGE-FIRST HOLE " + Format(link.ReferenceY),
                view,
                new P2(0.0, 1.0),
                FindHorizontalAttributes(view),
                false
            );
            plan.Points.Add(new P2(columnEdgeX, link.ReferenceY));
            plan.Points.Add(Clone(link.FirstHole));
            // Preserve the technical foot order on both mirrored sides:
            // column edge -> first connection-plate hole.
            plan.Distance = targetLine - plan.Points[0].Y;
            return plan;
        }

        private static DimPlan NewPlan(
            string name,
            ViewGeometry view,
            P2 direction,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes,
            bool disableCombine
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.Direction = direction;
            plan.Attributes = attributes;
            plan.DisableCombine = disableCombine;
            return plan;
        }

        private static ViewGeometry ReadViewGeometry(
            Context context,
            ViewCapture capture,
            TSG.Matrix currentToGlobal,
            bool readDimensions
        )
        {
            if (context == null || capture == null || capture.View == null)
                throw new InvalidOperationException("A captured Neighbor view is unavailable.");

            TSD.View view = capture.View;
            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem
            );
            List<P2> mainReference = ReadReferenceLine(
                context.MainPart,
                currentToGlobal,
                globalToView
            );
            P2 mainBottom;
            P2 mainTop;
            FindVerticalReferenceEnds(mainReference, out mainBottom, out mainTop);
            if (mainBottom == null || mainTop == null)
                throw new InvalidOperationException(
                    "A captured Neighbor view has no vertical MainPart REF."
                );

            ViewGeometry geometry = new ViewGeometry();
            geometry.Capture = capture;
            geometry.View = view;
            geometry.Scale = view.Attributes == null ? 0.0 : view.Attributes.Scale;
            if (!IsFinite(geometry.Scale) || geometry.Scale <= 0.0)
                throw new InvalidOperationException("A Neighbor view has an invalid scale.");
            geometry.MainRefX = (mainBottom.X + mainTop.X) * 0.5;
            geometry.MainBounds = ReadSolidBounds(context.MainPart, currentToGlobal, globalToView);
            if (geometry.MainBounds == null || !geometry.MainBounds.IsValid)
                throw new InvalidOperationException("MainPart bounds are unavailable.");

            Dictionary<int, TSM.Part> visibleParts = ReadVisibleModelParts(context.Model, view);
            foreach (KeyValuePair<int, TSM.Part> pair in visibleParts)
            {
                TSM.Part part = pair.Value;
                if (part == null || SameIdentifier(part, context.MainPart))
                    continue;
                try
                {
                    NeighborLink link = TryBuildNeighborLink(
                        context,
                        geometry,
                        part,
                        visibleParts,
                        currentToGlobal,
                        globalToView
                    );
                    if (link != null)
                        geometry.Links.Add(link);
                }
                catch (Exception ex)
                {
                    RecordSkippedRelation(context, part, ex.Message);
                }
            }

            geometry.Links.Sort(
                delegate(NeighborLink a, NeighborLink b)
                {
                    double dy = a.ReferenceY - b.ReferenceY;
                    if (Math.Abs(dy) > MatchTolerance)
                        return dy < 0.0 ? -1 : 1;
                    if (a.Side != b.Side)
                        return a.Side.CompareTo(b.Side);
                    return a.NeighborIdentifier.CompareTo(b.NeighborIdentifier);
                }
            );
            RemoveAmbiguousSameSideLevels(context, geometry);
            BuildLevels(geometry);
            if (readDimensions)
                geometry.Dimensions.AddRange(ReadExistingDimensions(view));
            return geometry;
        }

        private static NeighborLink TryBuildNeighborLink(
            Context context,
            ViewGeometry view,
            TSM.Part neighbor,
            Dictionary<int, TSM.Part> visibleParts,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<P2> reference = ReadReferenceLine(neighbor, currentToGlobal, globalToView);
            P2 near;
            P2 far;
            int side;
            if (
                !FindLongitudinalNeighborReference(
                    reference,
                    view.MainRefX,
                    out near,
                    out far,
                    out side
                )
            )
                return null;

            Bounds2 neighborBounds = ReadSolidBounds(neighbor, currentToGlobal, globalToView);
            if (
                neighborBounds == null
                || !neighborBounds.IsValid
                || neighborBounds.MaxX - neighborBounds.MinX
                    < Math.Max(
                        MinimumNeighborReferenceLength * 0.65,
                        (neighborBounds.MaxY - neighborBounds.MinY) * 2.5
                    )
            )
                return null;

            List<NeighborLink> matches = new List<NeighborLink>();
            HashSet<int> seenBolts = new HashSet<int>();
            TSM.ModelObjectEnumerator bolts = neighbor.GetBolts();
            while (bolts != null && bolts.MoveNext())
            {
                TSM.BoltGroup group = bolts.Current as TSM.BoltGroup;
                if (
                    group == null
                    || group.Identifier == null
                    || !seenBolts.Add(group.Identifier.ID)
                )
                    continue;

                string connectionName = ReadFatherComponentName(group);
                if (!IsSupportedConnection(connectionName))
                    continue;

                TSM.Part partToBeBolted = GetMember(group, "PartToBeBolted") as TSM.Part;
                TSM.Part plate = GetMember(group, "PartToBoltTo") as TSM.Part;
                if (
                    !SameIdentifier(partToBeBolted, neighbor)
                    || plate == null
                    || !AssemblyMainMatches(plate, context.MainPart)
                    || plate.Identifier == null
                    || !visibleParts.ContainsKey(plate.Identifier.ID)
                )
                    continue;

                List<P2> holes = ReadBoltPoints(group, currentToGlobal, globalToView);
                if (holes.Count < 2)
                    throw new InvalidOperationException(
                        "Supported neighbor bolt group "
                            + group.Identifier.ID
                            + " does not expose two distinct holes in its longitudinal view."
                    );

                P2 firstHole = SelectFirstHole(holes, view.MainRefX, side);
                if (firstHole == null)
                    throw new InvalidOperationException(
                        "Supported neighbor bolt group "
                            + group.Identifier.ID
                            + " has no first hole outward from the column REF."
                    );

                Bounds2 plateBounds = ReadSolidBounds(plate, currentToGlobal, globalToView);
                if (plateBounds == null || !plateBounds.IsValid)
                    throw new InvalidOperationException(
                        "The column-assembly connection plate has no readable solid."
                    );
                if (
                    firstHole.X < plateBounds.MinX - MatchTolerance
                    || firstHole.X > plateBounds.MaxX + MatchTolerance
                    || firstHole.Y < plateBounds.MinY - MatchTolerance
                    || firstHole.Y > plateBounds.MaxY + MatchTolerance
                )
                    throw new InvalidOperationException(
                        "The selected first hole is not inside its connection plate."
                    );

                double referenceY = (near.Y + far.Y) * 0.5;
                double oppositeY = ResolveOppositeNeighborEdgeY(neighborBounds, referenceY);
                double nearSolidX = side < 0 ? neighborBounds.MaxX : neighborBounds.MinX;
                double plateOuterX = side < 0 ? plateBounds.MinX : plateBounds.MaxX;

                bool needsEdgeDim = String.Equals(
                    connectionName,
                    ConnectionFlange,
                    StringComparison.OrdinalIgnoreCase
                );
                if (needsEdgeDim)
                {
                    double plateInnerX = side < 0 ? plateBounds.MaxX : plateBounds.MinX;
                    double columnEdgeX = side < 0 ? view.MainBounds.MinX : view.MainBounds.MaxX;
                    if (Math.Abs(plateInnerX - columnEdgeX) > MatchTolerance)
                        throw new InvalidOperationException(
                            "Geo_11 plate inner edge does not coincide with the facing column edge."
                        );
                }

                NeighborLink link = new NeighborLink();
                link.NeighborIdentifier = neighbor.Identifier.ID;
                link.PlateIdentifier = plate.Identifier.ID;
                link.BoltIdentifier = group.Identifier.ID;
                link.ConnectionName = connectionName;
                link.Side = side;
                link.ReferenceY = referenceY;
                link.OppositeEdgeY = oppositeY;
                link.NearSolidX = nearSolidX;
                link.PlateOuterX = plateOuterX;
                link.ReferenceAtColumnAxis = new P2(view.MainRefX, referenceY);
                link.ReferenceAtNeighborEdge = new P2(nearSolidX, referenceY);
                link.FirstHole = firstHole;
                link.RequiresColumnEdgeDimension = needsEdgeDim;
                link.NeighborBounds = neighborBounds;
                link.PlateBounds = plateBounds;
                matches.Add(link);
            }

            if (matches.Count > 1)
                throw new InvalidOperationException(
                    "A longitudinal neighbor resolves more than one supported plate/bolt relation to the current column."
                );
            return matches.Count == 1 ? matches[0] : null;
        }

        private static void ValidateSupportedTopology(ViewGeometry left, ViewGeometry right)
        {
            int total =
                (left == null ? 0 : left.Links.Count) + (right == null ? 0 : right.Links.Count);
            if (total == 0)
                throw new InvalidOperationException(
                    "Supported relations exist, but no longitudinal neighbor could be resolved in either view."
                );
            ValidateViewTopology(left);
            ValidateViewTopology(right);
        }

        private static void ValidateViewTopology(ViewGeometry view)
        {
            if (view == null || view.Links.Count == 0)
                return;

            for (int i = 0; i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                if (
                    link.ReferenceY <= view.MainBounds.MinY + GeometryTolerance
                    || link.ReferenceY >= view.MainBounds.MaxY - GeometryTolerance
                )
                    throw new InvalidOperationException(
                        "A neighbor REF lies outside the main column span."
                    );
            }

            for (int i = 0; i < view.Levels.Count; i++)
            {
                if (view.Levels[i].LinkCount < 1 || view.Levels[i].LinkCount > 2)
                    throw new InvalidOperationException(
                        "A neighbor level has an invalid side topology."
                    );
            }
        }

        private static void BuildLevels(ViewGeometry view)
        {
            for (int i = 0; i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                NeighborLevel level = null;
                for (int j = 0; j < view.Levels.Count; j++)
                {
                    if (Math.Abs(view.Levels[j].ReferenceY - link.ReferenceY) <= MatchTolerance)
                    {
                        level = view.Levels[j];
                        break;
                    }
                }
                if (level == null)
                {
                    level = new NeighborLevel();
                    level.ReferenceY = link.ReferenceY;
                    view.Levels.Add(level);
                }

                if (link.Side < 0)
                {
                    if (level.Left != null)
                        throw new InvalidOperationException(
                            "Two left neighbors occupy the same REF level."
                        );
                    level.Left = link;
                }
                else
                {
                    if (level.Right != null)
                        throw new InvalidOperationException(
                            "Two right neighbors occupy the same REF level."
                        );
                    level.Right = link;
                }
            }
            view.Levels.Sort(
                delegate(NeighborLevel a, NeighborLevel b)
                {
                    return a.ReferenceY.CompareTo(b.ReferenceY);
                }
            );
        }

        private static void RemoveAmbiguousSameSideLevels(Context context, ViewGeometry view)
        {
            if (view == null || view.Links.Count == 0)
                return;

            bool[] remove = new bool[view.Links.Count];
            for (int i = 0; i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                if (
                    link.ReferenceY <= view.MainBounds.MinY + GeometryTolerance
                    || link.ReferenceY >= view.MainBounds.MaxY - GeometryTolerance
                )
                {
                    remove[i] = true;
                    RecordSkippedRelation(
                        context,
                        null,
                        "Neighbor REF is outside the MainPart span."
                    );
                }

                for (int j = i + 1; j < view.Links.Count; j++)
                {
                    NeighborLink other = view.Links[j];
                    if (
                        link.Side != other.Side
                        || Math.Abs(link.ReferenceY - other.ReferenceY) > MatchTolerance
                    )
                        continue;
                    remove[i] = true;
                    remove[j] = true;
                    RecordSkippedRelation(
                        context,
                        null,
                        "More than one neighbor occupies the same side and REF level."
                    );
                }
            }

            for (int i = remove.Length - 1; i >= 0; i--)
            {
                if (remove[i])
                    view.Links.RemoveAt(i);
            }
        }

        private static void RecordSkippedRelation(Context context, TSM.Part neighbor, string reason)
        {
            if (context == null)
                return;
            context.SkippedRelationCount++;
            if (context.SkipReasons.Count >= 5)
                return;
            string partText =
                neighbor == null || neighbor.Identifier == null
                    ? String.Empty
                    : " neighbor=" + neighbor.Identifier.ID.ToString(CultureInfo.InvariantCulture);
            context.SkipReasons.Add(partText + " " + (reason ?? "uncertain geometry"));
        }

        private static string DescribeSkippedRelations(Context context)
        {
            if (context == null || context.SkippedRelationCount == 0)
                return String.Empty;
            StringBuilder text = new StringBuilder();
            text.Append(" Skipped uncertain relations=")
                .Append(context.SkippedRelationCount.ToString(CultureInfo.InvariantCulture));
            if (context.SkipReasons.Count > 0)
            {
                text.Append(" [");
                for (int i = 0; i < context.SkipReasons.Count; i++)
                {
                    if (i > 0)
                        text.Append("; ");
                    text.Append(context.SkipReasons[i].Trim());
                }
                text.Append(']');
            }
            text.Append('.');
            return text.ToString();
        }

        private static int CountSupportedRelations(Context context, TSG.Matrix currentToGlobal)
        {
            int count = 0;
            HashSet<string> seen = new HashSet<string>();
            ViewCapture[] captures = new ViewCapture[] { context.Left, context.Right };
            for (int v = 0; v < captures.Length; v++)
            {
                TSD.View view = captures[v].View;
                Dictionary<int, TSM.Part> parts = ReadVisibleModelParts(context.Model, view);
                foreach (KeyValuePair<int, TSM.Part> pair in parts)
                {
                    TSM.Part part = pair.Value;
                    if (part == null || SameIdentifier(part, context.MainPart))
                        continue;
                    HashSet<int> boltIds = new HashSet<int>();
                    TSM.ModelObjectEnumerator bolts = part.GetBolts();
                    while (bolts != null && bolts.MoveNext())
                    {
                        TSM.BoltGroup group = bolts.Current as TSM.BoltGroup;
                        if (
                            group == null
                            || group.Identifier == null
                            || !boltIds.Add(group.Identifier.ID)
                            || !IsSupportedConnection(ReadFatherComponentName(group))
                        )
                            continue;
                        TSM.Part beam = GetMember(group, "PartToBeBolted") as TSM.Part;
                        TSM.Part plate = GetMember(group, "PartToBoltTo") as TSM.Part;
                        if (
                            !SameIdentifier(beam, part)
                            || plate == null
                            || !AssemblyMainMatches(plate, context.MainPart)
                        )
                            continue;
                        string key =
                            part.Identifier.ID.ToString(CultureInfo.InvariantCulture)
                            + ":"
                            + group.Identifier.ID.ToString(CultureInfo.InvariantCulture);
                        if (seen.Add(key))
                            count++;
                    }
                }
            }
            return count;
        }

        private static void CaptureTopology(Context context, ViewGeometry left, ViewGeometry right)
        {
            context.ExpectedTopology.Clear();
            AddTopology(context.ExpectedTopology, left);
            AddTopology(context.ExpectedTopology, right);
            context.ExpectedTopology.Sort(StringComparer.Ordinal);
        }

        private static void VerifyTopologyUnchanged(
            Context context,
            ViewGeometry left,
            ViewGeometry right
        )
        {
            List<string> actual = new List<string>();
            AddTopology(actual, left);
            AddTopology(actual, right);
            actual.Sort(StringComparer.Ordinal);
            if (actual.Count != context.ExpectedTopology.Count)
                throw new InvalidOperationException(
                    "Neighbor topology changed while Shape was running."
                );
            for (int i = 0; i < actual.Count; i++)
            {
                if (
                    !String.Equals(actual[i], context.ExpectedTopology[i], StringComparison.Ordinal)
                )
                    throw new InvalidOperationException(
                        "A captured Neighbor relation changed while Shape was running."
                    );
            }
        }

        private static void AddTopology(List<string> target, ViewGeometry view)
        {
            for (int i = 0; view != null && i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                target.Add(
                    view.Capture.Identifier.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + link.NeighborIdentifier.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + link.PlateIdentifier.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + link.BoltIdentifier.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + link.Side.ToString(CultureInfo.InvariantCulture)
                        + "|"
                        + Math.Round(link.ReferenceY, 1)
                            .ToString("0.0", CultureInfo.InvariantCulture)
                );
            }
        }

        private static string DescribeViewTopology(ViewGeometry view)
        {
            if (view == null)
                return "view unavailable";
            StringBuilder sides = new StringBuilder();
            if (HasSide(view, -1))
                sides.Append('L');
            if (HasSide(view, 1))
                sides.Append('R');
            return (view.Capture.IsLeftOnSheet ? "LEFT" : "RIGHT")
                + " view="
                + view.Capture.Identifier.ToString(CultureInfo.InvariantCulture)
                + " links="
                + view.Links.Count.ToString(CultureInfo.InvariantCulture)
                + " levels="
                + view.Levels.Count.ToString(CultureInfo.InvariantCulture)
                + " sides="
                + (sides.Length == 0 ? "-" : sides.ToString());
        }

        private static List<ViewCapture> FindTwoVerticalMainViews(
            TSD.Drawing drawing,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal
        )
        {
            List<ViewCapture> result = new List<ViewCapture>();
            TSD.ContainerView sheet = drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (
                    view == null
                    || PHU_VerticalShapeViewLayoutContext.IsSectionView(view)
                    || !ViewContainsPart(view, mainPart)
                )
                    continue;
                TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                    view.DisplayCoordinateSystem
                );
                List<P2> reference = ReadReferenceLine(mainPart, currentToGlobal, globalToView);
                P2 bottom;
                P2 top;
                FindVerticalReferenceEnds(reference, out bottom, out top);
                if (bottom == null || top == null)
                    continue;
                int identifier = ReadIdentifier(view);
                TSG.Point origin = ClonePoint(view.Origin);
                if (identifier == 0 || origin == null)
                    continue;
                ViewCapture capture = new ViewCapture();
                capture.Identifier = identifier;
                capture.View = view;
                capture.OriginalOrigin = origin;
                result.Add(capture);
            }
            return result;
        }

        private static void ApplyCapturedUserOrder(List<ViewCapture> captures)
        {
            int leftId;
            int rightId;
            TSG.Point leftOrigin;
            TSG.Point rightOrigin;
            if (
                PHU_VerticalShapeViewLayoutContext.TryGetCapturedViewOrder(
                    out leftId,
                    out rightId,
                    out leftOrigin,
                    out rightOrigin
                )
            )
            {
                ViewCapture left = FindCapture(captures, leftId);
                ViewCapture right = FindCapture(captures, rightId);
                if (left == null || right == null)
                    throw new InvalidOperationException(
                        "Neighbor DIM could not bind the captured user view order."
                    );
                left.OriginalOrigin = ClonePoint(leftOrigin);
                right.OriginalOrigin = ClonePoint(rightOrigin);
                captures.Clear();
                captures.Add(left);
                captures.Add(right);
            }
            else
            {
                captures.Sort(
                    delegate(ViewCapture a, ViewCapture b)
                    {
                        double dx = a.OriginalOrigin.X - b.OriginalOrigin.X;
                        if (Math.Abs(dx) > GeometryTolerance)
                            return dx < 0.0 ? -1 : 1;
                        return a.Identifier.CompareTo(b.Identifier);
                    }
                );
            }

            if (
                captures.Count != 2
                || Math.Abs(captures[0].OriginalOrigin.X - captures[1].OriginalOrigin.X)
                    <= GeometryTolerance
            )
                throw new InvalidOperationException(
                    "Neighbor DIM has no stable user left/right view order."
                );
        }

        private static ViewCapture FindCapture(List<ViewCapture> captures, int identifier)
        {
            for (int i = 0; captures != null && i < captures.Count; i++)
            {
                if (captures[i].Identifier == identifier)
                    return captures[i];
            }
            return null;
        }

        private static void RebindCapturedViews(Context context)
        {
            context.Left.View = null;
            context.Right.View = null;
            TSD.ContainerView sheet = context.Drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                int id = ReadIdentifier(view);
                if (id == context.Left.Identifier)
                    context.Left.View = view;
                if (id == context.Right.Identifier)
                    context.Right.View = view;
            }
            if (context.Left.View == null || context.Right.View == null)
                throw new InvalidOperationException(
                    "A captured Neighbor view disappeared during Shape."
                );
        }

        private static Dictionary<int, TSM.Part> ReadVisibleModelParts(
            TSM.Model model,
            TSD.View view
        )
        {
            Dictionary<int, TSM.Part> result = new Dictionary<int, TSM.Part>();
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part drawingPart = parts.Current as TSD.Part;
                if (drawingPart == null || drawingPart.ModelIdentifier == null)
                    continue;
                TSM.Part modelPart =
                    model.SelectModelObject(drawingPart.ModelIdentifier) as TSM.Part;
                if (modelPart == null || modelPart.Identifier == null)
                    continue;
                result[modelPart.Identifier.ID] = modelPart;
            }
            return result;
        }

        private static bool FindLongitudinalNeighborReference(
            List<P2> points,
            double mainRefX,
            out P2 near,
            out P2 far,
            out int side
        )
        {
            near = null;
            far = null;
            side = 0;
            double bestLength = 0.0;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[j].X - points[i].X;
                    double dy = points[j].Y - points[i].Y;
                    double length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (
                        length <= bestLength
                        || length < MinimumNeighborReferenceLength
                        || Math.Abs(dx / length) < DirectionCosineTolerance
                    )
                        continue;

                    double minX = Math.Min(points[i].X, points[j].X);
                    double maxX = Math.Max(points[i].X, points[j].X);
                    if (mainRefX < minX - MatchTolerance || mainRefX > maxX + MatchTolerance)
                        continue;

                    double negative = mainRefX - minX;
                    double positive = maxX - mainRefX;
                    int candidateSide;
                    if (positive >= MinimumNeighborReferenceLength && negative <= MatchTolerance)
                        candidateSide = 1;
                    else if (
                        negative >= MinimumNeighborReferenceLength
                        && positive <= MatchTolerance
                    )
                        candidateSide = -1;
                    else
                        continue;

                    P2 a = points[i];
                    P2 b = points[j];
                    P2 candidateNear = Math.Abs(a.X - mainRefX) <= Math.Abs(b.X - mainRefX) ? a : b;
                    P2 candidateFar = Object.ReferenceEquals(candidateNear, a) ? b : a;
                    bestLength = length;
                    near = candidateNear;
                    far = candidateFar;
                    side = candidateSide;
                }
            }
            return near != null && far != null && side != 0;
        }

        private static double ResolveOppositeNeighborEdgeY(Bounds2 bounds, double referenceY)
        {
            if (bounds == null || !bounds.IsValid)
                throw new InvalidOperationException("Neighbor bounds are invalid.");
            double toMin = Math.Abs(referenceY - bounds.MinY);
            double toMax = Math.Abs(referenceY - bounds.MaxY);
            double near = Math.Min(toMin, toMax);
            double far = Math.Max(toMin, toMax);
            if (near > MatchTolerance || far <= GeometryTolerance)
                throw new InvalidOperationException(
                    "Neighbor REF is not on one exact solid depth edge."
                );
            return toMin >= toMax ? bounds.MinY : bounds.MaxY;
        }

        private static P2 SelectFirstHole(List<P2> holes, double referenceX, int side)
        {
            P2 best = null;
            double bestProjection = Double.PositiveInfinity;
            for (int i = 0; holes != null && i < holes.Count; i++)
            {
                P2 hole = holes[i];
                double projection = (hole.X - referenceX) * side;
                if (projection <= GeometryTolerance)
                    continue;
                if (
                    projection < bestProjection - GeometryTolerance
                    || (
                        Math.Abs(projection - bestProjection) <= GeometryTolerance
                        && (best == null || hole.Y < best.Y)
                    )
                )
                {
                    best = hole;
                    bestProjection = projection;
                }
            }
            return Clone(best);
        }

        private static List<P2> ReadBoltPoints(
            TSM.BoltGroup group,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<P2> result = new List<P2>();
            if (group == null || group.BoltPositions == null)
                return result;
            foreach (object value in group.BoltPositions)
            {
                TSG.Point point = value as TSG.Point;
                if (point != null)
                    AddUniquePoint(result, Transform(point, currentToGlobal, globalToView));
            }
            result.Sort(
                delegate(P2 a, P2 b)
                {
                    double dx = a.X - b.X;
                    if (Math.Abs(dx) > GeometryTolerance)
                        return dx < 0.0 ? -1 : 1;
                    return a.Y.CompareTo(b.Y);
                }
            );
            return result;
        }

        private static string ReadFatherComponentName(TSM.BoltGroup group)
        {
            object father = InvokeNoArg(group, "GetFatherComponent");
            return Convert.ToString(GetMember(father, "Name"), CultureInfo.InvariantCulture)
                ?? String.Empty;
        }

        private static bool IsSupportedConnection(string name)
        {
            return String.Equals(name, ConnectionBolt, StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, ConnectionFlange, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AssemblyMainMatches(TSM.Part part, TSM.Part expectedMain)
        {
            try
            {
                TSM.Assembly assembly = part == null ? null : part.GetAssembly();
                TSM.Part assemblyMain =
                    assembly == null ? null : assembly.GetMainPart() as TSM.Part;
                return SameIdentifier(assemblyMain, expectedMain);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasSide(ViewGeometry view, int side)
        {
            for (int i = 0; view != null && i < view.Links.Count; i++)
            {
                if (view.Links[i].Side == side)
                    return true;
            }
            return false;
        }

        private static double FindConnectionExtent(ViewGeometry view, int side)
        {
            bool found = false;
            double result = side < 0 ? Double.PositiveInfinity : Double.NegativeInfinity;
            for (int i = 0; view != null && i < view.Links.Count; i++)
            {
                NeighborLink link = view.Links[i];
                if (link.Side != side)
                    continue;
                found = true;
                result =
                    side < 0
                        ? Math.Min(result, link.PlateOuterX)
                        : Math.Max(result, link.PlateOuterX);
            }
            if (!found)
                throw new InvalidOperationException(
                    "No connection extent exists on " + SideName(side) + "."
                );
            return result;
        }

        private static bool TryFindOutermostExistingVerticalLine(
            ViewGeometry view,
            int side,
            ExistingDimension excluded,
            out double lineCoordinate
        )
        {
            lineCoordinate = 0.0;
            double columnEdge = side < 0 ? view.MainBounds.MinX : view.MainBounds.MaxX;
            bool found = false;
            for (int i = 0; i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (
                    Object.ReferenceEquals(dimension, excluded)
                    || dimension.Direction == null
                    || Math.Abs(dimension.Direction.X) < DirectionCosineTolerance
                    || dimension.Points.Count < 2
                )
                    continue;
                double minY = Double.PositiveInfinity;
                double maxY = Double.NegativeInfinity;
                for (int p = 0; p < dimension.Points.Count; p++)
                {
                    minY = Math.Min(minY, dimension.Points[p].Y);
                    maxY = Math.Max(maxY, dimension.Points[p].Y);
                }
                double overlap =
                    Math.Min(maxY, view.MainBounds.MaxY) - Math.Max(minY, view.MainBounds.MinY);
                if (
                    overlap <= GeometryTolerance
                    || (dimension.LineCoordinate - columnEdge) * side <= GeometryTolerance
                )
                    continue;
                if (
                    !found
                    || (dimension.LineCoordinate - lineCoordinate) * side > GeometryTolerance
                )
                {
                    lineCoordinate = dimension.LineCoordinate;
                    found = true;
                }
            }
            return found;
        }

        private static void RegisterOutermostVerticalTiers(List<DimPlan> plans)
        {
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || plan.View == null
                    || plan.View.Capture == null
                    || plan.Direction == null
                    || Math.Abs(plan.Direction.X) < DirectionCosineTolerance
                )
                    continue;
                int side = plan.Direction.X < 0.0 ? -1 : 1;
                PHU_ColumnDimensionTierContext.RegisterNeighborOutermostVerticalLine(
                    plan.View.Capture.Identifier,
                    side,
                    GetPlanLineCoordinate(plan)
                );
            }
        }

        private static double GetPlanLineCoordinate(DimPlan plan)
        {
            if (plan == null || plan.Direction == null || plan.Points.Count == 0)
                return Double.NaN;
            return Math.Abs(plan.Direction.X) >= Math.Abs(plan.Direction.Y)
                ? plan.Points[0].X + (plan.Direction.X * plan.Distance)
                : plan.Points[0].Y + (plan.Direction.Y * plan.Distance);
        }

        private static List<ExistingDimension> ReadExistingDimensions(TSD.View view)
        {
            List<ExistingDimension> result = new List<ExistingDimension>();
            TSD.DrawingObjectEnumerator objects = view.GetAllObjects(
                typeof(TSD.StraightDimensionSet)
            );
            while (objects != null && objects.MoveNext())
            {
                TSD.StraightDimensionSet set = objects.Current as TSD.StraightDimensionSet;
                if (set == null)
                    continue;
                ExistingDimension item = new ExistingDimension();
                item.Dimension = set;
                item.Points.AddRange(ReadDimensionPoints(set));
                item.Direction = ReadDirection(set);
                item.Distance = ReadDoubleMember(set, "Distance");
                try
                {
                    item.Attributes = set.Attributes;
                }
                catch
                {
                    item.Attributes = null;
                }
                if (item.Points.Count == 0 || item.Direction == null || !IsFinite(item.Distance))
                    continue;
                item.LineCoordinate =
                    Math.Abs(item.Direction.X) >= Math.Abs(item.Direction.Y)
                        ? item.Points[0].X + (item.Direction.X * item.Distance)
                        : item.Points[0].Y + (item.Direction.Y * item.Distance);
                result.Add(item);
            }
            return result;
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes FindVerticalAttributes(
            ViewGeometry view,
            int side
        )
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes fallback = null;
            for (int i = 0; view != null && i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (dimension.Attributes == null)
                    continue;
                if (fallback == null)
                    fallback = dimension.Attributes;
                if (
                    dimension.Direction != null
                    && Math.Abs(dimension.Direction.X) >= DirectionCosineTolerance
                    && dimension.Direction.X * side > 0.0
                )
                    return dimension.Attributes;
            }
            return fallback;
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes FindHorizontalAttributes(
            ViewGeometry view
        )
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes fallback = null;
            for (int i = 0; view != null && i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (dimension.Attributes == null)
                    continue;
                if (fallback == null)
                    fallback = dimension.Attributes;
                if (
                    dimension.Direction != null
                    && dimension.Direction.Y >= DirectionCosineTolerance
                )
                    return dimension.Attributes;
            }
            return fallback;
        }

        private static void ValidatePlans(List<DimPlan> plans)
        {
            if (plans == null || plans.Count == 0)
                throw new InvalidOperationException("Neighbor planner returned no dimensions.");
            HashSet<string> signatures = new HashSet<string>();
            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan == null
                    || plan.View == null
                    || plan.View.View == null
                    || plan.Direction == null
                    || plan.Points.Count < 2
                    || !IsFinite(plan.Distance)
                    || plan.Distance <= GeometryTolerance
                )
                    throw new InvalidOperationException(
                        "Invalid Neighbor plan at index "
                            + i.ToString(CultureInfo.InvariantCulture)
                            + "."
                    );
                for (int p = 0; p < plan.Points.Count; p++)
                {
                    if (
                        plan.Points[p] == null
                        || !IsFinite(plan.Points[p].X)
                        || !IsFinite(plan.Points[p].Y)
                    )
                        throw new InvalidOperationException(
                            plan.Name + " contains a non-finite foot."
                        );
                    for (int q = p + 1; q < plan.Points.Count; q++)
                    {
                        if (Distance(plan.Points[p], plan.Points[q]) <= GeometryTolerance)
                            throw new InvalidOperationException(
                                plan.Name + " contains duplicate feet."
                            );
                    }
                }
                string signature = BuildPlanSignature(plan);
                if (!signatures.Add(signature))
                    throw new InvalidOperationException(
                        "Duplicate Neighbor dimension semantic: " + plan.Name + "."
                    );
            }
        }

        private static string BuildPlanSignature(DimPlan plan)
        {
            StringBuilder text = new StringBuilder();
            text.Append(plan.View.Capture.Identifier)
                .Append('|')
                .Append(Math.Abs(plan.Direction.X) >= Math.Abs(plan.Direction.Y) ? 'Y' : 'X')
                .Append('|');
            for (int i = 0; i < plan.Points.Count; i++)
            {
                P2 point = plan.Points[i];
                text.Append(Math.Round(point.X, 1).ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(Math.Round(point.Y, 1).ToString("0.0", CultureInfo.InvariantCulture))
                    .Append(';');
            }
            return text.ToString();
        }

        private static void VerifyCreatedDimensions(
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> created
        )
        {
            for (int i = 0; i < plans.Count; i++)
            {
                try
                {
                    created[i].Select();
                }
                catch { }
                List<P2> actual = ReadDimensionPoints(created[i]);
                if (!PointChainsMatch(actual, plans[i].Points, MatchTolerance))
                    throw new InvalidOperationException(
                        "Read-back feet do not match " + plans[i].Name + "."
                    );
                if (plans[i].DisableCombine && actual.Count > 2)
                {
                    TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes = created[
                        i
                    ].Attributes;
                    if (
                        attributes == null
                        || attributes.CombinedDimension == null
                        || attributes.CombinedDimension.Format
                            != TSD.DimensionSetBaseAttributes.CombineFormats.Off
                    )
                        throw new InvalidOperationException(
                            "CombinedDimension read-back failed for " + plans[i].Name + "."
                        );
                }
            }
        }

        private static void DisableCombine(
            TSD.StraightDimensionSet dimension,
            int pointCount,
            string name
        )
        {
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes =
                dimension.Attributes;
            if (attributes == null)
                throw new InvalidOperationException(
                    "Created dimension has no attributes for " + name + "."
                );
            TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes combined =
                attributes.CombinedDimension
                ?? new TSD.DimensionSetBaseAttributes.CombinedDimensionAttributes();
            combined.Format = TSD.DimensionSetBaseAttributes.CombineFormats.Off;
            combined.MinimumNumberToCombine = Math.Max(5, pointCount);
            attributes.CombinedDimension = combined;
            dimension.Attributes = attributes;
            if (!dimension.Modify())
                throw new InvalidOperationException("Could not disable combine for " + name + ".");
        }

        private static void AddVerticalFoot(List<P2> points, P2 candidate)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].Y - candidate.Y) <= MatchTolerance)
                    return;
            }
            points.Add(candidate);
        }

        private static void SortVertical(List<P2> points)
        {
            points.Sort(
                delegate(P2 a, P2 b)
                {
                    double dy = a.Y - b.Y;
                    if (Math.Abs(dy) > GeometryTolerance)
                        return dy < 0.0 ? -1 : 1;
                    return a.X.CompareTo(b.X);
                }
            );
        }

        private static void SortHorizontal(List<P2> points)
        {
            points.Sort(
                delegate(P2 a, P2 b)
                {
                    double dx = a.X - b.X;
                    if (Math.Abs(dx) > GeometryTolerance)
                        return dx < 0.0 ? -1 : 1;
                    return a.Y.CompareTo(b.Y);
                }
            );
        }

        private static List<P2> ReadReferenceLine(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<P2> result = new List<P2>();
            try
            {
                ArrayList points = part.GetReferenceLine(false);
                if (points != null)
                {
                    foreach (object value in points)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point != null)
                            AddUniquePoint(result, Transform(point, currentToGlobal, globalToView));
                    }
                }
            }
            catch { }
            return result;
        }

        private static void FindVerticalReferenceEnds(List<P2> points, out P2 bottom, out P2 top)
        {
            bottom = null;
            top = null;
            double best = 0.0;
            for (int i = 0; points != null && i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[j].X - points[i].X;
                    double dy = points[j].Y - points[i].Y;
                    double length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (length <= best || Math.Abs(dy / length) < DirectionCosineTolerance)
                        continue;
                    best = length;
                    bottom = points[i].Y <= points[j].Y ? points[i] : points[j];
                    top = points[i].Y <= points[j].Y ? points[j] : points[i];
                }
            }
        }

        private static Bounds2 ReadSolidBounds(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            Bounds2 bounds = new Bounds2();
            try
            {
                TSM.Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge = edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;
                    bounds.Add(Transform(edge.StartPoint, currentToGlobal, globalToView));
                    bounds.Add(Transform(edge.EndPoint, currentToGlobal, globalToView));
                }
            }
            catch { }
            return bounds;
        }

        private static bool ViewContainsPart(TSD.View view, TSM.Part mainPart)
        {
            if (view == null || mainPart == null || mainPart.Identifier == null)
                return false;
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part part = parts.Current as TSD.Part;
                if (
                    part != null
                    && part.ModelIdentifier != null
                    && part.ModelIdentifier.ID == mainPart.Identifier.ID
                )
                    return true;
            }
            return false;
        }

        private static List<P2> ReadDimensionPoints(object dimension)
        {
            List<P2> result = new List<P2>();
            IEnumerable points = GetMember(dimension, "DimensionPoints") as IEnumerable;
            if (points == null)
                return result;
            foreach (object value in points)
            {
                double x = ReadDoubleMember(value, "X");
                double y = ReadDoubleMember(value, "Y");
                if (IsFinite(x) && IsFinite(y))
                    result.Add(new P2(x, y));
            }
            return result;
        }

        private static P2 ReadDirection(object dimension)
        {
            object value =
                GetMember(dimension, "UpDirection") ?? GetMember(dimension, "OffsetDirection");
            double x = ReadDoubleMember(value, "X");
            double y = ReadDoubleMember(value, "Y");
            if (!IsFinite(x) || !IsFinite(y))
                return null;
            double length = Math.Sqrt((x * x) + (y * y));
            return length <= GeometryTolerance ? null : new P2(x / length, y / length);
        }

        private static bool PointChainsMatch(List<P2> first, List<P2> second, double tolerance)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            bool direct = true;
            bool reverse = true;
            for (int i = 0; i < first.Count; i++)
            {
                direct &= Distance(first[i], second[i]) <= tolerance;
                reverse &= Distance(first[i], second[second.Count - 1 - i]) <= tolerance;
            }
            return direct || reverse;
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
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (property != null)
                    return property.GetValue(value, null);
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                return field == null ? null : field.GetValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static object InvokeNoArg(object value, string methodName)
        {
            if (value == null || String.IsNullOrEmpty(methodName))
                return null;
            try
            {
                MethodInfo method = value
                    .GetType()
                    .GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null
                    );
                return method == null ? null : method.Invoke(value, null);
            }
            catch
            {
                return null;
            }
        }

        private static double ReadDoubleMember(object value, string name)
        {
            object raw = GetMember(value, name);
            if (raw == null)
                return Double.NaN;
            try
            {
                return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return Double.NaN;
            }
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                Tekla.Structures.Identifier identifier =
                    value == null
                        ? null
                        : GetMember(value, "Identifier") as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch
            {
                return 0;
            }
        }

        private static P2 Transform(
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            if (point == null)
                return null;
            TSG.Point global = currentToGlobal.Transform(point);
            TSG.Point projected = globalToView.Transform(global);
            return new P2(projected.X, projected.Y);
        }

        private static void AddUniquePoint(List<P2> points, P2 candidate)
        {
            if (candidate == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], candidate) <= GeometryTolerance)
                    return;
            }
            points.Add(candidate);
        }

        private static bool SameIdentifier(TSM.ModelObject first, TSM.ModelObject second)
        {
            return first != null
                && second != null
                && first.Identifier != null
                && second.Identifier != null
                && first.Identifier.ID == second.Identifier.ID;
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

        private static P2 Clone(P2 point)
        {
            return point == null ? null : new P2(point.X, point.Y);
        }

        private static TSG.Point ClonePoint(TSG.Point point)
        {
            return point == null ? null : new TSG.Point(point.X, point.Y, point.Z);
        }

        private static double Distance(P2 first, P2 second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static string SideName(int side)
        {
            return side < 0 ? "LEFT" : "RIGHT";
        }

        private static string FormatPoint(P2 point)
        {
            return point == null ? "<null>" : "(" + Format(point.X) + "," + Format(point.Y) + ")";
        }

        private static string Format(double value)
        {
            return IsFinite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "NA";
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static void ClearState()
        {
            _enabled = false;
            _context = null;
            LastRunApplicable = false;
            LastRunSucceeded = false;
            LastCreatedCount = 0;
            LastRunMessage = String.Empty;
        }
    }
}
