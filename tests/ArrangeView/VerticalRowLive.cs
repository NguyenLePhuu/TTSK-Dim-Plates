using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

static class VerticalRowLive
{
    internal static void Run(string executable,string expectedMark,string snapshotPath)
    {
        Console.WriteLine("MUTATING vertical row-order integration test; uses current selection");
        var handler=new DrawingHandler();var drawing=handler.GetActiveDrawing();
        if(drawing==null||drawing.Mark!=expectedMark)throw new Exception("Drawing mismatch");
        var views=new List<View>();var e=drawing.GetSheet().GetAllViews();
        while(e.MoveNext()){var v=e.Current as View;if(v!=null)views.Add(v);}
        var origins=views.ToDictionary(SelectionAudit.Id,v=>new Point(v.Origin));
        var scales=views.ToDictionary(SelectionAudit.Id,v=>v.Attributes.Scale);
        File.WriteAllLines(snapshotPath,new[]{drawing.Mark}.Concat(views.Select(v=>SelectionAudit.Id(v)+" "+v.Name+" "+v.Origin)));
        var asm=Assembly.LoadFrom(Path.GetFullPath(executable));
        AppDomain.CurrentDomain.AssemblyResolve+=(s,a)=>new AssemblyName(a.Name).Name==asm.GetName().Name?asm:null;
        var engine=asm.GetType("TTSK_AutoDim_Plates.PHU_ArrangeView",true);
        var run=engine.GetMethod("Run",new[]{typeof(bool),typeof(bool),typeof(double),typeof(bool)});
        try
        {
            foreach(bool up in new[]{false,true})
            {
                if(!drawing.IsSameDatabaseObject(handler.GetActiveDrawing()))throw new Exception("Drawing changed");
                Console.WriteLine(engine.GetMethod("AuditCurrentLayout").Invoke(null,new object[]{false,15d,up}));
                var result=run.Invoke(null,new object[]{false,false,15d,up});
                Console.WriteLine(result.GetType().GetField("Message").GetValue(result));
                if(!(bool)result.GetType().GetField("Success").GetValue(result))throw new Exception("Arrange failed");
                foreach(var v in views)
                {
                    v.Select();
                    if((v.Name=="A"||v.ViewType!=View.ViewTypes.SectionView)&&Distance.PointToPoint(v.Origin,origins[SelectionAudit.Id(v)])>.001)
                        throw new Exception("Protected view moved");
                    if(v.Attributes.Scale!=scales[SelectionAudit.Id(v)])throw new Exception("Scale changed");
                }
                Console.WriteLine("PASS live vertical rows "+(up?"bottom-up":"top-down")+"; protected origins unchanged");
            }
        }
        catch
        {
            foreach(var v in views){v.Select();if(Distance.PointToPoint(v.Origin,origins[SelectionAudit.Id(v)])>.001){v.Origin=origins[SelectionAudit.Id(v)];if(!v.Modify())throw new Exception("Restore failed");}}
            drawing.CommitChanges();throw;
        }
    }
}
