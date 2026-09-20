using System;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

class Program
{
    static int count;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    static R.ArrangeReferenceView V(string name, string type)
    { return new R.ArrangeReferenceView { Name = name, Type = type }; }
    static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--live-main") { LiveTests.Run(); return; }
        if (args.Length == 1 && (args[0] == "--audit-selected" || args[0] == "--audit-layout-selected"))
        {
            var model = new Tekla.Structures.Model.Model();
            var handler = new Tekla.Structures.Drawing.DrawingHandler();
            Console.WriteLine("Model=" + model.GetConnectionStatus() + " Drawing=" + handler.GetConnectionStatus());
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new Exception("No connection");
            var active = handler.GetActiveDrawing();
            Console.WriteLine("Active=" + (active == null ? "none" : active.Mark));
            var selected = handler.GetDrawingSelector().GetSelected(); int n = 0;
            var drawings = new System.Collections.Generic.List<Tekla.Structures.Drawing.Drawing>();
            while (selected.MoveNext())
            {
                var d = selected.Current;
                drawings.Add(d);
                Console.WriteLine(++n + " " + d.Mark + " Locked=" + d.IsLocked + " Frozen=" + d.IsFrozen
                    + " Issued=" + d.IsIssued + " Ready=" + d.IsReadyForIssue);
            }
            if (args[0] == "--audit-layout-selected")
            {
                if (active != null) throw new Exception("Close drawing before read-only layout audit");
                R.ArrangeReference reference = null;
                foreach (var d in drawings)
                {
                    if (!handler.SetActiveDrawing(d, false)) throw new Exception("Cannot open " + d.Mark);
                    try
                    {
                        if (!handler.GetActiveDrawing().IsSameDatabaseObject(d)) throw new Exception("Wrong active drawing");
                        var current = R.CaptureArrangeReference();
                        if (reference == null) reference = current;
                        foreach (var v in current.Views)
                            Console.WriteLine("VIEW " + d.Mark + " " + v.Name + " " + v.Type + " bounds=" + v.Part.MinX + "," + v.Part.MaxX + "," + v.Part.MinY + "," + v.Part.MaxY
                                + " localAxes=" + String.Join(",", v.LocalAxes.Select(x=>x.ToString("F4"))));
                        try
                        {
                        var targets = R.PlanArrangeReference(reference, current);
                        for (int i = 0; i < targets.Count; i++)
                            Console.WriteLine(d.Mark + " " + current.Views[i].Name + " " + current.Views[i].Type
                                + " origin=" + current.Views[i].Origin + " target=" + targets[i] + " scale=" + current.Views[i].Scale);
                        Console.WriteLine("PASS read-only plan " + d.Mark);
                        }
                        catch (Exception ex) { Console.WriteLine("PLAN ERROR " + d.Mark + ": " + ex.Message); }
                    }
                    finally
                    {
                        var now = handler.GetActiveDrawing();
                        if (now == null || !now.IsSameDatabaseObject(d) || !handler.CloseActiveDrawing(false))
                            throw new Exception("Cannot close owned drawing");
                    }
                }
            }
            return;
        }
        for (int flags = 0; flags < 16; flags++)
        {
            var drawing = (Tekla.Structures.Drawing.SinglePartDrawing)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Tekla.Structures.Drawing.SinglePartDrawing));
            drawing.IsLocked = (flags & 1) != 0;
            drawing.IsFrozen = (flags & 2) != 0;
            typeof(Tekla.Structures.Drawing.Drawing).GetProperty("IsIssued").GetSetMethod(true).Invoke(drawing, new object[] {(flags & 4) != 0});
            drawing.IsReadyForIssue = (flags & 8) != 0;
            if (drawing.IsLocked) Reject(() => R.ValidateArrangeEditability(drawing), "locked flags=" + flags);
            else { R.ValidateArrangeEditability(drawing); Check(true, "editable Frozen/Issued/Ready flags=" + flags); }
            Check(drawing.IsFrozen == ((flags & 2) != 0) && drawing.IsIssued == ((flags & 4) != 0)
                && drawing.IsReadyForIssue == ((flags & 8) != 0), "lifecycle flags unchanged=" + flags);
        }
        var baseline = new[] { V("FRONT", "FrontView"), V("TOP", "TopView") };
        Check(R.MatchArrangeViews(baseline, baseline.Reverse().ToArray()).SequenceEqual(new[] {1,0}), "shuffled view order");
        Check(R.MatchArrangeViews(baseline, new[] { V("RENAMED", "FrontView"), V("TOP", "TopView") }).SequenceEqual(new[] {0,1}), "unique type fallback");
        Reject(() => R.MatchArrangeViews(baseline, baseline.Take(1).ToArray()), "missing view rejects drawing");
        Reject(() => R.MatchArrangeViews(new[] {V("A","SectionView"),V("A","SectionView")}, new[] {V("A","SectionView"),V("A","SectionView")}), "duplicate aliases reject");
        Reject(() => R.MatchArrangeViews(baseline, new[] { V("FRONT", "FrontView"), V("FRONT", "FrontView") }), "one-to-one mapping required");
        Reject(() => R.MatchArrangeViews(new[] { V("A", "UnknownViewType") }, new[] { V("B", "UnknownViewType") }), "unknown type cannot fall back");
        var topAxes = new double[] {1,0,0,0,1,0};
        var frontAxes = new double[] {1,0,0,0,0,1};
        baseline[0].LocalAxes = frontAxes; baseline[1].LocalAxes = topAxes;
        var sections = new[] { V("B", "SectionView"), V("A", "SectionView") };
        sections[0].LocalAxes = frontAxes; sections[1].LocalAxes = topAxes;
        Check(R.MatchArrangeViews(baseline, sections).SequenceEqual(new[] {0,1}), "real B-C150 Section B/A maps to Front/Top");
        Check(R.MatchArrangeViews(baseline, sections.Reverse().ToArray()).SequenceEqual(new[] {1,0}), "section order is irrelevant");
        Check(R.MatchArrangeViews(sections, baseline).SequenceEqual(new[] {0,1}), "Section drawing can be reference");
        var mirrored = V("FRONT", "FrontView"); mirrored.LocalAxes = new double[] {-1,0,0,0,0,1};
        Reject(() => R.MatchArrangeViews(new[] { baseline[0] }, new[] { mirrored }), "same name/type cannot override incompatible orientation");
        Reject(() => R.MatchArrangeViews(new[] { baseline[0],baseline[0] }, new[] { sections[0],sections[0] }), "ambiguous orientation rejected");
        var invalidAxes = V("A","SectionView"); invalidAxes.LocalAxes = new double[] {double.NaN,0,0,0,1,0};
        Reject(() => R.MatchArrangeViews(new[] { baseline[1] }, new[] { invalidAxes }), "nonfinite orientation rejected");
        var old = new R.SnapshotView { X=120, Y=80 };
        var before = new R.RevisionPartBounds { MinX=-30, MaxX=30, MinY=-10, MaxY=10 };
        var after = new R.RevisionPartBounds { MinX=10, MaxX=110, MinY=20, MaxY=40 };
        var target = R.RevisionBalancedOrigin(old,before,after,3);
        Check(Math.Abs(target.X+60-120)<.001 && Math.Abs(target.Y+20-70)<.001 && target.Z==3, "resized translated part keeps centre and bottom on paper");
        var again = R.RevisionBalancedOrigin(old,before,after,3);
        Check(!R.RevisionPositionDiffers(target,again), "second run is idempotent");
        if (args.Length > 0)
        {
            using(var form = new TTSK_AutoDim_Plates.MainForm())
            {
                form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-30000,-30000);
                form.Show(); Application.DoEvents();
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(TTSK_AutoDim_Plates.MainForm).GetMethod("OpenArrangeViewPanel",flags).Invoke(form,null);
                var until=DateTime.UtcNow.AddSeconds(1);
                while(DateTime.UtcNow<until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                var panel=(Panel)typeof(TTSK_AutoDim_Plates.MainForm).GetField("slideArrangePanel",flags).GetValue(form);
                var buttons=panel.Controls.Cast<Control>().Where(c=>c.Text=="Theo bản mẫu").ToList();
                Check(buttons.Count==1 && buttons[0].Visible && buttons[0].Bottom<=panel.Height,"single visible reference button fits panel");
                using(var bitmap=new Bitmap(form.Width,form.Height))
                { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); bitmap.Save(args[0]); }
            }
        }
        Console.WriteLine(count+" checks passed");
    }
}
