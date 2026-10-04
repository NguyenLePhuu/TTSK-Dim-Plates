using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using M = Tekla.Structures.Model;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;

namespace Tekla.Technology.Akit.UserScript
{
    public static class PHU_AutoDimSlot08
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }
        public static string LastRunAudit { get; private set; }
        public static string Audit() { return PHU_NeighborCleanup.Analyze().Report(); }
        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunAudit = String.Empty;
            try
            {
                var plan = PHU_NeighborCleanup.Analyze();
                LastRunAudit = plan.Report();
                int count = plan.Apply();
                LastRunSucceeded = true;
                LastRunMessage = "Neighbor: an " + count + ", hien lai " + plan.RestoredCount + " plate, "
                    + plan.RestoredSupportCount + " thep tham chieu trong " + plan.ViewCount
                    + " view. " + plan.Warnings.Count + " canh bao. "
                    + String.Join("; ", plan.Warnings.Take(3));
            }
            catch (Exception ex) { LastRunMessage = "Neighbor cleanup: " + ex.Message; }
        }
    }

    // No selection dependency, work-plane change, model write, or recursive bolt expansion.
    public static class PHU_NeighborCleanup
    {
        public sealed class Node
        {
            public int Id, Assembly;
            public bool Plate, Dummy;
            public string Label;
            public M.Part Part;
        }
        public sealed class Edge
        {
            public int A, B;
            public bool Weld;
            public string Evidence;
        }
        public sealed class Item
        {
            public D.Part Part;
            public int ModelId, ViewId;
            public bool WasHidden;
            public string Reason;
        }
        public sealed class Plan
        {
            public D.Drawing Drawing;
            public int MainId, ViewCount;
            public readonly List<Item> Items = new List<Item>();
            public readonly List<string> Warnings = new List<string>();
            public readonly Dictionary<int, Node> Nodes = new Dictionary<int, Node>();
            public readonly List<Edge> Edges = new List<Edge>();
            // Trạng thái lần chạy: chỉ hiện lại plate nối main-neighbor đã xác minh trong cùng view.
            public readonly List<Item> RestorePlates = new List<Item>();
            // Chỉ nhận support là primary input của Connection, secondary là plate nối cụm main.
            public readonly Dictionary<int, HashSet<int>> ReferenceSupportPlates = new Dictionary<int, HashSet<int>>();
            public readonly List<Item> RestoreSupports = new List<Item>();
            public int RestoredCount { get; private set; }
            public int RestoredSupportCount { get; private set; }
            public Dictionary<int, string> Keep;
            public string Report()
            {
                var s = new StringBuilder("NEIGHBOR CLEANUP - READ ONLY\n");
                s.AppendLine("Main=" + MainId + " Views=" + ViewCount);
                foreach (var e in Edges.OrderBy(e => e.A).ThenBy(e => e.B))
                    s.AppendLine("LINK " + e.A + " -> " + e.B + " " + e.Evidence);
                foreach (var i in Items.OrderBy(i => i.ViewId).ThenBy(i => i.ModelId))
                    s.AppendLine("VIEW=" + i.ViewId + " P" + i.ModelId + " " + Nodes[i.ModelId].Label
                        + " hidden=" + i.WasHidden + " " + i.Reason);
                foreach (string w in Warnings) s.AppendLine("WARNING " + w);
                s.AppendLine("WouldHide=" + Items.Count(i => !i.WasHidden && !Keep.ContainsKey(i.ModelId)));
                s.AppendLine("WouldShow=" + (RestorePlates.Count + RestoreSupports.Count));
                s.AppendLine("WouldShowSupports=" + RestoreSupports.Count);
                return s.ToString();
            }
            public int Apply()
            {
                var handler = new D.DrawingHandler();
                var active = handler.GetActiveDrawing();
                if (active == null || !active.IsSameDatabaseObject(Drawing))
                    throw new InvalidOperationException("Ban ve active da thay doi.");
                RestoredCount = 0;
                RestoredSupportCount = 0;
                // Hai danh sách đã có bằng chứng riêng; không hiện tất cả part thuộc Keep.
                var toRestore = RestorePlates.Concat(RestoreSupports).ToList();
                var targets = Items.Where(i => !i.WasHidden && !Keep.ContainsKey(i.ModelId)).ToList();
                if (targets.Count == 0 && toRestore.Count == 0) return 0;
                foreach (var i in toRestore)
                    if (!i.Part.Select() || !i.Part.Hideable.IsHidden)
                        throw new InvalidOperationException("Trang thai part can hien lai da thay doi; chay lai lenh.");
                var changed = new List<Item>();
                var restoredPlates = new List<Item>();
                try
                {
                    changed.AddRange(targets);
                    if (targets.Count > 0) RunBatchMacro(Drawing, targets);
                    // Verify in one enumeration per view instead of one remoting Select per part.
                    var verified = new HashSet<D.Part>();
                    foreach (var group in changed.GroupBy(i => i.ViewId))
                    {
                        var objects = group.First().Part.GetView().GetAllObjects(typeof(D.Part));
                        var pending = group.ToDictionary(i => i.ModelId);
                        while (objects.MoveNext())
                        {
                            var part = (D.Part)objects.Current;
                            Item item;
                            if (pending.TryGetValue(part.ModelIdentifier.ID, out item) && part.Hideable.IsHidden)
                                verified.Add(item.Part);
                        }
                    }
                    if (verified.Count != changed.Count) throw new InvalidOperationException("Hide read-back mismatch.");
                    // Chỉ hiện sau khi macro ẩn đã kết thúc; không đua với macro pending.
                    foreach (var i in toRestore)
                    {
                        restoredPlates.Add(i);
                        i.Part.Hideable.ShowInDrawingView();
                        if (!i.Part.Modify() || !i.Part.Select() || i.Part.Hideable.IsHidden)
                            throw new InvalidOperationException("Show part read-back mismatch.");
                    }
                    if ((changed.Count > 0 || restoredPlates.Count > 0) && !Drawing.CommitChanges())
                        throw new InvalidOperationException("Commit drawing that bai.");
                    RestoredCount = RestorePlates.Count;
                    RestoredSupportCount = RestoreSupports.Count;
                    return changed.Count;
                }
                catch (BatchPendingException) { throw; } // Never race a possibly running native command with rollback.
                catch (BatchPreflightException) { throw; }
                catch
                {
                    bool restored = true;
                    foreach (var i in restoredPlates)
                    {
                        try
                        {
                            i.Part.Hideable.HideFromDrawingView();
                            restored &= i.Part.Modify() && i.Part.Select() && i.Part.Hideable.IsHidden;
                        }
                        catch { restored = false; }
                    }
                    foreach (var i in changed)
                    {
                        try
                        {
                            i.Part.Hideable.ShowInDrawingView();
                            restored &= i.Part.Modify() && i.Part.Select() && !i.Part.Hideable.IsHidden;
                        }
                        catch { restored = false; }
                    }
                    try { if (changed.Count > 0 || restoredPlates.Count > 0) restored &= Drawing.CommitChanges(); }
                    catch { restored = false; }
                    if (!restored) throw new InvalidOperationException("Rollback chua duoc xac minh; kiem tra ban ve.");
                    throw;
                }
            }
        }

        private static void RunBatchMacro(D.Drawing drawing, List<Item> targets)
        {
            using (var mutex = new System.Threading.Mutex(false, "Local\\TTSK_NeighborBatchHide"))
            {
                bool locked;
                try { locked = mutex.WaitOne(0); }
                catch (System.Threading.AbandonedMutexException) { locked = true; }
                if (!locked) throw new BatchPendingException("Neighbor batch dang chay trong ung dung khac.");
                try { RunBatchMacroCore(drawing, targets); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        private static void RunBatchMacroCore(D.Drawing drawing, List<Item> targets)
        {
            string template = BatchMacroSource;
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TTSK_NeighborBatch");
            System.IO.Directory.CreateDirectory(folder);
            string request = System.IO.Path.Combine(folder, "request.txt");
            if (System.IO.File.Exists(request))
            {
                string[] previous = System.IO.File.ReadAllLines(request);
                if (previous.Length > 0 && !System.IO.File.Exists(previous[0]))
                    throw new BatchPendingException("Macro truoc chua ket thuc; kiem tra Tekla truoc khi chay lai.");
            }
            string result = System.IO.Path.Combine(folder, "result.txt");
            if (System.IO.File.Exists(result)) System.IO.File.Delete(result);
            string source = System.IO.Path.Combine(folder, "NeighborBatch.cs");
            string keys = String.Join(";", targets.Select(i => i.ViewId+":"+RuntimeId(i.Part)+":"+i.ModelId));
            // Stable source enables Tekla's macro compilation cache on subsequent runs.
            if (!System.IO.File.Exists(source) || System.IO.File.ReadAllText(source) != template)
                System.IO.File.WriteAllText(source, template);
            System.IO.File.WriteAllLines(request, new[] { result, RuntimeId(drawing.GetSheet()).ToString(), keys });
            if (!M.Operations.Operation.RunMacro(source))
            {
                System.IO.File.WriteAllText(result, "ERROR: Tekla refused macro");
                throw new BatchPreflightException("Tekla refused batch hide macro");
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!System.IO.File.Exists(result) && timer.ElapsedMilliseconds < 15000)
                System.Threading.Thread.Sleep(50);
            if (!System.IO.File.Exists(result))
                throw new BatchPendingException("Batch macro chua tra ket qua. Khong chay lai khi macro con dang chay. Log: " + folder);
            string response = System.IO.File.ReadAllText(result);
            if (response.StartsWith("BEFORE:")) throw new BatchPreflightException(response);
            if (response != "DONE") throw new InvalidOperationException(response);
        }
        private const string BatchMacroSource = @"#pragma warning disable 1633
#pragma reference ""Tekla.Macros.Wpf.Runtime""
#pragma reference ""Tekla.Macros.Runtime""
#pragma reference ""Tekla.Structures""
#pragma reference ""Tekla.Structures.Drawing""
#pragma warning restore 1633
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Tekla.Structures.Drawing;
namespace UserMacros
{
    public sealed class Macro
    {
        static int Id(object o)
        {
            return ((Tekla.Structures.Identifier)o.GetType().GetProperty(""Identifier"",
                BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o,null)).ID;
        }
        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            string[] request = File.ReadAllLines(Path.Combine(Path.GetTempPath(), ""TTSK_NeighborBatch"", ""request.txt""));
            string result = request[0];
            bool invoked = false;
            try
            {
                var handler = new DrawingHandler();
                var drawing = handler.GetActiveDrawing();
                if (drawing == null || Id(drawing.GetSheet()) != Int32.Parse(request[1]))
                    throw new Exception(""Active drawing changed"");
                var expected = new HashSet<string>(request[2].Split(';'));
                var selection = new ArrayList();
                var found = new HashSet<string>();
                var views = drawing.GetSheet().GetAllViews();
                while (views.MoveNext())
                {
                    var view = views.Current as View;
                    if (view == null) continue;
                    int viewId=Id(view);
                    var parts=view.GetAllObjects(typeof(Part));
                    while(parts.MoveNext())
                    {
                        var part=(Part)parts.Current;
                        string key=viewId+"":""+Id(part)+"":""+part.ModelIdentifier.ID;
                        if(!expected.Contains(key)) continue;
                        if(part.Hideable.IsHidden) throw new Exception(""Target state changed"");
                        found.Add(key); selection.Add(part);
                    }
                }
                if(!expected.SetEquals(found)) throw new Exception(""Target snapshot mismatch"");
                var selector=handler.GetDrawingObjectSelector();
                if(!selector.SelectObjects(selection,false)) throw new Exception(""Batch selection failed"");
                var actual=new HashSet<string>();
                var selected=selector.GetSelected();
                while(selected.MoveNext())
                {
                    var part=selected.Current as Part;
                    if(part==null) throw new Exception(""Unexpected selected object"");
                    actual.Add(Id(part.GetView())+"":""+Id(part)+"":""+part.ModelIdentifier.ID);
                }
                if(!expected.SetEquals(actual)) throw new Exception(""Selected set differs from plan"");
                invoked = true;
                runtime.Get<Tekla.Macros.Wpf.Runtime.IWpfMacroHost>()
                    .InvokeCommand(""CommandRepository"", ""View.HideObjectFromView"");
                File.WriteAllText(result,""DONE"");
            }
            catch(Exception ex) { File.WriteAllText(result,(invoked ? ""ERROR: "" : ""BEFORE: "")+ex.Message); }
        }
    }
}";

        private sealed class BatchPendingException : Exception
        {
            public BatchPendingException(string message) : base(message) { }
        }
        private sealed class BatchPreflightException : Exception
        {
            public BatchPreflightException(string message) : base(message) { }
        }

        // The primary fabrication unit can contain any number of members and subassemblies.
        // Follow connector plates (bolt or weld), then stop at the receiving structural unit.
        // Dummy references never expand the connection graph; their proximity is evaluated separately.
        public static Dictionary<int, string> Resolve(Dictionary<int, Node> nodes,
            List<Edge> edges, HashSet<int> primary, List<string> warnings)
        {
            var keep = primary.ToDictionary(id => id, id => "KEEP primary assembly");
            var seeds = new HashSet<int>();
            foreach (var e in edges)
            {
                if ((primary.Contains(e.A) && nodes[e.A].Dummy) || (primary.Contains(e.B) && nodes[e.B].Dummy)) continue;
                if (primary.Contains(e.A) && !primary.Contains(e.B)) seeds.Add(e.B);
                if (primary.Contains(e.B) && !primary.Contains(e.A)) seeds.Add(e.A);
            }
            foreach (int seed in seeds.OrderBy(id => id))
            {
                keep[seed] = "KEEP direct bolt/weld to primary assembly";
                if (nodes[seed].Dummy) continue; // Retain a directly attached reference; never expand through it.
                var visited = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                bool memberFound = false;
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    if (!visited.Add(id)) continue;
                    Node n = nodes[id];
                    if (!n.Plate) memberFound = true;
                    foreach (var e in edges.Where(e => e.A == id || e.B == id))
                    {
                        int next = e.A == id ? e.B : e.A;
                        Node other = nodes[next];
                        if (primary.Contains(next) || other.Dummy) continue;
                        if (!CanFollow(n, other, e)) continue;
                        if (!keep.ContainsKey(next)) keep[next] = "KEEP connector path from P" + seed + " via " + e.Evidence;
                        queue.Enqueue(next);
                    }
                }
                if (nodes[seed].Plate && !memberFound)
                {
                    warnings.Add(UnresolvedPlateWarning(seed));
                }
            }
            if (seeds.Count == 0)
            {
                warnings.Add("Khong co bang chung lien ket ngoai; giu cac doi tuong de tranh an nham.");
                foreach (var n in nodes.Values) if (!keep.ContainsKey(n.Id)) keep[n.Id] = "KEEP insufficient connection evidence";
            }
            return keep;
        }

        private static string UnresolvedPlateWarning(int id)
        { return "P" + id + ": chua xac minh thep hinh noi truc tiep; chi giu plate da co bang chung."; }

        private static bool CanFollow(Node from, Node to, Edge edge)
        {
            // A plate is an interface, regardless of which assembly owns it.
            // A reached member is a stopping boundary for bolt/plate expansion.
            return from.Plate && !to.Plate && !to.Dummy;
        }

        public static Plan Analyze()
        {
            var model = new M.Model();
            var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
                throw new InvalidOperationException("Khong ket noi Tekla.");
            var p = new Plan { Drawing = handler.GetActiveDrawing() };
            if (!(p.Drawing is D.AssemblyDrawing))
                throw new InvalidOperationException("Slot 08 hien ho tro Assembly Drawing.");
            M.Part main = PHU_MainPartResolver.Resolve(model, p.Drawing);
            if (main == null) throw new InvalidOperationException("Khong tim thay main part.");
            p.MainId = main.Identifier.ID;
            M.Assembly assembly = main.GetAssembly();
            var primary = new HashSet<int>();
            foreach (M.Part part in AssemblyParts(assembly))
            {
                Add(p, part);
                primary.Add(part.Identifier.ID);
            }
            if (!primary.Contains(p.MainId)) throw new InvalidOperationException("Main assembly khong hop le.");
            var views = p.Drawing.GetSheet().GetAllViews();
            while (views.MoveNext())
            {
                D.View view = views.Current as D.View;
                if (view == null) continue;
                var local = new List<Item>();
                var parts = view.GetAllObjects(typeof(D.Part));
                while (parts.MoveNext())
                {
                    var dp = (D.Part)parts.Current;
                    var mp = model.SelectModelObject(dp.ModelIdentifier) as M.Part;
                    if (mp == null) throw new InvalidOperationException("Khong doc duoc model part trong view.");
                    Add(p, mp);
                    local.Add(new Item { Part = dp, ModelId = mp.Identifier.ID,
                        ViewId = RuntimeId(view), WasHidden = dp.Hideable.IsHidden });
                }
                // Also process sections showing a secondary member of the primary fabrication unit.
                if (!local.Any(i => primary.Contains(i.ModelId))) continue;
                p.ViewCount++;
                p.Items.AddRange(local);
            }
            if (p.ViewCount == 0) throw new InvalidOperationException("Khong co view chua main part.");
            BuildConnections(p, primary);
            p.RestorePlates.AddRange(ResolvePlateRestores(p.Nodes, p.Edges, primary, p.Keep, p.Items));
            p.RestoreSupports.AddRange(ResolveReferenceSupportRestores(p.ReferenceSupportPlates, p.Nodes, primary, p.Items));
            foreach (var i in p.Items)
                i.Reason = p.RestorePlates.Contains(i) ? "SHOW required connector plate; " + p.Keep[i.ModelId]
                    : p.RestoreSupports.Contains(i) ? "SHOW verified reference support; " + p.Keep[i.ModelId]
                    : p.Keep.ContainsKey(i.ModelId) ? p.Keep[i.ModelId] : "HIDE outside local connection";
            return p;
        }

        public static List<Item> ResolvePlateRestores(Dictionary<int, Node> nodes, List<Edge> edges,
            HashSet<int> primary, Dictionary<int, string> keep, List<Item> items)
        {
            // Không đổi graph dùng chung với Check DIM. Cần đủ hai cạnh main-plate-neighbor,
            // đồng thời cụm main và neighbor đang hiện trong chính view có plate bị ẩn.
            var direct = new HashSet<int>();
            foreach (var e in edges)
            {
                if (primary.Contains(e.A) && !nodes[e.A].Dummy && !primary.Contains(e.B)) direct.Add(e.B);
                if (primary.Contains(e.B) && !nodes[e.B].Dummy && !primary.Contains(e.A)) direct.Add(e.A);
            }
            var receivers = new Dictionary<int, HashSet<int>>();
            foreach (int id in direct)
            {
                Node plate = nodes[id];
                if (!plate.Plate || plate.Dummy || !keep.ContainsKey(id)) continue;
                var members = new HashSet<int>();
                foreach (var e in edges.Where(e => e.A == id || e.B == id))
                {
                    int other = e.A == id ? e.B : e.A;
                    Node member = nodes[other];
                    if (!primary.Contains(other) && !member.Plate && !member.Dummy && keep.ContainsKey(other))
                        members.Add(other);
                }
                if (members.Count > 0) receivers.Add(id, members);
            }
            var result = new List<Item>();
            foreach (var view in items.GroupBy(i => i.ViewId))
            {
                var visible = new HashSet<int>(view.Where(i => !i.WasHidden).Select(i => i.ModelId));
                if (!visible.Any(id => primary.Contains(id) && !nodes[id].Dummy)) continue;
                foreach (var i in view.Where(i => i.WasHidden))
                {
                    HashSet<int> members;
                    if (receivers.TryGetValue(i.ModelId, out members) && members.Overlaps(visible)) result.Add(i);
                }
            }
            return result;
        }

        private static void BuildConnections(Plan p, HashSet<int> primary)
        {
            // Discover direct connections from ALL primary assembly parts, including parts absent in 2D.
            var edgeKeys = new HashSet<string>();
            foreach (int id in primary.Where(id => !p.Nodes[id].Dummy)) ReadEdges(p, p.Nodes[id].Part, edgeKeys, false);
            var external = p.Edges.Where(e => primary.Contains(e.A) || primary.Contains(e.B))
                .SelectMany(e => new[] { e.A, e.B }).Where(id => !primary.Contains(id) && !p.Nodes[id].Dummy).Distinct().ToList();
            var queue = new Queue<int>(external);
            var visited = new HashSet<int>();
            var assembliesRead = new HashSet<int>();
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                if (!visited.Add(id)) continue;
                if (visited.Count > 10000) throw new InvalidOperationException("Mang lien ket qua lon; dung truoc khi an.");
                Node node = p.Nodes[id];
                if (assembliesRead.Add(node.Assembly))
                    foreach (var part in AssemblyParts(node.Part.GetAssembly())) Add(p, part);
                ReadEdges(p, node.Part, edgeKeys, !node.Plate);
                foreach (var e in p.Edges.Where(e => e.A == id || e.B == id).ToList())
                {
                    int next = e.A == id ? e.B : e.A;
                    if (!primary.Contains(next) && !p.Nodes[next].Dummy && CanFollow(node, p.Nodes[next], e)) queue.Enqueue(next);
                }
            }
            p.Keep = Resolve(p.Nodes, p.Edges, primary, p.Warnings);
            RetainLocalDummies(p, primary);
            RetainConnectionReferenceSupports(p, primary);
        }

        private static void RetainConnectionReferenceSupports(Plan p, HashSet<int> primary)
        {
            // Dầm tham chiếu có thể chỉ có Connection khoan plate, không có weld tới dầm.
            // Không thêm cạnh graph: Check DIM và ranh giới mở rộng neighbor giữ nguyên.
            // Chạy sau quy tắc cũ; không thay lý do Keep hoặc trạng thái ẩn cũ của dummy đã được giữ.
            var geometry = new Dictionary<int, ReferenceShape>();
            var colors = new Dictionary<int, int>();
            G.Matrix toGlobal = null; // Chỉ đọc plane khi đã có Connection cần xử lý; cache trong lần Analyze.
            foreach (var n in p.Nodes.Values.Where(n => n.Dummy && !n.Plate && !primary.Contains(n.Id)
                && !p.Keep.ContainsKey(n.Id)))
            {
                try
                {
                    var proven = new HashSet<int>();
                    var connections = new HashSet<int>();
                    var components = n.Part.GetComponents();
                    while (components.MoveNext())
                    {
                        var connection = components.Current as M.Connection;
                        if (connection == null) continue;
                        var support = connection.GetPrimaryObject() as M.Part;
                        if (support == null || support.Identifier.ID != n.Id) continue;
                        var secondary = new HashSet<int>();
                        foreach (var obj in connection.GetSecondaryObjects())
                        {
                            var plate = obj as M.Part;
                            if (plate != null) secondary.Add(plate.Identifier.ID);
                        }
                        var plates = ResolveReferenceSupportPlates(n.Id, support.Identifier.ID, secondary,
                            p.Nodes, p.Edges, primary);
                        if (plates.Count == 0) continue;
                        proven.UnionWith(plates);
                        connections.Add(connection.Identifier.ID);
                    }
                    // Chỉ công bố kế hoạch khi toàn bộ lần đọc thành công, tránh bằng chứng dở dang.
                    if (proven.Count == 0) continue;
                    int chosen;
                    var protectedGroup = new HashSet<int> { n.Id };
                    try
                    {
                        if (toGlobal == null) toGlobal = new M.Model().GetWorkPlaneHandler()
                            .GetCurrentTransformationPlane().TransformationMatrixToGlobal;
                        chosen = ChooseReferenceSupport(p, n, proven, primary, toGlobal, geometry, colors, protectedGroup);
                    }
                    catch (Exception ex)
                    {
                        ProtectReferenceSupports(p, protectedGroup, proven,
                            "P" + n.Id + ": khong du du lieu, giu nhom reference da khoanh vung; " + ex.Message);
                        continue;
                    }
                    if (chosen == 0) continue;
                    // Không ghi đè Keep/ẩn thủ công của dummy đã được quy tắc cũ nhận.
                    if (!p.Keep.ContainsKey(chosen))
                    {
                        p.ReferenceSupportPlates.Add(chosen, proven);
                        p.Keep[chosen] = "KEEP reference support via Connection " + String.Join(",", connections.OrderBy(id => id))
                            + (chosen == n.Id ? "" : " replacing unused gray P" + n.Id)
                            + " and primary-linked plate " + String.Join(",", proven.OrderBy(id => id));
                    }
                    else if (p.ReferenceSupportPlates.ContainsKey(chosen)) p.ReferenceSupportPlates[chosen].UnionWith(proven);
                    foreach (int id in proven) p.Warnings.Remove(UnresolvedPlateWarning(id));
                }
                catch (Exception ex)
                {
                    // Lỗi nhánh bổ sung không đổi kết quả Keep của quy tắc cũ, không tự hiện part.
                    p.Warnings.Add("Reference support P" + n.Id + ": " + ex.Message);
                }
            }
        }

        // Quy ước người dùng: reference xám là chưa dùng. Chỉ dùng permanent representation,
        // không dùng màu selection/temporary; không áp Class 1/1010 hoặc màu này vào graph/part thường.
        // Ngưỡng chroma/saturation vô thứ nguyên; vùng giữa không đoán trạng thái sử dụng.
        public static int ReferenceColorRole(double red, double green, double blue)
        {
            if (new[] { red, green, blue }.Any(v => Double.IsNaN(v) || Double.IsInfinity(v) || v < 0 || v > 1)) return 0;
            double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue));
            if (max < .15 || min > .95) return 0; // Đen/trắng có thể do chế độ hiển thị, không coi là màu kỹ thuật.
            double chroma = max - min, saturation = chroma / max;
            if (chroma <= .08 && saturation <= .15) return -1;
            return chroma >= .15 && saturation >= .25 ? 1 : 0;
        }

        private static int ReadReferenceColor(Node node, Dictionary<int, int> cache)
        {
            int role;
            if (!cache.TryGetValue(node.Id, out role))
            {
                var color = new M.UI.Color();
                // Thiếu màu là mơ hồ: dùng hình học gom nhóm để giữ cả hai, không ép chọn.
                role = M.UI.ModelObjectVisualization.GetRepresentation(node.Part, ref color)
                    ? ReferenceColorRole(color.Red, color.Green, color.Blue) : 0;
                cache.Add(node.Id, role);
            }
            return role;
        }

        private static int ChooseReferenceSupport(Plan p, Node source, HashSet<int> plates, HashSet<int> primary,
            G.Matrix toGlobal, Dictionary<int, ReferenceShape> geometry, Dictionary<int, int> colors, HashSet<int> protectedGroup)
        {
            int role = ReadReferenceColor(source, colors);
            // Màu mơ hồ: vẫn tìm nhóm chồng hình học để giữ/hiện cả nhóm, không dừng batch.
            if (!(source.Part is M.Beam))
            {
                if (role == 1) return source.Id;
                throw new InvalidOperationException("Reference xam khong phai dam thang, chua ho tro thay the.");
            }
            var oldShape = ReadReferenceShape(source, toGlobal, geometry);
            var candidates = new List<int>();
            var uncertain = new List<int>();
            var peers = new List<int>();
            var plateShapes = plates.Select(id => ReadReferenceShape(p.Nodes[id], toGlobal, geometry)).ToList();
            if (role == 1) candidates.Add(source.Id);
            foreach (var other in MatchingReferencePeers(p, source, primary, plates))
            {
                try
                {
                    var shape = ReadReferenceShape(other, toGlobal, geometry);
                    if (!ReferenceAxesParallel(oldShape, shape)) continue;
                    bool fullContact = plateShapes.All(plate => SharesPlateInterface(oldShape, shape, plate));
                    // Bbox chỉ nhận vùng nghi vấn để GIỮ, tuyệt đối không dùng làm bằng chứng chọn/ẩn.
                    if (!fullContact && !plateShapes.Any(plate => ReferenceBoundsOverlap(oldShape, shape)
                        && ReferenceBoundsOverlap(oldShape, plate) && ReferenceBoundsOverlap(shape, plate))) continue;
                    peers.Add(other.Id); protectedGroup.Add(other.Id);
                    if (!fullContact) { uncertain.Add(other.Id); continue; }
                    int otherRole = ReadReferenceColor(other, colors);
                    if (otherRole == 1) candidates.Add(other.Id);
                    else if (otherRole == 0) uncertain.Add(other.Id);
                }
                catch (Exception ex)
                {
                    // Chỉ bảo vệ ứng viên đọc lỗi, không giữ toàn bộ reference cùng profile khác.
                    peers.Add(other.Id); protectedGroup.Add(other.Id); uncertain.Add(other.Id);
                    p.Warnings.Add("P" + other.Id + ": khong doc duoc ung vien reference, giu rieng; " + ex.Message);
                }
            }
            int selected = role != 0 ? UniqueReferenceReplacement(candidates, uncertain) : 0;
            if (selected == 0)
                ProtectReferenceSupports(p, peers.Concat(new[] { source.Id }), plates,
                    "P" + source.Id + ": chua phan biet chac, giu/hien ca nhom reference; ung vien mau hop le="
                    + String.Join(",", candidates.OrderBy(id => id)) + "; chua ro=" + String.Join(",", uncertain.OrderBy(id => id))
                    + ". Cac neighbor khac van xu ly.");
            return selected;
        }

        private static List<Node> MatchingReferencePeers(Plan p, Node source, HashSet<int> primary, HashSet<int> plates)
        {
            // Chỉ xét view của chính Connection đang xử lý, không kéo part từ section/mối nối khác.
            var sourceViews = new HashSet<int>(p.Items.Where(i => i.ModelId == source.Id).Select(i => i.ViewId));
            var plateViews = new HashSet<int>(p.Items.Where(i => plates.Contains(i.ModelId)).Select(i => i.ViewId));
            sourceViews.IntersectWith(plateViews);
            var drawingIds = new HashSet<int>(p.Items.Where(i => sourceViews.Contains(i.ViewId)).Select(i => i.ModelId));
            return p.Nodes.Values.Where(n => n.Id != source.Id && n.Dummy && !n.Plate && !primary.Contains(n.Id)
                && drawingIds.Contains(n.Id) && n.Part is M.Beam
                && String.Equals(source.Part.Profile.ProfileString, n.Part.Profile.ProfileString, StringComparison.OrdinalIgnoreCase)
                && String.Equals(source.Part.Material.MaterialString, n.Part.Material.MaterialString, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void ProtectReferenceSupports(Plan p, IEnumerable<int> ids, HashSet<int> plates, string reason)
        {
            foreach (int id in ids.Distinct())
            {
                // Giữ lý do Keep cũ. Riêng nhóm mơ hồ, người dùng yêu cầu hiện cả hai kể cả đã ẩn.
                if (!p.Keep.ContainsKey(id))
                {
                    p.Keep.Add(id, "KEEP ambiguous reference group; " + reason);
                }
                if (!p.ReferenceSupportPlates.ContainsKey(id)) p.ReferenceSupportPlates.Add(id, new HashSet<int>(plates));
                else p.ReferenceSupportPlates[id].UnionWith(plates);
            }
            p.Warnings.Add(reason);
        }

        public static int UniqueReferenceReplacement(IEnumerable<int> active, IEnumerable<int> uncertain)
        {
            var ids = active.Distinct().ToArray();
            return ids.Length == 1 && !uncertain.Any() ? ids[0] : 0;
        }

        public sealed class ReferenceFace
        {
            public G.Vector Normal;
            public G.Point[] Points;
        }
        public sealed class ReferenceShape
        {
            public G.Vector AxisX, AxisY;
            public G.Point[] Vertices;
            public readonly List<ReferenceFace> Faces = new List<ReferenceFace>();
        }
        // mm hình học thật, không nhân scale: mặt tiếp giáp lệch quá 0,05 mm thì không suy liên kết.
        private const double ReferenceContactTolerance = .05;
        // Cosine hướng: chỉ nhận trục song song/đảo chiều, không nhận dầm giao chéo.
        private const double ReferenceParallelCosine = .999999;

        private static ReferenceShape ReadReferenceShape(Node node, G.Matrix toGlobal, Dictionary<int, ReferenceShape> cache)
        {
            ReferenceShape shape;
            if (cache.TryGetValue(node.Id, out shape)) return shape;
            var cs = node.Part.GetCoordinateSystem();
            shape = new ReferenceShape { AxisX = GlobalDirection(toGlobal, cs.AxisX), AxisY = GlobalDirection(toGlobal, cs.AxisY) };
            var faces = node.Part.GetSolid().GetFaceEnumerator();
            var allPoints = new List<G.Point>();
            while (faces.MoveNext())
            {
                var face = faces.Current as Tekla.Structures.Solid.Face;
                if (face == null) continue;
                var loops = face.GetLoopEnumerator();
                var polygons = new List<G.Point[]>();
                while (loops.MoveNext())
                {
                    var loop = loops.Current as Tekla.Structures.Solid.Loop;
                    if (loop == null) continue;
                    var vertices = loop.GetVertexEnumerator(); var points = new List<G.Point>();
                    while (vertices.MoveNext()) points.Add(toGlobal.Transform((G.Point)vertices.Current));
                    allPoints.AddRange(points);
                    polygons.Add(points.ToArray());
                }
                // Mặt có lỗ/lõm không dùng làm bằng chứng thay thế: tránh lấp phần rỗng bằng bbox.
                if (polygons.Count != 1) continue;
                var contact = new ReferenceFace { Normal = GlobalDirection(toGlobal, face.Normal), Points = polygons[0] };
                if (ReferenceFaceIsConvex(contact)) shape.Faces.Add(contact);
            }
            if (shape.Faces.Count == 0) throw new InvalidOperationException("Khong doc duoc mat solid hop le P" + node.Id);
            shape.Vertices = allPoints.ToArray();
            cache.Add(node.Id, shape);
            return shape;
        }

        private static G.Vector GlobalDirection(G.Matrix transform, G.Vector vector)
        {
            var zero = transform.Transform(new G.Point());
            var point = transform.Transform(new G.Point(vector.X, vector.Y, vector.Z));
            var result = new G.Vector(point.X-zero.X, point.Y-zero.Y, point.Z-zero.Z);
            if (result.GetLength() < 1e-9) throw new InvalidOperationException("Huong hinh hoc khong hop le.");
            result.Normalize(); return result;
        }
        private static G.Vector Delta(G.Point a, G.Point b) { return new G.Vector(b.X-a.X,b.Y-a.Y,b.Z-a.Z); }
        private static double Dot(G.Vector a, G.Vector b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }

        public static bool ReferenceAxesParallel(ReferenceShape a, ReferenceShape b)
        {
            return Math.Abs(Dot(a.AxisX,b.AxisX)) >= ReferenceParallelCosine
                && Math.Abs(Dot(a.AxisY,b.AxisY)) >= ReferenceParallelCosine;
        }

        private static bool ReferenceFaceIsConvex(ReferenceFace face)
        {
            var points = face.Points;
            if (points == null || points.Length < 3 || points.Any(p => p == null || new[] { p.X,p.Y,p.Z }
                .Any(v => Double.IsNaN(v) || Double.IsInfinity(v)))) return false;
            int sign = 0; double area = 0;
            for (int i=0;i<points.Length;i++)
            {
                if (Math.Abs(Dot(Delta(points[0],points[i]),face.Normal)) > ReferenceContactTolerance) return false;
                double turn = Dot(G.Vector.Cross(Delta(points[i],points[(i+1)%points.Length]),
                    Delta(points[(i+1)%points.Length],points[(i+2)%points.Length])),face.Normal);
                if (Math.Abs(turn)>1e-8) { int next = Math.Sign(turn); if(sign!=0 && sign!=next)return false; sign=next; }
                if(i>0 && i+1<points.Length) area += Dot(G.Vector.Cross(Delta(points[0],points[i]),Delta(points[0],points[i+1])),face.Normal);
            }
            return sign != 0 && Math.Abs(area)*.5 > .01; // mm²; loại cạnh/điểm chạm và mặt suy biến.
        }

        public static bool ReferenceFaceCovers(ReferenceFace support, ReferenceFace plate)
        {
            if (!ReferenceFaceIsConvex(support) || !ReferenceFaceIsConvex(plate)
                || Dot(support.Normal,plate.Normal)>-ReferenceParallelCosine) return false;
            foreach (var point in plate.Points)
            {
                if (Math.Abs(Dot(Delta(support.Points[0],point),support.Normal))>ReferenceContactTolerance) return false;
                int sign=0;
                for(int i=0;i<support.Points.Length;i++)
                {
                    var edge=Delta(support.Points[i],support.Points[(i+1)%support.Points.Length]);
                    double distance=Dot(G.Vector.Cross(edge,Delta(support.Points[i],point)),support.Normal)/Math.Max(1e-9,edge.GetLength());
                    if(Math.Abs(distance)<=ReferenceContactTolerance)continue;
                    int next=Math.Sign(distance); if(sign!=0 && sign!=next)return false; sign=next;
                }
            }
            return true;
        }

        public static bool SharesPlateInterface(ReferenceShape oldSupport, ReferenceShape replacement, ReferenceShape plate)
        {
            return plate.Faces.Any(face => oldSupport.Faces.Any(oldFace => ReferenceFaceCovers(oldFace,face))
                && replacement.Faces.Any(newFace => ReferenceFaceCovers(newFace,face)));
        }

        public static bool ReferenceBoundsOverlap(ReferenceShape a, ReferenceShape b)
        {
            var av = a.Vertices ?? a.Faces.SelectMany(f => f.Points).ToArray();
            var bv = b.Vertices ?? b.Faces.SelectMany(f => f.Points).ToArray();
            if (av.Length == 0 || bv.Length == 0) return false;
            return av.Min(p=>p.X)<=bv.Max(p=>p.X)+ReferenceContactTolerance && bv.Min(p=>p.X)<=av.Max(p=>p.X)+ReferenceContactTolerance
                && av.Min(p=>p.Y)<=bv.Max(p=>p.Y)+ReferenceContactTolerance && bv.Min(p=>p.Y)<=av.Max(p=>p.Y)+ReferenceContactTolerance
                && av.Min(p=>p.Z)<=bv.Max(p=>p.Z)+ReferenceContactTolerance && bv.Min(p=>p.Z)<=av.Max(p=>p.Z)+ReferenceContactTolerance;
        }

        public static HashSet<int> ResolveReferenceSupportPlates(int candidate, int connectionPrimary,
            HashSet<int> secondary, Dictionary<int, Node> nodes, List<Edge> edges, HashSet<int> primary)
        {
            var result = new HashSet<int>();
            Node support;
            if (candidate != connectionPrimary || primary.Contains(candidate)
                || !nodes.TryGetValue(candidate, out support) || !support.Dummy || support.Plate) return result;
            foreach (int id in secondary)
            {
                Node plate;
                if (id == candidate || !nodes.TryGetValue(id, out plate) || !plate.Plate || plate.Dummy) continue;
                // Plate thuộc primary vẫn cần cạnh thật tới part primary khác, không suy từ assembly.
                if (edges.Any(e => (e.A == id && e.B != id && primary.Contains(e.B) && !nodes[e.B].Dummy)
                    || (e.B == id && e.A != id && primary.Contains(e.A) && !nodes[e.A].Dummy))) result.Add(id);
            }
            return result;
        }

        public static List<Item> ResolveReferenceSupportRestores(Dictionary<int, HashSet<int>> supports,
            Dictionary<int, Node> nodes, HashSet<int> primary, List<Item> items)
        {
            var result = new List<Item>();
            foreach (var view in items.GroupBy(i => i.ViewId))
            {
                var visible = new HashSet<int>(view.Where(i => !i.WasHidden).Select(i => i.ModelId));
                if (!visible.Any(id => primary.Contains(id) && !nodes[id].Dummy)) continue;
                foreach (var i in view.Where(i => i.WasHidden))
                {
                    HashSet<int> plates;
                    if (supports.TryGetValue(i.ModelId, out plates) && plates.Overlaps(visible)) result.Add(i);
                }
            }
            return result;
        }

        private static void RetainLocalDummies(Plan p, HashSet<int> primary)
        {
            var cache = new Dictionary<int, HashSet<int>>();
            foreach (var n in p.Nodes.Values.Where(n => n.Dummy && !p.Keep.ContainsKey(n.Id)))
            {
                try
                {
                    var father = n.Part.GetFatherComponent();
                    if (father == null)
                    {
                        // Imported/reference parts may have a DUMMY mark but no generating component.
                        // Check actual component participation and direct bolt/weld evidence instead.
                        var components = n.Part.GetComponents();
                        while (components.MoveNext())
                        {
                            var c = components.Current as M.BaseComponent;
                            if (c == null) continue;
                            HashSet<int> ids;
                            if (!cache.TryGetValue(c.Identifier.ID, out ids))
                            { ids = ReadComponentMembers(c); cache.Add(c.Identifier.ID, ids); }
                            if (ids.Contains(n.Id) && ComponentTouchesPrimaryConnection(ids, p.Edges, primary, p.Nodes))
                                p.Keep[n.Id] = "KEEP dummy participates in primary connection component " + c.Identifier.ID;
                        }
                        continue;
                    }
                    HashSet<int> members;
                    if (!cache.TryGetValue(father.Identifier.ID, out members))
                    {
                        members = ReadComponentMembers(father);
                        cache.Add(father.Identifier.ID, members);
                    }
                    if (ComponentTouchesPrimaryConnection(members, p.Edges, primary, p.Nodes))
                        p.Keep[n.Id] = "KEEP dummy owned by primary connection component " + father.Identifier.ID;
                }
                catch (Exception ex)
                {
                    p.Keep[n.Id] = "KEEP unresolved dummy component";
                    p.Warnings.Add("Dummy P"+n.Id+": "+ex.Message);
                }
            }
        }

        public static bool ComponentTouchesPrimaryConnection(HashSet<int> members, List<Edge> edges, HashSet<int> primary,
            Dictionary<int, Node> nodes = null)
        {
            if (edges.Any(e => primary.Contains(e.A) != primary.Contains(e.B)
                && members.Contains(e.A) && members.Contains(e.B))) return true;
            if (nodes == null) return false;
            // A splice component often exposes only its two member inputs and the dummy.
            // The eight plates may be owned by a different component; prove the actual
            // two-edge connection through a plate, never proximity or whole-assembly reachability.
            foreach (int root in members.Where(primary.Contains))
                foreach (var first in edges.Where(e => e.A == root || e.B == root))
                {
                    int bridge = first.A == root ? first.B : first.A;
                    Node plate;
                    if (!nodes.TryGetValue(bridge, out plate) || !plate.Plate || plate.Dummy) continue;
                    if (edges.Any(e => (e.A == bridge && members.Contains(e.B) && !primary.Contains(e.B))
                        || (e.B == bridge && members.Contains(e.A) && !primary.Contains(e.A)))) return true;
                }
            return false;
        }
        private static HashSet<int> ReadComponentMembers(M.BaseComponent component)
        {
            var result = new HashSet<int>();
            var connection = component as M.Connection;
            var seam = component as M.Seam;
            if (connection != null)
            {
                AddComponentData(result, connection.GetPrimaryObject());
                AddComponentData(result, connection.GetSecondaryObjects());
            }
            else if (seam != null)
            {
                AddComponentData(result, seam.GetPrimaryObject());
                AddComponentData(result, seam.GetSecondaryObjects());
            }
            var custom = component as M.Component;
            if (custom != null)
                foreach (M.InputItem input in custom.GetComponentInput())
                    if (input.GetInputType() == M.InputItem.InputTypeEnum.INPUT_1_OBJECT
                        || input.GetInputType() == M.InputItem.InputTypeEnum.INPUT_N_OBJECTS)
                        AddComponentData(result, input.GetData());
            var children = component.GetChildren();
            while (children.MoveNext())
            {
                var child = children.Current;
                if (child is M.Part) AddComponentData(result, child);
                var bolt = child as M.BoltGroup;
                if (bolt != null)
                {
                    AddComponentData(result, bolt.PartToBoltTo); AddComponentData(result, bolt.PartToBeBolted);
                    AddComponentData(result, bolt.OtherPartsToBolt);
                }
                var weld = child as M.BaseWeld;
                if (weld != null) { AddComponentData(result, weld.MainObject); AddComponentData(result, weld.SecondaryObject); }
            }
            return result;
        }
        private static void AddComponentData(HashSet<int> result, object value)
        {
            var obj = value as M.ModelObject;
            if (obj != null) { result.Add(obj.Identifier.ID); return; }
            var id = value as Tekla.Structures.Identifier;
            if (id != null) { result.Add(id.ID); return; }
            var items = value as IEnumerable;
            if (items != null) foreach (object item in items) AddComponentData(result, item);
        }

        private static int RuntimeId(object value)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            var property = value.GetType().GetProperty("Identifier", flags);
            var id = property == null ? null : property.GetValue(value, null) as Tekla.Structures.Identifier;
            if (id == null || id.ID == 0) throw new InvalidOperationException("Khong doc duoc drawing/view ID.");
            return id.ID;
        }

        private static List<M.Part> AssemblyParts(M.Assembly a)
        {
            if (a == null) throw new InvalidOperationException("Khong doc duoc assembly.");
            var result = new List<M.Part>();
            ReadAssemblyTree(a, result, new HashSet<int>());
            return result;
        }
        private static void ReadAssemblyTree(M.Assembly a, List<M.Part> result, HashSet<int> seen)
        {
            if (a == null || !seen.Add(a.Identifier.ID)) throw new InvalidOperationException("Assembly tree khong hop le.");
            if (seen.Count > 1000) throw new InvalidOperationException("Assembly tree qua lon.");
            var main = a.GetMainPart() as M.Part;
            if (main == null) throw new InvalidOperationException("Assembly khong co main part.");
            result.Add(main);
            var secondaries = a.GetSecondaries();
            if (secondaries.Count >= 2048)
                throw new InvalidOperationException("Assembly qua lon de xac minh day du.");
            foreach (object value in secondaries)
            {
                var part = value as M.Part;
                if (part != null) result.Add(part);
            }
            foreach (M.Assembly child in a.GetSubAssemblies()) ReadAssemblyTree(child, result, seen);
        }

        public static bool IsProtectedReference(string name, string material, string position, string assemblyPosition)
        {
            string metadata = String.Join(" ", name, material, position, assemblyPosition).ToUpperInvariant();
            if (metadata.Contains("DUMMY") || metadata.Contains("JOINT") || metadata.Contains("JOYCON")) return true;
            // Project joint placeholders can be unnumbered; do not depend on their changing mark number.
            return System.Text.RegularExpressions.Regex.IsMatch((name ?? "").Trim(),
                @"^(BJ|PJ|HJ|VJ|BS)\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private static void Add(Plan p, M.Part part)
        {
            int id = part.Identifier.ID;
            if (p.Nodes.ContainsKey(id)) return;
            string profile = part.Profile.ProfileString ?? "";
            var a = part.GetAssembly();
            if (a == null) throw new InvalidOperationException("Thieu assembly P" + id);
            string position = "", assemblyPosition = "";
            part.GetReportProperty("PART_POS", ref position);
            part.GetReportProperty("ASSEMBLY_POS", ref assemblyPosition);
            p.Nodes.Add(id, new Node { Id = id, Assembly = a.Identifier.ID, Part = part,
                Dummy = IsProtectedReference(part.Name, part.Material.MaterialString, position, assemblyPosition),
                Plate = part is M.ContourPlate || profile.StartsWith("PL", StringComparison.OrdinalIgnoreCase),
                Label = part.Name + " " + profile + " pos=" + position });
        }
        private static void Link(Plan p, M.Part a, M.Part b, bool weld, string evidence, HashSet<string> keys)
        {
            if (a == null || b == null) throw new InvalidOperationException("Lien ket co endpoint khong phai part.");
            Add(p, a); Add(p, b);
            int x = a.Identifier.ID, y = b.Identifier.ID;
            if (x == y) return; // self bolt group = holes, not an attachment
            string key = evidence + ":" + Math.Min(x, y) + ":" + Math.Max(x, y);
            if (keys.Add(key))
            {
                var edge = new Edge { A = x, B = y, Weld = weld, Evidence = evidence };
                p.Edges.Add(edge);
            }
        }
        private static void ReadEdges(Plan p, M.Part part, HashSet<string> keys, bool weldOnly)
        {
            if (!weldOnly)
            {
                var bolts = part.GetBolts();
                while (bolts.MoveNext())
                {
                    var b = bolts.Current as M.BoltGroup;
                    if (b == null) continue;
                    var members = new List<M.Part> { b.PartToBeBolted, b.PartToBoltTo };
                    foreach (M.Part other in b.OtherPartsToBolt) members.Add(other);
                    for (int i = 0; i < members.Count; i++)
                        for (int j = i + 1; j < members.Count; j++)
                            Link(p, members[i], members[j], false, "bolt " + b.Identifier.ID, keys);
                }
            }
            var welds = part.GetWelds();
            while (welds.MoveNext())
            {
                var w = welds.Current as M.BaseWeld;
                if (w != null)
                {
                    Link(p, w.MainObject as M.Part, w.SecondaryObject as M.Part,
                        true, "weld " + w.Identifier.ID, keys);
                }
            }
        }
    }
}
