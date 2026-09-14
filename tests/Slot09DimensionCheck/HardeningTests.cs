using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using C = Tekla.Technology.Akit.UserScript.PHU_DimensionCheck;
using G = Tekla.Structures.Geometry3d;

// Replays operate on value snapshots only; no drawing mutation or selection.
static class HardeningTests
{
    static int assertions;
    static void Require(bool condition, string message)
    { assertions++; if (!condition) throw new Exception("Hardening: " + message); }
    static G.Point P(double x, double y) { return new G.Point(x, y, 0); }
    static C.Feature F(int owner, string kind, G.Point a, G.Point b)
    { return new C.Feature { Owner=owner, Kind=kind, A=a, B=b, Allowed=true }; }
    static C.Finding Classify(G.Point p, G.Vector n, params C.Feature[] features)
    { return C.Classify(p,n,features); }

    public static void Run()
    {
        assertions=0;
        var n=new G.Vector(0,1,0);
        var hole=F(1,"HOLE",P(0,0),P(0,0));
        Require(C.Classify(P(0,0),n,null).Status=="REVIEW","missing geometry cannot prove an error");
        Require(Classify(P(0,0),n).Status=="REVIEW","empty geometry cannot prove an error");
        var broken=F(2,"EDGE",P(Double.NaN,0),P(0,0));
        Require(Classify(P(1,0),n,hole,broken,null).Status=="REVIEW","partial corrupt geometry is incomplete");
        Require(Classify(P(0,0),n,hole,broken).Status=="OK","valid exact evidence survives partial geometry");
        Require(Classify(P(0,0),new G.Vector(0,0,0),hole).Status=="REVIEW","zero direction");
        Require(Classify(P(0,0),new G.Vector(0,1,1),hole).Status=="REVIEW","out-of-plane direction");
        Require(Classify(P(0,0),new G.Vector(0,Double.NaN,0),hole).Status=="REVIEW","NaN direction");
        Require(Classify(P(1,0),new G.Vector(0,1e300,0),hole).Status=="ERROR","large normal must not overflow to a false OK");
        Require(Classify(P(0,0),new G.Vector(0,1e300,0),hole).Status=="OK","large normal preserves valid feet");

        Require(C.ValidateChain(null,n,1)!=null,"missing chain");
        Require(C.ValidateChain(new[]{P(0,0)},n,1)!=null,"single foot");
        Require(C.ValidateChain(new[]{P(0,0),P(0,50)},n,1)!=null,"same station with displaced origins");
        Require(C.ValidateChain(new[]{P(0,0),P(100,0),P(100,80)},n,1)!=null,"duplicate interior station");
        Require(C.ValidateChain(new[]{P(0,0),P(0.039,50)},n,1)!=null,"sub-tolerance chain");
        Require(C.ValidateChain(new[]{P(0,0),P(0.041,50)},n,1)==null,"short measurable chain");
        Require(C.ValidateChain(new[]{P(0,0),P(100,0)},n,Double.NaN)!=null,"unreadable distance");
        Require(C.ValidateChain(new[]{P(0,0),P(100,0)},n,Double.PositiveInfinity)!=null,"infinite distance");
        Require(C.ValidateChain(new[]{P(0,0),P(100,0)},n,-50)==null,"negative distance is a valid placement side");
        Require(C.ValidateChain(new[]{P(100,20),P(0,0),P(50,-10)},n,0)==null,"API point order need not be geometric order");
        // The displayed 100 mm remains unchanged when BOTH feet drift together.
        var end=F(1,"HOLE",P(100,0),P(100,0));
        Require(C.ValidateChain(new[]{P(1,0),P(101,0)},n,50)==null,"a plausible numeric span alone cannot prove correct feet");
        Require(Classify(P(1,0),n,hole,end).Status=="ERROR","translated chain start is wrong despite unchanged value");
        Require(Classify(P(101,0),n,hole,end).Status=="ERROR","translated chain end is wrong despite unchanged value");

        var near=Classify(P(100,0),n,F(2,"HOLE",P(99,10000),P(99,10000)),F(1,"HOLE",P(90,0),P(90,0)));
        Require(near.Status=="ERROR" && Math.Abs(near.MeasurementResidual-1)<1e-8,"nearest hint follows measurement axis");
        Require(C.UserReason(near).Contains("1 mm"),"user sees actual measurement residual");
        var ambiguous=Classify(P(0,0),n,hole,F(3,"VERTEX",P(0,0),P(0,0)));
        Require(ambiguous.Status=="OK" && ambiguous.CandidateOwners.SequenceEqual(new[]{1,3}),"coincident owners recorded without inventing drafting intent");
        Require(ambiguous.EvidenceKind=="EXACT","exact evidence distinguished");
        Require(Classify(P(0,200),n,hole).EvidenceKind=="PROJECTED","projection evidence distinguished");
        var review=new C.Finding { Status="REVIEW",Reason="CHAIN: Hai chân trùng nhau." };
        Require(C.UserReason(review)=="Hai chân trùng nhau.","review explains chain issue");

        foreach(double angle in new[]{0.0,0.17,Math.PI/4,1.31,Math.PI,4.72})
        foreach(int mirror in new[]{-1,1})
        foreach(double size in new[]{0.5,1.0,7.0})
        {
            Func<G.Point,G.Point> transform=p=>Transform(p,angle,mirror,250000,-110000);
            var normal=TransformNormal(n,angle,mirror);
            var local=new[]{F(1,"VERTEX",P(0,0),P(0,0)),F(1,"VERTEX",P(100*size,40*size),P(100*size,40*size)),
                F(1,"HOLE",P(17*size,23*size),P(17*size,23*size)),F(1,"EDGE",P(0,0),P(100*size,0))};
            var features=local.Select(f=>F(f.Owner,f.Kind,transform(f.A),transform(f.B))).ToList();
            var correct=transform(P(17*size,23*size));
            Require(C.Classify(correct,normal,features).Status=="OK","rotated/mirrored/resized hole");
            foreach(double drift in new[]{-1.0,-0.041,0.041,1.0})
                Require(C.Classify(transform(P(17*size+drift,23*size)),normal,features).Status=="ERROR","measured drift survives arbitrary rotation");
            Require(C.Classify(transform(P(17*size+0.039,23*size)),normal,features).Status=="OK","within tolerance");
            Require(C.Classify(transform(P(17*size,2000)),normal,features).Status=="OK","perpendicular extension displacement");
            features.Reverse();
            Require(C.Classify(correct,new G.Vector(-normal.X*100,-normal.Y*100,0),features).Status=="OK","shuffle, sign and normal magnitude");
            Require(C.Classify(transform(P(51*size,0)),normal,features).Status=="ERROR","longitudinal edge is not a station");
            var points=new[]{transform(P(0,0)),correct,transform(P(100*size,40*size))};
            Require(C.ValidateChain(points,normal,-100)==null,"valid transformed chain");
            Require(C.ValidateChain(new[]{correct,transform(P(17*size,500))},normal,-100)!=null,"transformed zero chain");
        }

        foreach(double angle in new[]{0.0,0.37,Math.PI/2,2.72}) foreach(int mirror in new[]{-1,1})
        {
            var r=Notch(angle,mirror);
            r.GeometryCoverageTracked=true; r.Warnings.Add("VIEW=2 missing solid"); r.IncompleteGeometryViews.Add(2);
            C.CheckNotchPairs(r);
            Require(Target(r).Status=="ERROR","other view failure cannot hide a proven notch error");
            r=Notch(angle,mirror);r.GeometryCoverageTracked=true;r.Warnings.Add("VIEW=1 unsupported curved dimension");
            C.CheckNotchPairs(r);
            Require(Target(r).Status=="ERROR","unsupported unrelated set cannot disable readable notch pair");
            r=Notch(angle,mirror);r.GeometryCoverageTracked=true;r.IncompleteGeometryViews.Add(1);
            C.CheckNotchPairs(r);
            Require(Target(r).Status=="OK","incomplete local contour cannot prove notch relation");
            foreach(string status in new[]{"REVIEW","ERROR"})
            {
                r=Notch(angle,mirror);r.Findings.Single(f=>f.Dimension==11&&f.Foot==1).Status=status;
                C.CheckNotchPairs(r);
                Require(Target(r).Status=="OK","unreliable companion cannot accuse another dimension");
            }
            r=Notch(angle,mirror);r.Warnings.Add("unscoped legacy failure");C.CheckNotchPairs(r);
            Require(Target(r).Status=="OK","unscoped legacy warning remains conservative");
            r=Notch(angle,mirror);r.Findings.Reverse();r.Features[1].Reverse();C.CheckNotchPairs(r);
            Require(Target(r).Status=="ERROR","notch shuffled order");
            var before=r.Findings.Select(f=>f.Status).ToArray();C.CheckNotchPairs(r);
            Require(before.SequenceEqual(r.Findings.Select(f=>f.Status)),"notch repeat is stable");
        }
        Console.WriteLine("PASS "+assertions+" hardening assertions: coverage, chains, evidence, arbitrary rotation/mirror/resize and tolerance.");
    }

