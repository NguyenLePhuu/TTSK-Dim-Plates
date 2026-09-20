using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using B=Tekla.Technology.Akit.UserScript.PHU_DimensionBatch;
using C=Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;
using F=Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm;

static class BatchTests
{
    static int assertions;
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception("Batch: "+message);}
    static B.Entry Entry(int id,string name=null){return new B.Entry {Key="id-"+id,Number=id,Name=name??"[B-"+id+"]"};}
    static C.Result Result(int errors=0,int reviews=0,bool warning=false)
    {
        var result=new C.Result {Drawing="Sample",Dimensions=35,Views=4};
        for(int i=0;i<errors+reviews;i++)result.Findings.Add(new C.Finding {Status=i<errors?"ERROR":"REVIEW",Dimension=i/2,View=1,
            ViewLabel="Hình chiếu đứng",DimensionLabel="1250 mm",Location="Chuỗi phía trên · chân bên phải",Reason="Không tìm thấy"});
        if(warning)result.Warnings.Add("Hình chiếu phụ: chưa đọc đủ hình học.");
        return result;
    }
    sealed class Fake:B.IAdapter
    {
        public string Active;
        public bool Connected=true,ModelMatches=true,FailClose;
        public Action<B.Entry> OnOpen,OnAnalyze;
        public readonly Dictionary<string,C.Result> Results=new Dictionary<string,C.Result>();
        public readonly List<string> Calls=new List<string>();
        public void EnsureContext(){if(!Connected||!ModelMatches)throw new B.ContextException("Model/kết nối đã thay đổi.");}
        public void RequireClosed(){EnsureContext();if(Active!=null)throw new B.ContextException("Đóng bản vẽ đang mở.");}
        public void Open(B.Entry entry,bool visible)
        {
            EnsureContext();if(Active!=null&&!(visible&&Active==entry.Key))throw new B.ContextException("Bản vẽ khác đang mở.");
            Calls.Add("open:"+entry.Key);if(OnOpen!=null)OnOpen(entry);Active=entry.Key;
        }
        public void CloseOwned(B.Entry entry)
        {
            EnsureContext();if(Active==null)return;if(Active!=entry.Key)throw new B.ContextException("Không đóng bản vẽ khác.");
            if(FailClose)throw new InvalidOperationException("Close failed");Calls.Add("close:"+entry.Key);Active=null;
        }
        public C.Result Analyze(B.Entry entry)
        {
            EnsureContext();if(Active!=entry.Key)throw new B.ContextException("Sai bản vẽ.");Calls.Add("analyze:"+entry.Key);
            if(OnAnalyze!=null)OnAnalyze(entry);EnsureContext();if(Active!=entry.Key)throw new B.ContextException("Bản vẽ thay đổi.");
            return Results.ContainsKey(entry.Key)?Results[entry.Key]:Result();
        }
        public void ValidateActive(B.Entry entry)
        {EnsureContext();if(Active!=entry.Key)throw new B.ContextException("Sai bản vẽ.");}
    }
    static void Finish(B.Session session){int guard=0;while(session.Running&&guard++<100)session.Step();Check(guard<100,"finite queue");}
    public static void Run()
    {
        var same1=Entry(1,"[Trùng mã]");var same2=Entry(2,"[Trùng mã]");var warning=Entry(3);var failed=Entry(4);var afterFailure=Entry(5);var unsupported=Entry(6);unsupported.Supported=false;
        var adapter=new Fake();adapter.Results[same1.Key]=Result(2);adapter.Results[same2.Key]=Result();adapter.Results[warning.Key]=Result(0,0,true);
        adapter.OnOpen=e=>{if(e==failed)throw new InvalidOperationException("Locked or unavailable");};
        var session=new B.Session(new[]{same1,same2,warning,failed,afterFailure,unsupported},adapter);
        Check(session.Start(session.Entries),"start");Finish(session);
        Check(same1.Status==B.State.NeedsAttention&&same1.Errors==2,"real findings classified separately");
        Check(same2.Status==B.State.Passed&&same2.Errors==0,"duplicate names do not mix results");
        Check(warning.Status==B.State.NeedsReview,"coverage failure is not a pass");
        Check(failed.Status==B.State.Failed&&failed.Errors==0&&failed.Snapshot==null,"execution failure is not a DIM defect");
        Check(failed.Problem.Contains("Không mở")&&failed.Recovery.Length>0,"stage-specific useful recovery");
        Check(afterFailure.Status==B.State.Passed,"continue after one drawing fails");
        Check(unsupported.Status==B.State.Unsupported&&!adapter.Calls.Contains("open:"+unsupported.Key),"unsupported skip before open");
        Check(unsupported.NeedsWork,"unsupported drawings remain visible among work requiring human review");
        Check(adapter.Active==null,"owned drawings closed");
        Check(same1.Snapshot.Objects.Count==0&&same1.Snapshot.Features.Count==0&&same1.Snapshot.SourceDrawing==null,"no cached live object handles or geometry graphs");
        string export=session.ExportText();
        foreach(var entry in session.Entries)Check(export.Contains(entry.Key)&&export.Contains(entry.StatusText),"export includes each identity/state");
        Check(export.Contains("Cách xử lý")&&export.Contains("Hình chiếu đứng"),"export contains actionable details");
        int cleanRuns=adapter.Calls.Count(c=>c=="analyze:"+same2.Key);
        adapter.OnOpen=null;Check(session.Start(new[]{failed}),"retry just failed drawing");Finish(session);
        Check(failed.Status==B.State.Passed&&adapter.Calls.Count(c=>c=="analyze:"+same2.Key)==cleanRuns,"retry isolated");
        var old=same2.Snapshot;adapter.Active="user-unsaved";
        Check(!session.Start(session.Entries)&&same2.Snapshot==old&&adapter.Active=="user-unsaved","active drawing blocks batch without closing or clearing results");
        adapter.Active=null;
        session.Start(session.Entries);session.Step();session.Pause();
        Check(same1.Status==B.State.NeedsAttention&&same2.Status==B.State.Pending,"stop preserves completed and pending");
        int firstRuns=adapter.Calls.Count(c=>c=="analyze:"+same1.Key);
        session.Start(session.Entries.Where(e=>e.Status==B.State.Pending));Finish(session);
        Check(adapter.Calls.Count(c=>c=="analyze:"+same1.Key)==firstRuns,"resume does not rerun completed drawings");
        var next=Entry(8);var changing=Entry(7);var changedAdapter=new Fake();
        var changedSession=new B.Session(new[]{changing,next},changedAdapter);
        changedSession.Start(changedSession.Entries);changedAdapter.Connected=false;changedSession.Step();
        Check(!changedSession.Running&&changing.Status==B.State.Pending&&changedAdapter.Calls.Count==0,"connection loss before entry leaves queue untouched");
        changedAdapter.Connected=true;changedSession.Start(changedSession.Entries);
        changedAdapter.OnAnalyze=e=>changedAdapter.ModelMatches=false;changedSession.Step();
        Check(!changedSession.Running&&changing.Status==B.State.Failed&&next.Status==B.State.Pending,"model switch stops batch");
        Check(!changedAdapter.Calls.Any(c=>c.StartsWith("close:")),"never close a drawing after model switch");
        var wrong=new Fake();var wrongSession=new B.Session(new[]{Entry(9),Entry(10)},wrong);
        wrongSession.Start(wrongSession.Entries);wrong.OnAnalyze=e=>wrong.Active="different-user-drawing";wrongSession.Step();
        Check(!wrongSession.Running&&wrong.Active=="different-user-drawing"&&wrongSession.Entries[1].Status==B.State.Pending,"changed active drawing retained and batch paused");
        var closing=new Fake {FailClose=true};var closingSession=new B.Session(new[]{Entry(11),Entry(12)},closing);
        closingSession.Start(closingSession.Entries);closingSession.Step();
        Check(!closingSession.Running&&closingSession.Entries[0].Status==B.State.Failed&&closingSession.Entries[0].Problem.Contains("đóng"),"cleanup failure is visible and pauses");
        var broken=Entry(20);var following=Entry(21);var analysisAdapter=new Fake();
        analysisAdapter.OnAnalyze=e=>{if(e==broken)throw new InvalidOperationException("Analysis failed on only this drawing");};
        var analysisSession=new B.Session(new[]{broken,following},analysisAdapter);analysisSession.Start(analysisSession.Entries);Finish(analysisSession);
        Check(broken.Status==B.State.Failed&&following.Status==B.State.Passed&&analysisAdapter.Active==null,"analysis failure closes owned drawing and continues");
        var partial=Entry(22);var partialAdapter=new Fake();partialAdapter.OnOpen=e=>{partialAdapter.Active=e.Key;throw new InvalidOperationException("Open returned failure after activation");};
        var partialSession=new B.Session(new[]{partial},partialAdapter);partialSession.Start(partialSession.Entries);Finish(partialSession);
        Check(partialAdapter.Active==null&&partial.Status==B.State.Failed,"partially successful open still closes only the owned drawing");
        var detail=Entry(13);var detailAdapter=new Fake();var detailSession=new B.Session(new[]{detail},detailAdapter);
        detailSession.Start(detailSession.Entries);Finish(detailSession);
        detailAdapter.Results[detail.Key]=Result(3,1);var fresh=detailSession.OpenForReview(detail);
        Check(detail.Errors==3&&detail.Reviews==1&&ReferenceEquals(fresh,detailAdapter.Results[detail.Key]),"detail always analyzes fresh");
        Check(detailAdapter.Active==detail.Key,"visible review stays open for edits");
        detailAdapter.Results[detail.Key]=Result();detailSession.RefreshOpen(detail);
        Check(detail.Status==B.State.Passed,"F5 updates only same batch entry");
        detailAdapter.Active="other";bool rejected=false;
        try{detailSession.EnsureReviewContext(detail);}catch(B.ContextException){rejected=true;}
        Check(rejected,"DIM selection validates model and drawing before selecting");rejected=false;
        try{detailSession.RefreshOpen(detail);}catch(B.ContextException){rejected=true;}
        Check(rejected&&detail.Status==B.State.Failed&&detail.Snapshot==null,"stale detail rejects and invalidates prior pass");
        detailAdapter.Active=null;detailAdapter.OnOpen=e=>{throw new InvalidOperationException("Cannot open");};rejected=false;
        try{detailSession.OpenForReview(detail);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&detail.Problem.Contains("Không mở"),"review open failures attach to the exact drawing");
        var zero=Entry(14);zero.Accept(new C.Result());Check(zero.Status==B.State.NeedsReview,"zero checked DIMs never green");
        var review=Entry(15);review.Accept(Result(0,2));Check(review.Status==B.State.NeedsReview&&review.Errors==0,"unproven feet are review only");
        var angled=Entry(16);var mixed=Result(2);
        C.IncludeDimension(mixed,1,77,typeof(Tekla.Structures.Drawing.AngleDimension));angled.Accept(mixed);
        Check(angled.Status==B.State.NeedsAttention&&angled.Errors==2&&angled.Snapshot.Dimensions==35,"angles cannot skip whole drawing or hide straight DIM errors");
        Check(angled.Snapshot.SkippedAngles.Count==1&&angled.Summary.Contains("Bỏ qua 1 DIM góc"),"batch keeps explicit angle count and notice");
        var cleanAngles=Result();C.IncludeDimension(cleanAngles,1,77,typeof(Tekla.Structures.Drawing.AngleDimension));angled.Accept(cleanAngles);
        Check(angled.Status==B.State.Passed&&angled.StatusText=="DIM thẳng: không lỗi"&&!angled.NeedsWork,"angle excluded from verdict; clean result explicitly scoped to straight DIMs");
        var onlyAngles=new C.Result();C.IncludeDimension(onlyAngles,1,77,typeof(Tekla.Structures.Drawing.AngleDimension));angled.Accept(onlyAngles);
        Check(angled.Status==B.State.NeedsReview&&angled.StatusText=="Không có DIM thẳng"&&angled.Snapshot.Dimensions==0,"angle-only drawing cannot pass as checked");
        Check(new B.Session(new[]{angled},new Fake()).ExportText().Contains("DIM góc chưa hỗ trợ"),"export preserves exclusion notice");
        bool duplicate=false;try{new B.Session(new[]{Entry(1),Entry(1)},new Fake());}catch(ArgumentException){duplicate=true;}
        Check(duplicate,"duplicate identities rejected");
        AutoStart();
        Console.WriteLine("PASS "+assertions+" batch assertions: per-drawing isolation, status/recovery, continuation, stop/resume, context guards, cleanup, fresh detail, retry and export.");
    }
    static void AutoStart()
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        Action<F> shown=form=>typeof(Form).GetMethod("OnShown",flags).Invoke(form,new object[]{EventArgs.Empty});
        Action<F> step=form=>typeof(F).GetMethod("RunBatchStep",flags).Invoke(form,null);
        var adapter=new Fake();var first=Entry(31);var second=Entry(32);
        var session=new B.Session(new[]{first,second},adapter);
        using(var form=new F(session,true,true))
        {
            Check(!session.Running&&adapter.Calls.Count==0,"construction does not run batch before shown");
            shown(form);
            Check(session.Running&&adapter.Calls.Count==0,"shown queues work automatically without a Start click");
            step(form);shown(form);
            Check(session.Running&&first.Status==B.State.Passed&&second.Status==B.State.Pending,"repeated shown never pauses or resets automatic work");
            typeof(F).GetMethod("StartOrPauseBatch",flags).Invoke(form,null);
            Check(!session.Running&&second.Status==B.State.Pending,"stop still works after automatic start");
        }
        using(var form=new F(session,false,true))
        {
            shown(form);step(form);
            Check(!session.Running&&second.Status==B.State.Passed&&adapter.Calls.Count(c=>c=="analyze:"+first.Key)==1,"reopening resumes pending drawings without rechecking completed ones");
        }
        using(var form=new F(session,true,true))
        {
            shown(form);step(form);step(form);
            Check(!session.Running&&adapter.Calls.Count(c=>c=="analyze:"+first.Key)==2&&adapter.Calls.Count(c=>c=="analyze:"+second.Key)==2,"reopening completed batch automatically rechecks full list");
        }
        adapter.Active="user-unsaved";var previous=first.Snapshot;int calls=adapter.Calls.Count;
        using(var form=new F(session,false,true))
        {
            shown(form);
            Check(!session.Running&&adapter.Active=="user-unsaved"&&adapter.Calls.Count==calls&&ReferenceEquals(first.Snapshot,previous),"blocked automatic start preserves user's drawing and prior results");
            Check(session.Notice.Contains("Đóng"),"blocked automatic start provides inline recovery");
            adapter.Active=null;
            typeof(F).GetMethod("StartOrPauseBatch",flags).Invoke(form,null);step(form);step(form);
            Check(!session.Running&&adapter.Calls.Count(c=>c=="analyze:"+first.Key)==3,"manual retry remains usable after automatic start is blocked");
        }
        using(var form=new F(session,false))
        {shown(form);Check(!session.Running,"preview can opt out of automatic execution");}
    }
    static IEnumerable<Control> Descendants(Control parent)
    {foreach(Control child in parent.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    public static void Preview(string folder)
    {
        Directory.CreateDirectory(folder);Application.EnableVisualStyles();
        foreach(bool dark in new[]{false,true})foreach(int width in new[]{1000,1180})
        {
            var entries=Enumerable.Range(1,8).Select(i=>Entry(i,"[BmC-"+(135+i)+"] / Dầm thép")).ToList();
            entries[0].Accept(Result(2,0,true));entries[1].Accept(Result(0,1));entries[2].Fail(new InvalidOperationException("Drawing unavailable"),"open");
            entries[3].Accept(Result());entries[4].Supported=false;entries[4].Status=B.State.Unsupported;entries[4].Problem="Chỉ hỗ trợ bản vẽ Assembly và SinglePart.";
            var session=new B.Session(entries,new Fake());session.Pause();
            using(var form=new F(session,dark))
            {
                form.ClientSize=new Size(width,730);RevisionTests.ShowPreview(form);
                var cards=Descendants(form).Single(c=>c.Name=="BatchCards");
                Check(cards.Controls.Count==8,"all drawing cards visible");
                var searchHost=Descendants(form).Single(c=>c.Name=="BatchSearchBox");
                Check(searchHost.Bottom<=searchHost.Parent.ClientSize.Height&&searchHost.Height>=30,"rounded search stays inside toolbar without clipping");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                Action<int> filter=index=>typeof(F).GetMethod("SetBatchFilter",flags).Invoke(form,new object[]{index});
                Check(!Descendants(form).Any(c=>c is ComboBox||c is ProgressBar||c.GetType().Name=="BatchProgress"),"no native white filter or progress line");
                filter(1);Check(cards.Controls.Count==4,"needs-work filter includes unsupported manual review");
                using(var bitmap=new Bitmap(form.Width,form.Height))
                {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"batch-"+(dark?"dark":"light")+"-"+width+".png"));}
                var search=Descendants(form).OfType<TextBox>().Single();search.Text="137";
                Check(search.BorderStyle==BorderStyle.None&&search.Parent.GetType().Name=="BatchSearchBox","search uses themed rounded host without native square border");
                Check(cards.Controls.Count==1&&ReferenceEquals(cards.Controls[0].Tag,entries[1]),"search exact drawing");
                search.Text="no-match";Check(cards.Controls.Count==0,"empty filter safe");
                var clear=Descendants(form).Single(c=>c.Name=="ClearBatchSearch");
                Check(clear.Visible,"clear action visible while searching");
                typeof(Control).GetMethod("OnClick",flags).Invoke(clear,new object[]{EventArgs.Empty});
                Check(search.Text.Length==0&&cards.Controls.Count==4&&!clear.Visible,"clear action restores filtered drawings");filter(0);
                typeof(F).GetMethod("StartOrPauseBatch",flags).Invoke(form,null);
                Check(session.Running&&entries[5].Status==B.State.Pending,"start remaining only");
                typeof(F).GetMethod("RunBatchStep",flags).Invoke(form,null);
                Check(entries[5].Status==B.State.Passed&&entries[0].Errors==2,"timer updates one entry without changing previous results");
                typeof(F).GetMethod("StartOrPauseBatch",flags).Invoke(form,null);
                Check(!session.Running&&entries[6].Status==B.State.Pending,"pause button retains queue");
                form.Close();
            }
            var mixed=Result(2);C.IncludeDimension(mixed,1,77,typeof(Tekla.Structures.Drawing.AngleDimension));
            using(var form=new F(mixed,dark))
            {
                form.ClientSize=new Size(width,730);RevisionTests.ShowPreview(form);
                Check(Descendants(form).Single(c=>c.Name=="FindingCards").Controls.Count==2,"mixed drawing still renders every straight DIM error card");
                Check(Descendants(form).OfType<Label>().Any(l=>l.Text.Contains("Bỏ qua 1 DIM góc")),"mixed drawing explicitly displays angular exclusion");
                using(var bitmap=new Bitmap(form.Width,form.Height))
                {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"angular-mixed-"+(dark?"dark":"light")+"-"+width+".png"));}
                form.Close();
            }
        }
        Console.WriteLine("PASS batch UI: 4 previews, all/needs-work/search/empty filters, one-entry progress and stop; no Tekla connection.");
    }
}
