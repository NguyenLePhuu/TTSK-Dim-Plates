using System;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using M = Tekla.Structures.Model;

public static partial class PHU_OpenGridView
{
    private static readonly Dictionary<string, AABB> FINAL_FIT_BOXES = new Dictionary<string, AABB>();
    private static readonly Dictionary<string, AABB> FIT_ENTRY_BOXES = new Dictionary<string, AABB>();

    private static void CaptureFitEntryBoxes(List<View> views)
    {
        FINAL_FIT_BOXES.Clear();
        FIT_ENTRY_BOXES.Clear();
        foreach (var view in views)
        {
            AABB entry;
            if (!TryGetRestrictionBoxClone(view, out entry)) throw new InvalidOperationException("Không đọc được biên trước Fit.");
            FIT_ENTRY_BOXES.Add(GetSessionViewKey(view), entry);
        }
    }

    private static void RememberFinalFitBoxes(Drawing drawing, Dictionary<View, AABB> boxes)
    {
        EnsureOpenGridSessionForDrawing(drawing);
        FINAL_FIT_BOXES.Clear();
        foreach (var item in boxes)
            FINAL_FIT_BOXES[GetSessionViewKey(item.Key)] = new AABB(new Point(item.Value.MinPoint), new Point(item.Value.MaxPoint));
    }

    // Attribute macros run after Fit in both the button and Open/Create/Fit combo.
    // Reapply the planned box if Tekla regenerates it while enabling neighboring parts.
    public static void VerifyFinalFitRestrictionBoxes()
    {
        var handler = new DrawingHandler();
        var drawing = handler.GetActiveDrawing();
        if (!handler.GetConnectionStatus() || drawing == null) throw new InvalidOperationException("Mất kết nối khi kiểm tra Fit.");
        EnsureOpenGridSessionForDrawing(drawing);
        var views = GetAllViewsInDrawing(drawing);
        int verified = 0;
        foreach (var view in views)
        {
            AABB expected;
            if (!FINAL_FIT_BOXES.TryGetValue(GetSessionViewKey(view), out expected)) continue;
            if (!view.Select()) throw new InvalidOperationException("Không đọc lại được view sau Fit.");
            if (!RestrictionBoxMatches(view, expected))
            {
                if (!TryApplyRestrictionBox(view, expected) || !drawing.CommitChanges() || !view.Select() || !RestrictionBoxMatches(view, expected))
                    throw new InvalidOperationException("Thuộc tính neighbor đã đổi restriction box; không khôi phục được biên Fit.");
            }
            verified++;
        }
        if (verified == 0 || verified != FINAL_FIT_BOXES.Count) throw new InvalidOperationException("Thiếu biên Fit để kiểm tra sau cập nhật neighbor.");
    }

    // Read-only geometry adapter. Cache is per Fit, never per drawing/session.
    // Solids stay in global coordinates until projected into the restriction-box plane.
    private sealed class FitGeometry
    {
        internal M.Part Part;
        internal readonly List<Point[]> Edges = new List<Point[]>();
        internal readonly List<List<Point[]>> Faces = new List<List<Point[]>>();
        internal Point[] Vertices;
        internal Point AxisStart, AxisEnd;
        internal AABB Bounds;
    }

    private sealed class FitEnvelopeContext
    {
        internal readonly M.Model Model;
        internal readonly Matrix ToGlobal;
        internal readonly Dictionary<int, FitGeometry> Geometry = new Dictionary<int, FitGeometry>();
        internal readonly Dictionary<int, M.Part> Core = new Dictionary<int, M.Part>();
        internal readonly Dictionary<int, M.Part> Neighbors = new Dictionary<int, M.Part>();
        internal readonly HashSet<int> RequiredNeighbors = new HashSet<int>();