    static C.Finding Target(C.Result result) { return result.Findings.Single(f=>f.Dimension==10&&f.Foot==1); }
    static C.Result Notch(double angle,int mirror)
    {
        var r=new C.Result { Main=1 };
        Func<double,double,G.Point> t=(x,y)=>Transform(P(x,y),angle,mirror,250,-110);
        var edges=new List<C.Feature>();
        Action<double,double,double,double> edge=(x,y,bx,by)=>edges.Add(F(1,"EDGE",t(x,y),t(bx,by)));
        edge(0,0,100,0);edge(100,0,100,100);edge(0,80,20,80);edge(20,80,30,90);edge(30,90,30,100);edge(30,100,100,100);
        r.Features[1]=edges;
        var n=TransformNormal(new G.Vector(0,1,0),angle,mirror);
        var other=TransformNormal(new G.Vector(1,0,0),angle,mirror);
        r.Findings.Add(new C.Finding { View=1,Dimension=10,Foot=0,Point=t(0,80),Normal=n,Status="OK" });
        r.Findings.Add(new C.Finding { View=1,Dimension=10,Foot=1,Point=t(50,100),Normal=n,Status="OK" });
        r.Findings.Add(new C.Finding { View=1,Dimension=11,Foot=0,Point=t(30,100),Normal=other,Status="OK" });
        r.Findings.Add(new C.Finding { View=1,Dimension=11,Foot=1,Point=t(0,80),Normal=other,Status="OK" });
        return r;
    }
    static G.Point Transform(G.Point p,double angle,int mirror,double tx,double ty)
    {
        double x=p.X*mirror,y=p.Y,c=Math.Cos(angle),s=Math.Sin(angle);
        return P(x*c-y*s+tx,x*s+y*c+ty);
    }
    static G.Vector TransformNormal(G.Vector n,double angle,int mirror)
    { var p=Transform(n,angle,mirror,0,0);return new G.Vector(p.X,p.Y,0); }

