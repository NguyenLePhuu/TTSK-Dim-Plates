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
    /// Shared H/C/L vertical-member layout policy.  It is project-independent:
    /// remember the user's two sheet positions before Shape, suppress legacy
    /// ViewType-based arranging, then preserve both X positions/order and align
    /// the RIGHT view Y to the LEFT view Y exactly once after Shape.
    /// </summary>
    public static class PHU_VerticalShapeViewLayoutContext
    {
        private const double Tolerance = 0.20;
        private const double DirectionCosineTolerance = 0.985;

        private sealed class Snapshot
        {
            public int Identifier;
            public TSD.View View;
            public TSG.Point Origin;
        }

        private sealed class Context
        {
            public TSD.Drawing Drawing;
            public Snapshot Left;
            public Snapshot Right;
        }

        private static Context _context;

        public static bool ShouldPreserveUserViewLayout
        {
            get { return _context != null; }
        }

        public static bool LastAlignmentApplied { get; private set; }
        public static string LastMessage { get; private set; }

        public static void Reset()
        {
            _context = null;
            LastAlignmentApplied = false;
            LastMessage = String.Empty;
        }

        /// <summary>
        /// Read-only.  A horizontal H/C/L main member does not match this
        /// topology and therefore keeps its existing company arrangement.
        /// </summary>
        public static bool Begin(TSM.Model model, TSD.Drawing drawing, TSM.Part mainPart)
        {
            Reset();
            try
            {
                if (
                    model == null
                    || drawing == null
                    || mainPart == null
                    || !model.GetConnectionStatus()
                )
                    return false;

                TSG.Matrix currentToGlobal = model
                    .GetWorkPlaneHandler()
                    .GetCurrentTransformationPlane()
                    .TransformationMatrixToGlobal;
                List<Snapshot> candidates = FindVerticalMainViews(
                    drawing,
                    mainPart,
                    currentToGlobal
                );
                if (candidates.Count != 2)
                {
                    LastMessage =
                        "Vertical H/C/L user-layout capture skipped: expected 2 full views, found "
                        + candidates.Count.ToString(CultureInfo.InvariantCulture)
                        + ".";
                    return false;
                }

                candidates.Sort(
                    delegate(Snapshot a, Snapshot b)
                    {
                        double dx = a.Origin.X - b.Origin.X;
                        if (Math.Abs(dx) > Tolerance)
                            return dx < 0.0 ? -1 : 1;
                        return a.Identifier.CompareTo(b.Identifier);
                    }
                );
                if (Math.Abs(candidates[0].Origin.X - candidates[1].Origin.X) <= Tolerance)
                {
                    LastMessage =
                        "Vertical H/C/L user-layout capture skipped: left/right X order is ambiguous.";
                    return false;
                }

                Context context = new Context();
                context.Drawing = drawing;
                context.Left = candidates[0];
                context.Right = candidates[1];
                _context = context;
                LastMessage =
                    "Vertical H/C/L user layout captured LEFT="
                    + context.Left.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ", RIGHT="
                    + context.Right.Identifier.ToString(CultureInfo.InvariantCulture)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                _context = null;
                LastMessage = "Vertical H/C/L user-layout capture skipped. " + ex.Message;
                return false;
            }
        }

        public static bool TryGetCapturedViewOrder(
            out int leftIdentifier,
            out int rightIdentifier,
            out TSG.Point leftOrigin,
            out TSG.Point rightOrigin
        )
        {
            leftIdentifier = 0;
            rightIdentifier = 0;
            leftOrigin = null;
            rightOrigin = null;
            if (_context == null || _context.Left == null || _context.Right == null)
                return false;
            leftIdentifier = _context.Left.Identifier;
            rightIdentifier = _context.Right.Identifier;
            leftOrigin = ClonePoint(_context.Left.Origin);
            rightOrigin = ClonePoint(_context.Right.Origin);
            return leftIdentifier != 0
                && rightIdentifier != 0
                && leftOrigin != null
                && rightOrigin != null;
        }

        public static bool AlignOnceAfterShape()
        {
            LastAlignmentApplied = false;
            if (_context == null)
                return false;

            try
            {
                RebindViews(_context);
                TSG.Point leftTarget = ClonePoint(_context.Left.Origin);
                TSG.Point rightTarget = new TSG.Point(
                    _context.Right.Origin.X,
                    _context.Left.Origin.Y,
                    _context.Right.Origin.Z
                );

                if (
                    !SetOrigin(_context.Left.View, leftTarget)
                    || !SetOrigin(_context.Right.View, rightTarget)
                )
                    throw new InvalidOperationException("Tekla rejected a captured view Origin.");
                _context.Drawing.CommitChanges();

                TSG.Point leftRead = _context.Left.View.Origin;
                TSG.Point rightRead = _context.Right.View.Origin;
                if (
                    Distance3(leftRead, leftTarget) > 0.05
                    || Distance3(rightRead, rightTarget) > 0.05
                    || rightRead.X <= leftRead.X + 0.05
                )
                    throw new InvalidOperationException("Origin read-back verification failed.");

                LastAlignmentApplied = true;
                LastMessage =
                    "Vertical H/C/L aligned once: original X/order preserved, RIGHT Y aligned to LEFT.";
                return true;
            }
            catch (Exception ex)
            {
                RestoreOriginalOrigins();
                TryCommit(_context.Drawing);
                LastMessage =
                    "Vertical H/C/L alignment failed; original origins restored. " + ex.Message;
                return false;
            }
        }

        private static List<Snapshot> FindVerticalMainViews(
            TSD.Drawing drawing,
            TSM.Part mainPart,
            TSG.Matrix currentToGlobal
        )
        {
            List<Snapshot> result = new List<Snapshot>();
            TSD.ContainerView sheet = drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                if (view == null || !ViewContainsPart(view, mainPart))
                    continue;
                TSG.Matrix globalToView = TSG.MatrixFactory.ToCoordinateSystem(
                    view.DisplayCoordinateSystem
                );
                if (!HasVerticalReferenceLine(mainPart, currentToGlobal, globalToView))
                    continue;
                int identifier = ReadIdentifier(view);
                TSG.Point origin = ClonePoint(view.Origin);
                if (identifier == 0 || origin == null)
                    continue;
                Snapshot snapshot = new Snapshot();
                snapshot.Identifier = identifier;
                snapshot.View = view;
                snapshot.Origin = origin;
                result.Add(snapshot);
            }
            return result;
        }

        private static bool HasVerticalReferenceLine(
            TSM.Part part,
            TSG.Matrix currentToGlobal,
            TSG.Matrix globalToView
        )
        {
            List<TSG.Point> points = new List<TSG.Point>();
            try
            {
                ArrayList line = part.GetReferenceLine(false);
                if (line != null)
                {
                    foreach (object value in line)
                    {
                        TSG.Point point = value as TSG.Point;
                        if (point == null)
                            continue;
                        TSG.Point global = currentToGlobal.Transform(point);
                        points.Add(globalToView.Transform(global));
                    }
                }
            }
            catch
            {
                return false;
            }

            double bestLength = 0.0;
            double bestVerticalCosine = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double dx = points[j].X - points[i].X;
                    double dy = points[j].Y - points[i].Y;
                    double length = Math.Sqrt((dx * dx) + (dy * dy));
                    if (length <= bestLength)
                        continue;
                    bestLength = length;
                    bestVerticalCosine = length <= Tolerance ? 0.0 : Math.Abs(dy / length);
                }
            }
            return bestLength > Tolerance && bestVerticalCosine >= DirectionCosineTolerance;
        }

        private static bool ViewContainsPart(TSD.View view, TSM.Part mainPart)
        {
            TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(TSD.Part));
            while (parts != null && parts.MoveNext())
            {
                TSD.Part part = parts.Current as TSD.Part;
                if (
                    part != null
                    && part.ModelIdentifier != null
                    && mainPart.Identifier != null
                    && part.ModelIdentifier.ID == mainPart.Identifier.ID
                )
                    return true;
            }
            return false;
        }

        private static void RebindViews(Context context)
        {
            TSD.ContainerView sheet = context.Drawing.GetSheet();
            TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
            TSD.View left = null;
            TSD.View right = null;
            while (views != null && views.MoveNext())
            {
                TSD.View view = views.Current as TSD.View;
                int identifier = ReadIdentifier(view);
                if (identifier == context.Left.Identifier)
                    left = view;
                if (identifier == context.Right.Identifier)
                    right = view;
            }
            if (left == null || right == null)
                throw new InvalidOperationException("A captured vertical Shape view disappeared.");
            context.Left.View = left;
            context.Right.View = right;
        }

        private static void RestoreOriginalOrigins()
        {
            if (_context == null)
                return;
            SetOrigin(_context.Left.View, ClonePoint(_context.Left.Origin));
            SetOrigin(_context.Right.View, ClonePoint(_context.Right.Origin));
        }

        private static bool SetOrigin(TSD.View view, TSG.Point origin)
        {
            if (view == null || origin == null)
                return false;
            if (Distance3(view.Origin, origin) <= 0.01)
                return true;
            view.Origin = ClonePoint(origin);
            return view.Modify();
        }

        private static int ReadIdentifier(object value)
        {
            try
            {
                PropertyInfo property =
                    value == null
                        ? null
                        : value
                            .GetType()
                            .GetProperty(
                                "Identifier",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                            );
                Tekla.Structures.Identifier identifier =
                    property == null
                        ? null
                        : property.GetValue(value, null) as Tekla.Structures.Identifier;
                return identifier == null ? 0 : identifier.ID;
            }
            catch
            {
                return 0;
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

        private static void TryCommit(TSD.Drawing drawing)
        {
            try
            {
                if (drawing != null)
                    drawing.CommitChanges();
            }
            catch { }
        }
    }
}
