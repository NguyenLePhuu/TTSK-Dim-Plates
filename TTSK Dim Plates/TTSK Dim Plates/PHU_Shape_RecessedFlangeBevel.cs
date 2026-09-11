using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using ModelPart = Tekla.Structures.Model.Part;
using DrawingLine = Tekla.Structures.Drawing.Line;

namespace Tekla.Technology.Akit.UserScript
{
    public partial class ShapeScript
    {
        private const double RecessedBevelTolerance = 0.05;
        private const string RecessedBevelOwnerField = "TTSK_AUTODIM_OWNER";
        private const string RecessedBevelOwner = "H_RECESSED_FLANGE_BEVEL_V1";

        private sealed class RecessedFlangeException : InvalidOperationException
        {
            public RecessedFlangeException(string message) : base(message) { }
            public RecessedFlangeException(string message, Exception inner) : base(message,inner) { }
        }

        private sealed class RecessedBevelEdge
        {
            public Point A, B;
            public RecessedBevelEdge(Point a, Point b) { A = a; B = b; }
        }

        private sealed class RecessedFlangeBevel
        {
            public Point Outer, Shoulder, End, Corner;
            public Point GlobalShoulder, GlobalEnd;
            public bool Top, Right, TopFlange;
        }

        // Read-only adapter. Solid points are in the CURRENT model plane, then
        // normalized through global into the requested web-view coordinates.
        private static List<RecessedFlangeBevel> ReadRecessedFlangeBevels(
            Model model, ModelPart part, View view)
        {
            var empty = new List<RecessedFlangeBevel>();
            string profile = part.Profile.ProfileString.ToUpperInvariant();
            if (!(profile.StartsWith("H") || profile.StartsWith("BH") || profile.StartsWith("I")))
                return empty;
            double thickness = GetFlangeThicknessFromProfile(part);
            if (thickness <= RecessedBevelTolerance) return empty;
            Matrix toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane()
                .TransformationMatrixToGlobal;
            Matrix toView = MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            Func<Point, Point> project = p => toView.Transform(toGlobal.Transform(p));
            CoordinateSystem cs = part.GetCoordinateSystem();
            Point o = project(cs.Origin);
            Point x = project(new Point(cs.Origin.X + cs.AxisX.X,
                cs.Origin.Y + cs.AxisX.Y, cs.Origin.Z + cs.AxisX.Z));
            Point y = project(new Point(cs.Origin.X + cs.AxisY.X,
                cs.Origin.Y + cs.AxisY.Y, cs.Origin.Z + cs.AxisY.Z));
            // Only longitudinal, orthogonal web views; flange/end/skew views
            // must never enter this new rule even if their bounds look similar.
            if (Math.Abs(x.X-o.X) < 0.001 || Math.Abs(y.Y-o.Y) < 0.001
                || Math.Abs(x.Y-o.Y) + Math.Abs(x.Z-o.Z) > Math.Abs(x.X-o.X)*0.00001
                || Math.Abs(y.X-o.X) + Math.Abs(y.Z-o.Z) > Math.Abs(y.Y-o.Y)*0.00001)
                return empty;
            var edges = new List<RecessedBevelEdge>();
            var enumerator = part.GetSolid().GetEdgeEnumerator();
            while (enumerator.MoveNext())
            {
                var edge = enumerator.Current as Tekla.Structures.Solid.Edge;
                if (edge == null) continue;
                Point a = project(edge.StartPoint), b = project(edge.EndPoint);
                // No diagonal may be fabricated by projecting a depth edge.
                if (Math.Abs(a.Z-b.Z) > RecessedBevelTolerance) continue;
                a = Clone2D(a); b = Clone2D(b);
                if (Distance2D(a,b) <= RecessedBevelTolerance) continue;
                bool duplicate = edges.Exists(e => SameRecessedPoint(e.A,a) && SameRecessedPoint(e.B,b)
                    || SameRecessedPoint(e.A,b) && SameRecessedPoint(e.B,a));
                if (!duplicate) edges.Add(new RecessedBevelEdge(a,b));
            }
            var result = ResolveRecessedFlangeBevels(edges, thickness);
            Matrix fromView = MatrixFactory.FromCoordinateSystem(view.DisplayCoordinateSystem);
            foreach (var bevel in result)
            {
                bevel.GlobalShoulder = fromView.Transform(bevel.Shoulder);
                bevel.GlobalEnd = fromView.Transform(bevel.End);
                bevel.TopFlange = bevel.Top == (y.Y > o.Y);
            }
            return result;
        }

