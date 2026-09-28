using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using U=Tekla.Technology.Akit.UserScript;
using D=Tekla.Structures.Drawing;

partial class Program
{
    static void DrawingRouteTests()
    {
        var column=new[]{P(0,38645,19318),P(0,38645,22376)};
        var horizontal=new[]{P(0,38645,19318),P(3058,38645,19318)};
        Check(U.PHU_Slot07_DrawingRoute.Resolve(U.PHU_Slot07_ColumnConnections.IsVerticalReference(column),true,false)==U.Slot07DrawingRoute.LegacyColumn,"Inzai vertical H must enter full legacy column flow");
        Check(U.PHU_Slot07_DrawingRoute.Resolve(U.PHU_Slot07_ColumnConnections.IsVerticalReference(horizontal),true,false)==U.Slot07DrawingRoute.Beam,"Same H section horizontal must retain beam flow");
        Check(U.PHU_Slot07_DrawingRoute.Resolve(true,false,true)==U.Slot07DrawingRoute.HollowColumn,"Sinjuku hollow column must retain plate/grid flow");
        Check(U.PHU_Slot07_DrawingRoute.Resolve(false,false,true)==U.Slot07DrawingRoute.Beam,"Horizontal hollow section remains a beam");
        Check(U.PHU_Slot07_DrawingRoute.Resolve(true,false,false)==U.Slot07DrawingRoute.UnsupportedColumn,"Unsupported vertical profile must not silently enter beam flow");
        Array.Reverse(column);
        Check(U.PHU_Slot07_DrawingRoute.Resolve(U.PHU_Slot07_ColumnConnections.IsVerticalReference(column),true,false)==U.Slot07DrawingRoute.LegacyColumn,"Reversing MainPart REF cannot turn column into beam");
        foreach(var p in column){p.X+=10000;p.Y-=90000;p.Z+=42;}
        Check(U.PHU_Slot07_DrawingRoute.Resolve(U.PHU_Slot07_ColumnConnections.IsVerticalReference(column),true,false)==U.Slot07DrawingRoute.LegacyColumn,"Translation cannot change route");
        Check(U.PHU_Slot07_DrawingRoute.Resolve(false,false,false)==U.Slot07DrawingRoute.Beam,"Other nonvertical supported beam profiles retain existing preflight");
    }

    static void ApplyInzai(string product,int expectedId,string path)
    {
        var a=Assembly.LoadFrom(Path.GetFullPath(product));var t=a.GetType("TTSK_AutoDim_Plates.MainForm",true);
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        object form=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);
        // Deliberately conflicting UI preferences. Explicit slot routing must win
        // without persisting or rewriting these settings.
        t.GetField("fitKeepGridAxes",flags).SetValue(form,false);
        t.GetField("fitArrangeColumnMode",flags).SetValue(form,false);
        t.GetField("fitGridAxisCount",flags).SetValue(form,1);
        t.GetField("_autoSectionEnabled",flags).SetValue(form,true);
        var h=new D.DrawingHandler();var d=h.GetActiveDrawing();
        Check(ObjectId(d)==expectedId,"Wrong drawing for live Inzai test");
        Check(U.PHU_Slot07_ColumnConnections.IsCurrentDrawingVertical(d) && !U.PHU_Slot07_ColumnConnections.IsCurrentDrawingColumn(d),"Must be a legacy upright column");
        string before=Layout(d);var log=new StringBuilder("MUTATING INZAI SLOT07 COORDINATOR\nDrawing="+d.Mark+" Id="+expectedId+"\n");
        var execute=t.GetMethod("RunDataCenterCurrentDrawing",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(bool)},null);
        Action run=()=>
        {
            Check(ObjectId(h.GetActiveDrawing())==expectedId,"Active drawing changed");
            var result=execute.Invoke(form,new object[]{true});
            var rt=result.GetType();string message=(string)rt.GetField("Message",flags).GetValue(result);
            string stage=(string)rt.GetField("FailedStage",flags).GetValue(result);
            log.AppendLine(stage+" "+message);File.WriteAllText(path,log.ToString());Console.WriteLine(stage+" "+message);
            Check((bool)rt.GetField("Success",flags).GetValue(result),"Inzai full coordinator failed: "+message);
            Check(message.StartsWith("COLUMN:"),"Wrong route reported success");
        };
        run();
        var first=NativeDimensionSignatures(h.GetActiveDrawing());
        Check(first.Count>0,"No actual dimensions after reported success");
        Check(Layout(h.GetActiveDrawing())==before,"Column view origins/scale/box or work plane changed");
        run();
        var second=NativeDimensionSignatures(h.GetActiveDrawing());
        File.WriteAllLines(path+".first",first.Values.OrderBy(s=>s));File.WriteAllLines(path+".second",second.Values.OrderBy(s=>s));
        Check(first.Values.OrderBy(s=>s).SequenceEqual(second.Values.OrderBy(s=>s)),"Second legacy replacement run changed dimension geometry or count");
        Check(!(bool)t.GetField("fitKeepGridAxes",flags).GetValue(form) && !(bool)t.GetField("fitArrangeColumnMode",flags).GetValue(form)
            && (int)t.GetField("fitGridAxisCount",flags).GetValue(form)==1 && (bool)t.GetField("_autoSectionEnabled",flags).GetValue(form),"Slot operation leaked into UI preferences");
        string messageFinal="PASS: "+first.Count+" dimension sets, all repeat feet/directions/distances identical; layout/work plane and UI settings preserved.";
        log.AppendLine(messageFinal);File.WriteAllText(path,log.ToString());Console.WriteLine(messageFinal);
    }
}
