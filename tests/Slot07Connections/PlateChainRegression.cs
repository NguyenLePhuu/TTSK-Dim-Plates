using System;
using System.Collections.Generic;
using System.Linq;
using G=Tekla.Structures.Geometry3d;
using S=Tekla.Technology.Akit.UserScript.PHU_Slot07_ColumnConnections;

partial class Program
{
    static S.Part Block(int id,double x0,double x1,double y0,double y1,double z0,double z1,string profile,bool plate=false)
    {
        var p=Box(id,x0,x1,y0,y1,profile,plate);p.Vertices.Clear();
        foreach(double x in new[]{x0,x1})foreach(double y in new[]{y0,y1})foreach(double z in new[]{z0,z1})p.Vertices.Add(P(x,y,z));
        return p;
    }
    static S.View PlateFixture()
    {
        var v=new S.View { Id=1,Scale=15 };
        double[] nodes={-19,1181,1481,2131,2281};
        for(int i=0;i<nodes.Length-1;i++)
        {
            var c=Block(i+1,-125,125,nodes[i]+10,nodes[i+1]-10,-125,125,"STKR250*250*12");
            c.Reference.AddRange(new[]{P(0,nodes[i]),P(0,nodes[i+1])});v.Parts.Add(c);if(i==0)v.Main=c;
        }
        v.Parts.Add(Block(21,-225,225,-19,0,-150,150,"PL19",true));
        v.Parts.Add(Block(22,-150,150,1178,1194,-150,150,"PL16",true));
        v.Parts.Add(Block(23,-150,150,1468,1484,-150,150,"PL16",true));
        v.Parts.Add(Block(24,-150,150,2128,2147,-150,150,"PL19",true));
        v.Parts.Add(Block(25,-150,150,2265,2284,-150,150,"PL19",true));
        for(int i=0;i<2;i++)
        {
            double low=i==0?1181:2131,high=i==0?1481:2281;
            var b=Block(11+i,125,600,low,high,-75,75,i==0?"H300*150*6.5*9":"H150*150*7*10");
            b.Reference.AddRange(new[]{P(0,high),P(600,high)});v.Parts.Add(b);
        }
        v.Grids.Add(new S.Grid{Start=P(0,-100),End=P(0,6000)});
        v.Grids.Add(new S.Grid{Start=P(-2000,-19),End=P(2000,-19)});
        v.Grids.Add(new S.Grid{Start=P(-2000,5881),End=P(2000,5881)});
        return v;
    }
    static List<S.Plan> PlatePlans(S.View v) { return S.BuildColumnGrid(v,Build(v)); }
    static void RejectPlate(S.View v,string reason)
    { bool rejected=false;try{PlatePlans(v);}catch(InvalidOperationException){rejected=true;}Check(rejected,reason); }
    static void PlateTests()
    {
        var v=PlateFixture(); var plans=PlatePlans(v);
        Check(plans.Count==5,"Five complete column/grid plans");
        var chain=plans.Single(p=>p.Kind=="COLUMN-PLATE-CHAIN");
        Check(Values(chain).SequenceEqual(new[]{156.0,644.0,306.0,1197.0}),"Approved plate chain must match ALL segments");
        Check(chain.Feet.Last().X==-225 && chain.Feet.Take(4).All(p=>p.X==-150),"Every chain foot must use actual left plate edge");
        Check(Values(plans.Single(p=>p.Kind=="COLUMN-PLATE-TOTAL")).Single()==2303,"Total to plate top");
        Check(Values(plans.Single(p=>p.Kind=="COLUMN-FLOOR-CHAIN")).SequenceEqual(new[]{3597.0,2303.0}),"Floor chain consumes same top plate foot");
        Check(Values(plans.Single(p=>p.Kind=="FLOOR-TOTAL")).Single()==5900,"Floor total unchanged");
        v.Dimensions.AddRange(plans.Select(Existing));
        Check(PlatePlans(v).All(p=>p.Satisfied),"Approved manual dimensions must be reused");
        var falseFoot=Existing(chain);falseFoot.Feet.ForEach(p=>p.X=0);
        Check(!S.Matches(chain,falseFoot),"Equal segment values with REF feet must not satisfy plate chain");
        v.Parts.Reverse();v.Grids.Reverse();foreach(var p in v.Parts){p.Reference.Reverse();p.Vertices.Reverse();p.Id+=1000;p.Position="renumbered";}
        Check(PlatePlans(v).All(p=>p.Satisfied),"Names, IDs and enumeration must not affect geometry");

        v=PlateFixture();v.Parts.Single(p=>p.Id==4).Vertices.AddRange(new[]{P(-113,2280),P(113,2280)});
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-EDGE-AXIS-EDGE")).SequenceEqual(new[]{125.0,125.0}),"Bevel inner wall must not replace outer edges");
        v=PlateFixture();foreach(var p in v.Parts.Single(p=>p.Id==25).Vertices.Where(p=>p.Y==2284))p.Y+=7;
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-TOTAL")).Single()==2310,"Plate thickness drives total; no fixed +3");
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-CHAIN")).First()==163,"Plate thickness drives connection interval");
        v=PlateFixture();v.Scale=30;
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-CHAIN")).SequenceEqual(Values(chain)),"Scale cannot change feet");
        v=PlateFixture();
        for(int i=0;i<2;i++)
        {
            double a=i==0?2281:2781,b=i==0?2781:2981;
            var col=Block(5+i,-125,125,a+10,b-10,-125,125,"STKR250*250*12");col.Reference.AddRange(new[]{P(0,a),P(0,b)});v.Parts.Add(col);
        }
        v.Parts.Add(Block(31,-150,150,2778,2794,-150,150,"PL16",true));
        v.Parts.Add(Block(32,-150,150,2968,2984,-150,150,"PL16",true));
        var extra=Block(13,125,600,2781,2981,-75,75,"H200*150*6*9");extra.Reference.AddRange(new[]{P(0,2981),P(600,2981)});v.Parts.Add(extra);
        Check(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-CHAIN").Feet.Count==7,"Additional connection stations derive from geometry, not fixed count");
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-TOTAL")).Single()==3003,"Additional connection changes total naturally");
        v=PlateFixture();
        foreach(var part in v.Parts)foreach(var list in new[]{part.Vertices,part.Reference,part.WebBolts})foreach(var p in list){p.X*=1.2;p.Y*=1.2;p.Z*=1.2;}
        foreach(var grid in v.Grids)foreach(var p in new[]{grid.Start,grid.End}){p.X*=1.2;p.Y*=1.2;}
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-CHAIN")).SequenceEqual(new[]{187.2,772.8,367.2,1436.4}),"Resized geometry must not reproduce sample values");

        foreach(double angle in new[]{0.0,Math.PI/2,Math.PI})
        {
            v=PlateFixture();double c=Math.Cos(angle),s=Math.Sin(angle);
            foreach(var part in v.Parts)foreach(var list in new[]{part.Vertices,part.Reference,part.WebBolts})foreach(var p in list)
            {double x=p.X,z=p.Z;p.X=c*x-s*z+12000;p.Z=s*x+c*z+5000;p.Y+=900;}
            foreach(var grid in v.Grids)foreach(var p in new[]{grid.Start,grid.End}){p.X+=12000;p.Y+=900;}
            Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-PLATE-CHAIN")).SequenceEqual(Values(chain)),"Translated, mirrored or end-on beam still resolves plates");
        }
        v=PlateFixture();v.Grids[0].Start.X=v.Grids[0].End.X=40;
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-REF-GRID")).Single()==40,"Actual grid offset");
        v.Grids.Add(new S.Grid{Start=P(1000,-100),End=P(1000,6000)});
        Check(Values(PlatePlans(v).Single(p=>p.Kind=="COLUMN-REF-GRID")).Single()==40,"Farther grid does not steal nearest axis");
        v.Grids.Add(new S.Grid{Start=P(-40,-100),End=P(-40,6000)});RejectPlate(v,"Equal-distance grids must reject");
        v=PlateFixture();v.Parts.RemoveAll(p=>p.Id==23);RejectPlate(v,"Missing plate must not fall back to REF");
        v=PlateFixture();v.Parts.Add(Block(99,-150,150,1468,1484,-150,150,"PL16",true));RejectPlate(v,"Ambiguous plates must reject");
        v=PlateFixture();v.Parts.Single(p=>p.Id==23).Assembly=99;RejectPlate(v,"Different assembly plate must reject");
        v=PlateFixture();v.Parts.RemoveAll(p=>p.Id==3);RejectPlate(v,"Disconnected column refs must reject");
        v=PlateFixture();v.Grids.RemoveAt(2);RejectPlate(v,"Missing floor must reject before writing");
        v=PlateFixture();v.Grids[2].Start.Y=v.Grids[2].End.Y=2281;
        var floorPlan=PlatePlans(v).Single(p=>p.Kind=="FLOOR-TOTAL");v.Dimensions.Add(Existing(floorPlan));
        Check(PlatePlans(v).Single(p=>p.Kind=="FLOOR-TOTAL").Satisfied,"Legitimate grid total coinciding with old REF span must not be called stale");
        v=PlateFixture();foreach(var p in v.Parts.Single(p=>p.Id==11).Reference)p.Y-=150;RejectPlate(v,"Centreline REF cannot invent plate roles");
        v=PlateFixture();var old=new S.Existing{Normal=new G.Vector(-1,0,0)};
        old.Feet.AddRange(new[]{P(0,-19),P(0,1181),P(0,1481),P(0,2131),P(0,2281)});v.Dimensions.Add(old);
        RejectPlate(v,"Old unowned REF chains must cause a visible conflict, never silent duplicates");
    }
}
