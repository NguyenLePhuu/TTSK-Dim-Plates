// Source: PHU_DimensionRevision.cs
namespace Tekla.Technology.Akit.UserScript
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;


    // DPM contains presentation geometry, not a historical Drawing API database.
    // Match presentation geometry/topology; historical child IDs are only supporting evidence.
    public static partial class PHU_DimensionRevision
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        [Serializable]
        public sealed class Segment
        {
            public int Id;
            public double Value, A, B, X, Y;
            public double PaperA, PaperB, Level;
            public bool HasPaperGeometry;
            public double FootA, FootB;
            public bool HasFeet;
            // DPM witness starts are clipped by extension gaps; they are NOT model feet.
            // Keep their provenance so rendered corridors can support regenerated chains.
            public bool HasWitnessGeometry;
            public string Text;
        }
        [Serializable]
        public sealed class SnapshotView
        {
            public int Id;
            public double X, Y;
            public List<Segment> Segments = new List<Segment>();
            public List<ContentBlock> Content = new List<ContentBlock>();
            public List<int> UnsupportedDimensions = new List<int>();
            public List<RevisionPartBounds> PartBounds = new List<RevisionPartBounds>();
        }
        public sealed class Item
        {
            public int Id, ViewId;
            public string View, Before, After, Reason, Signature, SetSignature, Message;
            // Presentation data only; comparison and snapshot matching keep their existing rules.
            public ContentBlock ContentBefore, ContentAfter;
            public bool Changed, Review, AlreadyRed, IsNew, ReadOnly;
            public D.DrawingColors? NativeColor;
            public D.StraightDimensionSet Object;
            public D.StraightDimension Dimension;
            public List<Segment> Segments = new List<Segment>();
        }
        public sealed class Result
        {
            public D.Drawing Drawing;
            public string ModelPath, BaselinePath, BaselineHash;
            public DateTime BaselineTime;
            public List<Item> Items = new List<Item>();
            public List<Item> UnchangedItems = new List<Item>();
            public List<string> Warnings = new List<string>();
            public int Unchanged, Compared;
            public string Report()
            {
                return "CHECK REVISION (read only)\r\n" + (Drawing == null ? "" : Drawing.Mark) +
                    "\r\nBaseline: " + BaselinePath + "\r\nSHA256: " + BaselineHash +
                    "\r\nCompared=" + Compared + " Unchanged=" + Unchanged + "\r\n" +
                    String.Join("\r\n", Items.Select(i => i.View + " ID=" + i.Id + " " + i.Before + " -> " + i.After +
                        " changed=" + i.Changed + " review=" + i.Review + " red=" + i.AlreadyRed + " " + i.Reason)) +
                    "\r\n" + String.Join("\r\n", Warnings);
            }
        }
        static object Member(object value, string name)
        {
            if (value == null) return null;
            var property = value.GetType().GetProperty(name, Members);
            return property == null ? null : property.GetValue(value, null);
        }
        static int Id(object value)
        { var id = Member(value, "Identifier") as Tekla.Structures.Identifier; return id == null ? 0 : id.ID; }
        static double Number(object value, string name)
        { return Convert.ToDouble(Member(value, name), CultureInfo.InvariantCulture); }
        static IEnumerable<object> Children(object value)
        { var list = Member(value, "Primitives") as IEnumerable; return list == null ? Enumerable.Empty<object>() : list.Cast<object>(); }
        static IEnumerable<object> Descendants(object value)
        {
            foreach (var child in Children(value))
            { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            // Combined dimensions, tolerances, fractions, prefixes and overrides need review.
            if (text == null || !Regex.IsMatch(text.Trim(), @"^\d+(?:[.,]\d+)?$")) return false;
            return Double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value) && value > 0 && !Double.IsInfinity(value);
        }
        static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static string ModelKey(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).TrimEnd('\\').ToUpperInvariant()))).Replace("-", ""); }

        static string Baseline(M.Model model, D.Drawing drawing, bool preserve)
        {
            var id = Member(drawing, "Identifier") as Tekla.Structures.Identifier;
            if (id == null || id.GUID == Guid.Empty) throw new InvalidOperationException("Không đọc được định danh bản vẽ.");
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates", "RevisionBaselines", ModelKey(model.GetInfo().ModelPath), id.GUID + ".DPM");
            if (File.Exists(cache)) return cache;
            var util = typeof(D.Drawing).Assembly.GetType("Tekla.Structures.Drawing.Internal.DgUtil", true);
            var method = util.GetMethod("GetDgFileNameByDrawingGuid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new NotSupportedException("Tekla không hỗ trợ tìm snapshot gốc.");
            string dg = Convert.ToString(method.Invoke(null, new object[] { id.GUID }));
            if (String.IsNullOrWhiteSpace(dg)) throw new InvalidOperationException("Không xác định được file snapshot.");
            string source = Path.Combine(model.GetInfo().ModelPath, "drawings", "Snapshots", Path.GetFileName(dg) + ".DPM");
            if (!File.Exists(source)) throw new FileNotFoundException("Bản vẽ chưa có snapshot gốc. Không thể kết luận Revision.", source);
            if (!preserve) return source;
            // Freeze the first baseline: saving a revised drawing must not silently replace it.
            Directory.CreateDirectory(Path.GetDirectoryName(cache));
            string temporary = cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary, false);
                if (Hash(source) != Hash(temporary)) throw new IOException("Snapshot thay đổi trong lúc đọc. Hãy kiểm tra lại.");
                File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(source));
                try { File.Move(temporary, cache); }
                catch (IOException) { if (!File.Exists(cache)) throw; }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return cache;
        }

        [Serializable]
        public sealed class SnapshotData
        {
            public List<SnapshotView> Views;
            public List<string> Warnings = new List<string>();
        }
        public sealed partial class SnapshotReader : MarshalByRefObject
        {
            public override object InitializeLifetimeService() { return null; }
            public SnapshotData Read(string path)
            {
                var result = new SnapshotData();
                result.Views = ReadSnapshotCore(path, result.Warnings);
                return result;
            }
        }
        public static List<SnapshotView> ReadSnapshot(string path, List<string> warnings)
        {
            lock(PresentationReaderLock)
            {
                string fullPath=Path.GetFullPath(path),hash=Hash(fullPath);
                if(cachedSnapshot==null||!String.Equals(cachedSnapshotPath,fullPath,StringComparison.OrdinalIgnoreCase)||cachedSnapshotHash!=hash)
                {
                    var data=UsePresentationReader(reader=>reader.Read(fullPath));
                    if(Hash(fullPath)!=hash)throw new IOException("Snapshot thay đổi trong lúc đọc. Hãy kiểm tra lại.");
                    cachedSnapshot=CopyValue(data);cachedSnapshotPath=fullPath;cachedSnapshotHash=hash;
                }
                // Callers mutate matching data. Always return an independent graph.
                var copy=CopyValue(cachedSnapshot);warnings.AddRange(copy.Warnings);return copy.Views;
            }
        }
        static readonly object PresentationReaderLock=new object();
        static AppDomain presentationDomain;
        static SnapshotReader presentationReader;
        static string cachedSnapshotPath,cachedSnapshotHash;
        static SnapshotData cachedSnapshot;
        static SnapshotData UsePresentationReader(Func<SnapshotReader,SnapshotData> read)
        {
            lock(PresentationReaderLock)
            {
                try
                {
                    if(presentationReader==null)
                    {
                        // Reuse one isolated binding context; live data is still fetched
                        // from Tekla on every analysis. No drawing/API handles are cached.
                        string bin=Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
                        presentationDomain=AppDomain.CreateDomain("TTSK Revision presentation "+Guid.NewGuid(),null,
                            new AppDomainSetup{ApplicationBase=bin,ConfigurationFile=Path.Combine(bin,"TeklaStructures.exe.config")});
                        presentationReader=(SnapshotReader)presentationDomain.CreateInstanceFromAndUnwrap(typeof(SnapshotReader).Assembly.Location,typeof(SnapshotReader).FullName);
                    }
                    return read(presentationReader);
                }
                catch
                {
                    var failed=presentationDomain;presentationDomain=null;presentationReader=null;
                    cachedSnapshot=null;cachedSnapshotPath=null;cachedSnapshotHash=null;
                    if(failed!=null)try{AppDomain.Unload(failed);}catch{}
                    throw;
                }
            }
        }
        static List<SnapshotView> ReadSnapshotCore(string path, List<string> warnings)
        {
            string bin = Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
            var assembly = Assembly.LoadFrom(Path.Combine(bin, "DPMPrinter.dll"));
            var type = assembly.GetType("Tekla.Structures.DPMPrinter.DpmData", true);
            object data = Activator.CreateInstance(type);
            type.GetMethod("LoadFromFile").Invoke(data, new object[] { path });
            var raw = Member(data, "Views") as IEnumerable;
            if (raw == null) throw new InvalidDataException("Không đọc được các hình chiếu trong snapshot.");
            return ParsePresentation(raw,warnings);
        }
        static List<SnapshotView> ParsePresentation(IEnumerable raw,List<string> warnings)
        {
            var result = new List<SnapshotView>();
            foreach (var view in raw)
            {
                int viewId = (int)Number(view, "Id");
                var position = Member(view, "Position");
                var target = new SnapshotView { Id = viewId, X = Number(position, "X"), Y = Number(position, "Y") };
                result.Add(target);
                target.Content = ReadContent(view);
                target.PartBounds = ReadRevisionPartBounds(view);
                foreach (var group in Children(view))
                {
                    // Tekla 2025 DPM Segment.ObjectType 256 = straight dimension.
                    if (group.GetType().Name != "Segment" || (int)Number(group, "ObjectType") != 256) continue;
                    var primitives = Descendants(group).ToList();
                    // Main value is nested in a Segment; external tags (e.g. 切り欠き)
                    // are siblings and must never participate in numeric comparison.
                    var valueGroups = primitives.Where(p => p.GetType().Name == "Segment" &&
                        (int)Number(p,"Id") == 0 && (int)Number(p,"ObjectType") == 256).ToList();
                    var texts = valueGroups.SelectMany(p => Children(p)).Where(p => p.GetType().Name == "TextPrimitive").ToList();
                    var points = primitives.Where(p => p.GetType().Name == "PointPrimitive").Select(p => Member(p, "Position")).ToList();
                    double value;
                    if (texts.Count != 1 || points.Count != 2 || !TryNumber(Convert.ToString(Member(texts[0], "Text")), out value))
                    { target.UnsupportedDimensions.Add((int)Number(group,"Id")); warnings.Add("Snapshot: DIM " + Number(group, "Id") + " có nội dung/định dạng chưa hỗ trợ."); continue; }
                    double dx=Number(points[1],"X")-Number(points[0],"X"),dy=Number(points[1],"Y")-Number(points[0],"Y");
                    double length=Math.Sqrt(dx*dx+dy*dy);
                    if(!Finite(length)||length<.001)
                    {target.UnsupportedDimensions.Add((int)Number(group,"Id"));warnings.Add("Snapshot: DIM không có khoảng đo hợp lệ.");continue;}
                    // Arrow stations define the measurement axis. Text can be horizontal,
                    // reversed, or rounded independently of an inclined dimension line.
                    double x=dx/length,y=dy/length;
                    // Canonical direction makes reversed endpoint/enumeration order equivalent.
                    if (x < -0.0001 || (Math.Abs(x) < 0.0001 && y < 0)) { x = -x; y = -y; }
                    if (Math.Abs(x) < 0.0001) x = 0;
                    if (Math.Abs(y) < 0.0001) y = 0;
                    double a = Number(points[0], "X") * x + Number(points[0], "Y") * y;
                    double b = Number(points[1], "X") * x + Number(points[1], "Y") * y;
                    if (!Finite(a)||!Finite(b)||Math.Abs(a-b)<.001)
                    {target.UnsupportedDimensions.Add((int)Number(group,"Id"));warnings.Add("Snapshot: DIM không có khoảng đo hợp lệ.");continue;}
                    // Scale derived from displayed value and arrow span is used only for matching.
                    // Value comparison uses the actual printed number, not this reconstructed geometry.
                    double scale = value / Math.Abs(a - b);
                    var segment=new Segment { Id = (int)Number(group, "Id"), Value = value,
                        Text = Convert.ToString(Member(texts[0], "Text")), X = x, Y = y,
                        A = Math.Min(a, b) * scale, B = Math.Max(a, b) * scale,
                        PaperA=Math.Min(a,b), PaperB=Math.Max(a,b), HasPaperGeometry=true,
                        Level=-Number(points[0],"X")*y+Number(points[0],"Y")*x };
                    ReadWitnessFeet(segment,primitives);
                    target.Segments.Add(segment);
                }
            }
            if (result.Count==0) throw new InvalidDataException("Snapshot không có dữ liệu đọc được.");
            return result;
        }

        static List<Segment> ReadSegments(D.StraightDimensionSet set)
        {return ReadSegmentsFromChildren(set,ChildrenOf(set));}
        static List<Segment> ReadSegmentsFromChildren(D.StraightDimensionSet set,IEnumerable<D.StraightDimension> children)
        {
            var result = new List<Segment>();
            var normal = Member(set, "UpDirection") as Tekla.Structures.Geometry3d.Vector;
            if (normal == null) throw new InvalidDataException("Không đọc được hướng đo.");
            double length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
            if (length < 1e-9 || !Finite(length)) throw new InvalidDataException("Hướng đo không hợp lệ.");
            double x = normal.Y / length, y = -normal.X / length;
            if (x < -0.0001 || (Math.Abs(x) < 0.0001 && y < 0)) { x = -x; y = -y; }
            foreach(var child in children)
            {
                if (child == null) continue;
                string text = String.Concat(child.Value.Cast<object>().OfType<D.TextElement>().Select(e => e.Value));
                double value;
                if (!TryNumber(text, out value)) throw new InvalidDataException("DIM có giá trị/định dạng chưa hỗ trợ: " + text);
                double a = child.StartPoint.X * x + child.StartPoint.Y * y;
                double b = child.EndPoint.X * x + child.EndPoint.Y * y;
                if (Double.IsNaN(a + b) || Double.IsInfinity(a + b) || Math.Abs(a - b) < 1e-6)
                    throw new InvalidDataException("Chân DIM không hợp lệ.");
                // DPM has no reliable historical unit metadata. Do not equate cm/inch values to mm.
                if (Math.Abs(value - Math.Abs(a - b)) > 1.01)
                    throw new InvalidDataException("DIM không theo mm, bị ghi đè hoặc định dạng đặc biệt; cần đối chiếu thủ công.");
                result.Add(new Segment { Id = Id(child), Value = value, Text = text, A = Math.Min(a, b), B = Math.Max(a, b), X = x, Y = y });
            }
            if (result.Count == 0) throw new InvalidDataException("Không đọc được các đoạn trong chuỗi.");
            return result.OrderBy(s => s.A).ThenBy(s => s.B).ThenBy(s => s.Id).ToList();
        }
        static string Signature(List<Segment> segments)
        { return String.Join("|", segments.Select(s => s.Id + ":" + s.Text + ":" + s.A.ToString("R", CultureInfo.InvariantCulture) + ":" + s.B.ToString("R", CultureInfo.InvariantCulture) + ":" + s.X.ToString("R", CultureInfo.InvariantCulture) + ":" + s.Y.ToString("R", CultureInfo.InvariantCulture))); }

        public static Result Analyze(bool preserveBaseline = true)
        {
            var model = new M.Model(); var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new InvalidOperationException("Không kết nối Tekla.");
            var drawing = handler.GetActiveDrawing();
            if (drawing == null) throw new InvalidOperationException("Chưa mở bản vẽ.");
            var result = new Result { Drawing = drawing, ModelPath = model.GetInfo().ModelPath };
            result.BaselinePath = Baseline(model, drawing, preserveBaseline);
            result.BaselineHash = Hash(result.BaselinePath);
            result.BaselineTime = File.GetLastWriteTime(result.BaselinePath);
            var old = ReadSnapshot(result.BaselinePath, result.Warnings);
            var current = ReadCurrentPresentation(result.Warnings);
            var views = new List<D.View>(); var iterator = drawing.GetSheet().GetAllViews();
            while (iterator.MoveNext()) { var view = iterator.Current as D.View; if (view != null) views.Add(view); }
            foreach (var view in views)
            {
                var baseline = old.SingleOrDefault(v => v.Id == Id(view));
                var presentation=current.SingleOrDefault(v=>v.Id==Id(view));
                bool scaleReview=baseline!=null&&SnapshotScaleDiffers(baseline,view.Attributes.Scale);
                if(scaleReview)result.Warnings.Add("Hình chiếu "+Id(view)+": tỷ lệ snapshot khác hiện hành; cần đối chiếu thủ công.");
                var paperById=presentation==null?null:presentation.Segments.ToLookup(s=>s.Id);
                var chains=Chains(baseline==null?new List<Segment>():baseline.Segments);
                var objects = view.GetAllObjects(); var seen = new HashSet<int>();
                var sets=new List<D.StraightDimensionSet>();
                while (objects.MoveNext())
                {
                    var obj = objects.Current;
                    if (obj is D.StraightDimension) continue;
                    var set = obj as D.StraightDimensionSet;
                    if (set == null)
                    { if (obj is D.DimensionBase) result.Warnings.Add("Hình chiếu " + Id(view) + ": " + obj.GetType().Name + " chưa hỗ trợ Revision."); continue; }
                    if (!seen.Add(Id(set))) continue;
                    sets.Add(set);
                }
                var members=new Dictionary<int,Item>();bool incompleteCurrent=false;
                foreach(var set in sets)
                {
                    try
                    {
                        var children=ChildrenOf(set).ToDictionary(Id);
                        var segments=ReadSegmentsFromChildren(set,children.Values);
                        var pending=new List<Item>();string setSignature=ChildrenGeometryKey(children.Values);
                        foreach(var segment in segments)
                        {
                            var paper=paperById==null?null:paperById[segment.Id].SingleOrDefault();
                            if(paper==null||Math.Abs(paper.Value-segment.Value)>.0001)throw new InvalidDataException("Chưa đọc đủ hình học hiển thị của DIM; kiểm tra lại.");
                            segment.PaperA=paper.PaperA;segment.PaperB=paper.PaperB;segment.Level=paper.Level;segment.HasPaperGeometry=true;
                            segment.FootA=paper.FootA;segment.FootB=paper.FootB;segment.HasFeet=paper.HasFeet;
                            segment.HasWitnessGeometry=paper.HasWitnessGeometry;
                            pending.Add(new Item {Id=segment.Id,ViewId=Id(view),View=ViewName(view),Object=set,Dimension=children[segment.Id],
                                Segments=new List<Segment>{segment},Signature=Signature(new List<Segment>{segment}),SetSignature=setSignature,After=segment.Text});
                        }
                        // An unreadable child invalidates the entire set, never publish a partial chain.
                        foreach(var item in pending)members.Add(item.Id,item);
                    }
                    catch(Exception ex){incompleteCurrent=true;result.Items.Add(new Item{Id=Id(set),ViewId=Id(view),View=ViewName(view),Object=set,Review=true,Before="?",After="Chưa đọc được",Reason=ex.GetBaseException().Message});}
                }
                // A visual chain may have multiple native sets after assigning different colours.
                var visualChains=Chains(members.Values.SelectMany(i=>i.Segments));
                bool[] chainReviews;var chainMatches=MatchChains(visualChains,chains,out chainReviews);
                for(int chainIndex=0;chainIndex<visualChains.Count;chainIndex++)
                {
                    var segments=visualChains[chainIndex];var chain=chainMatches[chainIndex];bool ambiguous=chainReviews[chainIndex];
                    var matched=MatchChainSegments(segments,chain);
                    for(int i=0;i<segments.Count;i++)
                    {
                        var segment=segments[i];var before=matched[i];var child=members[segment.Id];
                        bool review=RevisionReviewRequired(baseline,segments,chain,before,ambiguous,incompleteCurrent,scaleReview);
                        child.Before=before==null?(review?"?":"DIM mới"):before.Text;child.IsNew=before==null&&!review;
                        child.Changed=!review&&(before==null||Math.Abs(segment.Value-before.Value)>.0001);child.Review=review;
                        child.Reason=review?"Chưa đủ dữ liệu/không xác định duy nhất cụm DIM trong snapshot; không tự đổi màu.":
                            before==null?"Khoảng đo/cụm DIM chưa có trong snapshot. DIM mới.":"Đối chiếu hướng, khoảng đo và hình học nét dóng của cụm DIM; không coi đầu nét dóng là chân đo thật.";
                        result.Compared++;
                        if(!child.Changed&&!child.Review){result.Unchanged++;result.UnchangedItems.Add(child);}else result.Items.Add(child);
                    }
                }
            }
            CompareContent(result,old,current);
            if (Hash(result.BaselinePath) != result.BaselineHash) throw new IOException("Snapshot thay đổi trong lúc kiểm tra.");
            EnsureActive(result);
            var colorItems=result.Items.Concat(result.UnchangedItems).Where(i=>!i.ReadOnly&&i.Dimension!=null&&i.Object!=null).ToList();
            WriteColorPlans(result,colorItems.ToDictionary(i=>i,i=>D.DrawingColors.Black),true);
            return result;
        }
        static bool SnapshotScaleDiffers(SnapshotView snapshot,double scale)
        {
            if(!Finite(scale)||scale<=0)return true;
            // Allow printed rounding, but never compare two different paper scales as
            // though their station coordinates were in the same coordinate system.
            return snapshot.Segments.Any(s=>Math.Abs((s.PaperB-s.PaperA)*scale-s.Value)>1.01);
        }
        static bool RevisionReviewRequired(SnapshotView baseline,IList<Segment> current,IList<Segment> matchedChain,
            Segment matchedSegment,bool ambiguous,bool incompleteCurrent,bool scaleChanged)
        {
            // Failed extraction / changed coordinate system is never evidence of absence.
            return baseline==null||ambiguous||incompleteCurrent||scaleChanged||current.Any(s=>!s.HasFeet)||
                matchedChain!=null&&matchedChain.Any(s=>!s.HasFeet)||
                baseline.UnsupportedDimensions.Count>0&&matchedSegment==null;
        }
        static string ViewName(D.View view)
        {
            string type = view.ViewType.ToString();
            string name = type.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0 ? "Hình chiếu đứng" :
                type.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0 ? "Hình chiếu bằng" : "Hình chiếu " + type;
            return String.IsNullOrWhiteSpace(view.Name) ? name : name + " / " + view.Name;
        }
        static D.Drawing EnsureActive(Result result)
        {
            var model = new M.Model(); var drawing = new D.DrawingHandler().GetActiveDrawing();
            if (!model.GetConnectionStatus() || !String.Equals(model.GetInfo().ModelPath, result.ModelPath, StringComparison.OrdinalIgnoreCase) ||
                drawing == null || !drawing.IsSameDatabaseObject(result.Drawing))
                throw new InvalidOperationException("Bản vẽ/model đã thay đổi. Hãy kiểm tra lại Revision.");
            return drawing;
        }
        public static bool CanSelectMark(Item item)
        {
            return item!=null&&item.ReadOnly&&item.ContentAfter!=null&&
                item.ContentAfter.Kind=="Mark"&&item.ContentAfter.Id>0;
        }
        public static void Select(Result result, Item item)
        {
            if(item.ReadOnly&&!CanSelectMark(item))return;
            var active=EnsureActive(result);
            if(item.ReadOnly)
            {
                var sheet=active.GetSheet();
                D.ViewBase target=Id(sheet)==item.ViewId?sheet:null;
                var views=sheet.GetAllViews();
                while(target==null&&views.MoveNext())
                    if(Id(views.Current)==item.ViewId)target=views.Current as D.ViewBase;
                D.MarkBase mark=null;
                if(target!=null)
                {
                    var objects=target.GetAllObjects();
                    while(objects.MoveNext())
                    {
                        var candidate=objects.Current as D.MarkBase;
                        if(candidate!=null&&Id(candidate)==item.ContentAfter.Id){mark=candidate;break;}
                    }
                }
                if(mark==null||!mark.Select())
                    throw new InvalidOperationException("Mark không còn tồn tại. Hãy chạy lại Check Revision.");
                if(!new D.DrawingHandler().GetDrawingObjectSelector().SelectObjects(new ArrayList {mark},false))
                    throw new InvalidOperationException("Chưa chọn được mark trên bản vẽ. Hãy thử lại.");
                return;
            }
            D.DrawingObject selected=(D.DrawingObject)item.Dimension??item.Object;
            if (selected==null||!selected.Select()) throw new InvalidOperationException("DIM không còn tồn tại. Hãy kiểm tra lại.");
            if (!new D.DrawingHandler().GetDrawingObjectSelector().SelectObjects(new ArrayList { selected }, false))
                throw new InvalidOperationException("Không chọn được DIM trong Tekla.");
        }
        static D.StraightDimensionSet.StraightDimensionSetAttributes ChildAttributes(D.StraightDimension child)
        {
            // Open API returns inherited set formatting, not native per-child font overrides.
            var attributes=Member(child,"DimensionSetAttributes") as D.StraightDimensionSet.StraightDimensionSetAttributes;
            if(attributes==null)throw new NotSupportedException("Phiên bản Tekla này không hỗ trợ đọc màu riêng từng đoạn DIM.");
            return attributes;
        }
        public static D.DrawingColors GetColor(Item item)
        {
            if(item.Dimension==null)throw new InvalidOperationException("Chưa đọc được đoạn DIM riêng; không đổi màu cả chuỗi.");
            if(!item.NativeColor.HasValue)throw new InvalidOperationException("Chưa đọc được thuộc tính màu riêng của DIM; hãy kiểm tra lại.");
            return item.NativeColor.Value;
        }
        public static int Apply(Result result, IList<Item> chosen)
        {
            if(chosen.Any(i=>i==null||i.ReadOnly||i.Review||!i.Changed))
                throw new InvalidOperationException("Chỉ tự tô đỏ DIM đã xác định thay đổi; DIM cần kiểm tra phải được chọn màu thủ công.");
            var colors=chosen.Distinct().ToDictionary(i=>i,i=>D.DrawingColors.Red);
            // Unchanged siblings may have intentional user colours; never reset them implicitly.
            return SetColors(result,colors);
        }
        public static int SetColor(Result result,IList<Item> chosen,D.DrawingColors color)
        { return SetColors(result,chosen.Distinct().ToDictionary(i=>i,i=>color)); }
        public static int RestoreUnchangedColors(Result result,IList<Item> chosen,D.DrawingColors color)
        {
            if(chosen.Any(i=>i==null||i.Review||i.Changed||!result.UnchangedItems.Contains(i)))
                throw new InvalidOperationException("Chỉ khôi phục màu DIM đã xác định không đổi.");
            // A manual colour change between analysis and restoration must win.
            return WriteColorPlans(result,chosen.Distinct().ToDictionary(i=>i,i=>color),false,D.DrawingColors.Red);
        }
        static int SetColors(Result result,IDictionary<Item,D.DrawingColors> colors)
        {
            return WriteColorPlans(result,colors);
        }
    }
}

