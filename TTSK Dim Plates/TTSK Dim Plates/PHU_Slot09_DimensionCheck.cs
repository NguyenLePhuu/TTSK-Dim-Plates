using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;

namespace Tekla.Technology.Akit.UserScript
{
    public static class PHU_AutoDimSlot09
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }
        public static string LastRunAudit { get; private set; }
        public static string Audit() { return PHU_DimensionCheck.Analyze().Report(); }
        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunAudit = String.Empty;
            try
            {
                var result = PHU_DimensionCheck.Analyze();
                LastRunAudit = result.Report();
                LastRunMessage = result.Summary;
                LastRunSucceeded = true; // Analysis completed; findings are represented separately.
                using (var form = new Form { Text = "Slot 09 — Kiểm tra chân DIM", Width = 1100, Height = 650,
                    StartPosition = FormStartPosition.CenterScreen })
                {
                    var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                        ScrollBars = ScrollBars.Both, WordWrap = false, Text = LastRunAudit };
                    form.Controls.Add(text);
                    form.ShowDialog();
                }
            }
            catch (Exception ex) { LastRunMessage = "DIM check không hoàn tất: " + ex.Message; }
        }
    }

    // Read-only adapter and pure feature classifier. No work-plane change or drawing writes.
    public static class PHU_DimensionCheck
    {
        // Model millimetres, independent of drawing scale. A 1 mm wrong pick must remain visible.
        public const double Tolerance = 0.25;
        public sealed class Feature
        {
            public int Owner;
            public string Kind;
            public G.Point A, B;
            public bool Allowed;
        }
        public sealed class Finding
        {
            public string Status, Reason;
            public int View, Dimension, Foot;
            public G.Point Point;
            public G.Vector Normal;
            public override string ToString()
            {
                return Status + " VIEW=" + View + " DIM=" + Dimension + " foot=" + Foot
                    + " " + P(Point) + " " + Reason;
            }
        }
        public sealed class Result
        {
            public string Drawing;
            public int Main, Views, Dimensions;
            public readonly List<Finding> Findings = new List<Finding>();
            public readonly List<string> Warnings = new List<string>();
            public readonly Dictionary<int, List<Feature>> Features = new Dictionary<int, List<Feature>>();
            public readonly List<string> Relations = new List<string>();
            public string Summary { get { return "DIM check: " + Dimensions + " DIM / " + Views + " view; "
                + Findings.Count(f => f.Status == "ERROR") + " chân nghi sai; "
                + Findings.Count(f => f.Status == "REVIEW") + " chân cần đối chiếu; "
                + Warnings.Count + " mục chưa kiểm tra đầy đủ."; } }
            public string Report()
            {
                var s = new StringBuilder("SLOT 09 DIMENSION CHECK — READ ONLY\r\n");
                s.AppendLine("CapturedUtc=" + DateTime.UtcNow.ToString("O"));
                s.AppendLine("Drawing=" + Drawing + " Main=" + Main);
                s.AppendLine(Summary);
                s.AppendLine("Tọa độ View.DisplayCoordinateSystem; dung sai=" + Tolerance + " mm model.");
                s.AppendLine("ERROR: không có feature hợp lệ tại chân; REVIEW: chỉ khớp phép chiếu hoặc thiếu bằng chứng.");
                s.AppendLine("Khớp hình học không chứng minh associativity đã lưu trong Tekla. Không tự sửa DIM.");
                foreach(var group in Findings.GroupBy(f=>f.View).OrderBy(g=>g.Key))
                    s.AppendLine("VIEW="+group.Key+" Sets="+group.Select(f=>f.Dimension).Distinct().Count()+" Feet="+group.Count());
                foreach(string relation in Relations) s.AppendLine(relation);
                foreach (var f in Findings.OrderBy(f => f.View).ThenBy(f => f.Dimension).ThenBy(f => f.Foot)) s.AppendLine(f.ToString());
                foreach (var w in Warnings) s.AppendLine("INCOMPLETE " + w);
                return s.ToString();
            }
        }
        public static Finding Classify(G.Point point, G.Vector normal, IList<Feature> features)
        {
            var f = new Finding { Point = point, Normal = normal };
            if (!Finite(point) || normal == null || !Finite(normal) || Math.Sqrt(normal.X*normal.X+normal.Y*normal.Y) < 1e-9)
            { f.Status = "REVIEW"; f.Reason = "Tọa độ hoặc phương DIM không hợp lệ."; return f; }
            var exact = features.Where(x => x.Allowed && Residual(point, x) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            if (exact.Count > 0)
            {
                f.Status = "OK"; f.Reason = Describe(exact[0], Residual(point, exact[0])); return f;
            }
            // A displayed extension origin can be moved perpendicular to the measurement axis.
            // Only discrete anchors and constant-projection edges provide projection evidence;
            // the entire projected interval of a longitudinal edge is never a valid anchor.
            double len = Math.Sqrt(normal.X*normal.X+normal.Y*normal.Y);
            double dx = -normal.Y/len, dy = normal.X/len;
            var projected = features.Where(x => x.Allowed
                && Math.Abs((x.B.X-x.A.X)*dx+(x.B.Y-x.A.Y)*dy) <= Tolerance
                && Math.Abs((point.X-x.A.X)*dx+(point.Y-x.A.Y)*dy) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            var foreign = features.Where(x => !x.Allowed && Residual(point, x) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            if (projected.Count > 0)
            {
                f.Status = "REVIEW";
                f.Reason = "Chỉ khớp tọa độ theo phương đo: " + Describe(projected[0], Residual(point, projected[0]))
                    + (foreign.Count > 0 ? "; đồng thời nằm trên feature không được phép: " + Describe(foreign[0], 0) : "");
            }
            else
            {
                f.Status = "ERROR";
                f.Reason = foreign.Count > 0 ? "Chân nằm trên neighbor/feature không được phép: " + Describe(foreign[0], 0)
                    : "Không tìm thấy lỗ, mép hoặc REF hợp lệ tại chân.";
                var nearest = features.Where(x => x.Allowed).OrderBy(x => Residual(point,x)).FirstOrDefault();
                if (nearest != null) f.Reason += " Gần nhất (không phải owner xác nhận): " + Describe(nearest, Residual(point,nearest));
            }
            return f;
        }
        private static string Describe(Feature f, double distance)
        { return "P" + f.Owner + ":" + f.Kind + " residual=" + distance.ToString("0.###", CultureInfo.InvariantCulture); }
        private static bool Finite(G.Point p)
        { return p != null && !Double.IsNaN(p.X) && !Double.IsNaN(p.Y) && !Double.IsNaN(p.Z)
            && !Double.IsInfinity(p.X) && !Double.IsInfinity(p.Y) && !Double.IsInfinity(p.Z); }
        public static double Residual(G.Point p, Feature f)
        {
            double dx=f.B.X-f.A.X, dy=f.B.Y-f.A.Y, sq=dx*dx+dy*dy;
            double t=sq<1e-16?0:Math.Max(0,Math.Min(1,((p.X-f.A.X)*dx+(p.Y-f.A.Y)*dy)/sq));
            double x=p.X-f.A.X-t*dx,y=p.Y-f.A.Y-t*dy;
            return Math.Sqrt(x*x+y*y);
        }
        private static string P(G.Point p)
        { return p == null ? "(?)" : String.Format(CultureInfo.InvariantCulture,"({0:0.###},{1:0.###})",p.X,p.Y); }
        private static object Member(object obj, string name)
        {
            var prop=obj.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            return prop == null ? null : prop.GetValue(obj,null);
        }
        private static int Id(object obj)
        { var id=Member(obj,"Identifier") as Tekla.Structures.Identifier;
            if(id==null || id.ID==0) throw new InvalidOperationException("Không đọc được ID drawing object."); return id.ID; }

        public static HashSet<int> ResolveConnected(PHU_NeighborCleanup.Plan graph, HashSet<int> primary)
        {
            var connected=new HashSet<int>();
            foreach(var edge in graph.Edges)
            {
                if(primary.Contains(edge.A) && !graph.Nodes[edge.A].Dummy && !primary.Contains(edge.B)) connected.Add(edge.B);
                if(primary.Contains(edge.B) && !graph.Nodes[edge.B].Dummy && !primary.Contains(edge.A)) connected.Add(edge.A);
            }
            foreach(int seed in connected.ToList())
            {
                if(graph.Nodes[seed].Dummy || !graph.Nodes[seed].Plate) continue;
                foreach(var edge in graph.Edges.Where(e=>e.A==seed || e.B==seed))
                {
                    int next=edge.A==seed?edge.B:edge.A;
                    if(!graph.Nodes[next].Plate && !graph.Nodes[next].Dummy && !primary.Contains(next)) connected.Add(next);
                }
            }
            connected.RemoveWhere(id=>graph.Nodes[id].Dummy);
            return connected;
        }

        public static Result Analyze()
        {
            var model=new M.Model(); var handler=new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new InvalidOperationException("Không kết nối Tekla.");
            var drawing=handler.GetActiveDrawing();
            if (drawing == null) throw new InvalidOperationException("Chưa mở bản vẽ.");
            if (!(drawing is D.AssemblyDrawing) && !(drawing is D.SinglePartDrawing))
                throw new InvalidOperationException("Hỗ trợ Assembly và SinglePart Drawing.");
            var main=PHU_MainPartResolver.Resolve(model,drawing);
            if(main==null) throw new InvalidOperationException("Không xác định được main.");
            var result=new Result { Main=main.Identifier.ID,Drawing=drawing.Name+" / "+drawing.Mark };
            var primary=new HashSet<int> { result.Main };
            var connected=new HashSet<int>();
            var dummy=new HashSet<int>();
            if(drawing is D.AssemblyDrawing)
            {
                var graph=PHU_NeighborCleanup.Analyze();
                if(!graph.Drawing.IsSameDatabaseObject(drawing)) throw new InvalidOperationException("Drawing thay đổi trong lúc đọc.");
                primary=new HashSet<int>(graph.Keep.Where(x=>x.Value=="KEEP primary assembly").Select(x=>x.Key));
                foreach(var node in graph.Nodes.Values) if(node.Dummy) dummy.Add(node.Id);
                // Reuse Slot08 connection graph, never its visibility/proximity fallback.
                connected=ResolveConnected(graph,primary);
                foreach(int id in connected.OrderBy(x=>x)) result.Relations.Add("ALLOW NEIGHBOR REF P"+id+" "+graph.Nodes[id].Label);
                foreach(var edge in graph.Edges.Where(e=>(primary.Contains(e.A)||connected.Contains(e.A))
                    && (primary.Contains(e.B)||connected.Contains(e.B))).OrderBy(e=>e.A).ThenBy(e=>e.B))
                    result.Relations.Add("LINK P"+edge.A+" -> P"+edge.B+" "+edge.Evidence);
            }
            primary.ExceptWith(dummy);
            result.Relations.Add("PRIMARY="+String.Join(",",primary.OrderBy(x=>x)));
            if(!primary.Contains(result.Main)) throw new InvalidOperationException("Main là dummy/reference; cần xác minh thủ công.");
            var global=model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var views=drawing.GetSheet().GetAllViews();
            while(views.MoveNext())
            {
                var view=views.Current as D.View; if(view==null) continue;
                result.Views++;
                var matrix=G.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                var features=new List<Feature>(); int warningCount=result.Warnings.Count;
                var local=new Dictionary<int,M.Part>();
                var dparts=view.GetAllObjects(typeof(D.Part));
                while(dparts.MoveNext())
                {
                    var dp=(D.Part)dparts.Current;
                    var part=model.SelectModelObject(dp.ModelIdentifier) as M.Part;
                    if(part==null) { result.Warnings.Add("VIEW="+Id(view)+" model part không đọc được."); continue; }
                    local[part.Identifier.ID]=part;
                }
                foreach(var pair in local.OrderBy(x=>x.Key))
                {
                    try { Extract(pair.Value, primary.Contains(pair.Key), connected.Contains(pair.Key), dummy.Contains(pair.Key), global,matrix,features); }
                    catch(Exception ex) { result.Warnings.Add("VIEW="+Id(view)+" P"+pair.Key+" "+ex.Message); }
                }
                if(!local.Keys.Any(primary.Contains)) result.Warnings.Add("VIEW="+Id(view)+" không có part thuộc main fabrication unit để đối chiếu.");
                result.Features[Id(view)]=features;
                bool incomplete=result.Warnings.Count>warningCount;
                var dimensions=view.GetAllObjects();
                var seen=new HashSet<int>();
                while(dimensions.MoveNext())
                {
                    object dim=dimensions.Current;
                    if(!(dim is D.DimensionBase) && !(dim is D.StraightDimensionSet)) continue;
                    // Straight children are represented by the parent set's complete point list.
                    if(dim is D.StraightDimension) continue;
                    if(!seen.Add(Id(dim))) continue;
                    result.Dimensions++;
                    if(!(dim is D.StraightDimensionSet))
                    { result.Warnings.Add("VIEW="+Id(view)+" DIM="+Id(dim)+" chưa hỗ trợ "+dim.GetType().Name); continue; }
                    var raw=Member(dim,"DimensionPoints") as IEnumerable;
                    var normal=Member(dim,"UpDirection") as G.Vector;
                    if(raw==null) { result.Warnings.Add("DIM="+Id(dim)+" thiếu DimensionPoints."); continue; }
                    int index=0;
                    foreach(object value in raw)
                    {
                        var finding=Classify(value as G.Point,normal,features);
                        finding.View=Id(view); finding.Dimension=Id(dim); finding.Foot=index++;
                        if(incomplete && finding.Status=="ERROR")
                        { finding.Status="REVIEW"; finding.Reason="Geometry chưa đầy đủ. "+finding.Reason; }
                        result.Findings.Add(finding);
                    }
                    if(index<2) result.Warnings.Add("DIM="+Id(dim)+" có ít hơn 2 chân đọc được.");
                }
            }
            if(result.Dimensions==0) result.Warnings.Add("Không có DIM được kiểm tra.");
            var active=handler.GetActiveDrawing();
            if(active==null || !active.IsSameDatabaseObject(drawing)) throw new InvalidOperationException("Drawing thay đổi trong lúc kiểm tra.");
            return result;
        }
        private static void Extract(M.Part part, bool primary, bool connected, bool dummy,
            G.Matrix global,G.Matrix view,List<Feature> output)
        {
            int id=part.Identifier.ID;
            Func<G.Point,G.Point> transform=p=>view.Transform(global.Transform(p));
            Action<string,G.Point,G.Point,bool> add=(kind,a,b,allowed)=>
            {
                var x=transform(a); var y=transform(b);
                if(!Finite(x)||!Finite(y)) throw new InvalidOperationException("Geometry không hữu hạn.");
                output.Add(new Feature { Owner=id,Kind=kind,A=x,B=y,Allowed=allowed });
            };
            var solid=part.GetSolid(); var edges=solid.GetEdgeEnumerator();
            while(edges.MoveNext())
            {
                var edge=edges.Current as Tekla.Structures.Solid.Edge; if(edge==null) continue;
                add("EDGE",edge.StartPoint,edge.EndPoint,primary&&!dummy);
                add("VERTEX",edge.StartPoint,edge.StartPoint,primary&&!dummy);
                add("VERTEX",edge.EndPoint,edge.EndPoint,primary&&!dummy);
            }
            var refs=part.GetReferenceLine(false).Cast<G.Point>().ToList();
            for(int i=0;i<refs.Count;i++)
            {
                add("REF-POINT",refs[i],refs[i],(primary||connected)&&!dummy);
                if(i>0) add("REF",refs[i-1],refs[i],(primary||connected)&&!dummy);
            }
            var bolts=part.GetBolts();
            while(bolts.MoveNext())
            {
                var bolt=bolts.Current as M.BoltGroup; if(bolt==null) continue;
                foreach(G.Point point in bolt.BoltPositions) add("HOLE/BOLT:"+bolt.Identifier.ID,point,point,primary&&!dummy);
            }
        }
    }
}
