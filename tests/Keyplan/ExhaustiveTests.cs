using System;
using System.Linq;
using TTSK_AutoDim_Plates.Keyplan;
static class ExhaustiveTests
{
    static int checks,scenarios;
    static void Require(bool ok,string why) { checks++;if(!ok)throw new Exception(why); }
    static string Key(Angles a) { return a.X+","+a.Y+","+a.Z; }
    // Independent integer matrices; no production rotation calls.
    static readonly int[][,] Rx={new[,]{{1,0,0},{0,1,0},{0,0,1}},new[,]{{1,0,0},{0,0,1},{0,-1,0}},new[,]{{1,0,0},{0,-1,0},{0,0,-1}},new[,]{{1,0,0},{0,0,-1},{0,1,0}}};
    static readonly int[][,] Rz={new[,]{{1,0,0},{0,1,0},{0,0,1}},new[,]{{0,1,0},{-1,0,0},{0,0,1}},new[,]{{-1,0,0},{0,-1,0},{0,0,1}},new[,]{{0,-1,0},{1,0,0},{0,0,1}}};
    static readonly int[,] Ry={{-1,0,0},{0,1,0},{0,0,-1}};
    static V Multiply(int[,] m,V v)
    { var a=new[]{v.X,v.Y,v.Z};var b=new double[3];for(int i=0;i<3;i++)for(int j=0;j<3;j++)b[i]+=m[i,j]*a[j];return new V(b[0],b[1],b[2]); }
    static Frame Oracle(Frame basis,bool vertical,string type,Angles a)
    {
        Func<V,V> transform=v=> {v=Multiply(Rz[a.Z/90],v);if(a.Y==180)v=Multiply(Ry,v);v=Multiply(Rx[a.X/90],v);return basis.Right*v.X+basis.Up*v.Y+V.Cross(basis.Right,basis.Up)*v.Z;};
        V localUp;
        switch(type){case "FrontView":localUp=new V(0,1,0);break;case "BackView":localUp=new V(0,-1,0);break;case "TopView":localUp=new V(0,0,-1);break;case "BottomView":localUp=new V(0,0,1);break;default:throw new Exception("Unknown oracle view");}
        V r=transform(new V(1,0,0)),u=transform(localUp);
        if(!vertical)return new Frame(r,u);
        return basis.Right.Z<0?new Frame(u,r*(-1)):new Frame(u*(-1),r);
    }
    static bool Same(Frame a,Frame b) {return Geometry.Error(a,b)<1e-7;}
    internal static void Run()
    {
        var allowed=Geometry.AllowedAngles().ToArray();Require(allowed.Length==32&&allowed.Select(Key).Distinct().Count()==32,"32 unique triples");
        foreach(double yaw in new[]{0.0,37.25,-83.4})foreach(double roll in new[]{0.0,1.23,90.0})foreach(int member in new[]{0,1,2})
        {
            double a=yaw*Math.PI/180;var gx=new V(Math.Cos(a),Math.Sin(a),0);var z=new V(0,0,1);
            var basic=member==0?new Frame(gx,z):new Frame(z*(member==1?1:-1),gx*(-1));
            double b=roll*Math.PI/180;var basis=new Frame(basic.Right,basic.Up*Math.Cos(b)+basic.Normal*Math.Sin(b));
            foreach(var pair in new[]{new[]{"FrontView","TopView"},new[]{"FrontView","BottomView"},new[]{"BackView","TopView"},new[]{"BackView","BottomView"},new[]{"FrontView","BackView"},new[]{"TopView","BottomView"}})
            foreach(bool reverseOrder in new[]{false,true})
            {
                var types=reverseOrder?pair.Reverse().ToArray():pair;
                var observed=allowed.ToDictionary(Key,v=>types.Select(t=>Oracle(basis,member!=0,t,v)).ToArray());
                foreach(var to in allowed)
                {
                    var target=observed[Key(to)];scenarios++;
                    var expected=allowed.Where(v=>observed[Key(v)].Zip(target,Same).All(ok=>ok)).Select(Key).OrderBy(s=>s);
                    var search=Geometry.SearchLocal(basis,member!=0,types,target);
                    Require(search.Evaluated.Count==32,"all combinations evaluated");
                    Require(search.Matches.Select(c=>Key(c.Angles)).OrderBy(s=>s).SequenceEqual(expected),"independent matrix oracle mismatch");
                    Require(search.Evaluated.All(c=>Geometry.Finite(c.Error)),"finite candidate errors");
                }
            }
        }
        Console.WriteLine("PASS independent integer-matrix oracle: "+scenarios+" searches / "+checks+" assertions / "+scenarios*32+" candidate comparisons.");
    }
}