        internal FitEnvelopeContext(Drawing drawing, M.Model model)
        {
            Model = model;
            if (model == null || !model.GetConnectionStatus())
                throw new InvalidOperationException("Không đọc được model để bảo vệ neighbor khi Fit.");
            ToGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var assemblyDrawing = drawing as AssemblyDrawing;
            var partDrawing = drawing as SinglePartDrawing;
            M.ModelObject root = assemblyDrawing != null
                ? model.SelectModelObject(assemblyDrawing.AssemblyIdentifier)
                : partDrawing != null ? model.SelectModelObject(partDrawing.PartIdentifier) : null;
            var assembly = root as M.Assembly;
            var main = root as M.Part ?? (assembly == null ? null : assembly.GetMainPart() as M.Part);
            if (main == null)
                throw new InvalidOperationException("Không xác định được main part của bản vẽ để Fit.");
            Add(Core, main);
            // Single-part drawings must not acquire the whole assembly.
            if (assemblyDrawing != null)
            {
                assembly = assembly ?? main.GetAssembly();
                foreach (var secondary in assembly.GetSecondaries()) Add(Core, secondary as M.Part);
            }

            // Exactly one topology hop from the drawing assembly. Do not flood-fill the building.
            var roots = Core.Values.ToArray();
            foreach (var part in roots)
                foreach (var connected in ConnectedParts(part))
                {
                    if (IsFitPlate(connected)) Add(Core, connected);
                    else AddNeighbor(connected, true);
                }
            // A bolted end plate belongs to the support assembly, not the drawing assembly.
            foreach (var plate in Core.Values.Where(IsFitPlate).ToArray())
            {
                var support = plate.GetAssembly().GetMainPart() as M.Part;
                if (support != null && !IsFitPlate(support)) AddNeighbor(support, true);
                foreach (var connected in ConnectedParts(plate))
                    if (!IsFitPlate(connected)) AddNeighbor(connected, true);
            }
            foreach (var id in Core.Keys) { Neighbors.Remove(id); RequiredNeighbors.Remove(id); }

            // Also find physical supports whose model has no weld/bolt topology (e.g. dummy members).
            // The selector is only a broad phase. Exact solid face contact below decides membership.
            var corePoints = Core.Values.SelectMany(p => Read(p).Vertices).ToArray();
            var workPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToLocal;
            var query = FitBounds(corePoints.Select(workPlane.Transform));
            const double contactTolerance = 2.0; // model fabrication/contact tolerance, mm
            var candidates = model.GetModelObjectSelector().GetObjectsByBoundingBox(
                new Point(query.MinPoint.X-contactTolerance, query.MinPoint.Y-contactTolerance, query.MinPoint.Z-contactTolerance),
                new Point(query.MaxPoint.X+contactTolerance, query.MaxPoint.Y+contactTolerance, query.MaxPoint.Z+contactTolerance));
            while (candidates.MoveNext())
            {
                var part = candidates.Current as M.Part;
                if (part == null || Core.ContainsKey(part.Identifier.ID) || Neighbors.ContainsKey(part.Identifier.ID) || IsFitPlate(part)) continue;
                var candidate = Read(part);
                if (Core.Values.Any(p => FitSolidsTouch(Read(p), candidate, contactTolerance)))
                    AddNeighbor(part, false);
            }
        }

        private void AddNeighbor(M.Part part, bool required)
        {
            Add(Neighbors, part);
            if (required && part != null) RequiredNeighbors.Add(part.Identifier.ID);
        }

        private static void Add(Dictionary<int, M.Part> parts, M.Part part)
        {
            if (part != null) parts[part.Identifier.ID] = part;
        }

        private static IEnumerable<M.Part> ConnectedParts(M.Part part)
        {
            var result = new Dictionary<int, M.Part>();
            var bolts = part.GetBolts();
            if (bolts == null) throw new InvalidOperationException("Không đọc được bolt của part " + part.Identifier.ID);
            while (bolts.MoveNext())
            {
                var bolt = bolts.Current as M.BoltGroup;
                if (bolt == null) continue;
                Add(result, bolt.PartToBoltTo); Add(result, bolt.PartToBeBolted);
                foreach (var other in bolt.OtherPartsToBolt) Add(result, other as M.Part);
            }
            var welds = part.GetWelds();
            if (welds == null) throw new InvalidOperationException("Không đọc được weld của part " + part.Identifier.ID);
            while (welds.MoveNext())
            {
                var weld = welds.Current as M.BaseWeld;
                if (weld == null) continue;
                Add(result, weld.MainObject as M.Part); Add(result, weld.SecondaryObject as M.Part);
            }
            result.Remove(part.Identifier.ID);
            return result.Values;
        }

