using System;
using System.Linq;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

static class AlignedGridTests
{
    internal static void Run(Action<bool,string> check)
    {
        var six=Enumerable.Range(0,6).Select(i=>new P.Item{Width=40,Height=30}).ToArray();
        foreach(bool up in new[]{false,true})
        {
            var sheet=new P.Rect(0,0,190,100);var main=new P.Rect(0,0,100,100);
            var legacy=P.Build(sheet,six,new[]{main},main,5,false,up);
            check(legacy.Success&&legacy.Bands.Count==2&&legacy.Bands.All(b=>b.Column&&b.Count==3),"legacy default still fills columns first");
            var grid=P.Build(sheet,six,new[]{main},main,5,false,up,P.VerticalLayout.AlignedGrid);
            check(grid.Success&&grid.GridColumns==2&&grid.Bands.All(b=>!b.Column&&b.Count==2),"grid fills rows with common columns");
            check(grid.Places[0].Left==grid.Places[2].Left&&grid.Places[2].Left==grid.Places[4].Left,"column alignment across all rows");
        }
        // Snapshot sizes and origin-to-frame offsets from the approved manual
        // sample. Varying annotation extents must not shift the column axis.
        double[] widths={99.861,101.534,100.866,99.126,76.320,44.598,43.141,74.674,69.919,43.692};
        double[] heights={102.295,102.647,97.524,96.192,67.457,52.363,51.628,67.874,53.463,36.250};
        double[] anchors={49.026,48.859,49.487,47.138,48.722,34.093,8,22.611,35.740,35.692};
        var items=widths.Select((w,i)=>new P.Item{Width=w,Height=heights[i],AnchorX=anchors[i]}).ToArray();
        var paper=new P.Rect(15,15,826,579);var mainRegion=new P.Rect(34.457,67.315,457.135,567.999);
        var obstacles=new[]{mainRegion,new P.Rect(638.555,443.688,825.755,586.1),new P.Rect(8,7.9,830.789,54.1),new P.Rect(821.555,7.9,825.755,586.1)};
        foreach(bool up in new[]{false,true})
        {
            var p=P.Build(paper,items,obstacles,mainRegion,15,false,up,P.VerticalLayout.AlignedGrid);
            check(p.Success&&p.GridColumns==3&&p.GlobalGap>14,"sample fits three aligned columns with bounded gap reduction");
            check(P.ValidatePlan(paper,items,obstacles,mainRegion,p),"sample grid valid");
            for(int i=3;i<items.Length;i++)check(Math.Abs(p.Places[i].Left+anchors[i]-p.Places[i%3].Left-anchors[i%3])<.001,"sample origin axes aligned");
            foreach(var b in p.Bands)check(p.Places.Skip(b.Start).Take(b.Count).All(r=>Math.Abs(r.Bottom-p.Places[b.Start].Bottom)<.001),"sample row baseline aligned");
            var corrupt=p.Places.Select(r=>new P.Rect(r.Left,r.Bottom,r.Right,r.Top)).ToList();
            corrupt[9].Left+=1;corrupt[9].Right+=1;
            check(!P.ValidatePlan(paper,items,obstacles,mainRegion,p,corrupt),"readback rejects a shifted last-row column");
            var again=P.Build(paper,items,obstacles.Reverse().ToArray(),mainRegion,15,false,up,P.VerticalLayout.AlignedGrid);
            check(again.Places.Select(r=>r.ToString()).SequenceEqual(p.Places.Select(r=>r.ToString())),"grid independent of obstacle order");
        }
        foreach(int cols in new[]{1,2,3,6})
        {
            var p=P.Build(new P.Rect(0,0,40*cols+5*(cols-1),250),six,new P.Rect[0],null,5,false,false,P.VerticalLayout.AlignedGrid);
            check(p.Success&&p.GridColumns==cols,"grid column count follows free width");
        }
        var random=new Random(1843);
        for(int run=0;run<80;run++)
        {
            var data=Enumerable.Range(0,random.Next(1,22)).Select(i=>new P.Item{Width=random.Next(20,70),Height=random.Next(20,80),AnchorX=random.Next(0,20)}).ToArray();
            var p=P.Build(new P.Rect(0,0,350,300),data,new[]{new P.Rect(0,0,90,210)},new P.Rect(0,0,90,210),8,false,run%2==0,P.VerticalLayout.AlignedGrid);
            check(p.Success?P.ValidatePlan(new P.Rect(0,0,350,300),data,new[]{new P.Rect(0,0,90,210)},new P.Rect(0,0,90,210),p):p.Places.Count==0&&p.TriedZeroGap,"random grids either fully valid or fail atomically");
        }
    }
}
