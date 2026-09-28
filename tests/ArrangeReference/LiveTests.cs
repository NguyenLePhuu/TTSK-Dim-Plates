using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using D = Tekla.Structures.Drawing;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

static class LiveTests
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static string report;
    static string N(double value) { return value.ToString("F6", CultureInfo.InvariantCulture); }
    static string Point(Tekla.Structures.Geometry3d.Point p) { return N(p.X)+","+N(p.Y)+","+N(p.Z); }
    static int Id(object value)
    { return ((Tekla.Structures.Identifier)value.GetType().GetProperty("Identifier",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value,null)).ID; }
    sealed class ViewState
    {
        internal int Id;
        internal string Name, Type, FixedGeometry, Dimensions, Objects;
        internal Tekla.Structures.Geometry3d.Point Origin;
        internal double Scale;
    }
    sealed class State
    {
        internal R.ArrangeReference Layout;
        internal List<ViewState> Views = new List<ViewState>();
        internal string Lifecycle;
    }
    static State Capture()
    {
        var state = new State { Layout = R.CaptureArrangeReference() };
        var drawing = state.Layout.Drawing;
        state.Lifecycle = drawing.IsLocked+","+drawing.IsFrozen+","+drawing.IsIssued+","+drawing.IsReadyForIssue;
        var presentation = (List<R.SnapshotView>)typeof(R).GetMethod("ReadCurrentPresentation", BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[] { new List<string>() });
        var views = drawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            var view = views.Current as D.View; if (view == null) continue;
            var box = view.RestrictionBox; var cs = view.DisplayCoordinateSystem; var vcs = view.ViewCoordinateSystem;
            var p = presentation.Single(v => v.Id == Id(view));
            var objects = new List<string>(); var en = view.GetAllObjects();
            while (en.MoveNext())
            {
                var obj = en.Current as D.DrawingObject;
                if (obj != null) objects.Add(Id(obj)+":"+obj.GetType().Name);
            }
            state.Views.Add(new ViewState { Id = Id(view), Name = view.Name, Type = view.ViewType.ToString(),
                Origin = new Tekla.Structures.Geometry3d.Point(view.Origin), Scale = view.Attributes.Scale,
                FixedGeometry = Point(box.MinPoint)+"/"+Point(box.MaxPoint)+"/"+Point(cs.Origin)+"/"+Point(cs.AxisX)+"/"+Point(cs.AxisY)
                    +"/"+Point(vcs.Origin)+"/"+Point(vcs.AxisX)+"/"+Point(vcs.AxisY),
                Objects = String.Join(";",objects.OrderBy(s=>s)),
                Dimensions = String.Join(";",p.Segments.OrderBy(s=>s.Id).Select(s=>s.Id+":"+N(s.Value)+":"+N(s.A)+":"+N(s.B)
                    +":"+N(s.X)+":"+N(s.Y)+":"+N(s.PaperA)+":"+N(s.PaperB)+":"+N(s.Level)+":"+N(s.FootA)+":"+N(s.FootB))) });
        }
        return state;
    }
    static void Log(string message)
    { Console.WriteLine(message); File.AppendAllText(report,message+Environment.NewLine); }
    static State Read(D.DrawingHandler h,D.Drawing drawing)
    {
        if(h.GetActiveDrawing()!=null || !h.SetActiveDrawing(drawing,false)) throw new Exception("Cannot open "+drawing.Mark);
        try
        {
            if(!h.GetActiveDrawing().IsSameDatabaseObject(drawing)) throw new Exception("Wrong drawing");
            return Capture();
        }
        finally { if(!h.CloseActiveDrawing(false)) throw new Exception("Cannot close "+drawing.Mark); }
    }
    static void Await(Task task)
    {
        while(!task.IsCompleted) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        task.GetAwaiter().GetResult();
    }
    public static void ProfileRead()
    {
        report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profile-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
        var h=new D.DrawingHandler();
        if(h.GetActiveDrawing()!=null)throw new Exception("Close active drawing before profiling");
        var selected=h.GetDrawingSelector().GetSelected();
        while(selected.MoveNext())
        {
            var d=selected.Current; var mark=d.Mark; var watch=Stopwatch.StartNew();
            if(!h.SetActiveDrawing(d,false))throw new Exception("Cannot open "+mark);
            Log("PROFILE "+mark+" OPEN_MS="+watch.ElapsedMilliseconds);
            try
            {
                if(!h.GetActiveDrawing().IsSameDatabaseObject(d))throw new Exception("Wrong drawing");
                watch.Restart(); R.CaptureArrangeReference();
                Log("PROFILE "+mark+" CAPTURE_MS="+watch.ElapsedMilliseconds);
            }
            finally
            {
                watch.Restart(); if(!h.CloseActiveDrawing(false))throw new Exception("Cannot close "+mark);
                Log("PROFILE "+mark+" CLOSE_MS="+watch.ElapsedMilliseconds);
            }
        }
    }
    public static void Benchmark()
    {
        report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"benchmark-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
        if (new D.DrawingHandler().GetActiveDrawing()!=null) throw new Exception("Close active drawing before benchmark");
        using (var form = new TTSK_AutoDim_Plates.MainForm())
        {
            form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new System.Drawing.Point(-30000,-30000);
            form.Show(); Application.DoEvents(); var type=form.GetType();
            for(int pass=1;pass<=2;pass++)
            {
                var load=(Task<bool>)type.GetMethod("LoadSelectedArrangeDrawings",Flags).Invoke(form,null);
                Await(load); if(!load.Result) throw new Exception("Load failed");
                var watch=Stopwatch.StartNew();
                Await((Task)type.GetMethod("RunArrangeReferenceBatch",Flags).Invoke(form,null));
                watch.Stop(); Log("BATCH_MS pass="+pass+" elapsed="+watch.ElapsedMilliseconds);
                foreach(DataGridViewRow row in ((DataGridView)type.GetField("dgvDrawings",Flags).GetValue(form)).Rows)
                {
                    if(row.IsNewRow)continue;
                    var result=Convert.ToString(row.Cells["RESULT"].Value);
                    Log(row.Cells["MARK"].Value+" "+result);
                    if(!result.StartsWith("OK")&&!result.StartsWith("REFERENCE"))throw new Exception(result);
                }
            }
        }
    }
    // Explicit mutating test setup: restore only main-view positions recorded by
    // this test, on the same model and currently selected drawings. Never move sections.
    public static void RestoreMainOrigins(string path)
    {
        report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"restore-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt");
        var lines=File.ReadAllLines(path);
        var modelPath=new Tekla.Structures.Model.Model().GetInfo().ModelPath;
        var h=new D.DrawingHandler(); if(h.GetActiveDrawing()!=null)throw new Exception("Close active drawing before restore");
        var en=h.GetDrawingSelector().GetSelected();var drawings=new List<D.Drawing>();while(en.MoveNext())drawings.Add(en.Current);
        if(drawings.Count!=lines.Count(l=>l.StartsWith("BEFORE ")))throw new Exception("Selection changed");
        foreach(var d in drawings.Skip(1))
        {
            if(!lines.Any(l=>l.StartsWith("BEFORE "+d.Mark+" model="+modelPath+" flags=")))throw new Exception("Model/drawing not in baseline");
            var rows=lines.Where(l=>l.StartsWith("VIEW "+d.Mark+" ")).ToDictionary(l=>int.Parse(Regex.Match(l,@" id=(\d+) ").Groups[1].Value));
            if(!h.SetActiveDrawing(d,false))throw new Exception("Cannot open "+d.Mark);
            bool saved=false;
            try
            {
                if(!h.GetActiveDrawing().IsSameDatabaseObject(d))throw new Exception("Wrong active drawing");
                var views=d.GetSheet().GetAllViews(); int read=0;
                while(views.MoveNext())
                {
                    var v=views.Current as D.View;if(v==null)continue;read++;
                    var row=rows[Id(v)];var values=Regex.Match(row,@" origin=([^ ]+) ").Groups[1].Value.Split(',').Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                    var original=new Tekla.Structures.Geometry3d.Point(values[0],values[1],values[2]);
                    if(row.Contains("role=PRESERVE"))
                    {
                        if(R.RevisionPositionDiffers(v.Origin,original))throw new Exception("Preserved section no longer matches baseline");
                        continue;
                    }
                    Log("RESTORE "+d.Mark+" id="+Id(v)+" from="+Point(v.Origin)+" to="+Point(original));
                    if(R.RevisionPositionDiffers(v.Origin,original)){v.Origin=original;if(!v.Modify())throw new Exception("Restore failed");}
                }
                if(read!=rows.Count || !d.CommitChanges() || !h.SaveActiveDrawing())throw new Exception("Restore/save failed");
                saved=true;
            }
            finally
            {
                var active=h.GetActiveDrawing();
                if(active!=null && active.IsSameDatabaseObject(d))h.CloseActiveDrawing(false);
                Log("RESTORE SAVED "+d.Mark+" "+saved);
            }
        }
    }
    public static void Run(bool auditOnly = false)
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selected-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory); report = Path.Combine(directory,auditOnly ? "audit.txt" : "live-main-report.txt");
        Log((auditOnly ? "READ ONLY: no Modify/Commit/Save" : "MUTATING: user-selected drawings, actual MainForm batch, two passes")+" "+DateTime.Now);
        Log("Production="+typeof(R).Assembly.Location+" Report="+report);
        var handler=new D.DrawingHandler();
        if(!handler.GetConnectionStatus() || handler.GetActiveDrawing()!=null) throw new Exception("Need connected Tekla, no active drawing");
        var selected=handler.GetDrawingSelector().GetSelected();var drawings=new List<D.Drawing>();
        while(selected.MoveNext())drawings.Add(selected.Current);
        if(drawings.Count<2)throw new Exception("Need selected drawings");
        var before=new List<State>();
        var plans = new List<List<Tekla.Structures.Geometry3d.Point>>();
        foreach(var d in drawings)
        {
            var snapshot=Read(handler,d);before.Add(snapshot);
            Log("BEFORE "+d.Mark+" model="+snapshot.Layout.ModelPath+" flags="+snapshot.Lifecycle);
            foreach (var v in snapshot.Views)
            {
                var role = snapshot.Layout.Views.Concat(snapshot.Layout.PreservedViews).Single(x=>x.Id==v.Id);
                Log("VIEW "+d.Mark+" id="+v.Id+" "+v.Name+" "+v.Type+" origin="+Point(v.Origin)+" scale="+N(v.Scale)
                    +" role="+(snapshot.Layout.Views.Any(x=>x.Id==v.Id)?"MOVE MAIN":"PRESERVE")+" axis="+role.LongitudinalAxisIndex
                    +" coverage="+N(role.LongitudinalCoverage)+" axes="+String.Join(",",role.LocalAxes.Select(N)));
                Log("GEOMETRY "+v.Id+" "+v.FixedGeometry);
                Log("DIM "+v.Id+" "+v.Dimensions);
                Log("OBJECTS "+v.Id+" "+v.Objects);
            }
            var targets = R.PlanArrangeReference(before[0].Layout,snapshot.Layout); plans.Add(targets);
            for (int i=0;i<targets.Count;i++) Log("PLAN "+d.Mark+" id="+snapshot.Layout.Views[i].Id+" target="+Point(targets[i]));
        }
        Log("PREFLIGHT ALL "+drawings.Count+" DRAWINGS PASSED");
        if (auditOnly) return;
        using(var form=new TTSK_AutoDim_Plates.MainForm())
        {
            form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new System.Drawing.Point(-30000,-30000);
            form.Show();Application.DoEvents();
            var type=form.GetType();
            for(int pass=1;pass<=2;pass++)
            {
                Log("START load pass "+pass);
                var load = (Task<bool>)type.GetMethod("LoadSelectedArrangeDrawings",Flags).Invoke(form,null);
                Await(load); if(!load.Result)throw new Exception("Load failed");
                Log("LOADED pass "+pass);
                var loaded=(List<D.Drawing>)type.GetField("_selectedDrawings",Flags).GetValue(form);
                if(loaded.Count!=drawings.Count || loaded.Where((d,i)=>!d.IsSameDatabaseObject(drawings[i])).Any())throw new Exception("Selection/reference changed");
                var watch=Stopwatch.StartNew();
                var task=(Task)type.GetMethod("RunArrangeReferenceBatch",Flags).Invoke(form,null);
                Log("START batch task pass "+pass);
                Await(task);
                watch.Stop(); Log("BATCH_MS pass="+pass+" elapsed="+watch.ElapsedMilliseconds);
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
                    if(after.Views.Count!=before[i].Views.Count || after.Lifecycle!=before[i].Lifecycle) throw new Exception("View count/lifecycle changed");
                    foreach(var v in after.Views)
                    {
                        var old=before[i].Views.Single(b=>b.Id==v.Id);
                        if(v.Type!=old.Type || v.Name!=old.Name || Math.Abs(v.Scale-old.Scale)>1e-6
                            || v.FixedGeometry!=old.FixedGeometry || v.Dimensions!=old.Dimensions || v.Objects!=old.Objects)
                            throw new Exception("View geometry/scale/DIM/objects changed "+drawings[i].Mark+" id="+v.Id);
                        int mainIndex=before[i].Layout.Views.FindIndex(x=>x.Id==v.Id);
                        bool fixedView=i==0 || mainIndex<0;
                        var target=fixedView?old.Origin:plans[i][mainIndex];
                        if(Tekla.Structures.Geometry3d.Distance.PointToPoint(v.Origin,target)>.001)
                            throw new Exception("Persisted origin mismatch "+drawings[i].Mark+" id="+v.Id+" expected="+Point(target)+" actual="+Point(v.Origin));
                        Log("VERIFIED "+drawings[i].Mark+" id="+v.Id+" "+(fixedView?"PRESERVED":"ALIGNED")
                            +" before="+Point(old.Origin)+" after="+Point(v.Origin)+" unchanged scale/geometry/DIM/object IDs");
                    }
                }
            }
        }
        Log("SUCCESS: both MainForm runs and reopen verification passed; reference unchanged.");
    }
}