        private static bool SameRecessedPoint(Point a, Point b)
        {
            return Distance2D(a,b) <= RecessedBevelTolerance;
        }

        // Pure topology resolver: outer horizontal -> full-thickness diagonal
        // -> horizontal web projection -> vertical end joining the two flanges.
        // A square cope has no diagonal; an ordinary chamfer has no web projection.
        private static List<RecessedFlangeBevel> ResolveRecessedFlangeBevels(
            List<RecessedBevelEdge> edges, double thickness)
        {
            var result = new List<RecessedFlangeBevel>();
            if (edges == null || edges.Count < 6 || !IsFiniteRecessed(thickness)
                || thickness <= RecessedBevelTolerance) return result;
            double minX=double.MaxValue, maxX=double.MinValue;
            double minY=double.MaxValue, maxY=double.MinValue;
            foreach (var edge in edges)
                foreach (Point p in new[] {edge.A,edge.B})
                {
                    if (!IsValidDimOffsetAnchorPoint(p)) return result;
                    minX=Math.Min(minX,p.X); maxX=Math.Max(maxX,p.X);
                    minY=Math.Min(minY,p.Y); maxY=Math.Max(maxY,p.Y);
                }
            double tol = RecessedBevelTolerance;
            if (maxY-minY <= 2*thickness+tol) return result;
            foreach (bool right in new[] {false,true})
            {
                var pair = new List<RecessedFlangeBevel>();
                double endX = right ? maxX : minX;
                double sign = right ? 1 : -1;
                foreach (bool top in new[] {true,false})
                {
                    double outerY=top ? maxY : minY;
                    double innerY=outerY+(top ? -thickness : thickness);
                    var candidates = new List<RecessedFlangeBevel>();
                    foreach (var diagonal in edges)
                    {
                        Point outer=null, shoulder=null;
                        if (Math.Abs(diagonal.A.Y-outerY)<=tol && Math.Abs(diagonal.B.Y-innerY)<=tol)
                        { outer=diagonal.A; shoulder=diagonal.B; }
                        else if (Math.Abs(diagonal.B.Y-outerY)<=tol && Math.Abs(diagonal.A.Y-innerY)<=tol)
                        { outer=diagonal.B; shoulder=diagonal.A; }
                        if (outer==null || sign*(shoulder.X-outer.X)<=tol
                            || sign*(endX-shoulder.X)<=tol
                            || Math.Abs(endX-outer.X)>=(maxX-minX)*0.5) continue;
                        Point end = new Point(endX,innerY,0);
                        bool shelf = edges.Exists(e => SameRecessedPoint(e.A,shoulder) && SameRecessedPoint(e.B,end)
                            || SameRecessedPoint(e.B,shoulder) && SameRecessedPoint(e.A,end));
                        bool outerEdge = edges.Exists(e =>
                            SameRecessedPoint(e.A,outer) && Math.Abs(e.B.Y-outerY)<=tol && sign*(outer.X-e.B.X)>tol
                            || SameRecessedPoint(e.B,outer) && Math.Abs(e.A.Y-outerY)<=tol && sign*(outer.X-e.A.X)>tol);
                        if (!shelf || !outerEdge) continue;
                        if (!candidates.Exists(c => SameRecessedPoint(c.Outer,outer)
                            && SameRecessedPoint(c.Shoulder,shoulder)))
                            candidates.Add(new RecessedFlangeBevel {Outer=Clone2D(outer),
                                Shoulder=Clone2D(shoulder), End=end,
                                Corner=new Point(endX,outerY,0), Top=top, Right=right, TopFlange=top});
                    }
                    if (candidates.Count!=1) { pair.Clear(); break; }
                    pair.Add(candidates[0]);
                }
                if (pair.Count!=2) continue;
                bool webEnd = edges.Exists(e => SameRecessedPoint(e.A,pair[0].End) && SameRecessedPoint(e.B,pair[1].End)
                    || SameRecessedPoint(e.B,pair[0].End) && SameRecessedPoint(e.A,pair[1].End));
                if (webEnd) result.AddRange(pair);
            }
            // This first rule deliberately handles one complete paired end only.
            // A mixed cope at the other end belongs to another topology and must
            // not have its legacy details suppressed by this branch.
            if (result.Count != 2) return new List<RecessedFlangeBevel>();
            double oppositeX = result[0].Right ? minX : maxX;
            foreach (double outerY in new[] {minY,maxY})
                if (!edges.Exists(e => SameRecessedPoint(e.A,new Point(oppositeX,outerY,0))
                    || SameRecessedPoint(e.B,new Point(oppositeX,outerY,0))))
                    return new List<RecessedFlangeBevel>();
            return result;
        }

