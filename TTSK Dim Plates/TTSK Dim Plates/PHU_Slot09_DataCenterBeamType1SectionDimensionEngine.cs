#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Type-1-only partial extension of the Slot09 Beam supplemental engine.
    /// It dimensions an optional true cross-section after Shape H has finished
    /// the prepared main views. No section is created or arranged here.
    /// </summary>
    public static partial class PHU_Slot09_DataCenterBeamType2DimensionEngine
    {
        private static bool _type1SectionEnabled;

        public static bool LastType1SectionApplicable { get; private set; }
        public static bool LastType1SectionSucceeded { get; private set; }
        public static int LastType1SectionViewCount { get; private set; }
        public static int LastType1SectionCreatedCount { get; private set; }
        public static int LastType1SectionReusedCount { get; private set; }
        public static int LastType1SectionConflictCount { get; private set; }
        public static string LastType1SectionMessage { get; private set; }

        public static void ConfigureType1Sections(bool enabled)
        {
            ResetType1SectionResult();
            _type1SectionEnabled = enabled;
        }

        /// <summary>
        /// Read-only geometry gate used before Shape H starts mutating DIMs.
        /// A genuinely absent optional section is valid; an observed but
        /// ambiguous plate/hole topology stops the drawing before mutation.
        /// </summary>
        public static bool PreflightType1Sections(out string message)
        {
            message = String.Empty;
            if (!_type1SectionEnabled
                || !PHU_Slot09_DataCenterContext.IsActive
                || PHU_Slot09_DataCenterBeamType2Context.IsActive)
                return true;
            try
            {
                Analysis analysis;
                string analysisMessage;
                if (!TryAnalyzeType1Sections(out analysis, out analysisMessage))
                {
                    message = "Data Center Beam Type1 Section preflight: "
                        + analysisMessage;
                    return false;
                }
                ValidatePlans(analysis.Plans);
                message = analysis.Plans.Count == 0
                    ? "Data Center Beam Type1: optional Section A is absent."
                    : "Data Center Beam Type1: optional Section A geometry is proven; plans="
                        + analysis.Plans.Count.ToString(CultureInfo.InvariantCulture)
                        + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = "Data Center Beam Type1 Section preflight failed: "
                    + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Runs only in Slot09 Data Center Type 1. Existing exact dimensions
        /// are reused; missing plans are added. Existing dimensions are never
        /// deleted by this optional section pass.
        /// </summary>
        public static bool ExecuteType1SectionsAfterShape()
        {
            ResetType1SectionResult();
            if (!_type1SectionEnabled
                || !PHU_Slot09_DataCenterContext.IsActive
                || PHU_Slot09_DataCenterBeamType2Context.IsActive)
            {
                LastType1SectionSucceeded = true;
                LastType1SectionMessage =
                    "Data Center Beam Type1 Section DIM skipped: route is inactive.";
                return true;
            }

            // Shape H has just finished and owns the drawing's tier contract.
            // Capture that exact base/step; the section engine must not keep
            // an independent spacing rule.
            CaptureCurrentShapeXTierSpacing();

            List<TSD.StraightDimensionSet> created =
                new List<TSD.StraightDimensionSet>();
            TSD.Drawing drawing = null;
            try
            {
                Analysis analysis;
                string message;
                if (!TryAnalyzeType1Sections(out analysis, out message))
                {
                    LastType1SectionMessage =
                        "Data Center Beam Type1 Section DIM failed preflight: "
                        + message;
                    return false;
                }

                drawing = analysis.Drawing;
                LastType1SectionViewCount = analysis.Sections.Count;
                LastType1SectionApplicable = analysis.Plans.Count > 0;
                if (!LastType1SectionApplicable)
                {
                    LastType1SectionSucceeded = true;
                    LastType1SectionMessage =
                        "Data Center Beam Type1 Section DIM: no proven optional section; main-view flow unchanged.";
                    return true;
                }

                ValidatePlans(analysis.Plans);
                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();
                for (int i = 0; i < analysis.Plans.Count; i++)
                {
                    DimPlan plan = analysis.Plans[i];
                    ExistingPlanStatus status = FindExistingPlan(plan);
                    if (status == ExistingPlanStatus.Exact)
                    {
                        LastType1SectionReusedCount++;
                        continue;
                    }
                    if (status == ExistingPlanStatus.FootConflict)
                    {
                        LastType1SectionConflictCount++;
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
                    LastType1SectionCreatedCount++;
                }

                if (LastType1SectionConflictCount > 0)
                    throw new InvalidOperationException(
                        LastType1SectionConflictCount.ToString(
                            CultureInfo.InvariantCulture)
                        + " same-foot direction conflict(s) were protected.");

                if (created.Count > 0)
                    analysis.Drawing.CommitChanges();

                LastType1SectionSucceeded = true;
                LastType1SectionMessage =
                    "Data Center Beam Type1 optional Section DIM: views="
                    + LastType1SectionViewCount.ToString(CultureInfo.InvariantCulture)
                    + ", plans="
                    + analysis.Plans.Count.ToString(CultureInfo.InvariantCulture)
                    + ", created="
                    + LastType1SectionCreatedCount.ToString(CultureInfo.InvariantCulture)
                    + ", reused="
                    + LastType1SectionReusedCount.ToString(CultureInfo.InvariantCulture)
                    + ", conflicts=0.";
                return true;
            }
            catch (Exception ex)
            {
                DeleteCreated(created);
                TryCommit(drawing);
                LastType1SectionCreatedCount = 0;
                LastType1SectionSucceeded = false;
                LastType1SectionMessage =
                    "Data Center Beam Type1 optional Section DIM rolled back: "
                    + ex.Message;
                return false;
            }
        }

        /// <summary>Read-only Type-1 optional section plan audit.</summary>
        public static string AuditType1SectionCurrentDrawingPlan()
        {
            try
            {
                CaptureCurrentShapeXTierSpacing();
                Analysis analysis;
                string message;
                if (!TryAnalyzeType1Sections(out analysis, out message))
                    return "DATA CENTER BEAM TYPE1 SECTION DIM AUDIT SKIPPED\r\n"
                        + message;

                StringBuilder text = new StringBuilder();
                text.AppendLine(
                    "DATA CENTER BEAM TYPE1 SECTION DIM PLAN - READ ONLY");
                text.AppendLine("TIER SOURCE=ShapeScript current drawing contract; base="
                    + _capturedShapeXTierBase.ToString(
                        "0.###", CultureInfo.InvariantCulture)
                    + " step="
                    + _capturedShapeXTierStep.ToString(
                        "0.###", CultureInfo.InvariantCulture));
                text.AppendLine("SupportedSectionViews="
                    + analysis.Sections.Count.ToString(CultureInfo.InvariantCulture));
                if (analysis.Plans.Count == 0)
                {
                    text.AppendLine("TOTAL plans=0 exact=0 missing=0 conflicts=0");
                    text.AppendLine(
                        "Optional section is absent; main-view flow remains valid.");
                    return text.ToString();
                }

                ValidatePlans(analysis.Plans);
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
                        .Append(" distance=").Append(
                            plan.Distance.ToString(
                                "0.###", CultureInfo.InvariantCulture))
                        .Append(" feet=").Append(FormatPoints(plan.Points))
                        .AppendLine();
                }
                text.AppendLine("TOTAL plans="
                    + analysis.Plans.Count.ToString(CultureInfo.InvariantCulture)
                    + " exact=" + exact.ToString(CultureInfo.InvariantCulture)
                    + " missing=" + missing.ToString(CultureInfo.InvariantCulture)
                    + " conflicts=" + conflicts.ToString(CultureInfo.InvariantCulture));
                text.AppendLine(
                    "WIDTH RULE=ordered union of true MainPart/plate edges; coincident edges collapse.");
                text.AppendLine(
                    "POINT ORDER=plate-left -> hole -> plate-right; vertical hole -> beam face.");
                return text.ToString();
            }
            catch (Exception ex)
            {
                return "DATA CENTER BEAM TYPE1 SECTION DIM AUDIT FAILED\r\n"
                    + ex;
            }
        }

        /// <summary>Pure geometry regression; no Tekla API mutation.</summary>
        public static string AuditType1SectionGeometryRegression()
        {
            try
            {
                AssertSyntheticType1PlateSection(
                    BuildSyntheticType1PlateSection(0.0, 125.0, 0.0, 125.0, false),
                    new double[] { 0.0, 125.0 },
                    "equal");
                AssertSyntheticType1PlateSection(
                    BuildSyntheticType1PlateSection(0.0, 200.0, 37.5, 162.5, false),
                    new double[] { 0.0, 37.5, 162.5, 200.0 },
                    "narrow");
                AssertSyntheticType1PlateSection(
                    BuildSyntheticType1PlateSection(0.0, 125.0, -20.0, 145.0, false),
                    new double[] { -20.0, 0.0, 125.0, 145.0 },
                    "wide");
                ViewData normal = BuildSyntheticType1PlateSection(
                    0.0, 200.0, 20.0, 145.0, false);
                ViewData shuffled = BuildSyntheticType1PlateSection(
                    0.0, 200.0, 20.0, 145.0, true);
                Analysis first = BuildSyntheticType1Analysis(normal);
                Analysis second = BuildSyntheticType1Analysis(shuffled);
                if (!String.Equals(
                        PlanFingerprint(first.Plans),
                        PlanFingerprint(second.Plans),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Type1 section part-order invariance failed.");

                ViewData absent = BuildSyntheticType1PlateSection(
                    0.0, 200.0, 20.0, 145.0, false);
                absent.Parts.RemoveAt(absent.Parts.Count - 1);
                PartData absentPlate;
                P3 absentHole;
                bool absentObserved;
                string absentReason;
                if (TryResolveType1SectionPlateHole(
                        absent,
                        out absentPlate,
                        out absentHole,
                        out absentObserved,
                        out absentReason)
                    || absentObserved)
                    throw new InvalidOperationException(
                        "Type1 absent-section no-op regression failed.");

                ViewData ambiguous = BuildSyntheticType1PlateSection(
                    0.0, 200.0, 20.0, 145.0, false);
                PartData secondVisible = ambiguous.Parts[1];
                secondVisible.Depth.Min = 0.0;
                secondVisible.Depth.Max = 0.0;
                secondVisible.Bolts.Clear();
                secondVisible.Bolts.Add(203, SyntheticBolt(
                    203,
                    18.0,
                    new P3(82.5, 157.5, 0.0)));
                PartData ambiguousPlate;
                P3 ambiguousHole;
                bool ambiguousObserved;
                string ambiguousReason;
                if (TryResolveType1SectionPlateHole(
                        ambiguous,
                        out ambiguousPlate,
                        out ambiguousHole,
                        out ambiguousObserved,
                        out ambiguousReason)
                    || !ambiguousObserved)
                    throw new InvalidOperationException(
                        "Type1 ambiguous-section fail-closed regression failed.");

                return "DATA CENTER BEAM TYPE1 SECTION GEOMETRY REGRESSION PASS: "
                    + "equal/narrow/wide edge-union=2/4/4, "
                    + "plate-hole/vertical-order=3/2, "
                    + "hidden-depth/shuffle/absent/ambiguity=PASS.";
            }
            catch (Exception ex)
            {
                return "DATA CENTER BEAM TYPE1 SECTION GEOMETRY REGRESSION FAILED: "
                    + ex.Message;
            }
        }

        private static bool TryAnalyzeType1Sections(
            out Analysis analysis,
            out string message)
        {
            analysis = null;
            message = String.Empty;
            if (!PHU_Slot09_DataCenterContext.IsActive)
            {
                message = "Slot09 Data Center geometry scope is inactive.";
                return false;
            }
            if (PHU_Slot09_DataCenterBeamType2Context.IsActive)
            {
                message = "Type2 owns its own section plan; Type1 route is excluded.";
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
            if (!(drawing is TSD.AssemblyDrawing))
            {
                message = "Type1 optional section requires an AssemblyDrawing.";
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

            TSD.ContainerView sheet = drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null
                ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View drawingView = views.Current as TSD.View;
                if (!IsRuntimeSectionViewForType1(drawingView))
                    continue;
                ViewData view = ReadView(
                    model,
                    mainPart,
                    drawingView,
                    currentToGlobal);
                if (view == null || !IsTrueCrossSectionForType1(view))
                    continue;

                PartData plate;
                P3 hole;
                bool observed;
                string reason;
                if (!TryResolveType1SectionPlateHole(
                        view,
                        out plate,
                        out hole,
                        out observed,
                        out reason))
                {
                    if (observed)
                    {
                        message = "Section view "
                            + view.Identifier.ToString(CultureInfo.InvariantCulture)
                            + " has an ambiguous plate/hole relation: " + reason;
                        return false;
                    }
                    continue;
                }
                result.Sections.Add(view);
            }

            result.Sections.Sort(delegate(ViewData first, ViewData second)
            {
                return first.Identifier.CompareTo(second.Identifier);
            });
            for (int i = 0; i < result.Sections.Count; i++)
            {
                ViewData view = result.Sections[i];
                PartData plate;
                P3 hole;
                bool observed;
                string reason;
                if (!TryResolveType1SectionPlateHole(
                        view,
                        out plate,
                        out hole,
                        out observed,
                        out reason))
                {
                    message = "Section geometry changed during analysis: " + reason;
                    return false;
                }
                BuildSectionPlateHolePlansCore(
                    result,
                    view,
                    plate,
                    hole,
                    "T1-S" + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    1);
            }

            analysis = result;
            message = result.Sections.Count == 0
                ? "No proven optional Type1 cross-section exists."
                : "Type1 optional section plan resolved.";
            return true;
        }

        private static bool TryResolveType1SectionPlateHole(
            ViewData view,
            out PartData plate,
            out P3 hole,
            out bool topologyObserved,
            out string reason)
        {
            plate = null;
            hole = null;
            topologyObserved = false;
            reason = String.Empty;
            if (view == null || view.Main == null || !view.Main.Bounds.IsValid)
                return false;

            double mainTop = view.Main.Bounds.MaxY;
            double contactTolerance = GeometryTolerance * 4.0;
            List<PartData> candidates = new List<PartData>();
            List<P3> holes = new List<P3>();
            for (int i = 0; i < view.Parts.Count; i++)
            {
                PartData part = view.Parts[i];
                if (part == null || part.IsMain || !part.IsContourPlate
                    || !part.SameMainAssembly || !part.Bounds.IsValid
                    || !part.Depth.Overlaps(
                        view.DepthMin,
                        view.DepthMax,
                        GeometryTolerance * 2.0))
                    continue;
                bool touchesTop = Math.Abs(part.Bounds.MinY - mainTop)
                        <= contactTolerance
                    && part.Bounds.MaxY > mainTop + GeometryTolerance;
                if (!touchesTop)
                    continue;

                double overlap = Math.Min(part.Bounds.MaxX, view.Main.Bounds.MaxX)
                    - Math.Max(part.Bounds.MinX, view.Main.Bounds.MinX);
                double smallerWidth = Math.Min(
                    part.Bounds.Width,
                    view.Main.Bounds.Width);
                if (smallerWidth <= GeometryTolerance
                    || overlap < smallerWidth * 0.80)
                    continue;

                int visibleHoleCount = CountVisibleProjectedHoles(view, part);
                if (visibleHoleCount <= 0)
                    continue;
                topologyObserved = true;
                P3 visibleHole = FindVisibleSingletonHole(view, part);
                if (visibleHole == null)
                    continue;
                candidates.Add(part);
                holes.Add(visibleHole);
            }

            if (candidates.Count != 1)
            {
                reason = topologyObserved
                    ? candidates.Count == 0
                        ? "visible plate holes are not a unique singleton."
                        : candidates.Count.ToString(CultureInfo.InvariantCulture)
                            + " cut plate candidates remain."
                    : "no same-assembly cut plate with a visible hole.";
                return false;
            }

            plate = candidates[0];
            hole = holes[0];
            return true;
        }

        private static int CountVisibleProjectedHoles(ViewData view, PartData part)
        {
            List<P2> points = new List<P2>();
            if (view == null || part == null)
                return 0;
            foreach (BoltData bolt in part.Bolts.Values)
            {
                List<P3> visible = VisibleProjectedPoints(view, bolt);
                for (int i = 0; i < visible.Count; i++)
                    AddUnique(points, visible[i].XY);
            }
            return points.Count;
        }

        private static bool IsTrueCrossSectionForType1(ViewData view)
        {
            if (view == null || view.Main == null
                || view.Main.Bounds.Width <= GeometryTolerance * 4.0
                || view.Main.Bounds.Height <= GeometryTolerance * 4.0)
                return false;
            P3 first;
            P3 second;
            FindFarthestPair(view.Main.Reference, out first, out second);
            return first == null || second == null
                || Distance(first.XY, second.XY) <= GeometryTolerance * 4.0;
        }

        private static bool IsRuntimeSectionViewForType1(TSD.View view)
        {
            try
            {
                string value = view == null
                    ? String.Empty : view.ViewType.ToString();
                return value.IndexOf(
                    "Section",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static Analysis BuildSyntheticType1Analysis(ViewData view)
        {
            Analysis analysis = new Analysis();
            PartData plate;
            P3 hole;
            bool observed;
            string reason;
            if (!TryResolveType1SectionPlateHole(
                    view,
                    out plate,
                    out hole,
                    out observed,
                    out reason))
                throw new InvalidOperationException(
                    "Synthetic Type1 section resolver failed: " + reason);
            analysis.Sections.Add(view);
            BuildSectionPlateHolePlansCore(
                analysis,
                view,
                plate,
                hole,
                "T1-S01",
                1);
            ValidatePlans(analysis.Plans);
            return analysis;
        }

        private static void AssertSyntheticType1PlateSection(
            ViewData view,
            double[] expectedOuterStations,
            string variant)
        {
            Analysis analysis = BuildSyntheticType1Analysis(view);
            if (analysis.Plans.Count != 3)
                throw new InvalidOperationException(
                    "Type1 " + variant + " section plan count failed.");
            DimPlan outer = FindPlanByName(
                analysis.Plans,
                "T1-S01-SECTION-A-01-MAIN-PLATE-CHAIN");
            DimPlan holeChain = FindPlanByName(
                analysis.Plans,
                "T1-S01-SECTION-A-02-PLATE-HOLE-CHAIN");
            DimPlan vertical = FindPlanByName(
                analysis.Plans,
                "T1-S01-SECTION-A-03-MAIN-HOLE-Y");
            if (outer == null || outer.Points.Count != expectedOuterStations.Length)
                throw new InvalidOperationException(
                    "Type1 " + variant + " edge-union count failed.");
            for (int i = 0; i < expectedOuterStations.Length; i++)
            {
                if (Math.Abs(outer.Points[i].X - expectedOuterStations[i]) > 0.001)
                    throw new InvalidOperationException(
                        "Type1 " + variant + " edge-union station failed.");
            }
            if (holeChain == null || holeChain.Points.Count != 3
                || vertical == null || vertical.Points.Count != 2
                || vertical.Points[0].Y <= vertical.Points[1].Y
                || vertical.Direction.X >= -DirectionCosineTolerance)
                throw new InvalidOperationException(
                    "Type1 " + variant + " plate-hole/vertical order failed.");
        }

        private static ViewData BuildSyntheticType1PlateSection(
            double mainMinX,
            double mainMaxX,
            double plateMinX,
            double plateMaxX,
            bool shuffle)
        {
            ViewData view = new ViewData();
            view.Identifier = 301;
            view.Scale = 15.0;
            view.DepthMin = -10.0;
            view.DepthMax = 10.0;
            PartData main = SyntheticRectanglePart(
                1,
                mainMinX,
                mainMaxX,
                -22.5,
                102.5,
                true,
                true,
                false,
                0.0);
            main.Reference.Add(new P3(
                (mainMinX + mainMaxX) * 0.5,
                102.5,
                -100.0));
            main.Reference.Add(new P3(
                (mainMinX + mainMaxX) * 0.5,
                102.5,
                100.0));
            double holeX = (plateMinX + plateMaxX) * 0.5;
            PartData plate = SyntheticRectanglePart(
                2,
                plateMinX,
                plateMaxX,
                102.5,
                202.5,
                false,
                true,
                true,
                0.0);
            plate.Bolts.Add(201, SyntheticBolt(
                201,
                18.0,
                new P3(holeX, 157.5, 0.0)));
            PartData hidden = SyntheticRectanglePart(
                3,
                plateMinX,
                plateMaxX,
                102.5,
                202.5,
                false,
                true,
                true,
                100.0);
            hidden.Bolts.Add(202, SyntheticBolt(
                202,
                18.0,
                new P3(holeX, 157.5, 100.0)));
            view.Main = main;
            view.Parts.Add(main);
            if (shuffle)
            {
                view.Parts.Add(plate);
                view.Parts.Add(hidden);
            }
            else
            {
                view.Parts.Add(hidden);
                view.Parts.Add(plate);
            }
            return view;
        }

        private static void ResetType1SectionResult()
        {
            LastType1SectionApplicable = false;
            LastType1SectionSucceeded = false;
            LastType1SectionViewCount = 0;
            LastType1SectionCreatedCount = 0;
            LastType1SectionReusedCount = 0;
            LastType1SectionConflictCount = 0;
            LastType1SectionMessage = String.Empty;
        }
    }
}
