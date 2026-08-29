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
    /// Project-routed column Grid DIM flow. Inzai Data Center retains its
    /// isolated project strategy; every non-Inzai model uses the independent
    /// company-standard geometry strategy.
    /// </summary>
    public static class PHU_ColumnGridDimensionEngine
    {
        private const double GeometryTolerance = 0.20;
        private const double MatchTolerance = 2.0;
        private const double DirectionCosineTolerance = 0.985;

        private enum ColumnGridStrategy
        {
            None = 0,
            GeneralSystem = 1,
            InzaiDataCenter = 2
        }

        private enum InzaiVerticalTopology
        {
            OneInsideOneOutside = 0,
            ColumnBetweenGrids = 1
        }

        private sealed class InzaiVerticalStationPlan
        {
            public InzaiVerticalTopology Topology;
            public double[] ColumnStations;
            public double[] FloorStations;
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

        private sealed class CompositeBottomBoltFeet
        {
            public readonly List<P2> Vertical = new List<P2>();
            public readonly List<P2> Width = new List<P2>();
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
                if (point == null)
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

        private sealed class GridAxis
        {
            public bool IsVertical;
            public double Coordinate;
            public double SpanMin;
            public double SpanMax;
            public int ModelIdentifier;
            public string Label;
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

        private sealed class ViewSnapshot
        {
            public int Identifier;
            public TSD.View View;
            public TSG.Point OriginalOrigin;
        }

        private sealed class ViewGeometry
        {
            public ViewSnapshot Snapshot;
            public TSD.View View;
            public double Scale;
            public double RefX;
            public double RefBottomY;
            public double RefTopY;
            public Bounds2 MainBounds;
            public readonly List<P2> MainVertices = new List<P2>();
            public GridAxis VerticalGrid;
            public readonly List<GridAxis> HorizontalLevels = new List<GridAxis>();
            public readonly List<ExistingDimension> Dimensions = new List<ExistingDimension>();
        }

        private sealed class CapturedShapeDimension
        {
            public ViewSnapshot View;
            public ExistingDimension Dimension;
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

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public string ProjectKey;
            public ColumnGridStrategy Strategy;
            public ViewSnapshot Left;
            public ViewSnapshot Right;
            public ExistingDimension ReplaceableLeftVerticalTotal;
            public ExistingDimension ReplaceableRightVerticalTotal;
            public readonly List<CapturedShapeDimension> ShapeDimensions =
                new List<CapturedShapeDimension>();
        }

        private static bool _enabled;
        private static int _requestedAxisCount;
        private static Context _context;

        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Configure(bool enabled, int requestedAxisCount)
        {
            ClearState();
            _enabled = enabled;
            _requestedAxisCount = requestedAxisCount;
            if (_enabled && _requestedAxisCount != 3)
            {
                _enabled = false;
                LastRunMessage = "Column Grid DIM disabled: Column mode only permits Grid=3.";
            }
        }

        public static void Reset()
        {
            ClearState();
        }

        /// <summary>
        /// Read-only preflight immediately before Shape.  Capturing identifiers
        /// and sheet origins here preserves the user's left/right intent even
        /// when Tekla runtime ViewType differs between the two approved samples.
        /// </summary>
        public static bool Begin(TSM.Model model, TSD.Drawing drawing, TSM.Part mainPart)
        {
            if (!_enabled)
                return false;

            _context = null;
            LastRunSucceeded = false;
            try
            {
                if (_requestedAxisCount != 3)
                    throw new InvalidOperationException(
                        "Column mode requires exactly three Grid axes."
                    );
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
                if (!PHU_ColumnProjectRouter.TryResolve(model, out projectKey, out routeMessage))
                    throw new InvalidOperationException(routeMessage);

                ColumnGridStrategy strategy;
                if (
                    String.Equals(
                        projectKey,
                        PHU_ColumnProjectRouter.InzaiDataCenterKey,
                        StringComparison.Ordinal
                    )
                )
                    strategy = ColumnGridStrategy.InzaiDataCenter;
                else if (
                    String.Equals(
                        projectKey,
                        PHU_ColumnProjectRouter.GeneralSystemKey,
                        StringComparison.Ordinal
                    )
                )
                    strategy = ColumnGridStrategy.GeneralSystem;
                else
                    throw new InvalidOperationException(
                        "No Column Grid strategy is registered for project key " + projectKey + "."
                    );

                TSG.Matrix currentToGlobal = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                List<ViewSnapshot> candidates = FindTwoVerticalMainViews(
                    drawing,
                    mainPart,
                    currentToGlobal
                );
                if (candidates.Count != 2)
                    throw new InvalidOperationException(
                        "Column Grid requires exactly two full vertical MainPart views; found "
                            + candidates.Count.ToString(CultureInfo.InvariantCulture)
                            + "."
                    );

                int capturedLeftId;
                int capturedRightId;
                TSG.Point capturedLeftOrigin;
                TSG.Point capturedRightOrigin;
                if (
                    PHU_VerticalShapeViewLayoutContext.TryGetCapturedViewOrder(
                        out capturedLeftId,
                        out capturedRightId,
                        out capturedLeftOrigin,
                        out capturedRightOrigin
                    )
                )
                {
                    ViewSnapshot capturedLeft = FindSnapshotById(candidates, capturedLeftId);
                    ViewSnapshot capturedRight = FindSnapshotById(candidates, capturedRightId);
                    if (capturedLeft == null || capturedRight == null)
                        throw new InvalidOperationException(
                            "The initial user view order could not be rebound after preflight."
                        );
                    capturedLeft.OriginalOrigin = ClonePoint(capturedLeftOrigin);
                    capturedRight.OriginalOrigin = ClonePoint(capturedRightOrigin);
                    candidates.Clear();
                    candidates.Add(capturedLeft);
                    candidates.Add(capturedRight);
                }
                else
                {
                    candidates.Sort(
                        delegate(ViewSnapshot a, ViewSnapshot b)
                        {
                            double dx = a.OriginalOrigin.X - b.OriginalOrigin.X;
                            if (Math.Abs(dx) > GeometryTolerance)
                                return dx < 0.0 ? -1 : 1;
                            return a.Identifier.CompareTo(b.Identifier);
                        }
                    );
                }
                if (
                    Math.Abs(candidates[0].OriginalOrigin.X - candidates[1].OriginalOrigin.X)
                    <= GeometryTolerance
                )
                    throw new InvalidOperationException(
                        "The two Column views do not have a stable left/right order."
                    );

                Context context = new Context();
                context.Model = model;
                context.Drawing = drawing;
                context.MainPart = mainPart;
                context.ProjectKey = projectKey;
                context.Strategy = strategy;
                context.Left = candidates[0];
                context.Right = candidates[1];
                _context = context;
                LastRunMessage =
                    (
                        strategy == ColumnGridStrategy.InzaiDataCenter
                            ? "Inzai Column"
                            : "General Column"
                    )
                    + " preflight passed; user view order captured LEFT="
                    + context.Left.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ", RIGHT="
                    + context.Right.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                _context = null;
                LastRunMessage = "Column Grid preflight skipped. " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Pure regression for the Type-2-only horizontal terminal/grid tiers.
        /// It proves that Type 2 consumes the published Shape-spaced grid line
        /// while every other family retains the legacy clearance rule.
        /// </summary>
        public static string AuditType2HorizontalGridRegression()
        {
            const int total = 4;
            int passed = 0;
            try
            {
                ViewGeometry view = new ViewGeometry();
                view.RefTopY = 5613.0;

                PHU_InzaiCompositeViewGeometry type2 =
                    new PHU_InzaiCompositeViewGeometry();
                type2.HorizontalClearanceY = 6613.0;
                type2.Type2TerminalFamily = true;
                type2.Type2TerminalAxisLine = 6013.0;
                if (Math.Abs(
                    ResolveCompositeHorizontalGridLine(view, type2, 200.0)
                    - 6013.0) <= MatchTolerance)
                    passed++;
                P2 type2Direction = ResolveCompositeHorizontalGridDirection(
                    type2,
                    6013.0,
                    6791.0);
                if (type2Direction != null
                    && type2Direction.Y < -DirectionCosineTolerance)
                    passed++;

                PHU_InzaiCompositeViewGeometry nonType2 =
                    new PHU_InzaiCompositeViewGeometry();
                nonType2.HorizontalClearanceY = 6613.0;
                nonType2.Type2TerminalFamily = false;
                nonType2.Type2TerminalAxisLine = 6013.0;
                if (Math.Abs(
                    ResolveCompositeHorizontalGridLine(view, nonType2, 200.0)
                    - 7013.0) <= MatchTolerance)
                    passed++;
                P2 nonType2Direction = ResolveCompositeHorizontalGridDirection(
                    nonType2,
                    7013.0,
                    6791.0);
                if (nonType2Direction != null
                    && nonType2Direction.Y > DirectionCosineTolerance)
                    passed++;

                return "TYPE2 HORIZONTAL GRID REGRESSION="
                    + passed.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + total.ToString(CultureInfo.InvariantCulture)
                    + (passed == total ? " PASS" : " FAIL");
            }
            catch (Exception ex)
            {
                return "TYPE2 HORIZONTAL GRID REGRESSION="
                    + passed.ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + total.ToString(CultureInfo.InvariantCulture)
                    + " FAIL "
                    + ex.Message;
            }
        }

        /// <summary>
        /// Read-only production audit for the routed Column strategy. It never
        /// creates, modifies, deletes, or commits a drawing object.
        /// </summary>
        public static string AuditPreparedGeometry()
        {
            if (_context == null)
                return "Column Grid audit unavailable: preflight has not passed.";
            try
            {
                RebindCapturedViews(_context);
                TSG.Matrix currentToGlobal = _context
                    .Model.GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                bool general = _context.Strategy == ColumnGridStrategy.GeneralSystem;
                ViewGeometry left = ReadViewGeometry(
                    _context,
                    _context.Left,
                    currentToGlobal,
                    true,
                    general
                );
                ViewGeometry right = ReadViewGeometry(
                    _context,
                    _context.Right,
                    currentToGlobal,
                    general,
                    general
                );
                StringBuilder text = new StringBuilder();
                text.Append(general ? "GENERAL" : "INZAI")
                    .Append(" LEFT refX=")
                    .Append(Format(left.RefX))
                    .Append(" colY=[")
                    .Append(Format(left.MainBounds.MinY))
                    .Append(',')
                    .Append(Format(left.MainBounds.MaxY))
                    .Append(']')
                    .Append(" vGrid=")
                    .Append(left.VerticalGrid.Label)
                    .Append('@')
                    .Append(Format(left.VerticalGrid.Coordinate))
                    .Append(" levels=");
                for (int i = 0; i < left.HorizontalLevels.Count; i++)
                {
                    if (i > 0)
                        text.Append(',');
                    text.Append(left.HorizontalLevels[i].Label)
                        .Append('@')
                        .Append(Format(left.HorizontalLevels[i].Coordinate));
                }
                text.Append(" | RIGHT refX=")
                    .Append(Format(right.RefX))
                    .Append(" vGrid=")
                    .Append(right.VerticalGrid.Label)
                    .Append('@')
                    .Append(Format(right.VerticalGrid.Coordinate));
                if (general)
                {
                    text.Append(" | ").Append(BuildGeneralGeometryAudit(left, right));
                }
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "Column Grid audit failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Captures only the dimension objects produced by the Shape stage. The
        /// snapshot is taken before Neighbor/Connection run, so the final Inzai
        /// composite stage can replace exactly these objects without scanning or
        /// deleting any connection dimension by geometry or by drawing region.
        /// General Column and non-prepared flows are intentionally untouched.
        /// </summary>
        public static bool CaptureShapeDimensionsBeforeAddOns()
        {
            if (
                _context == null
                || _context.Strategy != ColumnGridStrategy.InzaiDataCenter
            )
                return false;

            _context.ShapeDimensions.Clear();
            try
            {
                RebindCapturedViews(_context);
                CaptureShapeDimensions(_context.Left, _context.ShapeDimensions);
                CaptureShapeDimensions(_context.Right, _context.ShapeDimensions);
                if (_context.ShapeDimensions.Count == 0)
                    throw new InvalidOperationException(
                        "Shape produced no straight dimension set to hand off."
                    );
                return true;
            }
            catch (Exception ex)
            {
                _context.ShapeDimensions.Clear();
                LastRunMessage = "Inzai Shape DIM handoff skipped. " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Final connected flow: analyze after Shape, create and verify all four
        /// plans, align the captured views once, then delete only the exact Shape
        /// vertical total superseded by the left column chain.
        /// </summary>
        public static bool ExecuteAfterShape()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            bool replacementDeleted = false;
            try
            {
                if (_context == null)
                    return false;
                if (_context.Strategy == ColumnGridStrategy.GeneralSystem)
                    return ExecuteGeneralAfterShape();

                RebindCapturedViews(_context);
                TSG.Matrix currentToGlobal = _context
                    .Model.GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                ViewGeometry left = ReadViewGeometry(
                    _context,
                    _context.Left,
                    currentToGlobal,
                    true,
                    false
                );
                ViewGeometry right = ReadViewGeometry(
                    _context,
                    _context.Right,
                    currentToGlobal,
                    false,
                    false
                );

                PHU_InzaiCompositeViewGeometry leftComposite;
                PHU_InzaiCompositeViewGeometry rightComposite;
                if (
                    _context.ShapeDimensions.Count > 0
                    && PHU_InzaiColumnCompositeDimensionContext.TryGet(
                        left.Snapshot.Identifier,
                        out leftComposite
                    )
                    && PHU_InzaiColumnCompositeDimensionContext.TryGet(
                        right.Snapshot.Identifier,
                        out rightComposite
                    )
                )
                {
                    return ExecuteInzaiCompositeAfterShape(
                        left,
                        right,
                        leftComposite,
                        rightComposite
                    );
                }

                List<DimPlan> plans = BuildInzaiPlans(left, right, _context);
                ValidatePlans(plans, 4);

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
                        "Created DIM count does not match the validated plan count."
                    );
                VerifyCreatedDimensions(plans, created);

                ExistingDimension replacement = _context.ReplaceableLeftVerticalTotal;
                if (
                    replacement == null
                    || replacement.Dimension == null
                    || !replacement.Dimension.Delete()
                )
                    throw new InvalidOperationException(
                        "The exact Shape vertical total could not be replaced safely."
                    );
                replacementDeleted = true;

                _context.Drawing.CommitChanges();
                LastRunSucceeded = true;
                LastRunMessage =
                    "Inzai Column Grid=3 created 4 DIMs: LEFT column/floor/ref-grid, "
                    + "RIGHT ref-grid.";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                if (replacementDeleted)
                    RestoreReplacement(
                        _context == null ? null : _context.ReplaceableLeftVerticalTotal
                    );
                TryCommit(_context == null ? null : _context.Drawing);
                LastRunSucceeded = false;
                LastRunMessage = "Inzai Column rolled back. " + ex.Message;
                return false;
            }
        }

        private static bool ExecuteInzaiCompositeAfterShape(
            ViewGeometry left,
            ViewGeometry right,
            PHU_InzaiCompositeViewGeometry leftComposite,
            PHU_InzaiCompositeViewGeometry rightComposite
        )
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            List<CapturedShapeDimension> deleted = new List<CapturedShapeDimension>();
            try
            {
                List<DimPlan> plans = BuildInzaiCompositePlans(
                    left,
                    right,
                    leftComposite,
                    rightComposite,
                    _context
                );
                ValidatePlans(plans, 9);
                CreateDimensionPlans(plans, created);
                VerifyCreatedDimensions(plans, created);
                DeleteCapturedShapeDimensions(_context.ShapeDimensions, deleted);

                _context.Drawing.CommitChanges();
                LastRunSucceeded = true;
                LastRunMessage =
                    "Inzai composite Shape/Connection/Grid satisfied "
                    + plans.Count.ToString(CultureInfo.InvariantCulture)
                    + " final Shape/Grid plans; replaced exactly "
                    + deleted.Count.ToString(CultureInfo.InvariantCulture)
                    + " captured Shape DIMs.";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                RestoreCapturedShapeDimensions(deleted);
                TryCommit(_context == null ? null : _context.Drawing);
                LastRunSucceeded = false;
                LastRunMessage = "Inzai composite rolled back. " + ex.Message;
                return false;
            }
        }

        private static bool ExecuteGeneralAfterShape()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();
            List<ExistingDimension> deleted = new List<ExistingDimension>();
            try
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
                    true,
                    true
                );
                ViewGeometry right = ReadViewGeometry(
                    _context,
                    _context.Right,
                    currentToGlobal,
                    true,
                    true
                );

                string summary;
                List<DimPlan> plans = BuildGeneralPlans(left, right, _context, out summary);
                ValidateGeneralPlans(plans);
                CreateDimensionPlans(plans, created);
                VerifyCreatedDimensions(plans, created);

                DeleteGeneralReplacement(_context.ReplaceableLeftVerticalTotal, deleted, "LEFT");
                DeleteGeneralReplacement(_context.ReplaceableRightVerticalTotal, deleted, "RIGHT");

                _context.Drawing.CommitChanges();
                LastRunSucceeded = true;
                LastRunMessage =
                    "General Column Grid=3 created "
                    + plans.Count.ToString(CultureInfo.InvariantCulture)
                    + " DIMs; "
                    + summary
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                for (int i = 0; i < deleted.Count; i++)
                {
                    ExistingDimension replacement = deleted[i];
                    TSD.View view =
                        replacement
                        == (_context == null ? null : _context.ReplaceableLeftVerticalTotal)
                            ? (_context == null ? null : _context.Left.View)
                            : (_context == null ? null : _context.Right.View);
                    RestoreReplacement(replacement, view);
                }
                TryCommit(_context == null ? null : _context.Drawing);
                LastRunSucceeded = false;
                LastRunMessage = "General Column Grid rolled back. " + ex.Message;
                return false;
            }
        }

        private static List<DimPlan> BuildGeneralPlans(
            ViewGeometry left,
            ViewGeometry right,
            Context context,
            out string summary
        )
        {
            summary = String.Empty;
            if (left == null || right == null || context == null)
                throw new InvalidOperationException(
                    "The captured General Column views are unavailable after Shape."
                );

            ExistingDimension leftTotal = FindReplaceableVerticalTotal(left);
            ExistingDimension rightTotal = FindReplaceableVerticalTotal(right);
            if (leftTotal == null || rightTotal == null)
                throw new InvalidOperationException(
                    "Both views require one exact Shape vertical total; protected DIMs were not changed."
                );
            context.ReplaceableLeftVerticalTotal = leftTotal;
            context.ReplaceableRightVerticalTotal = rightTotal;

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

            double[] leftFloors = SelectGeneralFloorPair(left);
            double[] rightFloors = SelectGeneralFloorPair(right);
            P2 leftEdgeBottom;
            P2 leftEdgeTop;
            P2 rightEdgeBottom;
            P2 rightEdgeTop;
            ResolveReplacementEdgePoints(leftTotal, out leftEdgeBottom, out leftEdgeTop);
            ResolveReplacementEdgePoints(rightTotal, out rightEdgeBottom, out rightEdgeTop);

            PHU_GeneralColumnVerticalPlan leftVertical = PHU_GeneralColumnGridPlanner.BuildVertical(
                leftFloors[0],
                leftFloors[1],
                left.RefBottomY,
                left.RefTopY,
                leftEdgeBottom.Y,
                leftEdgeTop.Y,
                MatchTolerance
            );
            PHU_GeneralColumnVerticalPlan rightVertical =
                PHU_GeneralColumnGridPlanner.BuildVertical(
                    rightFloors[0],
                    rightFloors[1],
                    right.RefBottomY,
                    right.RefTopY,
                    rightEdgeBottom.Y,
                    rightEdgeTop.Y,
                    MatchTolerance
                );

            List<DimPlan> plans = new List<DimPlan>();
            AppendGeneralVerticalPlans(
                plans,
                left,
                leftTotal,
                leftEdgeBottom,
                leftEdgeTop,
                leftVertical.LeftInnerToOuter,
                tierStep,
                "LEFT"
            );
            AppendGeneralVerticalPlans(
                plans,
                right,
                rightTotal,
                rightEdgeBottom,
                rightEdgeTop,
                rightVertical.RightInnerToOuter,
                tierStep,
                "RIGHT"
            );

            plans.Add(BuildGeneralHorizontalGridPlan(left, leftTotal.Attributes, true, tierStep));
            plans.Add(
                BuildGeneralHorizontalGridPlan(right, rightTotal.Attributes, false, tierStep)
            );

            summary =
                "LEFT="
                + leftVertical.Topology.ToString()
                + ", RIGHT="
                + rightVertical.Topology.ToString()
                + ", Shape tier base="
                + Format(tierBase)
                + ", step="
                + Format(tierStep);
            return plans;
        }

        private static string BuildGeneralGeometryAudit(ViewGeometry left, ViewGeometry right)
        {
            double[] leftFloors = SelectGeneralFloorPair(left);
            double[] rightFloors = SelectGeneralFloorPair(right);
            PHU_GeneralColumnVerticalPlan leftVertical = PHU_GeneralColumnGridPlanner.BuildVertical(
                leftFloors[0],
                leftFloors[1],
                left.RefBottomY,
                left.RefTopY,
                left.MainBounds.MinY,
                left.MainBounds.MaxY,
                MatchTolerance
            );
            PHU_GeneralColumnVerticalPlan rightVertical =
                PHU_GeneralColumnGridPlanner.BuildVertical(
                    rightFloors[0],
                    rightFloors[1],
                    right.RefBottomY,
                    right.RefTopY,
                    right.MainBounds.MinY,
                    right.MainBounds.MaxY,
                    MatchTolerance
                );
            PHU_GeneralColumnHorizontalPlan leftHorizontal =
                PHU_GeneralColumnGridPlanner.BuildHorizontal(
                    left.RefX,
                    left.VerticalGrid.Coordinate,
                    left.MainBounds.MinX,
                    left.MainBounds.MaxX,
                    MatchTolerance
                );
            PHU_GeneralColumnHorizontalPlan rightHorizontal =
                PHU_GeneralColumnGridPlanner.BuildHorizontal(
                    right.RefX,
                    right.VerticalGrid.Coordinate,
                    right.MainBounds.MinX,
                    right.MainBounds.MaxX,
                    MatchTolerance
                );

            ExistingDimension leftReplacement = FindReplaceableVerticalTotal(left);
            ExistingDimension rightReplacement = FindReplaceableVerticalTotal(right);
            StringBuilder text = new StringBuilder();
            text.Append("geometry-only LEFT=")
                .Append(leftVertical.Topology.ToString())
                .Append(" RIGHT=")
                .Append(rightVertical.Topology.ToString())
                .Append(" HLEFT=")
                .Append(leftHorizontal.Relation.ToString())
                .Append(" HRIGHT=")
                .Append(rightHorizontal.Relation.ToString())
                .Append(" shape-total-now=[")
                .Append(leftReplacement != null ? "ready" : "pending")
                .Append(',')
                .Append(rightReplacement != null ? "ready" : "pending")
                .Append(']');
            AppendGeneralChainAudit(
                text,
                " LEFT tiers inner->outer",
                leftVertical.LeftInnerToOuter
            );
            AppendGeneralChainAudit(
                text,
                " RIGHT tiers inner->outer",
                rightVertical.RightInnerToOuter
            );
            return text.ToString();
        }

        private static void AppendGeneralChainAudit(
            StringBuilder text,
            string label,
            List<PHU_GeneralColumnVerticalChain> chains
        )
        {
            text.Append(label).Append('=');
            for (int i = 0; chains != null && i < chains.Count; i++)
            {
                if (i > 0)
                    text.Append(" | ");
                text.Append(chains[i].Role.ToString()).Append('(');
                for (int p = 0; chains[i].Stations != null && p < chains[i].Stations.Length; p++)
                {
                    if (p > 0)
                        text.Append("->");
                    text.Append(Format(chains[i].Stations[p]));
                }
                text.Append(')');
            }
        }

        private static double[] SelectGeneralFloorPair(ViewGeometry view)
        {
            List<double> coordinates = new List<double>();
            for (int i = 0; view != null && i < view.HorizontalLevels.Count; i++)
                coordinates.Add(view.HorizontalLevels[i].Coordinate);
            return PHU_GeneralColumnGridPlanner.SelectFloorPair(coordinates, MatchTolerance);
        }

        private static void ResolveReplacementEdgePoints(
            ExistingDimension replacement,
            out P2 bottom,
            out P2 top
        )
        {
            bottom = null;
            top = null;
            for (int i = 0; replacement != null && i < replacement.Points.Count; i++)
            {
                P2 point = replacement.Points[i];
                if (point == null)
                    continue;
                if (bottom == null || point.Y < bottom.Y)
                    bottom = point;
                if (top == null || point.Y > top.Y)
                    top = point;
            }
            if (bottom == null || top == null || top.Y - bottom.Y <= GeometryTolerance)
            {
                throw new InvalidOperationException(
                    "A Shape vertical total has invalid edge feet."
                );
            }
        }

        private static void AppendGeneralVerticalPlans(
            List<DimPlan> plans,
            ViewGeometry view,
            ExistingDimension replacement,
            P2 edgeBottom,
            P2 edgeTop,
            List<PHU_GeneralColumnVerticalChain> chains,
            double tierStep,
            string viewName
        )
        {
            int side = ResolveVerticalDimensionSide(view, replacement);
            for (int tier = 0; chains != null && tier < chains.Count; tier++)
            {
                PHU_GeneralColumnVerticalChain chain = chains[tier];
                double line = replacement.LineCoordinate + (side * tierStep * tier);
                List<P2> feet = new List<P2>();
                for (int i = 0; chain.Stations != null && i < chain.Stations.Length; i++)
                {
                    feet.Add(
                        ResolveGeneralVerticalFoot(
                            view,
                            edgeBottom,
                            edgeTop,
                            chain.Role,
                            chain.Stations[i]
                        )
                    );
                }
                DimPlan plan = NewPlanAtLine(
                    viewName + " " + chain.Role.ToString(),
                    view,
                    new P2(side, 0.0),
                    line,
                    replacement.Attributes,
                    true,
                    feet
                );
                plans.Add(plan);
            }
        }

        private static P2 ResolveGeneralVerticalFoot(
            ViewGeometry view,
            P2 edgeBottom,
            P2 edgeTop,
            PHU_GeneralColumnVerticalChainRole role,
            double station
        )
        {
            if (
                Math.Abs(station - view.RefBottomY) <= MatchTolerance
                || Math.Abs(station - view.RefTopY) <= MatchTolerance
            )
                return new P2(view.RefX, station);
            if (role == PHU_GeneralColumnVerticalChainRole.ShapeRefEdges)
            {
                if (Math.Abs(station - edgeBottom.Y) <= MatchTolerance)
                    return new P2(edgeBottom.X, edgeBottom.Y);
                if (Math.Abs(station - edgeTop.Y) <= MatchTolerance)
                    return new P2(edgeTop.X, edgeTop.Y);
            }
            // A floor foot is the exact Grid/REF intersection in this view.
            return new P2(view.RefX, station);
        }

        private static int ResolveVerticalDimensionSide(
            ViewGeometry view,
            ExistingDimension replacement
        )
        {
            if (replacement.LineCoordinate < view.MainBounds.MinX - GeometryTolerance)
                return -1;
            if (replacement.LineCoordinate > view.MainBounds.MaxX + GeometryTolerance)
                return 1;
            throw new InvalidOperationException(
                "The Shape vertical total is not outside the MainPart."
            );
        }

        private static DimPlan NewPlanAtLine(
            string name,
            ViewGeometry view,
            P2 direction,
            double lineCoordinate,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes,
            bool disableCombine,
            List<P2> feet
        )
        {
            if (feet == null || feet.Count < 2)
                throw new InvalidOperationException(name + " has fewer than two feet.");
            DimPlan plan = NewPlan(name, view, direction, 0.0, attributes, disableCombine);
            plan.Points.AddRange(feet);
            if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
                plan.Distance = (lineCoordinate - plan.Points[0].X) / direction.X;
            else
                plan.Distance = (lineCoordinate - plan.Points[0].Y) / direction.Y;
            return plan;
        }

        private static DimPlan BuildGeneralHorizontalGridPlan(
            ViewGeometry view,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes fallbackAttributes,
            bool isLeft,
            double tierStep
        )
        {
            if (view.VerticalGrid == null)
                throw new InvalidOperationException(
                    "A General Column view has no resolved vertical Grid."
                );

            P2 leftEdge = SelectTopExtremeVertex(view, false);
            P2 rightEdge = SelectTopExtremeVertex(view, true);
            PHU_GeneralColumnHorizontalPlan geometry = PHU_GeneralColumnGridPlanner.BuildHorizontal(
                view.RefX,
                view.VerticalGrid.Coordinate,
                leftEdge.X,
                rightEdge.X,
                MatchTolerance
            );

            double topLine = view.RefTopY + tierStep;
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes = fallbackAttributes;
            for (int i = 0; i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (dimension.Direction == null || dimension.Direction.Y < DirectionCosineTolerance)
                    continue;
                topLine = Math.Max(topLine, dimension.LineCoordinate + tierStep);
                if (dimension.Attributes != null)
                    attributes = dimension.Attributes;
            }

            List<P2> feet = new List<P2>();
            for (int i = 0; i < geometry.Stations.Length; i++)
            {
                double station = geometry.Stations[i];
                if (
                    geometry.Relation
                        == PHU_GeneralColumnHorizontalRelation.GridCoincidentWithReference
                    && Math.Abs(station - leftEdge.X) <= MatchTolerance
                )
                    feet.Add(new P2(leftEdge.X, leftEdge.Y));
                else if (
                    geometry.Relation
                        == PHU_GeneralColumnHorizontalRelation.GridCoincidentWithReference
                    && Math.Abs(station - rightEdge.X) <= MatchTolerance
                )
                    feet.Add(new P2(rightEdge.X, rightEdge.Y));
                else
                    feet.Add(new P2(station, view.RefTopY));
            }

            return NewPlanAtLine(
                (isLeft ? "LEFT " : "RIGHT ") + "HORIZONTAL " + geometry.Relation.ToString(),
                view,
                new P2(0.0, 1.0),
                topLine,
                attributes,
                geometry.Stations.Length > 2,
                feet
            );
        }

        private static P2 SelectTopExtremeVertex(ViewGeometry view, bool right)
        {
            double targetX = right ? view.MainBounds.MaxX : view.MainBounds.MinX;
            P2 selected = null;
            for (int i = 0; i < view.MainVertices.Count; i++)
            {
                P2 candidate = view.MainVertices[i];
                if (candidate == null || Math.Abs(candidate.X - targetX) > MatchTolerance)
                    continue;
                if (selected == null || candidate.Y > selected.Y)
                    selected = candidate;
            }
            if (selected == null)
                throw new InvalidOperationException(
                    "An exact MainPart top edge anchor could not be resolved."
                );
            return new P2(selected.X, selected.Y);
        }

        private static void CreateDimensionPlans(
            List<DimPlan> plans,
            List<TSD.StraightDimensionSet> created
        )
        {
            TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
            for (int i = 0; plans != null && i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                TSD.PointList points = new TSD.PointList();
                for (int p = 0; p < plan.Points.Count; p++)
                    points.Add(plan.Points[p].ToPoint());
                TSG.Vector direction = new TSG.Vector(plan.Direction.X, plan.Direction.Y, 0.0);
                TSD.StraightDimensionSet dimension =
                    plan.Attributes == null
                        ? handler.CreateDimensionSet(
                            plan.View.View,
                            points,
                            direction,
                            plan.Distance
                        )
                        : handler.CreateDimensionSet(
                            plan.View.View,
                            points,
                            direction,
                            plan.Distance,
                            plan.Attributes
                        );
                if (dimension == null)
                    throw new InvalidOperationException("Tekla did not create " + plan.Name + ".");
                created.Add(dimension);
                if (plan.DisableCombine)
                    DisableCombine(dimension, plan.Points.Count, plan.Name);
            }
            if (created.Count != plans.Count)
                throw new InvalidOperationException(
                    "Created DIM count does not match the validated General plan count."
                );
        }

        private static void DeleteGeneralReplacement(
            ExistingDimension replacement,
            List<ExistingDimension> deleted,
            string viewName
        )
        {
            if (
                replacement == null
                || replacement.Dimension == null
                || !replacement.Dimension.Delete()
            )
            {
                throw new InvalidOperationException(
                    "The exact " + viewName + " Shape vertical total could not be replaced safely."
                );
            }
            deleted.Add(replacement);
        }

        private static List<DimPlan> BuildInzaiCompositePlans(
            ViewGeometry left,
            ViewGeometry right,
            PHU_InzaiCompositeViewGeometry leftGeometry,
            PHU_InzaiCompositeViewGeometry rightGeometry,
            Context context
        )
        {
            ValidateCompositeGeometry(left, leftGeometry, "LEFT");
            ValidateCompositeGeometry(right, rightGeometry, "RIGHT");
            if (left.HorizontalLevels.Count != 2)
                throw new InvalidOperationException(
                    "Composite LEFT view must resolve exactly two floor Grid levels."
                );

            double lowerGrid = Math.Min(
                left.HorizontalLevels[0].Coordinate,
                left.HorizontalLevels[1].Coordinate
            );
            double upperGrid = Math.Max(
                left.HorizontalLevels[0].Coordinate,
                left.HorizontalLevels[1].Coordinate
            );
            if (upperGrid - lowerGrid <= MatchTolerance)
                throw new InvalidOperationException(
                    "Composite floor Grid levels are not distinct."
                );

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

            TSD.StraightDimensionSet.StraightDimensionSetAttributes leftAttributes =
                FindCapturedAttributes(context, left.Snapshot.Identifier)
                ?? FindAnyDimensionAttributes(left);
            TSD.StraightDimensionSet.StraightDimensionSetAttributes rightAttributes =
                FindCapturedAttributes(context, right.Snapshot.Identifier)
                ?? FindAnyDimensionAttributes(right);

            double leftLine = ResolveCompositeFirstVerticalLine(
                context,
                left,
                -1,
                tierStep
            );
            double rightLine = ResolveCompositeFirstVerticalLine(
                context,
                right,
                -1,
                tierStep
            );

            List<DimPlan> plans = new List<DimPlan>();

            plans.Add(
                NewPlanAtLine(
                    "LEFT BASE-FLOOR-SPLICE",
                    left,
                    new P2(-1.0, 0.0),
                    leftLine,
                    leftAttributes,
                    true,
                    NewFeet(
                        new P2(leftGeometry.BaseLeftX, leftGeometry.BaseY),
                        new P2(leftGeometry.BaseLeftX, lowerGrid),
                        new P2(leftGeometry.SpliceLowLeftX, leftGeometry.SpliceLowY)
                    )
                )
            );
            leftLine -= tierStep;
            plans.Add(
                NewPlanAtLine(
                    "LEFT COLUMN SEGMENTS",
                    left,
                    new P2(-1.0, 0.0),
                    leftLine,
                    leftAttributes,
                    true,
                    NewFeet(
                        new P2(leftGeometry.BaseLeftX, leftGeometry.BaseY),
                        new P2(leftGeometry.SpliceLowLeftX, leftGeometry.SpliceLowY),
                        new P2(leftGeometry.SpliceHighLeftX, leftGeometry.SpliceHighY),
                        new P2(leftGeometry.TerminalLeftX, leftGeometry.TerminalY)
                    )
                )
            );
            leftLine -= tierStep;
            plans.Add(
                NewPlanAtLine(
                    "LEFT BASE-TERMINAL-UPPER GRID",
                    left,
                    new P2(-1.0, 0.0),
                    leftLine,
                    leftAttributes,
                    true,
                    NewFeet(
                        new P2(leftGeometry.BaseLeftX, leftGeometry.BaseY),
                        new P2(leftGeometry.TerminalLeftX, leftGeometry.TerminalY),
                        new P2(leftGeometry.RefX, upperGrid)
                    )
                )
            );
            leftLine -= tierStep;
            plans.Add(
                NewPlanAtLine(
                    "LEFT FLOOR TOTAL",
                    left,
                    new P2(-1.0, 0.0),
                    leftLine,
                    leftAttributes,
                    true,
                    NewFeet(
                        new P2(leftGeometry.BaseLeftX, leftGeometry.BaseY),
                        new P2(leftGeometry.BaseLeftX, lowerGrid),
                        new P2(leftGeometry.RefX, upperGrid)
                    )
                )
            );

            plans.Add(
                NewPlanAtLine(
                    "RIGHT COLUMN SEGMENTS",
                    right,
                    new P2(-1.0, 0.0),
                    rightLine,
                    rightAttributes,
                    true,
                    NewFeet(
                        new P2(rightGeometry.BaseLeftX, rightGeometry.BaseY),
                        new P2(rightGeometry.SpliceLowLeftX, rightGeometry.SpliceLowY),
                        new P2(rightGeometry.SpliceHighLeftX, rightGeometry.SpliceHighY),
                        new P2(rightGeometry.TerminalLeftX, rightGeometry.TerminalY)
                    )
                )
            );
            rightLine -= tierStep;
            plans.Add(
                NewPlanAtLine(
                    "RIGHT BASE-TERMINAL-UPPER GRID",
                    right,
                    new P2(-1.0, 0.0),
                    rightLine,
                    rightAttributes,
                    true,
                    NewFeet(
                        new P2(rightGeometry.BaseLeftX, rightGeometry.BaseY),
                        new P2(rightGeometry.TerminalLeftX, rightGeometry.TerminalY),
                        new P2(rightGeometry.RefX, upperGrid)
                    )
                )
            );

            AppendCompositeBottomPlans(
                plans,
                left,
                leftGeometry,
                context,
                leftAttributes,
                tierBase
            );

            double rightGridLine = ResolveCompositeHorizontalGridLine(
                right,
                rightGeometry,
                tierStep
            );
            P2 rightGridDirection = ResolveCompositeHorizontalGridDirection(
                rightGeometry,
                rightGridLine,
                upperGrid);
            plans.Add(
                NewPlanAtLine(
                    "RIGHT REF-GRID",
                    right,
                    rightGridDirection,
                    rightGridLine,
                    rightAttributes,
                    false,
                    NewFeet(
                        new P2(right.VerticalGrid.Coordinate, upperGrid),
                        new P2(rightGeometry.RefX, rightGeometry.TerminalRefY)
                    )
                )
            );

            if (plans.Count != 9)
                throw new InvalidOperationException(
                    "Composite planner did not produce the required nine Shape/Grid plans."
                );
            return plans;
        }

        private static void ValidateCompositeGeometry(
            ViewGeometry view,
            PHU_InzaiCompositeViewGeometry geometry,
            string viewName
        )
        {
            if (
                view == null
                || view.Snapshot == null
                || geometry == null
                || geometry.ViewIdentifier != view.Snapshot.Identifier
            )
                throw new InvalidOperationException(
                    viewName + " composite geometry is not bound to the captured view."
                );

            double[] values = new double[]
            {
                geometry.RefX,
                geometry.BaseY,
                geometry.BaseLeftX,
                geometry.BaseRightX,
                geometry.SpliceLowY,
                geometry.SpliceLowLeftX,
                geometry.SpliceHighY,
                geometry.SpliceHighLeftX,
                geometry.TerminalY,
                geometry.TerminalLeftX,
                geometry.TerminalRightX,
                geometry.TerminalRefY,
                geometry.HorizontalClearanceY
            };
            for (int i = 0; i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    throw new InvalidOperationException(
                        viewName + " composite geometry contains a non-finite coordinate."
                    );
            }
            if (
                geometry.SpliceLowY <= geometry.BaseY + GeometryTolerance
                || geometry.SpliceHighY <= geometry.SpliceLowY + GeometryTolerance
                || geometry.TerminalY <= geometry.SpliceHighY + GeometryTolerance
                || geometry.TerminalRefY < geometry.TerminalY - MatchTolerance
                || geometry.BaseRightX <= geometry.BaseLeftX + GeometryTolerance
                || geometry.TerminalRightX <= geometry.TerminalLeftX + GeometryTolerance
            )
                throw new InvalidOperationException(
                    viewName + " composite stations are not geometrically ordered."
                );
        }

        private static List<P2> NewFeet(params P2[] points)
        {
            List<P2> result = new List<P2>();
            for (int i = 0; points != null && i < points.Length; i++)
                result.Add(points[i]);
            return result;
        }

        private static double ResolveCompositeFirstVerticalLine(
            Context context,
            ViewGeometry view,
            int side,
            double tierStep
        )
        {
            double connectionLine;
            if (
                PHU_ColumnDimensionTierContext.TryGetNeighborOutermostVerticalLine(
                    view.Snapshot.Identifier,
                    side,
                    out connectionLine
                )
            )
                return connectionLine + (side * tierStep);

            double capturedLine;
            if (
                TryGetOutermostCapturedVerticalLine(
                    context,
                    view.Snapshot.Identifier,
                    side,
                    out capturedLine
                )
            )
                return capturedLine;

            throw new InvalidOperationException(
                "Composite vertical tier cannot be anchored to Connection or Shape."
            );
        }

        private static bool TryGetOutermostCapturedVerticalLine(
            Context context,
            int viewIdentifier,
            int side,
            out double line
        )
        {
            line = side < 0 ? Double.PositiveInfinity : Double.NegativeInfinity;
            bool found = false;
            for (int i = 0; context != null && i < context.ShapeDimensions.Count; i++)
            {
                CapturedShapeDimension captured = context.ShapeDimensions[i];
                ExistingDimension dimension = captured == null ? null : captured.Dimension;
                if (
                    captured == null
                    || captured.View == null
                    || captured.View.Identifier != viewIdentifier
                    || dimension == null
                    || dimension.Direction == null
                    || Math.Abs(dimension.Direction.X) < DirectionCosineTolerance
                    || !IsFinite(dimension.LineCoordinate)
                )
                    continue;
                if (
                    !found
                    || (side < 0 && dimension.LineCoordinate < line)
                    || (side > 0 && dimension.LineCoordinate > line)
                )
                {
                    line = dimension.LineCoordinate;
                    found = true;
                }
            }
            return found;
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes FindCapturedAttributes(
            Context context,
            int viewIdentifier
        )
        {
            for (int i = 0; context != null && i < context.ShapeDimensions.Count; i++)
            {
                CapturedShapeDimension captured = context.ShapeDimensions[i];
                if (
                    captured != null
                    && captured.View != null
                    && captured.View.Identifier == viewIdentifier
                    && captured.Dimension != null
                    && captured.Dimension.Attributes != null
                )
                    return captured.Dimension.Attributes;
            }
            return null;
        }

        private static void AppendCompositeBottomPlans(
            List<DimPlan> plans,
            ViewGeometry view,
            PHU_InzaiCompositeViewGeometry geometry,
            Context context,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes fallbackAttributes,
            double tierBase
        )
        {
            List<P2> bolts = new List<P2>();
            for (int i = 0; i < geometry.BottomBoltPoints.Count; i++)
            {
                TSG.Point raw = geometry.BottomBoltPoints[i];
                if (raw != null)
                    AddUniquePoint(bolts, new P2(raw.X, raw.Y));
            }
            CompositeBottomBoltFeet bottomFeet = BuildCompositeBottomBoltFeet(
                bolts,
                geometry.BaseLeftX,
                geometry.BaseRightX,
                geometry.BaseY
            );

            ExistingDimension verticalSource = FindCapturedBottomSource(
                context,
                view.Snapshot.Identifier,
                geometry,
                true
            );
            ExistingDimension horizontalSource = FindCapturedBottomSource(
                context,
                view.Snapshot.Identifier,
                geometry,
                false
            );
            double verticalLine = ResolveCompositeBottomLeftLine(
                geometry.BaseLeftX,
                geometry.BaseRightX,
                tierBase,
                verticalSource == null
                    ? Double.NaN
                    : verticalSource.LineCoordinate
            );
            double horizontalLine = horizontalSource == null
                ? geometry.BaseY - tierBase
                : horizontalSource.LineCoordinate;

            plans.Add(
                NewPlanAtLine(
                    "LEFT BASE BOLT VERTICAL",
                    view,
                    new P2(-1.0, 0.0),
                    verticalLine,
                    verticalSource == null ? fallbackAttributes : verticalSource.Attributes,
                    true,
                    bottomFeet.Vertical
                )
            );
            plans.Add(
                NewPlanAtLine(
                    "LEFT BASE BOLT WIDTH",
                    view,
                    new P2(0.0, -1.0),
                    horizontalLine,
                    horizontalSource == null ? fallbackAttributes : horizontalSource.Attributes,
                    true,
                    bottomFeet.Width
                )
            );
        }

        private static CompositeBottomBoltFeet BuildCompositeBottomBoltFeet(
            List<P2> rawBolts,
            double baseLeftX,
            double baseRightX,
            double baseY
        )
        {
            if (
                !IsFinite(baseLeftX)
                || !IsFinite(baseRightX)
                || !IsFinite(baseY)
                || baseRightX <= baseLeftX + GeometryTolerance
            )
                throw new InvalidOperationException(
                    "LEFT composite base geometry is invalid."
                );

            List<P2> bolts = new List<P2>();
            for (int i = 0; rawBolts != null && i < rawBolts.Count; i++)
            {
                P2 bolt = rawBolts[i];
                if (bolt == null || !IsFinite(bolt.X) || !IsFinite(bolt.Y))
                    continue;
                AddUniquePoint(bolts, new P2(bolt.X, bolt.Y));
            }
            if (bolts.Count < 2)
                throw new InvalidOperationException(
                    "LEFT composite base bolt group has fewer than two distinct feet."
                );

            bolts.Sort(
                delegate(P2 a, P2 b)
                {
                    double dy = a.Y - b.Y;
                    if (Math.Abs(dy) > GeometryTolerance)
                        return dy < 0.0 ? -1 : 1;
                    return a.X.CompareTo(b.X);
                }
            );

            List<List<P2>> rows = new List<List<P2>>();
            for (int i = 0; i < bolts.Count; i++)
            {
                P2 bolt = bolts[i];
                List<P2> row = rows.Count == 0 ? null : rows[rows.Count - 1];
                if (
                    row == null
                    || Math.Abs(bolt.Y - row[0].Y) > MatchTolerance
                )
                {
                    row = new List<P2>();
                    rows.Add(row);
                }
                row.Add(bolt);
            }
            if (rows.Count == 0)
                throw new InvalidOperationException(
                    "LEFT composite base bolt rows could not be resolved."
                );

            CompositeBottomBoltFeet result = new CompositeBottomBoltFeet();
            result.Vertical.Add(new P2(baseLeftX, baseY));
            for (int r = 0; r < rows.Count; r++)
            {
                List<P2> row = rows[r];
                row.Sort(delegate(P2 a, P2 b) { return a.X.CompareTo(b.X); });
                P2 rightmost = row[row.Count - 1];
                if (rightmost.Y <= baseY + GeometryTolerance)
                    throw new InvalidOperationException(
                        "LEFT composite base bolt row is not above the base edge."
                    );
                result.Vertical.Add(new P2(rightmost.X, rightmost.Y));
            }

            List<P2> furthestRow = rows[rows.Count - 1];
            P2 furthestLeft = furthestRow[0];
            P2 furthestRight = furthestRow[furthestRow.Count - 1];
            if (
                furthestRight.X - furthestLeft.X <= GeometryTolerance
                || furthestLeft.X <= baseLeftX + GeometryTolerance
                || furthestRight.X >= baseRightX - GeometryTolerance
            )
                throw new InvalidOperationException(
                    "LEFT composite furthest base bolt row is incomplete or outside the base edges."
                );

            result.Width.Add(new P2(baseLeftX, baseY));
            result.Width.Add(new P2(furthestLeft.X, furthestLeft.Y));
            result.Width.Add(new P2(furthestRight.X, furthestRight.Y));
            result.Width.Add(new P2(baseRightX, baseY));
            return result;
        }

        private static double ResolveCompositeBottomLeftLine(
            double baseLeftX,
            double baseRightX,
            double tierBase,
            double capturedRightLine
        )
        {
            double gap = tierBase;
            if (
                IsFinite(capturedRightLine)
                && capturedRightLine > baseRightX + GeometryTolerance
            )
                gap = capturedRightLine - baseRightX;
            if (!IsFinite(gap) || gap <= GeometryTolerance)
                throw new InvalidOperationException(
                    "LEFT composite base bolt tier gap is invalid."
                );
            return baseLeftX - gap;
        }

        private static ExistingDimension FindCapturedBottomSource(
            Context context,
            int viewIdentifier,
            PHU_InzaiCompositeViewGeometry geometry,
            bool vertical
        )
        {
            ExistingDimension best = null;
            double bestGap = Double.PositiveInfinity;
            for (int i = 0; context != null && i < context.ShapeDimensions.Count; i++)
            {
                CapturedShapeDimension captured = context.ShapeDimensions[i];
                ExistingDimension dimension = captured == null ? null : captured.Dimension;
                if (
                    captured == null
                    || captured.View == null
                    || captured.View.Identifier != viewIdentifier
                    || dimension == null
                    || dimension.Direction == null
                    || !IsFinite(dimension.LineCoordinate)
                )
                    continue;

                double gap;
                if (vertical)
                {
                    if (
                        dimension.Direction.X < DirectionCosineTolerance
                        || dimension.LineCoordinate <= geometry.BaseRightX + GeometryTolerance
                    )
                        continue;
                    gap = dimension.LineCoordinate - geometry.BaseRightX;
                }
                else
                {
                    if (
                        dimension.Direction.Y > -DirectionCosineTolerance
                        || dimension.LineCoordinate >= geometry.BaseY - GeometryTolerance
                    )
                        continue;
                    gap = geometry.BaseY - dimension.LineCoordinate;
                }
                if (gap < bestGap)
                {
                    best = dimension;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static double ResolveCompositeHorizontalGridLine(
            ViewGeometry view,
            PHU_InzaiCompositeViewGeometry geometry,
            double tierStep
        )
        {
            if (geometry.Type2TerminalFamily
                && IsFinite(geometry.Type2TerminalAxisLine))
                return geometry.Type2TerminalAxisLine;

            double line = Math.Max(
                view.RefTopY + tierStep,
                geometry.HorizontalClearanceY + (2.0 * tierStep)
            );
            for (int i = 0; i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (
                    dimension.Direction == null
                    || dimension.Direction.Y < DirectionCosineTolerance
                    || !IsFinite(dimension.LineCoordinate)
                )
                    continue;
                line = Math.Max(line, dimension.LineCoordinate + tierStep);
            }
            return line;
        }

        private static P2 ResolveCompositeHorizontalGridDirection(
            PHU_InzaiCompositeViewGeometry geometry,
            double line,
            double upperGrid)
        {
            if (geometry != null
                && geometry.Type2TerminalFamily
                && IsFinite(line)
                && IsFinite(upperGrid)
                && line < upperGrid - MatchTolerance)
                return new P2(0.0, -1.0);
            return new P2(0.0, 1.0);
        }

        private static void DeleteCapturedShapeDimensions(
            List<CapturedShapeDimension> captured,
            List<CapturedShapeDimension> deleted
        )
        {
            if (captured == null || captured.Count == 0)
                throw new InvalidOperationException(
                    "No exact Shape DIM snapshot is available for replacement."
                );
            for (int i = 0; i < captured.Count; i++)
            {
                CapturedShapeDimension item = captured[i];
                if (
                    item == null
                    || item.View == null
                    || item.Dimension == null
                    || item.Dimension.Dimension == null
                )
                    throw new InvalidOperationException(
                        "An exact captured Shape DIM could not be deleted safely."
                    );
                RefreshExistingDimensionSnapshot(item.Dimension);
                if (!item.Dimension.Dimension.Delete())
                    throw new InvalidOperationException(
                        "An exact captured Shape DIM could not be deleted safely."
                    );
                deleted.Add(item);
            }
        }

        private static void RefreshExistingDimensionSnapshot(ExistingDimension snapshot)
        {
            if (snapshot == null || snapshot.Dimension == null)
                throw new InvalidOperationException(
                    "A captured Shape DIM is unavailable for final-state snapshot."
                );
            try
            {
                snapshot.Dimension.Select();
            }
            catch { }

            List<P2> points = ReadDimensionPoints(snapshot.Dimension);
            P2 direction = ReadDirection(snapshot.Dimension);
            double distance = ReadDoubleMember(snapshot.Dimension, "Distance");
            if (points.Count < 2 || direction == null || !IsFinite(distance))
                throw new InvalidOperationException(
                    "A captured Shape DIM could not be read immediately before replacement."
                );

            snapshot.Points.Clear();
            snapshot.Points.AddRange(points);
            snapshot.Direction = direction;
            snapshot.Distance = distance;
            try
            {
                snapshot.Attributes = snapshot.Dimension.Attributes;
            }
            catch { }
            snapshot.LineCoordinate =
                Math.Abs(direction.X) >= Math.Abs(direction.Y)
                    ? points[0].X + (direction.X * distance)
                    : points[0].Y + (direction.Y * distance);
        }

        private static void RestoreCapturedShapeDimensions(
            List<CapturedShapeDimension> deleted
        )
        {
            for (int i = 0; deleted != null && i < deleted.Count; i++)
            {
                CapturedShapeDimension item = deleted[i];
                RestoreReplacement(
                    item == null ? null : item.Dimension,
                    item == null || item.View == null ? null : item.View.View
                );
            }
        }

        private static List<DimPlan> BuildInzaiPlans(
            ViewGeometry left,
            ViewGeometry right,
            Context context
        )
        {
            if (left == null || right == null)
                throw new InvalidOperationException(
                    "The captured Inzai views are unavailable after Shape."
                );
            if (left.HorizontalLevels.Count != 2)
                throw new InvalidOperationException(
                    "LEFT view must resolve exactly two unique floor Grid levels."
                );

            InzaiVerticalStationPlan verticalGeometry = BuildInzaiVerticalStationPlan(
                left.HorizontalLevels[0].Coordinate,
                left.HorizontalLevels[1].Coordinate,
                left.MainBounds.MinY,
                left.MainBounds.MaxY
            );

            ExistingDimension verticalTotal = FindReplaceableLeftVerticalTotal(left);
            if (verticalTotal == null)
                throw new InvalidOperationException(
                    "No exact LEFT Shape vertical total was found; protected DIMs were not changed."
                );
            context.ReplaceableLeftVerticalTotal = verticalTotal;

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

            double columnLineX;
            double floorLineX;
            if (
                !PHU_ColumnDimensionTierContext.TryResolveGridVerticalLines(
                    left.Snapshot.Identifier,
                    -1,
                    verticalTotal.LineCoordinate,
                    tierStep,
                    out columnLineX,
                    out floorLineX
                )
            )
                throw new InvalidOperationException(
                    "Column Grid could not resolve the shared Shape tier sequence."
                );
            double anchorX = left.MainBounds.MinX;

            List<DimPlan> plans = new List<DimPlan>();
            DimPlan column = NewPlan(
                "LEFT COLUMN CHAIN",
                left,
                new P2(-1.0, 0.0),
                anchorX - columnLineX,
                verticalTotal.Attributes,
                true
            );
            AddSortedVerticalFeet(
                column,
                anchorX,
                verticalGeometry.ColumnStations
            );
            plans.Add(column);

            DimPlan floors = NewPlan(
                "LEFT FLOOR CHAIN",
                left,
                new P2(-1.0, 0.0),
                anchorX - floorLineX,
                verticalTotal.Attributes,
                true
            );
            AddSortedVerticalFeet(
                floors,
                anchorX,
                verticalGeometry.FloorStations
            );
            plans.Add(floors);

            plans.Add(BuildHorizontalGridPlan(left, verticalTotal.Attributes, true, tierStep));
            plans.Add(
                BuildHorizontalGridPlan(right, FindAnyDimensionAttributes(right), false, tierStep)
            );
            return plans;
        }

        private static InzaiVerticalStationPlan BuildInzaiVerticalStationPlan(
            double firstGrid,
            double secondGrid,
            double columnBottom,
            double columnTop
        )
        {
            if (
                !IsFinite(firstGrid)
                || !IsFinite(secondGrid)
                || !IsFinite(columnBottom)
                || !IsFinite(columnTop)
                || columnTop - columnBottom <= MatchTolerance
            )
                throw new InvalidOperationException(
                    "The Inzai Column floor coordinates are not valid."
                );

            double lower = Math.Min(firstGrid, secondGrid);
            double upper = Math.Max(firstGrid, secondGrid);
            if (upper - lower <= MatchTolerance)
                throw new InvalidOperationException(
                    "The two Inzai floor Grid levels are not distinct."
                );

            bool lowerInside =
                lower > columnBottom + MatchTolerance
                && lower < columnTop - MatchTolerance;
            bool upperInside =
                upper > columnBottom + MatchTolerance
                && upper < columnTop - MatchTolerance;
            bool lowerBelow = lower < columnBottom - MatchTolerance;
            bool upperAbove = upper > columnTop + MatchTolerance;

            InzaiVerticalStationPlan result = new InzaiVerticalStationPlan();
            if (lowerInside != upperInside)
            {
                double outside = lowerInside ? upper : lower;
                bool outsideBelow = outside < columnBottom - MatchTolerance;
                bool outsideAbove = outside > columnTop + MatchTolerance;
                if (!outsideBelow && !outsideAbove)
                    throw new InvalidOperationException(
                        "The exterior Inzai floor Grid is not outside the column span."
                    );

                result.Topology = InzaiVerticalTopology.OneInsideOneOutside;
                result.ColumnStations = BuildSortedStations(
                    outside,
                    columnBottom,
                    columnTop
                );
                result.FloorStations = BuildSortedStations(
                    lower,
                    upper,
                    outsideAbove ? columnBottom : columnTop
                );
            }
            else if (lowerBelow && upperAbove)
            {
                result.Topology = InzaiVerticalTopology.ColumnBetweenGrids;
                result.ColumnStations = BuildSortedStations(
                    lower,
                    columnBottom,
                    columnTop,
                    upper
                );
                result.FloorStations = BuildSortedStations(lower, upper);
            }
            else
            {
                throw new InvalidOperationException(
                    "LEFT floor topology must contain one inside/one exterior level "
                        + "or two exterior levels bracketing the column."
                );
            }

            int expectedColumnCount =
                result.Topology == InzaiVerticalTopology.ColumnBetweenGrids ? 4 : 3;
            int expectedFloorCount =
                result.Topology == InzaiVerticalTopology.ColumnBetweenGrids ? 2 : 3;
            if (
                result.ColumnStations == null
                || result.ColumnStations.Length != expectedColumnCount
                || result.FloorStations == null
                || result.FloorStations.Length != expectedFloorCount
            )
                throw new InvalidOperationException(
                    "The Inzai floor topology did not retain its semantic DIM feet."
                );
            return result;
        }

        private static double[] BuildSortedStations(params double[] values)
        {
            List<double> result = new List<double>();
            for (int i = 0; values != null && i < values.Length; i++)
                AddUniqueCoordinate(result, values[i]);
            result.Sort();
            return result.ToArray();
        }

        private static DimPlan BuildHorizontalGridPlan(
            ViewGeometry view,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes fallbackAttributes,
            bool isLeft,
            double tierStep
        )
        {
            if (view.VerticalGrid == null)
                throw new InvalidOperationException(
                    "A target view has no overlapping vertical Grid axis."
                );

            double topLine = view.RefTopY + tierStep;
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes = fallbackAttributes;
            for (int i = 0; i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (dimension.Direction == null || dimension.Direction.Y < DirectionCosineTolerance)
                    continue;
                topLine = Math.Max(topLine, dimension.LineCoordinate + tierStep);
                if (dimension.Attributes != null)
                    attributes = dimension.Attributes;
            }

            bool coincident =
                Math.Abs(view.VerticalGrid.Coordinate - view.RefX) <= MatchTolerance;
            List<P2> feet = new List<P2>();
            string name;
            if (coincident)
            {
                P2 leftEdge = SelectTopExtremeVertex(view, false);
                P2 rightEdge = SelectTopExtremeVertex(view, true);
                if (
                    view.RefX <= leftEdge.X + MatchTolerance
                    || view.RefX >= rightEdge.X - MatchTolerance
                )
                    throw new InvalidOperationException(
                        "A coincident Inzai vertical Grid is not between the MainPart edges."
                    );

                name = isLeft ? "LEFT EDGE-REF-EDGE" : "RIGHT EDGE-REF-EDGE";
                feet.Add(leftEdge);
                feet.Add(new P2(view.RefX, view.RefTopY));
                feet.Add(rightEdge);
            }
            else
            {
                name = isLeft ? "LEFT REF-GRID" : "RIGHT REF-GRID";
                // Preserve the approved legacy semantic order on either side.
                feet.Add(new P2(view.RefX, view.RefTopY));
                feet.Add(new P2(view.VerticalGrid.Coordinate, view.RefTopY));
            }

            return NewPlanAtLine(
                name,
                view,
                new P2(0.0, 1.0),
                topLine,
                attributes,
                coincident,
                feet
            );
        }

        private static DimPlan NewPlan(
            string name,
            ViewGeometry view,
            P2 direction,
            double distance,
            TSD.StraightDimensionSet.StraightDimensionSetAttributes attributes,
            bool disableCombine
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.Direction = direction;
            plan.Distance = distance;
            plan.Attributes = attributes;
            plan.DisableCombine = disableCombine;
            return plan;
        }

        private static void AddSortedVerticalFeet(DimPlan plan, double x, params double[] values)
        {
            List<double> sorted = new List<double>();
            for (int i = 0; values != null && i < values.Length; i++)
                AddUniqueCoordinate(sorted, values[i]);
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++)
                plan.Points.Add(new P2(x, sorted[i]));
        }

        private static ViewGeometry ReadViewGeometry(
            Context context,
            ViewSnapshot snapshot,
            TSG.Matrix currentToGlobal,
            bool requireFloorLevels,
            bool allowCoincidentVerticalGrid
        )
        {
            if (context == null || snapshot == null || snapshot.View == null)
                return null;

            TSD.View view = snapshot.View;
            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem
            );
            List<P2> reference = ReadReferenceLine(context.MainPart, currentToGlobal, globalToView);
            P2 bottom;
            P2 top;
            FindVerticalReferenceEnds(reference, out bottom, out top);
            if (bottom == null || top == null)
                throw new InvalidOperationException(
                    "A captured view no longer has a vertical MainPart REF."
                );
            if (allowCoincidentVerticalGrid)
                ValidateGeneralVerticalReference(reference, bottom, top);

            ViewGeometry geometry = new ViewGeometry();
            geometry.Snapshot = snapshot;
            geometry.View = view;
            geometry.Scale = view.Attributes == null ? 0.0 : view.Attributes.Scale;
            if (!IsFinite(geometry.Scale) || geometry.Scale <= 0.0)
                throw new InvalidOperationException("A captured view has an invalid scale.");
            geometry.RefX = (bottom.X + top.X) * 0.5;
            geometry.RefBottomY = bottom.Y;
            geometry.RefTopY = top.Y;
            geometry.MainBounds = ReadSolidBounds(
                context.MainPart,
                currentToGlobal,
                globalToView,
                geometry.MainVertices
            );
            if (
                geometry.MainBounds == null
                || !geometry.MainBounds.IsValid
                || geometry.MainBounds.MaxY - geometry.MainBounds.MinY <= MatchTolerance
            )
                throw new InvalidOperationException(
                    "MainPart solid bounds are unavailable in a captured view."
                );

            List<GridAxis> grids = ReadDrawingGridAxes(view);
            geometry.VerticalGrid = allowCoincidentVerticalGrid
                ? ResolveGeneralOverlappingVerticalGrid(geometry, grids)
                : ResolveOverlappingVerticalGrid(geometry, grids);
            if (geometry.VerticalGrid == null)
                throw new InvalidOperationException(
                    allowCoincidentVerticalGrid
                        ? "No unique vertical Grid overlaps the column span."
                        : "No non-coincident vertical Grid overlaps the column span."
                );

            if (requireFloorLevels)
            {
                for (int i = 0; i < grids.Count; i++)
                {
                    GridAxis grid = grids[i];
                    if (
                        grid.IsVertical
                        || grid.SpanMax < geometry.MainBounds.MinX - MatchTolerance
                        || grid.SpanMin > geometry.MainBounds.MaxX + MatchTolerance
                    )
                        continue;
                    AddUniqueHorizontalLevel(geometry.HorizontalLevels, grid);
                }
                geometry.HorizontalLevels.Sort(
                    delegate(GridAxis a, GridAxis b)
                    {
                        return a.Coordinate.CompareTo(b.Coordinate);
                    }
                );
            }

            geometry.Dimensions.AddRange(ReadExistingDimensions(view));
            return geometry;
        }

        private static ExistingDimension FindReplaceableLeftVerticalTotal(ViewGeometry view)
        {
            ExistingDimension best = null;
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
                    || dimension.LineCoordinate >= view.MainBounds.MinX - GeometryTolerance
                )
                    continue;
                double gap = view.MainBounds.MinX - dimension.LineCoordinate;
                if (gap < bestGap)
                {
                    best = dimension;
                    bestGap = gap;
                }
            }
            return best;
        }

        private static ExistingDimension FindReplaceableVerticalTotal(ViewGeometry view)
        {
            ExistingDimension best = null;
            double bestGap = Double.PositiveInfinity;
            bool ambiguous = false;
            for (int i = 0; view != null && i < view.Dimensions.Count; i++)
            {
                ExistingDimension dimension = view.Dimensions[i];
                if (
                    dimension == null
                    || dimension.Points.Count != 2
                    || dimension.Direction == null
                    || Math.Abs(dimension.Direction.X) < DirectionCosineTolerance
                )
                    continue;

                double y0 = Math.Min(dimension.Points[0].Y, dimension.Points[1].Y);
                double y1 = Math.Max(dimension.Points[0].Y, dimension.Points[1].Y);
                if (
                    Math.Abs(y0 - view.MainBounds.MinY) > MatchTolerance
                    || Math.Abs(y1 - view.MainBounds.MaxY) > MatchTolerance
                )
                    continue;

                double gap;
                if (dimension.LineCoordinate < view.MainBounds.MinX - GeometryTolerance)
                {
                    gap = view.MainBounds.MinX - dimension.LineCoordinate;
                }
                else if (dimension.LineCoordinate > view.MainBounds.MaxX + GeometryTolerance)
                {
                    gap = dimension.LineCoordinate - view.MainBounds.MaxX;
                }
                else
                {
                    continue;
                }

                if (gap < bestGap - MatchTolerance)
                {
                    best = dimension;
                    bestGap = gap;
                    ambiguous = false;
                }
                else if (
                    Math.Abs(gap - bestGap) <= MatchTolerance
                    && best != null
                    && best.Dimension != dimension.Dimension
                )
                {
                    ambiguous = true;
                }
            }

            if (ambiguous)
                throw new InvalidOperationException(
                    "More than one Shape vertical total is equally eligible for replacement."
                );
            return best;
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
                if (Math.Abs(item.Direction.X) >= Math.Abs(item.Direction.Y))
                    item.LineCoordinate = item.Points[0].X + (item.Direction.X * item.Distance);
                else
                    item.LineCoordinate = item.Points[0].Y + (item.Direction.Y * item.Distance);
                result.Add(item);
            }
            return result;
        }

        private static void CaptureShapeDimensions(
            ViewSnapshot view,
            List<CapturedShapeDimension> target
        )
        {
            if (view == null || view.View == null || target == null)
                throw new InvalidOperationException(
                    "A captured Shape view is unavailable."
                );

            List<ExistingDimension> dimensions = ReadExistingDimensions(view.View);
            for (int i = 0; i < dimensions.Count; i++)
            {
                ExistingDimension dimension = dimensions[i];
                if (dimension == null || dimension.Dimension == null)
                    continue;
                CapturedShapeDimension captured = new CapturedShapeDimension();
                captured.View = view;
                captured.Dimension = dimension;
                target.Add(captured);
            }
        }

        private static List<GridAxis> ReadDrawingGridAxes(TSD.View view)
        {
            List<GridAxis> result = new List<GridAxis>();
            TSD.DrawingObjectEnumerator objects = view.GetAllObjects(typeof(TSD.GridLine));
            while (objects != null && objects.MoveNext())
            {
                TSD.GridLine line = objects.Current as TSD.GridLine;
                if (line == null || line.StartLabel == null || line.EndLabel == null)
                    continue;
                TSG.Point start = line.StartLabel.GridPoint;
                TSG.Point end = line.EndLabel.GridPoint;
                if (start == null || end == null)
                    continue;
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length <= GeometryTolerance)
                    continue;

                GridAxis axis = new GridAxis();
                axis.IsVertical = Math.Abs(dy / length) >= DirectionCosineTolerance;
                bool horizontal = Math.Abs(dx / length) >= DirectionCosineTolerance;
                if (!axis.IsVertical && !horizontal)
                    continue;
                axis.Coordinate = axis.IsVertical
                    ? (start.X + end.X) * 0.5
                    : (start.Y + end.Y) * 0.5;
                axis.SpanMin = axis.IsVertical
                    ? Math.Min(start.Y, end.Y)
                    : Math.Min(start.X, end.X);
                axis.SpanMax = axis.IsVertical
                    ? Math.Max(start.Y, end.Y)
                    : Math.Max(start.X, end.X);
                axis.Label = ReadGridLabel(line);
                try
                {
                    axis.ModelIdentifier =
                        line.ModelIdentifier == null ? 0 : line.ModelIdentifier.ID;
                }
                catch
                {
                    axis.ModelIdentifier = 0;
                }
                AddUniqueGridAxis(result, axis);
            }
            return result;
        }

        private static GridAxis ResolveOverlappingVerticalGrid(
            ViewGeometry view,
            List<GridAxis> grids
        )
        {
            GridAxis best = null;
            GridAxis coincident = null;
            double bestDistance = Double.PositiveInfinity;
            double minimumOverlap = Math.Max(
                1.0,
                (view.MainBounds.MaxY - view.MainBounds.MinY) * 0.10
            );
            for (int i = 0; grids != null && i < grids.Count; i++)
            {
                GridAxis axis = grids[i];
                if (!axis.IsVertical)
                    continue;
                double overlap =
                    Math.Min(axis.SpanMax, view.MainBounds.MaxY)
                    - Math.Max(axis.SpanMin, view.MainBounds.MinY);
                if (overlap < minimumOverlap)
                    continue;
                double distance = Math.Abs(axis.Coordinate - view.RefX);
                if (distance <= MatchTolerance)
                {
                    if (coincident == null || CompareGridIdentity(axis, coincident) < 0)
                        coincident = axis;
                    continue;
                }
                if (distance < bestDistance)
                {
                    best = axis;
                    bestDistance = distance;
                }
            }
            // Preserve the legacy non-coincident choice whenever one exists.
            // A coincident Grid is a new fallback for edge -> REF -> edge only.
            return best ?? coincident;
        }

        private static GridAxis ResolveGeneralOverlappingVerticalGrid(
            ViewGeometry view,
            List<GridAxis> grids
        )
        {
            List<GridAxis> candidates = new List<GridAxis>();
            List<double> coordinates = new List<double>();
            double minimumOverlap = Math.Max(
                1.0,
                (view.MainBounds.MaxY - view.MainBounds.MinY) * 0.10
            );
            for (int i = 0; grids != null && i < grids.Count; i++)
            {
                GridAxis axis = grids[i];
                if (axis == null || !axis.IsVertical)
                    continue;
                double overlap =
                    Math.Min(axis.SpanMax, view.MainBounds.MaxY)
                    - Math.Max(axis.SpanMin, view.MainBounds.MinY);
                if (overlap < minimumOverlap)
                    continue;
                candidates.Add(axis);
                coordinates.Add(axis.Coordinate);
            }

            double selectedCoordinate = PHU_GeneralColumnGridPlanner.SelectNearestCoordinate(
                view.RefX,
                coordinates,
                MatchTolerance
            );
            GridAxis selected = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                GridAxis candidate = candidates[i];
                if (Math.Abs(candidate.Coordinate - selectedCoordinate) > MatchTolerance)
                    continue;
                if (selected == null || CompareGridIdentity(candidate, selected) < 0)
                    selected = candidate;
            }
            return selected;
        }

        private static int CompareGridIdentity(GridAxis first, GridAxis second)
        {
            int firstId =
                first == null || first.ModelIdentifier <= 0
                    ? Int32.MaxValue
                    : first.ModelIdentifier;
            int secondId =
                second == null || second.ModelIdentifier <= 0
                    ? Int32.MaxValue
                    : second.ModelIdentifier;
            int byId = firstId.CompareTo(secondId);
            if (byId != 0)
                return byId;
            return String.Compare(
                first == null ? String.Empty : first.Label,
                second == null ? String.Empty : second.Label,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static List<ViewSnapshot> FindTwoVerticalMainViews(
            TSD.Drawing drawing,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal
        )
        {
            List<ViewSnapshot> result = new List<ViewSnapshot>();
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
                TSG.Point origin = ClonePoint(view.Origin);
                int identifier = ReadIdentifier(view);
                if (origin == null || identifier == 0)
                    continue;
                ViewSnapshot snapshot = new ViewSnapshot();
                snapshot.Identifier = identifier;
                snapshot.View = view;
                snapshot.OriginalOrigin = origin;
                result.Add(snapshot);
            }
            return result;
        }

        private static ViewSnapshot FindSnapshotById(List<ViewSnapshot> snapshots, int identifier)
        {
            for (int i = 0; snapshots != null && i < snapshots.Count; i++)
            {
                if (snapshots[i] != null && snapshots[i].Identifier == identifier)
                    return snapshots[i];
            }
            return null;
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

        private static void ValidateGeneralVerticalReference(List<P2> points, P2 bottom, P2 top)
        {
            if (
                points == null
                || points.Count < 2
                || bottom == null
                || top == null
                || top.Y - bottom.Y <= MatchTolerance
            )
            {
                throw new InvalidOperationException(
                    "The MainPart REF does not define a valid vertical line in this view."
                );
            }

            double refX = (bottom.X + top.X) * 0.5;
            if (Math.Abs(top.X - bottom.X) > MatchTolerance)
            {
                throw new InvalidOperationException(
                    "The MainPart REF is not vertical in the captured view."
                );
            }
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null || Math.Abs(points[i].X - refX) > MatchTolerance)
                {
                    throw new InvalidOperationException(
                        "The MainPart REF is not one straight vertical line in the captured view."
                    );
                }
            }
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

        private static Bounds2 ReadSolidBounds(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView,
            List<P2> vertices
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
                    P2 start = Transform(edge.StartPoint, currentToGlobal, globalToView);
                    P2 end = Transform(edge.EndPoint, currentToGlobal, globalToView);
                    bounds.Add(start);
                    bounds.Add(end);
                    if (vertices != null)
                    {
                        AddUniquePoint(vertices, start);
                        AddUniquePoint(vertices, end);
                    }
                }
            }
            catch { }
            return bounds;
        }

        private static void ValidatePlans(List<DimPlan> plans, int expectedCount)
        {
            if (plans == null || plans.Count != expectedCount)
                throw new InvalidOperationException(
                    "Inzai Column requires exactly "
                        + expectedCount.ToString(CultureInfo.InvariantCulture)
                        + " DIM plans for this strategy."
                );
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
                    throw new InvalidOperationException("Invalid plan at index " + i + ".");
                for (int p = 1; p < plan.Points.Count; p++)
                {
                    if (Distance(plan.Points[p - 1], plan.Points[p]) <= GeometryTolerance)
                        throw new InvalidOperationException(
                            plan.Name + " has duplicate adjacent feet."
                        );
                }
            }
        }

        private static void ValidateGeneralPlans(List<DimPlan> plans)
        {
            if (plans == null || plans.Count < 4 || plans.Count > 7)
                throw new InvalidOperationException(
                    "General Column Grid requires four to seven non-duplicate DIM plans."
                );
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
                        "Invalid General Column plan at index " + i + "."
                    );

                bool measuresVertical = Math.Abs(plan.Direction.X) >= Math.Abs(plan.Direction.Y);
                double previous = measuresVertical ? plan.Points[0].Y : plan.Points[0].X;
                int order = 0;
                for (int p = 1; p < plan.Points.Count; p++)
                {
                    double current = measuresVertical ? plan.Points[p].Y : plan.Points[p].X;
                    double delta = current - previous;
                    if (Math.Abs(delta) <= GeometryTolerance)
                        throw new InvalidOperationException(
                            plan.Name + " has duplicate measurement feet."
                        );
                    int currentOrder = delta < 0.0 ? -1 : 1;
                    if (order != 0 && order != currentOrder)
                        throw new InvalidOperationException(
                            plan.Name + " feet are not geometrically ordered."
                        );
                    order = currentOrder;
                    previous = current;
                }

                for (int other = 0; other < i; other++)
                {
                    if (PlansShareMeasurementSignature(plan, plans[other]))
                        throw new InvalidOperationException(
                            plan.Name + " duplicates " + plans[other].Name + "."
                        );
                }
            }
        }

        private static bool PlansShareMeasurementSignature(DimPlan first, DimPlan second)
        {
            if (
                first == null
                || second == null
                || first.View == null
                || second.View == null
                || first.View.Snapshot == null
                || second.View.Snapshot == null
                || first.View.Snapshot.Identifier != second.View.Snapshot.Identifier
                || first.Points.Count != second.Points.Count
            )
                return false;
            bool firstVertical = Math.Abs(first.Direction.X) >= Math.Abs(first.Direction.Y);
            bool secondVertical = Math.Abs(second.Direction.X) >= Math.Abs(second.Direction.Y);
            if (firstVertical != secondVertical)
                return false;

            bool direct = true;
            bool reverse = true;
            for (int i = 0; i < first.Points.Count; i++)
            {
                double firstStation = firstVertical ? first.Points[i].Y : first.Points[i].X;
                double directStation = firstVertical ? second.Points[i].Y : second.Points[i].X;
                int reverseIndex = second.Points.Count - 1 - i;
                double reverseStation = firstVertical
                    ? second.Points[reverseIndex].Y
                    : second.Points[reverseIndex].X;
                direct &= Math.Abs(firstStation - directStation) <= MatchTolerance;
                reverse &= Math.Abs(firstStation - reverseStation) <= MatchTolerance;
            }
            return direct || reverse;
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
                return;
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

        private static void RebindCapturedViews(Context context)
        {
            TSD.ContainerView sheet = context.Drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null)
                    continue;
                int identifier = ReadIdentifier(view);
                if (identifier == context.Left.Identifier)
                    context.Left.View = view;
                if (identifier == context.Right.Identifier)
                    context.Right.View = view;
            }
            if (context.Left.View == null || context.Right.View == null)
                throw new InvalidOperationException("A captured view disappeared during Shape.");
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

        private static void AddUniqueHorizontalLevel(List<GridAxis> levels, GridAxis candidate)
        {
            for (int i = 0; i < levels.Count; i++)
            {
                if (Math.Abs(levels[i].Coordinate - candidate.Coordinate) <= MatchTolerance)
                    return;
            }
            levels.Add(candidate);
        }

        private static void AddUniqueGridAxis(List<GridAxis> axes, GridAxis candidate)
        {
            for (int i = 0; i < axes.Count; i++)
            {
                GridAxis old = axes[i];
                bool sameId =
                    old.ModelIdentifier > 0 && old.ModelIdentifier == candidate.ModelIdentifier;
                bool sameGeometry =
                    old.IsVertical == candidate.IsVertical
                    && Math.Abs(old.Coordinate - candidate.Coordinate) <= MatchTolerance
                    && String.Equals(
                        old.Label,
                        candidate.Label,
                        StringComparison.OrdinalIgnoreCase
                    );
                if (sameId || sameGeometry)
                    return;
            }
            axes.Add(candidate);
        }

        private static void AddUniqueCoordinate(List<double> values, double value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - value) <= GeometryTolerance)
                    return;
            }
            values.Add(value);
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

        private static List<P2> ReadDimensionPoints(object dimension)
        {
            List<P2> result = new List<P2>();
            IEnumerable values = GetMember(dimension, "DimensionPoints") as IEnumerable;
            if (values == null)
                return result;
            foreach (object value in values)
            {
                TSG.Point point = value as TSG.Point;
                if (point != null)
                    result.Add(new P2(point.X, point.Y));
            }
            return result;
        }

        private static P2 ReadDirection(object dimension)
        {
            object value =
                GetMember(dimension, "UpDirection") ?? GetMember(dimension, "OffsetDirection");
            TSG.Vector vector = value as TSG.Vector;
            if (vector == null)
                return null;
            double length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
            return length <= GeometryTolerance
                ? null
                : new P2(vector.X / length, vector.Y / length);
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

        private static string ReadGridLabel(TSD.GridLine line)
        {
            try
            {
                if (
                    line.StartLabel != null
                    && !String.IsNullOrWhiteSpace(line.StartLabel.GridLabelText)
                )
                    return line.StartLabel.GridLabelText.Trim();
                if (
                    line.EndLabel != null
                    && !String.IsNullOrWhiteSpace(line.EndLabel.GridLabelText)
                )
                    return line.EndLabel.GridLabelText.Trim();
            }
            catch { }
            return String.Empty;
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                Tekla.Structures.Identifier identifier =
                    GetMember(value, "Identifier") as Tekla.Structures.Identifier;
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
            TSG.Point local = globalToView.Transform(global);
            return new P2(local.X, local.Y);
        }

        private static TSD.StraightDimensionSet.StraightDimensionSetAttributes FindAnyDimensionAttributes(
            ViewGeometry view
        )
        {
            for (int i = 0; view != null && i < view.Dimensions.Count; i++)
            {
                if (view.Dimensions[i].Attributes != null)
                    return view.Dimensions[i].Attributes;
            }
            return null;
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

        private static void RestoreReplacement(ExistingDimension replacement)
        {
            RestoreReplacement(replacement, _context == null ? null : _context.Left.View);
        }

        private static void RestoreReplacement(ExistingDimension replacement, TSD.View view)
        {
            if (
                view == null
                || replacement == null
                || replacement.Points.Count < 2
                || replacement.Direction == null
            )
                return;
            try
            {
                TSD.PointList points = new TSD.PointList();
                for (int i = 0; i < replacement.Points.Count; i++)
                    points.Add(replacement.Points[i].ToPoint());
                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                if (replacement.Attributes == null)
                {
                    handler.CreateDimensionSet(
                        view,
                        points,
                        new TSG.Vector(replacement.Direction.X, replacement.Direction.Y, 0.0),
                        replacement.Distance
                    );
                }
                else
                {
                    handler.CreateDimensionSet(
                        view,
                        points,
                        new TSG.Vector(replacement.Direction.X, replacement.Direction.Y, 0.0),
                        replacement.Distance,
                        replacement.Attributes
                    );
                }
            }
            catch { }
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

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static string Format(double value)
        {
            return Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
        }

        private static void ClearState()
        {
            _enabled = false;
            _requestedAxisCount = 0;
            _context = null;
            LastRunSucceeded = false;
            LastRunMessage = String.Empty;
        }
    }
}