    public static void Live(string reportPath)
    {
        assertions=0;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var result=C.Analyze();
        File.WriteAllText(reportPath,result.Report(),new UTF8Encoding(false));
        Require(result.Dimensions>0 && result.Findings.Count>0,"live snapshot must contain dimensions");
        int replayed=0,wrong=0;
        foreach(var group in result.Findings.GroupBy(f=>f.View))
        {
            var features=result.Features[group.Key];
            foreach(var foot in group.Where(f=>f.Status=="OK"))
            {
                var baseline=C.Classify(foot.Point,foot.Normal,features);
                Require(baseline.Status=="OK","accepted live foot retains geometric support");
                foreach(double angle in new[]{0.37,1.19}) foreach(int mirror in new[]{-1,1})
                {
                    var mapped=features.Select(f=>new C.Feature { Owner=f.Owner,Kind=f.Kind,Allowed=f.Allowed,
                        Infinite=f.Infinite,ConnectedPart=f.ConnectedPart,ExternalMemberReference=f.ExternalMemberReference,
                        A=Transform(f.A,angle,mirror,23000,-41000),B=Transform(f.B,angle,mirror,23000,-41000) }).Reverse().ToList();
                    var actual=C.Classify(Transform(foot.Point,angle,mirror,23000,-41000),TransformNormal(foot.Normal,angle,mirror),mapped);
                    Require(actual.Status==baseline.Status,"live snapshot rotation/mirror/shuffle");
                    replayed++;
                }
            }
            // Select the minimum real measured station across ALL feature endpoints.
            // Moving one millimetre outward cannot accidentally hit another station.
            foreach(var normal in new[]{new G.Vector(0,1,0),new G.Vector(-1,0,0)})
            {
                bool xAxis=normal.Y!=0;
                var outer=features.Where(f=>f.Allowed).SelectMany(f=>new[]{f.A,f.B})
                    .OrderBy(p=>xAxis?p.X:p.Y).First();
                if(C.Classify(outer,normal,features).Status!="OK") continue;
                var shifted=P(outer.X-(xAxis?1:0),outer.Y-(xAxis?0:1));
                Require(C.Classify(shifted,normal,features).Status=="ERROR","live extreme moved 1mm must be caught");wrong++;
            }
        }
        Require(replayed>0 && wrong>0,"live stress must exercise valid and incorrect feet");
        var repeat=C.Analyze();
        Require(result.Drawing==repeat.Drawing && result.Dimensions==repeat.Dimensions
            && result.Findings.Select(f=>f.ToString()).SequenceEqual(repeat.Findings.Select(f=>f.ToString()))
            && result.Warnings.SequenceEqual(repeat.Warnings),"read-only repeat must retain all verdicts");
        timer.Stop();
        string summary="PASS live: "+result.Dimensions+" sets / "+result.Findings.Count+" feet; "+replayed
            +" rotation/mirror replays; "+wrong+" deliberate 1mm errors detected; repeat stable; "+timer.ElapsedMilliseconds+" ms.";
        Console.WriteLine(summary);
        File.AppendAllText(reportPath,Environment.NewLine+summary+Environment.NewLine,new UTF8Encoding(false));
    }
}
