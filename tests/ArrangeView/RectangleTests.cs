using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using A=TTSK_AutoDim_Plates.PHU_ArrangeView;
using P=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner;

static class RectangleTests
{
    internal static void Run(Action<bool,string> check)
    {
        var paper=new P.Rect(15,15,826,579);
        foreach(double angle in new[]{0d,90,-90,180}) {
            var r=A.RestrictToRectangle(paper,new Point(1200,500,0),new Point(950,10,0),angle);
            check(r.Left==952&&r.Right==1198&&r.Bottom==12&&r.Top==498,"explicit external rectangle supersedes paper");
            foreach(bool horizontal in new[]{true,false})foreach(bool up in new[]{false,true})
            foreach(var mode in new[]{P.VerticalLayout.LegacyColumns,P.VerticalLayout.AlignedGrid}) {
                var items=Enumerable.Range(0,12).Select(i=>new P.Item{Width=40+i%3*5,Height=30+i%2*10}).ToArray();
                var p=P.Build(r,items,new P.Rect[0],null,10,horizontal,up,mode);
                check(p.Success&&p.Places.All(r.Contains)&&P.ValidatePlan(r,items,new P.Rect[0],null,p),"every mode contained by rectangle");
            }
        }
        foreach(double angle in new[]{10d,45,double.NaN}) {
            bool rejected=false;try{A.RestrictToRectangle(paper,new Point(),new Point(100,100,0),angle);}catch(InvalidOperationException){rejected=true;}
            check(rejected,"skewed/invalid rectangle rejected");
        }
        bool small=false;try{A.RestrictToRectangle(paper,new Point(),new Point(3,3,0),0);}catch(InvalidOperationException){small=true;}
        check(small,"tiny region rejected");
    }
    internal static void Live(string expectedMark)
    {
        Console.WriteLine("MUTATING rectangle integration; all view origins and original selection restored after each case");
        var handler=new DrawingHandler();var drawing=handler.GetActiveDrawing();
        if(drawing==null||drawing.Mark!=expectedMark)throw new Exception("Drawing identity mismatch");
        var selected=new ArrayList();var enumerator=handler.GetDrawingObjectSelector().GetSelected();
        while(enumerator.MoveNext())selected.Add(enumerator.Current);
        var rectangle=selected.OfType<Tekla.Structures.Drawing.Rectangle>().Single();
        var originalStart=new Point(rectangle.StartPoint);var originalEnd=new Point(rectangle.EndPoint);
        var views=new List<View>();enumerator=drawing.GetSheet().GetAllViews();
        while(enumerator.MoveNext()){var v=enumerator.Current as View;if(v!=null)views.Add(v);}
        var origins=views.ToDictionary(SelectionAudit.Id,v=>new Point(v.Origin));
        var scales=views.ToDictionary(SelectionAudit.Id,v=>v.Attributes.Scale);
        var subset=views.Where(v=>v.Name=="B"||v.Name=="C").ToList();
        if(subset.Count!=2)throw new Exception("Missing B/C test fixture");
        var region=A.RestrictToRectangle(new P.Rect(0,0,1,1),rectangle.StartPoint,rectangle.EndPoint,rectangle.Angle);
        Action restore=()=>{
            if(!drawing.IsSameDatabaseObject(handler.GetActiveDrawing()))throw new Exception("Drawing changed; cannot restore");
            foreach(var v in views){v.Select();var p=origins[SelectionAudit.Id(v)];if(Distance.PointToPoint(v.Origin,p)>.001){v.Origin=new Point(p);if(!v.Modify())throw new Exception("Restore Modify failed");}}
            drawing.CommitChanges();
            foreach(var v in views){if(!v.Select()||Distance.PointToPoint(v.Origin,origins[SelectionAudit.Id(v)])>.001)throw new Exception("Restore verification failed");}
        };
        try {
            foreach(bool onlySubset in new[]{false,true})foreach(bool horizontal in new[]{true,false})
            foreach(bool grid in horizontal?new[]{false}:new[]{false,true})foreach(bool up in new[]{false,true}) {
                handler.GetDrawingObjectSelector().UnselectAllObjects();var pick=new ArrayList{rectangle};
                if(onlySubset)foreach(var v in subset)pick.Add(v);
                handler.GetDrawingObjectSelector().SelectObjects(pick,false);
                var result=A.Run(false,horizontal,15,up,grid);
                if(!result.Success||result.SectionCount!=(onlySubset?2:views.Count(v=>v.ViewType==View.ViewTypes.SectionView)))throw new Exception(result.Message);
                foreach(var v in views) {
                    v.Select();bool moves=onlySubset?subset.Contains(v):v.ViewType==View.ViewTypes.SectionView;
                    if(moves){var b=v.GetAxisAlignedBoundingBox();if(!region.Contains(new P.Rect(b.MinPoint.X,b.MinPoint.Y,b.MaxPoint.X,b.MaxPoint.Y)))throw new Exception("View outside Rectangle: "+v.Name);}
                    else if(Distance.PointToPoint(v.Origin,origins[SelectionAudit.Id(v)])>.001)throw new Exception("Unselected view moved");
                    if(v.Attributes.Scale!=scales[SelectionAudit.Id(v)])throw new Exception("Scale changed");
                }
                rectangle.Select();if(Distance.PointToPoint(rectangle.StartPoint,originalStart)>.001||Distance.PointToPoint(rectangle.EndPoint,originalEnd)>.001)throw new Exception("Rectangle changed");
                restore();Console.WriteLine("PASS rectangle "+(onlySubset?"subset":"all")+" horizontal="+horizontal+" grid="+grid+" up="+up+"; restored");
            }
        }
        finally {restore();handler.GetDrawingObjectSelector().UnselectAllObjects();handler.GetDrawingObjectSelector().SelectObjects(selected,false);}
    }
}
