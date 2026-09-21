using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using R=Tekla.Technology.Akit.UserScript.PHU_DimensionRevision;

// Read-only live capture plus pure replay. No drawing mutation in this class.
static class RevisionMatchingTests
{
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Static;
    static R.Segment Dim(int id,double a,double b,double level,double fa,double fb,bool rendered=true)
    {return new R.Segment{Id=id,PaperA=a,PaperB=b,A=a*10,B=b*10,Value=(b-a)*10,Text=((b-a)*10).ToString(CultureInfo.InvariantCulture),
        X=0,Y=1,Level=level,FootA=fa,FootB=fb,HasFeet=true,HasWitnessGeometry=rendered,HasPaperGeometry=true};}
    static List<R.Segment> Pair(int id,double level,double fa,double fm,double fb)
    {return new List<R.Segment>{Dim(id,-7.5,0,level,fa,fm),Dim(id+1,0,7.5,level,fm,fb)};}
    public static void Run()
    {
        checks=0;
        // Observed DPM: right upper witness starts at X=258.3000186 although the
        // old part ends at 255.3. Recreated start is 257.5, part end 255.5.
        var oldRight=Pair(1,-274.3,-257.3,-253.3,-258.300018604812);
        var right=Pair(101,-275.5,-257.5,-249.3,-257.5);
        var oldLeft=Pair(3,14.035217341815,2,-2,2);
        var left=Pair(103,20,2,-25.45,2);
        var middle=Pair(105,-148.683457467236,-172.683457467236,-172.683457467236,-172.683457467236);
        foreach(bool reverseOld in new[]{false,true})foreach(bool reverseNew in new[]{false,true})
        {
            var baseline=new List<List<R.Segment>>{oldLeft,oldRight};
            var current=new List<List<R.Segment>>{right,left,middle};
            if(reverseOld)baseline.Reverse();if(reverseNew)current.Reverse();
            bool[] review;var matches=R.MatchChains(current,baseline,out review);
            Check(matches[current.IndexOf(right)]==oldRight&&!review[current.IndexOf(right)],"Create drawing: existing right 75-75 must match snapshot despite gaps and new IDs");
            Check(matches[current.IndexOf(left)]==oldLeft&&!review[current.IndexOf(left)],"Left chain stays unchanged");
            Check(matches[current.IndexOf(middle)]==null&&!review[current.IndexOf(middle)],"New middle 75-75 remains new/red");
            Check(R.MatchChainSegments(right,oldRight).Select((s,i)=>s.Value==right[i].Value).All(b=>b),"Right numeric verdict unchanged");
        }
        bool ambiguous;
        var exactFeet=Pair(301,-275.5,-257.5,-249.3,-257.5);
        foreach(var s in exactFeet)s.HasWitnessGeometry=false;
        Check(R.MatchChain(exactFeet,new[]{oldRight},out ambiguous)==null,"Rendered fallback must never loosen exact model-foot comparisons");
        var otherSide=Pair(401,-240,-257.5,-249.3,-257.5);
        Check(R.MatchChain(otherSide,new[]{oldRight},out ambiguous)==null,"Opposite witness directions do not use corridor fallback");
        var oneEnd=Pair(501,-275.5,-257.5,-249.3,-140);
        Check(R.MatchChain(oneEnd,new[]{oldRight},out ambiguous)==null,"Both outer witnesses must support a regenerated chain");
        var duplicate=Pair(601,-275.5,-257.5,-249.3,-257.5);
        bool[] reviews;var contested=R.MatchChains(new[]{right,duplicate},new[]{oldRight},out reviews);
        Check(contested.All(c=>c==null)&&reviews.All(b=>b),"Two recreated copies cannot consume one baseline");
        var duplicateOld=Pair(701,-274.3,-257.3,-253.3,-258.300018604812);
        Check(R.MatchChain(right,new[]{oldRight,duplicateOld},out ambiguous)==null&&ambiguous,"Coincident historical alternatives require review");
        var random=new Random(719);
        for(int run=0;run<100;run++)
        {
            // Tiny DPM line-level noise used to split/reverse chains because level
            // was the primary sort key. Perturb every segment independently.
            var chain=Enumerable.Range(0,9).Select(i=>Dim(i+1,i*6,(i+1)*6,20+(random.NextDouble()-.5)*.0001,0,0)).OrderBy(s=>random.Next()).ToList();
            var grouped=R.Chains(chain);
            Check(grouped.Count==1&&grouped[0].Count==9,"Connectivity survives shuffled order and independent line noise");
        }
        foreach(double invalid in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity})
        {
            var broken=Pair(1,-275.5,-257.5,-249.3,-257.5);broken[0].PaperA=invalid;
            Check(R.MatchChain(broken,new[]{oldRight},out ambiguous)==null&&ambiguous,"Invalid geometry must require review, never DIM new");
        }
        var nearAngle=Pair(801,-275.5,-257.5,-249.3,-257.5);
        foreach(var s in nearAngle){s.X=.02;s.Y=Math.Sqrt(1-.02*.02);}
        Check(R.MatchChain(nearAngle,new[]{oldRight},out ambiguous)==null,"Different axes within old 0.999 dot threshold cannot compare projected stations");
        foreach(double angle in new[]{0.0,.27,1.9,3.6,5.8})foreach(bool mirror in new[]{false,true})
        foreach(double factor in new[]{.5,1.0,2.0})
        {
            var baseline=new[]{oldLeft,oldRight}.Select(c=>c.Select(s=>Transform(s,angle,mirror,factor)).Reverse().ToList()).Reverse().ToList();
            var current=new[]{right,left,middle}.Select(c=>c.Select(s=>Transform(s,angle,mirror,factor)).Reverse().ToList()).ToList();
            var matches=R.MatchChains(current,baseline,out reviews);
            Check(matches[0]==baseline[0]&&matches[1]==baseline[1]&&matches[2]==null&&!reviews.Any(b=>b),"Regenerated chains under rotation/mirror/common scale/large translation");
            Check(R.MatchChainSegments(current[0],matches[0]).Select((s,i)=>s!=null&&s.Value==current[0][i].Value).All(b=>b),"Transformed right remains unchanged");
        }
        foreach(var item in new[]{new R.Item{Review=true,Changed=true},new R.Item{Changed=false},new R.Item{ReadOnly=true,Changed=true}})
        {
            bool rejected=false;try{R.Apply(null,new[]{item});}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Automatic writer eligibility enforced before any Tekla access");
        }
        ParserTests();
        ReviewGates(oldRight,right,middle);
        string fixture=Path.Combine("tests","Slot09DimensionCheck","fixtures","revision-create-bmc117.tsv");
        if(!File.Exists(fixture))throw new FileNotFoundException("Run matching tests from repository root",fixture);
        Replay(fixture);
        Console.WriteLine("PASS Revision matching: "+checks+" assertions (Create drawing, parser, corridors, topology, malformed data, recorded drawing replay).");
    }
    static void ReviewGates(List<R.Segment> old,List<R.Segment> current,List<R.Segment> added)
    {
        var method=typeof(R).GetMethod("RevisionReviewRequired",Private);
        var baseline=new R.SnapshotView{Segments=old};
        Check(!(bool)method.Invoke(null,new object[]{baseline,current,old,old[0],false,false,false}),"Complete matched geometry can be classified");
        Check(!(bool)method.Invoke(null,new object[]{baseline,added,null,null,false,false,false}),"Complete new middle geometry stays new");
        Check((bool)method.Invoke(null,new object[]{null,current,null,null,false,false,false}),"Recreated/missing view is not proof of a new dimension");
        foreach(int gate in new[]{0,1,2})
            Check((bool)method.Invoke(null,new object[]{baseline,current,old,old[0],gate==0,gate==1,gate==2}),"Ambiguity/incomplete read/scale change blocks automatic colour");
        baseline.UnsupportedDimensions.Add(999);
        Check((bool)method.Invoke(null,new object[]{baseline,current,old,null,false,false,false}),"Unmatched interval in a partially readable baseline needs review even inside matched chain");
        baseline.UnsupportedDimensions.Clear();current[0].HasFeet=false;
        Check((bool)method.Invoke(null,new object[]{baseline,current,old,old[0],false,false,false}),"Missing current witnesses cannot certify location");
        current[0].HasFeet=true;old[0].HasFeet=false;
        Check((bool)method.Invoke(null,new object[]{baseline,current,old,old[0],false,false,false}),"Missing historical witnesses cannot certify location");
        old[0].HasFeet=true;
    }
    static R.Segment Transform(R.Segment s,double angle,bool mirror,double scale)
    {
        double cosine=Math.Cos(angle),sine=Math.Sin(angle),reflection=mirror?-1:1;
        Func<double,double,double[]> rotate=(x,y)=>new[]{scale*(cosine*x-sine*y*reflection)+12345,scale*(sine*x+cosine*y*reflection)-23456};
        double dx=cosine*s.X-sine*s.Y*reflection,dy=sine*s.X+cosine*s.Y*reflection;
        if(dx<-.0001||Math.Abs(dx)<.0001&&dy<0){dx=-dx;dy=-dy;}
        Func<double,double,double[]> project=(station,normal)=>{
            var p=rotate(station*s.X-normal*s.Y,station*s.Y+normal*s.X);
            return new[]{p[0]*dx+p[1]*dy,-p[0]*dy+p[1]*dx};};
        var a=project(s.PaperA,s.FootA);var b=project(s.PaperB,s.FootB);var line=project(s.PaperA,s.Level);
        return new R.Segment{Id=s.Id,PaperA=Math.Min(a[0],b[0]),PaperB=Math.Max(a[0],b[0]),Level=line[1],
            FootA=a[0]<b[0]?a[1]:b[1],FootB=a[0]<b[0]?b[1]:a[1],X=dx,Y=dy,Value=s.Value,Text=s.Text,
            HasFeet=s.HasFeet,HasPaperGeometry=true,HasWitnessGeometry=s.HasWitnessGeometry};
    }
    public class Root {public int Id{get;set;}public P Position{get;set;}=new P();public List<object> Primitives{get;set;}=new List<object>();}
    public class Segment:Root {public int ObjectType{get;set;}}
    public class P {public double X{get;set;}public double Y{get;set;}}
    public class PointPrimitive {public P Position{get;set;}}
    public class TextPrimitive {public string Text{get;set;}public double Angle{get;set;}}
    public class LinePrimitive {public P StartPoint{get;set;}public P EndPoint{get;set;}}
    static void ParserTests()
    {
        foreach(double angle in new[]{0.0,.42,Math.PI/2,2.3})foreach(bool reversed in new[]{false,true})
        {
            double x=Math.Cos(angle),y=Math.Sin(angle);
            var root=new Root{Id=1};var dim=new Segment{Id=2,ObjectType=256};root.Primitives.Add(dim);
            var label=new Segment{ObjectType=256};label.Primitives.Add(new TextPrimitive{Text="100",Angle=0});dim.Primitives.Add(label);
            var a=new PointPrimitive{Position=new P{X=3,Y=4}};
            var b=new PointPrimitive{Position=new P{X=3+10*x,Y=4+10*y}};
            dim.Primitives.Add(reversed?b:a);dim.Primitives.Add(reversed?a:b);
            var warnings=new List<string>();
            var parsed=(List<R.SnapshotView>)typeof(R).GetMethod("ParsePresentation",Private).Invoke(null,new object[]{new[]{root},warnings});
            var s=parsed.Single().Segments.Single();
            Check(Math.Abs(Math.Abs(s.X*x+s.Y*y)-1)<1e-8&&Math.Abs(s.PaperB-s.PaperA-10)<1e-8,"Arrow axis, independent of horizontal label and endpoint order");
            b.Position.X=a.Position.X;b.Position.Y=a.Position.Y;
            parsed=(List<R.SnapshotView>)typeof(R).GetMethod("ParsePresentation",Private).Invoke(null,new object[]{new[]{root},new List<string>()});
            Check(parsed[0].Segments.Count==0&&parsed[0].UnsupportedDimensions.Contains(2),"Degenerate dimension remains recorded as unreadable");
        }
        var scale=new R.SnapshotView{Segments=Pair(1,20,2,2,2)};
        Check(!(bool)typeof(R).GetMethod("SnapshotScaleDiffers",Private).Invoke(null,new object[]{scale,10.0}),"Matching paper scale");
        Check((bool)typeof(R).GetMethod("SnapshotScaleDiffers",Private).Invoke(null,new object[]{scale,5.0}),"Changed scale cannot silently turn unchanged DIMs into new ones");
    }
    static string Row(string kind,int view,R.Segment s)
    {return String.Join("\t",new object[]{kind,view,s.Id,s.Value,s.PaperA,s.PaperB,s.Level,s.X,s.Y,s.HasFeet?1:0,s.FootA,s.FootB,s.HasWitnessGeometry?1:0}.Select(v=>Convert.ToString(v,CultureInfo.InvariantCulture)));}
    public static void Audit(string folder)
    {
        Directory.CreateDirectory(folder);
        var result=R.Analyze(false);
        var old=R.ReadSnapshot(result.BaselinePath,new List<string>());
        var current=(List<R.SnapshotView>)typeof(R).GetMethod("ReadCurrentPresentation",Private).Invoke(null,new object[]{new List<string>()});
        var rows=new List<string>{"# READ ONLY: kind view id value paperA paperB level axisX axisY hasWitness startA startB rendered"};
        rows.AddRange(old.SelectMany(v=>v.Segments.Select(s=>Row("old",v.Id,s))));
        rows.AddRange(current.SelectMany(v=>v.Segments.Select(s=>Row("current",v.Id,s))));
        File.WriteAllLines(Path.Combine(folder,"geometry.tsv"),rows);
        var report=result.Report()+"\r\nUNCHANGED\r\n"+String.Join("\r\n",result.UnchangedItems.Select(i=>i.View+" ID="+i.Id+" "+i.Before+" -> "+i.After+" red="+i.AlreadyRed));
        // Exercise the real form's restoration planner without invoking Refresh/Apply.
        using(var form=new Tekla.Technology.Akit.UserScript.PHU_DimensionReportForm(new Tekla.Technology.Akit.UserScript.PHU_DimensionCheck.Result(),false))
        {
            var type=form.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            type.GetField("revisionResult",flags).SetValue(form,result);
            type.GetMethod("LoadRevisionChoices",flags).Invoke(form,null);
            var restores=(Dictionary<R.Item,Tekla.Structures.Drawing.DrawingColors>)type.GetMethod("RevisionColorRestorations",flags).Invoke(form,null);
            report+="\r\nPLANNED RESTORATIONS (not applied)\r\n"+String.Join("\r\n",restores.Select(p=>p.Key.Id+" "+p.Key.After+" -> "+p.Value));
        }
        File.WriteAllText(Path.Combine(folder,"report.txt"),report);Console.WriteLine(report);
    }
    static void Replay(string path)
    {
        var rows=File.ReadAllLines(path).Where(l=>!l.StartsWith("#")).Select(l=>l.Split('\t')).ToList();
        int unchanged=0,changed=0,added=0,reviewed=0;
        foreach(string view in rows.Select(r=>r[1]).Distinct())
        {
            Func<string,List<List<R.Segment>>> read=kind=>R.Chains(rows.Where(r=>r[0]==kind&&r[1]==view).Select(r=>new R.Segment{
                Id=Int32.Parse(r[2]),Value=Double.Parse(r[3],CultureInfo.InvariantCulture),Text=r[3],PaperA=Double.Parse(r[4],CultureInfo.InvariantCulture),PaperB=Double.Parse(r[5],CultureInfo.InvariantCulture),
                Level=Double.Parse(r[6],CultureInfo.InvariantCulture),X=Double.Parse(r[7],CultureInfo.InvariantCulture),Y=Double.Parse(r[8],CultureInfo.InvariantCulture),HasFeet=r[9]=="1",
                FootA=Double.Parse(r[10],CultureInfo.InvariantCulture),FootB=Double.Parse(r[11],CultureInfo.InvariantCulture),HasWitnessGeometry=r[12]=="1",HasPaperGeometry=true}));
            var old=read("old");var current=read("current");bool[] review;
            var matches=R.MatchChains(current,old,out review);
            for(int c=0;c<current.Count;c++)
            {
                var prior=R.MatchChainSegments(current[c],matches[c]);
                for(int i=0;i<prior.Count;i++)
                {
                    if(review[c])reviewed++;
                    else if(prior[i]==null)added++;
                    else if(Math.Abs(prior[i].Value-current[c][i].Value)>.0001)changed++;
                    else unchanged++;
                }
            }
        }
        Check(unchanged==14&&changed==3&&added==2&&reviewed==0,"Frozen BmC-117 replay: 14 unchanged, 3 changed values, 2 new middle DIMs, no reviews; actual="+unchanged+","+changed+","+added+","+reviewed);
    }
}