        internal FitGeometry Read(M.Part part)
        {
            FitGeometry g;
            if (Geometry.TryGetValue(part.Identifier.ID, out g)) return g;
            g = new FitGeometry { Part = part };
            var solid = part.GetSolid();
            var edges = solid.GetEdgeEnumerator();
            while (edges.MoveNext())
            {
                var edge = edges.Current as Tekla.Structures.Solid.Edge;
                if (edge != null) g.Edges.Add(new[] { ToGlobal.Transform(edge.StartPoint), ToGlobal.Transform(edge.EndPoint) });
            }
            g.Vertices = g.Edges.SelectMany(e => e).ToArray();
            if (g.Vertices.Length == 0 || g.Vertices.Any(p => !FitFinitePoint(p)))
                throw new InvalidOperationException("Không đọc được solid của part " + part.Identifier.ID);
            g.Bounds = FitBounds(g.Vertices);
            var faces = solid.GetFaceEnumerator();
            while (faces.MoveNext())
            {
                var face = faces.Current as Tekla.Structures.Solid.Face;
                if (face == null) continue;
                var loops = face.GetLoopEnumerator();
                var polygons = new List<Point[]>();
                while (loops.MoveNext())
                {
                    var loop = loops.Current as Tekla.Structures.Solid.Loop;
                    if (loop == null) continue;
                    var vertices = loop.GetVertexEnumerator(); var points = new List<Point>();
                    while (vertices.MoveNext()) points.Add(ToGlobal.Transform(vertices.Current));
                    if (points.Count >= 3) polygons.Add(points.ToArray());
                }
                if (polygons.Count > 0) g.Faces.Add(polygons);
            }
            if (!IsFitPlate(part))
            {
                var axis = part.GetCenterLine(false).Cast<Point>().Select(ToGlobal.Transform).ToArray();
                // Curved/polyline neighbors require a different longitudinal clipping rule.
                if (axis.Length == 2 && Distance.PointToPoint(axis[0], axis[1]) > .01)
                { g.AxisStart = axis[0]; g.AxisEnd = axis[1]; }
            }
            Geometry.Add(part.Identifier.ID, g);
            return g;
        }
    }

    private static bool IsFitPlate(M.Part part)
    {
        string profile = part.Profile.ProfileString ?? "";
        return part is M.ContourPlate || part is M.BentPlate
            || profile.StartsWith("PL", StringComparison.OrdinalIgnoreCase);
    }

    // Finite local connection envelope, independent of the current expanded Open Grid box.
    // Core parts/plates are complete. Neighbor members retain their cross-section but are
    // clipped only along their own longitudinal axis to the connection neighborhood.
    private static AABB BuildFitNeighborEnvelope(FitEnvelopeContext context, View view, AABB original, double padding)
    {
        return BuildFitGeometryEnvelope(context, view, original, padding, true);
    }

    // An unopened view has no approved Open Grid floor. Recompute XY from geometry
    // on every run, preserving only depth; otherwise an oversized box can never shrink.
    private static AABB BuildFitGeometryEnvelope(FitEnvelopeContext context, View view, AABB original, double padding, bool preserveOriginal)
    {
        if (!IsFinite(padding) || padding < 0) throw new ArgumentException("Fit padding không hợp lệ.");
        var toView = MatrixFactory.ToCoordinateSystem(view.ViewCoordinateSystem);
        var fromView = MatrixFactory.FromCoordinateSystem(view.ViewCoordinateSystem);
        double scale = view.Attributes.Scale;
        if (!IsFinite(scale) || scale <= 0) throw new InvalidOperationException("View scale không hợp lệ.");
        double stubMargin = Math.Max(padding, 3.0 * scale); // 3 mm paper around the connection
        var protectedPoints = new List<Point>();
        foreach (var part in context.Core.Values)
        {
            var vertices = context.Read(part).Vertices.Select(toView.Transform).ToArray();
            if (!FitOverlapsDepth(vertices, original)) continue;
            // Explicit section views must retain their cut depth rather than exposing an entire member.
            if (IsSectionViewType(view))
                vertices = FitClipEdges(context.Read(part).Edges.Select(e => new[] { toView.Transform(e[0]), toView.Transform(e[1]) }),
                    new Vector(0,0,1), original.MinPoint.Z, original.MaxPoint.Z).ToArray();
            protectedPoints.AddRange(vertices);
        }
        if (protectedPoints.Count == 0) throw new InvalidOperationException("Không có hình học main part trong chiều sâu view.");
        // Freeze focus before visiting neighbors: traversal order cannot enlarge the next member.
        var focusGlobal = protectedPoints.Select(fromView.Transform).ToArray();
        foreach (var part in context.Neighbors.Values)
        {
            var g = context.Read(part);
            if (!FitOverlapsDepth(g.Vertices.Select(toView.Transform), original)) continue;
            if (g.AxisStart == null || g.AxisEnd == null)
                throw new InvalidOperationException("Neighbor cong/đa đoạn chưa xác định được biên an toàn: " + part.Identifier.ID);
            var direction = new Vector(g.AxisEnd.X-g.AxisStart.X, g.AxisEnd.Y-g.AxisStart.Y, g.AxisEnd.Z-g.AxisStart.Z);
            direction.Normalize();
            double start = focusGlobal.Min(p => FitDot(p, direction)) - stubMargin;
            double end = focusGlobal.Max(p => FitDot(p, direction)) + stubMargin;
            var points = FitClipEdges(g.Edges, direction, start, end).Select(toView.Transform).ToArray();
            if (points.Length == 0)
            {
                if (context.RequiredNeighbors.Contains(part.Identifier.ID))
                    throw new InvalidOperationException("Không tìm thấy đoạn neighbor tại mối nối: " + part.Identifier.ID);
                continue;
            }
            protectedPoints.AddRange(points);
        }
        var bounds = FitBounds(protectedPoints);
        return FinishFitGeometryEnvelope(original, bounds, padding, preserveOriginal);
    }

