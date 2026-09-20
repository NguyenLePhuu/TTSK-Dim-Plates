using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using D = Tekla.Structures.Drawing;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

static class LiveTests
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static string Signature(R.ArrangeReference layout)
    {
        return String.Join("|", layout.Views.OrderBy(v=>v.Presentation.Id).Select(v=>
            v.Presentation.Id + ":" + v.Scale + ":" + String.Join(";", v.Presentation.Segments.OrderBy(s=>s.Id)
                .Select(s=>s.Id+":"+s.Value.ToString("F6")+":"+s.A.ToString("F6")+":"+s.B.ToString("F6")))));
    }
    static void Log(string message)
    { Console.WriteLine(message); File.AppendAllText("live-main-report.txt",message+Environment.NewLine); }
    static R.ArrangeReference Read(D.DrawingHandler h,D.Drawing drawing)
    {
        if(h.GetActiveDrawing()!=null || !h.SetActiveDrawing(drawing,false)) throw new Exception("Cannot open "+drawing.Mark);
        try
        {
            if(!h.GetActiveDrawing().IsSameDatabaseObject(drawing)) throw new Exception("Wrong drawing");
            return R.CaptureArrangeReference();
        }
        finally { if(!h.CloseActiveDrawing(false)) throw new Exception("Cannot close "+drawing.Mark); }
    }
    public static void Run()
    {
        File.WriteAllText("live-main-report.txt","Official drawing run "+DateTime.Now+Environment.NewLine);
        var handler=new D.DrawingHandler();
        if(!handler.GetConnectionStatus() || handler.GetActiveDrawing()!=null) throw new Exception("Need connected Tekla, no active drawing");
        var selected=handler.GetDrawingSelector().GetSelected();var drawings=new List<D.Drawing>();
        while(selected.MoveNext())drawings.Add(selected.Current);
        if(drawings.Count<2)throw new Exception("Need selected drawings");
        var before=new List<R.ArrangeReference>();
        foreach(var d in drawings)
        {
            var snapshot=Read(handler,d);before.Add(snapshot);
            Log("BEFORE "+d.Mark+" "+String.Join(";",snapshot.Views.Select(v=>v.Presentation.Id+" "+v.Type+" "+v.Origin+" scale="+v.Scale)));
        }
        using(var form=new TTSK_AutoDim_Plates.MainForm())
        {
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-30000,-30000);
            form.Show();Application.DoEvents();
            var type=form.GetType();
            for(int pass=1;pass<=2;pass++)
            {
                Log("START load pass "+pass);
                if(!(bool)type.GetMethod("LoadSelectedArrangeDrawings",Flags).Invoke(form,null))throw new Exception("Load failed");
                Log("LOADED pass "+pass);
                var loaded=(List<D.Drawing>)type.GetField("_selectedDrawings",Flags).GetValue(form);
                if(loaded.Count!=drawings.Count || !loaded[0].IsSameDatabaseObject(drawings[0]))throw new Exception("Selection/reference changed");
                var task=(Task)type.GetMethod("RunArrangeReferenceBatch",Flags).Invoke(form,null);
                Log("START batch task pass "+pass);
                while(!task.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(10);}
                task.GetAwaiter().GetResult();
                var grid=(DataGridView)type.GetField("dgvDrawings",Flags).GetValue(form);
                foreach(DataGridViewRow row in grid.Rows)
                {
                    if(row.IsNewRow)continue;
                    string result=Convert.ToString(row.Cells["RESULT"].Value);
                    Log("PASS "+pass+" "+row.Cells["MARK"].Value+" "+result);
                    if(!result.StartsWith("OK")&&!result.StartsWith("REFERENCE"))throw new Exception("Batch row failed: "+result);
                    if(pass==2 && result.StartsWith("OK")&&!result.Contains("đã thẳng hàng"))throw new Exception("Second run moved view");
                }
                if(handler.GetActiveDrawing()!=null)throw new Exception("Batch left drawing open");
                for(int i=0;i<drawings.Count;i++)
                {
                    var after=Read(handler,drawings[i]);
                    if(Signature(after)!=Signature(before[i]))throw new Exception("DIM/scale changed "+drawings[i].Mark);
                    if(i==0)
                    {
                        foreach(var v in after.Views)
                            if(R.RevisionPositionDiffers(v.Origin,before[0].Views.Single(b=>b.Presentation.Id==v.Presentation.Id).Origin))
                                throw new Exception("Reference moved");
                    }
                    else
                    {
                        var targets=R.PlanArrangeReference(before[0],after);
                        for(int j=0;j<targets.Count;j++)
                            if(R.RevisionPositionDiffers(after.Views[j].Origin,targets[j]))throw new Exception("Persisted position mismatch "+drawings[i].Mark);
                    }
                    Log("VERIFIED reopened "+drawings[i].Mark+" · position, scale, DIM signatures");
                }
            }
        }
        Log("SUCCESS: both MainForm runs and reopen verification passed; reference unchanged.");
    }
}
