using System;
using System.Collections.Generic;
using System.Linq;
using TTSK_AutoDim_Plates;
using Rect=TTSK_AutoDim_Plates.PHU_SectionLayoutPlanner.Rect;

static class PresentationTests
{
    public sealed class XY { public double X {get;set;} public double Y {get;set;} }
    public sealed class LinePrimitive { public XY StartPoint {get;set;} public XY EndPoint {get;set;} }
    public sealed class Segment
    { public int Id {get;set;} public int ObjectType {get;set;} public List<object> Primitives {get;set;} }
    public sealed class FakeView
    { public int Id {get;set;} public XY Position {get;set;} public List<object> Primitives {get;set;} }
    static LinePrimitive Line(double x1,double y1,double x2,double y2)
    {return new LinePrimitive{StartPoint=new XY{X=x1,Y=y1},EndPoint=new XY{X=x2,Y=y2}};}
    static Segment Box(double l,double b,double r,double t,int type=263)
    {return new Segment{ObjectType=type,Primitives=new List<object>{Line(l,b,r,b),Line(r,b,r,t),Line(r,t,l,t),Line(l,t,l,b)}};}
    static PHU_ArrangePresentation.Data Read(Segment group,double x=0,double y=0)
    {
        return PHU_ArrangePresentation.Parse(new[]{
            new FakeView{Id=1,Position=new XY(),Primitives=new List<object>()},
            new FakeView{Id=-1,Position=new XY{X=x,Y=y},Primitives=new List<object>{group}}},new[]{1},400,300);
    }
    internal static void Run(Action<bool,string> check)
    {
        foreach(double w in new[]{20d,100,350})foreach(double h in new[]{30d,140,260})
        {
            var p=Read(Box(0,0,w,h),25,20);
            check(p.Obstacles.Any(b=>b.Contains(new Rect(25,20,25+w,20+h))),"keyplan dimensions and translation preserved");
            check(p.Obstacles.Count==1,"large keyplan never mistaken for paper frame");
        }
        var frame=Box(0,0,400,300);frame.Primitives.AddRange(Box(10,10,390,40).Primitives);
        var parsed=Read(frame);
        check(!parsed.Obstacles.Any(b=>b.Overlaps(new Rect(100,100,200,200))),"paper frame interior stays available");
        check(parsed.Obstacles.Any(b=>b.Contains(new Rect(150,15,180,35))),"empty title cell is occupied");
        var wholePageKeyplan=Box(0,0,400,300);wholePageKeyplan.Id=42;
        var keyplan=PHU_ArrangePresentation.Parse(new[]{new FakeView{Id=-1,Position=new XY(),Primitives=new List<object>{wholePageKeyplan}}},new int[0],400,300,new[]{42});
        check(keyplan.Obstacles.Count==1&&keyplan.Obstacles[0].Contains(new Rect(1,1,399,299)),"keyplan filling the paper is never treated as a frame");
        bool rejected=false;
        try{Read(new Segment{ObjectType=263,Primitives=new List<object>{new object()}});}catch(NotSupportedException){rejected=true;}
        check(rejected,"unknown visible geometry fails closed");
        rejected=false;
        try{PHU_ArrangePresentation.Parse(new object[0],new[]{1},400,300);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"missing live view rejects stale presentation");
        var type=typeof(PHU_ArrangeView);var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        var key=type.GetMethod("ExtractSectionLetterKey",flags);var compare=type.GetMethod("CompareSectionKeys",flags);
        check((string)key.Invoke(null,new object[]{"AA-AA",true})=="AA","multi-letter section alias preserved");
        check((int)compare.Invoke(null,new object[]{"Z","AA"})<0,"Z before AA");
        check((int)compare.Invoke(null,new object[]{"9","10"})<0,"numeric section order");
    }
}
