using System;
using System.Linq;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

static class ReadingOrderTests
{
    internal static void Run(Action<bool,string> check)
    {
        // Read-only snapshot of [2-h-47], 2026-09-22. This reproduces the user's
        // GAP 35 scatter and GAP >40 return-to-the-left failure without Tekla.
        var sheet=new P.Rect(15,15,826,579);
        var main=new P.Rect(121.002,214.383,655.140,501.705);
        var obstacles=new[]{
            new P.Rect(188.929,364.983,635.197,501.705),
            new P.Rect(121.002,214.383,655.140,354.871),
            new P.Rect(700.719,54.900,820.755,265.130),
            new P.Rect(702.313,54.250,820.755,265.130),
            new P.Rect(414.404,7.900,830.789,54.100),
            new P.Rect(8,8,418.504,54),
            new P.Rect(638.555,539.700,825.755,586.100),
            new P.Rect(638.555,527.688,825.755,544.188),
            new P.Rect(326.900,52.900,514.100,66.100),
            new P.Rect(821.555,7.484,850.724,47.796),
            new P.Rect(821.555,7.900,825.755,586.100),
            new P.Rect(7.900,49.900,825.755,54.100)};
        var items=new[]{
            new P.Item{Key="A",Width=72.685,Height=65.984},
            new P.Item{Key="B",Width=70.963,Height=80.414},
            new P.Item{Key="C",Width=88.747,Height=63.122},
            new P.Item{Key="D",Width=88.867,Height=84.932},
            new P.Item{Key="E",Width=100.224,Height=86.918},
            new P.Item{Key="F",Width=69.777,Height=94.829},
            new P.Item{Key="G",Width=77.452,Height=79.084}};
        foreach(double gap in new[]{0d,5,15,30,35,40,41,45,50,60,100})
        {
            foreach(bool horizontal in new[]{true,false})foreach(bool bottomUp in new[]{false,true})
            {
                var p=P.Build(sheet,items,obstacles,main,gap,horizontal,bottomUp);
                check(p.Success&&P.ValidatePlan(sheet,items,obstacles,main,p),"live fixture fits ordered GAP="+gap);
                check(p.Bands.Sum(b=>b.Count)==items.Length,"every letter occurs once");
                if(horizontal && gap>=35)
                {
                    check(p.GlobalGap>0&&p.GlobalGap<gap&&p.RightGap==p.GlobalGap,"large GAP uses balanced global reduction");
                    check(p.RightGroup.SequenceEqual(new[]{false,false,false,false,false,true,true}),"A-E stay in the lower row, F-G at right");
                    for(int i=1;i<5;i++)
                        check(p.Places[i].Left>p.Places[i-1].Right&&Math.Abs(p.Places[i].Bottom-p.Places[0].Bottom)<.001,"physical A-E alphabetical order");
                    check(bottomUp?p.Places[6].Bottom>p.Places[5].Top:p.Places[6].Top<p.Places[5].Bottom,"physical F-G order follows selected direction");
                    check(p.Places.Skip(5).All(r=>r.Left>=main.Right),"right letters never backfill the lower row");
                }
            }
        }
        // The old planner accepted both layouts as collision-free. Neither is a
        // valid reading order, even though the list of input letters is sorted.
        var rowBackfill=new P.Plan{Horizontal=true};
        rowBackfill.Places.AddRange(new[]{new P.Rect(60,20,70,30),new P.Rect(80,20,90,30),new P.Rect(0,20,10,30)});
        rowBackfill.Bands.Add(new P.Band{Start=0,Count=2});rowBackfill.Bands.Add(new P.Band{Start=2,Count=1});
        check(!P.ValidateReadingOrder(rowBackfill,rowBackfill.Places),"later letter cannot return to the left of an existing row");
        var columnBackfill=new P.Plan();
        columnBackfill.Places.AddRange(new[]{new P.Rect(60,60,70,70),new P.Rect(60,40,70,50),new P.Rect(30,60,40,70)});
        columnBackfill.Bands.Add(new P.Band{Start=0,Count=2,Column=true});columnBackfill.Bands.Add(new P.Band{Start=2,Count=1,Column=true});
        check(!P.ValidateReadingOrder(columnBackfill,columnBackfill.Places),"later column cannot return left");
        var descendingRows=new P.Plan{Horizontal=true};
        descendingRows.Places.AddRange(new[]{new P.Rect(20,60,30,70),new P.Rect(40,60,50,70),new P.Rect(20,30,30,40)});
        descendingRows.Bands.Add(new P.Band{Start=0,Count=2});descendingRows.Bands.Add(new P.Band{Start=2,Count=1});
        check(P.ValidateReadingOrder(descendingRows,descendingRows.Places),"next alphabetical row may start at the left on a lower baseline");
    }
}
