using System;
using System.Linq;
using TTSK_AutoDim_Plates;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

static class GapTests
{
    static P.Item Item(double width,double height) {return new P.Item{Width=width,Height=height};}
    internal static void Run(Action<bool,string> check)
    {
        // Three sections below the main and two at the right. A top table and
        // a keyplan leave insufficient height for the right pair at GAP 20.
        var sheet=new P.Rect(0,0,400,300);
        var main=new P.Rect(120,140,300,260);
        var obstacles=new[]{main,new P.Rect(0,0,400,32),new P.Rect(310,0,400,110),new P.Rect(310,270,400,300)};
        var items=new[]{Item(70,40),Item(70,40),Item(70,40),Item(60,60),Item(60,60)};
        foreach(bool bottomUp in new[]{false,true})
        {
            var plan=P.Build(sheet,items,obstacles,main,20,true,bottomUp);
            check(plan.Success&&P.ValidatePlan(sheet,items,obstacles,main,plan),"right-only compression is safe");
            check(plan.GlobalGap==20&&plan.RightGap>0&&plan.RightGap<20&&plan.GapAdjusted,"only right cluster GAP reduced");
            check(plan.RightGroup.SequenceEqual(new[]{false,false,false,true,true}),"overflow kept together at the right");
            check(plan.Places.Skip(3).All(p=>p.Left>=main.Right),"neither overflow section sent left");
            var row=P.Build(sheet,items.Take(3).ToArray(),obstacles,main,20,true,bottomUp);
            check(row.Places.Select(p=>p.ToString()).SequenceEqual(plan.Places.Take(3).Select(p=>p.ToString())),"requested-GAP row unchanged");
            check(plan.GapNotice.Contains("cụm phải")&&plan.GapNotice.Contains("20"),"right GAP adjustment reported");
            check(bottomUp?plan.Places[3].Bottom<plan.Places[4].Bottom:plan.Places[3].Bottom>plan.Places[4].Bottom,"right vertical order retained");
        }
        // No right corridor: GAP must be reduced globally before a full row fits.
        var small=new P.Rect(0,0,240,180);var wideMain=new P.Rect(0,80,240,180);
        var four=Enumerable.Range(0,4).Select(i=>Item(50,50)).ToArray();
        var uniform=P.Build(small,four,new[]{wideMain},wideMain,30,true,false);
        check(uniform.Success&&P.ValidatePlan(small,four,new[]{wideMain},wideMain,uniform),"global reduction fits without changing scale");
        check(uniform.GlobalGap>0&&uniform.GlobalGap<30&&uniform.GlobalGap==uniform.RightGap,"uniform global GAP");
        check(uniform.RequestedGap==30&&uniform.GapNotice.Contains("toàn bộ"),"requested and used global GAP reported");
        for(int i=1;i<four.Length;i++)
            check(Math.Abs(uniform.Places[i].Left-uniform.Places[i-1].Right-uniform.GlobalGap)<.001,"global row gaps are even");
        // Exact fit only at zero; excessive request must not cause an early rejection.
        var exactSheet=new P.Rect(0,0,100,50);var pair=new[]{Item(50,50),Item(50,50)};
        var zero=P.Build(exactSheet,pair,new P.Rect[0],null,25,true,false);
        check(zero.Success&&zero.GlobalGap==0&&zero.GapAdjusted,"zero-gap exact fit is tried and accepted");
        var impossible=P.Build(exactSheet,new[]{Item(101,50)},new P.Rect[0],null,25,true,false);
        check(!impossible.Success&&impossible.TriedZeroGap&&impossible.GlobalGap==0&&impossible.Places.Count==0,"spatial failure only after zero-gap attempt");
        check(impossible.Error.Contains("0 mm"),"zero-gap failure clearly reported");
        var kept=P.Build(new P.Rect(0,0,300,100),pair,new P.Rect[0],null,10,true,false);
        check(kept.Success&&!kept.GapAdjusted&&kept.GlobalGap==10&&kept.GapNotice=="","fitting requested GAP remains unchanged");
        // Read-back must check each group's spacing, not erroneously require the
        // requested GAP inside a deliberately compressed right cluster.
        var local=P.Build(sheet,items,obstacles,main,20,true,false);
        var bad=local.Places.Select(p=>new P.Rect(p.Left,p.Bottom,p.Right,p.Top)).ToList();
        bad[4]=new P.Rect(bad[3].Left,bad[3].Bottom,bad[3].Right,bad[3].Top);
        check(!P.ValidatePlan(sheet,items,obstacles,main,local,bad),"read-back still rejects overlap after adaptive GAP");
    }
}