// Source: PHU_RevisionCreateDrawing.cs
namespace Tekla.Technology.Akit.UserScript
{
using System;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;
using M = Tekla.Structures.Model;


    public static partial class PHU_DimensionRevision
    {
        [Serializable]
        public sealed class RevisionPartBounds
        {
            public int Id;
            public double MinX, MaxX, MinY, MaxY;
        }
        static List<RevisionPartBounds> ReadRevisionPartBounds(object view)
        {
            var result = new List<RevisionPartBounds>();
            foreach (var group in Children(view).Where(p => p.GetType().Name == "Segment" && (int)Number(p,"ObjectType") == 259))
            {
                var points = Descendants(group).Where(p=>p.GetType().Name == "LinePrimitive")
                    .SelectMany(p=>new[]{Member(p,"StartPoint"),Member(p,"EndPoint")}).Where(p=>p!=null).ToList();
                if (points.Count == 0) continue;
                result.Add(new RevisionPartBounds { Id = (int)Number(group,"Id"),
                    MinX = points.Min(p=>Number(p,"X")), MaxX = points.Max(p=>Number(p,"X")),
                    MinY = points.Min(p=>Number(p,"Y")), MaxY = points.Max(p=>Number(p,"Y")) });
            }
            return result;
        }
        public static G.Point RevisionBalancedOrigin(SnapshotView old, RevisionPartBounds before, RevisionPartBounds after, double z)
        {
            // Keep the main part's bottom edge and horizontal centre at the snapshot's sheet coordinates.
            // Length changes expand/contract equally left/right; DIM/text extents never participate.
            return new G.Point(old.X + (before.MinX + before.MaxX - after.MinX - after.MaxX)/2,
                old.Y + before.MinY - after.MinY, z);
        }
        public sealed class RevisionLayout
        {
            public Result Baseline;
            public List<RevisionViewPosition> Views = new List<RevisionViewPosition>();
        }
        public sealed class RevisionViewPosition
        {
            public D.View View;
            public int Id;
            public G.Point Original, Target;
            public double Scale;
            public SnapshotView Snapshot;
            public RevisionPartBounds PartBefore;
        }
        // Sheet-space origin is independent of the green frame (which includes DIMs).
        // Match only the exact view ID within the frozen drawing baseline.
        public static RevisionLayout ReadRevisionLayout(bool preserveBaseline = true)
        {
            var model = new M.Model(); var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new InvalidOperationException("Không kết nối Tekla.");
            var drawing = handler.GetActiveDrawing();
            if (drawing == null || (!(drawing is D.SinglePartDrawing) && !(drawing is D.AssemblyDrawing)))
                throw new InvalidOperationException("Cần mở bản vẽ single part hoặc assembly.");
            string path = Baseline(model, drawing, preserveBaseline);
            var baseline = new Result { Drawing = drawing, ModelPath = model.GetInfo().ModelPath,
                BaselinePath = path, BaselineHash = Hash(path), BaselineTime = System.IO.File.GetLastWriteTime(path) };
            var old = ReadSnapshot(baseline.BaselinePath, new List<string>());
            var current = ReadCurrentPresentation(new List<string>());
            var mainPart = PHU_MainPartResolver.Resolve(model, baseline.Drawing);
            if (mainPart == null) throw new InvalidOperationException("Không xác định được main part.");
            var layout = new RevisionLayout { Baseline = baseline };
            var iterator = baseline.Drawing.GetSheet().GetAllViews();
            while (iterator.MoveNext())
            {
                var view = iterator.Current as D.View;
                if (view == null) continue;
                int id = Id(view);
                var matches = old.Where(v => v.Id == id).ToList();
                var now = current.Where(v => v.Id == id).ToList();
                if (matches.Count != 1 || now.Count != 1)
                    throw new InvalidOperationException("View " + id + " không có đối chiếu snapshot duy nhất; chưa tạo lại DIM.");
                var origin = view.Origin;
                if (!Finite(origin.X) || !Finite(origin.Y) || !Finite(matches[0].X) || !Finite(matches[0].Y)
                    || !Finite(view.Attributes.Scale) || view.Attributes.Scale <= 0)
                    throw new InvalidOperationException("Tọa độ/scale view không hợp lệ.");
                // Validate the DPM origin convention against the live Drawing API first.
                if (Math.Abs(origin.X - now[0].X) > .01 || Math.Abs(origin.Y - now[0].Y) > .01)
                    throw new InvalidOperationException("Gốc hiển thị view " + id + " chưa đồng bộ; chưa dịch chuyển view.");
                var parts = new List<D.Part>(); var objects = view.GetAllObjects();
                while (objects.MoveNext())
                { var part = objects.Current as D.Part; if (part != null && part.ModelIdentifier.ID == mainPart.Identifier.ID) parts.Add(part); }
                if (parts.Count != 1) throw new InvalidOperationException("View " + id + " không có main part duy nhất để căn theo snapshot.");
                int partId = Id(parts[0]);
                var before = matches[0].PartBounds.SingleOrDefault(p=>p.Id == partId);
                var after = now[0].PartBounds.SingleOrDefault(p=>p.Id == partId);
                if (before == null || after == null)
                    throw new InvalidOperationException("Không đọc được biên main part trong view " + id + "; chưa tự căn view.");
                var target = RevisionBalancedOrigin(matches[0], before, after, origin.Z);
                if (!Finite(target.X) || !Finite(target.Y)) throw new InvalidOperationException("Biên hình học không hợp lệ.");
                layout.Views.Add(new RevisionViewPosition { View = view, Id = id,
                    Original = new G.Point(origin), Target = target,
                    Scale = view.Attributes.Scale, Snapshot = matches[0], PartBefore = before });
            }
            if (layout.Views.Count == 0) throw new InvalidOperationException("Bản vẽ không có view.");
            return layout;
        }
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public static bool RevisionPositionDiffers(G.Point a, G.Point b)
        { return Math.Abs(a.X-b.X) > .001 || Math.Abs(a.Y-b.Y) > .001 || Math.Abs(a.Z-b.Z) > .001; }