    private static AABB FinishFitGeometryEnvelope(AABB original, AABB bounds, double padding, bool preserveOriginal)
    {
        var baseline = preserveOriginal ? original : new AABB(
            new Point(bounds.MinPoint.X, bounds.MinPoint.Y, original.MinPoint.Z),
            new Point(bounds.MaxPoint.X, bounds.MaxPoint.Y, original.MaxPoint.Z));
        return MergeFitEnvelope(baseline, bounds, padding, false, false, false, false);
    }

    private static bool HasOpenGridFitSnapshot(Drawing drawing, View view)
    {
        string key = GetSessionViewKey(view);
        return OPEN_GRID_READY_VIEW_KEYS.Contains(GetOpenGridReadyViewKey(drawing, view))
            && FIT_ORIGINAL_SHEET_BOXES.ContainsKey(key)
            && FIT_ORIGINAL_RESTRICTION_BOXES.ContainsKey(key);
    }

    private static AABB PlanFitTarget(Drawing drawing, FitEnvelopeContext context, View view, double padding)
    {
        AABB source;
        bool opened = HasOpenGridFitSnapshot(drawing, view);
        if (opened ? !TryGetFitOriginalRestrictionBox(view, out source) : !TryGetRestrictionBoxClone(view, out source))
            throw new InvalidOperationException("Không đọc được biên view để Fit.");
        return BuildFitGeometryEnvelope(context, view, source, padding, opened);
    }

    // Each grid-owned side is immutable. Called after legacy grid selection has finished.
    private static AABB MergeFitEnvelope(AABB baseline, AABB envelope, double padding,
        bool lockLeft, bool lockRight, bool lockBottom, bool lockTop)
    {
        if (baseline == null || envelope == null || !FitFinitePoint(baseline.MinPoint) || !FitFinitePoint(baseline.MaxPoint)
            || !FitFinitePoint(envelope.MinPoint) || !FitFinitePoint(envelope.MaxPoint) || !IsFinite(padding) || padding < 0)
            throw new InvalidOperationException("Restriction box không hợp lệ.");
        var min = new Point(lockLeft ? baseline.MinPoint.X : Math.Min(baseline.MinPoint.X, envelope.MinPoint.X-padding),
            lockBottom ? baseline.MinPoint.Y : Math.Min(baseline.MinPoint.Y, envelope.MinPoint.Y-padding), baseline.MinPoint.Z);
        var max = new Point(lockRight ? baseline.MaxPoint.X : Math.Max(baseline.MaxPoint.X, envelope.MaxPoint.X+padding),
            lockTop ? baseline.MaxPoint.Y : Math.Max(baseline.MaxPoint.Y, envelope.MaxPoint.Y+padding), baseline.MaxPoint.Z);
        if (max.X <= min.X || max.Y <= min.Y || max.Z < min.Z) throw new InvalidOperationException("Restriction box rỗng.");
        return new AABB(min,max);
    }

    private static IEnumerable<Point> FitClipEdges(IEnumerable<Point[]> edges, Vector direction, double min, double max)
    {
        foreach (var edge in edges)
        {
            double a = FitDot(edge[0],direction), b = FitDot(edge[1],direction), delta = b-a;
            double lo = 0, hi = 1;
            if (Math.Abs(delta) < 1e-9) { if (a < min || a > max) continue; }
            else { double t1=(min-a)/delta, t2=(max-a)/delta; lo=Math.Max(0,Math.Min(t1,t2)); hi=Math.Min(1,Math.Max(t1,t2)); if(lo>hi)continue; }
            yield return FitLerp(edge[0],edge[1],lo);
            yield return FitLerp(edge[0],edge[1],hi);
        }
    }

