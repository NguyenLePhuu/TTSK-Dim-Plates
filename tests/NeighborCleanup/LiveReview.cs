using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using D = Tekla.Structures.Drawing;
using N = Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;
using Check = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;

// MUTATING: chỉ chạy sau khi người dùng cho phép live-test hai bản vẽ được nêu rõ.
internal static class LiveReview
{
    static int Main(string[] args)
    {
        try
        {
            string product=args[0];
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
                string name=new AssemblyName(e.Name).Name;
                if(name=="TTSK Dim Plates")return Assembly.LoadFrom(product);
                foreach(string dir in new[] {Path.GetDirectoryName(product),args[1]})
                { string path=Path.Combine(dir,name+".dll");if(File.Exists(path))return Assembly.LoadFrom(path); }
                return null;
            };
            var assembly=Assembly.LoadFrom(product);
            Environment.SetEnvironmentVariable("TTSK_SESSION_PID",args[2]);
            Environment.SetEnvironmentVariable("TTSK_SESSION_START",args[3]);
            var session=assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionCoordinator");
            foreach(string method in new[]{"Prepare","VerifyConnection"})
                session.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            if(args.Length>5 && args[5]=="resume")Resume(args[4]);
            else if(args.Length>5 && args[5]=="ambiguous")Ambiguous(args[4]);
            else Execute(args[4]);return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static int Id(object obj)
    {return ((Tekla.Structures.Identifier)obj.GetType().GetProperty("Identifier",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj,null)).ID;}
    static string Key(N.Item item){return item.ViewId+":"+item.ModelId;}
    static string NormalReport(Check.Result result)
    {return String.Join("\n",result.Report().Split('\n').Where(line=>!line.StartsWith("CapturedUtc=")));}
    static string Layout(D.Drawing drawing)
    {
        var rows=new List<string>();var views=drawing.GetSheet().GetAllViews();
        while(views.MoveNext())
        {
            var v=views.Current as D.View;if(v==null)continue;
            rows.Add("VIEW "+Id(v)+" origin="+v.Origin+" scale="+v.Attributes.Scale+" box="+v.RestrictionBox.MinPoint+":"+v.RestrictionBox.MaxPoint
                +" cs="+v.DisplayCoordinateSystem.Origin+":"+v.DisplayCoordinateSystem.AxisX+":"+v.DisplayCoordinateSystem.AxisY);
            var objects=v.GetAllObjects();
            while(objects.MoveNext())
            {
                object obj=objects.Current;int id=Id(obj);rows.Add("OBJECT "+Id(v)+":"+id+":"+obj.GetType().FullName);
                if(!(obj is D.DimensionBase) && !(obj is D.StraightDimensionSet))continue;
                foreach(string name in new[]{"Distance","UpDirection","DimensionPoints"})
                {
                    var prop=obj.GetType().GetProperty(name);if(prop==null)continue;object value=prop.GetValue(obj,null);
                    var list=value as System.Collections.IEnumerable;
                    rows.Add("DIM "+id+":"+name+"="+(list==null?Convert.ToString(value,CultureInfo.InvariantCulture):String.Join(";",list.Cast<object>())));
                }
            }
        }
        return String.Join("\n",rows.OrderBy(s=>s,StringComparer.Ordinal));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Ambiguous(string output)
    {
        var handler=new D.DrawingHandler();var drawing=handler.GetActiveDrawing();
        Require(drawing!=null && drawing.Mark=="[C2-e-34]","Ambiguous live test requires [C2-e-34]");
        var plan=N.Analyze();File.WriteAllText(Path.Combine(output,"ambiguous-before.txt"),plan.Report());
        var baseline=plan.Items.ToDictionary(Key,i=>i.WasHidden);
        var primary=new HashSet<int>(plan.Keep.Where(p=>p.Value=="KEEP primary assembly").Select(p=>p.Key));
        string layout=Layout(drawing),check=NormalReport(Check.Analyze());
        var matrix=new Tekla.Structures.Model.Model().GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
        var choose=typeof(N).GetMethod("ChooseReferenceSupport",BindingFlags.Static|BindingFlags.NonPublic);
        int chosen=(int)choose.Invoke(null,new object[]{plan,plan.Nodes[109028105],new HashSet<int>{112256909},primary,matrix,
            new Dictionary<int,N.ReferenceShape>(),new Dictionary<int,int>{{109028105,-1},{130973315,0}},new HashSet<int>{109028105}});
        Require(chosen==0 && plan.Keep.ContainsKey(109028105) && plan.Keep.ContainsKey(130973315),"Ambiguous fallback did not keep both");
        plan.RestoreSupports.Clear();
        plan.RestoreSupports.AddRange(N.ResolveReferenceSupportRestores(plan.ReferenceSupportPlates,plan.Nodes,primary,plan.Items));
        Require(plan.RestoreSupports.All(i=>i.ModelId==109028105 || i.ModelId==130973315),"Ambiguous restore escaped target pair");
        Console.WriteLine("MUTATING AMBIGUITY TEST: color cache only; show both references, then restore normal rule");
        int count=plan.Apply();Require(count==0,"Ambiguous pass hid unrelated objects");
        var shown=N.Analyze();File.WriteAllText(Path.Combine(output,"ambiguous-both-shown.txt"),shown.Report());
        Require(shown.Items.Count(i=>(i.ModelId==109028105 || i.ModelId==130973315) && !i.WasHidden)==4,"Both references not visible both views");
        foreach(var i in shown.Items.Where(i=>i.ModelId!=109028105 && i.ModelId!=130973315))
            Require(baseline[Key(i)]==i.WasHidden,"Unrelated visibility changed in ambiguous pass");
        Require(layout==Layout(drawing) && check==NormalReport(Check.Analyze()),"Ambiguous pass changed layout or DIM");
        Console.WriteLine("PASS ambiguity: both references visible both views; unrelated objects/layout/DIM unchanged; no force-stop");
        Require(shown.Items.Where(i=>!i.WasHidden && !shown.Keep.ContainsKey(i.ModelId)).All(i=>i.ModelId==109028105),"Normal restoration targets escaped gray reference");
        Require(shown.Apply()==2,"Normal rule did not re-hide gray reference in both views");
        var final=N.Analyze();foreach(var i in final.Items)Require(baseline[Key(i)]==i.WasHidden,"Final visibility differs from verified baseline");
        Require(layout==Layout(drawing) && check==NormalReport(Check.Analyze()),"Final layout or DIM changed");
        Require(final.Apply()==0 && handler.SaveActiveDrawing(),"Final idempotency/save failed");
        File.WriteAllText(Path.Combine(output,"ambiguous-final.txt"),final.Report());
        Console.WriteLine("PASS restored final real rule: red visible, gray hidden, second run=0; saved [C2-e-34]");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Resume(string output)
    {
        var handler=new D.DrawingHandler();var current=handler.GetActiveDrawing();
        Require(current!=null && current.Mark=="[C2-e-73]","Resume requires [C2-e-73]");
        var expected=new Dictionary<string,bool>();
        foreach(string line in File.ReadAllLines(Path.Combine(output,"C2-e-73-before.txt")))
        {
            var match=System.Text.RegularExpressions.Regex.Match(line,@"^VIEW=(\d+) P(\d+) .* hidden=(True|False) (.*)$");
            if(!match.Success)continue;
            string reason=match.Groups[4].Value;
            expected.Add(match.Groups[1].Value+":"+match.Groups[2].Value,
                !reason.StartsWith("SHOW ") && (match.Groups[3].Value=="True" || reason.StartsWith("HIDE ")));
        }
        Require(expected.Count>0,"Missing baseline");
        var after=N.Analyze();Require(after.Items.Count==expected.Count,"Part count changed after live test");
        foreach(var i in after.Items)Require(expected.ContainsKey(Key(i)) && expected[Key(i)]==i.WasHidden,"Unexpected part visibility "+Key(i));
        string currentLayout=Layout(current);
        File.WriteAllText(Path.Combine(output,"C2-e-73-layout-after.txt"),currentLayout);
        Require(File.ReadAllText(Path.Combine(output,"C2-e-73-layout-before.txt"))==currentLayout,"Layout/DIM/objects changed");
        string currentCheck=NormalReport(Check.Analyze());
        File.WriteAllText(Path.Combine(output,"C2-e-73-check-after.txt"),currentCheck);
        Require(File.ReadAllText(Path.Combine(output,"C2-e-73-check-before.txt"))==currentCheck,"Check DIM changed");
        Require(after.RestorePlates.Count==0 && after.RestoreSupports.Count==0
            && after.Items.All(i=>i.WasHidden || after.Keep.ContainsKey(i.ModelId)),"Second run still has changes");
        Require(after.Apply()==0,"Second Apply not idempotent");
        Require(handler.SaveActiveDrawing(),"Cannot save e73 verified result");
        Console.WriteLine("PASS [C2-e-73] resumed verification: all expected visibility, object/layout/DIM and Check DIM unchanged; second Apply=0");
        Console.WriteLine("DISCOVERY locating assembly 117633989 for initial drawing [C2-e-34]");
        D.Drawing first=null;var drawings=handler.GetDrawings();
        while(drawings.MoveNext())
        {var d=drawings.Current as D.AssemblyDrawing;if(d!=null && d.AssemblyIdentifier.ID==117633989){first=d;break;}}
        Require(first!=null && first.Mark=="[C2-e-34]","Initial drawing not found");
        Require(handler.CloseActiveDrawing(true) && handler.SetActiveDrawing(first,true),"Cannot restore initial active drawing");
        var reopen=N.Analyze();
        Require(reopen.Items.Count(i=>i.ModelId==130973315 && !i.WasHidden)==2
            && reopen.Items.Where(i=>i.ModelId==109028105).All(i=>i.WasHidden),"Saved reference visibility failed");
        File.WriteAllText(Path.Combine(output,"C2-e-34-reopened.txt"),reopen.Report());
        Console.WriteLine("PASS [C2-e-34] saved/reopened state: correct red support visible both views, gray reference hidden; active drawing restored");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Execute(string output)
    {
        var handler=new D.DrawingHandler();var original=handler.GetActiveDrawing();
        Require(original!=null && original.Mark=="[C2-e-34]","Expected currently open [C2-e-34]");
        var drawings=new Dictionary<string,D.Drawing>();drawings.Add(original.Mark,original);
        Console.WriteLine("DISCOVERY read-only: locating assembly 112007028 for [C2-e-73]");
        var iterator=handler.GetDrawings();int scanned=0;
        while(iterator.MoveNext())
        {
            scanned++;if(scanned%100==0)Console.WriteLine("DISCOVERY scanned="+scanned);
            var d=iterator.Current as D.AssemblyDrawing;if(d==null || d.AssemblyIdentifier.ID!=112007028)continue;
            Require(d.Mark=="[C2-e-73]","Assembly matched but mark differs");drawings.Add(d.Mark,d);break;
        }
        Require(drawings.Count==2,"Both authorized drawings must exist");
        Console.WriteLine("MUTATING LIVE REVIEW: [C2-e-34] and [C2-e-73]; snapshots outside model");
        foreach(string mark in new[]{"[C2-e-34]","[C2-e-73]"})
        {
            var drawing=handler.GetActiveDrawing();
            if(drawing.Mark!=mark)
            {
                Require(handler.CloseActiveDrawing(true),"Cannot save/close current drawing");
                Require(handler.SetActiveDrawing(drawings[mark],true),"Cannot open "+mark);
                drawing=handler.GetActiveDrawing();Require(drawing!=null && drawing.Mark==mark,"Wrong active drawing");
            }
            string stem=mark.Trim('[',']');
            var plan=N.Analyze();Require(plan.Drawing.IsSameDatabaseObject(drawing),"Plan identity mismatch");
            File.WriteAllText(Path.Combine(output,stem+"-before.txt"),plan.Report());
            string layout=Layout(drawing),check=NormalReport(Check.Analyze());
            File.WriteAllText(Path.Combine(output,stem+"-layout-before.txt"),layout);
            File.WriteAllText(Path.Combine(output,stem+"-check-before.txt"),check);
            var expected=plan.Items.ToDictionary(Key,i=>i.WasHidden);
            foreach(var i in plan.Items.Where(i=>!i.WasHidden && !plan.Keep.ContainsKey(i.ModelId)))expected[Key(i)]=true;
            foreach(var i in plan.RestorePlates.Concat(plan.RestoreSupports))expected[Key(i)]=false;
            int hides=plan.Items.Count(i=>!i.WasHidden && !plan.Keep.ContainsKey(i.ModelId));
            int shows=plan.RestorePlates.Count+plan.RestoreSupports.Count;
            Console.WriteLine(mark+" planned hide="+hides+" show="+shows);
            int changed=plan.Apply();Require(changed==hides,"Hide count mismatch");
            var after=N.Analyze();File.WriteAllText(Path.Combine(output,stem+"-after.txt"),after.Report());
            Require(after.Items.Count==expected.Count,"Drawing part count changed");
            foreach(var i in after.Items)Require(expected.ContainsKey(Key(i)) && expected[Key(i)]==i.WasHidden,"Unexpected visibility "+Key(i));
            Require(layout==Layout(drawing),"Layout/object/DIM signature changed");
            Require(check==NormalReport(Check.Analyze()),"Check DIM output changed");
            if(mark=="[C2-e-34]")
            {
                Require(after.Items.Count(i=>i.ModelId==130973315 && !i.WasHidden)==2,"Correct red reference not visible in both views");
                Require(after.Items.Where(i=>i.ModelId==109028105).All(i=>i.WasHidden),"Old gray reference still visible");
            }
            else foreach(int plate in new[]{119336702,119337652})
                Require(after.Items.Count(i=>i.ModelId==plate && !i.WasHidden)>=2,"Prior connector plate lost: "+plate);
            Require(after.Items.All(i=>i.WasHidden || after.Keep.ContainsKey(i.ModelId)) && after.RestorePlates.Count==0 && after.RestoreSupports.Count==0,
                "Second run would still change visibility");
            Require(after.Apply()==0,"Second run changed drawing");
            Require(layout==Layout(drawing),"Second run changed layout");
            Require(handler.SaveActiveDrawing(),"Cannot save verified result");
            Console.WriteLine("PASS "+mark+": all planned visibility verified, unrelated objects/views/DIM unchanged, second Apply=0");
        }
        Require(handler.CloseActiveDrawing(true),"Cannot close previous drawing");
        Require(handler.SetActiveDrawing(drawings["[C2-e-34]"],true),"Cannot restore initial active drawing");
        var reopen=N.Analyze();Require(reopen.Drawing.Mark=="[C2-e-34]","Wrong reopened drawing");
        Require(reopen.Items.Count(i=>i.ModelId==130973315 && !i.WasHidden)==2
            && reopen.Items.Where(i=>i.ModelId==109028105).All(i=>i.WasHidden),"Saved support visibility did not persist");
        Console.WriteLine("PASS saved/reopened [C2-e-34], original active drawing restored");
    }
}
