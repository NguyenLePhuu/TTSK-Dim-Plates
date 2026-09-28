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
        if (args.Length == 1 && args[0] == "--review-selected") { LiveTests.Run(true); return; }
        if (args.Length == 1 && args[0] == "--benchmark-main") { LiveTests.Benchmark(); return; }
        if (args.Length == 1 && args[0] == "--profile-main") { LiveTests.ProfileRead(); return; }
        if (args.Length == 2 && args[0] == "--restore-main-origins") { LiveTests.RestoreMainOrigins(args[1]); return; }
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
        sections[0].LocalAxes = frontAxes; sections[0].LongitudinalCoverage = .90;
        sections[1].LocalAxes = topAxes; sections[1].LongitudinalCoverage = .90; sections[1].LongitudinalAxisIndex = 1;
        Check(R.IsArrangeMainView(sections[0]) && R.IsArrangeMainView(sections[1]),
            "longitudinal section views are eligible main views");
        Check(R.MatchArrangeViews(baseline, sections).SequenceEqual(new[] {0,1}),
            "longitudinal sections can replace matching main views");
        Check(R.MatchArrangeViews(sections, baseline).SequenceEqual(new[] {0,1}),
            "longitudinal sections can serve as reference main views");
        Check(R.MatchArrangeViews(baseline, sections.Reverse().ToArray()).SequenceEqual(new[] {1,0}),
            "longitudinal section mapping independent of enumeration order");
        var verticalMainSection = V("RENAMED", "SectionView");
        verticalMainSection.LocalAxes = new double[] {0,1,0,-1,0,0}; verticalMainSection.LongitudinalCoverage = 1;
        Check(R.IsArrangeMainView(verticalMainSection), "main section can be vertical/mirrored on sheet");
        verticalMainSection.LongitudinalAxisIndex = 3;
        Check(!R.IsArrangeMainView(verticalMainSection), "invalid longitudinal axis is never guessed");
        verticalMainSection.LongitudinalAxisIndex = 0; verticalMainSection.LongitudinalCoverage = double.NaN;
        Check(!R.IsArrangeMainView(verticalMainSection), "invalid coverage is never movable");
        var transverseSection = V("A", "SectionView");
        transverseSection.LocalAxes = new double[] {0,1,0,0,0,1};
        transverseSection.LongitudinalCoverage = .99;
        Reject(() => R.MatchArrangeViews(new[] { sections[0] }, new[] { transverseSection }),
            "transverse section cannot replace a main view");
        var shortSection = V("A", "SectionView");
        shortSection.LocalAxes = frontAxes; shortSection.LongitudinalCoverage = .25;
        Reject(() => R.MatchArrangeViews(new[] { sections[0] }, new[] { shortSection }),
            "small cropped section cannot replace a main view");
        foreach (var type in new[] { "FrontView", "TopView", "BackView", "BottomView", "EndView", "ModelView", "_3DView" })
            Check(R.IsArrangeMainView(type), "main view eligible: " + type);
        foreach (var type in new[] { "DetailView", "UnknownViewType", "", null })
            Check(!R.IsArrangeMainView(type), "non-main view preserved: " + type);
        var mixedReference = baseline;
        var extraSection = V("C", "SectionView");
        extraSection.LocalAxes = frontAxes; extraSection.LongitudinalCoverage = .12;
        var mixedTarget = new[] { sections[0], baseline[1], extraSection };
        Check(R.MatchArrangeViews(mixedReference.Where(R.IsArrangeMainView).ToArray(),
            mixedTarget.Where(R.IsArrangeMainView).ToArray()).SequenceEqual(new[] { 0, 1 }),
            "different section counts and names do not affect main-view mapping");
        var preserved = V("A", "SectionView"); preserved.Id = 501;
        preserved.LocalAxes = new double[] {0,1,0,0,0,1}; preserved.LongitudinalCoverage = .99;
        preserved.Origin = new Tekla.Structures.Geometry3d.Point(100, 200, 3); preserved.Scale = 10;
        var preservedDetail = V("DETAIL", "DetailView"); preservedDetail.Id = 502;
        preservedDetail.Origin = new Tekla.Structures.Geometry3d.Point(400, 250, 0); preservedDetail.Scale = 5;
        R.VerifyPreservedArrangeViews(new[] { preserved, preservedDetail }, new[] { preservedDetail, preserved });
        Check(true, "preserved views can be verified without main-part geometry or presentation");
        var changedSection = V("A", "SectionView"); changedSection.Id = preserved.Id; changedSection.Scale = preserved.Scale;
        changedSection.Origin = new Tekla.Structures.Geometry3d.Point(101, 200, 3);
        Reject(() => R.VerifyPreservedArrangeViews(new[] { preserved }, new[] { changedSection }), "detect section displacement");
        changedSection.Origin = new Tekla.Structures.Geometry3d.Point(preserved.Origin); changedSection.Scale = 20;
        Reject(() => R.VerifyPreservedArrangeViews(new[] { preserved }, new[] { changedSection }), "detect section scale change");
        Reject(() => R.VerifyPreservedArrangeViews(new[] { preserved }, new R.ArrangeReferenceView[0]), "detect missing section");
        Reject(() => R.VerifyPreservedArrangeViews(new[] { preserved, preservedDetail }, new[] { preserved, preserved }), "detect duplicated preserved IDs");
        var mirrored = V("FRONT", "FrontView"); mirrored.LocalAxes = new double[] {-1,0,0,0,0,1};
        Reject(() => R.MatchArrangeViews(new[] { baseline[0] }, new[] { mirrored }), "same name/type cannot override incompatible orientation");
        Reject(() => R.MatchArrangeViews(new[] { baseline[0],baseline[0] }, new[] { baseline[0],baseline[0] }), "ambiguous orientation rejected");
        var invalidAxes = V("TOP","TopView"); invalidAxes.LocalAxes = new double[] {double.NaN,0,0,0,1,0};
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