        static void CheckRevisionDrawing(RevisionLayout layout)
        {
            var model = new M.Model();
            var active = new D.DrawingHandler().GetActiveDrawing();
            if (active == null || !active.IsSameDatabaseObject(layout.Baseline.Drawing)
                || !String.Equals(model.GetInfo().ModelPath, layout.Baseline.ModelPath, StringComparison.OrdinalIgnoreCase)
                || Hash(layout.Baseline.BaselinePath) != layout.Baseline.BaselineHash)
                throw new InvalidOperationException("Bản vẽ/model/snapshot đã thay đổi; dừng Create drawing.");
            var ids = new HashSet<int>();
            var views = active.GetSheet().GetAllViews();
            while (views.MoveNext()) if (views.Current is D.View) ids.Add(Id(views.Current));
            if (!ids.SetEquals(layout.Views.Select(v => v.Id)))
                throw new InvalidOperationException("Danh sách view đã thay đổi; không tự ghép view theo tên hoặc thứ tự.");
        }
        static int RestoreRevisionLayout(RevisionLayout layout, bool original)
        {
            CheckRevisionDrawing(layout);
            int moved = 0;
            var current = original ? null : ReadCurrentPresentation(new List<string>());
            foreach (var entry in layout.Views)
            {
                if (!entry.View.Select()) throw new InvalidOperationException("Không đọc lại được view " + entry.Id);
                if (Math.Abs(entry.View.Attributes.Scale-entry.Scale) > .000001)
                    throw new InvalidOperationException("Scale view " + entry.Id + " đã đổi ngoài luồng Create drawing.");
                if (!original)
                {
                    var presentation = current.SingleOrDefault(v=>v.Id == entry.Id);
                    var part = presentation == null ? null : presentation.PartBounds.SingleOrDefault(p=>p.Id == entry.PartBefore.Id);
                    if (part == null) throw new InvalidOperationException("Mất biên main part sau khi tạo DIM ở view " + entry.Id);
                    entry.Target = RevisionBalancedOrigin(entry.Snapshot, entry.PartBefore, part, entry.Original.Z);
                }
                var target = original ? entry.Original : entry.Target;
                if (!RevisionPositionDiffers(entry.View.Origin, target)) continue;
                entry.View.Origin = new G.Point(target);
                if (!entry.View.Modify()) throw new InvalidOperationException("Không khôi phục được vị trí view " + entry.Id);
                moved++;
            }
            if (moved > 0 && !layout.Baseline.Drawing.CommitChanges())
                throw new InvalidOperationException("Chưa commit được vị trí view.");
            foreach (var entry in layout.Views)
            {
                if (!entry.View.Select() || RevisionPositionDiffers(entry.View.Origin, original ? entry.Original : entry.Target)
                    || Math.Abs(entry.View.Attributes.Scale-entry.Scale) > .000001)
                    throw new InvalidOperationException("Vị trí/scale view " + entry.Id + " chưa khớp sau khi đọc lại.");
            }
            return moved;
        }
        // Called by the independent Revision UI; never calls MainForm's full create workflow.
        public static string CreateRevisionDrawing(Action dimensionOnly)
        {
            if (dimensionOnly == null) throw new ArgumentNullException("dimensionOnly");
            var layout = ReadRevisionLayout();
            using (var scope = new PHU_RevisionDimensionScope(layout.Baseline.Drawing))
            {
                bool deleting = false;
                try
                {
                    dimensionOnly();
                    CheckRevisionDrawing(layout);
                    scope.ValidateReplacements();
                    int moved = RestoreRevisionLayout(layout, false);
                    deleting = true;
                    int deleted = scope.DeleteReplacedDimensions();
                    if (!layout.Baseline.Drawing.CommitChanges())
                        throw new InvalidOperationException("Chưa commit được DIM thay thế.");
                    moved += RestoreRevisionLayout(layout, false);
                    return "Đã tạo lại DIM · xóa " + deleted + " DIM cũ · " + moved + " lượt khôi phục view về snapshot · giữ nguyên scale";
                }
                catch (Exception ex)
                {
                    if (deleting)
                    {
                        throw new InvalidOperationException("Create drawing chưa hoàn tất sau bước thay DIM; giữ DIM mới để kiểm tra. " + ex.Message, ex);
                    }
                    // Never touch a different active drawing during cleanup.
                    CheckRevisionDrawing(layout);
                    try { scope.RemoveNewDimensions(); }
                    finally { RestoreRevisionLayout(layout, true); }
                    throw;
                }
            }
        }
    }

