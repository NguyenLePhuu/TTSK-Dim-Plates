#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

using TSD = Tekla.Structures.Drawing;
using TSG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// A short-lived, geometry-proven route for the Inzai Data Center beam
    /// Type-2 drawing.  Tekla may persist the second longitudinal projection
    /// as either FrontView or SectionView even when the model/view geometry is
    /// equivalent, so the runtime ViewType is deliberately not a route key.
    ///
    /// The route is intentionally fail-closed.  It is active only when two
    /// longitudinal projections of the authoritative MainPart, the expected
    /// perpendicular/parallel Grid orientations, at least one true cross
    /// section, and a same-assembly transverse member are all proven.  When
    /// any geometric proof is missing the established Slot09 Type-1 path is
    /// untouched.
    /// </summary>
    public static class PHU_Slot09_DataCenterBeamType2Context
    {
        private const double GeometryTolerance = 0.75;
        private const double DirectionCosineTolerance = 0.985;
        private const double LongitudinalAspectMinimum = 3.0;

        private sealed class ViewCandidate
        {
            public TSD.View View;
            public int Identifier;
            public P2 MainDirection;
            public P2 UpDirection;
            public P2 RefLeft;
            public P2 RefRight;
            public double RefLength;
            public Bounds2 Bounds;
            public int PerpendicularGridCount;
            public int ParallelGridCount;
            public bool IsSection;
            public bool HasSameAssemblyTransverse;
            public double TransverseStation;
            public string TransverseDiagnostic;
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

        private sealed class Scope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                End();
            }
        }

        [ThreadStatic]
        private static int _depth;

        [ThreadStatic]
        private static TSD.View _topView;

        [ThreadStatic]
        private static TSD.View _frontView;

        [ThreadStatic]
        private static List<TSD.View> _sectionViews;

        [ThreadStatic]
        private static double _frontTransverseStation;

        [ThreadStatic]
        private static string _message;

        [ThreadStatic]
        private static int _frontTransverseDimensionTier;

        [ThreadStatic]
        private static bool _hasFrontTransverseDimensionTier;

        public static bool IsActive
        {
            get { return _depth > 0 && _topView != null && _frontView != null; }
        }

        /// <summary>
        /// Type-2 drawings arrive with their view origins, restriction boxes,
        /// and visible Grid axes already approved.  Consumers use this scoped
        /// flag to run only DIM creation plus the owned TOP/FRONT REF lines;
        /// Open Grid, Fit Grid, view arrangement, and Neighbor Grid marking
        /// remain enabled for the established Type-1 route.
        /// </summary>
        public static bool PreservePreparedDrawingLayout
        {
            get { return IsActive; }
        }

        public static string AuditExecutionPolicy()
        {
            bool preserve = PreservePreparedDrawingLayout;
            return "SLOT09 BEAM TYPE2 EXECUTION POLICY: active="
                + IsActive.ToString(CultureInfo.InvariantCulture)
                + ", DIM=True, REF-lines=True, OpenGrid="
                + (!preserve).ToString(CultureInfo.InvariantCulture)
                + ", FitGrid=" + (!preserve).ToString(CultureInfo.InvariantCulture)
                + ", ArrangeViews=" + (!preserve).ToString(CultureInfo.InvariantCulture)
                + ", NeighborGridMark=" + (!preserve).ToString(CultureInfo.InvariantCulture)
                + ".";
        }

        public static TSD.View TopView
        {
            get { return IsActive ? _topView : null; }
        }

        public static TSD.View FrontView
        {
            get { return IsActive ? _frontView : null; }
        }

        public static string LastMessage
        {
            get { return _message ?? String.Empty; }
        }

        public static IList<TSD.View> SectionViews
        {
            get
            {
                return IsActive && _sectionViews != null
                    ? new List<TSD.View>(_sectionViews)
                    : new List<TSD.View>();
            }
        }

        /// <summary>
        /// Read-only topology preflight followed by a scoped activation.  No
        /// Drawing object is modified and no CommitChanges call is made.
        /// </summary>
        public static IDisposable TryBeginCurrentDrawing(out bool active, out string message)
        {
            active = false;
            message = String.Empty;

            if (_depth > 0)
            {
                _depth++;
                active = IsActive;
                message = LastMessage;
                return new Scope();
            }

            TSD.View top;
            TSD.View front;
            List<TSD.View> sections;
            double transverseStation;
            if (!TryResolveCurrentDrawing(
                    out top,
                    out front,
                    out sections,
                    out transverseStation,
                    out message))
            {
                _message = message;
                return null;
            }

            _depth = 1;
            _topView = top;
            _frontView = front;
            _sectionViews = sections;
            _frontTransverseStation = transverseStation;
            _frontTransverseDimensionTier = 0;
            _hasFrontTransverseDimensionTier = false;
            _message = message;
            active = true;
            return new Scope();
        }

        public static bool IsTopView(TSD.View view)
        {
            return IsActive && SameView(_topView, view);
        }

        public static bool IsFrontView(TSD.View view)
        {
            return IsActive && SameView(_frontView, view);
        }

        public static bool IsMainView(TSD.View view)
        {
            return IsTopView(view) || IsFrontView(view);
        }

        public static bool TryGetFrontTransverseStation(out double station)
        {
            station = IsActive ? _frontTransverseStation : Double.NaN;
            return IsActive && IsFinite(station);
        }

        /// <summary>
        /// BeamGrid calls this with Shape H's exact next-free horizontal tier.
        /// The supplemental transverse chain then occupies that tier and
        /// BeamGrid places Slot04/Grid strictly outside it.
        /// </summary>
        public static void RegisterFrontTransverseDimensionTier(
            TSD.View view,
            int tier)
        {
            if (!IsActive || !IsFrontView(view) || tier < 0)
                return;
            _frontTransverseDimensionTier = tier;
            _hasFrontTransverseDimensionTier = true;
        }

        public static bool TryGetFrontTransverseDimensionTier(
            TSD.View view,
            out int tier)
        {
            tier = _frontTransverseDimensionTier;
            return IsActive && IsFrontView(view)
                && _hasFrontTransverseDimensionTier && tier >= 0;
        }

        public static bool IsSectionView(TSD.View view)
        {
            if (!IsActive || view == null || _sectionViews == null)
                return false;
            for (int i = 0; i < _sectionViews.Count; i++)
            {
                if (SameView(_sectionViews[i], view))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Type-2-only Slot04 guard.  The vertical plate at the proven
        /// transverse-member station belongs to the local edge/REF relation;
        /// it must not be inserted into the global top-plate chain.
        /// Inputs are already normalized by Slot04 to X=beam longitudinal.
        /// </summary>
        public static bool ShouldExcludeFrontSlot04Target(
            double targetMinX,
            double targetMaxX
        )
        {
            if (!IsActive || !IsFinite(_frontTransverseStation))
                return false;

            double minX = Math.Min(targetMinX, targetMaxX);
            double maxX = Math.Max(targetMinX, targetMaxX);
            // The local GP plate terminates a few millimetres before the
            // transverse REF (4 mm in the approved sample), so interval
            // containment alone is too strict.  Twenty-five model units is a
            // small contact-zone test relative to the H-beam width and remains
            // far below the independent plate stations.
            double tolerance = Math.Max(25.0, (maxX - minX) * 0.5);
            return _frontTransverseStation >= minX - tolerance
                && _frontTransverseStation <= maxX + tolerance;
        }

        /// <summary>Pure diagnostic string; no model or drawing mutation.</summary>
        public static string AuditCurrentDrawingRoute()
        {
            TSD.View top;
            TSD.View front;
            List<TSD.View> sections;
            double station;
            string message;
            bool supported = TryResolveCurrentDrawing(
                out top,
                out front,
                out sections,
                out station,
                out message);

            return "SLOT09 DATA CENTER BEAM TYPE2 ROUTE - READ ONLY\r\n"
                + "Supported=" + supported.ToString(CultureInfo.InvariantCulture) + "\r\n"
                + "TopView=" + ViewText(top) + "\r\n"
                + "FrontView=" + ViewText(front) + "\r\n"
                + "SectionCount=" + (sections == null ? 0 : sections.Count)
                    .ToString(CultureInfo.InvariantCulture) + "\r\n"
                + "FrontTransverseStation=" + Format(station) + "\r\n"
                + "Message=" + message;
        }

        private static void End()
        {
            if (_depth <= 0)
                return;
            _depth--;
            if (_depth > 0)
                return;

            _topView = null;
            _frontView = null;
            _sectionViews = null;
            _frontTransverseStation = Double.NaN;
            _frontTransverseDimensionTier = 0;
            _hasFrontTransverseDimensionTier = false;
        }

        private static bool TryResolveCurrentDrawing(
            out TSD.View top,
            out TSD.View front,
            out List<TSD.View> sections,
            out double transverseStation,
            out string message
        )
        {
            top = null;
            front = null;
            sections = new List<TSD.View>();
            transverseStation = Double.NaN;
            message = String.Empty;

            try
            {
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
                    message = "Type2 requires an AssemblyDrawing.";
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
                    message = "Project route is not Inzai Data Center. " + routeMessage;
                    return false;
                }

                TSM.Part mainPart = PHU_MainPartResolver.Resolve(model, drawing);
                if (mainPart == null || mainPart.Identifier == null)
                {
                    message = "Assembly MainPart could not be resolved.";
                    return false;
                }

                TSM.TransformationPlane currentPlane = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane();
                TSG.Matrix currentToGlobal = currentPlane.TransformationMatrixToGlobal;
                List<ViewCandidate> longitudinal = new List<ViewCandidate>();
                List<ViewCandidate> cross = new List<ViewCandidate>();

                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null || !ViewContainsPart(view, mainPart.Identifier))
                        continue;

                    ViewCandidate candidate = ReadCandidate(
                        model,
                        mainPart,
                        view,
                        currentToGlobal);
                    if (candidate == null)
                        continue;

                    double longSide = Math.Max(candidate.Bounds.Width, candidate.Bounds.Height);
                    double shortSide = Math.Min(candidate.Bounds.Width, candidate.Bounds.Height);
                    bool isLongitudinal = candidate.RefLength > 500.0
                        && shortSide > GeometryTolerance
                        && longSide >= shortSide * LongitudinalAspectMinimum
                        && candidate.RefLength >= shortSide * LongitudinalAspectMinimum;

                    if (isLongitudinal)
                        longitudinal.Add(candidate);
                    else
                        cross.Add(candidate);
                }

                if (longitudinal.Count != 2)
                {
                    message = "Type2 requires exactly two proven longitudinal MainPart views; found "
                        + longitudinal.Count.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                ViewCandidate topCandidate = null;
                ViewCandidate frontCandidate = null;
                for (int i = 0; i < longitudinal.Count; i++)
                {
                    ViewCandidate candidate = longitudinal[i];
                    if (candidate.PerpendicularGridCount > 0
                        && candidate.ParallelGridCount == 0)
                    {
                        if (topCandidate != null)
                        {
                            message = "Type2 Top role is ambiguous.";
                            return false;
                        }
                        topCandidate = candidate;
                    }
                    if (candidate.ParallelGridCount > 0
                        && candidate.PerpendicularGridCount == 0)
                    {
                        if (frontCandidate != null)
                        {
                            message = "Type2 Front role is ambiguous.";
                            return false;
                        }
                        frontCandidate = candidate;
                    }
                }

                if (topCandidate == null || frontCandidate == null
                    || SameView(topCandidate.View, frontCandidate.View))
                {
                    message = "The perpendicular/parallel Grid pair did not prove unique Top/Front roles.";
                    return false;
                }

                // Do not classify this topology by Tekla's runtime ViewType.
                // Equivalent approved drawings have been observed with the
                // semantic Front persisted as either SectionView or FrontView.
                // Its role is already proven above by the MainPart projection
                // and the unique parallel-Grid relation; the remaining Type-2
                // evidence below stays fail-closed.

                if (!topCandidate.HasSameAssemblyTransverse)
                {
                    message = "No same-assembly transverse member was proven in the Top view. "
                        + (topCandidate.TransverseDiagnostic ?? String.Empty);
                    return false;
                }

                for (int i = 0; i < cross.Count; i++)
                {
                    if (cross[i].IsSection)
                        sections.Add(cross[i].View);
                }
                if (sections.Count == 0)
                {
                    message = "No true MainPart cross-section view was proven.";
                    return false;
                }

                top = topCandidate.View;
                front = frontCandidate.View;
                transverseStation = topCandidate.TransverseStation;
                message = "Type2 proven independent of runtime ViewType by MainPart REF/solid spans, orthogonal Grid roles, "
                    + "same-assembly transverse member, and "
                    + sections.Count.ToString(CultureInfo.InvariantCulture)
                    + " cross-section view(s).";
                return true;
            }
            catch (Exception ex)
            {
                message = "Type2 route analysis failed: " + ex.Message;
                return false;
            }
        }

        private static ViewCandidate ReadCandidate(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal
        )
        {
            TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                view.DisplayCoordinateSystem);
            List<P2> reference = ReadReference(mainPart, currentToGlobal, globalToView);
            if (reference.Count == 0)
                return null;

            Bounds2 mainBounds = ReadSolidBounds(mainPart, currentToGlobal, globalToView);
            if (!mainBounds.IsValid)
                return null;

            P2 first;
            P2 second;
            P2 direction;
            if (reference.Count >= 2)
            {
                FindFarthestPair(reference, out first, out second);
                direction = Normalize(Subtract(second, first));
            }
            else
            {
                first = second = reference[0];
                direction = mainBounds.Width >= mainBounds.Height
                    ? new P2(1.0, 0.0)
                    : new P2(0.0, 1.0);
            }
            if (direction == null)
                return null;
            if (direction.X < -GeometryTolerance
                || (Math.Abs(direction.X) <= GeometryTolerance && direction.Y < 0.0))
                direction = Scale(direction, -1.0);

            ViewCandidate result = new ViewCandidate();
            result.View = view;
            result.Identifier = ReadIdentifier(view);
            result.MainDirection = direction;
            result.UpDirection = Normalize(new P2(-direction.Y, direction.X));
            if (result.UpDirection == null)
                return null;
            if (result.UpDirection.Y < 0.0)
                result.UpDirection = Scale(result.UpDirection, -1.0);
            result.RefLeft = Extreme(reference, direction, false);
            result.RefRight = Extreme(reference, direction, true);
            result.RefLength = Dot(Subtract(result.RefRight, result.RefLeft), direction);
            result.Bounds = mainBounds;
            result.IsSection = IsRuntimeSectionView(view);
            ReadGridRoles(view, result);
            ResolveSameAssemblyTransverse(
                model,
                mainPart,
                view,
                currentToGlobal,
                globalToView,
                result);
            return result.Bounds.IsValid ? result : null;
        }

        private static void ReadGridRoles(TSD.View view, ViewCandidate result)
        {
            try
            {
                TSD.DrawingObjectEnumerator grids = view.GetAllObjects(typeof(TSD.GridLine));
                while (grids != null && grids.MoveNext())
                {
                    TSD.GridLine line = grids.Current as TSD.GridLine;
                    if (line == null || line.StartLabel == null || line.EndLabel == null)
                        continue;
                    TSG.Point start = line.StartLabel.GridPoint;
                    TSG.Point end = line.EndLabel.GridPoint;
                    if (start == null || end == null)
                        continue;
                    P2 gridDirection = Normalize(new P2(end.X - start.X, end.Y - start.Y));
                    if (gridDirection == null)
                        continue;
                    double alongMain = Math.Abs(Dot(gridDirection, result.MainDirection));
                    double alongUp = Math.Abs(Dot(gridDirection, result.UpDirection));
                    if (alongUp >= DirectionCosineTolerance && alongUp > alongMain)
                        result.PerpendicularGridCount++;
                    else if (alongMain >= DirectionCosineTolerance && alongMain > alongUp)
                        result.ParallelGridCount++;
                }
            }
            catch { }
        }

        private static void ResolveSameAssemblyTransverse(
            TSM.Model model,
            TSM.Part mainPart,
            TSD.View view,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView,
            ViewCandidate result
        )
        {
            try
            {
                TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
                bool found = false;
                double resolvedStation = Double.NaN;
                int total = 0;
                int sameAssembly = 0;
                int longEnough = 0;
                int perpendicular = 0;
                int intersecting = 0;
                int interior = 0;
                while (parts != null && parts.MoveNext())
                {
                    total++;
                    TSD.Part drawingPart = parts.Current as TSD.Part;
                    if (drawingPart == null || drawingPart.ModelIdentifier == null
                        || SameIdentifier(drawingPart.ModelIdentifier, mainPart.Identifier))
                        continue;
                    TSM.Part candidate = model.SelectModelObject(
                        drawingPart.ModelIdentifier) as TSM.Part;
                    if (candidate == null || !SameMainAssembly(candidate, mainPart))
                        continue;
                    sameAssembly++;
                    List<P2> reference = ReadReference(
                        candidate,
                        currentToGlobal,
                        globalToView);
                    if (reference.Count < 2)
                        continue;
                    P2 a;
                    P2 b;
                    FindFarthestPair(reference, out a, out b);
                    double referenceLength = Distance(a, b);
                    double mainShortSide = Math.Min(
                        result.Bounds.Width,
                        result.Bounds.Height);
                    // Connection splice plates in this topology have long
                    // reference lines too (about twice the beam width).  A
                    // transverse H member is materially longer, so require a
                    // 2.5x structural span before treating it as the routing
                    // proof.  No NAME/profile/class string participates.
                    if (referenceLength < Math.Max(500.0, mainShortSide * 2.5))
                        continue;
                    longEnough++;
                    P2 candidateDirection = Normalize(Subtract(b, a));
                    if (candidateDirection == null
                        || Math.Abs(Dot(candidateDirection, result.UpDirection))
                            < DirectionCosineTolerance)
                        continue;
                    perpendicular++;
                    P2 intersection;
                    if (!TryLineIntersection(
                            result.RefLeft,
                            result.RefRight,
                            a,
                            b,
                            out intersection))
                        continue;
                    intersecting++;
                    double localStation = Dot(
                        Subtract(intersection, result.RefLeft),
                        result.MainDirection);
                    if (localStation <= GeometryTolerance
                        || localStation >= result.RefLength - GeometryTolerance)
                        continue;
                    interior++;
                    double station = Dot(intersection, result.MainDirection);
                    if (found && Math.Abs(station - resolvedStation) > GeometryTolerance)
                    {
                        result.HasSameAssemblyTransverse = false;
                        result.TransverseStation = Double.NaN;
                        return;
                    }
                    found = true;
                    resolvedStation = station;
                }
                result.HasSameAssemblyTransverse = found;
                result.TransverseStation = resolvedStation;
                result.TransverseDiagnostic = "candidates=" + total
                    + ", sameAssembly=" + sameAssembly
                    + ", longEnough=" + longEnough
                    + ", perpendicular=" + perpendicular
                    + ", intersecting=" + intersecting
                    + ", interior=" + interior + ".";
            }
            catch
            {
                result.HasSameAssemblyTransverse = false;
                result.TransverseStation = Double.NaN;
                result.TransverseDiagnostic = "transverse analysis threw.";
            }
        }

        private static List<P2> ReadReference(
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
                        if (point == null)
                            continue;
                        TSG.Point global = currentToGlobal.Transform(point);
                        TSG.Point local = globalToView.Transform(global);
                        AddUnique(result, new P2(local.X, local.Y));
                    }
                }
            }
            catch { }
            return result;
        }

        private static Bounds2 ReadSolidBounds(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            Bounds2 result = new Bounds2();
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
                    AddSolidPoint(result, edge.StartPoint, currentToGlobal, globalToView);
                    AddSolidPoint(result, edge.EndPoint, currentToGlobal, globalToView);
                }
            }
            catch { }
            return result;
        }

        private static void AddSolidPoint(
            Bounds2 bounds,
            TSG.Point point,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            if (point == null)
                return;
            TSG.Point global = currentToGlobal.Transform(point);
            TSG.Point local = globalToView.Transform(global);
            bounds.Add(new P2(local.X, local.Y));
        }

        private static bool ViewContainsPart(
            TSD.View view,
            Tekla.Structures.Identifier identifier
        )
        {
            try
            {
                TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
                while (parts != null && parts.MoveNext())
                {
                    TSD.Part part = parts.Current as TSD.Part;
                    if (part != null && part.ModelIdentifier != null
                        && SameIdentifier(part.ModelIdentifier, identifier))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool SameMainAssembly(TSM.Part first, TSM.Part main)
        {
            try
            {
                if (first == null || main == null)
                    return false;
                TSM.Assembly assembly = first.GetAssembly();
                TSM.Part assemblyMain = assembly == null
                    ? null
                    : assembly.GetMainPart() as TSM.Part;
                return assemblyMain != null
                    && SameIdentifier(assemblyMain.Identifier, main.Identifier);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLineIntersection(
            P2 a,
            P2 b,
            P2 c,
            P2 d,
            out P2 intersection
        )
        {
            intersection = null;
            P2 r = Subtract(b, a);
            P2 s = Subtract(d, c);
            double denominator = Cross(r, s);
            if (Math.Abs(denominator) <= 1.0e-9)
                return false;
            double t = Cross(Subtract(c, a), s) / denominator;
            intersection = new P2(a.X + t * r.X, a.Y + t * r.Y);
            return IsFinite(intersection.X) && IsFinite(intersection.Y);
        }

        private static bool IsRuntimeSectionView(TSD.View view)
        {
            try
            {
                string value = view == null ? String.Empty : view.ViewType.ToString();
                return value.IndexOf("Section", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SameView(TSD.View first, TSD.View second)
        {
            if (first == null || second == null)
                return false;
            if (Object.ReferenceEquals(first, second))
                return true;
            int firstId = ReadIdentifier(first);
            int secondId = ReadIdentifier(second);
            return firstId > 0 && firstId == secondId;
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                if (value == null)
                    return 0;
                Type type = value.GetType();
                PropertyInfo property = type.GetProperty(
                    "Identifier",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object raw = property == null ? null : property.GetValue(value, null);
                if (raw == null)
                {
                    FieldInfo field = type.GetField(
                        "Identifier",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    raw = field == null ? null : field.GetValue(value);
                }
                Tekla.Structures.Identifier identifier = raw
                    as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch
            {
                return 0;
            }
        }

        private static bool SameIdentifier(
            Tekla.Structures.Identifier first,
            Tekla.Structures.Identifier second
        )
        {
            return first != null && second != null && first.ID > 0 && first.ID == second.ID;
        }

        private static void AddUnique(List<P2> points, P2 point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Distance(points[i], point) <= GeometryTolerance)
                    return;
            }
            points.Add(point);
        }

        private static void FindFarthestPair(List<P2> points, out P2 first, out P2 second)
        {
            first = null;
            second = null;
            double best = -1.0;
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
        }

        private static P2 Extreme(List<P2> points, P2 axis, bool maximum)
        {
            P2 result = null;
            double best = maximum ? Double.NegativeInfinity : Double.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double value = Dot(points[i], axis);
                if ((maximum && value > best) || (!maximum && value < best))
                {
                    best = value;
                    result = points[i];
                }
            }
            return result;
        }

        private static P2 Normalize(P2 point)
        {
            if (point == null)
                return null;
            double length = Math.Sqrt(point.X * point.X + point.Y * point.Y);
            return length <= 1.0e-9 ? null : new P2(point.X / length, point.Y / length);
        }

        private static P2 Subtract(P2 first, P2 second)
        {
            return first == null || second == null
                ? null
                : new P2(first.X - second.X, first.Y - second.Y);
        }

        private static P2 Scale(P2 point, double value)
        {
            return point == null ? null : new P2(point.X * value, point.Y * value);
        }

        private static double Dot(P2 first, P2 second)
        {
            return first == null || second == null ? 0.0 : first.X * second.X + first.Y * second.Y;
        }

        private static double Cross(P2 first, P2 second)
        {
            return first == null || second == null ? 0.0 : first.X * second.Y - first.Y * second.X;
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

        private static string ViewText(TSD.View view)
        {
            if (view == null)
                return "<null>";
            string type = String.Empty;
            try { type = view.ViewType.ToString(); }
            catch { }
            return "id=" + ReadIdentifier(view).ToString(CultureInfo.InvariantCulture)
                + " type=" + type;
        }

        private static string Format(double value)
        {
            return IsFinite(value)
                ? Math.Round(value, 3).ToString(CultureInfo.InvariantCulture)
                : "<nan>";
        }
    }
}
