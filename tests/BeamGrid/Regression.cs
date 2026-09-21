using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

static class Regression
{
    static int checks;
    static object New(string name) { return Activator.CreateInstance(Program.Engine.GetNestedType(name, BindingFlags.NonPublic), true); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, Program.Flags).SetValue(o, v); }
    static object Get(object o, string n) { return Program.Get(o, n); }
    static double Num(object o,string n) { return Convert.ToDouble(Get(o,n)); }
    static object P(double x,double y) { return Activator.CreateInstance(Program.Engine.GetNestedType("P2",BindingFlags.NonPublic),x,y); }
    static object Pt(object v,double s,double n) { return Program.Call("FromLocal",v,s,n); }
    static void Check(bool yes,string why) { if(!yes)throw new Exception(why); checks++; }
    static void Mode(int n) { Program.Call("Configure",true,n); }
    static void Axis(object v,double coordinate,bool cross)
    {
        var a=New("GridAxis"); Set(a,"Coordinate",coordinate); Set(a,"IsCrossAxis",cross);
        ((IList)Get(v,"GridAxes")).Add(a);
    }
    static object View(string key,double length=1900,double scale=15,double angle=0,double tx=0,double ty=0)
    {
        var v=New("ViewGeometry"); Set(v,"Key",key); Set(v,"RefLeft",P(tx,ty));
        Set(v,"MainDirection",P(Math.Cos(angle),Math.Sin(angle))); Set(v,"UpDirection",P(-Math.Sin(angle),Math.Cos(angle)));
        Set(v,"RefLength",length); Set(v,"RefRight",Pt(v,length,0)); Set(v,"HasMainSpan",true);
        Set(v,"MainSpanStart",110.0); Set(v,"MainSpanEnd",length-160);
        var t=New("TotalTakeover"); Set(t,"OriginalLeft",Pt(v,110,75)); Set(t,"OriginalRight",Pt(v,length-160,75));
        Set(t,"HorizontalLineNormal",50*scale); Set(t,"HorizontalTier",4);
        Set(t,"HorizontalTierOffset",30*scale); Set(t,"NextHorizontalTierOffset",40*scale); Set(t,"OuterHorizontalTierOffset",50*scale);
        Set(t,"OriginalTop",Pt(v,110,75)); Set(t,"OriginalBottom",Pt(v,110,-75)); Set(t,"HasExactVerticalEndpoints",true);
        Set(t,"VerticalLineStation",-40*scale); Set(t,"VerticalTierOffset",30*scale); Set(t,"NextVerticalTierOffset",40*scale);
        Set(v,"Takeover",t); return v;
    }
    static IList Plans(object v,int mode,bool isTop)
    {
        Mode(mode); Set(v,"Axes",Program.Call("ResolveAxes",v,mode));
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Program.Engine.GetNestedType("DimPlan",BindingFlags.NonPublic)));
        Program.Call(mode==1?"BuildViewPlansMode1":"BuildViewPlans",v,isTop,list);
        Program.Call("ValidatePlans",list); return list;
    }
    static string Signature(IList plans)
    {
        return String.Join("|",plans.Cast<object>().Select(p=>Get(p,"Name")+":"+Num(p,"Distance").ToString("R",CultureInfo.InvariantCulture)+":"+
            String.Join(";",((IEnumerable)Get(p,"Points")).Cast<object>().Select(q=>Num(q,"X").ToString("R",CultureInfo.InvariantCulture)+","+Num(q,"Y").ToString("R",CultureInfo.InvariantCulture)))));
    }
    internal static void Run(bool baseline)
    {
        var signatures = new List<string>();
        // Byte-identical signatures protect all exterior/coincident cases in
        // modes 1/2/3, FRONT mode 1, cross above/below/REF, and tier spacing.
        foreach(int mode in new[]{1,2,3}) foreach(string key in new[]{"TOP","FRONT"})
        foreach(double end in new[]{-500.0,0,1900,2200}) foreach(double cross in new[]{-750.0,0,300})
        {
            var v=View(key); Axis(v,end,false); Axis(v,cross,true);
            if(mode==3)Axis(v,end<=0?2200:-500,false);
            signatures.Add("BASE "+mode+" "+key+" "+end+" "+cross+" "+Signature(Plans(v,mode,key=="TOP")));
        }
        if(baseline) { foreach(string signature in signatures) Console.WriteLine(signature); return; }
        var assembly=typeof(Regression).Assembly;
        using(var reader=new System.IO.StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("legacy-signatures.txt")))))
            Check(reader.ReadToEnd().Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).SequenceEqual(signatures),"Legacy signatures changed");
        foreach(int mode in new[]{1,2}) foreach(string key in new[]{"TOP","FRONT"})
        foreach(double scale in new[]{5.0,15,30}) foreach(double length in new[]{1900.0,3700})
        foreach(double station in new[]{300.0,900}) foreach(double angle in new[]{0.0,0.1})
        foreach(bool reverse in new[]{false,true})
        {
            var v=View(key,length,scale,angle,732,-240); Axis(v,station,false); Axis(v,length,false); Axis(v,-750,true);
            if(reverse) { var axes=(IList)Get(v,"GridAxes"); var a=axes[0]; axes.RemoveAt(0); axes.Add(a); }
            var plans=Plans(v,mode,key=="TOP");
            bool interior=mode==2 || key=="TOP";
            var horizontal=plans.Cast<object>().Where(p=>!Convert.ToString(Get(p,"Name")).Contains("CROSS")).ToList();
            Check(horizontal.Count==(interior?3:2),"Horizontal count/FRONT mode1 isolation");
            if(!interior)continue;
            Check(((IList)Get(horizontal[1],"Points")).Count==3,"Grid chain missing foot");
            var points=(IList)Get(horizontal[1],"Points");
            Check(Math.Abs((double)Program.Call("LocalStation",v,points[1])-station)<1e-7,"Wrong interior station");
            Check(Convert.ToString(Get(horizontal[1],"Name")).Contains("REF-GRID-REF"),"Grid must be middle");
            Check(Convert.ToString(Get(horizontal[2],"Name")).EndsWith("REF-REF"),"Overall must be outer");
            Check(Math.Abs(Num(horizontal[1],"Distance")-60*scale)<1e-7,"Middle scale gap");
            Check(Math.Abs(Num(horizontal[2],"Distance")-70*scale)<1e-7,"Outer scale gap");
            Check((int)Get(v,"HorizontalGridPlanCount")==2,"Outer tier count");
        }
        foreach(int mode in new[]{1,2})
        {
            var v=View("TOP"); Axis(v,300,false); Axis(v,-750,true);
            Check(Plans(v,mode,true).Count==(mode==1?3:4),"Interior without exterior grid");
            Axis(v,900,false);
            Check(Program.Call("ResolveUniqueInteriorEndAxis",v)==null,"Ambiguous interiors must not be guessed");
            foreach(double x in new[]{110.0,110.19,1740,1739.81})
            { v=View("TOP"); Axis(v,x,false); Check(Program.Call("ResolveUniqueInteriorEndAxis",v)==null,"Cut-end tolerance"); }
        }
        foreach(string role in new[]{"TopView","FrontView","BottomView","BackView","SectionView"}) foreach(int mode in new[]{1,2,3})
            Check((bool)Program.Call("IsSupplementaryInteriorRole",role,mode)==((mode==1||mode==2)&&(role=="BottomView"||(mode==2&&role=="BackView"))),"Supplementary role isolation");
        foreach(string key in new[]{"BottomView","BackView"})
        {
            Mode(2);
            var v=View(key); Axis(v,300,false);
            var axes=New("ResolvedAxes"); Set(axes,"InteriorEnd",Program.Call("ResolveUniqueInteriorEndAxis",v));
            Set(v,"Axes",axes); Set(v,"SupplementaryInteriorView",true);
            var context=New("Context"); ((IDictionary)Get(context,"Views")).Add(key,v);
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Program.Engine.GetNestedType("DimPlan",BindingFlags.NonPublic)));
            Program.Call("BuildSupplementaryInteriorPlans",context,list); Program.Call("ValidatePlans",list);
            Check(list.Count==3,"Supplementary three horizontal tiers");
            Check(Convert.ToString(Get(list[1],"Name")).Contains("REF-GRID-REF"),"Supplementary middle grid");
            Set(v,"Takeover",null); list.Clear(); Program.Call("BuildSupplementaryInteriorPlans",context,list);
            Check(list.Count==0,"Missing Shape handoff must not create DIM");
        }
        var countView=View("FRONT"); Axis(countView,300,false); Axis(countView,-750,true);
        Check((int)Program.Call("CountResolvableAxes",countView,2)==2,"Interior axis count without exterior");
        Check((int)Program.Call("CountResolvableAxes",countView,3)==1,"Mode3 cannot acquire interior behavior");
        Program.Call("Reset");
        Console.WriteLine("PASS "+checks+" geometry/tier/isolation checks; "+signatures.Count+" legacy signatures unchanged.");
    }
}