    // Scoped opt-in gates only. The normal engine entry points retain their default behavior.
    internal sealed class PHU_RevisionDimensionScope : IDisposable
    {
        [ThreadStatic] static PHU_RevisionDimensionScope current;
        public static bool IsActive { get { return current != null; } }
        readonly D.Drawing drawing;
        readonly Dictionary<int, List<D.DrawingObject>> original;
        readonly HashSet<int> originalIds;
        public PHU_RevisionDimensionScope(D.Drawing drawing)
        {
            if (current != null) throw new InvalidOperationException("Create drawing đang chạy.");
            this.drawing = drawing;
            original = Dimensions(drawing);
            originalIds = new HashSet<int>(original.Values.SelectMany(v => v).Select(ObjectId));
            current = this;
        }
        static int ObjectId(D.DrawingObject obj)
        { return ((Tekla.Structures.Identifier)obj.GetType().GetProperty("Identifier", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(obj,null)).ID; }
        static Dictionary<int, List<D.DrawingObject>> Dimensions(D.Drawing drawing)
        {
            var result = new Dictionary<int, List<D.DrawingObject>>();
            var views = drawing.GetSheet().GetAllViews();
            while (views.MoveNext())
            {
                var view = views.Current as D.View; if (view == null) continue;
                var items = new List<D.DrawingObject>();
                var objects = view.GetAllObjects(); var seen = new HashSet<int>();
                while (objects.MoveNext())
                {
                    var obj = objects.Current;
                    // Parent sets own their children; never delete a child twice.
                    if (!(obj is D.StraightDimensionSet) && !(obj is D.AngleDimension)
                        && !(obj is D.RadiusDimension) && !(obj is D.CurvedDimensionSetRadial)
                        && !(obj is D.CurvedDimensionSetOrthogonal)) continue;
                    if (seen.Add(ObjectId(obj))) items.Add(obj);
                }
                result.Add(ObjectId(view), items);
            }
            return result;
        }
        public void ValidateReplacements()
        {
            var now = Dimensions(drawing);
            if (!original.Keys.OrderBy(i=>i).SequenceEqual(now.Keys.OrderBy(i=>i)))
                throw new InvalidOperationException("View đã thay đổi trong khi tạo DIM.");
            int count = 0;
            foreach (var pair in now)
            {
                var created = pair.Value.Where(d => !originalIds.Contains(ObjectId(d))).ToList();
                if (!original[pair.Key].All(old=>pair.Value.Any(o=>ObjectId(o)==ObjectId(old))))
                    throw new InvalidOperationException("DIM cũ đã bị thay đổi ngoài bước thay thế có kiểm tra.");
                if (original[pair.Key].Count > 0 && created.Count == 0)
                    throw new InvalidOperationException("View " + pair.Key + " chưa tạo được DIM thay thế; giữ DIM cũ.");
                foreach (var kind in original[pair.Key].Select(o=>o.GetType()).Distinct())
                    if (!created.Any(o=>o.GetType()==kind))
                        throw new InvalidOperationException("Chưa tạo được DIM thay thế loại " + kind.Name + " trong view " + pair.Key + "; giữ DIM cũ.");
                foreach (var set in created.OfType<D.StraightDimensionSet>())
                {
                    var children = set.GetObjects(); int segments = 0;
                    while (children.MoveNext())
                    {
                        var child = children.Current as D.StraightDimension; if (child == null) continue;
                        double length = child.StartPoint == null || child.EndPoint == null ? Double.NaN : G.Distance.PointToPoint(child.StartPoint, child.EndPoint);
                        if (Double.IsNaN(length) || Double.IsInfinity(length) || length < .001)
                            throw new InvalidOperationException("DIM mới có chân không hợp lệ.");
                        segments++;
                    }
                    if (segments == 0) throw new InvalidOperationException("DIM mới không có đoạn đo.");
                }
                count += created.Count;
            }
            if (count == 0) throw new InvalidOperationException("Không tạo được DIM mới; giữ DIM cũ.");
        }
        public int DeleteReplacedDimensions()
        {
            int count = 0;
            foreach (var obj in original.Values.SelectMany(v => v))
            {
                if (!obj.Delete()) throw new InvalidOperationException("Không xóa được DIM cũ " + ObjectId(obj));
                count++;
            }
            return count;
        }
        public void RemoveNewDimensions()
        {
            foreach (var obj in Dimensions(drawing).Values.SelectMany(v=>v).Where(d=>!originalIds.Contains(ObjectId(d))))
                if (!obj.Delete()) throw new InvalidOperationException("Không thu hồi được DIM mới " + ObjectId(obj));
            if (!drawing.CommitChanges()) throw new InvalidOperationException("Chưa commit được việc thu hồi DIM mới.");
        }
        public void Dispose() { current = null; }
    }
}

// Source: PHU_RevisionPresentation.cs
namespace Tekla.Technology.Akit.UserScript
{
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using D = Tekla.Structures.Drawing;


    public static partial class PHU_DimensionRevision
    {
        [Serializable]
        public sealed class ContentBlock
        {
            public int Id, Rows;
            public string Kind, Signature, Text;
            public double X, Y;
            public List<string[]> Values;
        }
        sealed class TextCell { public string Text; public double X,Y; }
        static List<ContentBlock> ReadContent(object view)
        {
            var blocks=new List<ContentBlock>();
            foreach(var group in Children(view))
            {
                if(group.GetType().Name!="Segment")continue;
                int type=(int)Number(group,"ObjectType");
                if(type!=264&&type!=263)continue;
                var primitives=Descendants(group).ToList();
                var cells=primitives.Where(p=>p.GetType().Name=="TextPrimitive").Select(p=>new TextCell {
                    Text=Convert.ToString(Member(p,"Text")),X=Number(Member(p,"Position"),"X"),Y=Number(Member(p,"Position"),"Y") }).ToList();
                if(cells.Count==0)continue;
                // The property schedule shown by the user, identified by its headers, never screen position/colour.
                string compact=String.Concat(cells.Select(c=>c.Text)).Replace(" ","").Replace("　","");
                bool table=type==263;
                if(table&&!(compact.Contains("製品マーク")&&compact.Contains("数量")&&compact.Contains("図面番号")))continue;
                var lines=primitives.Where(p=>p.GetType().Name=="LinePrimitive").ToList();
                if(table)
                {
                    var ignored=cells.First(c=>c.Text.Replace(" ","").Replace("　","")=="図面番号");
                    var boundaries=lines.Where(l=>Math.Abs(Number(Member(l,"StartPoint"),"X")-Number(Member(l,"EndPoint"),"X"))<.01)
                        .Select(l=>Number(Member(l,"StartPoint"),"X")).Where(x=>x<ignored.X).ToList();
                    double boundary=boundaries.Count>0?boundaries.Max():
                        (ignored.X+cells.Where(c=>c.X<ignored.X&&Math.Abs(c.Y-ignored.Y)<1).Max(c=>c.X))/2;
                    cells=cells.Where(c=>c.X<boundary-.01).ToList();
                }
                var levels=new List<double>();
                foreach(var line in lines)
                {
                    var a=Member(line,"StartPoint");var b=Member(line,"EndPoint");
                    if(Math.Abs(Number(a,"Y")-Number(b,"Y"))<.01&&Math.Abs(Number(a,"X")-Number(b,"X"))>1)
                        if(!levels.Any(y=>Math.Abs(y-Number(a,"Y"))<.05))levels.Add(Number(a,"Y"));
                }
                levels=levels.OrderByDescending(y=>y).ToList();
                var rows=new List<List<TextCell>>();
                if(table&&levels.Count>=2)
                {
                    for(int i=0;i<levels.Count-1;i++)rows.Add(cells.Where(c=>c.Y<=levels[i]+.01&&c.Y>levels[i+1]+.01).OrderBy(c=>c.X).ToList());
                    var outside=cells.Where(c=>c.Y>levels[0]+.01||c.Y<=levels.Last()+.01).ToList();
                    if(outside.Count>0)rows.Add(outside.OrderByDescending(c=>c.Y).ThenBy(c=>c.X).ToList());
                }
                else
                {
                    foreach(var cell in cells.OrderByDescending(c=>c.Y).ThenBy(c=>c.X))
                    { if(rows.Count==0||Math.Abs(rows.Last()[0].Y-cell.Y)>.6)rows.Add(new List<TextCell>());rows.Last().Add(cell); }
                }
                var values=rows.Select(r=>r.OrderBy(c=>c.X).Select(c=>c.Text).ToArray()).ToList();
                blocks.Add(new ContentBlock {Id=(int)Number(group,"Id"),Kind=table?"Bảng thuộc tính":"Mark",Rows=rows.Count,
                    X=cells.Min(c=>c.X),Y=cells.Max(c=>c.Y),Signature=ContentSignature(values),Values=values,
                    Text=String.Join(" / ",values.Select(r=>String.Join(" | ",r))) });
            }
            return blocks;
        }
        public static string ContentSignature(IList<string[]> rows)
        {
            // Length framing preserves repeated/empty cells and additional empty rows.
            return rows.Count+":"+String.Concat(rows.Select(r=>r.Length+":"+String.Concat(r.Select(s=>(s??"").Length+":"+(s??"")))));
        }

