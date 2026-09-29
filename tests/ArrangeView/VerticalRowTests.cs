using System;
using System.Linq;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

static class VerticalRowTests
{
    internal static void Run(Action<bool,string> check)
    {
        var six=Enumerable.Range(0,6).Select(i=>new P.Item{Key=((char)('A'+i)).ToString(),Width=40,Height=30}).ToArray();
        foreach(bool up in new[]{false,true})foreach(int columns in new[]{1,2,3,6})
        {
            var sheet=new P.Rect(0,0,columns*40+(columns-1)*5,220);
            var plan=P.Build(sheet,six,new P.Rect[0],null,5,false,up,P.VerticalLayout.AdaptiveRows);
            check(plan.Success&&!plan.GapAdjusted,"row grid fits original gap");
            check(plan.Bands.All(b=>!b.Column)&&plan.Bands[0].Count==columns,"column count follows available width");
            check(plan.Bands.Count==(6+columns-1)/columns,"row count follows actual capacity");
            check(P.ValidatePlan(sheet,six,new P.Rect[0],null,plan),"grid geometry and reading order valid");
            foreach(var band in plan.Bands)
                for(int i=band.Start+1;i<band.Start+band.Count;i++)
                    check(plan.Places[i].Left>=plan.Places[i-1].Right+5-P.Epsilon,"alphabet reads left to right");
            for(int b=1;b<plan.Bands.Count;b++)
            {
                var previous=plan.Places.Skip(plan.Bands[b-1].Start).Take(plan.Bands[b-1].Count);
                var current=plan.Places.Skip(plan.Bands[b].Start).Take(plan.Bands[b].Count);
                check(up?current.Min(r=>r.Bottom)>=previous.Max(r=>r.Top)+5-P.Epsilon:
                    current.Max(r=>r.Top)<=previous.Min(r=>r.Bottom)-5+P.Epsilon,"rows follow selected up/down direction");
            }
        }
        // Right corridor has two columns; the main remains an obstacle.
        foreach(bool up in new[]{false,true})
        {
            var sheet=new P.Rect(0,0,190,160);var main=new P.Rect(0,0,100,160);
            var p=P.Build(sheet,six,new[]{main},main,5,false,up,P.VerticalLayout.AdaptiveRows);
            check(p.Success&&p.Bands.All(b=>b.Count==2),"two-column right grid");
            check(p.Places.All(r=>r.Left>=105),"right-hand preference preserved");
            var corrupted=p.Places.ToList();var tmp=corrupted[1];corrupted[1]=corrupted[2];corrupted[2]=tmp;
            check(!P.ValidateReadingOrder(p,corrupted),"column-first permutation rejected by readback");
        }
        // A table narrows one end of the space: rows have different capacities.
        var paper=new P.Rect(0,0,130,150);
        foreach(bool up in new[]{false,true})
        {
            var table=up?new P.Rect(90,0,130,30):new P.Rect(90,120,130,150);
            var p=P.Build(paper,six,new[]{table},null,5,false,up,P.VerticalLayout.AdaptiveRows);
            check(p.Success&&P.ValidatePlan(paper,six,new[]{table},null,p),"table changes free row intervals");
            check(p.Bands[0].Count==2&&p.Bands[1].Count==3,"row capacity adapts from two to three columns");
        }
        // Different extents must remain attached to their letter, never merely
        // relabel a previously packed column grid.
        var mixed=new[]{new P.Item{Width=65,Height=25},new P.Item{Width=30,Height=55},
            new P.Item{Width=48,Height=18},new P.Item{Width=22,Height=32},new P.Item{Width=72,Height=41}};
        foreach(bool up in new[]{false,true})
        {
            var p=P.Build(new P.Rect(0,0,110,240),mixed,new P.Rect[0],null,7,false,up,P.VerticalLayout.AdaptiveRows);
            check(p.Success&&P.ValidatePlan(new P.Rect(0,0,110,240),mixed,new P.Rect[0],null,p),"mixed-size row grid preserves view dimensions");
            check(p.Bands.Count>=3&&p.Bands.All(b=>!b.Column),"mixed sizes wrap by row");
        }
    }
}
