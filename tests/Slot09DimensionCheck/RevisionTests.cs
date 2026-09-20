using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using R = Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;
using C = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;
using F = Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm;

static class RevisionTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window,int command);
    public static void ShowPreview(Form form)
    {
        form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);
        ShowWindow(form.Handle,4);Application.DoEvents();form.PerformLayout();
    }
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    static R.Segment Segment(int id,double a,double b,double value,double x=1,double y=0)
    { return new R.Segment { Id=id,A=a,B=b,Value=value,Text=value.ToString(System.Globalization.CultureInfo.InvariantCulture),X=x,Y=y }; }
    public static void Run()
    {
        double value;
        Assert(R.TryNumber(" 607,5 ",out value)&&value==607.5,"Decimal comma");
        foreach(string unsupported in new[]{"R 10","2*50=100","100 ± 1","1/2","切り欠き","NaN","Infinity","0","1,234,567"})
            Assert(!R.TryNumber(unsupported,out value),"Unsupported numeric format: "+unsupported);
        RevisionFeatureTests.Run();
        SessionChoices();
    }
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Invoke(F form,string name,params object[] args)
    {return typeof(F).GetMethod(name,Private).Invoke(form,args);}
    static object Field(F form,string name) {return typeof(F).GetField(name,Private).GetValue(form);}
    static void SetField(F form,string name,object value) {typeof(F).GetField(name,Private).SetValue(form,value);}
    static void SessionChoices()
    {
        string folder=Path.Combine(Path.GetTempPath(),"TTSK-Revision-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string baseline=Path.Combine(folder,"sample.dpm");
        string choicesPath=baseline+".choices.tsv";
        try
        {
            File.WriteAllText(choicesPath,"1\t"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("sample"))+"\t"+
                (int)Tekla.Structures.Drawing.DrawingColors.Blue+"\t1\r\n");
            using(var form=new F(new C.Result {Drawing="Offline"},false))
            {
                var result=new R.Result {BaselinePath=baseline,BaselineHash="hash"};
                SetField(form,"revisionResult",result);
                Invoke(form,"LoadRevisionChoices");
                var choices=(System.Collections.IDictionary)Field(form,"revisionChoices");
                object choice=choices[1];var type=choice.GetType();
                Assert(!(bool)type.GetField("Excluded").GetValue(choice),"Legacy persisted exclusion must not disable new session");
                Assert((Tekla.Structures.Drawing.DrawingColors)type.GetField("Original").GetValue(choice)==Tekla.Structures.Drawing.DrawingColors.Blue,"Original custom color preserved");
                type.GetField("Excluded").SetValue(choice,true);
                Invoke(form,"LoadRevisionChoices");
                Assert((bool)type.GetField("Excluded").GetValue(choices[1]),"F5 retains session exclusion");
                Invoke(form,"SaveRevisionChoices");
                Assert(File.ReadAllLines(choicesPath)[0].EndsWith("\t0"),"Exclusion must never persist to disk");
                Invoke(form,"BuildRevisionPanel");Invoke(form,"CloseRevision");Invoke(form,"LoadRevisionChoices");
                Assert(!(bool)type.GetField("Excluded").GetValue(choices[1]),"Reopen clears session exclusion");
                type.GetField("Excluded").SetValue(choices[1],true);
                result.BaselineHash="replacement";Invoke(form,"LoadRevisionChoices");
                Assert(!(bool)type.GetField("Excluded").GetValue(choices[1]),"Changed baseline clears session exclusion");
                var manual=new R.Item {Review=true,Before="?",After="412.1",Signature="valid",Segments=new List<R.Segment>{Segment(1,0,412.1,412.1)}};
                Assert((bool)Invoke(form,"CanToggleRevision",manual),"Readable unmatched DIM allows explicit red");
                Assert(!(bool)Invoke(form,"CanAutoRevision",manual),"Unmatched DIM remains excluded from automatic red");
                manual.Signature=null;
                Assert(!(bool)Invoke(form,"CanToggleRevision",manual),"Unreadable DIM cannot be written");
            }
        }
        finally {File.Delete(choicesPath);Directory.Delete(folder);}
        Console.WriteLine("PASS Revision session: legacy exclusion reset, original color, F5, reopen, baseline change, manual eligibility and unreadable guard.");
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach(Control child in parent.Controls)
        {yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    public static void OfflinePreview(string folder)
    {
        Directory.CreateDirectory(folder);Application.EnableVisualStyles();
        foreach(bool dark in new[]{false,true})foreach(int width in new[]{1000,1180})
        using(var form=new F(new C.Result {Drawing="Offline preview"},dark))
        {
            form.ClientSize=new Size(width,730);ShowPreview(form);Invoke(form,"BuildRevisionPanel");
            var result=new R.Result {Unchanged=2};
            string[] before={"688.2","DIM mới","?","DIM mới"};
            string[] after={"690.2","20","412.1","101"};
            var choices=(System.Collections.IDictionary)Field(form,"revisionChoices");
            var choiceType=typeof(F).GetNestedType("RevisionChoice",BindingFlags.NonPublic);
            for(int i=0;i<4;i++)
            {
                var item=new R.Item {Id=i+1,View=i==0?"Hình chiếu bằng":"Hình chiếu đứng",Before=before[i],After=after[i],
                    AlreadyRed=i!=2,Changed=i!=2,IsNew=i==1||i==3,Review=i==2,Signature="sample"+i,Reason="Chi tiết đối chiếu snapshot"};
                item.Segments.Add(Segment(i+1,0,100,100));result.Items.Add(item);
                object choice=Activator.CreateInstance(choiceType,true);choiceType.GetField("Signature").SetValue(choice,item.Signature);choices[item.Id]=choice;
            }
            result.Items.Add(new R.Item{Id=-1,ReadOnly=true,Changed=true,View="Mark · chỉ thông báo",Before="BmC-117 / C150*75*20*3.2",After="BmC-118 / C150*75*20*3.2",Reason="Nội dung mark thay đổi · người dùng tự chỉnh thuộc tính."});
            result.Items.Add(new R.Item{Id=-2,ReadOnly=true,Changed=true,View="Bảng thuộc tính · chỉ thông báo",Before="B-C150-230 | 1 | BPU-049",After="B-C150-230 | 1 | BPU-049 / B-C150-231 | 2 | BPU-050",Reason="Snapshot: 1 hàng; hiện hành: 2 hàng. Chỉ thông báo, không đổi thuộc tính."});
            SetField(form,"revisionResult",result);
            ((Label)Field(form,"revisionSource")).Text="単品図(形鋼) / [BmC-136]";
            ((Label)Field(form,"revisionSnapshot")).Text="Snapshot · 27/04/2026 09:48";
            Invoke(form,"RenderRevisionCards");Invoke(form,"UpdateRevisionSummary");
            form.PerformLayout();
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"revision-"+(dark?"dark":"light")+"-"+width+".png"));}
            var cards=(FlowLayoutPanel)Field(form,"revisionCards");
            foreach(var label in Descendants(cards).OfType<Label>().Where(l=>l.Text.StartsWith("Hình chiếu")))
                Assert(label.Height>=TextRenderer.MeasureText(label.Text,label.Font).Height,"View caption clipped at width "+width);
            var manualCard=cards.Controls[2];
            Assert(Descendants(manualCard).OfType<Button>().Any(b=>b.Text=="Bật lại Revision"&&b.Enabled),"Manual DIM has enabled red button");
            Assert(!((Label)Field(form,"revisionDetail")).Visible,"No redundant footer details");
            Assert(!Descendants(cards.Controls[4]).OfType<Button>().Any(b=>b.Visible),"Read-only mark card has no colour action");
            Assert(!(bool)Invoke(form,"CanToggleRevision",result.Items[4])&&!(bool)Invoke(form,"CanAutoRevision",result.Items[4]),"Read-only content cannot be coloured");
            var delete=Descendants(form).OfType<Button>().Single(b=>b.Name=="revisionDelete");
            Assert(delete.Visible&&delete.Right<=delete.Parent.ClientSize.Width,"Delete REV visible at every tested width");
            Assert(((Button)Field(form,"revisionNext")).Enabled,"Pending review navigation enabled");
            result.Items[2].AlreadyRed=true;Invoke(form,"UpdateRevisionSummary");
            Assert(((Button)Field(form,"revisionNext")).Enabled,"Already-red DIMs remain navigable even with no pending review");
            Invoke(form,"SetRevisionFilter",1);
            Assert(((System.Collections.IList)Invoke(form,"NavigableRevisionItems")).Count==1,"Changed-value filter excludes new DIMs and notices");
            Invoke(form,"SetRevisionFilter",2);
            Assert(((System.Collections.IList)Invoke(form,"NavigableRevisionItems")).Count==2,"New-DIM filter contains both new DIMs");
            Invoke(form,"SetRevisionFilter",4);
            Assert(!((Button)Field(form,"revisionNext")).Enabled,"Empty filter disables navigation");
            Invoke(form,"SetRevisionFilter",0);
            for(int i=0;i<10;i++)cards.Controls.Add(new Panel {Height=108});
            Invoke(form,"ResizeRevisionCards");
            var scroll=Field(form,"revisionScroll");
            scroll.GetType().GetMethod("Reveal").Invoke(scroll,new object[]{cards.Controls[5]});
            using(var bitmap=new Bitmap(form.Width,form.Height))
            {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(folder,"revision-content-"+(dark?"dark":"light")+"-"+width+".png"));}
            scroll.GetType().GetMethod("Reveal").Invoke(scroll,new object[]{cards.Controls[cards.Controls.Count-1]});
            var viewport=(Panel)Field(form,"revisionViewport");
            Assert(cards.Top<0&&cards.Top+cards.Controls[cards.Controls.Count-1].Bottom<=viewport.ClientSize.Height,"Next review scroll reaches last card");
        }
        Console.WriteLine("PASS offline Revision UI: both themes and widths, unclipped captions, manual red, concise footer, next-review state and scroll.");
    }
    public static void Live(string output)
    {
        var result=R.Analyze(false);
        File.WriteAllText(output,result.Report());
        Assert(result.Drawing.Mark=="[BmC-136]","Golden drawing changed; stop read-only test");
        Assert(result.Compared==6&&result.Unchanged==4&&result.Items.Count==2,"Golden comparison counts");
        Assert(result.Items.All(i=>i.Before=="607"&&i.After=="784"&&i.Changed&&i.AlreadyRed),"Golden revised dimensions");
        // No black DIM is written: exercise idempotence and stale-input rejection on the red sample.
        Assert(R.Apply(result,result.Items)==0,"Already red must be a no-op");
        string signature=result.Items[0].Signature; result.Items[0].Signature="stale";
        bool rejected=false; try { R.Apply(result,result.Items); } catch(InvalidOperationException) { rejected=true; }
        finally { result.Items[0].Signature=signature; }
        Assert(rejected,"Stale report must reject before writes");
        var repeated=R.Analyze(false);
        Assert(result.Report()==repeated.Report(),"Read-only repeat changed drawing/baseline");
        Console.WriteLine("PASS live Revision: two 607 -> 784, four unchanged, red idempotence, stale preflight, repeated read-only audit.");
    }
    public static void Preview(string folder)
    {
        Directory.CreateDirectory(folder); Application.EnableVisualStyles();
        var source=C.Analyze();
        foreach(bool dark in new[]{true,false})
        using(var form=new F(source,dark))
        {
            form.Show(); Application.DoEvents();
            typeof(F).GetMethod("OpenRevision",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form,null);
            Application.DoEvents();
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var cards=(FlowLayoutPanel)typeof(F).GetField("revisionCards",flags).GetValue(form);
            var result=(R.Result)typeof(F).GetField("revisionResult",flags).GetValue(form);
            Assert(result.Items.Count>=2&&cards.Controls.Count==result.Items.Count,"Revision cards must list current findings");
            Assert(result.Items.Where(i=>!i.ReadOnly).All(i=>i.AlreadyRed),"Automatic red");
            typeof(F).GetMethod("SelectRevision",flags).Invoke(form,new object[]{result.Items[0]});
            Assert((int)typeof(F).GetField("revisionSelected",flags).GetValue(form)==result.Items[0].Id,"Single click selection");
            if(dark)
            {
                var item=result.Items[0];
                try
                {
                    typeof(F).GetMethod("ToggleRevision",flags).Invoke(form,new object[]{item});
                    Assert(item.Dimension.Select()&&R.GetColor(item)==Tekla.Structures.Drawing.DrawingColors.Black,"Disable restores black");
                    typeof(F).GetMethod("RefreshRevision",flags).Invoke(form,null);
                    result=(R.Result)typeof(F).GetField("revisionResult",flags).GetValue(form);
                    item=result.Items.Single(i=>i.Id==item.Id);
                    Assert(!item.AlreadyRed,"Exclusion survives refresh");
                    typeof(F).GetMethod("CloseRevision",flags).Invoke(form,null);
                    typeof(F).GetMethod("OpenRevision",flags).Invoke(form,null);
                    result=(R.Result)typeof(F).GetField("revisionResult",flags).GetValue(form);
                    item=result.Items.Single(i=>i.Id==item.Id);
                    Assert(item.AlreadyRed,"Reopen resets exclusion and automatically restores red");
                    typeof(F).GetMethod("ToggleRevision",flags).Invoke(form,new object[]{item});
                    typeof(F).GetMethod("ToggleRevision",flags).Invoke(form,new object[]{item});
                    Assert(item.Dimension.Select()&&R.GetColor(item)==Tekla.Structures.Drawing.DrawingColors.Red,"Enable writes and reads red");
                }
                finally
                {
                    if(item.Dimension.Select()&&R.GetColor(item)!=Tekla.Structures.Drawing.DrawingColors.Red)
                        R.SetColor(result,new[]{item},Tekla.Structures.Drawing.DrawingColors.Red);
                }
            }
            Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height))
            { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(Path.Combine(folder,dark?"revision-dark.png":"revision-light.png")); }
            cards=(FlowLayoutPanel)typeof(F).GetField("revisionCards",flags).GetValue(form);
            for(int i=0;i<10;i++)cards.Controls.Add(new Panel {Height=88,Margin=new Padding(0,0,0,8)});
            typeof(F).GetMethod("ResizeRevisionCards",flags).Invoke(form,null);
            var scroll=typeof(F).GetField("revisionScroll",flags).GetValue(form);
            scroll.GetType().GetMethod("OnMouseWheel",flags).Invoke(scroll,new object[]{new MouseEventArgs(MouseButtons.None,0,0,0,-120)});
            Assert(cards.Location.Y<0&&!cards.AutoScroll,"Custom themed wheel scroll without native scrollbar");
            scroll.GetType().GetMethod("MoveTo",flags).Invoke(scroll,new object[]{Int32.MaxValue});
            var viewport=(Panel)typeof(F).GetField("revisionViewport",flags).GetValue(form);
            Assert(cards.Bottom==viewport.ClientSize.Height,"Scroll clamps to final card");
            scroll.GetType().GetMethod("MoveTo",flags).Invoke(scroll,new object[]{0});
            Assert(cards.Top==0,"Scroll returns to top");
            form.Close();
        }
        Console.WriteLine("PASS Revision cards: both themes, selection, automatic red, toggle black/red, session exclusion across refresh, reset on reopen.");
    }
}