        public sealed partial class SnapshotReader
        {
            [DllImport("wtsapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
            static extern bool WTSQuerySessionInformation(IntPtr server,int session,int info,out IntPtr buffer,out int bytes);
            [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr buffer);
            public SnapshotData ReadCurrent()
            {
                string bin=Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location);
                // Use Tekla's presentation RPC directly: DrawingHandler remoting is not initialized in this AppDomain.
                // Explicit RPC deadline avoids Tekla's default one-day timeout when its service is unavailable.
                string session=Environment.GetEnvironmentVariable("SESSIONNAME");
                if(String.IsNullOrEmpty(session))
                {
                    IntPtr ptr;int bytes;
                    if(!WTSQuerySessionInformation(IntPtr.Zero,System.Diagnostics.Process.GetCurrentProcess().SessionId,6,out ptr,out bytes))
                        throw new InvalidOperationException("Không đọc được phiên Tekla để lấy nội dung hiện hành.");
                    try { session=Marshal.PtrToStringUni(ptr); } finally { WTSFreeMemory(ptr); }
                }
                string version=typeof(D.Drawing).Assembly.GetName().Version.Major+".0";
                using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Trimble\Tekla Structures\"+version+@"\Drawings\PresentationPlugin\"+session))
                {
                    int port=key==null?0:Convert.ToInt32(key.GetValue("GrpcServerPort",0));
                    if(port<=0)throw new InvalidOperationException("Không tìm thấy dịch vụ nội dung bản vẽ trong phiên Tekla hiện tại.");
                    var grpc=Assembly.LoadFrom(Path.Combine(bin,"Grpc.Core.dll"));
                    var api=Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.DrawingPresentationModelConvertor.dll"));
                    var channelType=grpc.GetType("Grpc.Core.Channel",true);
                    var credentialsType=grpc.GetType("Grpc.Core.ChannelCredentials",true);
                    var channel=Activator.CreateInstance(channelType,new object[]{"localhost",port,credentialsType.GetProperty("Insecure").GetValue(null,null)});
                    try
                    {
                        var clientType=api.GetType("Tekla.Structures.DrawingPresentationModelInterface.DrawingPresentationModelService+DrawingPresentationModelServiceClient",true);
                        var client=Activator.CreateInstance(clientType,new[]{channel});
                        var request=Activator.CreateInstance(Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.GrpcContracts.dll")).GetType("Tekla.Structures.DrawingPresentationModelInterface.GetCurrentPresentationModelRequest",true));
                        var method=clientType.GetMethods().Single(m=>m.Name=="GetCurrentPresentationModel"&&m.GetParameters().Length==4);
                        var response=method.Invoke(client,new object[]{request,null,(DateTime?)DateTime.UtcNow.AddSeconds(15),CancellationToken.None});
                        var convertor=Assembly.LoadFrom(Path.Combine(bin,"Tekla.Structures.DrawingPresentationModelConvertor.dll"));
                        var convert=convertor.GetType("Tekla.Structures.DrawingPresentationModelConvertor.GrpcMessageConvertor",true).GetMethod("GetViews");
                        var raw=convert.Invoke(null,new[]{Member(response,"Views")}) as IEnumerable;
                        if(raw==null)throw new InvalidDataException("Không đọc được nội dung hiện hành; chưa thể kết luận giống snapshot.");
                        var result=new SnapshotData();result.Views=ParsePresentation(raw,result.Warnings);return result;
                    }
                    finally { channelType.GetMethod("ShutdownAsync").Invoke(channel,null); }
                }
            }
        }
        static List<SnapshotView> ReadCurrentPresentation(List<string> warnings)
        {
            var data=UsePresentationReader(reader=>reader.ReadCurrent());warnings.AddRange(data.Warnings);return data.Views;
        }

        public static List<List<Segment>> Chains(IEnumerable<Segment> source)
        {
            var remaining=source.OrderBy(s=>s.PaperA).ThenBy(s=>s.PaperB).ThenBy(s=>s.Level).ThenBy(s=>s.Id).ToList();
            var chains=new List<List<Segment>>();
            // Group by connectivity, not floating-point sort order of line levels.
            // A branch/duplicate remains separate and will require review when matching.
            while(remaining.Count>0)
            {
                var chain=new List<Segment>{remaining[0]};remaining.RemoveAt(0);
                while(true)
                {
                    var next=remaining.Where(s=>CanJoin(chain.Last(),s)&&Math.Abs(chain[0].Level-s.Level)<.15).ToList();
                    if(next.Count!=1)break;
                    if(remaining.Count(s=>CanJoin(s,next[0]))>0)break;
                    chain.Add(next[0]);remaining.Remove(next[0]);
                }
                chains.Add(chain);
            }
            return chains;
        }
        static bool SameAxis(Segment a,Segment b)
        {return a.X*b.X+a.Y*b.Y>0&&Math.Abs(a.X*b.Y-a.Y*b.X)<.0001;}
        static bool CanJoin(Segment a,Segment b)
        {return SameAxis(a,b)&&Math.Abs(a.Level-b.Level)<.15&&Math.Abs(a.PaperB-b.PaperA)<FootTolerance;}
        static void ReadWitnessFeet(Segment segment,IEnumerable<object> primitives)
        {
            var ends=new List<Tuple<double,double>>();
            foreach(var line in primitives.Where(p=>p.GetType().Name=="LinePrimitive"))
            {
                var a=Member(line,"StartPoint");var b=Member(line,"EndPoint");
                double ax=Number(a,"X")*segment.X+Number(a,"Y")*segment.Y,bx=Number(b,"X")*segment.X+Number(b,"Y")*segment.Y;
                double an=-Number(a,"X")*segment.Y+Number(a,"Y")*segment.X,bn=-Number(b,"X")*segment.Y+Number(b,"Y")*segment.X;
                if(!Finite(ax)||!Finite(bx)||!Finite(an)||!Finite(bn)||Math.Abs(ax-bx)>.02||Math.Abs(an-bn)<.05)continue;
                // A witness must reach its own dimension line; unrelated tags do not qualify.
                if(segment.Level<Math.Min(an,bn)-.03||segment.Level>Math.Max(an,bn)+.03)continue;
                ends.Add(Tuple.Create((ax+bx)/2,Math.Abs(an-segment.Level)>Math.Abs(bn-segment.Level)?an:bn));
            }
            var left=ends.Where(p=>Math.Abs(p.Item1-segment.PaperA)<.03).OrderByDescending(p=>Math.Abs(p.Item2-segment.Level)).ToList();
            var right=ends.Where(p=>Math.Abs(p.Item1-segment.PaperB)<.03).OrderByDescending(p=>Math.Abs(p.Item2-segment.Level)).ToList();
            if(left.Count==0||right.Count==0)return;
            segment.FootA=left[0].Item2;segment.FootB=right[0].Item2;segment.HasFeet=true;segment.HasWitnessGeometry=true;
        }
        public static List<Segment> MatchChain(IList<Segment> current,IList<List<Segment>> old,out bool ambiguous)
        {
            ambiguous=false;if(current.Count==0)return null;
            if(current.Any(s=>!ValidMatchSegment(s))||old.Any(c=>c.Count==0||c.Any(s=>!ValidMatchSegment(s))))
            {ambiguous=true;return null;}
            double a=current.Min(s=>s.PaperA),b=current.Max(s=>s.PaperB),span=b-a;
            var candidates=old.Where(c=>SameAxis(c[0],current[0])&&SameChainFeet(current,c))
                .Where(c=>Math.Max(0,Math.Min(b,c.Max(s=>s.PaperB))-Math.Max(a,c.Min(s=>s.PaperA)))/Math.Max(span,c.Max(s=>s.PaperB)-c.Min(s=>s.PaperA))>=.65 ||
                    current.Any(n=>c.Any(s=>SameInterval(n,s)))).ToList();
            // IDs are only a tie breaker inside geometrically compatible candidates.
            // Add/remove point can replace every child ID, or reuse an ID for another interval.
            if(candidates.Count>1)
            {
                // Regenerating DIMs can move both the overall and hole-chain lines.
                // Their shared stations/topology are stronger evidence than the new line level.
                var topology=candidates.Select(c=>new {Chain=c,
                    Anchors=current.Count(n=>c.Any(s=>SameInterval(n,s))),
                    SameCount=c.Count==current.Count}).ToList();
                int mostAnchors=topology.Max(c=>c.Anchors);
                var anchored=topology.Where(c=>c.Anchors==mostAnchors).ToList();
                if(mostAnchors>0&&anchored.Count==1)return anchored[0].Chain;
                var sameCount=anchored.Where(c=>c.SameCount).ToList();
                if(sameCount.Count==1)return sameCount[0].Chain;
                var exact=candidates.Where(c=>c.Any(s=>current.Any(n=>n.Id>0&&n.Id==s.Id&&SameInterval(n,s)))).ToList();
                if(exact.Count==1)return exact[0];
                // A drawing may contain a full-span DIM and a hole chain with the
                // same witness feet.  The dimension-line level is the remaining
                // geometric identity; use it only when one candidate is clearly
                // nearer, and retain review for genuinely coincident candidates.
                var ranked=anchored.Select(c=>new { Chain=c.Chain, Distance=ChainLevelDistance(current,c.Chain) })
                    .OrderBy(p=>p.Distance).ToList();
                if(ranked.Count>1&&ranked[1].Distance-ranked[0].Distance>.5)return ranked[0].Chain;
            }
            ambiguous=candidates.Count>1;return candidates.Count==1?candidates[0]:null;
        }
        const double FootTolerance=.025;
        static bool ValidMatchSegment(Segment s)
        {return s!=null&&s.HasPaperGeometry&&Finite(s.PaperA)&&Finite(s.PaperB)&&s.PaperB-s.PaperA>1e-6&&
            Finite(s.Level)&&Finite(s.X)&&Finite(s.Y)&&Math.Abs(s.X*s.X+s.Y*s.Y-1)<.001&&
            Finite(s.Value)&&s.Value>0&&(!s.HasFeet||Finite(s.FootA)&&Finite(s.FootB));}
        static bool SameInterval(Segment a,Segment b)
        {return Math.Abs(a.PaperA-b.PaperA)<FootTolerance&&Math.Abs(a.PaperB-b.PaperB)<FootTolerance;}
        // Resolve all chains together. Two recreated chains competing for one baseline must
        // not change classification depending on native enumeration/ID order.
        public static List<List<Segment>> MatchChains(IList<List<Segment>> current,IList<List<Segment>> old,out bool[] ambiguous)
        {
            ambiguous=new bool[current.Count];var result=new List<List<Segment>>();
            for(int i=0;i<current.Count;i++){bool review;result.Add(MatchChain(current[i],old,out review));ambiguous[i]=review;}
            foreach(var candidate in result.Where(c=>c!=null).Distinct().ToList())
            {
                var users=Enumerable.Range(0,current.Count).Where(i=>ReferenceEquals(result[i],candidate)).ToList();
                if(users.Count<2)continue;
                var owners=users.Where(i=>current[i].Any(n=>candidate.Any(s=>n.Id>0&&n.Id==s.Id&&SameInterval(n,s)))).ToList();
                foreach(int i in users.Where(i=>owners.Count!=1||i!=owners[0])){result[i]=null;ambiguous[i]=true;}
            }
            return result;
        }
        static bool SameChainFeet(IList<Segment> current,IList<Segment> old)
        {
            var a=current.OrderBy(s=>s.PaperA).ToList();var b=old.OrderBy(s=>s.PaperA).ToList();
            if(a[0].HasFeet&&b[0].HasFeet&&a.Last().HasFeet&&b.Last().HasFeet)
            {
                if(Math.Abs(a[0].FootA-b[0].FootA)<=.3&&Math.Abs(a.Last().FootB-b.Last().FootB)<=.3)return true;
                // Create drawing changes gaps, witness origins and line offsets. Compare
                // the rendered corridors at BOTH outer stations, never increase the foot
                // tolerance or match by the printed values alone. The interior new chain
                // has disjoint corridors even when it displays the same 75-75 values.
                return Math.Abs(a[0].PaperA-b[0].PaperA)<FootTolerance&&Math.Abs(a.Last().PaperB-b.Last().PaperB)<FootTolerance&&
                    WitnessCorridorsOverlap(a[0],b[0],false)&&WitnessCorridorsOverlap(a.Last(),b.Last(),true);
            }
            // Complete equal/recreated ranges do not become new just because their line was dragged.
            if(Math.Abs(a[0].PaperA-b[0].PaperA)<FootTolerance&&Math.Abs(a.Last().PaperB-b.Last().PaperB)<FootTolerance)return true;
            return Math.Abs(a[0].Level-b[0].Level)<=2;
        }
        static bool WitnessCorridorsOverlap(Segment a,Segment b,bool end)
        {
            if(!a.HasWitnessGeometry||!b.HasWitnessGeometry)return false;
            double af=end?a.FootB:a.FootA,bf=end?b.FootB:b.FootA;
            double da=a.Level-af,db=b.Level-bf;
            if(da*db<=0)return false;
            double overlap=Math.Max(0,Math.Min(Math.Max(af,a.Level),Math.Max(bf,b.Level))-
                Math.Max(Math.Min(af,a.Level),Math.Min(bf,b.Level)));
            // Require agreement over most of each visible witness (not just crossing).
            return overlap/Math.Max(Math.Abs(da),Math.Abs(db))>=.65;
        }
        static double ChainLevelDistance(IList<Segment> current,IList<Segment> old)
        {
            double a=current.Average(s=>s.Level),b=old.Average(s=>s.Level);
            return Math.Abs(a-b);
        }
        public static List<Segment> MatchChainSegments(IList<Segment> current,IList<Segment> old)
        {
            var answer=Enumerable.Repeat<Segment>(null,current.Count).ToList();
            if(old==null)return answer;
            var ordered=current.Select((s,i)=>new {Segment=s,Index=i}).OrderBy(p=>p.Segment.PaperA).ThenBy(p=>p.Segment.PaperB).ToList();
            var prior=old.OrderBy(s=>s.PaperA).ThenBy(s=>s.PaperB).ToList();
            // Reserve unchanged intervals first, independent of IDs, count and enumeration.
            // They delimit local edit regions so inserting one foot cannot shift every pair.
            var anchors=new List<Tuple<int,int>> {Tuple.Create(-1,-1)};
            for(int i=0;i<ordered.Count;i++)
            {
                var hits=Enumerable.Range(0,prior.Count).Where(j=>SameInterval(ordered[i].Segment,prior[j])).ToList();
                if(hits.Count!=1||ordered.Count(p=>SameInterval(p.Segment,prior[hits[0]]))!=1||hits[0]<=anchors.Last().Item2)continue;
                answer[ordered[i].Index]=prior[hits[0]];anchors.Add(Tuple.Create(i,hits[0]));
            }
            anchors.Add(Tuple.Create(ordered.Count,prior.Count));
            for(int k=1;k<anchors.Count;k++)
            {
                int a=anchors[k-1].Item1+1,b=anchors[k-1].Item2+1;
                int count=anchors[k].Item1-a,oldCount=anchors[k].Item2-b;
                if(count==0||oldCount==0)continue;
                // Equal topology inside this anchored region: moved feet/length changes.
                // Unequal topology: retain uniquely matching translated intervals through
                // monotonic alignment; split/merged intervals remain new measurements.
                var localCurrent=ordered.Skip(a).Take(count).Select(p=>p.Segment).ToList();
                var localOld=prior.Skip(b).Take(oldCount).ToList();
                bool topologyChanged=localOld.Any(s=>localCurrent.Count(n=>Inside(n,s))>1)||localCurrent.Any(s=>localOld.Count(n=>Inside(n,s))>1);
                if(count==oldCount&&!topologyChanged)
                {
                    for(int j=0;j<count;j++)answer[ordered[a+j].Index]=prior[b+j];
                    continue;
                }
                AlignEditedRegion(localCurrent,localOld,
                    (i,s)=>answer[ordered[a+i].Index]=s);
            }
            return answer;
        }
        static bool Inside(Segment inner,Segment outer)
        {return inner.PaperA>=outer.PaperA-FootTolerance&&inner.PaperB<=outer.PaperB+FootTolerance;}
        static void AlignEditedRegion(IList<Segment> current,IList<Segment> old,Action<int,Segment> assign)
        {
            int n=current.Count,m=old.Count;var costs=new double[n+1,m+1];var step=new int[n+1,m+1];
            var split=old.Select(b=>current.Count(s=>Inside(s,b))>1).ToArray();
            var merged=current.Select(a=>old.Count(s=>Inside(s,a))>1).ToArray();
            for(int i=1;i<=n;i++){costs[i,0]=i;step[i,0]=1;}
            for(int j=1;j<=m;j++){costs[0,j]=j;step[0,j]=2;}
            for(int i=1;i<=n;i++)for(int j=1;j<=m;j++)
            {
                var a=current[i-1];var b=old[j-1];
                double overlap=Math.Max(0,Math.Min(a.PaperB,b.PaperB)-Math.Max(a.PaperA,b.PaperA));
                double span=Math.Max(a.PaperB-a.PaperA,b.PaperB-b.PaperA);
                // A split/merge must not steal the old interval via a surviving child ID.
                double pair=!split[j-1]&&!merged[i-1]&&span>0&&overlap/span>.65?
                    .2+(Math.Abs(a.PaperA-b.PaperA)+Math.Abs(a.PaperB-b.PaperB))/span:Double.PositiveInfinity;
                double match=costs[i-1,j-1]+pair,insert=costs[i-1,j]+1,delete=costs[i,j-1]+1;
                costs[i,j]=Math.Min(match,Math.Min(insert,delete));
                step[i,j]=match<insert-1e-8&&match<delete-1e-8?3:insert<=delete?1:2;
            }
            for(int i=n,j=m;i>0||j>0;)
            {
                if(step[i,j]==3){assign(i-1,old[j-1]);i--;j--;}
                else if(step[i,j]==1)i--;else j--;
            }
        }
        static void CompareContent(Result result,IList<SnapshotView> old,IList<SnapshotView> current)
        {
            int reportId=-1;
            foreach(int viewId in old.Select(v=>v.Id).Union(current.Select(v=>v.Id)))
            {
                var before=old.Where(v=>v.Id==viewId).SelectMany(v=>v.Content).ToList();
                var after=current.Where(v=>v.Id==viewId).SelectMany(v=>v.Content).ToList();
                foreach(var n in after.ToList())
                {
                    var matches=before.Where(b=>b.Id==n.Id&&b.Kind==n.Kind).ToList();
                    if(matches.Count!=1)continue;
                    var o=matches[0];before.Remove(o);after.Remove(n);
                    if(o.Signature!=n.Signature)result.Items.Add(ContentItem(reportId--,viewId,o,n));
                }
                // Reserve unchanged contents before matching edited/recreated blocks; multiplicity matters.
                foreach(var n in after.ToList())
                {var o=before.FirstOrDefault(b=>b.Kind==n.Kind&&b.Signature==n.Signature);if(o!=null){before.Remove(o);after.Remove(n);}}
                foreach(var n in after)
                {
                    var o=before.FirstOrDefault(b=>b.Kind==n.Kind&&b.Id==n.Id);
                    if(o==null&&before.Count(b=>b.Kind==n.Kind)==1&&after.Count(b=>b.Kind==n.Kind)==1)o=before.Single(b=>b.Kind==n.Kind);
                    if(o!=null)before.Remove(o);
                    result.Items.Add(ContentItem(reportId--,viewId,o,n));
                }
                foreach(var o in before)result.Items.Add(ContentItem(reportId--,viewId,o,null));
            }
        }
        static Item ContentItem(int id,int viewId,ContentBlock old,ContentBlock current)
        {
            return new Item {Id=id,ViewId=viewId,ReadOnly=true,Changed=true,View=(current??old).Kind+" · chỉ thông báo",
                ContentBefore=old,ContentAfter=current,
                Before=old==null?"Không có":old.Text,After=current==null?"Đã xóa":current.Text,
                Message=(current??old).Kind=="Bảng thuộc tính"?TableMessage(old,current):null,
                Reason="Snapshot: "+(old==null?0:old.Rows)+" hàng; hiện hành: "+(current==null?0:current.Rows)+" hàng. Chỉ đọc nội dung; người dùng tự chỉnh thuộc tính."};
        }
        public static string TableMessage(ContentBlock old,ContentBlock current)
        {
            if(old==null)return "Thêm hàng thuộc tính [製品マーク, 数量]";
            if(current==null)return "Đã xóa hàng thuộc tính [製品マーク, 数量]";
            if(old.Rows!=current.Rows)return "Số hàng thuộc tính khác ["+old.Rows+" → "+current.Rows+"]";
            var names=new List<string>();
            if(old.Values!=null&&current.Values!=null)
                for(int c=0;c<2;c++)
                {
                    if(old.Values.Count!=current.Values.Count||old.Values.Where((r,i)=>(r.Length>c?r[c]:"")!=(current.Values[i].Length>c?current.Values[i][c]:"")).Any())
                        names.Add(c==0?"製品マーク":"数量");
                }
            if(names.Count==0)names.Add("製品マーク, 数量");
            return (names.Count==1&&names[0]=="数量"?"Ô số lượng":"Ô thuộc tính")+" khác giá trị ["+String.Join(", ",names)+"]";
        }
    }
}

// Source: PHU_RevisionColors.cs
namespace Tekla.Technology.Akit.UserScript
{
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using D=Tekla.Structures.Drawing;


