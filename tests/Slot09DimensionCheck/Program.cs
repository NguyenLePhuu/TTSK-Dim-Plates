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
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==2 && args[0]=="--live")
            {
                var result=C.Analyze();
                File.WriteAllText(args[1],result.Report(),new UTF8Encoding(false));
                Console.WriteLine(result.Summary);
                foreach(var f in result.Findings.Where(f=>f.Status!="OK")) Console.WriteLine(f);
                foreach(var w in result.Warnings) Console.WriteLine("INCOMPLETE "+w);
                // Golden pair replay: exact IDs identify test fixtures only, never production rules.
                int replay=0;
                foreach(var foot in result.Findings.Where(f=>f.Foot==0 && new[]{504753,504857,504982,505078}.Contains(f.Dimension)))
                {
                    Check("live corrected VIEW="+foot.View+" DIM="+foot.Dimension,"OK",foot.Point,foot.Normal,result.Features[foot.View]);
                    Check("live wrong bottom replay VIEW="+foot.View+" DIM="+foot.Dimension,"ERROR",
                        new G.Point(-75,-1,0),foot.Normal,result.Features[foot.View]);
                    replay++;
                }
                if(replay!=4) throw new Exception("Golden fixture changed: expected 4 corrected chains across two views, found "+replay);
                var again=C.Analyze();
                string before=String.Join("\n",result.Findings.Select(f=>f.ToString()));
                string after=String.Join("\n",again.Findings.Select(f=>f.ToString()));
                if(before!=after) throw new Exception("Read-only repeat differs; drawing may have changed.");
                Console.WriteLine("PASS live: four corrected/wrong-bottom pairs, both views; repeated audit identical; feet="+result.Findings.Count);
                return 0;
            }
            var fs=new List<C.Feature> { F(1,"EDGE",-75,0,-75,6019,true), F(1,"VERTEX",-75,0,-75,0,true),
                F(1,"VERTEX",-75,6019,-75,6019,true),F(2,"NEIGHBOR EDGE",-500,-1,500,-1,false) };
            var normal=new G.Vector(-1,0,0);
            // Both red-box views had the same wrong lower-edge coordinate. IDs/order differ.
            foreach(string view in new[]{"Front","Top"})
            {
                Check(view+" wrong bottom 1mm","ERROR",new G.Point(-75,-1,0),normal,fs);
                Check(view+" corrected bottom","OK",new G.Point(-75,0,0),normal,fs);
            }
            Check("off-edge foot is not on longitudinal projection interval","ERROR",new G.Point(-82.937,179,0),normal,fs);
            Check("far free foot","ERROR",new G.Point(-355.096,6679,0),normal,fs);
            Check("extension shifted from true vertex","REVIEW",new G.Point(-200,0,0),normal,fs);
            fs.Add(F(3,"CONNECTED REF",0,100,200,100,true));
            Check("direct neighbor reference","OK",new G.Point(100,100,0),normal,fs);
            fs.Add(F(3,"CONNECTED EDGE",0,90,200,90,false));
            Check("connected neighbor edge is not reference","ERROR",new G.Point(100,90,0),normal,fs);
            fs.Add(F(4,"UNCONNECTED REF",0,200,200,200,false));
            Check("unconnected neighbor reference","ERROR",new G.Point(100,200,0),normal,fs);
            fs.Add(F(1,"HOLE",5,50,5,50,true));
            Check("hole","OK",new G.Point(5,50,0),normal,fs);
            Check("shifted hole origin","REVIEW",new G.Point(-20,50,0),normal,fs);
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
            Console.WriteLine("PASS "+count+" classifier cases"); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
