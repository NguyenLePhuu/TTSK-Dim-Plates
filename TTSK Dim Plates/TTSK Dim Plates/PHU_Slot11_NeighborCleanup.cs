using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using M = Tekla.Structures.Model;
using D = Tekla.Structures.Drawing;

namespace Tekla.Technology.Akit.UserScript
{
    public static class PHU_AutoDimSlot11
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
                LastRunMessage = "Neighbor: an " + count + " part trong " + plan.ViewCount
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
                return s.ToString();
            }
            public int Apply()
            {
                var handler = new D.DrawingHandler();
                var active = handler.GetActiveDrawing();
                if (active == null || !active.IsSameDatabaseObject(Drawing))
                    throw new InvalidOperationException("Ban ve active da thay doi.");
                // Revalidate every target before the first write. Pre-existing hidden states are untouched.
                var targets = Items.Where(i => !i.WasHidden && !Keep.ContainsKey(i.ModelId)).ToList();
                if (targets.Count == 0) return 0;
                var changed = new List<Item>();
                try
                {
                    changed.AddRange(targets);
                    RunBatchMacro(Drawing, targets);
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
                    if (changed.Count > 0 && !Drawing.CommitChanges())
                        throw new InvalidOperationException("Commit drawing that bai.");
                    return changed.Count;
                }
                catch (BatchPendingException) { throw; } // Never race a possibly running native command with rollback.
                catch (BatchPreflightException) { throw; }
                catch
                {
                    bool restored = true;
                    foreach (var i in changed)
                    {
                        try
                        {
                            i.Part.Hideable.ShowInDrawingView();
                            restored &= i.Part.Modify() && i.Part.Select() && !i.Part.Hideable.IsHidden;
                        }
                        catch { restored = false; }
                    }
                    try { if (changed.Count > 0) restored &= Drawing.CommitChanges(); }
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
                    warnings.Add("P" + seed + ": chua xac minh thep hinh noi truc tiep; chi giu plate da co bang chung.");
                }
            }
            if (seeds.Count == 0)
            {
                warnings.Add("Khong co bang chung lien ket ngoai; giu cac doi tuong de tranh an nham.");
                foreach (var n in nodes.Values) if (!keep.ContainsKey(n.Id)) keep[n.Id] = "KEEP insufficient connection evidence";
            }
            return keep;
        }

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
                throw new InvalidOperationException("Slot 11 hien ho tro Assembly Drawing.");
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
            foreach (var i in p.Items)
                i.Reason = p.Keep.ContainsKey(i.ModelId) ? p.Keep[i.ModelId] : "HIDE outside local connection";
            return p;
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