    public static partial class PHU_DimensionRevision
    {
        static T CopyValue<T>(T value)
        {
            using(var stream=new MemoryStream())
            {var serializer=new BinaryFormatter();serializer.Serialize(stream,value);stream.Position=0;return (T)serializer.Deserialize(stream);}
        }
        static List<D.StraightDimension> ChildrenOf(D.StraightDimensionSet set)
        {
            var list=new List<D.StraightDimension>();var it=set.GetObjects();
            while(it.MoveNext()){var child=it.Current as D.StraightDimension;if(child!=null)list.Add(child);}
            return list.OrderBy(c=>Id(c)).ToList();
        }
        static string PointKey(Tekla.Structures.Geometry3d.Point point)
        {return String.Join(",",new[]{point.X,point.Y,point.Z}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)));}
        static string GeometryKey(D.StraightDimension child)
        {return Id(child)+":"+Id(child.GetDimensionSet(false))+":"+PointKey(child.StartPoint)+":"+PointKey(child.EndPoint)+":"+
            PointKey(child.UpDirection)+":"+child.Distance.ToString("R",CultureInfo.InvariantCulture)+":"+
            String.Concat(child.Value.Cast<object>().OfType<D.TextElement>().Select(e=>e.Value));}
        static string SetGeometryKey(D.StraightDimensionSet set)
        {return ChildrenGeometryKey(ChildrenOf(set));}
        static string ChildrenGeometryKey(IEnumerable<D.StraightDimension> children)
        {return String.Join("|",children.OrderBy(Id).Select(GeometryKey));}
        static string Encode(string value){return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));}
        static string Decode(string value){return Encoding.UTF8.GetString(Convert.FromBase64String(value));}

