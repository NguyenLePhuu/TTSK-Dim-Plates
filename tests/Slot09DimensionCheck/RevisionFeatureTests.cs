using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using R=Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

static class RevisionFeatureTests
{
    static int assertions;
    static void Check(bool condition,string message){assertions++;if(!condition)throw new Exception(message);}
    static R.Segment Dim(int id,double a,double b,double level=26,double x=1,double y=0)
    {return new R.Segment{Id=id,PaperA=a,PaperB=b,Level=level,HasPaperGeometry=true,X=x,Y=y,Value=(b-a)*10};}
    public static void Run()
    {
        var old=new List<R.Segment>();double a=0;int id=1;
        foreach(double length in new[]{40,60,132.5,60,1412.3,60,688.2,60,40})
        {old.Add(Dim(id++,a,a+length/10));a+=length/10;}
        var chains=R.Chains(old.AsEnumerable().Reverse());
        Check(chains.Count==1&&chains[0].Count==9,"One sorted hole chain from shuffled snapshot");
        var current=new List<R.Segment>();a=0;id=101;
        foreach(double length in new[]{40,60,132.5,60,1412.3,60,690.2,60,40})
        {current.Add(Dim(id++,a,a+length/10));a+=length/10;}
        bool ambiguous;
        var match=R.MatchChain(current,chains,out ambiguous);
        Check(match==chains[0]&&!ambiguous,"Recreated hole chain retains technical identity");
        var segments=R.MatchChainSegments(current,match);
        Check(segments.Where((s,i)=>Math.Abs(s.Value-current[i].Value)>.001).Count()==1,"Only 688.2 -> 690.2 changes; eight unchanged");
        Check(R.MatchChain(new[]{Dim(300,245.4,255.5,-19.3)},chains,out ambiguous)==null&&!ambiguous,"101 below beam is new, not the nearby 60 above");
        var vertical=new List<List<R.Segment>>{new List<R.Segment>{Dim(400,-7.5,0,-268,0,1),Dim(401,0,7.5,-268,0,1)}};
        Check(R.MatchChain(new[]{Dim(402,-7.5,-5.5,-268,0,1)},vertical,out ambiguous)==null,"20 is new; cannot borrow 75 even at same line level");
        var dup=new List<List<R.Segment>>{old,new List<R.Segment>(old.Select(s=>Dim(s.Id+1000,s.PaperA,s.PaperB)))};
        Check(R.MatchChain(current,dup,out ambiguous)==null&&ambiguous,"Equivalent old chains remain ambiguous");
        Check(R.MatchChain(old,dup,out ambiguous)==old&&!ambiguous,"Stable IDs reserve old chain before geometric candidates");
        var overall=new List<R.Segment>{Dim(9010,0,255.3,27.9)};
        var newOverall=new List<R.Segment>{Dim(9020,0,255.5,20)};
        foreach(var s in current.Concat(old).Concat(overall).Concat(newOverall))
        {s.HasFeet=true;s.FootA=7.5;s.FootB=7.5;}
        foreach(var s in current)s.Level=17.5;
        bool[] topologyReview;
        var topologyMatch=R.MatchChains(new List<List<R.Segment>>{current,newOverall},new List<List<R.Segment>>{overall,old},out topologyReview);
        Check(topologyMatch[0]==old&&topologyMatch[1]==overall&&!topologyReview.Any(r=>r),
            "Regeneration separates hole chain and overall by stations/topology when both line levels move");
        var reversedMatch=R.MatchChains(new List<List<R.Segment>>{newOverall,current},new List<List<R.Segment>>{old,overall},out topologyReview);
        Check(reversedMatch[0]==overall&&reversedMatch[1]==old&&!topologyReview.Any(r=>r),"Regenerated topology is independent of set enumeration order");
        foreach(var s in current)s.Level=26;
        var sameFeetOtherLevel=old.Select(s=>Dim(s.Id+3000,s.PaperA,s.PaperB,s.Level+9.5)).ToList();
        foreach(var s in current.Concat(old).Concat(sameFeetOtherLevel)){s.HasFeet=true;s.FootA=2;s.FootB=2;}
        Check(R.MatchChain(current,new List<List<R.Segment>>{sameFeetOtherLevel,old},out ambiguous)==old&&!ambiguous,
            "Same witness feet use nearest dimension-line level to separate a full-span DIM");
        Check(R.MatchChain(new[]{Dim(900,0,255.3,26,0,1)},chains,out ambiguous)==null,"Orthogonal axes never match");
        Check(R.MatchChain(new[]{Dim(901,500,755.3)},chains,out ambiguous)==null,"Remote identical measurement does not match");
        Check(R.MatchChainSegments(current,null).All(s=>s==null),"New chain never compares its segments with another chain");
        var moved=old.Select(s=>Dim(s.Id+2000,s.PaperA,s.PaperB,s.Level+25)).ToList();
        foreach(var s in old){s.HasFeet=true;s.FootA=2;s.FootB=2;}
        foreach(var s in moved){s.HasFeet=true;s.FootA=2;s.FootB=2;}
        Check(R.MatchChain(moved,chains,out ambiguous)==chains[0],"Recreated DIM with same feet and far moved line is not new");
        moved[0].FootA=15;moved.Last().FootB=15;
        Check(R.MatchChain(moved,chains,out ambiguous)==null,"Same measurement on a different feature is genuinely new");
        var split=R.MatchChainSegments(new[]{Dim(701,0,4),Dim(702,4,7),Dim(703,7,10)},new[]{Dim(11,0,4),Dim(12,4,10)});
        Check(split[0]!=null&&split[1]==null&&split[2]==null,"Added feet preserve unchanged interval and mark new intervals");
        TestTopology();
        TestNativeWriterBoundary();
        var before=new[]{new[]{"mark","qty","drawing"},new[]{"A","1","D1"}};
        var added=new[]{new[]{"mark","qty","drawing"},new[]{"A","1","D1"},new[]{"A","1","D1"}};
        Check(R.ContentSignature(before)!=R.ContentSignature(added),"Adding duplicate row changes schedule");
        Check(R.ContentSignature(before)!=R.ContentSignature(before.Concat(new[]{new string[0]}).ToArray()),"Additional empty row changes schedule");
        Check(R.ContentSignature(new[]{new[]{"a|b","c"}})!=R.ContentSignature(new[]{new[]{"a","b|c"}}),"Cell delimiters cannot collide");
        Check(R.ContentSignature(before)==R.ContentSignature(before.Select(r=>r.ToArray()).ToArray()),"Identical content exact equality");
        TestContentComparison();TestTableParser();
        var frame=R.DeleteFrame(367.5,94.4,18.3,28.7,1);
        Check(frame.Count==4&&Math.Abs(frame[0].X-366.5)<.001&&Math.Abs(frame[2].Y-124.1)<.001,"Cloud follows translated sheet frame including margins");
        Check(Math.Abs((frame[0].X+frame[2].X)-(frame[1].X+frame[3].X))<1e-9&&Math.Abs((frame[0].Y+frame[2].Y)-(frame[1].Y+frame[3].Y))<1e-9,"Both diagonals intersect at exact shared centre");
        bool rejected=false;try{R.DeleteFrame(0,0,Double.NaN,10,1);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Invalid cloud plan rejects before mutation");
        Console.WriteLine("PASS Revision enhancements: "+assertions+" assertions (new DIM, individual chain values, ambiguity, rows, marks, read-only reports, delete geometry).");
    }
    static void TestTopology()
    {
        var old=new List<R.Segment>{Dim(1,0,4),Dim(2,4,10),Dim(3,10,20),Dim(4,20,26)};
        // Add dimension point may retain a previous child ID on one of its new halves.
        var added=new List<R.Segment>{Dim(1,0,4),Dim(2,4,7),Dim(30,7,10),Dim(31,10,20),Dim(32,20,26)};
        var match=R.MatchChainSegments(added,old);
        Check(match[0]==old[0]&&match[1]==null&&match[2]==null&&match[3]==old[2]&&match[4]==old[3],"Add point: reused ID must not pair a split half, intact siblings preserved");
        var removed=new List<R.Segment>{Dim(10,0,4),Dim(11,4,20),Dim(12,20,26)};
        match=R.MatchChainSegments(removed,old);
        Check(match[0]==old[0]&&match[1]==null&&match[2]==old[3],"Remove point: merged interval new, neighbors unchanged");
        var edited=new List<R.Segment>{Dim(100,0,4),Dim(101,4,7),Dim(102,7,10),Dim(103,10,20.2),Dim(104,20.2,26.2)};
        match=R.MatchChainSegments(edited,old);
        Check(match[1]==null&&match[2]==null&&match[3]==old[2]&&match[4]==old[3],"Added hole plus moved foot aligns later edited and translated measurements");
        Check(Math.Abs(match[3].Value-edited[3].Value)>1&&Math.Abs(match[4].Value-edited[4].Value)<.0001,"Only changed length red after added hole; translated 60 remains unchanged");
        var replaced=new List<R.Segment>{Dim(201,0,4),Dim(202,4,7),Dim(203,7,10),Dim(204,10,26)};
        match=R.MatchChainSegments(replaced,old);
        Check(match[0]==old[0]&&match.Skip(1).All(s=>s==null),"Simultaneous add and remove with unchanged total count detects split and merged intervals");
        var reversed=edited.AsEnumerable().Reverse().ToList();
        match=R.MatchChainSegments(reversed,old.AsEnumerable().Reverse().ToList());
        Check(match[0]==old[3]&&match[1]==old[2]&&match[2]==null&&match[3]==null&&match[4]==old[0],"Input order cannot alter measurement pairing");
        bool ambiguous;
        var chains=new List<List<R.Segment>>{old};
        Check(R.MatchChain(new[]{Dim(1,200,210)},chains,out ambiguous)==null&&!ambiguous,"Historical ID cannot override remote geometry");
        var farMoved=added.Select(s=>Dim(s.Id+400,s.PaperA,s.PaperB,100)).ToList();
        Check(R.MatchChain(farMoved,chains,out ambiguous)==old&&!ambiguous,"Recreated chain with added point and dragged line matches full range without IDs");
        var duplicate=old.Select(s=>Dim(s.Id+900,s.PaperA,s.PaperB)).ToList();
        bool[] reviews;var assigned=R.MatchChains(new List<List<R.Segment>>{duplicate,old},chains,out reviews);
        Check(assigned[1]==old&&assigned[0]==null&&reviews[0],"Reserve geometric plus ID owner before recreated competitor, regardless of order");
        var duplicate2=old.Select(s=>Dim(s.Id+1000,s.PaperA,s.PaperB)).ToList();
        assigned=R.MatchChains(new List<List<R.Segment>>{duplicate2,duplicate},chains,out reviews);
        Check(assigned.All(s=>s==null)&&reviews.All(s=>s),"Competing recreated chains stay explicitly ambiguous instead of consuming baseline greedily");
        foreach(int insertAt in new[]{0,1,2,3})
        {
            var sample=old.Select(s=>Dim(s.Id+2000,s.PaperA,s.PaperB)).ToList();var source=sample[insertAt];
            double middle=(source.PaperA+source.PaperB)/2;
            sample.RemoveAt(insertAt);sample.Insert(insertAt,Dim(5000,source.PaperA,middle));sample.Insert(insertAt+1,Dim(5001,middle,source.PaperB));
            match=R.MatchChainSegments(sample,old);
            Check(match.Count(s=>s==null)==2&&match[insertAt]==null&&match[insertAt+1]==null,"Added point at interval "+insertAt+" affects only its two resulting intervals");
        }
    }
    public sealed class FakeProxy:IDisposable
    {
        public object Value=153;
        public bool Accept=true,Disposed;
        public string Written;
        public object GetProperty(string name){if(name!="proFontColor")throw new Exception("Unexpected property read");return Value;}
        public bool SetProperty(string name,object value){Written=name;if(Accept)Value=value;return Accept;}
        public void Dispose(){Disposed=true;}
    }
    public sealed class FakeFactory
    {
        public FakeProxy Proxy=new FakeProxy();
        public int Id,Updates;
        public bool Accept=true;
        public FakeProxy CreateObject(int id,string type){Id=id;Check(type=="","Native proxy infers actual child type");return Proxy;}
        public bool UpdateObject(FakeProxy proxy){Updates++;return Accept;}
    }
    static object Font(FakeFactory factory,int? color)
    {return typeof(R).GetMethod("NativeFontColor",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{
        factory,typeof(FakeFactory).GetMethod("CreateObject"),typeof(FakeFactory).GetMethod("UpdateObject"),6902,color});}
    static void TestNativeWriterBoundary()
    {
        var child=(Tekla.Structures.Drawing.StraightDimension)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Tekla.Structures.Drawing.StraightDimension));
        var item=new R.Item{Dimension=child,NativeColor=Tekla.Structures.Drawing.DrawingColors.Red};
        Check(R.GetColor(item)==Tekla.Structures.Drawing.DrawingColors.Red,"Native child colour is authoritative, without reading inherited set font");
        item.NativeColor=null;bool unknown=false;
        try{R.GetColor(item);}catch(InvalidOperationException){unknown=true;}
        Check(unknown,"Missing native colour rejects instead of falsely reporting inherited black");
        var f=new FakeFactory();Check((int)Font(f,null)==153&&f.Updates==0&&f.Proxy.Disposed,"Native colour preflight is read-only and disposes proxy");
        f=new FakeFactory();Font(f,160);
        Check(f.Id==6902&&f.Updates==1&&f.Proxy.Written=="proFontColor"&&(int)f.Proxy.Value==160&&f.Proxy.Disposed,"Only requested child/font property written; no parent operation");
        f=new FakeFactory();f.Proxy.Accept=false;bool rejected=false;
        try{Font(f,160);}catch(TargetInvocationException){rejected=true;}
        Check(rejected&&f.Updates==0&&f.Proxy.Disposed,"Rejected property write cannot call native update");
        f=new FakeFactory();f.Accept=false;rejected=false;
        try{Font(f,160);}catch(TargetInvocationException){rejected=true;}
        Check(rejected&&f.Proxy.Disposed,"Native update failure is propagated");
        f=new FakeFactory();f.Proxy.Value="unsupported";rejected=false;
        try{Font(f,160);}catch(TargetInvocationException){rejected=true;}
        Check(rejected&&f.Updates==0,"Unsupported native colour representation rejects before update");
        string folder=Path.Combine(Path.GetTempPath(),"TTSK-Revision-Writer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var running=typeof(R).GetMethod("IsNativeColorJobRunning",BindingFlags.NonPublic|BindingFlags.Static);
            Check(!(bool)running.Invoke(null,new object[]{folder}),"Missing native job marker does not block retry");
            File.WriteAllText(Path.Combine(folder,"running"),Int32.MaxValue.ToString());
            Check(!(bool)running.Invoke(null,new object[]{folder}),"Exited Tekla process cannot block a cancelled retry");
            File.WriteAllText(Path.Combine(folder,"running"),"unreadable");
            Check((bool)running.Invoke(null,new object[]{folder}),"Uncertain native job remains blocked");
            File.Delete(Path.Combine(folder,"running"));
            string request=Path.Combine(folder,"request.txt");
            File.WriteAllLines(request,new[]{"","","","",DateTime.UtcNow.AddMinutes(-1).Ticks.ToString()});
            R.RunNativeColorRequest(request);
            Check(File.ReadAllText(Path.Combine(folder,"result.txt")).Contains("Chưa ghi màu."),"Expired request fails before any model/drawing access");
            File.Delete(Path.Combine(folder,"result.txt"));File.WriteAllText(Path.Combine(folder,"cancel"),"");
            File.WriteAllLines(request,new[]{"","","","",DateTime.UtcNow.AddMinutes(1).Ticks.ToString()});R.RunNativeColorRequest(request);
            Check(File.ReadAllText(Path.Combine(folder,"result.txt")).Contains("Chưa ghi màu."),"Cancelled request cannot execute late writes");
            File.Delete(Path.Combine(folder,"result.txt"));File.Delete(Path.Combine(folder,"cancel"));
            R.RunNativeColorRequest(request);
            Check(File.ReadAllText(Path.Combine(folder,"result.txt")).Contains("trong Tekla"),"Native factory is unreachable outside Tekla process");
            string macro=(string)typeof(R).GetMethod("NativeColorMacro",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{
                @"C:\path with spaces\quote""\TTSK.exe",request});
            using(var compiler=new Microsoft.CSharp.CSharpCodeProvider())
            {
                var options=new System.CodeDom.Compiler.CompilerParameters{GenerateExecutable=false,GenerateInMemory=true};
                options.ReferencedAssemblies.Add("System.dll");
                options.ReferencedAssemblies.Add(Path.Combine(Path.GetDirectoryName(typeof(Tekla.Structures.Drawing.Drawing).Assembly.Location),"Tekla.Macros.Runtime.dll"));
                var compiled=compiler.CompileAssemblyFromSource(options,macro);
                Check(!compiled.Errors.HasErrors,"Generated macro compiles with escaped paths: "+String.Join(" | ",compiled.Errors.Cast<System.CodeDom.Compiler.CompilerError>()));
            }
        }
        finally{Directory.Delete(folder,true);}
    }
    static R.ContentBlock Block(int id,string value){return new R.ContentBlock{Id=id,Kind="Mark",Rows=1,Text=value,Signature=value};}
    static R.Result Compare(List<R.ContentBlock> old,List<R.ContentBlock> current)
    {
        var result=new R.Result();typeof(R).GetMethod("CompareContent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{result,
            new List<R.SnapshotView>{new R.SnapshotView{Id=1,Content=old}},new List<R.SnapshotView>{new R.SnapshotView{Id=1,Content=current}}});return result;
    }
    static void TestContentComparison()
    {
        Check(Compare(new List<R.ContentBlock>{Block(1,"A")},new List<R.ContentBlock>{Block(1,"A")}).Items.Count==0,"Identical mark silent");
        var changed=Compare(new List<R.ContentBlock>{Block(1,"A")},new List<R.ContentBlock>{Block(1,"B")});
        Check(changed.Items.Count==1&&changed.Items[0].ReadOnly&&changed.Items[0].Dimension==null&&changed.Items[0].Object==null,"Changed mark has no writable drawing object");
        Check(Compare(new List<R.ContentBlock>{Block(1,"A"),Block(2,"B")},new List<R.ContentBlock>{Block(1,"B"),Block(2,"A")}).Items.Count==2,"Swapped mark contents detected by object identity");
        Check(Compare(new List<R.ContentBlock>{Block(1,"A")},new List<R.ContentBlock>{Block(10,"A")}).Items.Count==0,"Recreated identical mark silent");
        Check(Compare(new List<R.ContentBlock>{Block(1,"A")},new List<R.ContentBlock>{Block(1,"A"),Block(2,"A")}).Items.Count==1,"Additional identical mark still reported");
        Check(Compare(new List<R.ContentBlock>{Block(1,"A")},new List<R.ContentBlock>()).Items.Single().After=="Đã xóa","Removed mark reported");
    }
    // Reflection-shaped DPM fixture; type names correspond to native primitives.
    public class Root {public List<object> Primitives {get;set;}=new List<object>();}
    public class Segment:Root {public int Id {get;set;}public int ObjectType {get;set;}}
    public class P {public double X {get;set;}public double Y {get;set;}}
    public class TextPrimitive {public string Text {get;set;}public P Position {get;set;}}
    public class LinePrimitive {public P StartPoint {get;set;}public P EndPoint {get;set;}}
    static R.ContentBlock Table(int rows,double shift,string drawingNumber="BPU-049",string quantity="1")
    {
        var group=new Segment{Id=44,ObjectType=263};
        string[][] values={new[]{"製 品 マーク","数  量","図 面 番 号"},new[]{"B-C150-230",quantity,drawingNumber},new[]{"B-C150-230","1","BPU-049"}};
        for(int row=0;row<rows;row++)for(int col=0;col<3;col++)group.Primitives.Add(new TextPrimitive{Text=values[row][col],Position=new P{X=col*10+shift,Y=30-row*6+shift}});
        for(int row=0;row<=rows;row++)group.Primitives.Add(new LinePrimitive{StartPoint=new P{X=shift,Y=34-row*6+shift},EndPoint=new P{X=30+shift,Y=34-row*6+shift}});
        var root=new Root();root.Primitives.Add(group);
        return ((List<R.ContentBlock>)typeof(R).GetMethod("ReadContent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{root})).Single();
    }
    static void TestTableParser()
    {
        var a=Table(2,0);var translated=Table(2,80);var b=Table(3,0);
        Check(a.Rows==2&&b.Rows==3,"Grid preserves actual row count");
        Check(a.Signature==translated.Signature,"Schedule position change alone is silent");
        Check(a.Signature!=b.Signature,"Additional identical schedule row is a difference");
        Check(a.Signature==Table(2,0,"BPU-999").Signature&&a.Signature==Table(2,0,"").Signature,"Ignore both changed and newly filled drawing number");
        var quantity=Table(2,0,"BPU-999","2");
        Check(a.Signature!=quantity.Signature&&R.TableMessage(a,quantity)=="Ô số lượng khác giá trị [数量]","Quantity difference uses concise field name");
    }
}
