#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Production Grid/REF dimension integration for the Beam AutoDim flow.
    /// It never resolves a part from selection: ShapeScript supplies the same
    /// authoritative main part and Top/Front views it is already using.
    /// </summary>
    public static class PHU_BeamGridDimensionEngine
    {
        private const double GeometryTolerance = 0.20;
        private const double DirectionCosineTolerance = 0.985;
        private const string Slot09ReferenceConnectorOwnerField = "TTSK_AUTODIM_OWNER";
        private const string Slot09ReferenceConnectorOwnerValue = "SLOT09_TOP_FRONT_REF";

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

        private sealed class GridAxis
        {
            public bool IsCrossAxis;
            public double Coordinate;
            public int ModelIdentifier;
            public string Label;
            public P2 StartPoint;
            public P2 EndPoint;

            // GridPoint is the model-grid coordinate and deliberately remains
            // the resolver source above. OffsetGridPoint is the actual drawn
            // end of the grid line after its extension/offset is applied; only
            // the final TOP/FRONT alignment uses these two points.
            public P2 StartEndpoint;
            public P2 EndEndpoint;
        }

        private sealed class ResolvedAxes
        {
            public GridAxis Left;
            public GridAxis Right;
            public GridAxis SingleEnd;
            public GridAxis InteriorEnd;
            public bool SingleEndIsLeft;
            public GridAxis Cross;
            public bool LeftCoincident;
            public bool RightCoincident;
            public bool CrossCoincident;
        }

        private sealed class ViewGeometry
        {
            public string Key;
            public TSD.View View;
            public P2 RefLeft;
            public P2 RefRight;
            public P2 MainDirection;
            public P2 UpDirection;
            public double RefLength;
            public bool HasMainSpan;
            public double MainSpanStart;
            public double MainSpanEnd;
            public readonly List<GridAxis> GridAxes = new List<GridAxis>();
            public ResolvedAxes Axes;
            public TotalTakeover Takeover;
            public int HorizontalGridPlanCount;
        }

        private sealed class Context
        {
            public TSM.Model Model;
            public TSD.Drawing Drawing;
            public TSM.Part MainPart;
            public readonly Dictionary<string, ViewGeometry> Views = new Dictionary<
                string,
                ViewGeometry
            >(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class TotalTakeover
        {
            public P2 OriginalLeft;
            public P2 OriginalRight;
            public double OriginalHorizontalDistance;
            public double HorizontalLineNormal;
            public int HorizontalTier;
            public double HorizontalTierOffset;
            public double NextHorizontalTierOffset;
            public double OuterHorizontalTierOffset;
            public P2 OriginalTop;
            public P2 OriginalBottom;
            public bool HasExactVerticalEndpoints;
            public double OriginalVerticalDistance;
            public double VerticalLineStation;
            public int VerticalTier;
            public double VerticalTierOffset;
            public double NextVerticalTierOffset;
        }

        private sealed class DimPlan
        {
            public string Name;
            public ViewGeometry View;
            public P2 MeasurementAxis;
            public P2 DimensionDirection;
            public double Distance;
            public readonly List<P2> Points = new List<P2>();
        }

        private static bool _enabled;
        private static int _requestedAxisCount;
        private static int _resolvedAxisCount;
        private static int _skippedZeroPlanCount;
        private static Context _context;

        public enum FrontCrossGridPosition
        {
            Unknown = 0,
            AboveBeam = 1,
            BelowBeam = 2
        }

        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Configure(bool enabled, int requestedAxisCount)
        {
            ClearState();
            _enabled = enabled;
            _requestedAxisCount = requestedAxisCount;

            if (_enabled && (_requestedAxisCount < 1 || _requestedAxisCount > 3))
            {
                _enabled = false;
                LastRunMessage = "Grid DIM disabled: axis count must be 1, 2, or 3.";
            }
        }

        public static void Reset()
        {
            ClearState();
        }

        /// <summary>
        /// Read-only preflight. This must run after ShapeScript has settled its
        /// scale, and before ShapeScript decides whether to replace a total DIM.
        /// </summary>
        public static bool Prepare(
            TSM.Model model,
            TSD.Drawing drawing,
            TSM.Part mainPart,
            TSD.View topView,
            TSD.View frontView
        )
        {
            if (!_enabled)
                return false;

            _context = null;
            _resolvedAxisCount = 0;
            _skippedZeroPlanCount = 0;
            LastRunSucceeded = false;

            try
            {
                if (
                    model == null
                    || drawing == null
                    || mainPart == null
                    || !model.GetConnectionStatus()
                )
                    throw new InvalidOperationException(
                        "Grid preflight lacks the active Model, Drawing, or MainPart."
                    );
                if (topView == null || frontView == null)
                    throw new InvalidOperationException(
                        "Grid preflight requires the Shape Top and Front views."
                    );

                TSM.TransformationPlane currentPlane = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane();
                TSG.Matrix currentToGlobal = currentPlane.TransformationMatrixToGlobal;

                Context context = new Context();
                context.Model = model;
                context.Drawing = drawing;
                context.MainPart = mainPart;
                context.Views.Add(
                    "TOP",
                    ReadViewGeometry(model, mainPart, topView, "TOP", currentToGlobal)
                );
                context.Views.Add(
                    "FRONT",
                    ReadViewGeometry(model, mainPart, frontView, "FRONT", currentToGlobal)
                );

                if (_requestedAxisCount == 1)
                {
                    // MODE 1 ISOLATED TOPOLOGY:
                    // TOP = 1 End Grid (trục đứng), FRONT = 1 Cross Grid (trục ngang).
                    // Partial success: Có trục nào DIM trục đó, thiếu trục một view không làm fail view kia.
                    int topCount = 0;
                    int frontCount = 0;
                    ViewGeometry topGeom;
                    ViewGeometry frontGeom;

                    if (context.Views.TryGetValue("TOP", out topGeom) && topGeom != null)
                    {
                        topCount = CountResolvableAxes(topGeom, 1);
                        topGeom.Axes = ResolveAxesMode1(topGeom);
                    }
                    if (context.Views.TryGetValue("FRONT", out frontGeom) && frontGeom != null)
                    {
                        frontCount = CountResolvableAxes(frontGeom, 1);
                        frontGeom.Axes = ResolveAxesMode1(frontGeom);
                    }

                    _resolvedAxisCount = (topCount > 0 || frontCount > 0) ? 1 : 0;
                }
                else
                {
                    // EXISTING 2/3 AXIS LOGIC — KEEP EXACTLY AS IS
                    _resolvedAxisCount = _requestedAxisCount;
                    foreach (KeyValuePair<string, ViewGeometry> pair in context.Views)
                    {
                        _resolvedAxisCount = Math.Min(
                            _resolvedAxisCount,
                            CountResolvableAxes(pair.Value, _requestedAxisCount)
                        );
                        pair.Value.Axes = ResolveAxes(pair.Value, _requestedAxisCount);
                    }
                }

                _context = context;
                LastRunMessage =
                    "Grid preflight passed: requested "
                    + _requestedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ", resolved "
                    + _resolvedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                _context = null;
                LastRunMessage =
                    "Grid preflight failed: requested "
                    + _requestedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ", resolved "
                    + _resolvedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ". Shape horizontal totals are retained. "
                    + ex.Message;
                return false;
            }
        }

        public static bool IsViewPrepared(TSD.View view)
        {
            return FindPreparedView(view) != null;
        }

        public static bool ShouldTakeOverHorizontalTotal(TSD.View view)
        {
            ViewGeometry geometry = FindPreparedView(view);
            if (geometry == null)
                return false;

            if (_requestedAxisCount == 1)
            {
                // Mode 1:
                // - TOP only participates when one End Grid was resolved.
                // - FRONT always replaces Shape's single horizontal total with
                //   the two required base rows, independent of vertical End Grids.
                bool isTop = String.Equals(geometry.Key, "TOP", StringComparison.OrdinalIgnoreCase);
                bool isFront = String.Equals(
                    geometry.Key,
                    "FRONT",
                    StringComparison.OrdinalIgnoreCase
                );
                return isFront
                    || (
                        isTop
                        && geometry.Axes != null
                        && (geometry.Axes.SingleEnd != null || geometry.Axes.InteriorEnd != null)
                    );
            }

            // Mode 2 & 3: Giữ nguyên 100% logic cũ
            return true;
        }

        public static bool ShouldTakeOverVerticalTotal(TSD.View view)
        {
            if (!_enabled || _requestedAxisCount != 1)
                return false;

            ViewGeometry geometry = FindPreparedView(view);
            if (geometry == null)
                return false;

            // Chỉ FRONT view trong Mode 1 mới takeover Vertical Total
            bool isFront = String.Equals(geometry.Key, "FRONT", StringComparison.OrdinalIgnoreCase);
            if (!isFront || geometry.Axes == null || geometry.Axes.Cross == null)
                return false;

            // Cross Grid phải nằm ABOVE beam
            return IsCrossGridAboveBeam(geometry);
        }

        private static bool IsCrossGridAboveBeam(ViewGeometry geometry)
        {
            if (
                geometry == null
                || geometry.Axes == null
                || geometry.Axes.Cross == null
                || geometry.Takeover == null
                || !geometry.Takeover.HasExactVerticalEndpoints
                || geometry.Takeover.OriginalTop == null
                || geometry.Takeover.OriginalBottom == null
            )
                return false;

            // Chỉ phân loại ABOVE bằng mép dầm thật do Shape handoff.
            // Không bao giờ suy đoán ABOVE từ REF=0 khi chưa có Top/Bottom thật.
            double gridNormal = geometry.Axes.Cross.Coordinate;
            double topNormal = LocalNormal(geometry, geometry.Takeover.OriginalTop);
            double bottomNormal = LocalNormal(geometry, geometry.Takeover.OriginalBottom);
            double beamTop = Math.Max(topNormal, bottomNormal);

            // Grid nằm trong dầm / sát mép trên không được takeover Vertical Total.
            // Giữ DIM tổng của Shape là fail-safe, Grid plan riêng vẫn được zero-DIM guard xử lý.
            return gridNormal > beamTop + GeometryTolerance;
        }

        /// <summary>
        /// Reuses the exact FRONT Cross Grid and beam Top/Bottom supplied by
        /// Shape. No view name, Grid label or fallback coordinate is used.
        /// Unknown is returned for missing data, in/near-beam Grid, wrong mode,
        /// or a failed Grid DIM transaction.
        /// </summary>
        public static FrontCrossGridPosition GetPreparedFrontCrossGridPosition(TSD.View frontView)
        {
            if (!_enabled || _requestedAxisCount != 1 || !LastRunSucceeded)
                return FrontCrossGridPosition.Unknown;

            ViewGeometry geometry = FindPreparedView(frontView);
            if (
                geometry == null
                || !String.Equals(geometry.Key, "FRONT", StringComparison.OrdinalIgnoreCase)
                || geometry.Axes == null
                || geometry.Axes.Cross == null
                || geometry.Takeover == null
                || !geometry.Takeover.HasExactVerticalEndpoints
                || geometry.Takeover.OriginalTop == null
                || geometry.Takeover.OriginalBottom == null
            )
                return FrontCrossGridPosition.Unknown;

            double gridNormal = geometry.Axes.Cross.Coordinate;
            double firstNormal = LocalNormal(geometry, geometry.Takeover.OriginalTop);
            double secondNormal = LocalNormal(geometry, geometry.Takeover.OriginalBottom);
            double beamTop = Math.Max(firstNormal, secondNormal);
            double beamBottom = Math.Min(firstNormal, secondNormal);

            if (gridNormal > beamTop + GeometryTolerance)
                return FrontCrossGridPosition.AboveBeam;
            if (gridNormal < beamBottom - GeometryTolerance)
                return FrontCrossGridPosition.BelowBeam;

            return FrontCrossGridPosition.Unknown;
        }

        public static int GetOutermostHorizontalTier(TSD.View view)
        {
            ViewGeometry geometry = FindPreparedView(view);
            if (!LastRunSucceeded || geometry == null || geometry.Takeover == null)
                return 0;

            return geometry.Takeover.HorizontalTier + Math.Max(0, geometry.HorizontalGridPlanCount);
        }

        /// <summary>
        /// Pure regression for Slot09 mode-1 tier allocation. It proves that
        /// Type1 retains its former reservation count and Type2 inserts one
        /// real Shape tier before Slot04/Grid without hard-coded paper gaps.
        /// </summary>
        public static string AuditSlot09FrontTierReservationRegression()
        {
            int type1NoPlate = CountReservedFrontMode1Tiers(false, false);
            int type1WithPlate = CountReservedFrontMode1Tiers(true, false);
            int type2NoPlate = CountReservedFrontMode1Tiers(false, true);
            int type2WithPlate = CountReservedFrontMode1Tiers(true, true);
            bool pass = type1NoPlate == 0
                && type1WithPlate == 1
                && type2NoPlate == 1
                && type2WithPlate == 2;
            return pass
                ? "SLOT09 FRONT TIER REGRESSION PASS: Type1=0/1 unchanged, Type2=1/2 sequential."
                : "SLOT09 FRONT TIER REGRESSION FAILED: Type1="
                    + type1NoPlate + "/" + type1WithPlate
                    + " Type2=" + type2NoPlate + "/" + type2WithPlate + ".";
        }

        private static int CountReservedFrontMode1Tiers(
            bool reserveSlot04Tier,
            bool reserveBeamType2TransverseTier)
        {
            return (reserveSlot04Tier ? 1 : 0)
                + (reserveBeamType2TransverseTier ? 1 : 0);
        }

        /// <summary>
        /// Receives the exact total-DIM location already calculated by Shape.
        /// The production engine derives outer Grid tiers from those tier values;
        /// it never reuses Slot 08 paper-offset constants.
        /// </summary>
        public static bool ReportShapeHorizontalTotal(
            TSD.View view,
            TSG.Point originalLeft,
            TSG.Point originalRight,
            double originalHorizontalDistance,
            int horizontalTier,
            double horizontalTierOffset,
            double nextHorizontalTierOffset,
            double outerHorizontalTierOffset,
            TSG.Point originalVerticalStart,
            double originalVerticalDistance,
            int verticalTier,
            double verticalTierOffset,
            double nextVerticalTierOffset
        )
        {
            return ReportShapeHorizontalTotal(
                view,
                originalLeft,
                originalRight,
                originalHorizontalDistance,
                horizontalTier,
                horizontalTierOffset,
                nextHorizontalTierOffset,
                outerHorizontalTierOffset,
                originalVerticalStart,
                null,
                originalVerticalDistance,
                verticalTier,
                verticalTierOffset,
                nextVerticalTierOffset
            );
        }

        /// <summary>
        /// Overload nhận đầy đủ cả điểm Top edge và Bottom edge từ Shape handoff.
        /// </summary>
        public static bool ReportShapeHorizontalTotal(
            TSD.View view,
            TSG.Point originalLeft,
            TSG.Point originalRight,
            double originalHorizontalDistance,
            int horizontalTier,
            double horizontalTierOffset,
            double nextHorizontalTierOffset,
            double outerHorizontalTierOffset,
            TSG.Point originalVerticalStart,
            TSG.Point originalVerticalEnd,
            double originalVerticalDistance,
            int verticalTier,
            double verticalTierOffset,
            double nextVerticalTierOffset
        )
        {
            ViewGeometry geometry = FindPreparedView(view);
            if (geometry == null)
                return false;

            if (
                originalLeft == null
                || originalRight == null
                || originalVerticalStart == null
                || !IsFinite(originalHorizontalDistance)
                || originalHorizontalDistance <= GeometryTolerance
                || !IsFinite(originalVerticalDistance)
                || originalVerticalDistance <= GeometryTolerance
                || !IsFinite(horizontalTierOffset)
                || !IsFinite(nextHorizontalTierOffset)
                || !IsFinite(outerHorizontalTierOffset)
                || !IsFinite(verticalTierOffset)
                || !IsFinite(nextVerticalTierOffset)
            )
            {
                geometry.Takeover = null;
                LastRunMessage =
                    "Grid handoff is incomplete. Shape horizontal totals are retained.";
                return false;
            }

            TotalTakeover takeover = new TotalTakeover();
            takeover.OriginalLeft = new P2(originalLeft.X, originalLeft.Y);
            takeover.OriginalRight = new P2(originalRight.X, originalRight.Y);
            takeover.OriginalHorizontalDistance = originalHorizontalDistance;
            takeover.HorizontalLineNormal =
                LocalNormal(geometry, takeover.OriginalLeft) + originalHorizontalDistance;
            takeover.HorizontalTier = horizontalTier;
            takeover.HorizontalTierOffset = horizontalTierOffset;
            takeover.NextHorizontalTierOffset = nextHorizontalTierOffset;
            takeover.OuterHorizontalTierOffset = outerHorizontalTierOffset;

            takeover.OriginalTop = new P2(originalVerticalStart.X, originalVerticalStart.Y);
            takeover.HasExactVerticalEndpoints = originalVerticalEnd != null;
            if (takeover.HasExactVerticalEndpoints)
                takeover.OriginalBottom = new P2(originalVerticalEnd.X, originalVerticalEnd.Y);
            else
                takeover.OriginalBottom = new P2(geometry.RefLeft.X, geometry.RefLeft.Y);

            takeover.OriginalVerticalDistance = originalVerticalDistance;
            takeover.VerticalLineStation =
                LocalStation(geometry, takeover.OriginalTop) - originalVerticalDistance;
            takeover.VerticalTier = verticalTier;
            takeover.VerticalTierOffset = verticalTierOffset;
            takeover.NextVerticalTierOffset = nextVerticalTierOffset;
            geometry.Takeover = takeover;
            return true;
        }

        /// <summary>
        /// Creates all prepared Top/Front Grid dimensions in one transaction.
        /// If creation cannot complete, it deletes its own partial Grid DIMs and
        /// restores the Shape horizontal totals that it took over.
        /// </summary>
        public static bool CreatePreparedDimensions()
        {
            List<TSD.StraightDimensionSet> created = new List<TSD.StraightDimensionSet>();

            try
            {
                if (_context == null)
                    return false;

                EnsureAllTakeoversReported(_context);
                List<DimPlan> plans = BuildPlans(_context);
                ValidatePlans(plans);

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();

                for (int i = 0; i < plans.Count; i++)
                {
                    DimPlan plan = plans[i];
                    TSD.PointList points = new TSD.PointList();
                    for (int p = 0; p < plan.Points.Count; p++)
                        points.Add(plan.Points[p].ToPoint());

                    TSD.StraightDimensionSet dimension = handler.CreateDimensionSet(
                        plan.View.View,
                        points,
                        new TSG.Vector(plan.DimensionDirection.X, plan.DimensionDirection.Y, 0.0),
                        plan.Distance
                    );

                    if (dimension == null)
                        throw new InvalidOperationException(
                            "Tekla did not create " + plan.Name + "."
                        );

                    created.Add(dimension);
                }

                _context.Drawing.CommitChanges();
                LastRunSucceeded = true;
                LastRunMessage =
                    "Grid DIM created: "
                    + created.Count.ToString(CultureInfo.InvariantCulture)
                    + " (requested "
                    + _requestedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ", resolved "
                    + _resolvedAxisCount.ToString(CultureInfo.InvariantCulture)
                    + ", zero plans skipped "
                    + _skippedZeroPlanCount.ToString(CultureInfo.InvariantCulture)
                    + ").";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                bool restored = RestoreShapeHorizontalTotals();
                LastRunSucceeded = false;
                LastRunMessage = restored
                    ? "Grid DIM rolled back; Shape totals restored. " + ex.Message
                    : "Grid DIM rolled back, but Shape total restoration needs manual review. "
                        + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Draws the two sheet-level lines that connect the authoritative main
        /// reference endpoints in TOP to the matching endpoints in FRONT after
        /// a successful Grid=1 Beam DIM run (normal flow or Slot09 Data Center).
        /// The visual attributes are owned by PHU_LineDistance so this flow
        /// cannot drift from the approved Line Distance style.
        /// </summary>
        public static int CreatePreparedTopFrontReferenceConnectors(out string message)
        {
            message = string.Empty;
            TSD.Line leftLine = null;
            TSD.Line rightLine = null;

            try
            {
                if (!LastRunSucceeded || _context == null || _context.Drawing == null)
                {
                    message = "REF connectors require a successful prepared Grid DIM.";
                    return 0;
                }
                if (_requestedAxisCount != 1)
                {
                    message = "REF connectors require Beam Grid=1.";
                    return 0;
                }

                ViewGeometry top;
                ViewGeometry front;
                if (
                    !_context.Views.TryGetValue("TOP", out top)
                    || !_context.Views.TryGetValue("FRONT", out front)
                    || top == null
                    || front == null
                )
                {
                    message = "Prepared TOP/FRONT reference geometry is unavailable.";
                    return 0;
                }

                P2 topLeft = ToSheetPoint(top, top.RefLeft);
                P2 topRight = ToSheetPoint(top, top.RefRight);
                P2 frontLeft = ToSheetPoint(front, front.RefLeft);
                P2 frontRight = ToSheetPoint(front, front.RefRight);
                if (topLeft == null || topRight == null || frontLeft == null || frontRight == null)
                {
                    message = "Cannot transform TOP/FRONT REF endpoints to sheet coordinates.";
                    return 0;
                }

                TSD.ContainerView sheet = _context.Drawing.GetSheet();
                if (sheet == null)
                {
                    message = "Drawing sheet is unavailable for REF connectors.";
                    return 0;
                }

                List<TSD.Line> previousLines = FindExistingSlot09ReferenceConnectors(sheet);

                bool leftCreated =
                    global::TTSK_AutoDim_Plates.PHU_LineDistance.InsertLineWithLineDistanceAttributes(
                        sheet,
                        topLeft.ToPoint(),
                        frontLeft.ToPoint(),
                        out leftLine
                    ) && TagSlot09ReferenceConnector(leftLine);
                bool rightCreated =
                    leftCreated
                    && global::TTSK_AutoDim_Plates.PHU_LineDistance.InsertLineWithLineDistanceAttributes(
                        sheet,
                        topRight.ToPoint(),
                        frontRight.ToPoint(),
                        out rightLine
                    )
                    && TagSlot09ReferenceConnector(rightLine);

                if (!leftCreated || !rightCreated)
                {
                    DeleteReferenceConnector(leftLine);
                    DeleteReferenceConnector(rightLine);
                    message = "Tekla rejected one or both TOP-FRONT REF connectors.";
                    return 0;
                }

                for (int i = 0; i < previousLines.Count; i++)
                    DeleteReferenceConnector(previousLines[i]);

                _context.Drawing.CommitChanges();
                message = "Created LEFT/RIGHT TOP-FRONT REF connectors.";
                return 2;
            }
            catch (Exception ex)
            {
                DeleteReferenceConnector(leftLine);
                DeleteReferenceConnector(rightLine);
                message = "REF connector creation failed: " + ex.Message;
                return 0;
            }
        }

        /// <summary>
        /// Final Beam Grid step: align one corresponding physical GridLine from
        /// TOP to FRONT along sheet X. Only FRONT is moved horizontally. Its Y
        /// is preserved so the Shape final arrange remains TOP above FRONT with
        /// the approved green-box gap. A failed read-back restores its Origin.
        /// </summary>
        public static bool AlignPreparedTopFrontByGrid()
        {
            if (!LastRunSucceeded || _context == null)
                return false;

            // Data Center ACTIVE (Type1/Type2) and Batch Type2 view/Grid
            // layouts are supplied and approved upstream.
            // This guard sits at the mutation boundary so a future caller
            // cannot move FRONT even if it forgets the Shape-level policy.
            if (
                PHU_Slot07_DataCenterBeamType2Context.PreservePreparedDrawingLayout
                || PHU_Slot07_DataCenterContext.PreservePreparedDrawingLayout
            )
            {
                LastRunMessage +=
                    " Grid alignment skipped: Data Center prepared view layout preserved.";
                return true;
            }

            ViewGeometry top;
            ViewGeometry front;
            if (
                !_context.Views.TryGetValue("TOP", out top)
                || !_context.Views.TryGetValue("FRONT", out front)
                || top == null
                || front == null
                || top.View == null
                || front.View == null
            )
            {
                LastRunMessage += " Grid alignment skipped: prepared TOP/FRONT is unavailable.";
                return false;
            }

            GridAxis topAxis;
            GridAxis frontAxis;
            string side;
            if (!TrySelectCorrespondingEndAxes(top, front, out topAxis, out frontAxis, out side))
            {
                LastRunMessage +=
                    " Grid alignment skipped: no corresponding Left/Right GridLine identity was proven.";
                return false;
            }

            TSG.Point originalTopOrigin = ClonePoint(top.View.Origin);
            TSG.Point originalFrontOrigin = ClonePoint(front.View.Origin);
            if (originalTopOrigin == null || originalFrontOrigin == null)
            {
                LastRunMessage += " Grid alignment skipped: view Origin is unavailable.";
                return false;
            }

            try
            {
                P2 topSheet = SelectGridEndpointInSheet(top, topAxis, false);
                P2 frontSheet = SelectGridEndpointInSheet(front, frontAxis, true);
                if (topSheet == null || frontSheet == null)
                    throw new InvalidOperationException(
                        "Grid endpoint could not be converted to sheet coordinates."
                    );

                double deltaX = topSheet.X - frontSheet.X;
                double deltaY = topSheet.Y - frontSheet.Y;
                double sheetTolerance = GetAlignmentSheetTolerance(top, front);

                if (Math.Abs(deltaX) > sheetTolerance)
                {
                    front.View.Origin = new TSG.Point(
                        originalFrontOrigin.X + deltaX,
                        originalFrontOrigin.Y,
                        originalFrontOrigin.Z
                    );
                    if (!front.View.Modify())
                        throw new InvalidOperationException(
                            "Tekla rejected the FRONT Origin change."
                        );
                    _context.Drawing.CommitChanges();
                }

                GridAxis verifiedTopAxis = ReadMatchingGridAxis(top, topAxis);
                GridAxis verifiedFrontAxis = ReadMatchingGridAxis(front, frontAxis);
                P2 verifiedTopSheet = SelectGridEndpointInSheet(top, verifiedTopAxis, false);
                P2 verifiedFrontSheet = SelectGridEndpointInSheet(front, verifiedFrontAxis, true);
                double residualX =
                    verifiedTopSheet == null || verifiedFrontSheet == null
                        ? double.NaN
                        : Math.Abs(verifiedTopSheet.X - verifiedFrontSheet.X);
                TSG.Point verifiedTopOrigin = top.View.Origin;
                TSG.Point verifiedFrontOrigin = front.View.Origin;

                if (
                    verifiedTopOrigin == null
                    || Distance3(verifiedTopOrigin, originalTopOrigin) > sheetTolerance
                    || verifiedFrontOrigin == null
                    || Math.Abs(verifiedFrontOrigin.Y - originalFrontOrigin.Y) > sheetTolerance
                    || Math.Abs(verifiedFrontOrigin.Z - originalFrontOrigin.Z) > sheetTolerance
                    || !IsFinite(residualX)
                    || residualX > sheetTolerance
                )
                {
                    throw new InvalidOperationException(
                        "Grid X alignment or FRONT Y preservation verification failed; residualX="
                            + Format(residualX)
                            + "."
                    );
                }

                LastRunMessage +=
                    " Grid alignment "
                    + side
                    + " applied in sheet CS: deltaX="
                    + Format(deltaX)
                    + ", ignoredDeltaY="
                    + Format(deltaY)
                    + ", residualX="
                    + Format(residualX)
                    + "; FRONT Y preserved.";
                return true;
            }
            catch (Exception ex)
            {
                bool restored = RestoreFrontOrigin(front.View, originalFrontOrigin);
                LastRunMessage += restored
                    ? " Grid alignment failed; FRONT Origin restored. " + ex.Message
                    : " Grid alignment failed and FRONT Origin restoration needs manual review. "
                        + ex.Message;
                return false;
            }
        }

        private static void ClearState()
        {
            _enabled = false;
            _requestedAxisCount = 0;
            _resolvedAxisCount = 0;
            _skippedZeroPlanCount = 0;
            _context = null;
            LastRunSucceeded = false;
            LastRunMessage = String.Empty;
        }

        private static ViewGeometry FindPreparedView(TSD.View view)
        {
            if (_context == null || view == null)
                return null;

            foreach (KeyValuePair<string, ViewGeometry> pair in _context.Views)
            {
                if (Object.ReferenceEquals(pair.Value.View, view))
                    return pair.Value;
            }

            return null;
        }

        private static ViewGeometry ReadViewGeometry(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            string key,
            TSG.Matrix currentToGlobal
        )
        {
            if (!ViewContainsPart(view, mainPart))
                throw new InvalidOperationException(
                    key + " does not contain the authoritative MainPart."
                );

            ViewGeometry result = new ViewGeometry();
            result.Key = key;
            result.View = view;

            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem
            );
            List<P2> reference = ReadReferenceLine(mainPart, false, currentToGlobal, globalToView);
            if (reference.Count < 2)
                throw new InvalidOperationException(
                    key + " has no usable MainPart reference line."
                );

            P2 farA;
            P2 farB;
            FindFarthestPair(reference, out farA, out farB);
            P2 direction = Normalize(Subtract(farB, farA));
            if (direction == null)
                throw new InvalidOperationException(key + " reference line is too short.");

            if (
                direction.X < -GeometryTolerance
                || (Math.Abs(direction.X) <= GeometryTolerance && direction.Y < 0.0)
            )
                direction = Scale(direction, -1.0);
            if (Math.Abs(direction.X) < DirectionCosineTolerance)
                throw new InvalidOperationException(
                    key + " reference line is not a Beam horizontal topology."
                );

            P2 up = Normalize(new P2(-direction.Y, direction.X));
            if (up.Y < 0.0)
                up = Scale(up, -1.0);

            result.MainDirection = direction;
            result.UpDirection = up;
            result.RefLeft = ExtremePoint(reference, direction, false);
            result.RefRight = ExtremePoint(reference, direction, true);
            result.RefLength = Dot(Subtract(result.RefRight, result.RefLeft), direction);

            if (result.RefLength <= GeometryTolerance)
                throw new InvalidOperationException(key + " reference line is invalid.");

            if (String.Equals(key, "TOP", StringComparison.OrdinalIgnoreCase))
            {
                // Keep the no-cut line above as the approved REF span. The cut
                // reference line is the exact physical MainPart span and is used
                // only to classify a TOP Grid that falls inside the main beam.
                List<P2> mainSpan = ReadReferenceLine(
                    mainPart,
                    true,
                    currentToGlobal,
                    globalToView
                );
                if (mainSpan.Count >= 2)
                {
                    P2 spanStart = ExtremePoint(mainSpan, direction, false);
                    P2 spanEnd = ExtremePoint(mainSpan, direction, true);
                    double startStation = Dot(Subtract(spanStart, result.RefLeft), direction);
                    double endStation = Dot(Subtract(spanEnd, result.RefLeft), direction);
                    if (
                        IsFinite(startStation)
                        && IsFinite(endStation)
                        && endStation - startStation > GeometryTolerance
                    )
                    {
                        result.HasMainSpan = true;
                        result.MainSpanStart = startStation;
                        result.MainSpanEnd = endStation;
                    }
                }
            }

            ReadDrawingGridAxes(view, result);
            return result;
        }

        private static ResolvedAxes ResolveAxes(ViewGeometry view, int requestedAxisCount)
        {
            if (requestedAxisCount == 1)
                return ResolveAxesMode1(view);

            GridAxis left = null;
            GridAxis right = null;
            GridAxis leftCoincident = null;
            GridAxis rightCoincident = null;
            GridAxis crossCoincident = null;
            GridAxis crossAbove = null;
            GridAxis crossBelow = null;

            for (int i = 0; i < view.GridAxes.Count; i++)
            {
                GridAxis axis = view.GridAxes[i];
                if (axis.IsCrossAxis)
                {
                    if (Math.Abs(axis.Coordinate) <= GeometryTolerance)
                    {
                        if (crossCoincident == null)
                            crossCoincident = axis;
                    }
                    else if (
                        axis.Coordinate > GeometryTolerance
                        && (crossAbove == null || axis.Coordinate < crossAbove.Coordinate)
                    )
                        crossAbove = axis;
                    else if (
                        axis.Coordinate < -GeometryTolerance
                        && (crossBelow == null || axis.Coordinate > crossBelow.Coordinate)
                    )
                        crossBelow = axis;
                    continue;
                }

                if (Math.Abs(axis.Coordinate) <= GeometryTolerance)
                {
                    if (leftCoincident == null)
                        leftCoincident = axis;
                }
                else if (Math.Abs(axis.Coordinate - view.RefLength) <= GeometryTolerance)
                {
                    if (rightCoincident == null)
                        rightCoincident = axis;
                }
                else if (
                    axis.Coordinate < -GeometryTolerance
                    && (left == null || axis.Coordinate > left.Coordinate)
                )
                    left = axis;
                else if (
                    axis.Coordinate > view.RefLength + GeometryTolerance
                    && (right == null || axis.Coordinate < right.Coordinate)
                )
                    right = axis;
            }

            ResolvedAxes resolved = new ResolvedAxes();
            if (leftCoincident != null)
            {
                leftCoincident.Coordinate = 0.0;
                left = leftCoincident;
                resolved.LeftCoincident = true;
            }
            if (rightCoincident != null)
            {
                rightCoincident.Coordinate = view.RefLength;
                right = rightCoincident;
                resolved.RightCoincident = true;
            }
            if (crossCoincident != null)
            {
                crossCoincident.Coordinate = 0.0;
                resolved.CrossCoincident = true;
            }

            if (requestedAxisCount == 2)
            {
                if (left == null && right == null)
                    throw new InvalidOperationException(view.Key + " needs one resolved End Grid.");
                if (left == null)
                {
                    resolved.SingleEnd = right;
                    resolved.SingleEndIsLeft = false;
                }
                else if (right == null)
                {
                    resolved.SingleEnd = left;
                    resolved.SingleEndIsLeft = true;
                }
                else
                {
                    double leftDistance = -left.Coordinate;
                    double rightDistance = right.Coordinate - view.RefLength;
                    if (Math.Abs(leftDistance - rightDistance) <= GeometryTolerance)
                    {
                        throw new InvalidOperationException(
                            view.Key
                                + " has equal nearest End Grids; no deterministic production tie rule exists."
                        );
                    }

                    resolved.SingleEndIsLeft = leftDistance < rightDistance;
                    resolved.SingleEnd = resolved.SingleEndIsLeft ? left : right;
                }

                resolved.LeftCoincident = resolved.SingleEndIsLeft && resolved.LeftCoincident;
                resolved.RightCoincident = !resolved.SingleEndIsLeft && resolved.RightCoincident;

                resolved.Cross = ResolveNearestCrossAxis(
                    view,
                    crossCoincident,
                    crossAbove,
                    crossBelow
                );

                return resolved;
            }

            if (left == null || right == null)
                throw new InvalidOperationException(
                    view.Key + " needs both resolved Left and Right End Grids."
                );

            resolved.Left = left;
            resolved.Right = right;

            if (requestedAxisCount == 3)
                resolved.Cross = ResolveNearestCrossAxis(
                    view,
                    crossCoincident,
                    crossAbove,
                    crossBelow
                );

            return resolved;
        }

        private static ResolvedAxes ResolveAxesMode1(ViewGeometry view)
        {
            ResolvedAxes resolved = new ResolvedAxes();
            if (view == null)
                return resolved;

            bool isTop = String.Equals(view.Key, "TOP", StringComparison.OrdinalIgnoreCase);
            if (isTop)
            {
                // TOP VIEW: 1 End Grid (trục đứng)
                resolved.InteriorEnd = ResolveUniqueInteriorEndAxis(view);
                GridAxis left = null;
                GridAxis right = null;
                GridAxis leftCoincident = null;
                GridAxis rightCoincident = null;

                for (int i = 0; i < view.GridAxes.Count; i++)
                {
                    GridAxis axis = view.GridAxes[i];
                    if (axis == null || axis.IsCrossAxis)
                        continue;

                    if (Math.Abs(axis.Coordinate) <= GeometryTolerance)
                    {
                        if (leftCoincident == null)
                            leftCoincident = axis;
                    }
                    else if (Math.Abs(axis.Coordinate - view.RefLength) <= GeometryTolerance)
                    {
                        if (rightCoincident == null)
                            rightCoincident = axis;
                    }
                    else if (
                        axis.Coordinate < -GeometryTolerance
                        && (left == null || axis.Coordinate > left.Coordinate)
                    )
                    {
                        left = axis;
                    }
                    else if (
                        axis.Coordinate > view.RefLength + GeometryTolerance
                        && (right == null || axis.Coordinate < right.Coordinate)
                    )
                    {
                        right = axis;
                    }
                }

                if (leftCoincident != null)
                {
                    leftCoincident.Coordinate = 0.0;
                    left = leftCoincident;
                    resolved.LeftCoincident = true;
                }
                if (rightCoincident != null)
                {
                    rightCoincident.Coordinate = view.RefLength;
                    right = rightCoincident;
                    resolved.RightCoincident = true;
                }

                if (left == null && right == null)
                {
                    // Partial success: TOP không có End Grid -> không throw, SingleEnd = null
                    return resolved;
                }

                if (left == null)
                {
                    resolved.SingleEnd = right;
                    resolved.SingleEndIsLeft = false;
                }
                else if (right == null)
                {
                    resolved.SingleEnd = left;
                    resolved.SingleEndIsLeft = true;
                }
                else
                {
                    double leftDistance = -left.Coordinate;
                    double rightDistance = right.Coordinate - view.RefLength;
                    if (Math.Abs(leftDistance - rightDistance) <= GeometryTolerance)
                    {
                        resolved.SingleEndIsLeft = true;
                        resolved.SingleEnd = left;
                    }
                    else
                    {
                        resolved.SingleEndIsLeft = leftDistance < rightDistance;
                        resolved.SingleEnd = resolved.SingleEndIsLeft ? left : right;
                    }
                }

                resolved.LeftCoincident = resolved.SingleEndIsLeft && resolved.LeftCoincident;
                resolved.RightCoincident = !resolved.SingleEndIsLeft && resolved.RightCoincident;
                return resolved;
            }
            else
            {
                // FRONT VIEW: 1 Cross Grid (trục ngang)
                GridAxis crossCoincident = null;
                GridAxis crossAbove = null;
                GridAxis crossBelow = null;

                for (int i = 0; i < view.GridAxes.Count; i++)
                {
                    GridAxis axis = view.GridAxes[i];
                    if (axis == null || !axis.IsCrossAxis)
                        continue;

                    if (Math.Abs(axis.Coordinate) <= GeometryTolerance)
                    {
                        if (crossCoincident == null)
                            crossCoincident = axis;
                    }
                    else if (
                        axis.Coordinate > GeometryTolerance
                        && (crossAbove == null || axis.Coordinate < crossAbove.Coordinate)
                    )
                    {
                        crossAbove = axis;
                    }
                    else if (
                        axis.Coordinate < -GeometryTolerance
                        && (crossBelow == null || axis.Coordinate > crossBelow.Coordinate)
                    )
                    {
                        crossBelow = axis;
                    }
                }

                if (crossCoincident != null)
                {
                    crossCoincident.Coordinate = 0.0;
                    resolved.CrossCoincident = true;
                    resolved.Cross = crossCoincident;
                    return resolved;
                }

                if (crossAbove == null && crossBelow == null)
                {
                    // Partial success: FRONT không có Cross Grid -> không throw, Cross = null
                    return resolved;
                }

                if (crossAbove == null)
                    resolved.Cross = crossBelow;
                else if (crossBelow == null)
                    resolved.Cross = crossAbove;
                else if (
                    Math.Abs(crossAbove.Coordinate + crossBelow.Coordinate) <= GeometryTolerance
                )
                    resolved.Cross = crossAbove;
                else
                    resolved.Cross =
                        crossAbove.Coordinate < Math.Abs(crossBelow.Coordinate)
                            ? crossAbove
                            : crossBelow;

                return resolved;
            }
        }

        private static GridAxis ResolveUniqueInteriorEndAxis(ViewGeometry view)
        {
            if (view == null || !view.HasMainSpan)
                return null;

            GridAxis interior = null;
            for (int i = 0; i < view.GridAxes.Count; i++)
            {
                GridAxis axis = view.GridAxes[i];
                if (
                    axis == null
                    || axis.IsCrossAxis
                    || axis.Coordinate <= view.MainSpanStart + GeometryTolerance
                    || axis.Coordinate >= view.MainSpanEnd - GeometryTolerance
                )
                {
                    continue;
                }

                // Grid=1 has one semantic TOP axis. More than one strict
                // interior candidate is ambiguous, so do not guess.
                if (interior != null)
                    return null;
                interior = axis;
            }

            return interior;
        }

        private static GridAxis ResolveNearestCrossAxis(
            ViewGeometry view,
            GridAxis crossCoincident,
            GridAxis crossAbove,
            GridAxis crossBelow
        )
        {
            if (crossCoincident != null)
                return crossCoincident;
            if (crossAbove == null && crossBelow == null)
                throw new InvalidOperationException(view.Key + " needs one resolved Cross Grid.");
            if (crossAbove == null)
                return crossBelow;
            if (crossBelow == null)
                return crossAbove;
            if (Math.Abs(crossAbove.Coordinate + crossBelow.Coordinate) <= GeometryTolerance)
                throw new InvalidOperationException(
                    view.Key
                        + " has equal Cross Grid distances; no deterministic production tie rule exists."
                );
            return crossAbove.Coordinate < Math.Abs(crossBelow.Coordinate)
                ? crossAbove
                : crossBelow;
        }

        private static int CountResolvableAxes(ViewGeometry view, int requestedAxisCount)
        {
            if (view == null)
                return 0;

            if (requestedAxisCount == 1)
            {
                bool isTop = String.Equals(view.Key, "TOP", StringComparison.OrdinalIgnoreCase);
                if (isTop)
                {
                    if (ResolveUniqueInteriorEndAxis(view) != null)
                        return 1;

                    for (int i = 0; i < view.GridAxes.Count; i++)
                    {
                        GridAxis axis = view.GridAxes[i];
                        if (axis != null && !axis.IsCrossAxis)
                        {
                            if (
                                axis.Coordinate <= GeometryTolerance
                                || axis.Coordinate >= view.RefLength - GeometryTolerance
                            )
                                return 1;
                        }
                    }
                    return 0;
                }
                else
                {
                    for (int i = 0; i < view.GridAxes.Count; i++)
                    {
                        GridAxis axis = view.GridAxes[i];
                        if (axis != null && axis.IsCrossAxis)
                            return 1;
                    }
                    return 0;
                }
            }

            // EXISTING 2/3 AXIS LOGIC — KEEP EXACTLY AS IS
            bool hasLeftEnd = false;
            bool hasRightEnd = false;
            bool hasCross = false;
            for (int i = 0; i < view.GridAxes.Count; i++)
            {
                GridAxis axis = view.GridAxes[i];
                if (axis == null)
                    continue;

                if (axis.IsCrossAxis)
                {
                    hasCross = true;
                    continue;
                }

                if (axis.Coordinate <= GeometryTolerance)
                    hasLeftEnd = true;
                else if (axis.Coordinate >= view.RefLength - GeometryTolerance)
                    hasRightEnd = true;
            }

            if (requestedAxisCount == 2)
                return (hasLeftEnd || hasRightEnd ? 1 : 0) + (hasCross ? 1 : 0);
            return (hasLeftEnd ? 1 : 0) + (hasRightEnd ? 1 : 0) + (hasCross ? 1 : 0);
        }

        private static List<DimPlan> BuildPlans(Context context)
        {
            List<DimPlan> plans = new List<DimPlan>();
            _skippedZeroPlanCount = 0;

            if (_requestedAxisCount == 1)
            {
                ViewGeometry top;
                ViewGeometry front;
                if (context.Views.TryGetValue("TOP", out top) && top != null)
                    BuildViewPlansMode1(top, true, plans);
                if (context.Views.TryGetValue("FRONT", out front) && front != null)
                    BuildViewPlansMode1(front, false, plans);
                return plans;
            }

            // Mode 2 & 3: Legacy behavior unchanged
            BuildViewPlans(context.Views["TOP"], true, plans);
            BuildViewPlans(context.Views["FRONT"], false, plans);
            return plans;
        }

        private static void BuildViewPlansMode1(ViewGeometry view, bool isTop, List<DimPlan> plans)
        {
            TotalTakeover takeover = view.Takeover;
            view.HorizontalGridPlanCount = 0;

            if (isTop)
            {
                // TOP VIEW: 1 End Grid (trục đứng)
                if (view.Axes == null || takeover == null)
                    return;

                double g1Line = takeover.HorizontalLineNormal;
                double g2Line =
                    g1Line + (takeover.NextHorizontalTierOffset - takeover.HorizontalTierOffset);

                // G1: REF -> EDGE -> EDGE -> REF
                AddHorizontalPlanMode1(
                    plans,
                    view,
                    view.Key + " G1 REF-EDGE-EDGE-REF",
                    g1Line,
                    view.RefLeft,
                    takeover.OriginalLeft,
                    takeover.OriginalRight,
                    view.RefRight
                );

                if (view.Axes.InteriorEnd != null)
                {
                    // TOP CENTER uses the approved three-tier order:
                    // inner  G1 = REF-EDGE-EDGE-REF
                    // middle G2 = REF-REF
                    // outer  G3 = REF-GRID-REF
                    // The working left/right branch below remains unchanged.
                    AddHorizontalPlanMode1(
                        plans,
                        view,
                        view.Key + " G2 REF-REF",
                        g2Line,
                        view.RefLeft,
                        view.RefRight
                    );

                    double g3Line =
                        g1Line
                        + (takeover.OuterHorizontalTierOffset - takeover.HorizontalTierOffset);
                    // Start only the center Grid extension at the inner G1
                    // level. This matches the approved TOP drawing and avoids
                    // dragging that extension through the beam/detail area.
                    AddHorizontalPlanMode1(
                        plans,
                        view,
                        view.Key + " G3 REF-GRID-REF",
                        g3Line,
                        view.RefLeft,
                        FromLocal(view, view.Axes.InteriorEnd.Coordinate, g1Line),
                        view.RefRight
                    );
                    view.HorizontalGridPlanCount += 2;
                    return;
                }

                if (view.Axes.SingleEnd == null)
                    return;

                // G2: GRID -> REF -> REF hoặc REF -> REF -> GRID
                AddSingleEndBaseHorizontalPlanMode1(view, g2Line, plans);
            }
            else
            {
                // FRONT VIEW: two horizontal base rows always exist in mode 1.
                // The Cross Grid is an optional, independent vertical plan.
                if (takeover == null)
                    return;

                double tierStep = takeover.NextHorizontalTierOffset - takeover.HorizontalTierOffset;
                bool reserveSlot04Tier = PHU_Slot07_DataCenterContext.ReserveFrontSlot04Tier;
                bool reserveBeamType2TransverseTier =
                    PHU_Slot07_DataCenterBeamType2Context.IsFrontView(view.View);

                // Slot09 FRONT common tier order:
                // Type1: existing edge-hole tiers -> Slot04 plates -> G1 -> G2.
                // Type2: existing edge-hole tiers -> transverse relation ->
                //        Slot04 plates -> G1 -> G2.
                // The old Shape total line is the exact next free tier after
                // the existing FRONT hole dimensions. Type2 owns that line;
                // every later family is moved out by real Shape tier steps.
                int reservedTierCount = 0;
                if (reserveBeamType2TransverseTier)
                {
                    PHU_Slot07_DataCenterBeamType2Context
                        .RegisterFrontTransverseDimensionTier(
                            view.View,
                            takeover.HorizontalTier
                        );
                    reservedTierCount++;
                    view.HorizontalGridPlanCount++;
                }
                if (reserveSlot04Tier)
                {
                    double absoluteSlot04Line =
                        Dot(view.RefLeft, view.UpDirection)
                        + takeover.HorizontalLineNormal
                        + reservedTierCount * tierStep;
                    PHU_Slot07_DataCenterContext.RegisterFrontSlot04Tier(
                        view.View,
                        absoluteSlot04Line,
                        view.UpDirection.X,
                        view.UpDirection.Y
                    );
                    reservedTierCount++;
                    view.HorizontalGridPlanCount++;
                }
                if (reservedTierCount != CountReservedFrontMode1Tiers(
                        reserveSlot04Tier,
                        reserveBeamType2TransverseTier))
                    throw new InvalidOperationException(
                        "Slot09 FRONT tier reservation contract is inconsistent.");

                double g1Line =
                    takeover.HorizontalLineNormal + reservedTierCount * tierStep;
                double g2Line = g1Line + tierStep;

                AddHorizontalPlanMode1(
                    plans,
                    view,
                    view.Key + " G1 REF-EDGE-EDGE-REF",
                    g1Line,
                    view.RefLeft,
                    takeover.OriginalLeft,
                    takeover.OriginalRight,
                    view.RefRight
                );

                AddHorizontalPlanMode1(
                    plans,
                    view,
                    view.Key + " G2 REF-REF",
                    g2Line,
                    view.RefLeft,
                    view.RefRight
                );
                view.HorizontalGridPlanCount++;

                if (view.Axes == null || view.Axes.Cross == null)
                    return;

                bool isAbove = IsCrossGridAboveBeam(view);
                if (isAbove)
                {
                    // GRID NẰM TRÊN DẦM (ABOVE):
                    // Tạo 1 chain duy nhất: GRID -> REF -> TOP -> BOTTOM thay thế Vertical Total
                    double lineStation = takeover.VerticalLineStation;

                    P2 gridPt = FromLocal(view, 0.0, view.Axes.Cross.Coordinate);
                    P2 refPt = view.RefLeft;
                    P2 topPt = takeover.OriginalTop != null ? takeover.OriginalTop : view.RefLeft;
                    P2 bottomPt =
                        takeover.OriginalBottom != null ? takeover.OriginalBottom : view.RefLeft;

                    AddVerticalPlanMode1(
                        plans,
                        view,
                        view.Key + " VERTICAL CHAIN GRID-REF-TOP-BOTTOM",
                        lineStation,
                        gridPt,
                        refPt,
                        topPt,
                        bottomPt
                    );
                }
                else
                {
                    // GRID NẰM DƯỚI DẦM (BELOW):
                    // Vertical Total do Shape tự tạo. Grid engine tạo DIM riêng REF -> GRID
                    if (view.Axes.CrossCoincident)
                    {
                        _skippedZeroPlanCount++;
                        return;
                    }

                    double crossLine =
                        takeover.VerticalLineStation
                        - (takeover.NextVerticalTierOffset - takeover.VerticalTierOffset);

                    AddVerticalPlanMode1(
                        plans,
                        view,
                        view.Key + " CROSS REF-GRID",
                        crossLine,
                        view.RefLeft,
                        FromLocal(view, 0.0, view.Axes.Cross.Coordinate)
                    );
                }
            }
        }

        private static void AddSingleEndBaseHorizontalPlanMode1(
            ViewGeometry view,
            double lineNormal,
            List<DimPlan> plans
        )
        {
            if (view.Axes.SingleEndIsLeft)
            {
                AddHorizontalPlanMode1(
                    plans,
                    view,
                    view.Key + " G2 GRID-REF-REF",
                    lineNormal,
                    FromLocal(view, view.Axes.SingleEnd.Coordinate, 0.0),
                    view.RefLeft,
                    view.RefRight
                );
            }
            else
            {
                AddHorizontalPlanMode1(
                    plans,
                    view,
                    view.Key + " G2 REF-REF-GRID",
                    lineNormal,
                    view.RefLeft,
                    view.RefRight,
                    FromLocal(view, view.Axes.SingleEnd.Coordinate, 0.0)
                );
            }

            view.HorizontalGridPlanCount++;
        }

        private static void AddHorizontalPlanMode1(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            double lineNormal,
            params P2[] points
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.MeasurementAxis = view.MainDirection;
            plan.DimensionDirection = view.UpDirection;
            for (int i = 0; i < points.Length; i++)
                AddUniqueByAxis(plan.Points, points[i], view.MainDirection);

            if (plan.Points.Count < 2)
            {
                _skippedZeroPlanCount++;
                return;
            }

            plan.Distance = lineNormal - LocalNormal(view, plan.Points[0]);
            plans.Add(plan);
        }

        private static void AddVerticalPlanMode1(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            double lineStation,
            params P2[] points
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.MeasurementAxis = view.UpDirection;
            plan.DimensionDirection = Scale(view.MainDirection, -1.0);
            for (int i = 0; i < points.Length; i++)
                AddUniqueByAxis(plan.Points, points[i], view.UpDirection);

            if (plan.Points.Count < 2)
            {
                _skippedZeroPlanCount++;
                return;
            }

            plan.Distance = LocalStation(view, plan.Points[0]) - lineStation;
            plans.Add(plan);
        }

        private static void AddUniqueByAxis(List<P2> points, P2 candidate, P2 axis)
        {
            if (candidate == null || axis == null)
                return;

            double candidateStation = Dot(candidate, axis);
            for (int i = 0; i < points.Count; i++)
            {
                double existingStation = Dot(points[i], axis);
                if (Math.Abs(existingStation - candidateStation) <= GeometryTolerance)
                    return;
            }

            points.Add(new P2(candidate.X, candidate.Y));
        }

        private static void BuildViewPlans(ViewGeometry view, bool isTop, List<DimPlan> plans)
        {
            TotalTakeover takeover = view.Takeover;
            view.HorizontalGridPlanCount = 0;
            double g1Line = takeover.HorizontalLineNormal;
            double g2Line =
                g1Line + (takeover.NextHorizontalTierOffset - takeover.HorizontalTierOffset);
            double g3Line =
                g1Line + (takeover.OuterHorizontalTierOffset - takeover.HorizontalTierOffset);

            AddHorizontalPlan(
                plans,
                view,
                view.Key + " G1 REF-EDGE-EDGE-REF",
                g1Line,
                view.RefLeft,
                takeover.OriginalLeft,
                takeover.OriginalRight,
                view.RefRight
            );

            if (_requestedAxisCount == 1 || _requestedAxisCount == 2)
            {
                AddSingleEndBaseHorizontalPlan(view, g2Line, plans);

                if (_requestedAxisCount == 2)
                    AddCrossGridPlan(view, takeover, plans);
                return;
            }

            bool bothEndsCoincident = view.Axes.LeftCoincident && view.Axes.RightCoincident;
            if (bothEndsCoincident)
            {
                _skippedZeroPlanCount++;
            }
            else
            {
                AddHorizontalPlan(
                    plans,
                    view,
                    view.Key + " G2 GRID-REF-REF-GRID",
                    g2Line,
                    FromLocal(view, view.Axes.Left.Coordinate, 0.0),
                    view.RefLeft,
                    view.RefRight,
                    FromLocal(view, view.Axes.Right.Coordinate, 0.0)
                );
                view.HorizontalGridPlanCount++;
            }

            if (isTop)
            {
                AddHorizontalPlan(
                    plans,
                    view,
                    view.Key + " G3 GRID-GRID",
                    bothEndsCoincident ? g2Line : g3Line,
                    FromLocal(view, view.Axes.Left.Coordinate, 0.0),
                    FromLocal(view, view.Axes.Right.Coordinate, 0.0)
                );
                view.HorizontalGridPlanCount++;
            }

            if (_requestedAxisCount == 3)
                AddCrossGridPlan(view, takeover, plans);
        }

        private static void AddSingleEndBaseHorizontalPlan(
            ViewGeometry view,
            double lineNormal,
            List<DimPlan> plans
        )
        {
            // Start from the three-Grid horizontal base chain. One End Grid is
            // unavailable in mode 1/2, so retain both REF endpoints and insert
            // the resolved End Grid on its semantic side. AddUnique removes a
            // coincident Grid/REF foot without removing the remaining REF span.
            if (view.Axes.SingleEndIsLeft)
            {
                AddHorizontalPlan(
                    plans,
                    view,
                    view.Key + " G2 GRID-REF-REF",
                    lineNormal,
                    FromLocal(view, view.Axes.SingleEnd.Coordinate, 0.0),
                    view.RefLeft,
                    view.RefRight
                );
            }
            else
            {
                AddHorizontalPlan(
                    plans,
                    view,
                    view.Key + " G2 REF-REF-GRID",
                    lineNormal,
                    view.RefLeft,
                    view.RefRight,
                    FromLocal(view, view.Axes.SingleEnd.Coordinate, 0.0)
                );
            }

            view.HorizontalGridPlanCount++;
        }

        private static void AddCrossGridPlan(
            ViewGeometry view,
            TotalTakeover takeover,
            List<DimPlan> plans
        )
        {
            if (view.Axes.CrossCoincident)
            {
                _skippedZeroPlanCount++;
                return;
            }

            double crossLine =
                takeover.VerticalLineStation
                - (takeover.NextVerticalTierOffset - takeover.VerticalTierOffset);
            AddVerticalPlan(
                plans,
                view,
                view.Key + " CROSS REF-GRID",
                crossLine,
                view.RefLeft,
                FromLocal(view, 0.0, view.Axes.Cross.Coordinate)
            );
        }

        private static void AddHorizontalPlan(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            double lineNormal,
            params P2[] points
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.MeasurementAxis = view.MainDirection;
            plan.DimensionDirection = view.UpDirection;
            for (int i = 0; i < points.Length; i++)
                AddUnique(plan.Points, points[i]);
            plan.Distance = lineNormal - LocalNormal(view, plan.Points[0]);
            plans.Add(plan);
        }

        private static void AddVerticalPlan(
            List<DimPlan> plans,
            ViewGeometry view,
            string name,
            double lineStation,
            params P2[] points
        )
        {
            DimPlan plan = new DimPlan();
            plan.Name = name;
            plan.View = view;
            plan.MeasurementAxis = view.UpDirection;
            plan.DimensionDirection = Scale(view.MainDirection, -1.0);
            for (int i = 0; i < points.Length; i++)
                AddUnique(plan.Points, points[i]);
            plan.Distance = LocalStation(view, plan.Points[0]) - lineStation;
            plans.Add(plan);
        }

        private static void EnsureAllTakeoversReported(Context context)
        {
            if (_requestedAxisCount == 1)
            {
                // Mode 1: Chỉ kiểm tra những view có tham gia tạo Grid plan
                ViewGeometry top;
                if (
                    context.Views.TryGetValue("TOP", out top)
                    && top != null
                    && top.Axes != null
                    && (top.Axes.SingleEnd != null || top.Axes.InteriorEnd != null)
                    && top.Takeover == null
                )
                {
                    throw new InvalidOperationException(
                        "TOP Shape total handoff was not completed."
                    );
                }

                ViewGeometry front;
                if (
                    context.Views.TryGetValue("FRONT", out front)
                    && front != null
                    && front.Takeover == null
                )
                {
                    throw new InvalidOperationException(
                        "FRONT Shape total handoff was not completed."
                    );
                }
                return;
            }

            // Mode 2 & 3: Giữ nguyên 100% logic cũ
            foreach (KeyValuePair<string, ViewGeometry> pair in context.Views)
            {
                if (pair.Value.Takeover == null)
                    throw new InvalidOperationException(
                        pair.Key + " Shape total handoff was not completed."
                    );
            }
        }

        private static void ValidatePlans(List<DimPlan> plans)
        {
            if (plans == null || plans.Count == 0)
            {
                if (_requestedAxisCount == 1)
                    return; // Mode 1 cho phép no-op khi cả 2 view không có grid
                throw new InvalidOperationException("Grid dimension plan is empty.");
            }

            for (int i = 0; i < plans.Count; i++)
            {
                DimPlan plan = plans[i];
                if (
                    plan.View == null
                    || plan.Points.Count < 2
                    || !IsFinite(plan.Distance)
                    || plan.Distance <= GeometryTolerance
                )
                    throw new InvalidOperationException(
                        plan.Name + " is not a valid dimension plan."
                    );

                List<double> stations = ProjectAndSort(plan.Points, plan.MeasurementAxis);
                for (int p = 1; p < stations.Count; p++)
                {
                    if (stations[p] - stations[p - 1] <= GeometryTolerance)
                    {
                        throw new InvalidOperationException(
                            plan.Name + " contains duplicate or unordered dimension points."
                        );
                    }
                }
            }
        }

        private static bool RestoreShapeHorizontalTotals()
        {
            if (_context == null)
                return false;

            bool allRestored = true;
            bool createdAny = false;
            TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();

            foreach (KeyValuePair<string, ViewGeometry> pair in _context.Views)
            {
                ViewGeometry view = pair.Value;
                if (view.Takeover == null)
                    continue;

                // 1. Phục hồi Horizontal Total nếu view đã bị takeover Horizontal
                if (ShouldTakeOverHorizontalTotal(view.View))
                {
                    try
                    {
                        TSD.PointList points = new TSD.PointList();
                        points.Add(view.Takeover.OriginalLeft.ToPoint());
                        points.Add(view.Takeover.OriginalRight.ToPoint());
                        TSD.StraightDimensionSet restored = handler.CreateDimensionSet(
                            view.View,
                            points,
                            new TSG.Vector(0.0, 1.0, 0.0),
                            view.Takeover.OriginalHorizontalDistance
                        );
                        if (restored == null)
                            allRestored = false;
                        else
                            createdAny = true;
                    }
                    catch
                    {
                        allRestored = false;
                    }
                }

                // 2. Phục hồi Vertical Total nếu view đã bị takeover Vertical
                if (ShouldTakeOverVerticalTotal(view.View))
                {
                    try
                    {
                        TSD.PointList vPoints = new TSD.PointList();
                        if (view.Takeover.OriginalTop != null)
                            vPoints.Add(view.Takeover.OriginalTop.ToPoint());
                        if (view.Takeover.OriginalBottom != null)
                            vPoints.Add(view.Takeover.OriginalBottom.ToPoint());

                        if (vPoints.Count >= 2)
                        {
                            TSD.StraightDimensionSet restoredV = handler.CreateDimensionSet(
                                view.View,
                                vPoints,
                                new TSG.Vector(-1.0, 0.0, 0.0),
                                view.Takeover.OriginalVerticalDistance
                            );
                            if (restoredV == null)
                                allRestored = false;
                            else
                                createdAny = true;
                        }
                    }
                    catch
                    {
                        allRestored = false;
                    }
                }
            }

            try
            {
                if (createdAny)
                    _context.Drawing.CommitChanges();
            }
            catch
            {
                allRestored = false;
            }

            return allRestored;
        }

        private static void DeleteCreated(List<TSD.StraightDimensionSet> created)
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

            try
            {
                if (_context != null && _context.Drawing != null)
                    _context.Drawing.CommitChanges();
            }
            catch { }
        }

        private static bool ViewContainsPart(TSD.View view, TSM.Part modelPart)
        {
            if (view == null || modelPart == null || modelPart.Identifier == null)
                return false;

            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part drawingPart = parts.Current as TSD.Part;
                if (
                    drawingPart != null
                    && drawingPart.ModelIdentifier != null
                    && drawingPart.ModelIdentifier.ID == modelPart.Identifier.ID
                )
                    return true;
            }

            return false;
        }

        private static List<P2> ReadReferenceLine(
            TSM.Part part,
            bool withCuts,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<P2> result = new List<P2>();
            try
            {
                ArrayList line = part.GetReferenceLine(withCuts);
                if (line != null)
                {
                    foreach (object item in line)
                    {
                        TSG.Point point = item as TSG.Point;
                        if (point != null)
                            AddUnique(result, Transform(point, currentToGlobal, globalToView));
                    }
                }
            }
            catch { }

            if (result.Count < 2 && !withCuts)
            {
                TSM.Beam beam = part as TSM.Beam;
                if (beam != null)
                {
                    AddUnique(result, Transform(beam.StartPoint, currentToGlobal, globalToView));
                    AddUnique(result, Transform(beam.EndPoint, currentToGlobal, globalToView));
                }
            }

            return result;
        }

        private static bool TrySelectCorrespondingEndAxes(
            ViewGeometry top,
            ViewGeometry front,
            out GridAxis topAxis,
            out GridAxis frontAxis,
            out string side
        )
        {
            topAxis = GetResolvedEndAxis(top, true);
            frontAxis = GetResolvedEndAxis(front, true);
            side = "LEFT";
            if (AreCorrespondingGridAxes(topAxis, frontAxis))
                return true;

            topAxis = GetResolvedEndAxis(top, false);
            frontAxis = GetResolvedEndAxis(front, false);
            side = "RIGHT";
            if (AreCorrespondingGridAxes(topAxis, frontAxis))
                return true;

            topAxis = null;
            frontAxis = null;
            side = String.Empty;
            return false;
        }

        private static GridAxis GetResolvedEndAxis(ViewGeometry view, bool left)
        {
            if (view == null || view.Axes == null)
                return null;
            if (_requestedAxisCount == 1 || _requestedAxisCount == 2)
                return view.Axes.SingleEndIsLeft == left ? view.Axes.SingleEnd : null;
            return left ? view.Axes.Left : view.Axes.Right;
        }

        private static bool AreCorrespondingGridAxes(GridAxis first, GridAxis second)
        {
            if (first == null || second == null)
                return false;
            if (first.ModelIdentifier > 0 && second.ModelIdentifier > 0)
                return first.ModelIdentifier == second.ModelIdentifier;
            return !String.IsNullOrWhiteSpace(first.Label)
                && String.Equals(first.Label, second.Label, StringComparison.OrdinalIgnoreCase);
        }

        private static P2 SelectGridEndpointInSheet(
            ViewGeometry view,
            GridAxis axis,
            bool maximumSheetY
        )
        {
            if (view == null || axis == null)
                return null;

            // Sort the two real line ends only after both have reached the
            // common sheet coordinate system. This does not depend on the
            // MainPart reference direction and therefore cannot invert when
            // TOP and FRONT use different view coordinate systems.
            P2 startSheet = ToSheetPoint(view, axis.StartEndpoint ?? axis.StartPoint);
            P2 endSheet = ToSheetPoint(view, axis.EndEndpoint ?? axis.EndPoint);
            if (startSheet == null || endSheet == null)
                return null;

            if (maximumSheetY)
                return startSheet.Y >= endSheet.Y ? startSheet : endSheet;
            return startSheet.Y <= endSheet.Y ? startSheet : endSheet;
        }

        private static P2 ToSheetPoint(ViewGeometry view, P2 localPoint)
        {
            if (view == null || view.View == null || localPoint == null)
                return null;

            try
            {
                TSG.Point origin = view.View.Origin;
                double scale = view.View.Attributes.Scale;
                if (origin == null || !IsFinite(scale) || scale <= 0.0)
                    return null;
                return new P2(origin.X + (localPoint.X / scale), origin.Y + (localPoint.Y / scale));
            }
            catch
            {
                return null;
            }
        }

        private static List<TSD.Line> FindExistingSlot09ReferenceConnectors(TSD.ContainerView sheet)
        {
            List<TSD.Line> result = new List<TSD.Line>();
            if (sheet == null)
                return result;

            TSD.DrawingObjectEnumerator objects = sheet.GetAllObjects(typeof(TSD.Line));
            while (objects != null && objects.MoveNext())
            {
                TSD.Line line = objects.Current as TSD.Line;
                if (line == null)
                    continue;

                try
                {
                    string owner = string.Empty;
                    if (
                        ((TSD.DatabaseObject)line).GetUserProperty(
                            Slot09ReferenceConnectorOwnerField,
                            ref owner
                        )
                        && string.Equals(
                            owner,
                            Slot09ReferenceConnectorOwnerValue,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        result.Add(line);
                    }
                }
                catch { }
            }

            return result;
        }

        private static bool TagSlot09ReferenceConnector(TSD.Line line)
        {
            if (line == null)
                return false;

            try
            {
                bool tagged = ((TSD.DatabaseObject)line).SetUserProperty(
                    Slot09ReferenceConnectorOwnerField,
                    Slot09ReferenceConnectorOwnerValue
                );
                if (!tagged)
                    return false;
                try
                {
                    line.Modify();
                }
                catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DeleteReferenceConnector(TSD.Line line)
        {
            try
            {
                if (line != null)
                    line.Delete();
            }
            catch { }
        }

        private static double GetAlignmentSheetTolerance(ViewGeometry top, ViewGeometry front)
        {
            try
            {
                double maximumScale = Math.Max(
                    top.View.Attributes.Scale,
                    front.View.Attributes.Scale
                );
                if (IsFinite(maximumScale) && maximumScale > 0.0)
                    return Math.Max(0.01, GeometryTolerance / maximumScale);
            }
            catch { }
            return 0.01;
        }

        private static GridAxis ReadMatchingGridAxis(ViewGeometry view, GridAxis expected)
        {
            if (view == null || expected == null)
                return null;

            ViewGeometry fresh = new ViewGeometry();
            fresh.View = view.View;
            fresh.RefLeft = view.RefLeft;
            fresh.RefRight = view.RefRight;
            fresh.MainDirection = view.MainDirection;
            fresh.UpDirection = view.UpDirection;
            fresh.RefLength = view.RefLength;
            ReadDrawingGridAxes(view.View, fresh);

            for (int i = 0; i < fresh.GridAxes.Count; i++)
            {
                GridAxis candidate = fresh.GridAxes[i];
                if (!candidate.IsCrossAxis && AreCorrespondingGridAxes(expected, candidate))
                    return candidate;
            }

            return null;
        }

        private static bool RestoreFrontOrigin(TSD.View frontView, TSG.Point origin)
        {
            try
            {
                if (
                    frontView == null
                    || origin == null
                    || _context == null
                    || _context.Drawing == null
                )
                    return false;
                frontView.Origin = ClonePoint(origin);
                if (!frontView.Modify())
                    return false;
                _context.Drawing.CommitChanges();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static TSG.Point ClonePoint(TSG.Point point)
        {
            return point == null ? null : new TSG.Point(point.X, point.Y, point.Z);
        }

        private static double Distance3(TSG.Point first, TSG.Point second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            double dz = first.Z - second.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static string Format(double value)
        {
            return Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
        }

        private static void ReadDrawingGridAxes(TSD.View view, ViewGeometry geometry)
        {
            TSD.DrawingObjectEnumerator grids = view.GetAllObjects(typeof(TSD.GridLine));
            while (grids != null && grids.MoveNext())
            {
                TSD.GridLine line = grids.Current as TSD.GridLine;
                if (line == null || line.StartLabel == null || line.EndLabel == null)
                    continue;

                TSG.Point rawStart = line.StartLabel.GridPoint;
                TSG.Point rawEnd = line.EndLabel.GridPoint;
                if (rawStart == null || rawEnd == null)
                    continue;

                P2 start = new P2(rawStart.X, rawStart.Y);
                P2 end = new P2(rawEnd.X, rawEnd.Y);
                P2 direction = Normalize(Subtract(end, start));
                if (direction == null)
                    continue;

                double alongMain = Math.Abs(Dot(direction, geometry.MainDirection));
                double alongUp = Math.Abs(Dot(direction, geometry.UpDirection));
                GridAxis axis = new GridAxis();
                axis.StartPoint = start;
                axis.EndPoint = end;
                axis.StartEndpoint = ReadActualGridEndpoint(line.StartLabel, start);
                axis.EndEndpoint = ReadActualGridEndpoint(line.EndLabel, end);
                axis.Label = ReadGridLabel(line);
                try
                {
                    if (line.ModelIdentifier != null)
                        axis.ModelIdentifier = line.ModelIdentifier.ID;
                }
                catch
                {
                    axis.ModelIdentifier = 0;
                }
                if (alongUp >= DirectionCosineTolerance && alongUp > alongMain)
                {
                    axis.IsCrossAxis = false;
                    axis.Coordinate = LocalStation(geometry, start);
                }
                else if (alongMain >= DirectionCosineTolerance && alongMain > alongUp)
                {
                    axis.IsCrossAxis = true;
                    axis.Coordinate = LocalNormal(geometry, start);
                }
                else
                {
                    continue;
                }

                AddUniqueGridAxis(geometry.GridAxes, axis);
            }
        }

        private static P2 ReadActualGridEndpoint(TSD.GridLine.GridLabel label, P2 fallback)
        {
            if (label == null)
                return fallback;

            try
            {
                TSG.Point point = label.OffsetGridPoint;
                if (point != null && IsFinite(point.X) && IsFinite(point.Y))
                    return new P2(point.X, point.Y);
            }
            catch { }

            return fallback;
        }

        private static string ReadGridLabel(TSD.GridLine line)
        {
            if (line == null)
                return String.Empty;

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

        private static P2 FromLocal(ViewGeometry view, double station, double normal)
        {
            return new P2(
                view.RefLeft.X + (view.MainDirection.X * station) + (view.UpDirection.X * normal),
                view.RefLeft.Y + (view.MainDirection.Y * station) + (view.UpDirection.Y * normal)
            );
        }

        private static double LocalStation(ViewGeometry view, P2 point)
        {
            return Dot(Subtract(point, view.RefLeft), view.MainDirection);
        }

        private static double LocalNormal(ViewGeometry view, P2 point)
        {
            return Dot(Subtract(point, view.RefLeft), view.UpDirection);
        }

        private static void FindFarthestPair(List<P2> points, out P2 first, out P2 second)
        {
            first = null;
            second = null;
            double maximum = Double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = Distance(points[i], points[j]);
                    if (distance > maximum)
                    {
                        maximum = distance;
                        first = points[i];
                        second = points[j];
                    }
                }
            }
        }

        private static P2 ExtremePoint(List<P2> points, P2 axis, bool maximum)
        {
            P2 best = null;
            double bestValue = maximum ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double value = Dot(points[i], axis);
                if (
                    best == null
                    || (maximum && value > bestValue)
                    || (!maximum && value < bestValue)
                )
                {
                    best = points[i];
                    bestValue = value;
                }
            }
            return best;
        }

        private static List<double> ProjectAndSort(List<P2> points, P2 axis)
        {
            List<double> result = new List<double>();
            for (int i = 0; i < points.Count; i++)
                result.Add(Dot(points[i], axis));
            result.Sort();
            return result;
        }

        private static void AddUniqueGridAxis(List<GridAxis> axes, GridAxis candidate)
        {
            for (int i = 0; i < axes.Count; i++)
            {
                if (
                    axes[i].IsCrossAxis == candidate.IsCrossAxis
                    && Math.Abs(axes[i].Coordinate - candidate.Coordinate) <= GeometryTolerance
                )
                    return;
            }
            axes.Add(candidate);
        }

        private static void AddUnique(List<P2> points, P2 candidate)
        {
            if (candidate == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], candidate) <= GeometryTolerance)
                    return;
            }
            points.Add(new P2(candidate.X, candidate.Y));
        }

        private static P2 Normalize(P2 value)
        {
            if (value == null)
                return null;
            double length = Math.Sqrt((value.X * value.X) + (value.Y * value.Y));
            if (!IsFinite(length) || length <= GeometryTolerance)
                return null;
            return new P2(value.X / length, value.Y / length);
        }

        private static P2 Subtract(P2 first, P2 second)
        {
            if (first == null || second == null)
                return null;
            return new P2(first.X - second.X, first.Y - second.Y);
        }

        private static P2 Scale(P2 value, double factor)
        {
            return value == null ? null : new P2(value.X * factor, value.Y * factor);
        }

        private static double Dot(P2 first, P2 second)
        {
            if (first == null || second == null)
                return Double.NaN;
            return (first.X * second.X) + (first.Y * second.Y);
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
    }
}