        private static bool IsFiniteRecessed(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        private static ChamferInfluence RecessedFlangeInfluence(List<RecessedFlangeBevel> bevels)
        {
            var influence = new ChamferInfluence {Any=true,Top=true,Bottom=true,RecessedBevels=bevels};
            foreach (var b in bevels) { if (b.Right) influence.Right=true; else influence.Left=true; }
            return influence;
        }

        // The top/bottom consumer uses the shoulder station, NOT the further
        // recessed outer tip of the diagonal. Feet are selected from actual solid
        // vertices in the consumer view, including the web edge beyond the flange.
        private static TopBottomFrontNotchChain BuildRecessedFlangeFaceChain(
            List<RecessedFlangeBevel> bevels, View view, Solid solid, bool top)
        {
            var points = GetProjectedSolidPointsForFrontNotchDims(solid);
            Matrix toView = MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            return ResolveRecessedFlangeFaceChain(bevels,points,toView,top);
        }

        private static TopBottomFrontNotchChain ResolveRecessedFlangeFaceChain(
            List<RecessedFlangeBevel> bevels,List<Point> points,Matrix toView,bool top)
        {
            var chain = new TopBottomFrontNotchChain();
            foreach (var bevel in bevels)
            {
                if (bevel.TopFlange!=top) continue;
                Point shoulder = toView.Transform(bevel.GlobalShoulder);
                Point end = toView.Transform(bevel.GlobalEnd);
                if (Math.Abs(end.X-shoulder.X)<=RecessedBevelTolerance)
                    throw new RecessedFlangeException("Recessed flange: face view has no longitudinal span.");
                Point inner=null, outer=null;
                foreach (Point p in points)
                {
                    if (Math.Abs(p.X-shoulder.X)<=RecessedBevelTolerance && (inner==null || p.Y>inner.Y)) inner=Clone2D(p);
                    if (Math.Abs(p.X-end.X)<=RecessedBevelTolerance && (outer==null || p.Y>outer.Y)) outer=Clone2D(p);
                }
                if (inner==null || outer==null)
                    throw new RecessedFlangeException("Recessed flange: missing exact face-chain vertex.");
                if (end.X>shoulder.X)
                { chain.HasRight=true; chain.RightInner=inner; chain.RightOuter=outer; }
                else { chain.HasLeft=true; chain.LeftInner=inner; chain.LeftOuter=outer; }
            }
            return chain.HasLeft || chain.HasRight ? chain : null;
        }

        private static int CreateRecessedFlangeBevelDetails(StraightDimensionSetHandler handler,
            View view, List<RecessedFlangeBevel> bevels, DimOffsetAnchor4 anchors)
        {
            var created = new List<DrawingObject>();
            var staleOwned = FindStaleRecessedBevelObjects(view,bevels);
            int dimensions=0;
            try
            {
                foreach (var b in bevels)
                {
                    var horizontal = new PointList {Clone2D(b.Shoulder),Clone2D(b.End)};
                    var vertical = b.Top
                        ? new PointList {Clone2D(b.End),Clone2D(b.Outer)}
                        : new PointList {Clone2D(b.Outer),Clone2D(b.End)};
                    var directions = new[] {new Vector(0,b.Top?1:-1,0),new Vector(b.Right?1:-1,0,0)};
                    var lists = new[] {horizontal,vertical};
                    for (int i=0;i<2;i++)
                    {
                        double distance=ResolveDimDistanceByAnchor4(lists[i],directions[i],anchors,GetSteelDimOffsetByTier(0));
                        var dim=handler.CreateDimensionSet(view,lists[i],directions[i],distance);
                        if (dim==null) throw new InvalidOperationException("Recessed flange: dimension creation failed.");
                        created.Add(dim); dimensions++;
                    }
                    Point ray = new Point(b.Shoulder.X,b.Shoulder.Y+(b.Top?1:-1)*Math.Abs(b.Outer.Y-b.Shoulder.Y)*3,0);
                    if (!HasRecessedBevelAngle(view,b.Shoulder,b.Outer,ray))
                    {
                        var attr=new AngleDimensionAttributes {Type=AngleTypes.AngleAtVertex};
                        var angle=new AngleDimension(view,Clone2D(b.Shoulder),
                            b.Top?Clone2D(b.Outer):ray,b.Top?ray:Clone2D(b.Outer),8.0,attr);
                        if (!angle.Insert()) throw new InvalidOperationException("Recessed flange: angle creation failed.");
                        created.Add(angle); dimensions++;
                        TagRecessedBevelObject(angle);
                    }
                    foreach (Point start in new[] {b.Outer,b.End})
                    {
                        if (HasEquivalentRecessedBevelLine(view,start,b.Corner)) continue;
                        DrawingLine line;
                        bool inserted=TTSK_AutoDim_Plates.PHU_LineDistance.InsertLineWithLineDistanceAttributes(
                            view,Clone2D(start),Clone2D(b.Corner),out line);
                        if (line!=null) created.Add(line);
                        if (!inserted || line==null) throw new InvalidOperationException("Recessed flange: extension line creation failed.");
                        TagRecessedBevelObject(line);
                        if (!TTSK_AutoDim_Plates.PHU_LineDistance.VerifyLineDistanceAttributes(line))
                            throw new InvalidOperationException("Recessed flange: line style read-back failed.");
                    }
                }
                // Only this rule's stale auxiliary objects may be removed.
                // Unowned/manual lines and angles are never cleanup targets.
                foreach(var obj in staleOwned)
                    if(!obj.Delete()) throw new InvalidOperationException("Recessed flange: owned-object cleanup failed.");
                return dimensions;
            }
            catch (Exception ex)
            {
                foreach (var obj in created) { try { obj.Delete(); } catch { } }
                throw new RecessedFlangeException("Recessed flange details rolled back: " + ex.Message,ex);
            }
        }

        private static void TagRecessedBevelObject(DrawingObject obj)
        {
            if (!((DatabaseObject)obj).SetUserProperty(RecessedBevelOwnerField,RecessedBevelOwner))
                throw new InvalidOperationException("Recessed flange: cannot record auxiliary-object ownership.");
        }

        private static List<DrawingObject> FindStaleRecessedBevelObjects(View view,List<RecessedFlangeBevel> bevels)
        {
            var result=new List<DrawingObject>();
            var objects=view.GetAllObjects();
            while(objects.MoveNext())
            {
                var obj=objects.Current as DrawingObject;
                var line=obj as DrawingLine;
                var angle=obj as AngleDimension;
                if(line==null && angle==null) continue;
                string owner="";
                if(!((DatabaseObject)obj).GetUserProperty(RecessedBevelOwnerField,ref owner)
                    || owner!=RecessedBevelOwner) continue;
                bool match=false;
                foreach(var b in bevels)
                {
                    if(line!=null)
                    {
                        foreach(Point start in new[] {b.Outer,b.End})
                            match |= SameRecessedPoint(line.StartPoint,start) && SameRecessedPoint(line.EndPoint,b.Corner)
                                || SameRecessedPoint(line.EndPoint,start) && SameRecessedPoint(line.StartPoint,b.Corner);
                    }
                    else
                    {
                        Point ray=new Point(b.Shoulder.X,b.Outer.Y,0);
                        match |= SameRecessedPoint(angle.Origin,b.Shoulder)
                            && (SameRecessedRay(b.Shoulder,angle.Point1,b.Outer) && SameRecessedRay(b.Shoulder,angle.Point2,ray)
                            || SameRecessedRay(b.Shoulder,angle.Point2,b.Outer) && SameRecessedRay(b.Shoulder,angle.Point1,ray));
                    }
                }
                if(!match) result.Add(obj);
            }
            return result;
        }

        private static bool HasEquivalentRecessedBevelLine(View view,Point a,Point b)
        {
            var objects=view.GetAllObjects(typeof(DrawingLine));
            while(objects.MoveNext())
            {
                var line=objects.Current as DrawingLine;
                if(line!=null && (SameRecessedPoint(line.StartPoint,a) && SameRecessedPoint(line.EndPoint,b)
                    || SameRecessedPoint(line.StartPoint,b) && SameRecessedPoint(line.EndPoint,a))) return true;
            }
            return false;
        }

        private static bool SameRecessedRay(Point origin,Point a,Point b)
        {
            double ax=a.X-origin.X,ay=a.Y-origin.Y,bx=b.X-origin.X,by=b.Y-origin.Y;
            double lengths=Math.Sqrt((ax*ax+ay*ay)*(bx*bx+by*by));
            return lengths>0.0001 && ax*bx+ay*by>0 && Math.Abs(ax*by-ay*bx)/lengths<0.0001;
        }

        private static bool HasRecessedBevelAngle(View view,Point origin,Point first,Point second)
        {
            var objects=view.GetAllObjects(typeof(AngleDimension));
            while(objects.MoveNext())
            {
                var a=objects.Current as AngleDimension;
                if(a!=null && SameRecessedPoint(a.Origin,origin)
                    && (SameRecessedRay(origin,a.Point1,first) && SameRecessedRay(origin,a.Point2,second)
                        || SameRecessedRay(origin,a.Point2,first) && SameRecessedRay(origin,a.Point1,second))) return true;
            }
            return false;
        }

        public static string AuditRecessedFlangeBevels()
        {
            var model=new Model();
            var drawing=new DrawingHandler().GetActiveDrawing();
            if (!(drawing is SinglePartDrawing)) return "Recessed flange: SinglePartDrawing only; legacy route.";
            ModelPart part=PHU_MainPartResolver.Resolve(model,drawing);
            if(part==null) return "Recessed flange: main part unresolved.";
            var text=new StringBuilder("RECESSED FLANGE BEVEL - READ ONLY\n");
            var approvedBevels=new List<RecessedFlangeBevel>();
            var faceViews=new List<View>();
            var views=drawing.GetSheet().GetAllViews();
            while(views.MoveNext())
            {
                View view=views.Current as View;
                if(view==null) continue;
                var bevels=ReadRecessedFlangeBevels(model,part,view);
                if (bevels.Count > 0) approvedBevels.AddRange(bevels);
                else faceViews.Add(view);
                text.AppendLine("View="+view.ViewType+"/"+view.Name+" features="+bevels.Count);
                foreach(var b in bevels)
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}/{1} Outer=({2:F3},{3:F3}) Shoulder=({4:F3},{5:F3}) End=({6:F3},{7:F3}) Corner=({8:F3},{9:F3}) width={10:F3} depth={11:F3} angle={12:F3}",
                        b.Right?"right":"left",b.Top?"top":"bottom",b.Outer.X,b.Outer.Y,b.Shoulder.X,b.Shoulder.Y,
                        b.End.X,b.End.Y,b.Corner.X,b.Corner.Y,Math.Abs(b.End.X-b.Shoulder.X),Math.Abs(b.Outer.Y-b.End.Y),
                        Math.Atan2(Math.Abs(b.Outer.X-b.Shoulder.X),Math.Abs(b.Outer.Y-b.Shoulder.Y))*180/Math.PI));
            }
            if (approvedBevels.Count==2)
            {
                foreach (var view in faceViews)
                {
                    Matrix transform=MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                    Point a=transform.Transform(approvedBevels[0].GlobalEnd);
                    Point b=transform.Transform(approvedBevels[0].GlobalShoulder);
                    if (Math.Abs(a.X-b.X)<RecessedBevelTolerance) continue;
                    var chain=ResolveRecessedFlangeFaceChain(approvedBevels,
                        ReadRecessedViewVertices(model,part,view),transform,true);
                    Point inner=chain.HasRight?chain.RightInner:chain.LeftInner;
                    Point outer=chain.HasRight?chain.RightOuter:chain.LeftOuter;
                    text.AppendLine("FACE "+view.ViewType+"/"+view.Name+" shoulder="+inner+" end="+outer
                        +" extension="+Math.Abs(outer.X-inner.X).ToString("F3",CultureInfo.InvariantCulture));
                }
            }
            return text.ToString();
        }

        private static List<Point> ReadRecessedViewVertices(Model model,ModelPart part,View view)
        {
            Matrix global=model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            Matrix local=MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            var points=new List<Point>();
            var edges=part.GetSolid().GetEdgeEnumerator();
            while(edges.MoveNext())
            {
                var edge=edges.Current as Tekla.Structures.Solid.Edge;
                if(edge==null) continue;
                foreach(Point p in new[] {edge.StartPoint,edge.EndPoint})
                    points.Add(Clone2D(local.Transform(global.Transform(p))));
            }
            return points;
        }

        // All geometry consumers are checked before the existing full-DIM command
        // deletes its background dimensions. No scale, plane or drawing mutation.
        private static void PreflightRecessedFlangeDrawing(Model model,ModelPart part,
            View front,View top,List<View> bottoms)
        {
            var bevels=ReadRecessedFlangeBevels(model,part,front);
            if(bevels.Count==0) return;
            ResolveRecessedFlangeFaceChain(bevels,ReadRecessedViewVertices(model,part,top),
                MatrixFactory.ToCoordinateSystem(top.DisplayCoordinateSystem),true);
            foreach(var bottom in bottoms)
                ResolveRecessedFlangeFaceChain(bevels,ReadRecessedViewVertices(model,part,bottom),
                    MatrixFactory.ToCoordinateSystem(bottom.DisplayCoordinateSystem),false);
        }
    }
}
