using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;
using F = Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm;
using G = Tekla.Structures.Geometry3d;

static class RevisionCreateTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var old = new R.SnapshotView { X=70, Y=180 };
        var before = new R.RevisionPartBounds { MinX=0,MaxX=255,MinY=-4,MaxY=4 };
        foreach (double length in new[]{200.0,255,300})
        foreach (double lower in new[]{-9.0,-4,2})
        foreach (double shift in new[]{-30.0,0,40})
        {
            var after = new R.RevisionPartBounds { MinX=shift,MaxX=shift+length,MinY=lower,MaxY=lower+8 };
            var origin = R.RevisionBalancedOrigin(old,before,after,0);
            Assert(Math.Abs(origin.Y+lower-176)<1e-9,"Bottom edge must stay fixed after resize/translation");
            Assert(Math.Abs(origin.X+shift+length/2-197.5)<1e-9,"Horizontal centre must stay fixed after length change");
        }
        Assert(!R.RevisionPositionDiffers(new G.Point(70,180,0),R.RevisionBalancedOrigin(old,before,before,0)),"Unchanged view must be a no-op");
        foreach(bool dark in new[]{false,true}) foreach(int width in new[]{1000,1180})
        using(var form=new F(dark,()=>{throw new Exception("Must not run on open");}))
        {
            form.ClientSize=new Size(width,730);
            RevisionTests.ShowPreview(form);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            Assert(typeof(F).GetField("revisionResult",flags).GetValue(form)==null,"Opening Revision must not analyze or color");
            foreach(string name in new[]{"revisionStart","revisionCreate","revisionDelete"})
            {
                var button=form.Controls.Find(name,true).Single();
                Assert(button.Visible&&button.Enabled,"Missing enabled action "+name);
                Assert(button.Right<=button.Parent.ClientSize.Width,"Clipped action "+name);
                Assert(button.Bottom+button.Parent.Padding.Bottom<=button.Parent.ClientSize.Height,"Clipped bottom/shadow "+name);
                var bottom=form.PointToClient(button.PointToScreen(new Point(0,button.Height)));
                Assert(bottom.Y<form.ClientSize.Height-12,"Footer must have visible breathing room "+name);
            }
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"revision-start-"+(dark?"dark":"light")+"-"+width+".png"));}
        }
        Console.WriteLine("PASS Revision Create: resize/translation anchors, unchanged no-op, independent idle UI, 2 themes x 2 widths.");
    }
    public static void Audit(string path)
    {
        var layout=R.ReadRevisionLayout(false);
        var part=Tekla.Technology.Akit.UserScript.PHU_MainPartResolver.Resolve(new Tekla.Structures.Model.Model(),layout.Baseline.Drawing);
        string text=layout.Baseline.Report()+"\r\nPROFILE="+part.Profile.ProfileString+"\r\n"+
            String.Join("\r\n",layout.Views.Select(v=>"VIEW "+v.Id+" "+v.View.ViewType+" origin="+v.Original+" target="+v.Target+" scale="+v.Scale+
                " dx="+(v.Target.X-v.Original.X).ToString("R")+" dy="+(v.Target.Y-v.Original.Y).ToString("R")));
        File.WriteAllText(path,text);Console.WriteLine(text);
    }
    // Explicit opt-in integration command. Invokes exactly the production Create callback and REV UI.
    public static void MainPreview(string appPath,string folder)
    {
        Directory.CreateDirectory(folder);
        var assembly=Assembly.LoadFrom(Path.GetFullPath(appPath));
        var main=assembly.GetType("TTSK_AutoDim_Plates.MainForm",true);
        using(var form=(Form)Activator.CreateInstance(main))
        {
            RevisionTests.ShowPreview(form);
            form.PerformLayout();
            main.GetMethod("ShowCheckMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
            var host=(Control)main.GetField("checkMenuHost",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            var scale=(Control)main.GetField("btnCheckScale",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            Console.WriteLine("MENU visible="+host.Visible+" bounds="+host.Bounds+" scale enabled="+scale.Enabled);
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"check-menu.png"));}
        }
    }
    public static void Rollback(string output)
    {
        var layout=R.ReadRevisionLayout(false);
        Assert(layout.Baseline.Drawing.Mark=="[BmC-117]","Golden drawing changed; stop rollback test");
        var before=Geometry(layout);
        bool injected=false;
        try
        {
            R.CreateRevisionDrawing(()=>
            {
                var view=layout.Views.First(v=>v.View.ViewType.ToString().Contains("Top")).View;
                var points=new Tekla.Structures.Drawing.PointList();points.Add(new G.Point(0,0,0));points.Add(new G.Point(100,0,0));
                var created=new Tekla.Structures.Drawing.StraightDimensionSetHandler().CreateDimensionSet(view,points,new G.Vector(0,1,0),20);
                Assert(created!=null,"Failure fixture must create its temporary DIM");
                injected=true;
                throw new InvalidOperationException("Injected writer failure");
            });
        }
        catch(InvalidOperationException ex){Assert(ex.Message=="Injected writer failure","Unexpected rollback error: "+ex.Message);}
        Assert(injected,"Failure was not injected");
        var after=R.ReadRevisionLayout(false);
        Assert(before==Geometry(after),"Rollback must preserve all existing DIM IDs, feet, values, scale and view positions");
        string message="PASS live rollback: injected failure after DIM creation removed the new object; original DIMs and view positions/scales are unchanged.";
        File.WriteAllText(output,message);Console.WriteLine(message);
    }
    public static void NavigationLive(string output)
    {
        var layout=R.ReadRevisionLayout(false);var before=Geometry(layout);
        Assert(layout.Baseline.Drawing.Mark=="[BmC-117]","Golden drawing changed; stop navigation test");
        var result=R.Analyze(false);
        using(var form=new F(false,()=>"unused"))
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(F).GetField("revisionResult",flags).SetValue(form,result);
            typeof(F).GetMethod("RenderRevisionCards",flags).Invoke(form,null);
            Assert(((Button)typeof(F).GetField("revisionNext",flags).GetValue(form)).Enabled,"Red DIMs must be navigable");
            foreach(int direction in new[]{1,1,-1,-1})
            {
                typeof(F).GetMethod("MoveRevision",flags).Invoke(form,new object[]{direction});
                int expected=(int)typeof(F).GetField("revisionSelected",flags).GetValue(form);
                var selected=new Tekla.Structures.Drawing.DrawingHandler().GetDrawingObjectSelector().GetSelected();
                Assert(selected.MoveNext(),"Navigation did not select a DIM in Tekla");
                var id=(Tekla.Structures.Identifier)selected.Current.GetType().GetProperty("Identifier",flags|BindingFlags.Public).GetValue(selected.Current,null);
                var actualSet=selected.Current as Tekla.Structures.Drawing.StraightDimensionSet;
                bool containsTarget=id.ID==expected;
                if(actualSet!=null)
                {
                    // Tekla normalizes child selection to its owning dimension set.
                    var children=actualSet.GetObjects();
                    while(children.MoveNext())
                    {
                        var childId=(Tekla.Structures.Identifier)children.Current.GetType().GetProperty("Identifier",flags|BindingFlags.Public).GetValue(children.Current,null);
                        containsTarget|=childId.ID==expected;
                    }
                }
                Assert(containsTarget&&!selected.MoveNext(),"Navigation must select exactly the DIM set containing the displayed segment");
            }
            typeof(F).GetMethod("SetRevisionFilter",flags).Invoke(form,new object[]{1});
            var items=(System.Collections.IList)typeof(F).GetMethod("NavigableRevisionItems",flags).Invoke(form,null);
            Assert(items.Count==3,"Expected only 690.2 and the two 2555 value changes");
            typeof(F).GetMethod("MoveRevision",flags).Invoke(form,new object[]{1});
            Assert(((Label)typeof(F).GetField("revisionPosition",flags).GetValue(form)).Text=="1 / 3","Filtered position counter");
        }
        Assert(before==Geometry(R.ReadRevisionLayout(false)),"Navigation must not change dimensions or view geometry");
        var after=R.Analyze(false);
        Assert(after.Items.All(i=>!i.Review&&i.AlreadyRed)&&after.Unchanged==12,"Navigation must preserve REV colours/classification");
        string message="PASS live navigation: next/previous/wrap select exactly the owning DIM set; value filter has 3 DIMs; feet, values, views, scale and REV colours remain unchanged.";
        File.WriteAllText(output,message);Console.WriteLine(message);
    }
    static string Geometry(R.RevisionLayout layout)
    {
        var lines=new System.Collections.Generic.List<string>();
        foreach(var v in layout.Views)
        {
            lines.Add(v.Id+"|"+v.View.Origin+"|"+v.View.Attributes.Scale);
            var objects=v.View.GetAllObjects();while(objects.MoveNext())
            {
                var d=objects.Current as Tekla.Structures.Drawing.StraightDimension;if(d==null)continue;
                var id=((Tekla.Structures.Identifier)d.GetType().GetProperty("Identifier",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(d,null)).ID;
                lines.Add(id+"|"+d.StartPoint+"|"+d.EndPoint+"|"+d.Distance+"|"+d.UpDirection+"|"+
                    String.Concat(d.Value.Cast<object>().OfType<Tekla.Structures.Drawing.TextElement>().Select(t=>t.Value)));
            }
        }
        return String.Join("\n",lines.OrderBy(s=>s));
    }
    public static void Live(string appPath,string folder)
    {
        Directory.CreateDirectory(folder);
        Assert(new Tekla.Structures.Drawing.DrawingHandler().GetActiveDrawing().Mark=="[BmC-117]","Golden drawing changed; stop live test");
        var assembly=Assembly.LoadFrom(Path.GetFullPath(appPath));
        var main=assembly.GetType("TTSK_AutoDim_Plates.MainForm",true);
        using(var form=(Form)Activator.CreateInstance(main))
        {
            var method=main.GetMethod("CreateRevisionDimensions",BindingFlags.Instance|BindingFlags.NonPublic);
            Func<string> create=()=>Convert.ToString(method.Invoke(form,null));
            var type=assembly.GetType("Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm",true);
            using(var revision=(Form)Activator.CreateInstance(type,new object[]{false,create}))
            {
                type.GetMethod("CreateRevisionDrawing",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(revision,null);
                string detail=((Label)type.GetField("revisionDetail",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(revision)).Text;
                string status=((Label)type.GetField("revisionStatus",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(revision)).Text;
                File.WriteAllText(Path.Combine(folder,"result.txt"),detail+"\r\n"+status);Console.WriteLine(detail+"\r\n"+status);
                RevisionTests.ShowPreview(revision);
                using(var bitmap=new Bitmap(revision.Width,revision.Height))
                {revision.DrawToBitmap(bitmap,new Rectangle(Point.Empty,revision.Size));bitmap.Save(Path.Combine(folder,"revision-result.png"));}
                if(!detail.Contains("đã Check REV"))throw new Exception("Production flow did not complete: "+detail);
            }
        }
    }
}
