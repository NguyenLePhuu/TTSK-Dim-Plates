using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using N = Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;
using Old = NeighborLegacy.PHU_NeighborCleanup;
using Check = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;

// So sánh bản trước thay đổi với sản phẩm hiện tại; không có đường gọi Apply/Run.
internal static class ImpactReview
{
    static int Main(string[] args)
    {
        try
        {
            string product = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
                string name = new AssemblyName(e.Name).Name;
                if (name == "TTSK Dim Plates") return Assembly.LoadFrom(product);
                foreach (string dir in new[] { System.IO.Path.GetDirectoryName(product), args[1] })
                {
                    string path = System.IO.Path.Combine(dir, name + ".dll");
                    if (System.IO.File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            var assembly = Assembly.LoadFrom(product);
            if (args[2] == "offline") { Offline(); return 0; }
            Environment.SetEnvironmentVariable("TTSK_SESSION_PID", args[2]);
            Environment.SetEnvironmentVariable("TTSK_SESSION_START", args[3]);
            var session = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionCoordinator");
            foreach (string method in new[] { "Prepare", "VerifyConnection" })
                session.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (args.Length>4 && args[4]=="dimcheck")
            {
                // Reflection để chạy cùng entry point chỉ đọc trên hai EXE độc lập.
                var result=assembly.GetType("Tekla.Technology.Akit.UserScript.PHU_DimensionCheck")
                    .GetMethod("Analyze",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
                Console.WriteLine(result.GetType().GetMethod("Report").Invoke(result,null));
                PrintFeatures(result);
                return 0;
            }
            Live(); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static string Number(object value)
    { return Convert.ToDouble(value).ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
    static string FeatureValue(object value)
    {
        if (value==null) return "null";
        var type=value.GetType();
        if (type.FullName=="Tekla.Structures.Geometry3d.Point")
            return String.Join(",",new[] { "X","Y","Z" }.Select(name=>Number(type.GetField(name)!=null
                ? type.GetField(name).GetValue(value) : type.GetProperty(name).GetValue(value,null))));
        return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
    }
    static void PrintFeatures(object result)
    {
        var rows=new List<string>();
        var features=(System.Collections.IDictionary)result.GetType().GetField("Features").GetValue(result);
        foreach(System.Collections.DictionaryEntry view in features)
            foreach(object feature in (System.Collections.IEnumerable)view.Value)
                rows.Add("FEATURE view="+view.Key+" "+String.Join(";",feature.GetType().GetFields()
                    .OrderBy(f=>f.Name).Select(f=>f.Name+"="+FeatureValue(f.GetValue(feature)))));
        foreach(string row in rows.OrderBy(s=>s,StringComparer.Ordinal))Console.WriteLine(row);
        Console.WriteLine("FEATURE_COUNT="+rows.Count);
    }
    static string Keep(Dictionary<int,string> keep)
    { return String.Join(";", keep.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value)); }
    static Dictionary<int,Old.Node> OldNodes(Dictionary<int,N.Node> nodes)
    { return nodes.ToDictionary(p=>p.Key,p=>new Old.Node { Id=p.Value.Id, Assembly=p.Value.Assembly, Plate=p.Value.Plate, Dummy=p.Value.Dummy, Label=p.Value.Label }); }
    static List<Old.Edge> OldEdges(List<N.Edge> edges)
    { return edges.Select(e=>new Old.Edge { A=e.A,B=e.B,Weld=e.Weld,Evidence=e.Evidence }).ToList(); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Offline()
    {
        var random = new Random(802034);
        for (int test=0; test<2000; test++)
        {
            var nodes=new Dictionary<int,N.Node>(); var edges=new List<N.Edge>();
            for(int id=1;id<=12;id++) nodes.Add(id,new N.Node { Id=id, Assembly=random.Next(1,5), Plate=random.Next(2)==0,
                Dummy=random.Next(5)==0,Label="mark-"+random.Next(10000) });
            var primary=new HashSet<int> { 1,2,3 };
            for(int a=1;a<=12;a++) for(int b=a+1;b<=12;b++) if(random.Next(5)==0)
                edges.Add(new N.Edge { A=a,B=b,Weld=random.Next(2)==0,Evidence="edge-"+a+"-"+b });
            var warnings=new List<string>(); var oldWarnings=new List<string>();
            var current=N.Resolve(nodes,edges,primary,warnings);
            var legacy=Old.Resolve(OldNodes(nodes),OldEdges(edges),primary,oldWarnings);
            Require(Keep(current)==Keep(legacy) && warnings.SequenceEqual(oldWarnings),"Legacy Resolve changed: "+test);
            var component=new HashSet<int>(nodes.Keys.Where(id=>random.Next(2)==0));
            Require(N.ComponentTouchesPrimaryConnection(component,edges,primary,nodes)==
                Old.ComponentTouchesPrimaryConnection(component,OldEdges(edges),primary,OldNodes(nodes)),"Legacy component test changed: "+test);
            var graph=new N.Plan(); foreach(var pair in nodes)graph.Nodes.Add(pair.Key,pair.Value);
            graph.Edges.AddRange(edges); graph.Keep=current;
            var connected=Check.ResolveConnected(graph,primary);
            foreach(var n in nodes.Values.Where(n=>n.Dummy && !n.Plate && !primary.Contains(n.Id)))
            {
                var plates=N.ResolveReferenceSupportPlates(n.Id,n.Id,component,nodes,edges,primary);
                if(plates.Count>0 && !graph.Keep.ContainsKey(n.Id))graph.Keep[n.Id]="KEEP reference support";
            }
            Require(connected.SetEquals(Check.ResolveConnected(graph,primary)),"Check DIM relation changed: "+test);
            Require(primary.SetEquals(graph.Keep.Where(p=>p.Value=="KEEP primary assembly").Select(p=>p.Key)),"Primary membership changed");
        }
        Console.WriteLine("PASS 2000 differential graphs: old/new Resolve, warnings, component rules, Check DIM membership");
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        Require((string)typeof(N).GetField("BatchMacroSource",flags).GetRawConstantValue()==
            (string)typeof(Old).GetField("BatchMacroSource",flags).GetRawConstantValue(),"Native batch macro changed");
        foreach(string word in new[] { "DUMMY-17", "JOINT", "JOYCON", "BJ12z", "HJ1z", "BEAM", "" })
            Require(N.IsProtectedReference(word,"","","")==Old.IsProtectedReference(word,"","",""),"Dummy classification changed");
        Console.WriteLine("PASS native hide macro and legacy reference classification unchanged");
        var retain=typeof(N).GetMethod("RetainConnectionReferenceSupports",flags);
        var failed=new N.Plan { Keep=new Dictionary<int,string> { { 1,"KEEP primary assembly" } } };
        failed.Nodes.Add(1,new N.Node { Id=1 }); failed.Nodes.Add(4,new N.Node { Id=4,Dummy=true,Part=null });
        retain.Invoke(null,new object[] { failed,new HashSet<int> { 1 } });
        Require(!failed.Keep.ContainsKey(4) && failed.ReferenceSupportPlates.Count==0 && failed.Warnings.Count==1,
            "Extra component read failure changed legacy hide result");
        Console.WriteLine("PASS failed supplemental API read preserves old Keep and publishes no restore proof");
        failed.Keep[4]="KEEP dummy owned by primary connection component 99"; failed.Warnings.Clear();
        retain.Invoke(null,new object[] { failed,new HashSet<int> { 1 } });
        Require(failed.Warnings.Count==0 && failed.ReferenceSupportPlates.Count==0
            && failed.Keep[4]=="KEEP dummy owned by primary connection component 99","Old kept dummy re-read or overwritten");
        Console.WriteLine("PASS previously kept dummy is not re-read, overwritten, or added to restore plan");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Live()
    {
        Console.WriteLine("IMPACT REVIEW - READ ONLY - current drawing stays open");
        var time=System.Diagnostics.Stopwatch.StartNew(); var old=Old.Analyze(); long oldMs=time.ElapsedMilliseconds;
        time.Restart(); var current=N.Analyze(); long newMs=time.ElapsedMilliseconds;
        Require(old.Drawing.IsSameDatabaseObject(current.Drawing),"Active drawing changed");
        Console.WriteLine("DRAWING="+current.Drawing.Name+" "+current.Drawing.Mark+" main="+current.MainId);
        Require(old.MainId==current.MainId && old.ViewCount==current.ViewCount,"Target selection changed");
        Require(Keep(OldNodes(current.Nodes).ToDictionary(p=>p.Key,p=>p.Value.Assembly+"/"+p.Value.Plate+"/"+p.Value.Dummy+"/"+p.Value.Label))==
            Keep(old.Nodes.ToDictionary(p=>p.Key,p=>p.Value.Assembly+"/"+p.Value.Plate+"/"+p.Value.Dummy+"/"+p.Value.Label)),"Node identity/classification changed");
        Func<int,int,bool,string,string> edge=(a,b,weld,evidence)=>a+":"+b+":"+weld+":"+evidence;
        Require(old.Edges.Select(e=>edge(e.A,e.B,e.Weld,e.Evidence)).OrderBy(s=>s).SequenceEqual(
            current.Edges.Select(e=>edge(e.A,e.B,e.Weld,e.Evidence)).OrderBy(s=>s)),"Shared edge graph changed");
        var oldItems=old.Items.ToDictionary(i=>i.ViewId+":"+i.ModelId);
        Require(oldItems.Keys.OrderBy(s=>s).SequenceEqual(current.Items.Select(i=>i.ViewId+":"+i.ModelId).OrderBy(s=>s)),"View target set changed");
        foreach(var i in current.Items) Require(oldItems[i.ViewId+":"+i.ModelId].WasHidden==i.WasHidden,"Audit changed visibility");
        foreach(var pair in old.Keep)Require(current.Keep.ContainsKey(pair.Key) && current.Keep[pair.Key]==pair.Value,"Old Keep entry changed: "+pair.Key);
        foreach(var added in current.Keep.Keys.Except(old.Keep.Keys))Require(current.ReferenceSupportPlates.ContainsKey(added),"Unproven new Keep");
        var primary=new HashSet<int>(old.Keep.Where(p=>p.Value=="KEEP primary assembly").Select(p=>p.Key));
        var projected=new N.Plan { Keep=old.Keep };
        foreach(var pair in old.Nodes)projected.Nodes.Add(pair.Key,new N.Node { Id=pair.Value.Id,Assembly=pair.Value.Assembly,
            Plate=pair.Value.Plate,Dummy=pair.Value.Dummy,Label=pair.Value.Label });
        projected.Edges.AddRange(old.Edges.Select(e=>new N.Edge { A=e.A,B=e.B,Weld=e.Weld,Evidence=e.Evidence }));
        Require(Check.ResolveConnected(projected,primary).SetEquals(Check.ResolveConnected(current,primary)),"Live Check DIM membership changed");
        Console.WriteLine("PASS old Keep reasons preserved; Nodes/Edges/primary/dummy/Check DIM relations unchanged");
        foreach(var i in current.Items)
        {
            bool oldHide=!i.WasHidden && !old.Keep.ContainsKey(i.ModelId), newHide=!i.WasHidden && !current.Keep.ContainsKey(i.ModelId);
            if(oldHide!=newHide)Console.WriteLine("DELTA view="+i.ViewId+" P"+i.ModelId+" oldHide="+oldHide+" newHide="+newHide);
        }
        Console.WriteLine("RESTORE plates="+current.RestorePlates.Count+" supports="+current.RestoreSupports.Count);
        // Mẫu người dùng: đổi trạng thái MÀU TRONG CACHE và snapshot visibility, không đổi màu/ẩn trong Tekla.
        if(current.MainId==117633970 && current.Nodes.ContainsKey(109028105) && current.Nodes.ContainsKey(130973315))
        {
            var ambiguous=new N.Plan { Keep=new Dictionary<int,string>(old.Keep) };
            foreach(var pair in current.Nodes)ambiguous.Nodes.Add(pair.Key,pair.Value);
            ambiguous.Edges.AddRange(current.Edges);
            ambiguous.Items.AddRange(current.Items.Select(i=>new N.Item { Part=i.Part,ModelId=i.ModelId,ViewId=i.ViewId,
                WasHidden=i.ModelId==109028105 || i.ModelId==130973315 ? true : i.WasHidden }));
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            var choose=typeof(N).GetMethod("ChooseReferenceSupport",flags);
            var toGlobal=new Tekla.Structures.Model.Model().GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var selected=(int)choose.Invoke(null,new object[] { ambiguous,ambiguous.Nodes[109028105],new HashSet<int>{112256909},
                primary,toGlobal,new Dictionary<int,N.ReferenceShape>(),new Dictionary<int,int>{{109028105,-1},{130973315,0}},new HashSet<int>{109028105} });
            Require(selected==0 && ambiguous.Keep.ContainsKey(109028105) && ambiguous.Keep.ContainsKey(130973315),
                "Ambiguous live geometry did not retain both references");
            var restore=N.ResolveReferenceSupportRestores(ambiguous.ReferenceSupportPlates,ambiguous.Nodes,primary,ambiguous.Items);
            Require(restore.Count(i=>i.ModelId==109028105 || i.ModelId==130973315)==4,"Ambiguous group not restored in both views");
            Require(ambiguous.Warnings.Count>0 && Check.ResolveConnected(ambiguous,primary).SetEquals(Check.ResolveConnected(current,primary)),
                "Ambiguity warning/Check DIM invariant failed");
            foreach(var i in current.Items.Where(i=>i.ModelId!=109028105 && i.ModelId!=130973315))
                Require(ambiguous.Keep.ContainsKey(i.ModelId)==old.Keep.ContainsKey(i.ModelId),"Ambiguity changed unrelated neighbor target");
            Console.WriteLine("PASS ambiguous live-geometry snapshot retains/restores BOTH references in BOTH views; unrelated hide decisions unchanged; no Apply call");
            ambiguous.Keep=new Dictionary<int,string>(old.Keep);ambiguous.ReferenceSupportPlates.Clear();ambiguous.Warnings.Clear();
            var geometry=new Dictionary<int,N.ReferenceShape>();
            selected=(int)choose.Invoke(null,new object[] { ambiguous,ambiguous.Nodes[109028105],new HashSet<int>{112256909},
                primary,toGlobal,geometry,new Dictionary<int,int>{{109028105,1},{130973315,1}},new HashSet<int>{109028105} });
            Require(selected==0 && ambiguous.ReferenceSupportPlates.ContainsKey(109028105)
                && ambiguous.ReferenceSupportPlates.ContainsKey(130973315),"Two colored overlapping members must both remain visible");
            Console.WriteLine("PASS both overlapping references colored: keep both instead of preferring Connection input");
            int remoteId=ambiguous.Nodes.Keys.Max()+1;
            var remoteShape=new N.ReferenceShape { AxisX=geometry[130973315].AxisX,AxisY=geometry[130973315].AxisY };
            Func<Tekla.Structures.Geometry3d.Point,Tekla.Structures.Geometry3d.Point> shift=point=>
                new Tekla.Structures.Geometry3d.Point(point.X+100000,point.Y+100000,point.Z+100000);
            remoteShape.Vertices=geometry[130973315].Vertices.Select(shift).ToArray();
            foreach(var face in geometry[130973315].Faces)remoteShape.Faces.Add(new N.ReferenceFace {Normal=face.Normal,Points=face.Points.Select(shift).ToArray()});
            geometry[remoteId]=remoteShape;
            ambiguous.Nodes.Add(remoteId,new N.Node { Id=remoteId,Dummy=true,Part=ambiguous.Nodes[130973315].Part });
            ambiguous.Items.Add(new N.Item {ModelId=remoteId,ViewId=1120,WasHidden=false});
            ambiguous.Keep=new Dictionary<int,string>(old.Keep);ambiguous.ReferenceSupportPlates.Clear();ambiguous.Warnings.Clear();
            selected=(int)choose.Invoke(null,new object[] { ambiguous,ambiguous.Nodes[109028105],new HashSet<int>{112256909},
                primary,toGlobal,geometry,new Dictionary<int,int>{{109028105,-1},{130973315,0},{remoteId,1}},new HashSet<int>{109028105} });
            Require(selected==0 && !ambiguous.Keep.ContainsKey(remoteId) && !ambiguous.ReferenceSupportPlates.ContainsKey(remoteId),
                "Distant same-profile/material reference was pulled into keep-both group");
            Console.WriteLine("PASS distant reference with same profile/material and same view stays outside ambiguous group");
        }
        Console.WriteLine("TIMING single sample old_ms="+oldMs+" current_ms="+newMs+" (sequential, not a benchmark)");
        var verify=Old.Analyze();
        Require(old.Drawing.IsSameDatabaseObject(verify.Drawing) && Keep(old.Keep)==Keep(verify.Keep),"Source state changed during comparison");
        Require(old.Items.Select(i=>i.ViewId+":"+i.ModelId+":"+i.WasHidden).OrderBy(s=>s).SequenceEqual(
            verify.Items.Select(i=>i.ViewId+":"+i.ModelId+":"+i.WasHidden).OrderBy(s=>s)),"Visibility changed during comparison");
        Console.WriteLine("PASS no drawing/model/selection/work-plane mutation");
    }
}
