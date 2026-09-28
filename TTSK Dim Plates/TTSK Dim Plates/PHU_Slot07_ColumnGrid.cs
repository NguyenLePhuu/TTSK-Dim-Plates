using System;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;

namespace Tekla.Technology.Akit.UserScript
{
    internal static partial class PHU_Slot07_ColumnConnections
    {
        internal sealed class Grid
        {
            public G.Point Start, End;
        }

        private static void ReadGrids(View view)
        {
            var objects = view.Object.GetAllObjects(typeof(D.GridLine));
            while (objects.MoveNext())
            {
                var line = objects.Current as D.GridLine;
                // GridPoint is already in view coordinates, as in the legacy Grid reader.
                if (line?.StartLabel?.GridPoint == null || line.EndLabel?.GridPoint == null) continue;
                view.Grids.Add(new Grid { Start = line.StartLabel.GridPoint, End = line.EndLabel.GridPoint });
            }
        }

        internal static List<Plan> BuildColumnGrid(View view, List<Plan> connections)
        {
            var plans = new List<Plan>();
            var main = view.Main;
            if (main == null || main.Reference.Count != 2) throw new InvalidOperationException("Column REF unavailable.");
            double axis = main.Reference[0].X, depth = main.Reference[0].Z;
            // This adapter is for the existing upright X-X/Y-Y views only.
            if (Math.Abs(main.Reference[1].X-axis) > GeometryTolerance
                || Math.Abs(main.Reference[1].Z-depth) > GeometryTolerance)
                throw new InvalidOperationException("Column Grid cần hình chiếu cột đứng, không xiên.");
            var columns = view.Parts.Where(p => !p.Plate && p.Assembly == main.Assembly
                && IsHollowProfile(p.Profile)
                && p.Reference.Count == 2 && p.Vertices.Count > 0
                && p.Reference.All(r => Math.Abs(r.X-axis) <= GeometryTolerance && Math.Abs(r.Z-depth) <= GeometryTolerance))
                .OrderBy(p => p.Reference.Min(r=>r.Y)).ToList();
            if (!columns.Contains(main)) throw new InvalidOperationException("MainPart không thuộc chuỗi cột.");
            for (int i=1; i<columns.Count; i++)
                if (Math.Abs(columns[i-1].Reference.Max(r=>r.Y)-columns[i].Reference.Min(r=>r.Y)) > GeometryTolerance)
                    throw new InvalidOperationException("Các đoạn REF cột không nối tiếp duy nhất; dừng trước khi tạo DIM.");
            var nodes = Stations(columns.SelectMany(p=>p.Reference), new G.Vector(0,1,0));
            if (nodes.Count<2) throw new InvalidOperationException("Column REF has no span.");
            var plateFeet=ResolveColumnPlateFeet(view, columns, nodes, axis, depth);
            double bottom=plateFeet.First().Point.Y, top=plateFeet.Last().Point.Y;
            var bottomAxis=new G.Point(axis,bottom,0);
            var topPlate=plateFeet.Last().Point;
            var vertical = view.Grids.Where(g => Valid(g.Start) && Valid(g.End)
                && Math.Abs(g.End.X-g.Start.X)<=DimensionTolerance
                && Math.Min(g.Start.Y,g.End.Y)<top && Math.Max(g.Start.Y,g.End.Y)>bottom).ToList();
            var axes=Stations(vertical.Select(g=>g.Start),new G.Vector(1,0,0));
            axes=axes.OrderBy(x=>Math.Abs(x-axis)).ToList();
            if (axes.Count==0 || (axes.Count>1 && Math.Abs(Math.Abs(axes[0]-axis)-Math.Abs(axes[1]-axis))<=DimensionTolerance))
                throw new InvalidOperationException("Không có trục đứng gần cột duy nhất; không tự chọn trục mơ hồ.");
            var floors=Stations(view.Grids.Where(g => Valid(g.Start) && Valid(g.End)
                && Math.Abs(g.End.Y-g.Start.Y)<=DimensionTolerance
                && Math.Min(g.Start.X,g.End.X)<=axis && Math.Max(g.Start.X,g.End.X)>=axis)
                .Select(g=>g.Start),new G.Vector(0,1,0));
            if (floors.Count!=2) throw new InvalidOperationException("Column Grid cần hai cao độ tầng rõ ràng; có " + floors.Count + ".");

            var left=new G.Vector(-1,0,0); var up=new G.Vector(0,1,0);
            Add(plans,view,"COLUMN-PLATE-CHAIN",String.Join(" -> ",plateFeet.Select(f=>f.Evidence)),left,10,plateFeet.Select(f=>f.Point).ToArray());
            Add(plans,view,"COLUMN-PLATE-TOTAL","base underside on column axis -> " + plateFeet.Last().Evidence,left,20,bottomAxis,topPlate);
            var levels=Stations(new[] { new G.Point(0,floors[0],0),new G.Point(0,floors[1],0),new G.Point(0,bottom,0),new G.Point(0,top,0) },new G.Vector(0,1,0));
            Add(plans,view,"COLUMN-FLOOR-CHAIN","floor grids, base underside and terminal plate outside face",left,30,
                levels.Select(y=>Math.Abs(y-top)<=DimensionTolerance?topPlate:new G.Point(axis,y,0)).ToArray());
            Add(plans,view,"FLOOR-TOTAL","lower floor grid -> upper floor grid",left,40,new G.Point(axis,floors[0],0),new G.Point(axis,floors[1],0));
            var terminal=columns.Last();
            double terminalTop=terminal.Vertices.Max(p=>p.Y);
            // Upper bevels expose the inner wall at the very highest Y. Resolve
            // actual OUTER side vertices first, then their highest exact point.
            double minX=terminal.Vertices.Min(p=>p.X), maxX=terminal.Vertices.Max(p=>p.X);
            var edgeLeft=terminal.Vertices.Where(p=>Math.Abs(p.X-minX)<=DimensionTolerance).OrderByDescending(p=>p.Y).First();
            var edgeRight=terminal.Vertices.Where(p=>Math.Abs(p.X-maxX)<=DimensionTolerance).OrderByDescending(p=>p.Y).First();
            if (edgeLeft.X>=axis-DimensionTolerance || edgeRight.X<=axis+DimensionTolerance)
                throw new InvalidOperationException("REF không nằm giữa hai mép đầu cột.");
            if (Math.Abs(axes[0]-axis)<=DimensionTolerance)
                Add(plans,view,"COLUMN-EDGE-AXIS-EDGE","terminal solid edges -> coincident column/grid axis",up,10,edgeLeft,new G.Point(axis,terminalTop,0),edgeRight);
            else
                Add(plans,view,"COLUMN-REF-GRID","column REF -> actual vertical grid",up,10,new G.Point(axis,terminalTop,0),new G.Point(axes[0],terminalTop,0));
            // Continue outside the connection envelope; model geometry never gets a display offset.
            double leftLine=Math.Min(view.Parts.SelectMany(p=>p.Vertices).Min(p=>p.X),axis);
            double topLine=Math.Max(view.Parts.SelectMany(p=>p.Vertices).Max(p=>p.Y),top);
            foreach(var p in connections)
            {
                double line=Dot(p.Feet[0],p.Normal)+p.Distance;
                if(p.Normal.X < -0.99) leftLine=Math.Min(leftLine,-line);
                if(p.Normal.Y > 0.99) topLine=Math.Max(topLine,line);
            }
            foreach(var p in plans)
            {
                double paper=p.Kind=="COLUMN-PLATE-CHAIN"?10:p.Kind=="COLUMN-PLATE-TOTAL"?20:p.Kind=="COLUMN-FLOOR-CHAIN"?30:p.Kind=="FLOOR-TOTAL"?40:10;
                double line=p.Normal.X<0?-leftLine+paper*view.Scale:topLine+paper*view.Scale;
                p.Distance=line-Dot(p.Feet[0],p.Normal);
                p.RequireExactFeet=p.Kind=="COLUMN-PLATE-CHAIN" || p.Kind=="COLUMN-PLATE-TOTAL" || p.Kind=="COLUMN-FLOOR-CHAIN";
                p.Satisfied=view.Dimensions.Any(d=>Matches(p,d));
            }
            // Older builds left no ownership tag. Recognize their exact REF
            // footprints only to STOP, never delete an unowned/manual object.
            var old=new List<Plan>();
            Add(old,view,"OLD-REF-CHAIN","",left,10,nodes.Select(y=>new G.Point(axis,y,0)).ToArray());
            Add(old,view,"OLD-REF-TOTAL","",left,20,new G.Point(axis,nodes.First(),0),new G.Point(axis,nodes.Last(),0));
            var oldLevels=Stations(new[] { new G.Point(0,floors[0],0),new G.Point(0,floors[1],0),new G.Point(0,nodes.First(),0),new G.Point(0,nodes.Last(),0) },new G.Vector(0,1,0));
            Add(old,view,"OLD-FLOOR-CHAIN","",left,30,oldLevels.Select(y=>new G.Point(axis,y,0)).ToArray());
            foreach(var prior in old)
            {
                prior.RequireExactFeet=true;
                if(view.Dimensions.Any(d=>Matches(prior,d) && !plans.Any(p=>Matches(p,d))))
                    throw new InvalidOperationException("VIEW="+view.Id+" còn DIM chuỗi cũ bám REF ("+prior.Kind+"). Cần thay DIM này bằng chân mép plate; dừng để tránh chồng DIM hoặc xóa DIM thủ công.");
            }
            return plans;
        }

