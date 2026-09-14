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
    public static partial class PHU_DimensionCheck
    {
        public static bool ReportDarkMode { get; set; }
        internal static void AcceptRecheck(PHU_DimensionCheck.Result result)
        { LastRunAudit=result.Report(); LastRunMessage=result.Summary; LastRunSucceeded=true; }
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
                PHU_DimensionCheck.ShowReport(result);
            }
            catch (Exception ex) { LastRunMessage = "DIM check không hoàn tất: " + ex.Message; }
        }
    }

    // Read-only adapter and pure feature classifier. No work-plane change or drawing writes.
    public static partial class PHU_DimensionCheck
    {
        // Model millimetres, independent of drawing scale. A 1 mm wrong pick must remain visible.
        public const double Tolerance = 0.04;
        public sealed class Feature
        {
            public int Owner;
            public string Kind;
            public G.Point A, B;
            public bool Allowed;
            public bool Infinite;
            public bool ConnectedPart, ExternalMemberReference;
        }
        public sealed class Finding
        {
            public string Status, Reason;
            public int View, Dimension, Foot;
            public G.Point Point;
            public G.Vector Normal;
            public string ViewLabel, DimensionLabel, Location;
            // Geometric evidence only; a candidate owner is not saved Tekla associativity.
            public string EvidenceKind;
            public int[] CandidateOwners = new int[0];
            public double MeasurementResidual = Double.NaN;
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
            public bool GeometryCoverageTracked;
            public readonly HashSet<int> IncompleteGeometryViews = new HashSet<int>();
            public D.Drawing SourceDrawing;
            public readonly Dictionary<int,D.DrawingObject> Objects = new Dictionary<int,D.DrawingObject>();
            public string Summary { get {
                int count=Findings.Count(f=>f.Status=="ERROR");
                bool incomplete=Warnings.Count>0 || Findings.Any(f=>f.Status=="REVIEW");
                return (count==0 ? (incomplete ? "Chưa thể kết luận toàn bộ bản vẽ." : "Không phát hiện chân kích thước sai.")
                    : "Phát hiện "+count+" chân kích thước có dấu hiệu bắt sai.")
                    + " Đã kiểm tra "+Dimensions+" chuỗi kích thước trong "+Views+" hình chiếu.";
            } }
            public string UserReport()
            {
                var text=new StringBuilder(Summary);
                foreach(var f in Findings.Where(f=>f.Status=="ERROR"))
                    text.AppendLine().AppendLine(f.ViewLabel+" • Kích thước "+f.DimensionLabel+" • "+f.Location)
                        .AppendLine(UserReason(f));
                if(Warnings.Count>0 || Findings.Any(f=>f.Status=="REVIEW"))
                    text.AppendLine().AppendLine("Một số chuỗi cần xác minh, dữ liệu chưa đọc được hoặc loại kích thước chưa được hỗ trợ. Đây không phải kết luận bản vẽ sai.");
                return text.ToString();
            }
            public string Report()
            {
                var s = new StringBuilder("SLOT 09 DIMENSION CHECK — READ ONLY\r\n");
                s.AppendLine("CapturedUtc=" + DateTime.UtcNow.ToString("O"));
                s.AppendLine("Drawing=" + Drawing + " Main=" + Main);
                s.AppendLine(Summary);
                s.AppendLine("Tọa độ View.DisplayCoordinateSystem; dung sai=" + Tolerance + " mm model.");
                s.AppendLine("ERROR: sai mốc hình học hoặc quan hệ đã xác minh; REVIEW: thiếu dữ liệu hoặc chuỗi cần xác minh. Phép chiếu có cơ sở hình học được chấp nhận.");
                s.AppendLine("Khớp hình học không chứng minh associativity đã lưu trong Tekla. Không tự sửa DIM.");
                foreach(var group in Findings.GroupBy(f=>f.View).OrderBy(g=>g.Key))
                    s.AppendLine("VIEW="+group.Key+" Sets="+group.Select(f=>f.Dimension).Distinct().Count()+" Feet="+group.Count());
                foreach(string relation in Relations) s.AppendLine(relation);
                foreach (var f in Findings.OrderBy(f => f.View).ThenBy(f => f.Dimension).ThenBy(f => f.Foot)) s.AppendLine(f.ToString());
                foreach(var f in Findings.Where(f=>f.EvidenceKind!=null).OrderBy(f=>f.View).ThenBy(f=>f.Dimension).ThenBy(f=>f.Foot))
                    s.AppendLine("EVIDENCE VIEW="+f.View+" DIM="+f.Dimension+" foot="+f.Foot+" "+f.EvidenceKind
                        +" candidateOwners="+String.Join(",",f.CandidateOwners)+" measuredResidual="
                        +f.MeasurementResidual.ToString("0.###",CultureInfo.InvariantCulture));
                foreach (var w in Warnings) s.AppendLine("INCOMPLETE " + w);
                return s.ToString();
            }
        }
        public static Finding Classify(G.Point point, G.Vector normal, IList<Feature> features)
        {
            var f = new Finding { Point = point, Normal = normal };
            double dx,dy;
            if (!Finite(point) || !TryMeasurementAxis(normal,out dx,out dy))
            { f.Status = "REVIEW"; f.Reason = "Tọa độ hoặc phương DIM không hợp lệ."; return f; }
            bool incomplete=features==null || features.Any(x=>!ValidFeature(x));
            features=(features??new List<Feature>()).Where(ValidFeature).ToList();
            if(!features.Any(x=>x.Allowed))
            { f.Status="REVIEW"; f.Reason="Không có hình học hợp lệ để đối chiếu chân kích thước."; return f; }
            // A point on a longitudinal edge does not establish a measured station.
            // Use real vertices/hole centres or edges defining a constant measured coordinate.
            // This applies to exact contact too, not only to displaced extension origins.
            var exact = features.Where(x => x.Allowed && ConstantProjection(x,dx,dy) && Residual(point, x) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            if (exact.Count > 0)
            {
                SetEvidence(f,"EXACT",exact,dx,dy);
                f.Status = "OK"; f.Reason = Describe(exact[0], Residual(point, exact[0])); return f;
            }
            // A displayed extension origin can be moved perpendicular to the measurement axis.
            // Only discrete anchors and constant-projection edges provide projection evidence;
            // the entire projected interval of a longitudinal edge is never a valid anchor.
            var projected = features.Where(x => x.Allowed
                && ConstantProjection(x,dx,dy)
                && Math.Abs((point.X-x.A.X)*dx+(point.Y-x.A.Y)*dy) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            var foreign = features.Where(x => !x.Allowed && Residual(point, x) <= Tolerance)
                .OrderBy(x=>Residual(point,x)).ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).ToList();
            if (projected.Count > 0)
            {
                SetEvidence(f,"PROJECTED",projected,dx,dy);
                f.Status = "OK";
                f.Reason = "Đường dóng giữ đúng tọa độ theo phương đo: " + Describe(projected[0], Residual(point, projected[0]))
                    + (foreign.Count > 0 ? "; đồng thời nằm trên feature không được phép: " + Describe(foreign[0], 0) : "");
            }
            else
            {
                f.Status = "ERROR";
                f.Reason = foreign.Count > 0 ? "Chân nằm trên neighbor/feature không được phép: " + Describe(foreign[0], 0)
                    : "Không tìm thấy lỗ, mép hoặc REF hợp lệ tại chân.";
                var nearest = features.Where(x => x.Allowed && ConstantProjection(x,dx,dy))
                    .OrderBy(x=>MeasurementResidual(point,x,dx,dy)).ThenBy(x=>Residual(point,x))
                    .ThenBy(x=>x.Owner).ThenBy(x=>x.Kind,StringComparer.Ordinal).FirstOrDefault();
                if (nearest != null)
                {
                    f.MeasurementResidual=MeasurementResidual(point,nearest,dx,dy);
                    f.Reason += " Mốc gần nhất theo phương đo (không phải owner xác nhận): "
                        +Describe(nearest,Residual(point,nearest))+"; lệch phương đo="
                        +f.MeasurementResidual.ToString("0.###",CultureInfo.InvariantCulture)+" mm.";
                }
                if(foreign.Any(x=>x.ConnectedPart))
                {
                    var reference=features.Where(x=>x.Allowed && x.ExternalMemberReference && ConstantProjection(x,dx,dy))
                        .OrderBy(x=>Residual(point,x)).FirstOrDefault();
                    if(reference!=null)
                    {
                        double delta=Math.Abs((point.X-reference.A.X)*dx+(point.Y-reference.A.Y)*dy);
                        f.Reason="NEIGHBOR_REF: Chân đang bám mép vật liệu tại liên kết plate–neighbor. REF của thép hình liên kết gần nhất lệch "
                            +delta.ToString("0.###",CultureInfo.InvariantCulture)+" mm theo phương đo. Hãy bắt vào REF thay vì mép độ dày.";
                    }
                }
                if(incomplete)
                { f.Status="REVIEW"; f.Reason="Geometry chưa đầy đủ. "+f.Reason; }
            }
            return f;
        }
        private static bool ValidFeature(Feature f)
        { return f!=null && Finite(f.A) && Finite(f.B); }
        private static double MeasurementResidual(G.Point point,Feature feature,double dx,double dy)
        { return Math.Abs((point.X-feature.A.X)*dx+(point.Y-feature.A.Y)*dy); }
        private static void SetEvidence(Finding finding,string kind,List<Feature> matches,double dx,double dy)
        {
            finding.EvidenceKind=kind;
            finding.CandidateOwners=matches.Select(x=>x.Owner).Distinct().OrderBy(x=>x).ToArray();
            finding.MeasurementResidual=matches.Min(x=>MeasurementResidual(finding.Point,x,dx,dy));
        }
        private static bool TryMeasurementAxis(G.Vector normal,out double dx,out double dy)
        {
            dx=dy=0;
            if(!Finite(normal)) return false;
            double scale=Math.Max(Math.Abs(normal.X),Math.Abs(normal.Y));
            if(scale<1e-9 || Math.Abs(normal.Z)/scale>1e-8) return false;
            double x=normal.X/scale,y=normal.Y/scale,length=Math.Sqrt(x*x+y*y);
            dx=-y/length;dy=x/length;
            return true;
        }
        // A coincident station can be intentional. Request review, never infer a new foot.
        public static string ValidateChain(IList<G.Point> points,G.Vector normal,double distance)
        {
            double dx,dy;
            if(points==null || points.Count<2) return "Chuỗi có ít hơn hai chân kích thước đọc được.";
            if(points.Any(p=>!Finite(p)) || !TryMeasurementAxis(normal,out dx,out dy))
                return "Tọa độ hoặc phương đo của chuỗi không hợp lệ; cần kiểm tra trực tiếp.";
            if(Double.IsNaN(distance) || Double.IsInfinity(distance))
                return "Không đọc được vị trí đường kích thước của chuỗi.";
            var origin=points[0];
            var stations=points.Select(p=>(p.X-origin.X)*dx+(p.Y-origin.Y)*dy).OrderBy(x=>x).ToList();
            if(stations.Any(x=>Double.IsNaN(x)||Double.IsInfinity(x))) return "Tọa độ chuỗi vượt phạm vi tính toán.";
            if(stations.Last()-stations.First()<=Tolerance)
                return "Toàn bộ chân trùng tọa độ theo phương đo trong dung sai 0.04 mm; cần xác minh kích thước bằng không.";
            for(int i=1;i<stations.Count;i++) if(stations[i]-stations[i-1]<=Tolerance)
                return "Có hai chân trùng hoặc quá sát nhau theo phương đo (≤ 0.04 mm); cần xác minh đoạn kích thước bằng không.";
            return null;
        }
        private static string Describe(Feature f, double distance)
        { return "P" + f.Owner + ":" + f.Kind + " residual=" + distance.ToString("0.###", CultureInfo.InvariantCulture); }
        private static bool Finite(G.Point p)
        { return p != null && !Double.IsNaN(p.X) && !Double.IsNaN(p.Y) && !Double.IsNaN(p.Z)
            && !Double.IsInfinity(p.X) && !Double.IsInfinity(p.Y) && !Double.IsInfinity(p.Z); }
        public static double Residual(G.Point p, Feature f)
        {
            double dx=f.B.X-f.A.X, dy=f.B.Y-f.A.Y, sq=dx*dx+dy*dy;
            double t=sq<1e-16?0:((p.X-f.A.X)*dx+(p.Y-f.A.Y)*dy)/sq;
            if(!f.Infinite) t=Math.Max(0,Math.Min(1,t));
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
            var result=new Result { Main=main.Identifier.ID,Drawing=drawing.Name+" / "+drawing.Mark,SourceDrawing=drawing,
                GeometryCoverageTracked=true };
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
                foreach(int id in connected.Where(id=>!graph.Nodes[id].Plate).OrderBy(x=>x)) result.Relations.Add("CANDIDATE NEIGHBOR REF P"+id+" "+graph.Nodes[id].Label);
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
                ConstrainReferences(features,result.Main);
                foreach(var reference in features.Where(f=>f.Kind=="REF-INTERSECTION"))
                    result.Relations.Add("VIEW="+Id(view)+" VERIFIED REF MAIN -> P"+reference.Owner+" intersection="+P(reference.A));
                ExtractGrids(model,view,global,matrix,features,result.Warnings);
                result.Features[Id(view)]=features;
                bool incomplete=result.Warnings.Count>warningCount;
                if(incomplete) result.IncompleteGeometryViews.Add(Id(view));
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
                    try
                    {
                        var raw=Member(dim,"DimensionPoints") as IEnumerable;
                        var normal=Member(dim,"UpDirection") as G.Vector;
                        if(raw==null) { result.Warnings.Add("DIM="+Id(dim)+" thiếu DimensionPoints."); continue; }
                        var points=raw.Cast<object>().Select(x=>x as G.Point).ToList();
                        object rawDistance=Member(dim,"Distance");
                        double distance=rawDistance==null?Double.NaN:Convert.ToDouble(rawDistance,CultureInfo.InvariantCulture);
                        string chainReview=ValidateChain(points,normal,distance);
                        if(chainReview!=null) result.Warnings.Add("VIEW="+Id(view)+" DIM="+Id(dim)+" "+chainReview);
                        result.Objects[Id(dim)]=(D.DrawingObject)dim;
                        string dimensionLabel=DimensionLabel(points,normal);
                        int index=0;
                        foreach(object value in points)
                        {
                            var finding=Classify(value as G.Point,normal,features);
                            finding.View=Id(view); finding.Dimension=Id(dim); finding.Foot=index++;
                            finding.ViewLabel=ViewLabel(view);
                            finding.DimensionLabel=dimensionLabel;
                            finding.Location=Location(finding.Point,points,normal,finding.Foot);
                            if(normal!=null)
                            {
                                double sign=distance<0?-1:1;
                                string side=Math.Abs(normal.X)>Math.Abs(normal.Y)?(normal.X*sign<0?"Chuỗi bên trái":"Chuỗi bên phải")
                                    :(normal.Y*sign<0?"Chuỗi phía dưới":"Chuỗi phía trên");
                                finding.Location=side+" — "+finding.Location.ToLowerInvariant();
                            }
                            if(incomplete && finding.Status=="ERROR")
                            { finding.Status="REVIEW"; finding.Reason="Geometry chưa đầy đủ. "+finding.Reason; }
                            if(chainReview!=null && finding.Status=="OK")
                            { finding.Status="REVIEW"; finding.Reason="CHAIN: "+chainReview; }
                            result.Findings.Add(finding);
                        }
                    }
                    catch(Exception ex)
                    {
                        // A failed set must not stop the remaining sets, or act as notch evidence.
                        foreach(var finding in result.Findings.Where(f=>f.View==Id(view)&&f.Dimension==Id(dim)))
                        { finding.Status="REVIEW"; finding.Reason="Không đọc đủ chuỗi kích thước. "+ex.Message; }
                        result.Warnings.Add("VIEW="+Id(view)+" DIM="+Id(dim)+" không đọc đủ chuỗi: "+ex.Message);
                    }
                }
            }
            CheckNotchPairs(result);
            if(result.Dimensions==0) result.Warnings.Add("Không có DIM được kiểm tra.");
            var active=handler.GetActiveDrawing();
            if(active==null || !active.IsSameDatabaseObject(drawing)) throw new InvalidOperationException("Drawing thay đổi trong lúc kiểm tra.");
            return result;
        }
        // Cross-check the two dimensions describing the same end notch. A matching REF
        // elsewhere on the main part cannot replace the wall of this particular notch.
        public static void CheckNotchPairs(Result result)
        {
            // Legacy/manual snapshots without scoped coverage remain conservative.
            if(!result.GeometryCoverageTracked && result.Warnings.Count>0) return;
            foreach(var view in result.Findings.GroupBy(f=>f.View))
            {
                if(result.IncompleteGeometryViews.Contains(view.Key)) continue;
                List<Feature> features;
                if(!result.Features.TryGetValue(view.Key,out features)) continue;
                var edges=features.Where(f=>f.Owner==result.Main && f.Kind=="EDGE" && f.Allowed).ToList();
                var sets=view.GroupBy(f=>f.Dimension).Select(g=>g.OrderBy(f=>f.Foot).ToList())
                    .Where(g=>g.Count==2 && g.All(f=>Finite(f.Point) && f.Normal!=null && Finite(f.Normal))).ToList();
                // Snapshot eligibility before changing verdicts so enumeration cannot change evidence.
                var companions=sets.Where(g=>g.All(f=>f.Status=="OK")
                    && ValidateChain(g.Select(f=>f.Point).ToList(),g[0].Normal,0)==null).ToList();
                foreach(var set in sets)
                {
                    var normal=set[0].Normal;
                    double dx,dy;
                    if(!TryMeasurementAxis(normal,out dx,out dy)) continue;
                    foreach(int start in new[]{0,1})
                    {
                        var anchor=set[start]; var target=set[1-start];
                        if(anchor.Status=="REVIEW" || target.Status=="REVIEW") continue;
                        // A full main envelope dimension and a notch depth can share a
                        // shoulder. They are different valid measurements, not competing feet.
                        if(IsOverallSpan(anchor.Point,target.Point,dx,dy,edges)) continue;
                        var candidates=new List<G.Point>();
                        foreach(var companion in companions.Where(g=>g[0].Dimension!=anchor.Dimension))
                        {
                            var otherNormal=companion[0].Normal;
                            double ox,oy;
                            if(!TryMeasurementAxis(otherNormal,out ox,out oy) || Math.Abs(dx*ox+dy*oy)>1e-8) continue;
                            foreach(int i in new[]{0,1})
                            {
                                if(Distance2(anchor.Point,companion[i].Point)>Tolerance) continue;
                                var expected=companion[1-i].Point;
                                if(ProvesEndNotch(anchor.Point,expected,dx,dy,edges)) candidates.Add(expected);
                            }
                        }
                        if(candidates.Count==0) continue;
                        double coordinate=candidates[0].X*dx+candidates[0].Y*dy;
                        if(candidates.Any(p=>Math.Abs(p.X*dx+p.Y*dy-coordinate)>Tolerance)) continue;
                        double delta=Math.Abs(target.Point.X*dx+target.Point.Y*dy-coordinate);
                        if(delta<=Tolerance) continue;
                        double expectedSize=Math.Abs(coordinate-anchor.Point.X*dx-anchor.Point.Y*dy);
                        target.Status="ERROR";
                        target.Reason="NOTCH: Chân đang bắt sang mốc khác của thanh chính. Cặp kích thước và biên dạng rãnh xác định mép cần đo ở "
                            +expectedSize.ToString("0.###",CultureInfo.InvariantCulture)+" mm tính từ chân còn lại; chân hiện tại lệch "
                            +delta.ToString("0.###",CultureInfo.InvariantCulture)+" mm theo phương đo.";
                    }
                }
            }
        }
        private static double Distance2(G.Point a,G.Point b)
        { double x=a.X-b.X,y=a.Y-b.Y; return Math.Sqrt(x*x+y*y); }
        private static bool IsOverallSpan(G.Point a,G.Point b,double dx,double dy,List<Feature> edges)
        {
            if(edges.Count==0) return false;
            var coordinates=edges.SelectMany(e=>new[]{e.A.X*dx+e.A.Y*dy,e.B.X*dx+e.B.Y*dy});
            double min=coordinates.Min(),max=coordinates.Max(),ta=a.X*dx+a.Y*dy,tb=b.X*dx+b.Y*dy;
            return (Math.Abs(ta-min)<=Tolerance && Math.Abs(tb-max)<=Tolerance)
                || (Math.Abs(tb-min)<=Tolerance && Math.Abs(ta-max)<=Tolerance);
        }
        private static bool ProvesEndNotch(G.Point a,G.Point b,double dx,double dy,List<Feature> edges)
        {
            if(edges.Count==0) return false;
            Func<G.Point,double> t=p=>p.X*dx+p.Y*dy;
            Func<G.Point,double> n=p=>-p.X*dy+p.Y*dx;
            double ta=t(a),tb=t(b),na=n(a),nb=n(b);
            if(Math.Abs(tb-ta)<=Tolerance || Math.Abs(nb-na)<=Tolerance) return false;
            var vertices=edges.SelectMany(e=>new[]{e.A,e.B}).ToList();
            double minT=vertices.Min(t),maxT=vertices.Max(t),minN=vertices.Min(n),maxN=vertices.Max(n);
            // Outer shoulder -> internal notch wall at a real member end.
            if(Math.Min(Math.Abs(ta-minT),Math.Abs(ta-maxT))>Tolerance
                || tb<=minT+Tolerance || tb>=maxT-Tolerance
                || Math.Min(Math.Abs(nb-minN),Math.Abs(nb-maxN))>Tolerance
                || na<=minN+Tolerance || na>=maxN-Tolerance) return false;
            Func<G.Point,bool> inside=p=>t(p)>=Math.Min(ta,tb)-Tolerance && t(p)<=Math.Max(ta,tb)+Tolerance
                && n(p)>=Math.Min(na,nb)-Tolerance && n(p)<=Math.Max(na,nb)+Tolerance;
            var local=edges.Where(e=>inside(e.A)&&inside(e.B)
                // Do not shortcut through the member's end cap or outer bounding edge.
                && !(Math.Abs(n(e.A)-nb)<=Tolerance && Math.Abs(n(e.B)-nb)<=Tolerance)
                && !(Math.Abs(t(e.A)-ta)<=Tolerance && Math.Abs(t(e.B)-ta)<=Tolerance)).ToList();
            if(!local.Any(e=>(Distance2(e.A,a)<=Tolerance||Distance2(e.B,a)<=Tolerance)
                && Math.Abs(n(e.A)-n(e.B))<=Tolerance && Math.Abs(t(e.A)-t(e.B))>Tolerance)) return false;
            if(!local.Any(e=>(Distance2(e.A,b)<=Tolerance||Distance2(e.B,b)<=Tolerance)
                && Math.Abs(t(e.A)-t(e.B))<=Tolerance && Math.Abs(n(e.A)-n(e.B))>Tolerance)) return false;
            var queue=new Queue<G.Point>(); var visited=new List<G.Point>(); queue.Enqueue(a);
            while(queue.Count>0)
            {
                var point=queue.Dequeue();
                if(visited.Any(p=>Distance2(p,point)<=Tolerance)) continue;
                visited.Add(point);
                if(Distance2(point,b)<=Tolerance) return true;
                foreach(var edge in local)
                {
                    if(Distance2(point,edge.A)<=Tolerance) queue.Enqueue(edge.B);
                    if(Distance2(point,edge.B)<=Tolerance) queue.Enqueue(edge.A);
                }
            }
            return false;
        }
        private static void Extract(M.Part part, bool primary, bool connected, bool dummy,
            G.Matrix global,G.Matrix view,List<Feature> output)
        {
            int id=part.Identifier.ID;
            bool plate=part is M.ContourPlate || (part.Profile.ProfileString??String.Empty).StartsWith("PL",StringComparison.OrdinalIgnoreCase);
            bool referenceAllowed=ReferenceAllowed(primary,connected,plate,dummy);
            Func<G.Point,G.Point> transform=p=>view.Transform(global.Transform(p));
            Action<string,G.Point,G.Point,bool> add=(kind,a,b,allowed)=>
            {
                var x=transform(a); var y=transform(b);
                if(!Finite(x)||!Finite(y)) throw new InvalidOperationException("Geometry không hữu hạn.");
                output.Add(new Feature { Owner=id,Kind=kind,A=x,B=y,Allowed=allowed,
                    ConnectedPart=connected&&!primary,ExternalMemberReference=connected&&!primary&&!plate&&kind.StartsWith("REF",StringComparison.Ordinal) });
            };
            var solid=part.GetSolid(); var edges=solid.GetEdgeEnumerator();
            int edgeCount=0;
            while(edges.MoveNext())
            {
                var edge=edges.Current as Tekla.Structures.Solid.Edge; if(edge==null) continue;
                edgeCount++;
                add("EDGE",edge.StartPoint,edge.EndPoint,primary&&!dummy);
                add("VERTEX",edge.StartPoint,edge.StartPoint,primary&&!dummy);
                add("VERTEX",edge.EndPoint,edge.EndPoint,primary&&!dummy);
            }
            if(edgeCount==0) throw new InvalidOperationException("Solid không có cạnh đọc được để xác minh hình học.");
            var refs=part.GetReferenceLine(false).Cast<G.Point>().ToList();
            for(int i=0;i<refs.Count;i++)
            {
                add("REF-POINT",refs[i],refs[i],referenceAllowed);
                if(i>0) add("REF",refs[i-1],refs[i],referenceAllowed);
            }
            var bolts=part.GetBolts();
            while(bolts.MoveNext())
            {
                var bolt=bolts.Current as M.BoltGroup; if(bolt==null) continue;
                foreach(G.Point point in bolt.BoltPositions) add("HOLE/BOLT:"+bolt.Identifier.ID,point,point,primary&&!dummy);
            }
        }
        public static bool ReferenceAllowed(bool primary,bool connected,bool plate,bool dummy)
        { return !dummy && !plate && (primary || connected); }
        // Slot02 semantics: use the intersection of straight reference axes, with
        // a local-connection bound check. A connected member alone is not enough.
        // Existing grid features are handled independently by ExtractGrids.
        public static void ConstrainReferences(List<Feature> features,int mainId)
        {
            features.RemoveAll(f=>f.Kind=="REF-INTERSECTION");
            var main=features.Where(f=>f.Owner==mainId && f.Allowed && f.Kind.StartsWith("REF",StringComparison.Ordinal))
                .SelectMany(f=>new[]{f.A,f.B}).ToList();
            var candidates=features.Where(f=>f.ExternalMemberReference && f.Kind.StartsWith("REF",StringComparison.Ordinal))
                .GroupBy(f=>f.Owner).ToList();
            foreach(var feature in features.Where(f=>f.Owner!=mainId && f.Kind.StartsWith("REF",StringComparison.Ordinal))) feature.Allowed=false;
            G.Point a,b;
            if(!StraightAxis(main,out a,out b) || Distance2(a,b)<=Tolerance) return;
            double mx=b.X-a.X,my=b.Y-a.Y,ml=Distance2(a,b); mx/=ml;my/=ml;
            var mainVertices=features.Where(f=>f.Owner==mainId && f.Kind=="EDGE").SelectMany(f=>new[]{f.A,f.B}).ToList();
            if(mainVertices.Count==0) return;
            foreach(var group in candidates)
            {
                G.Point c,d;
                if(!StraightAxis(group.SelectMany(f=>new[]{f.A,f.B}).ToList(),out c,out d)) continue;
                double nl=Distance2(c,d),nx=0,ny=0;
                G.Point intersection;
                if(nl<=Tolerance)
                {
                    // End-on view: the actual neighbor REF projects to one point.
                    if(Math.Abs(mx*(c.Y-a.Y)-my*(c.X-a.X))>Tolerance) continue;
                    intersection=c; nx=-my;ny=mx;
                }
                else
                {
                    nx=(d.X-c.X)/nl;ny=(d.Y-c.Y)/nl;
                    double cross=mx*ny-my*nx;
                    if(Math.Abs(cross)<=0.000001) continue;
                    double parameter=((c.X-a.X)*ny-(c.Y-a.Y)*nx)/cross;
                    intersection=new G.Point(a.X+parameter*mx,a.Y+parameter*my,0);
                    if(!Finite(intersection)) continue;
                }
                var neighborVertices=features.Where(f=>f.Owner==group.Key && f.Kind=="EDGE").SelectMany(f=>new[]{f.A,f.B}).ToList();
                if(neighborVertices.Count==0) continue;
                Func<G.Point,double> alongMain=p=>p.X*mx+p.Y*my;
                Func<G.Point,double> alongNeighbor=p=>p.X*nx+p.Y*ny;
                double mainMin=mainVertices.Min(alongMain),mainMax=mainVertices.Max(alongMain);
                double neighborMin=neighborVertices.Min(alongNeighbor),neighborMax=neighborVertices.Max(alongNeighbor);
                double mainClearance=Math.Max(Tolerance,neighborVertices.Max(alongMain)-neighborVertices.Min(alongMain));
                double neighborClearance=Math.Max(Tolerance,mainVertices.Max(alongNeighbor)-mainVertices.Min(alongNeighbor));
                if(IntervalDistance(alongMain(intersection),mainMin,mainMax)>mainClearance
                    || IntervalDistance(alongNeighbor(intersection),neighborMin,neighborMax)>neighborClearance) continue;
                features.Add(new Feature { Owner=group.Key,Kind="REF-INTERSECTION",A=intersection,B=intersection,
                    Allowed=true,ConnectedPart=true,ExternalMemberReference=true });
            }
        }
        private static double IntervalDistance(double value,double min,double max)
        { return value<min?min-value:value>max?value-max:0; }
        private static bool StraightAxis(List<G.Point> points,out G.Point a,out G.Point b)
        {
            a=null;b=null;
            if(points.Count==0 || points.Any(p=>!Finite(p))) return false;
            a=points[0];b=a;double best=0;
            for(int i=0;i<points.Count;i++) for(int j=i+1;j<points.Count;j++)
            { double distance=Distance2(points[i],points[j]); if(distance>best){best=distance;a=points[i];b=points[j];} }
            if(best<=Tolerance) return true;
            double x=b.X-a.X,y=b.Y-a.Y;
            foreach(var p in points) if(Math.Abs(x*(p.Y-a.Y)-y*(p.X-a.X))/best>Tolerance) return false;
            return true;
        }
        private static bool ConstantProjection(Feature f,double dx,double dy)
        {
            double x=f.B.X-f.A.X,y=f.B.Y-f.A.Y,length=Math.Sqrt(x*x+y*y);
            return f.Infinite ? length>1e-9 && Math.Abs((x*dx+y*dy)/length)<1e-8
                : Math.Abs(x*dx+y*dy)<=Tolerance;
        }

        private static void ExtractGrids(M.Model model,D.View view,G.Matrix global,G.Matrix matrix,
            List<Feature> features,List<string> warnings)
        {
            var grids=view.GetAllObjects(typeof(D.GridLine));
            var seen=new HashSet<int>();
            while(grids.MoveNext())
            {
                var line=(D.GridLine)grids.Current;
                if(!seen.Add(line.ModelIdentifier.ID)) continue;
                try
                {
                    var grid=model.SelectModelObject(line.ModelIdentifier) as M.GridPlane;
                    if(grid==null || grid.Plane==null) throw new InvalidOperationException("Không đọc được grid plane.");
                    var plane=grid.Plane;
                    var origin=matrix.Transform(global.Transform(plane.Origin));
                    var x=matrix.Transform(global.Transform(new G.Point(plane.Origin.X+plane.AxisX.X,
                        plane.Origin.Y+plane.AxisX.Y,plane.Origin.Z+plane.AxisX.Z)));
                    var y=matrix.Transform(global.Transform(new G.Point(plane.Origin.X+plane.AxisY.X,
                        plane.Origin.Y+plane.AxisY.Y,plane.Origin.Z+plane.AxisY.Z)));
                    double ax=x.X-origin.X,ay=x.Y-origin.Y,bx=y.X-origin.X,by=y.Y-origin.Y;
                    double a=Math.Sqrt(ax*ax+ay*ay),b=Math.Sqrt(bx*bx+by*by);
                    // Only an edge-on plane has a unique projected grid line.
                    if(Math.Max(a,b)<1e-9 || (a>1e-9 && b>1e-9 && Math.Abs(ax*by-ay*bx)>1e-8*a*b))
                        throw new InvalidOperationException("Mặt phẳng trục không chiếu thành một đường duy nhất.");
                    var end=a>=b?x:y;
                    if(!Finite(origin)||!Finite(end)) throw new InvalidOperationException("Tọa độ trục không hữu hạn.");
                    features.Add(new Feature { Owner=grid.Identifier.ID,Kind="GRID "+grid.Label,
                        A=origin,B=end,Allowed=true,Infinite=true });
                }
                catch(Exception ex) { warnings.Add("VIEW="+Id(view)+" GRID="+line.ModelIdentifier.ID+" "+ex.Message); }
            }
        }
        private static string ViewLabel(D.View view)
        {
            string type=view.ViewType.ToString();
            string label=type=="FrontView"?"Hình chiếu đứng":type=="TopView"?"Hình chiếu bằng":type=="SectionView"?"Mặt cắt":"Hình chiếu";
            if(!String.IsNullOrWhiteSpace(view.Name)) label+=" "+view.Name;
            return label;
        }
        private static string DimensionLabel(List<G.Point> points,G.Vector normal)
        {
            double dx,dy;
            if(points.Count<2 || points.Any(p=>!Finite(p)) || !TryMeasurementAxis(normal,out dx,out dy)) return "chưa đọc được";
            var origin=points[0];
            var values=points.Select(p=>(p.X-origin.X)*dx+(p.Y-origin.Y)*dy).OrderBy(x=>x).ToList();
            return String.Join(" – ",values.Skip(1).Select((v,i)=>(v-values[i]).ToString("0.###",CultureInfo.InvariantCulture)))+" mm";
        }
        private static string Location(G.Point point,List<G.Point> points,G.Vector normal,int index)
        {
            if(point==null || normal==null) return "Chân "+(index+1);
            bool vertical=Math.Abs(normal.X)>Math.Abs(normal.Y);
            var ordered=points.Where(Finite).OrderBy(p=>vertical?p.Y:p.X).ToList();
            if(ordered.Count==0) return "Chân "+(index+1);
            int rank=ordered.IndexOf(point);
            string position=rank==0?(vertical?"Chân dưới cùng":"Chân bên trái"):
                rank==ordered.Count-1?(vertical?"Chân trên cùng":"Chân bên phải"):"Chân giữa số "+(rank+1);
            return position;
        }
        internal static string UserReason(Finding finding)
        {
            if(finding.Status=="REVIEW") return finding.Reason!=null && finding.Reason.StartsWith("CHAIN: ",StringComparison.Ordinal)
                ? finding.Reason.Substring(7) : "Dữ liệu chưa đủ hoặc chưa hợp lệ để kết luận chân kích thước đúng hay sai. Hãy đối chiếu trực tiếp trên bản vẽ; mở ‘Xem phạm vi’ để biết chi tiết.";
            if(String.IsNullOrEmpty(finding.Reason)) return "Cần đối chiếu chân kích thước với mốc đo dự kiến.";
            if(finding.Reason.StartsWith("NOTCH: ",StringComparison.Ordinal)) return finding.Reason.Substring(7);
            if(finding.Reason.StartsWith("NEIGHBOR_REF: ",StringComparison.Ordinal)) return finding.Reason.Substring(14);
            string reason=finding.Reason.StartsWith("Chân nằm trên neighbor",StringComparison.Ordinal)
                ? "Chân đang trùng với chi tiết lân cận, nhưng không có mốc đo hợp lệ thuộc cấu kiện chính, plate liên kết hoặc trục tham chiếu."
                : "Vị trí chân không khớp mép, tâm lỗ, đường tham chiếu hoặc trục lưới hợp lệ theo phương đo. Có thể đã bắt nhầm điểm.";
            if(!Double.IsNaN(finding.MeasurementResidual) && !Double.IsInfinity(finding.MeasurementResidual))
                reason+=" Lệch mốc hợp lệ gần nhất "+finding.MeasurementResidual.ToString("0.###",CultureInfo.InvariantCulture)
                    +" mm theo phương đo. Mốc gần nhất chỉ là gợi ý để đối chiếu.";
            return reason;
        }
        public static void ShowReport(Result result)
        {
            using (var report = new PHU_DimensionReportForm(result, PHU_DimensionCheck.ReportDarkMode)) report.ShowDialog();
        }
    }
}