    private static bool FitSolidsTouch(FitGeometry a, FitGeometry b, double tolerance)
    {
        if (a.Bounds.MaxPoint.X+tolerance < b.Bounds.MinPoint.X || b.Bounds.MaxPoint.X+tolerance < a.Bounds.MinPoint.X
            || a.Bounds.MaxPoint.Y+tolerance < b.Bounds.MinPoint.Y || b.Bounds.MaxPoint.Y+tolerance < a.Bounds.MinPoint.Y
            || a.Bounds.MaxPoint.Z+tolerance < b.Bounds.MinPoint.Z || b.Bounds.MaxPoint.Z+tolerance < a.Bounds.MinPoint.Z) return false;
        return a.Vertices.Any(p => b.Faces.Any(f => FitPointOnFace(p,f,tolerance)))
            || b.Vertices.Any(p => a.Faces.Any(f => FitPointOnFace(p,f,tolerance)));
    }

    // Project onto the actual face plane, then use even/odd polygon containment including holes.
    // AABB overlap alone is never a connection: it includes empty space between flanges.
    private static bool FitPointOnFace(Point point, List<Point[]> loops, double tolerance)
    {
        var outer = loops[0]; var origin=outer[0]; Vector normal=null;
        for(int i=1;i+1<outer.Length;i++)
        {
            var a=new Vector(outer[i].X-origin.X,outer[i].Y-origin.Y,outer[i].Z-origin.Z);
            var b=new Vector(outer[i+1].X-origin.X,outer[i+1].Y-origin.Y,outer[i+1].Z-origin.Z);
            var n=Vector.Cross(a,b); if(n.GetLength()>.000001){n.Normalize();normal=n;break;}
        }
        if(normal==null || Math.Abs(FitDot(point,normal)-FitDot(origin,normal))>tolerance) return false;
        int drop=Math.Abs(normal.X)>Math.Abs(normal.Y) ? 0:1;
        if(Math.Abs(normal.Z)>(drop==0?Math.Abs(normal.X):Math.Abs(normal.Y))) drop=2;
        Func<Point,double> x=p=>drop==0?p.Y:p.X;
        Func<Point,double> y=p=>drop==2?p.Y:p.Z;
        double px=x(point),py=y(point); bool inside=false;
        foreach(var loop in loops)
            for(int i=0,j=loop.Length-1;i<loop.Length;j=i++)
            {
                double ax=x(loop[j]),ay=y(loop[j]),bx=x(loop[i]),by=y(loop[i]);
                double dx=bx-ax,dy=by-ay,len=dx*dx+dy*dy;
                double t=len<1e-12?0:Math.Max(0,Math.Min(1,((px-ax)*dx+(py-ay)*dy)/len));
                if((px-ax-t*dx)*(px-ax-t*dx)+(py-ay-t*dy)*(py-ay-t*dy)<=tolerance*tolerance) return true;
                if((ay>py)!=(by>py) && px<(bx-ax)*(py-ay)/(by-ay)+ax) inside=!inside;
            }
        return inside;
    }

    private static double FitDot(Point p, Vector d) { return p.X*d.X+p.Y*d.Y+p.Z*d.Z; }
    private static Point FitLerp(Point a, Point b, double t) { return new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t); }
    private static bool FitFinitePoint(Point p) { return p!=null && IsFinite(p.X) && IsFinite(p.Y) && IsFinite(p.Z); }
    private static bool FitOverlapsDepth(IEnumerable<Point> points, AABB box)
    {
        var p=points.ToArray(); return p.Length>0 && p.Min(x=>x.Z)<=box.MaxPoint.Z+.01 && p.Max(x=>x.Z)>=box.MinPoint.Z-.01;
    }
    private static AABB FitBounds(IEnumerable<Point> points)
    {
        var p=points.ToArray();
        if(p.Length==0 || p.Any(x=>!FitFinitePoint(x))) throw new InvalidOperationException("Biên hình học không hợp lệ.");
        return new AABB(new Point(p.Min(x=>x.X),p.Min(x=>x.Y),p.Min(x=>x.Z)),new Point(p.Max(x=>x.X),p.Max(x=>x.Y),p.Max(x=>x.Z)));
    }
}
