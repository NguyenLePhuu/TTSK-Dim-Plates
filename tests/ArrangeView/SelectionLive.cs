using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

// Explicit MUTATING integration test, never dispatched by --audit/--inspect.
static class SelectionLive
{
    internal static void Run(string executable,string expectedMark,string snapshotPath)
    {
        Console.WriteLine("MUTATING ARRANGE SELECTION TEST; preserve special A and all unselected views");
        var handler=new DrawingHandler();var drawing=handler.GetActiveDrawing();
        if(drawing==null||drawing.Mark!=expectedMark)throw new Exception("Active drawing identity mismatch");
        var views=new List<View>();var e=drawing.GetSheet().GetAllViews();
        while(e.MoveNext()) {var v=e.Current as View;if(v!=null)views.Add(v);}
        var special=views.Single(v=>v.Name=="A");
        var subset=views.Where(v=>v.Name=="B"||v.Name=="C").ToList();
        if(subset.Count!=2)throw new Exception("Expected B and C fixture");
        var original=views.ToDictionary(SelectionAudit.Id,v=>new Point(v.Origin));
        var scales=views.ToDictionary(SelectionAudit.Id,v=>v.Attributes.Scale);
        var selected=new ArrayList();var selection=handler.GetDrawingObjectSelector().GetSelected();
        while(selection.MoveNext())selected.Add(selection.Current);
        File.WriteAllLines(snapshotPath,new[]{"Drawing="+drawing.Mark,"Portable="+Path.GetFullPath(executable)}.Concat(views.Select(v=>
            SelectionAudit.Id(v)+" "+v.Name+" Origin="+v.Origin+" Scale="+v.Attributes.Scale)));
        var asm=Assembly.LoadFrom(Path.GetFullPath(executable));
        AppDomain.CurrentDomain.AssemblyResolve+=(s,a)=>new AssemblyName(a.Name).Name==asm.GetName().Name?asm:null;
        var engine=asm.GetType("TTSK_AutoDim_Plates.PHU_ArrangeView",true);
        var method=engine.GetMethod("Run",new[]{typeof(bool),typeof(bool),typeof(double),typeof(bool)});
        Func<int,object> run=expected=>{
            if(!drawing.IsSameDatabaseObject(handler.GetActiveDrawing()))throw new Exception("Drawing changed");
            var result=method.Invoke(null,new object[]{false,true,15d,false});var t=result.GetType();
            Console.WriteLine(t.GetField("Message").GetValue(result));
            if(!(bool)t.GetField("Success").GetValue(result)||(int)t.GetField("SectionCount").GetValue(result)!=expected)
                throw new Exception("Run failed or wrong section count");
            return result;
        };
        Action<IEnumerable<View>> select=vs=>{
            var list=new ArrayList(vs.ToArray());
            handler.GetDrawingObjectSelector().UnselectAllObjects();
            if(list.Count>0)handler.GetDrawingObjectSelector().SelectObjects(list,false);
        };
        Action<HashSet<int>> verifyFixed=allowed=>{
            foreach(var v in views) {
                if(!v.Select())throw new Exception("View readback failed");
                if(!allowed.Contains(SelectionAudit.Id(v))&&Distance.PointToPoint(v.Origin,original[SelectionAudit.Id(v)])>.001)
                    throw new Exception("Unselected/protected view moved: "+v.Name);
                if(v.Attributes.Scale!=scales[SelectionAudit.Id(v)])throw new Exception("View scale changed");
            }
        };
        Action restore=()=>{
            foreach(var v in views) {
                v.Select();var p=original[SelectionAudit.Id(v)];
                if(Distance.PointToPoint(v.Origin,p)>.001){v.Origin=new Point(p);if(!v.Modify())throw new Exception("Restore failed");}
            }
            drawing.CommitChanges();verifyFixed(new HashSet<int>());
        };
        try
        {
            select(new[]{special});run(0);verifyFixed(new HashSet<int>());
            Console.WriteLine("PASS A-only: no movement, no automatic fallback");
            select(subset.Concat(new[]{special}));run(2);verifyFixed(new HashSet<int>(subset.Select(SelectionAudit.Id)));
            Console.WriteLine("PASS subset B,C + A: only B,C may move; A and all unselected fixed");
            restore();
            select(new View[0]);
            string audit=(string)engine.GetMethod("AuditCurrentLayout").Invoke(null,new object[]{true,15d,false});
            if(!audit.Contains("SelectionMode=False")||audit.Contains("VIEW A ")||!audit.Contains("PLAN PASS"))throw new Exception("Automatic audit failed");
            Console.WriteLine("PASS automatic read-only plan: A protected");
            select(views.Where(v=>v.ViewType==View.ViewTypes.SectionView));
            run(views.Count(v=>v.ViewType==View.ViewTypes.SectionView)-1);
            verifyFixed(new HashSet<int>(views.Where(v=>v.ViewType==View.ViewTypes.SectionView&&v.Name!="A").Select(SelectionAudit.Id)));
            Console.WriteLine("PASS full selection: B-K arranged; A/Front/Top unchanged");
            var applied=views.ToDictionary(SelectionAudit.Id,v=>new Point(v.Origin));
            run(views.Count(v=>v.ViewType==View.ViewTypes.SectionView)-1);
            foreach(var v in views){v.Select();if(Distance.PointToPoint(v.Origin,applied[SelectionAudit.Id(v)])>.1)throw new Exception("Repeat run changed placement");}
            Console.WriteLine("PASS repeat run stable; final arranged layout retained");
        }
        catch { restore();throw; }
        finally {
            handler.GetDrawingObjectSelector().UnselectAllObjects();
            if(selected.Count>0)handler.GetDrawingObjectSelector().SelectObjects(selected,false);
        }
    }
}