        // Same native property proxy as Tekla's Properties pane, inside its macro runtime.
        // Change only proFontColor on existing children: no selection, parent Modify,
        // deletion or recreation. All model/drawing/chain checks repeat inside Tekla.
        static int WriteColorPlans(Result result,IDictionary<Item,D.DrawingColors> colors,bool readOnly=false,D.DrawingColors? expectedColor=null)
        {
            EnsureActive(result);if(colors.Count==0)return 0;
            if(Hash(result.BaselinePath)!=result.BaselineHash)throw new InvalidOperationException("Snapshot đã thay đổi. Hãy kiểm tra lại.");
            var knownItems=new HashSet<Item>(result.Items.Concat(result.UnchangedItems));
            foreach(var item in colors.Keys)
                if(item.ReadOnly||item.Dimension==null||item.Object==null||!knownItems.Contains(item))
                    throw new InvalidOperationException("DIM đã thay đổi trong lúc đối chiếu; hãy kiểm tra lại.");
            var geometries=new Dictionary<int,string>();
            foreach(var group in colors.Keys.GroupBy(i=>i.Object))
            {
                if(!group.Key.Select())throw new InvalidOperationException("Cụm DIM không còn tồn tại; hãy kiểm tra lại.");
                var children=ChildrenOf(group.Key);
                var segments=ReadSegmentsFromChildren(group.Key,children).ToDictionary(s=>s.Id);
                string geometry=ChildrenGeometryKey(children);geometries[Id(group.Key)]=geometry;
                if(group.Any(i=>!segments.ContainsKey(i.Id)||Signature(new List<Segment>{segments[i.Id]})!=i.Signature||
                    String.IsNullOrEmpty(i.SetSignature)||i.SetSignature!=geometry))
                    throw new InvalidOperationException("Cụm DIM đã đổi chân đo hoặc số đoạn sau khi kiểm tra; hãy bấm F5.");
            }
            // One native operation refreshes current colours and applies only real
            // changes. Never use cached colours to skip a request after a manual edit.
            var changes=colors.ToList();
            if(changes.Count==0)return 0;
            using(var mutex=new System.Threading.Mutex(false,"Local\\TTSK_RevisionNativeColor"))
            {
                bool locked;try{locked=mutex.WaitOne(0);}catch(System.Threading.AbandonedMutexException){locked=true;}
                if(!locked)throw new InvalidOperationException("Revision đang ghi màu trong phiên khác.");
                try
                {
                    string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TTSK Dim Plates","RevisionColorJobs");
                    Directory.CreateDirectory(root);string pending=Path.Combine(root,"pending.txt");
                    if(File.Exists(pending))
                    {
                        string previous=File.ReadAllText(pending);
                        if(!File.Exists(Path.Combine(previous,"result.txt"))&&
                            (!File.Exists(Path.Combine(previous,"cancel"))||IsNativeColorJobRunning(previous)))
                            throw new InvalidOperationException("Lần ghi màu trước chưa trả kết quả. Hãy chờ Tekla hoàn tất trước khi kiểm tra lại.");
                    }
                    string folder=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                    string request=Path.Combine(folder,"request.txt"),response=Path.Combine(folder,"result.txt"),source=Path.Combine(folder,"RevisionColor.cs");
                    var rows=new List<string>{Encode(result.ModelPath),((Tekla.Structures.Identifier)Member(result.Drawing,"Identifier")).GUID.ToString(),
                        Encode(result.BaselinePath),result.BaselineHash,DateTime.UtcNow.AddSeconds(30).Ticks.ToString(CultureInfo.InvariantCulture)};
                    foreach(var set in changes.Select(p=>p.Key.Object).Distinct())
                        rows.Add("S\t"+Id(set)+"\t"+Encode(geometries[Id(set)]));
                    foreach(var pair in changes.Where(p=>!readOnly))
                        rows.Add("C\t"+pair.Key.Id+"\t"+(expectedColor.HasValue?(int)expectedColor.Value:-1)+"\t"+(int)pair.Value);
                    File.WriteAllLines(request,rows,Encoding.UTF8);
                    // LoadFile caches by path in Tekla. Use a content-addressed copy so a
                    // rebuilt TTSK cannot accidentally execute an older loaded writer.
                    string assembly=typeof(PHU_DimensionRevision).Assembly.Location,assemblyHash=Hash(assembly);
                    string writer=Path.Combine(root,"writer-"+assemblyHash+".dll");
                    if(!File.Exists(writer))File.Copy(assembly,writer,false);
                    if(Hash(writer)!=assemblyHash)throw new IOException("Bản ghi màu không đúng phiên bản chương trình.");
                    File.WriteAllText(source,NativeColorMacro(writer,request),Encoding.UTF8);
                    File.WriteAllText(pending,folder);
                    // Marshal a complete immutable request, never individual live drawing objects.
                    // RunMacro is a Tekla API call itself; keep it on the caller's
                    // complete-operation context. Only the macro's native properties
                    // work is marshalled to Tekla's main-window thread below.
                    bool launched;
                    try { launched=Tekla.Structures.Model.Operations.Operation.RunMacro(source); }
                    catch(Exception ex)
                    {
                        File.WriteAllText(Path.Combine(folder,"cancel"),"");
                        File.WriteAllText(response,"ERROR: Tekla không khởi chạy được bước ghi màu: "+ex.GetBaseException().Message);
                        launched=false;
                    }
                    if(!launched && File.Exists(response))
                        throw new InvalidOperationException(File.ReadAllText(response));
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    while(!File.Exists(response)&&timer.ElapsedMilliseconds<30000)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                    if(!File.Exists(response))
                    {
                        File.WriteAllText(Path.Combine(folder,"cancel"),"");
                        throw new InvalidOperationException("Tekla chưa trả kết quả ghi màu; yêu cầu đã hủy. Kiểm tra lại sau khi Tekla phản hồi. Log: "+folder);
                    }
                    string status=File.ReadAllText(response);
                    if(status!="DONE")throw new InvalidOperationException(status);
                    EnsureActive(result);
                    var persisted=File.ReadAllLines(Path.Combine(folder,"colors.txt")).Select(l=>l.Split('\t'))
                        .ToDictionary(p=>Int32.Parse(p[0],CultureInfo.InvariantCulture),p=>(D.DrawingColors)Int32.Parse(p[1],CultureInfo.InvariantCulture));
                    foreach(var item in result.Items.Concat(result.UnchangedItems).Where(i=>i.Dimension!=null&&persisted.ContainsKey(i.Id)))
                    {item.NativeColor=persisted[item.Id];item.AlreadyRed=item.NativeColor==D.DrawingColors.Red;}
                    foreach(var pair in changes)
                    {
                        if(!persisted.ContainsKey(pair.Key.Id)||!pair.Key.Dimension.Select()||(!readOnly&&GetColor(pair.Key)!=pair.Value))
                            throw new InvalidOperationException("Chưa đọc lại được màu DIM đã ghi; kiểm tra lại Revision.");
                    }
                    return readOnly?0:Int32.Parse(File.ReadAllText(Path.Combine(folder,"changed.txt")),CultureInfo.InvariantCulture);
                }
                finally{mutex.ReleaseMutex();}
            }
        }
        static string NativeColorMacro(string assembly,string request)
        {
            // Base64 prevents Windows paths from becoming executable source.
            return "#pragma warning disable 1633\n#pragma reference \"Tekla.Macros.Runtime\"\n#pragma warning restore 1633\n"+
                "using System;using System.IO;using System.Text;using System.Reflection;namespace UserMacros { public sealed class Macro {"+
                "[Tekla.Macros.Runtime.MacroEntryPointAttribute()] public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime) {"+
                "string request=Encoding.UTF8.GetString(Convert.FromBase64String(\""+Encode(request)+"\"));try {"+
                "Assembly.LoadFile(Encoding.UTF8.GetString(Convert.FromBase64String(\""+Encode(assembly)+"\")))"+
                ".GetType(\"Tekla.Technology.Akit.UserScript.PHU_DimensionRevision\",true).GetMethod(\"RunNativeColorRequest\").Invoke(null,new object[]{request});"+
                "} catch(Exception ex) { string path=Path.Combine(Path.GetDirectoryName(request),\"result.txt\");"+
                "File.WriteAllText(path+\".tmp\",\"ERROR: \"+ex.GetBaseException().Message);File.Move(path+\".tmp\",path); } } } }";
        }
        sealed class NativeColorState
        {
            public D.StraightDimension Child;
            public string Geometry;
            public D.StraightDimension.StraightDimensionAttributes Attributes;
            public D.StraightDimensionSet.StraightDimensionSetAttributes SetAttributes;
            public int Id,Before,After;
        }
        static void CheckColorDeadline(string folder,long deadline)
        {if(File.Exists(Path.Combine(folder,"cancel"))||DateTime.UtcNow.Ticks>deadline)throw new InvalidOperationException("Yêu cầu ghi màu đã hết hạn; hãy kiểm tra lại.");}
        static bool IsNativeColorJobRunning(string folder)
        {
            string path=Path.Combine(folder,"running");if(!File.Exists(path))return false;
            try
            {
                var parts=File.ReadAllText(path).Split('|');
                var process=System.Diagnostics.Process.GetProcessById(Int32.Parse(parts[0],CultureInfo.InvariantCulture));
                return String.Equals(process.ProcessName,"TeklaStructures",StringComparison.OrdinalIgnoreCase)&&
                    (parts.Length==1||process.StartTime.ToUniversalTime().Ticks==Int64.Parse(parts[1],CultureInfo.InvariantCulture));
            }
            catch(ArgumentException){return false;}
            catch{return true;} // Unreadable status is uncertain, never permission to overlap.
        }

        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        static void RequireNativeMainThread()
        {
            var process=System.Diagnostics.Process.GetCurrentProcess();uint owner;
            if(!String.Equals(process.ProcessName,"TeklaStructures",StringComparison.OrdinalIgnoreCase)||
                process.MainWindowHandle==IntPtr.Zero||GetWindowThreadProcessId(process.MainWindowHandle,out owner)!=GetCurrentThreadId()||
                owner!=(uint)process.Id)
                throw new InvalidOperationException("Chỉ được ghi thuộc tính native trên luồng chính Tekla; chưa ghi màu.");
        }
        delegate IntPtr ColorHook(int code,IntPtr wParam,IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        struct ColorWindowMessage { public IntPtr LParam,WParam;public uint Message;public IntPtr Window; }
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int kind,ColorHook hook,IntPtr module,uint thread);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);

        // Macro AppDomains have no WPF Application.Current. Dispatch a private message
        // through a temporary hook on THIS process's main-window thread. Never install
        // a global/cross-process hook. Keep the callback rooted until it has returned.
        static void InvokeColorOnMainThread(Action action)
        {
            var process=System.Diagnostics.Process.GetCurrentProcess();uint owner;
            IntPtr window=process.MainWindowHandle;
            uint thread=GetWindowThreadProcessId(window,out owner);
            if(window==IntPtr.Zero||thread==0||owner!=(uint)process.Id)
                throw new InvalidOperationException("Không tìm được luồng chính Tekla.");
            if(thread==GetCurrentThreadId()){RequireNativeMainThread();action();return;}
            uint message=RegisterWindowMessage("TTSK.RevisionColor."+Guid.NewGuid().ToString("N"));
            if(message==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr hook=IntPtr.Zero;Exception failure=null;int started=0;
            using(var completed=new System.Threading.ManualResetEvent(false))
            {
                ColorHook callback=(code,w,l)=>
                {
                    if(code>=0)
                    {
                        var msg=(ColorWindowMessage)Marshal.PtrToStructure(l,typeof(ColorWindowMessage));
                        if(msg.Window==window&&msg.Message==message&&System.Threading.Interlocked.CompareExchange(ref started,1,0)==0)
                        {
                            try{RequireNativeMainThread();action();}
                            catch(Exception ex){failure=ex;}
                            finally{completed.Set();}
                        }
                    }
                    return CallNextHookEx(hook,code,w,l);
                };
                hook=SetWindowsHookEx(4,callback,IntPtr.Zero,thread); // WH_CALLWNDPROC
                if(hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    IntPtr ignored;
                    SendMessageTimeout(window,message,IntPtr.Zero,IntPtr.Zero,2,25000,out ignored);
                    // On timeout, cancel a callback that has not started. If it already
                    // started, retain the delegate until rollback/verification finishes.
                    if(System.Threading.Interlocked.CompareExchange(ref started,-1,0)==0)
                        throw new TimeoutException("Luồng chính Tekla chưa nhận yêu cầu ghi màu.");
                    completed.WaitOne();
                    if(failure!=null)throw new InvalidOperationException(failure.Message,failure);
                }
                finally{UnhookWindowsHookEx(hook);GC.KeepAlive(callback);}
            }
        }

        // Native Properties must never run on the macro worker thread: Tekla asserts.
        public static void RunNativeColorRequest(string request)
        {
            string folder=Path.GetDirectoryName(request),response=Path.Combine(folder,"result.txt");
            try
            {
                var rows=File.ReadAllLines(request,Encoding.UTF8);
                CheckColorDeadline(folder,Int64.Parse(rows[4],CultureInfo.InvariantCulture));
                if(!String.Equals(System.Diagnostics.Process.GetCurrentProcess().ProcessName,"TeklaStructures",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Chỉ được ghi thuộc tính native trong Tekla; chưa ghi màu.");
                RunNativeColorRequestCore(request);
            }
            catch(Exception ex)
            {
                // Core publishes its own result. Never replace a verified rollback report.
                if(!File.Exists(response))
                {File.WriteAllText(response+".tmp","ERROR: "+ex.GetBaseException().Message+" Chưa ghi màu.",Encoding.UTF8);File.Move(response+".tmp",response);}
            }
        }
        static void RunNativeColorRequestCore(string request)
        {
            // Public Open API remotes to Tekla; calling it from the native main
            // thread waits on itself. Keep preflight/verification on the macro worker.
            uint owner;var process=System.Diagnostics.Process.GetCurrentProcess();
            if(GetWindowThreadProcessId(process.MainWindowHandle,out owner)==GetCurrentThreadId())
                throw new InvalidOperationException("Drawing API phải chạy ngoài luồng chính Tekla.");
            string folder=Path.GetDirectoryName(request),response=Path.Combine(folder,"result.txt");
            var states=new List<NativeColorState>();var touched=new List<NativeColorState>();
            D.Drawing drawing=null;
            string status="DONE";
            try
            {
                var rows=File.ReadAllLines(request,Encoding.UTF8);long deadline=Int64.Parse(rows[4],CultureInfo.InvariantCulture);
                CheckColorDeadline(folder,deadline);
                File.WriteAllText(Path.Combine(folder,"running"),process.Id.ToString(CultureInfo.InvariantCulture)+"|"+
                    process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
                CheckColorDeadline(folder,deadline);
                File.WriteAllText(Path.Combine(folder,"phase.txt"),"API preflight (macro worker)");
                var model=new Tekla.Structures.Model.Model();drawing=new D.DrawingHandler().GetActiveDrawing();
                if(!model.GetConnectionStatus()||!String.Equals(model.GetInfo().ModelPath,Decode(rows[0]),StringComparison.OrdinalIgnoreCase)||drawing==null||
                    ((Tekla.Structures.Identifier)Member(drawing,"Identifier")).GUID.ToString()!=rows[1]||Hash(Decode(rows[2]))!=rows[3])
                    throw new InvalidOperationException("Model, bản vẽ hoặc snapshot đã thay đổi; chưa ghi màu.");
                var expected=new Dictionary<int,string>();var desired=new Dictionary<int,Tuple<int,int>>();
                foreach(string row in rows.Skip(5))
                {
                    var p=row.Split('\t');int id=Int32.Parse(p[1],CultureInfo.InvariantCulture);
                    if(p[0]=="S")expected.Add(id,Decode(p[2]));
                    else if(p[0]=="C")desired.Add(id,Tuple.Create(Int32.Parse(p[2]),Int32.Parse(p[3])));
                    else throw new InvalidDataException("Yêu cầu ghi màu không hợp lệ.");
                }
                var sets=new Dictionary<int,D.StraightDimensionSet>();var views=drawing.GetSheet().GetAllViews();
                while(views.MoveNext())
                {
                    var view=views.Current as D.View;if(view==null)continue;
                    var objects=view.GetAllObjects(typeof(D.StraightDimensionSet));
                    while(objects.MoveNext()){var set=objects.Current as D.StraightDimensionSet;if(set!=null&&expected.ContainsKey(Id(set)))sets[Id(set)]=set;}
                }
                var childrenBySet=sets.ToDictionary(p=>p.Key,p=>ChildrenOf(p.Value));
                if(sets.Count!=expected.Count||childrenBySet.Any(p=>ChildrenGeometryKey(p.Value)!=expected[p.Key]))
                    throw new InvalidOperationException("Cụm DIM đã đổi chân đo, vị trí hoặc số đoạn; hãy kiểm tra lại.");
                foreach(var children in childrenBySet.Values)foreach(var child in children)
                {
                    states.Add(new NativeColorState{Id=Id(child),Child=child,Geometry=GeometryKey(child),
                        Attributes=desired.Count==0?null:CopyValue(child.Attributes),
                        SetAttributes=desired.Count==0?null:CopyValue(ChildAttributes(child))});
                }
                if(desired.Keys.Any(id=>states.Count(s=>Id(s.Child)==id)!=1))throw new InvalidOperationException("Không tìm thấy duy nhất DIM cần ghi màu.");
                CheckColorDeadline(folder,deadline);
                File.WriteAllText(Path.Combine(folder,"phase.txt"),"Native child font properties (main thread)");
                WithNativeColorFactory((factory,create,update)=>
                {
                    // No Drawing/Model API calls inside this main-thread callback.
                    foreach(var state in states)
                    {
                        int actual=NativeFontColor(factory,create,update,state.Id,null);Tuple<int,int> target;
                        bool change=desired.TryGetValue(state.Id,out target);
                        if(change&&target.Item1!=-1&&actual!=target.Item1)throw new InvalidOperationException("Màu DIM đã thay đổi; chưa ghi đè.");
                        state.Before=actual;state.After=change?target.Item2:actual;
                    }
                    foreach(var state in states.Where(s=>s.Before!=s.After))
                    {
                        CheckColorDeadline(folder,deadline);touched.Add(state);
                        NativeFontColor(factory,create,update,state.Id,state.After);
                    }
                    foreach(var state in touched.Count==0?Enumerable.Empty<NativeColorState>():states)
                    {
                        int actual=NativeFontColor(factory,create,update,state.Id,null);
                        if(actual!=state.After)throw new InvalidOperationException("Thuộc tính native chưa lưu màu DIM "+state.Id);
                    }
                });
                File.WriteAllText(Path.Combine(folder,"phase.txt"),"API verify and commit (macro worker)");
                CheckColorDeadline(folder,deadline);
                if(touched.Count>0&&!drawing.CommitChanges("TTSK Revision: individual dimension text colour"))throw new InvalidOperationException("Chưa commit được màu Revision.");
                if(touched.Count>0)
                {
                    VerifyNativeColors(states,sets,expected);
                    WithNativeColorFactory((factory,create,update)=>
                    {
                        foreach(var state in states)
                            if(NativeFontColor(factory,create,update,state.Id,null)!=state.After)
                                throw new InvalidOperationException("Màu native thay đổi sau commit ở DIM "+state.Id);
                    });
                }
                else
                {
                    // A read/no-op never calls SetProperty or commits. Validate the
                    // full chain again for concurrent edits without repeating colour
                    // reads and attribute serialization intended for write rollback.
                    foreach(var pair in sets)
                        if(!pair.Value.Select()||SetGeometryKey(pair.Value)!=expected[pair.Key])
                            throw new InvalidOperationException("Cụm DIM vừa thay đổi trong lúc đọc; hãy kiểm tra lại.");
                }
                File.WriteAllLines(Path.Combine(folder,"colors.txt"),states.Select(s=>s.Id+"\t"+s.After),Encoding.UTF8);
                File.WriteAllText(Path.Combine(folder,"changed.txt"),touched.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch(Exception ex)
            {
                bool restored=true;
                // Restore every sibling too, in case the native version propagates attributes.
                if(touched.Count>0)
                {
                    try
                    {
                        WithNativeColorFactory((factory,create,update)=>
                        {
                            foreach(var state in states.AsEnumerable().Reverse())
                                try{NativeFontColor(factory,create,update,state.Id,state.Before);}catch{restored=false;}
                        });
                        restored&=drawing.CommitChanges("TTSK Revision: restore text colours");
                        WithNativeColorFactory((factory,create,update)=>
                        {
                            foreach(var state in states)
                                restored&=NativeFontColor(factory,create,update,state.Id,null)==state.Before;
                        });
                        foreach(var state in states)
                            restored&=state.Child.Select()&&GeometryKey(state.Child)==state.Geometry&&
                                state.Attributes.IsEqual(state.Child.Attributes)&&state.SetAttributes.IsEqual(ChildAttributes(state.Child));
                    }
                    catch{restored=false;}
                }
                status="ERROR: "+ex.GetBaseException().Message+(touched.Count==0?" Chưa ghi màu.":restored?" Đã khôi phục màu ban đầu.":" Có thuộc tính chưa khôi phục được; kiểm tra bản vẽ trước khi lưu.");
            }
            File.WriteAllText(response+".tmp",status,Encoding.UTF8);File.Move(response+".tmp",response);
            if(File.Exists(Path.Combine(folder,"running")))File.Delete(Path.Combine(folder,"running"));
        }
        static void WithNativeColorFactory(Action<object,MethodInfo,MethodInfo> action)
        {
            string path=Path.Combine(Path.GetDirectoryName(typeof(D.Drawing).Assembly.Location),"TeklaStructuresCLI.dll");
            InvokeColorOnMainThread(()=>
            {
                RequireNativeMainThread();
                var type=Assembly.LoadFrom(path).GetType("teklastructures.cli.DrawingPropertiesObjectFactory",true);
                object factory=Activator.CreateInstance(type);
                try{action(factory,type.GetMethod("CreateObject",new[]{typeof(int),typeof(string)}),type.GetMethod("UpdateObject"));}
                finally{if(factory is IDisposable)((IDisposable)factory).Dispose();}
            });
        }
        static int NativeFontColor(object factory,MethodInfo create,MethodInfo update,int id,int? color)
        {
            object proxy=create.Invoke(factory,new object[]{id,String.Empty});
            if(proxy==null)throw new InvalidOperationException("Tekla không cung cấp thuộc tính riêng của đoạn DIM.");
            try
            {
                var type=proxy.GetType();var get=type.GetMethod("GetProperty");
                object value=get.Invoke(proxy,new object[]{"proFontColor"});
                if(!(value is int))throw new InvalidOperationException("Tekla không hỗ trợ màu chữ riêng qua proFontColor.");
                if(color.HasValue&&(!(bool)type.GetMethod("SetProperty").Invoke(proxy,new object[]{"proFontColor",color.Value})||
                    !(bool)update.Invoke(factory,new[]{proxy})))throw new InvalidOperationException("Không ghi được màu chữ riêng của đoạn DIM.");
                return (int)value;
            }
            finally{((IDisposable)proxy).Dispose();}
        }
        static void VerifyNativeColors(IList<NativeColorState> states,IDictionary<int,D.StraightDimensionSet> sets,IDictionary<int,string> expected)
        {
            foreach(var pair in sets)
                if(!pair.Value.Select()||SetGeometryKey(pair.Value)!=expected[pair.Key])throw new InvalidOperationException("Cụm DIM không còn nguyên hình học/ID sau ghi màu.");
            foreach(var state in states)
            {
                var attributes=CopyValue(state.SetAttributes);
                if(!state.Child.Select())throw new InvalidOperationException("Không đọc lại được DIM "+state.Id);
                bool geometry=GeometryKey(state.Child)==state.Geometry,marks=state.Attributes.IsEqual(state.Child.Attributes);
                var actual=CopyValue(ChildAttributes(state.Child));
                // Font colour is certified separately through the native child proxy.
                // The public API may return either the inherited colour or the override.
                actual.Text.Font.Color=attributes.Text.Font.Color;
                if(!geometry||!marks||!attributes.IsEqual(actual))
                    throw new InvalidOperationException("DIM "+state.Id+": geometry="+geometry+", marks="+marks+
                        ", non-color attributes="+attributes.IsEqual(actual)+"; đang khôi phục.");
            }
        }
    }
}

// Source: PHU_RevisionDelete.cs
namespace Tekla.Technology.Akit.UserScript
{
using System;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;


    public static partial class PHU_DimensionRevision
    {
        public static List<G.Point> DeleteFrame(double x,double y,double width,double height,double margin)
        {
            if(new[]{x,y,width,height,margin}.Any(v=>Double.IsNaN(v)||Double.IsInfinity(v))||width<=0||height<=0||margin<0)
                throw new InvalidOperationException("Khung mặt cắt không hợp lệ; chưa xóa view.");
            return new List<G.Point>{new G.Point(x-margin,y-margin),new G.Point(x+width+margin,y-margin),
                new G.Point(x+width+margin,y+height+margin),new G.Point(x-margin,y+height+margin)};
        }
        public static string DeleteRevisionSection()
        {
            var model=new Tekla.Structures.Model.Model();var handler=new D.DrawingHandler();
            var drawing=handler.GetActiveDrawing();
            if(!model.GetConnectionStatus()||!handler.GetConnectionStatus()||drawing==null)throw new InvalidOperationException("Chưa mở bản vẽ Tekla.");
            var context=new Result {Drawing=drawing,ModelPath=model.GetInfo().ModelPath};
            // Read the user's existing selection. No picker, prompt, macro, dialog or UI text is sent to Tekla.
            var selection=handler.GetDrawingObjectSelector().GetSelected();var selected=new List<D.View>();
            while(selection.MoveNext())
            {
                var obj=selection.Current;var candidate=obj as D.View??obj.GetView() as D.View;
                if(candidate!=null&&!selected.Any(v=>v.IsSameDatabaseObject(candidate)))selected.Add(candidate);
            }
            if(selected.Count!=1||selected[0].ViewType!=D.View.ViewTypes.SectionView)
                throw new InvalidOperationException("Chọn một view mặt cắt trên bản vẽ, rồi bấm Delete REV.");
            return DeleteRevisionSection(context,selected[0]);
        }
        public static string DeleteRevisionSection(Result context,D.View view)
        {
            var drawing=EnsureActive(context);
            if(view==null||view.ViewType!=D.View.ViewTypes.SectionView||!view.Select())
                throw new InvalidOperationException("Chọn một view mặt cắt hợp lệ; chưa thay đổi bản vẽ.");
            var lineAttributes=PHU_AllPartsDeletedMarker.CreateRevisionLineAttributes();
            var cloudAttributes=new D.Cloud.CloudAttributes();
            cloudAttributes.LoadAttributes("revcloud");
            cloudAttributes.Line=CopyValue(lineAttributes.Line);
            // Native revcloud geometry with the same tested red/DashDot style as AllPartsDeleted.
            double bulge=.5;
            var corners=DeleteFrame(view.Origin.X+view.FrameOrigin.X,view.Origin.Y+view.FrameOrigin.Y,view.Width,view.Height,1.0);
            corners.Reverse();
            var points=new D.PointList();foreach(var p in corners)points.Add(p);
            var created=new List<D.DrawingObject>();bool deleteAttempted=false;
            var sheet=drawing.GetSheet();
            try
            {
                var cloud=new D.Cloud(sheet,points,cloudAttributes){Bulge=bulge};created.Add(cloud);
                if(!cloud.Insert()||!cloud.Select()||!cloud.Attributes.IsEqual(cloudAttributes)||Math.Abs(cloud.Bulge-bulge)>.0001)
                    throw new InvalidOperationException("Chưa xác nhận được cloud và thuộc tính mẫu.");
                // Snap to the native returned cloud vertices, not approximated pixel positions.
                var actual=cloud.Points.Cast<G.Point>().ToList();
                var snapped=new List<G.Point>();
                foreach(var corner in corners)
                {
                    var near=actual.OrderBy(p=>G.Distance.PointToPoint(p,corner)).FirstOrDefault();
                    if(near==null||G.Distance.PointToPoint(near,corner)>.02)throw new InvalidOperationException("Cloud không giữ giao điểm góc; chưa vẽ đường chéo hoặc xóa view.");
                    snapped.Add(new G.Point(near));
                }
                for(int i=0;i<2;i++)
                {
                    var line=new D.Line(sheet,snapped[i],snapped[i+2],CopyValue(lineAttributes));created.Add(line);
                    if(!line.Insert())throw new InvalidOperationException("Không tạo được đường chéo REV.");
                    // Reuse this operation's exact preset; still reapply after Insert
                    // because Tekla can normalize attributes during insertion.
                    line.Attributes=CopyValue(lineAttributes);
                    if(!line.Modify()||!line.Select()||!line.Attributes.IsEqual(lineAttributes)||Math.Abs(line.Bulge)>.001||
                        G.Distance.PointToPoint(line.StartPoint,snapped[i])>.02||G.Distance.PointToPoint(line.EndPoint,snapped[i+2])>.02)
                        throw new InvalidOperationException("Chưa xác nhận được đường chéo bắt đúng cloud và nét mẫu.");
                }
                EnsureActive(context);
                if(!view.Select())throw new InvalidOperationException("View mặt cắt đã thay đổi trong lúc thao tác.");
                var latest=DeleteFrame(view.Origin.X+view.FrameOrigin.X,view.Origin.Y+view.FrameOrigin.Y,view.Width,view.Height,1.0);
                latest.Reverse();
                if(latest.Where((p,i)=>G.Distance.PointToPoint(p,corners[i])>.02).Any())throw new InvalidOperationException("Khung view vừa di chuyển; chưa xóa view.");
                deleteAttempted=true;
                if(!view.Delete()||view.Select())throw new InvalidOperationException("Tekla chưa xác nhận xóa view mặt cắt.");
                if(!drawing.CommitChanges("TTSK Delete REV: cloud and cross, delete section"))throw new InvalidOperationException("Tekla chưa xác nhận commit Delete REV.");
                return "Đã tạo cloud + 2 đường chéo theo mẫu và xóa view mặt cắt.";
            }
            catch(Exception failure)
            {
                bool viewExists=!deleteAttempted;
                if(deleteAttempted){try{viewExists=view.Select();}catch{viewExists=false;}}
                if(!viewExists)throw new InvalidOperationException(failure.Message+" View có thể đã bị xóa; đã giữ cloud và đường chéo. Kiểm tra hoặc Undo trong Tekla trước khi lưu.",failure);
                bool cleaned=true;
                foreach(var obj in created.AsEnumerable().Reverse())try{if(obj.Select())cleaned&=obj.Delete();}catch{cleaned=false;}
                if(created.Count>0)try{cleaned&=drawing.CommitChanges("TTSK Delete REV: cleanup incomplete markers");}catch{cleaned=false;}
                throw new InvalidOperationException(failure.Message+(cleaned?" Đã giữ nguyên view và dọn các đối tượng vừa tạo.":" View còn nguyên; kiểm tra cloud/line chưa dọn hết."),failure);
            }
        }
    }
}