        private sealed class PlateFoot
        {
            public G.Point Point;
            public string Evidence;
        }

        // REF identifies connection stations only. The measured feet belong to
        // actual base/diaphragm plate faces, including for an end-on child beam.
        private static List<PlateFoot> ResolveColumnPlateFeet(View view, List<Part> columns,
            List<double> nodes, double axis, double depth)
        {
            var roles=new Dictionary<int,bool>(); // true = upper outside face
            Action<double,bool> register=(station,upper)=>
            {
                int index=nodes.FindIndex(y=>Math.Abs(y-station)<=GeometryTolerance);
                if(index<0) throw new InvalidOperationException("Mép nhánh không khớp node cột; chưa đủ bằng chứng chọn plate.");
                bool previous;
                if(roles.TryGetValue(index,out previous) && previous!=upper)
                    throw new InvalidOperationException("Một node có hai vai trò mép plate trái ngược; dừng trước khi DIM.");
                roles[index]=upper;
            };
            foreach(var beam in view.Parts)
            {
                string profile=(beam.Profile??"").Trim().ToUpperInvariant();
                if(beam.Plate || beam.Assembly!=view.Main.Assembly || beam.Reference.Count!=2
                    || beam.Vertices.Count==0 || !(profile.StartsWith("H") || profile.StartsWith("I") || profile.StartsWith("BH"))) continue;
                var ends=beam.Reference.OrderBy(p=>(p.X-axis)*(p.X-axis)+(p.Z-depth)*(p.Z-depth)).ToArray();
                var node=ends[0]; var end=ends[1];
                if(Math.Abs(node.X-axis)>GeometryTolerance || Math.Abs(node.Z-depth)>GeometryTolerance
                    || Math.Abs(node.Y-end.Y)>GeometryTolerance || !Between(node.Y,nodes.First(),nodes.Last())) continue;
                double dx=end.X-node.X, dz=end.Z-node.Z, length=Math.Sqrt(dx*dx+dz*dz);
                if(length<100) continue;
                dx/=length; dz/=length;
                Func<G.Point,double> projection=p=>(p.X-node.X)*dx+(p.Z-node.Z)*dz;
                double terminal=beam.Vertices.Max(projection);
                if(terminal<100) continue;
                var face=beam.Vertices.Where(p=>Math.Abs(projection(p)-terminal)<=GeometryTolerance).ToList();
                double low=face.Min(p=>p.Y), high=face.Max(p=>p.Y);
                if(high-low<=GeometryTolerance) continue;
                if(Math.Min(Math.Abs(node.Y-low),Math.Abs(node.Y-high))>GeometryTolerance)
                    throw new InvalidOperationException("REF nhánh P"+beam.Id+" không trùng mép tiết diện; cần quy tắc hình học riêng.");
                register(low,false); register(high,true);
            }
            if(roles.Count==0) throw new InvalidOperationException("Không chứng minh được cặp diaphragm trên/dưới của liên kết.");
            if(roles.ContainsKey(0) && roles[0]) throw new InvalidOperationException("Base plate role conflicts with connection.");
            roles[0]=false;
            if(!roles.ContainsKey(nodes.Count-1)) roles[nodes.Count-1]=true;
            var result=new List<PlateFoot>(); var owners=new HashSet<int>();
            for(int i=0;i<nodes.Count;i++)
            {
                bool upper;
                if(!roles.TryGetValue(i,out upper)) throw new InvalidOperationException("Node cột chưa có vai trò diaphragm rõ ràng: "+F(nodes[i]));
                var adjacent=columns.Where(c=>Between(nodes[i],c.Reference.Min(p=>p.Y),c.Reference.Max(p=>p.Y))).ToList();
                double colMinX=adjacent.Min(c=>c.Vertices.Min(p=>p.X)), colMaxX=adjacent.Max(c=>c.Vertices.Max(p=>p.X));
                double colMinZ=adjacent.Min(c=>c.Vertices.Min(p=>p.Z)), colMaxZ=adjacent.Max(c=>c.Vertices.Max(p=>p.Z));
                var candidates=view.Parts.Where(p=>p.Plate && p.Assembly==view.Main.Assembly && p.Vertices.Count>0)
                    .Where(p=>
                    {
                        double lo=p.Vertices.Min(v=>v.Y), hi=p.Vertices.Max(v=>v.Y);
                        double x0=p.Vertices.Min(v=>v.X), x1=p.Vertices.Max(v=>v.X);
                        double z0=p.Vertices.Min(v=>v.Z), z1=p.Vertices.Max(v=>v.Z);
                        return Between(nodes[i],lo,hi) && hi-lo>DimensionTolerance
                            && hi-lo<0.5*Math.Min(x1-x0,z1-z0)
                            && x0<=colMinX+GeometryTolerance && x1>=colMaxX-GeometryTolerance
                            && z0<=colMinZ+GeometryTolerance && z1>=colMaxZ-GeometryTolerance;
                    }).ToList();
                if(candidates.Count!=1) throw new InvalidOperationException("Node "+F(nodes[i])+" cần đúng một plate ngang bao cột; tìm thấy "+candidates.Count+".");
                var plate=candidates[0];
                if(!owners.Add(plate.Id)) throw new InvalidOperationException("Hai node cùng chọn một plate; dừng DIM chuỗi.");
                double y=upper?plate.Vertices.Max(p=>p.Y):plate.Vertices.Min(p=>p.Y);
                var foot=plate.Vertices.Where(p=>Math.Abs(p.Y-y)<=DimensionTolerance)
                    .OrderBy(p=>p.X).ThenBy(p=>Math.Abs(p.Z-depth)).ThenBy(p=>p.Z).First();
                if(result.Count>0 && foot.Y-result.Last().Point.Y<=GeometryTolerance)
                    throw new InvalidOperationException("Các mép plate không theo thứ tự tăng dọc cột.");
                result.Add(new PlateFoot { Point=new G.Point(foot.X,foot.Y,0),
                    Evidence="P"+plate.Id+" "+(i==0?"base underside":upper?"upper outside face":"lower outside face") });
            }
            return result;
        }
    }
}
