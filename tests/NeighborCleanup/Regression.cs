using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using N = Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;

internal static class Regression
{
    sealed class Case
    {
        internal Dictionary<int, N.Node> Nodes = new Dictionary<int, N.Node>();
        internal List<N.Edge> Edges = new List<N.Edge>();
        internal HashSet<int> Primary = new HashSet<int> { 1, 2 };
        internal List<N.Item> Items = new List<N.Item>();
        internal Case()
        {
            foreach (int id in new[] { 1, 2, 3, 4, 5, 6, 7 })
                Nodes.Add(id, new N.Node { Id = id, Plate = id == 2 || id == 3 || id == 5 || id == 7,
                    Assembly = id <= 2 ? 10 : 20, Label = "renamable" });
            Edges.Add(new N.Edge { A = 1, B = 3, Evidence = "bolt" });
            Edges.Add(new N.Edge { A = 3, B = 4, Weld = true, Evidence = "weld" });
            // Plate 5 trên cùng neighbor nối sang thanh 6 khác: vẫn phải ẩn.
            Edges.Add(new N.Edge { A = 4, B = 5, Weld = true, Evidence = "weld" });
            Edges.Add(new N.Edge { A = 5, B = 6, Evidence = "bolt" });
            foreach (int view in new[] { 10, 20 })
                foreach (int id in Nodes.Keys)
                    Items.Add(new N.Item { ModelId = id, ViewId = view, WasHidden = id == 2 || id == 5 || (id == 3 && view == 10) });
        }
        internal Dictionary<int, string> Keep()
        { return N.Resolve(Nodes, Edges, Primary, new List<string>()); }
        internal N.Item Item(int id, int view = 10)
        { return Items.Single(i => i.ModelId == id && i.ViewId == view); }
    }
    static int count;
    static void CheckSupport(string name, Action<Case> change, int connectionPrimary, int[] secondary, int[] expected)
    {
        var c = new Case(); c.Nodes[4].Dummy = true; c.Edges.RemoveAt(1);
        change(c);
        var actual = N.ResolveReferenceSupportPlates(4, connectionPrimary, new HashSet<int>(secondary), c.Nodes, c.Edges, c.Primary);
        if (!actual.SetEquals(expected)) throw new Exception("Reference support: " + name);
        Console.WriteLine("PASS " + name); count++;
    }
    static void CheckSupportRestore(string name, Action<Case> change, params string[] expected)
    {
        var c = new Case(); c.Nodes[4].Dummy = true;
        c.Item(3).WasHidden = false; c.Item(4).WasHidden = true; c.Item(4,20).WasHidden = true;
        change(c);
        var actual = N.ResolveReferenceSupportRestores(new Dictionary<int, HashSet<int>> { { 4, new HashSet<int> { 3 } } },
            c.Nodes, c.Primary, c.Items).Select(i => i.ViewId + ":" + i.ModelId).OrderBy(s => s);
        if (!actual.SequenceEqual(expected.OrderBy(s => s))) throw new Exception("Reference restore: " + name);
        Console.WriteLine("PASS " + name); count++;
    }
    static void Check(string name, Action<Case> change, params string[] expected)
    {
        var c = new Case(); change(c);
        var keep = c.Keep();
        string before = String.Join(";", keep.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value));
        var actual = N.ResolvePlateRestores(c.Nodes, c.Edges, c.Primary, keep, c.Items)
            .Select(i => i.ViewId + ":" + i.ModelId).OrderBy(s => s).ToArray();
        if (!actual.SequenceEqual(expected.OrderBy(s => s)))
            throw new Exception(name + " expected=" + String.Join(",", expected) + " actual=" + String.Join(",", actual));
        if (before != String.Join(";", keep.OrderBy(k => k.Key).Select(k => k.Key + ":" + k.Value)))
            throw new Exception("Restore planning changed shared graph: " + name);
        Console.WriteLine("PASS " + name); count++;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        Check("Front restores connector; Top already visible; unrelated plates stay hidden", c => {}, "10:3");
        Check("Second run is idempotent", c => c.Item(3).WasHidden = false);
        Check("No hide targets still restores", c => c.Items.RemoveAll(i => i.ModelId >= 5), "10:3");
        Check("Restore per view", c => c.Item(3,20).WasHidden = true, "10:3", "20:3");
        Check("Receiver hidden only in Front", c => c.Item(4).WasHidden = true);
        Check("Receiver absent from Front", c => c.Items.Remove(c.Item(4)));
        Check("Main hidden in Front", c => c.Item(1).WasHidden = true);
        Check("Main absent from Front", c => c.Items.Remove(c.Item(1)));
        Check("Visible primary secondary supports section", c => { c.Item(1).WasHidden = true; c.Item(2).WasHidden = false; }, "10:3");
        Check("Dummy plate is not restored", c => c.Nodes[3].Dummy = true);
        Check("Dummy receiver is not evidence", c => c.Nodes[4].Dummy = true);
        Check("Dummy root is not evidence", c => c.Nodes[1].Dummy = true);
        Check("Primary hidden plate remains hidden", c => { c.Primary.Add(3); });
        Check("No receiver edge", c => c.Edges.RemoveAt(1));
        Check("No direct primary edge, fallback keep cannot restore", c => c.Edges.RemoveAt(0));
        Check("No graph evidence, fallback keep cannot restore", c => c.Edges.Clear());
        Check("Plate receiver is not structural member", c => c.Nodes[4].Plate = true);
        Check("Nonplate seed is not restored", c => c.Nodes[3].Plate = false);
        Check("Shared assembly alone is insufficient", c => { c.Nodes[7].Assembly = 10; c.Item(7).WasHidden = true; }, "10:3");
        Check("Renamed marks and assemblies do not control evidence", c => {
            foreach (var n in c.Nodes.Values) { n.Label = Guid.NewGuid().ToString(); n.Assembly += 800; }
        }, "10:3");
        Check("Primary secondary can own direct edge", c => c.Edges[0].A = 2, "10:3");
        Check("Reversed endpoints and shuffled enumeration", c => {
            foreach (var e in c.Edges) { int a = e.A; e.A = e.B; e.B = a; }
            c.Edges.Reverse(); c.Items.Reverse();
        }, "10:3");
        Check("Multiple actual connector plates", c => {
            c.Edges.Add(new N.Edge { A = 1, B = 7 }); c.Edges.Add(new N.Edge { A = 7, B = 4 });
            c.Item(7).WasHidden = true;
        }, "10:3", "10:7");
        var baseline = new Case(); var graph = new N.Plan();
        foreach (var n in baseline.Nodes) graph.Nodes.Add(n.Key,n.Value);
        graph.Edges.AddRange(baseline.Edges); graph.Keep = baseline.Keep();
        var connected = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.ResolveConnected(graph, baseline.Primary);
        if (!connected.SetEquals(new[] { 3, 4 }) || !graph.Keep.Keys.OrderBy(id => id).SequenceEqual(new[] { 1, 2, 3, 4 }))
            throw new Exception("Legacy neighbor/Check DIM relationship changed");
        Console.WriteLine("PASS shared Check DIM graph and stop-at-member rule");
        CheckSupport("Native Connection support with bolted plate and no support weld", c => {}, 4, new[] { 3 }, new[] { 3 });
        CheckSupport("Wrong Connection primary does not prove support", c => {}, 6, new[] { 3 }, new int[0]);
        CheckSupport("Plate in children but not secondary input is insufficient", c => {}, 4, new int[0], new int[0]);
        CheckSupport("Secondary member is not interface plate", c => {}, 4, new[] { 6 }, new int[0]);
        CheckSupport("Unrelated secondary plate", c => {}, 4, new[] { 5 }, new int[0]);
        CheckSupport("Dummy secondary plate", c => c.Nodes[3].Dummy = true, 4, new[] { 3 }, new int[0]);
        CheckSupport("No actual primary bolt or weld", c => c.Edges.Clear(), 4, new[] { 3 }, new int[0]);
        CheckSupport("Self holes are not attachment", c => { c.Edges.Clear(); c.Edges.Add(new N.Edge { A = 3, B = 3 }); }, 4, new[] { 3 }, new int[0]);
        CheckSupport("Dummy main cannot prove support", c => c.Nodes[1].Dummy = true, 4, new[] { 3 }, new int[0]);
        CheckSupport("Primary-owned interface needs real weld", c => c.Primary.Add(3), 4, new[] { 3 }, new[] { 3 });
        CheckSupport("Primary assembly membership alone cannot prove support", c => { c.Primary.Add(3); c.Edges.Clear(); }, 4, new[] { 3 }, new int[0]);
        CheckSupport("Scope excludes nonreference members", c => c.Nodes[4].Dummy = false, 4, new[] { 3 }, new int[0]);
        CheckSupport("Scope excludes primary members", c => c.Primary.Add(4), 4, new[] { 3 }, new int[0]);
        CheckSupport("Scope excludes dummy plates as support", c => c.Nodes[4].Plate = true, 4, new[] { 3 }, new int[0]);
        CheckSupport("Ignore unresolved component IDs", c => {}, 4, new[] { 999, 3 }, new[] { 3 });
        CheckSupport("Shuffled edges and changing label do not affect support", c => {
            c.Edges[0].A = 3; c.Edges[0].B = 1; c.Edges.Reverse(); c.Nodes[4].Label = "other mark";
        }, 4, new[] { 5, 3 }, new[] { 3 });
        CheckSupportRestore("Restore proven support in Top and Front", c => {}, "10:4", "20:4");
        CheckSupportRestore("Already visible support remains unchanged", c => { c.Item(4).WasHidden = false; c.Item(4,20).WasHidden = false; });
        CheckSupportRestore("Hidden plate prevents support restore in that view", c => c.Item(3).WasHidden = true, "20:4");
        CheckSupportRestore("Absent plate prevents support restore in that view", c => c.Items.Remove(c.Item(3)), "20:4");
        CheckSupportRestore("Hidden main prevents support restore in that view", c => c.Item(1).WasHidden = true, "20:4");
        CheckSupportRestore("Unrelated hidden dummy stays hidden", c => { c.Nodes[6].Dummy = true; c.Item(6).WasHidden = true; }, "10:4", "20:4");
        var reference = new Case(); reference.Nodes[4].Dummy = true; reference.Edges.RemoveAt(1);
        var refGraph = new N.Plan(); foreach (var n in reference.Nodes) refGraph.Nodes.Add(n.Key,n.Value);
        refGraph.Edges.AddRange(reference.Edges); refGraph.Keep = reference.Keep();
        var oldCheck = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.ResolveConnected(refGraph, reference.Primary);
        var oldEdges = refGraph.Edges.ToArray();
        var proof = N.ResolveReferenceSupportPlates(4,4,new HashSet<int> { 3 },refGraph.Nodes,refGraph.Edges,reference.Primary);
        if (!proof.SetEquals(new[] { 3 })) throw new Exception("Missing native input proof");
        refGraph.Keep[4] = "KEEP reference support via Connection";
        var newCheck = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.ResolveConnected(refGraph, reference.Primary);
        if (!oldCheck.SetEquals(newCheck) || !refGraph.Edges.SequenceEqual(oldEdges) || refGraph.Keep.ContainsKey(5) || refGraph.Keep.ContainsKey(6))
            throw new Exception("Reference support widened shared graph or neighbor expansion");
        Console.WriteLine("PASS reference keep leaves Check DIM graph and unrelated connections unchanged"); count++;
        Console.WriteLine("PASS " + (count + 1) + " offline regressions; no Tekla connection or mutation.");
    }
}
