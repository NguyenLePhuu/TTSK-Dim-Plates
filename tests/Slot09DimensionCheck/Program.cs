using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using G=Tekla.Structures.Geometry3d;
using C=Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;
using N=Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;
class Program
{
    static int count;
    static C.Feature F(int owner,string kind,double x,double y,double bx,double by,bool allowed)
    { return new C.Feature { Owner=owner,Kind=kind,A=new G.Point(x,y,0),B=new G.Point(bx,by,0),Allowed=allowed }; }
    static void Check(string name,string expected,G.Point point,G.Vector normal,List<C.Feature> fs)
    {
        var actual=C.Classify(point,normal,fs);
        if(actual.Status!=expected) throw new Exception(name+": expected "+expected+", got "+actual);
        count++; Console.WriteLine("PASS "+name);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==2 && args[0]=="--hardening-live")
            { HardeningTests.Live(args[1]); return 0; }
            if(args.Length==2 && args[0]=="--beam-live")
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                var result=C.Analyze();
                timer.Stop(); Console.WriteLine("Live analysis elapsed="+timer.ElapsedMilliseconds+" ms");
                File.WriteAllText(args[1],result.Report(),new UTF8Encoding(false));
                var errors=result.Findings.Where(f=>f.Status=="ERROR").ToList();
                if(errors.Count!=2 || !errors.All(f=>f.View==1184 && (f.Dimension==235889 || f.Dimension==235871))
                    || result.Warnings.Count>0 || result.Findings.Any(f=>f.Status=="REVIEW")) throw new Exception("Unexpected beam findings");
                foreach(var error in errors)
                {
                    var features=result.Features[error.View];
                    Check("beam corrected neighbor REF DIM="+error.Dimension,"OK",new G.Point(2327.5,error.Point.Y,0),error.Normal,features);
                    Check("beam other thickness face DIM="+error.Dimension,"ERROR",new G.Point(2330.75,error.Point.Y,0),error.Normal,features);
                    Check("beam shifted REF extension DIM="+error.Dimension,"OK",new G.Point(2327.5,error.Point.Y+250,0),error.Normal,features);
                    Check("beam REF drift exceeds tolerance DIM="+error.Dimension,"ERROR",new G.Point(2327.55,error.Point.Y,0),error.Normal,features);
                }
                if(result.Findings.Where(f=>f.Dimension==236444 || f.Dimension==236355).Any(f=>f.Status!="OK")) throw new Exception("Valid overall 125 rejected");
                foreach(var error in errors) error.Point=new G.Point(2327.5,error.Point.Y,0);
                foreach(var foot in result.Findings) foot.Status=C.Classify(foot.Point,foot.Normal,result.Features[foot.View]).Status;
                C.CheckNotchPairs(result);
                if(result.Findings.Any(f=>f.Status!="OK")) throw new Exception("Corrected snapshot must be clean");
                var repeat=C.Analyze();
                var firstLines=File.ReadAllLines(args[1]).Where(l=>l.StartsWith("OK VIEW=")||l.StartsWith("ERROR VIEW="));
                var secondLines=repeat.Report().Split(new[]{"\r\n","\n"},StringSplitOptions.None).Where(l=>l.StartsWith("OK VIEW=")||l.StartsWith("ERROR VIEW="));
                if(!firstLines.SequenceEqual(secondLines)) throw new Exception("Live repeat differs");
                Console.WriteLine("PASS beam: both wrong contact feet detected, valid 125 preserved, corrected snapshot clean; "+repeat.Findings.Count+" feet."); return 0;
            }
            if(args.Length==2 && args[0]=="--audit")
            { var result=C.Analyze(); File.WriteAllText(args[1],result.Report(),new UTF8Encoding(false)); Console.WriteLine(result.Summary);
                foreach(var f in result.Findings.Where(f=>f.Status!="OK")) Console.WriteLine(f); return 0; }
            if(args.Length==2 && args[0]=="--notch-live")
            {
                var result=C.Analyze();
                File.WriteAllText(args[1],result.Report(),new UTF8Encoding(false));
                var errors=result.Findings.Where(f=>f.Status=="ERROR").ToList();
                if(errors.Count!=1 || errors[0].Dimension!=132403 || errors[0].Foot!=1
                    || result.Warnings.Count>0 || result.Findings.Any(f=>f.Status=="REVIEW"))
                    throw new Exception("Expected only the user-demonstrated wrong notch foot.");
                var foot=errors[0]; var features=result.Features[foot.View];
                if(!foot.Reason.StartsWith("NOTCH:") || !foot.Reason.Contains("71.5")) throw new Exception("Wrong-feature notch not identified semantically.");
                var original=foot.Point;
                foreach(double x in new[]{-3.5,0.0,3.5})
                {
                    foot.Point=new G.Point(x,original.Y,0); foot.Status="OK";
                    C.CheckNotchPairs(result);
                    if(foot.Status!=(x==-3.5?"OK":"ERROR")) throw new Exception("Live semantic wrong feature replay "+x);
                }
                foot.Point=original; foot.Status="OK"; C.CheckNotchPairs(result);
                Check("live notch restore correct X","OK",new G.Point(-3.5,foot.Point.Y,0),foot.Normal,features);
                Check("live notch shift Y only","OK",new G.Point(-3.5,foot.Point.Y-20,0),foot.Normal,features);
                Check("live notch nearby arc anchor exceeds 0.04mm tolerance","ERROR",new G.Point(-4.5,foot.Point.Y,0),foot.Normal,features);
                Check("live notch X drift away from real contour anchors","ERROR",new G.Point(-5.5,foot.Point.Y,0),foot.Normal,features);
                var repeat=C.Analyze();
                if(String.Join("\n",result.Findings.Select(f=>f.ToString()))!=String.Join("\n",repeat.Findings.Select(f=>f.ToString())))
                    throw new Exception("Live drawing changed during audit.");
                Console.WriteLine("PASS live: only wrong notch foot flagged; all other "+(result.Findings.Count-1)+" feet accepted; corrected/shifted replay passed.");
                return 0;
            }
            if(args.Length==2 && args[0]=="--preview")
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                Directory.CreateDirectory(args[1]);
                foreach(bool dark in new[]{false,true}) foreach(string scenario in new[]{"error","clean","single","review","warning","compact","chain"})
                {
                    bool clean=scenario=="clean" || scenario=="warning";
                    var sample=new C.Result { Drawing="間柱 / [C1-b-80]",Views=5,Dimensions=30 };
                    if(!clean) sample.Findings.Add(new C.Finding { Status="ERROR",ViewLabel="Mặt cắt C",DimensionLabel="69.171 mm",
                        Location="Chuỗi phía trên — chân bên phải",Reason="Không tìm thấy" });
                    if(!clean) for(int i=0;i<8;i++) sample.Findings.Add(new C.Finding { Status="ERROR",ViewLabel="Hình chiếu đứng",DimensionLabel="1145 – 2490 – 2385 mm",Location="Chuỗi bên trái — chân dưới cùng",Reason="Không tìm thấy" });
                    if(scenario=="single")
                    {
                        sample.Findings.RemoveRange(1,8); sample.Drawing="耐風梁 / [C-Hb15W-18]"; sample.Views=2; sample.Dimensions=15;
                        sample.Findings[0].DimensionLabel="1283.437 mm"; sample.Findings[0].ViewLabel="Hình chiếu bằng";
                        sample.Findings[0].Location="Chuỗi phía trên — chân bên trái";
                    }
                    if(scenario=="review") foreach(var finding in sample.Findings)finding.Status="REVIEW";
                    if(scenario=="chain") foreach(var finding in sample.Findings)
                    { finding.Status="REVIEW"; finding.Reason="CHAIN: Có hai chân trùng hoặc quá sát nhau theo phương đo (≤ 0.04 mm); cần xác minh đoạn kích thước bằng không."; }
                    if(scenario=="warning" || scenario=="review")sample.Warnings.Add("Hình chiếu phụ: chưa đọc được đầy đủ dữ liệu hình học.");
                    if(scenario=="compact")sample.Findings[0].DimensionLabel="1145 – 2490 – 2385 – 1283.437 mm";
                    int runs=0,selections=0;
                    C.Finding lastSelected=null;
                    using(var form=new Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm(sample,dark,()=>{runs++; return runs==1?new C.Result { Drawing="Rechecked",Views=2,Dimensions=10 }:sample;},
                        (snapshot,finding)=>{if(!ReferenceEquals(snapshot,sample))throw new Exception("Wrong selection snapshot");selections++;lastSelected=finding;}))
                    {
                        form.ShowInTaskbar=false; form.StartPosition=System.Windows.Forms.FormStartPosition.Manual;
                        if(scenario=="compact")form.Size=form.MinimumSize;
                        form.Location=new System.Drawing.Point(-20000,-20000); form.Show();
                        System.Windows.Forms.Application.DoEvents(); form.PerformLayout();
                        if(selections!=0)throw new Exception("Opening the report selected a drawing object");
                        if(!clean)
                        {
                            var grid=Descendants(form).OfType<System.Windows.Forms.DataGridView>().Single();
                            typeof(System.Windows.Forms.DataGridView).GetMethod("OnCellClick",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
                                .Invoke(grid,new object[]{new System.Windows.Forms.DataGridViewCellEventArgs(0,0)});
                            if(selections!=1 || !ReferenceEquals(lastSelected,grid.CurrentRow.Tag))throw new Exception("Single row click did not select its DIM");
                        }
                        if(!clean && scenario!="single")
                        {
                            var bar=Descendants(form).Single(c=>c.GetType().Name=="ReportScroll");
                            var grid=Descendants(form).OfType<System.Windows.Forms.DataGridView>().Single();
                            var mouse=new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left,1,8,bar.Height-8,0);
                            bar.GetType().GetMethod("OnMouseDown",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(bar,new object[]{mouse});
                            bar.GetType().GetMethod("OnMouseUp",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(bar,new object[]{mouse});
                            if(grid.FirstDisplayedScrollingRowIndex<=0) throw new Exception("Scrollbar did not scroll");
                            grid.FirstDisplayedScrollingRowIndex=0;
                            var next=Descendants(form).OfType<System.Windows.Forms.Button>().Single(b=>b.Text=="Vị trí tiếp theo  →");
                            next.PerformClick();
                            if(grid.CurrentRow.Index!=1)throw new Exception("Next finding did not update selection");
                            var previous=Descendants(form).OfType<System.Windows.Forms.Button>().Single(b=>b.Text=="←  Vị trí trước");
                            previous.PerformClick();
                            if(grid.CurrentRow.Index!=0 || previous.Enabled)throw new Exception("Previous finding boundary failed");
                            if(selections!=3 || !ReferenceEquals(lastSelected,grid.CurrentRow.Tag))throw new Exception("Navigation did not select the corresponding DIM");
                        }
                        using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height))
                        { form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));
                            bitmap.Save(Path.Combine(args[1],(dark?"dark":"light")+"-"+scenario+".png")); }
                        var recheck=Descendants(form).OfType<System.Windows.Forms.Button>().Single(b=>b.Name=="Recheck");
                        recheck.PerformClick(); System.Windows.Forms.Application.DoEvents();
                        if(runs!=1 || Descendants(form).OfType<System.Windows.Forms.DataGridView>().Single().RowCount!=0)
                            throw new Exception("Recheck did not clear old findings");
                        recheck=Descendants(form).OfType<System.Windows.Forms.Button>().Single(b=>b.Name=="Recheck");
                        recheck.PerformClick(); System.Windows.Forms.Application.DoEvents();
                        if(runs!=2 || Descendants(form).OfType<System.Windows.Forms.DataGridView>().Single().RowCount!=sample.Findings.Count)
                            throw new Exception("Second recheck did not restore findings");
                    }
                }
                Console.WriteLine("PASS 14 UI previews and 28 rechecks: navigation, scroll, errors/clean/review/warnings/single/compact/chain, both themes."); return 0;
            }
            if(args.Length==3 && (args[0]=="--product" || args[0]=="--product-beam"))
            {
                var assembly=System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[1]));
                string report=(string)assembly.GetType("Tekla.Technology.Akit.UserScript.PHU_DimensionCheck",true)
                    .GetMethod("Audit").Invoke(null,null);
                File.WriteAllText(args[2],report,new UTF8Encoding(false));
                var errorLines=report.Split('\n').Where(line=>line.StartsWith("ERROR ")).ToList();
                bool expectedErrors=args[0]=="--product-beam"
                    ? errorLines.Count==2 && errorLines.All(line=>line.Contains("VIEW=1184") && (line.Contains("DIM=235889")||line.Contains("DIM=235871")))
                    : errorLines.Count==0;
                if(!expectedErrors || report.Split('\n').Any(line=>line.StartsWith("REVIEW ")||line.StartsWith("INCOMPLETE ")))
                    throw new Exception("Official assembly produced unexpected findings on approved drawing.");
                Console.WriteLine("PASS official assembly read-only audit\n"+String.Join("\n",report.Split('\n').Take(5))); return 0;
            }
            if(args.Length==2 && args[0]=="--live")
            {
                var result=C.Analyze();
                File.WriteAllText(args[1],result.Report(),new UTF8Encoding(false));
                Console.WriteLine(result.Summary);
                foreach(var f in result.Findings.Where(f=>f.Status!="OK")) Console.WriteLine(f);
                foreach(var w in result.Warnings) Console.WriteLine("INCOMPLETE "+w);
                if(result.Findings.Any(f=>f.Status!="OK") || result.Warnings.Count>0)
                    throw new Exception("Approved live drawing must have zero errors/reviews/incomplete items.");
                File.WriteAllText(args[1]+".user.txt",result.UserReport(),new UTF8Encoding(false));
                // Replay wrong bottom picks on actual geometry; do not depend on drawing IDs.
                int replay=0;
                var replayViews=new HashSet<int>();
                foreach(var foot in result.Findings.Where(f=>f.Normal!=null && Math.Abs(f.Normal.X)>0.9
                    && Math.Abs(f.Point.Y-result.Features[f.View].Where(x=>x.Owner==result.Main && x.Kind=="VERTEX").Min(x=>x.A.Y))<C.Tolerance))
                {
                    Check("live corrected VIEW="+foot.View+" DIM="+foot.Dimension,"OK",foot.Point,foot.Normal,result.Features[foot.View]);
                    Check("live wrong bottom replay VIEW="+foot.View+" DIM="+foot.Dimension,"ERROR",
                        new G.Point(foot.Point.X,foot.Point.Y-1,0),foot.Normal,result.Features[foot.View]);
                    replay++;
                    replayViews.Add(foot.View);
                }
                if(replay<2 || replayViews.Count<2) throw new Exception("Expected bottom replay in both approved views, found "+replay);
                var again=C.Analyze();
                string before=String.Join("\n",result.Findings.Select(f=>f.ToString()));
                string after=String.Join("\n",again.Findings.Select(f=>f.ToString()));
                if(before!=after) throw new Exception("Read-only repeat differs; drawing may have changed.");
                Console.WriteLine("PASS live: "+replay+" corrected/wrong-bottom pairs, both views; repeated audit identical; feet="+result.Findings.Count);
                return 0;
            }
            var fs=new List<C.Feature> { F(1,"EDGE",-75,0,-75,6019,true), F(1,"VERTEX",-75,0,-75,0,true),
                F(1,"VERTEX",-75,6019,-75,6019,true),F(2,"NEIGHBOR EDGE",-500,-1,500,-1,false) };
            var normal=new G.Vector(-1,0,0);
            foreach(int rotation in new[]{0,1,2,3})
            {
                ReferenceGate(rotation,"extended local",500,80,500,300,true,true);
                ReferenceGate(rotation,"remote crossing",2000,80,2000,300,true,false);
                ReferenceGate(rotation,"parallel",400,150,600,150,true,false);
                ReferenceGate(rotation,"collinear ambiguous",400,0,600,0,true,false);
                ReferenceGate(rotation,"end-on",500,0,500,0,true,true);
                ReferenceGate(rotation,"end-on away from main",500,50,500,50,true,false);
                ReferenceGate(rotation,"unconnected",500,80,500,300,false,false);
            }
            foreach(int rotation in new[]{0,1,2,3})
            {
                var contact=new List<C.Feature> { F(1,"EDGE",0,0,0,100,true),
                    F(2,"REF",70,10,70,80,C.ReferenceAllowed(false,true,true,false)),
                    F(2,"EDGE",70,0,70,100,false), F(3,"EDGE",70,0,70,100,false),
                    F(3,"REF",73,0,73,100,C.ReferenceAllowed(false,true,false,false)) };
                var wrong=new G.Point(70,50,0); var correct=new G.Point(73,50,0); var n=new G.Vector(0,1,0);
                for(int j=0;j<rotation;j++)
                {
                    foreach(var f in contact){f.A=new G.Point(-f.A.Y,f.A.X,0);f.B=new G.Point(-f.B.Y,f.B.X,0);}
                    wrong=new G.Point(-wrong.Y,wrong.X,0);correct=new G.Point(-correct.Y,correct.X,0);n=new G.Vector(-n.Y,n.X,0);
                }
                Check("connected plate contour is not structural REF rotation="+rotation,"ERROR",wrong,n,contact);
                Check("structural neighbor REF remains valid rotation="+rotation,"OK",correct,n,contact);
                contact.Reverse(); Check("contact shuffle rotation="+rotation,"ERROR",wrong,n,contact);
            }
            if(C.ReferenceAllowed(true,false,true,false) || C.ReferenceAllowed(false,false,false,false)
                || C.ReferenceAllowed(true,true,false,true)) throw new Exception("Reference role policy regression");
            foreach(int rotation in new[]{0,1,2,3}) foreach(int mirror in new[]{-1,1})
            {
                Func<double,double,G.Point> transform=(x,y)=>
                {
                    x*=mirror;
                    for(int i=0;i<rotation;i++){ double temp=x; x=-y; y=temp; }
                    return new G.Point(x+250,y-110,0);
                };
                var a=transform(0,80); var b=transform(30,100);
                var local=new List<C.Feature>();
                Action<double,double,double,double> edge=(x,y,bx,by)=>local.Add(new C.Feature { Owner=1,Kind="EDGE",Allowed=true,A=transform(x,y),B=transform(bx,by) });
                edge(0,0,100,0); edge(100,0,100,100); edge(0,80,20,80); edge(20,80,30,90); edge(30,90,30,100); edge(30,100,100,100);
                var origin=transform(0,0); var v=transform(0,1); var h=transform(1,0);
                var r=new C.Result { Main=1 }; r.Features[1]=local;
                var na=new G.Vector(v.X-origin.X,v.Y-origin.Y,0); var nb=new G.Vector(h.X-origin.X,h.Y-origin.Y,0);
                r.Findings.Add(new C.Finding { View=1,Dimension=10,Foot=0,Point=a,Normal=na,Status="OK" });
                var target=new C.Finding { View=1,Dimension=10,Foot=1,Point=transform(50,100),Normal=na,Status="OK" }; r.Findings.Add(target);
                r.Findings.Add(new C.Finding { View=1,Dimension=11,Foot=0,Point=b,Normal=nb,Status="OK" });
                r.Findings.Add(new C.Finding { View=1,Dimension=11,Foot=1,Point=a,Normal=nb,Status="OK" });
                C.CheckNotchPairs(r); if(target.Status!="ERROR") throw new Exception("Semantic notch rotation/mirror missed");
                target.Point=transform(100,100); target.Status="OK"; C.CheckNotchPairs(r);
                if(target.Status!="OK") throw new Exception("Overall span sharing notch shoulder falsely rejected");
                target.Point=transform(30,130); target.Status="OK"; C.CheckNotchPairs(r);
                if(target.Status!="OK") throw new Exception("Allowed perpendicular displacement rejected");
                local.RemoveAll(f=>Distance(f.A,transform(20,80))<0.001 && Distance(f.B,transform(30,90))<0.001);
                target.Point=transform(50,100);target.Status="OK";C.CheckNotchPairs(r);
                if(target.Status!="OK") throw new Exception("Disconnected edges falsely treated as notch");
                count++;Console.WriteLine("PASS semantic notch rotation="+rotation+" mirror="+mirror+"; perpendicular shift/disconnected contour");
            }
            var anchor=new List<C.Feature> { F(1,"HOLE",0,0,0,0,true) };
            foreach(var n in new[]{new G.Vector(-1,0,0),new G.Vector(0,1,0)})
            {
                bool horizontal=Math.Abs(n.Y)>0.5;
                foreach(double offset in new[]{-0.041,-0.04,-0.039,0.039,0.04,0.041})
                    Check("0.04mm boundary "+(horizontal?"horizontal":"vertical")+" "+offset,
                        Math.Abs(offset)<=0.04?"OK":"ERROR",
                        new G.Point(horizontal?offset:0,horizontal?0:offset,0),n,anchor);
            }
            var notch=new List<C.Feature> { F(1,"EDGE",-75,5972,75,5972,true),
                F(1,"EDGE",-3.5,0,-3.5,5972,true),F(1,"VERTEX",-3.5,5972,-3.5,5972,true) };
            Check("notch horizontal X drift while touching real horizontal edge","ERROR",new G.Point(-19.988,5972,0),new G.Vector(0,1,0),notch);
            Check("notch horizontal correct vertex","OK",new G.Point(-3.5,5972,0),new G.Vector(0,1,0),notch);
            Check("notch horizontal Y-only shift supported by real edge","OK",new G.Point(-3.5,5920,0),new G.Vector(0,1,0),notch);
            Check("notch vertical Y drift while touching real vertical edge","ERROR",new G.Point(-3.5,5920,0),normal,notch);
            Check("notch vertical X-only shift supported by real edge","OK",new G.Point(-19.988,5972,0),normal,notch);
            // Both red-box views had the same wrong lower-edge coordinate. IDs/order differ.
            foreach(string view in new[]{"Front","Top"})
            {
                Check(view+" wrong bottom 1mm","ERROR",new G.Point(-75,-1,0),normal,fs);
                Check(view+" corrected bottom","OK",new G.Point(-75,0,0),normal,fs);
            }
            Check("off-edge foot is not on longitudinal projection interval","ERROR",new G.Point(-82.937,179,0),normal,fs);
            Check("far free foot","ERROR",new G.Point(-355.096,6679,0),normal,fs);
            Check("extension shifted from true vertex","OK",new G.Point(-200,0,0),normal,fs);
            fs.Add(F(3,"CONNECTED REF",0,100,200,100,true));
            Check("direct neighbor reference","OK",new G.Point(100,100,0),normal,fs);
            fs.Add(F(3,"CONNECTED EDGE",0,90,200,90,false));
            Check("connected neighbor edge is not reference","ERROR",new G.Point(100,90,0),normal,fs);
            fs.Add(F(4,"UNCONNECTED REF",0,200,200,200,false));
            Check("unconnected neighbor reference","ERROR",new G.Point(100,200,0),normal,fs);
            fs.Add(F(1,"HOLE",5,50,5,50,true));
            Check("hole","OK",new G.Point(5,50,0),normal,fs);
            Check("shifted hole origin","OK",new G.Point(-20,50,0),normal,fs);
            var level=F(9,"GRID level",0,700,1,700,true); level.Infinite=true; fs.Add(level);
            Check("grid elevation beyond finite endpoints","OK",new G.Point(-800,700,0),normal,fs);
            Check("grid elevation wrong 1mm","ERROR",new G.Point(-800,699,0),normal,fs);
            Check("grid along measurement does not authorize arbitrary station","ERROR",new G.Point(713,700,0),new G.Vector(0,1,0),fs);
            var axis=F(10,"GRID axis",300,0,300,1,true); axis.Infinite=true; fs.Add(axis);
            Check("grid axis shifted extension","OK",new G.Point(300,700,0),new G.Vector(0,1,0),fs);
            Check("unconnected neighbor still fails with grids","ERROR",new G.Point(100,200,0),normal,fs);
            fs.Reverse();
            Check("shuffle preserves verdict","ERROR",new G.Point(-75,-1,0),normal,fs);
            foreach(var f in fs) { f.A=new G.Point(-f.A.Y+30,f.A.X+70,0); f.B=new G.Point(-f.B.Y+30,f.B.X+70,0); }
            Check("rotated translated correct","OK",new G.Point(30,-5,0),new G.Vector(0,-1,0),fs);
            Check("rotated translated wrong","ERROR",new G.Point(31,-5,0),new G.Vector(0,-1,0),fs);
            Check("invalid coordinate","REVIEW",new G.Point(double.NaN,0,0),normal,fs);
            var graph=new N.Plan();
            for(int i=1;i<=7;i++) graph.Nodes[i]=new N.Node { Id=i,Plate=i==2||i==4,Dummy=i==6 };
            graph.Edges.Add(new N.Edge { A=1,B=2 });
            graph.Edges.Add(new N.Edge { A=2,B=3 });
            graph.Edges.Add(new N.Edge { A=3,B=4 });
            graph.Edges.Add(new N.Edge { A=4,B=5 });
            graph.Edges.Add(new N.Edge { A=1,B=6 });
            graph.Edges.Add(new N.Edge { A=6,B=7 });
            var connected=C.ResolveConnected(graph,new HashSet<int>{1});
            if(!connected.SetEquals(new[]{2,3})) throw new Exception("Neighbor graph escaped first connected member or expanded dummy");
            count++; Console.WriteLine("PASS main -> plate -> member; no second-hop member/dummy expansion");
            graph.Edges.Reverse();
            if(!C.ResolveConnected(graph,new HashSet<int>{1}).SetEquals(connected)) throw new Exception("Graph ordering matters");
            count++; Console.WriteLine("PASS graph shuffle");
            graph.Edges.Clear(); graph.Keep=graph.Nodes.ToDictionary(x=>x.Key,x=>"KEEP insufficient connection evidence");
            if(C.ResolveConnected(graph,new HashSet<int>{1}).Count!=0) throw new Exception("Visibility fallback incorrectly authorizes DIM");
            count++; Console.WriteLine("PASS visibility fallback never permits neighbor DIM");
            var reportTest=new C.Result { Views=2,Dimensions=24 };
            if(!reportTest.UserReport().StartsWith("Không phát hiện chân kích thước sai.")) throw new Exception("Clean user report wording");
            reportTest.Findings.Add(new C.Finding { Status="ERROR",Reason="Không tìm thấy",ViewLabel="Hình chiếu đứng",
                DimensionLabel="160 – 6500 mm",Location="Chuỗi bên trái — chân dưới cùng" });
            string user=reportTest.UserReport();
            if(!user.Contains("Hình chiếu đứng")||!user.Contains("160 – 6500 mm")||!user.Contains("chân dưới cùng")
                ||user.Contains("VIEW=")||user.Contains("residual")||user.Contains("P106")) throw new Exception("User report missing readable location or leaking diagnostics");
            count++; Console.WriteLine("PASS readable clean/failure reports");
            Console.WriteLine("PASS "+count+" classifier cases");
            HardeningTests.Run(); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static double Distance(G.Point a,G.Point b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
    static void ReferenceGate(int rotation,string name,double ax,double ay,double bx,double by,bool connected,bool expected)
    {
        var fs=new List<C.Feature> { F(1,"REF",0,0,1000,0,true),F(1,"EDGE",0,-50,1000,-50,true),F(1,"EDGE",0,50,1000,50,true),
            F(2,"REF",ax,ay,bx,by,true),F(2,"EDGE",ax-25,ay,bx-25,by,false),F(2,"EDGE",ax+25,ay,bx+25,by,false) };
        fs[3].ExternalMemberReference=connected;
        // Another primary member's reference must not be treated as main reference.
        fs.Add(F(3,"REF",10,400,900,400,true));
        for(int i=0;i<rotation;i++) foreach(var f in fs) { f.A=new G.Point(-f.A.Y,f.A.X,0);f.B=new G.Point(-f.B.Y,f.B.X,0); }
        C.ConstrainReferences(fs,1);
        if(fs.Any(f=>f.Owner==3&&f.Allowed) || fs.Any(f=>f.Owner==2&&f.Kind=="REF"&&f.Allowed)) throw new Exception("Raw neighbor reference escaped gate");
        if(fs.Any(f=>f.Kind=="REF-INTERSECTION"&&f.Allowed)!=expected) throw new Exception("Reference gate "+name+" rotation="+rotation);
        int size=fs.Count;fs.Reverse();C.ConstrainReferences(fs,1);
        if(fs.Count!=size || fs.Any(f=>f.Kind=="REF-INTERSECTION"&&f.Allowed)!=expected) throw new Exception("Reference gate not repeatable");
        count++;Console.WriteLine("PASS reference gate "+name+" rotation="+rotation);
    }
    static System.Collections.Generic.IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root)
    { foreach(System.Windows.Forms.Control child in root.Controls) { yield return child; foreach(var nested in Descendants(child)) yield return nested; } }
}
