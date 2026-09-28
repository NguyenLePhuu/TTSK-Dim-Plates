using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;

class Program
{
    static string Pt(G.Point p) { return string.Format("({0:0.###},{1:0.###},{2:0.###})", p.X,p.Y,p.Z); }
    static int Main(string[] args)
    {
        try {
            if(args.Length>1) Engine=Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1])).GetType("PHU_OpenGridView",true);
            if(args.Length>0 && args[0]=="--grid-baseline") Console.WriteLine(GridSignatures());
            else if(args.Length>0 && args[0]=="--tests") Regression(); else Audit(); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static Type Engine;
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static object Call(string name, params object[] args) { return Engine.GetMethod(name,Flags).Invoke(null,args); }
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    static object Get(object obj,string name) { return obj.GetType().GetField(name,Flags)?.GetValue(obj); }
    static string GridSignatures()
    {
        var output=new List<string>();var type=Engine.GetNestedType("GridSeg",Flags);
        for(int scenario=0;scenario<12;scenario++)
        {
            var segments=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
            for(int i=0;i<8;i++)
            {
                var g=Activator.CreateInstance(type,true); bool vertical=i<4;
                double coord=(i%4<2?-1:1)*(50+(i%2)*70+scenario*9);
                foreach(var pair in new Dictionary<string,object>{{"Label","G"+i},{"IsVertical",vertical},{"IsHorizontal",!vertical},{"ConstX",vertical?coord:0.0},{"ConstY",vertical?0.0:coord}})
                    type.GetField(pair.Key,Flags).SetValue(g,pair.Value);
                if(scenario%3!=0 || i%2==0) segments.Add(g);
            }
            if(scenario%2==0){var a=segments.Cast<object>().Reverse().ToArray();segments.Clear();foreach(var g in a)segments.Add(g);}
            Func<object,string> axis=g=>g==null?"none":Get(g,"Label")+"@"+Get(g,"ConstX")+","+Get(g,"ConstY");
            object[] common={segments,-30.0,90.0,-20.0,40.0,-40.0,100.0,-30.0,50.0};
            var candidates=Call("FindSingleAxisCandidates",common);
            output.Add(scenario+" mode1 "+string.Join("|",new[]{"LeftVertical","RightVertical","BottomHorizontal","TopHorizontal"}.Select(n=>axis(Get(candidates,n)))));
            foreach(int mode in new[]{2,3})
            {
                var args=common.Concat(new object[]{mode}).ToArray();var selected=Call("SelectNearestGridAxes",args);
                output.Add(scenario+" mode"+mode+" "+Get(selected,"VerticalIsLeft")+" "+string.Join("|",new[]{"Vertical","Bottom","Top"}.Select(n=>axis(Get(selected,n)))));
            }
        }
        return string.Join("\n",output);
    }
    static void Regression()
    {
        var baselinePath=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"grid-baseline.txt");
        Check(System.IO.File.ReadAllText(baselinePath).Replace("\r","").Trim()==GridSignatures(),"Legacy grid selection changed");
        var baseline=new G.AABB(new G.Point(0,0,-10),new G.Point(100,50,10));
        var envelope=new G.AABB(new G.Point(-30,-90,-100),new G.Point(250,130,100));
        for(int mask=0;mask<16;mask++)
        {
            var box=(G.AABB)Call("MergeFitEnvelope",baseline,envelope,20.0,(mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0);
            Check(box.MinPoint.X==((mask&1)!=0?0:-50)&&box.MaxPoint.X==((mask&2)!=0?100:270)&&box.MinPoint.Y==((mask&4)!=0?0:-110)&&box.MaxPoint.Y==((mask&8)!=0?50:150),"Grid lock mask "+mask);
            Check(box.MinPoint.Z==-10 && box.MaxPoint.Z==10,"Depth changed");
            var repeat=(G.AABB)Call("MergeFitEnvelope",box,envelope,20.0,(mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0);
            Check(Pt(box.MinPoint)==Pt(repeat.MinPoint)&&Pt(box.MaxPoint)==Pt(repeat.MaxPoint),"Fit grows on repeat");
        }
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1.0})
        { bool rejected=false;try{Call("MergeFitEnvelope",baseline,envelope,bad,false,false,false,false);}catch(TargetInvocationException){rejected=true;}Check(rejected,"Bad padding accepted"); }
        var edges=new List<G.Point[]>{new[]{new G.Point(-5,-5000,0),new G.Point(-5,5000,0)},new[]{new G.Point(5,5000,0),new G.Point(5,-5000,0)}};
        var clipped=((IEnumerable<G.Point>)Call("FitClipEdges",edges,new G.Vector(0,1,0),-100.0,150.0)).ToArray();
        Check(clipped.Min(p=>p.Y)==-100&&clipped.Max(p=>p.Y)==150&&clipped.Min(p=>p.X)==-5&&clipped.Max(p=>p.X)==5,"Stub lost cross-section / did not clip length");
        var reverse=((IEnumerable<G.Point>)Call("FitClipEdges",edges.AsEnumerable().Reverse(),new G.Vector(0,-1,0),-150.0,100.0)).ToArray();
        Check(reverse.Min(p=>p.Y)==-100&&reverse.Max(p=>p.Y)==150,"Mirrored member");
        var loops=new List<G.Point[]>{new[]{new G.Point(0,0,0),new G.Point(10,0,0),new G.Point(10,10,0),new G.Point(0,10,0)},new[]{new G.Point(4,4,0),new G.Point(6,4,0),new G.Point(6,6,0),new G.Point(4,6,0)}};
        Check((bool)Call("FitPointOnFace",new G.Point(2,2,.05),loops,.1),"Face contact missed");
        Check(!(bool)Call("FitPointOnFace",new G.Point(5,5,0),loops,.1),"Hole counted as contact");
        Check(!(bool)Call("FitPointOnFace",new G.Point(12,5,0),loops,.1),"AABB-only false contact");
        Check(!(bool)Call("FitPointOnFace",new G.Point(2,2,2),loops,.1),"Depth-only false contact");
        // Translation/rotation must not change longitudinal clipping or face contact.
        double c=Math.Cos(.63),s=Math.Sin(.63);
        Func<G.Point,G.Point> move=p=>new G.Point(300+c*p.X-s*p.Y,-900+s*p.X+c*p.Y,70+p.Z);
        var rotated=edges.Select(e=>e.Select(move).ToArray()).ToArray();
        var direction=new G.Vector(-s,c,0);double shift=300*direction.X-900*direction.Y;
        var transformed=((IEnumerable<G.Point>)Call("FitClipEdges",rotated,direction,-100.0+shift,150.0+shift)).ToArray();
        Check(transformed.Length==clipped.Length && transformed.Zip(clipped.Select(move),(a,b)=>G.Distance.PointToPoint(a,b)).Max()<1e-6,"Rotated/translated clipping changed");
        Check((bool)Call("FitPointOnFace",move(new G.Point(2,2,.05)),loops.Select(l=>l.Select(move).ToArray()).ToList(),.1),"Rotated contact missed");
        // Shape-style standalone fitting must shrink oversized XY, recover a cropped
        // member, keep depth and be independent of the previous frame on repeated runs.
        foreach(var previous in new[]{baseline,
            new G.AABB(new G.Point(-5000,-4000,-10),new G.Point(9000,8000,10)),
            new G.AABB(new G.Point(20,20,-10),new G.Point(30,30,10))})
        {
            var direct=(G.AABB)Call("FinishFitGeometryEnvelope",previous,envelope,20.0,false);
            Check(Pt(direct.MinPoint)==Pt(new G.Point(-50,-110,-10))
                &&Pt(direct.MaxPoint)==Pt(new G.Point(270,150,10)),"Standalone must fit geometry + 20, not old XY");
            var again=(G.AABB)Call("FinishFitGeometryEnvelope",direct,envelope,20.0,false);
            Check(Pt(direct.MinPoint)==Pt(again.MinPoint)&&Pt(direct.MaxPoint)==Pt(again.MaxPoint),"Standalone grows on repeat");
            var legacy=(G.AABB)Call("FinishFitGeometryEnvelope",previous,envelope,20.0,true);
            var expected=(G.AABB)Call("MergeFitEnvelope",previous,envelope,20.0,false,false,false,false);
            Check(Pt(legacy.MinPoint)==Pt(expected.MinPoint)&&Pt(legacy.MaxPoint)==Pt(expected.MaxPoint),"Open Grid floor changed");
        }
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1.0})
        { bool rejected=false;try{Call("FinishFitGeometryEnvelope",baseline,envelope,bad,false);}catch(TargetInvocationException){rejected=true;}Check(rejected,"Standalone bad padding accepted"); }
        Console.WriteLine("PASS: standalone oversized/cropped frames, exact 20 mm gap, unchanged Z, repeat and Open Grid floor regression.");
        Console.WriteLine("PASS: 36 frozen legacy grid cases; 16 side-lock masks; depth, repeat, invalid inputs; mirrored/rotated/translated stub; solid face and holes.");
    }
    static void Audit()
    {
        Console.WriteLine("ACTIVE DRAWING AUDIT - READ ONLY; no drawing/model/work-plane mutation.");
        var m = new M.Model(); var h = new D.DrawingHandler();
        Console.WriteLine("Model="+m.GetConnectionStatus()+" Drawing="+h.GetConnectionStatus());
        if(!m.GetConnectionStatus()||!h.GetConnectionStatus()) throw new Exception("Connection failed");
        var d=h.GetActiveDrawing(); if(d==null) throw new Exception("No active drawing");
        Console.WriteLine("Drawing="+d.GetType().Name+" "+d.Name+" "+d.Mark+" Model="+m.GetInfo().ModelPath);
        var ad=d as D.AssemblyDrawing; var sd=d as D.SinglePartDrawing;
        var obj=m.SelectModelObject(ad!=null?ad.AssemblyIdentifier:sd.PartIdentifier);
        var main=obj as M.Part ?? (obj as M.Assembly)?.GetMainPart() as M.Part;
        if(main==null) throw new Exception("No main part");
        Console.WriteLine("MAIN "+main.Identifier.ID+" "+main.Profile.ProfileString);
        var toGlobal=m.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
        object context=null;
        if(Engine!=null)
        {
            context=Activator.CreateInstance(Engine.GetNestedType("FitEnvelopeContext",Flags),Flags,null,new object[]{d,m},null);
            foreach(string group in new[]{"Core","Neighbors"})
            { var parts=(IDictionary)context.GetType().GetField(group,Flags).GetValue(context);Console.WriteLine("PLAN "+group+"="+string.Join(",",parts.Keys.Cast<object>())); }
        }
        var views=d.GetSheet().GetAllViews();
        while(views.MoveNext())
        {
            var v=views.Current as D.View; if(v==null)continue;
            var toView=G.MatrixFactory.ToCoordinateSystem(v.DisplayCoordinateSystem);
            var box=v.RestrictionBox;
            if(context!=null)
            {
                var direct=(G.AABB)Call("BuildFitGeometryEnvelope",context,v,box,20.0,false);
                var huge=new G.AABB(new G.Point(box.MinPoint.X-5000,box.MinPoint.Y-5000,box.MinPoint.Z),new G.Point(box.MaxPoint.X+5000,box.MaxPoint.Y+5000,box.MaxPoint.Z));
                var shrink=(G.AABB)Call("BuildFitGeometryEnvelope",context,v,huge,20.0,false);
                var repeatDirect=(G.AABB)Call("BuildFitGeometryEnvelope",context,v,direct,20.0,false);
                Check(G.Distance.PointToPoint(direct.MinPoint,shrink.MinPoint)<1e-6&&G.Distance.PointToPoint(direct.MaxPoint,shrink.MaxPoint)<1e-6,"Standalone depends on old XY");
                Check(G.Distance.PointToPoint(direct.MinPoint,repeatDirect.MinPoint)<1e-6&&G.Distance.PointToPoint(direct.MaxPoint,repeatDirect.MaxPoint)<1e-6,"Standalone live geometry grows");
                Console.WriteLine("PLAN standalone "+v.ViewType+" "+Pt(direct.MinPoint)+".."+Pt(direct.MaxPoint)+"; oversized + repeat PASS");
                var planned=(G.AABB)Call("BuildFitNeighborEnvelope",context,v,box,20.0);
                Console.WriteLine("PLAN current "+v.ViewType+" "+Pt(planned.MinPoint)+".."+Pt(planned.MaxPoint));
                // Simulates a newly created tight view in memory; does not assign/modify a Tekla view.
                var rawMain=new List<G.Point>();var mainEdges=main.GetSolid().GetEdgeEnumerator();
                var toBox=G.MatrixFactory.ToCoordinateSystem(v.ViewCoordinateSystem);
                while(mainEdges.MoveNext()){var e=mainEdges.Current as Tekla.Structures.Solid.Edge;if(e!=null){rawMain.Add(toBox.Transform(toGlobal.Transform(e.StartPoint)));rawMain.Add(toBox.Transform(toGlobal.Transform(e.EndPoint)));}}
                var tight=new G.AABB(new G.Point(rawMain.Min(p=>p.X)-20,rawMain.Min(p=>p.Y)-20,box.MinPoint.Z),new G.Point(rawMain.Max(p=>p.X)+20,rawMain.Max(p=>p.Y)+20,box.MaxPoint.Z));
                var regenerated=(G.AABB)Call("BuildFitNeighborEnvelope",context,v,tight,20.0);
                Console.WriteLine("PLAN simulated tight "+v.ViewType+" "+Pt(regenerated.MinPoint)+".."+Pt(regenerated.MaxPoint));
                var repeated=(G.AABB)Call("BuildFitNeighborEnvelope",context,v,regenerated,20.0);
                Check(G.Distance.PointToPoint(regenerated.MinPoint,repeated.MinPoint)<1e-6&&G.Distance.PointToPoint(regenerated.MaxPoint,repeated.MaxPoint)<1e-6,"Live geometry grows on repeat");
                var protectedParts=(IDictionary)context.GetType().GetField("Core",Flags).GetValue(context);
                foreach(M.Part p in protectedParts.Values)
                {
                    var geo=context.GetType().GetMethod("Read",Flags).Invoke(context,new object[]{p});
                    var points=((G.Point[])Get(geo,"Vertices")).Select(toBox.Transform).ToArray();
                    if(points.Min(x=>x.Z)>tight.MaxPoint.Z || points.Max(x=>x.Z)<tight.MinPoint.Z)continue;
                    Check(points.All(x=>x.X>=regenerated.MinPoint.X+19.99&&x.X<=regenerated.MaxPoint.X-19.99&&x.Y>=regenerated.MinPoint.Y+19.99&&x.Y<=regenerated.MaxPoint.Y-19.99),"Core plate clipped "+p.Identifier.ID);
                    Check(points.All(x=>x.X>=direct.MinPoint.X+19.99&&x.X<=direct.MaxPoint.X-19.99&&x.Y>=direct.MinPoint.Y+19.99&&x.Y<=direct.MaxPoint.Y-19.99),"Standalone core plate clipped "+p.Identifier.ID);
                }
                // Simulate no connections with the real main solid, changing only
                // in-memory planner dictionaries, never Tekla objects.
                var neighbors=(IDictionary)Get(context,"Neighbors");
                var coreSaved=new List<DictionaryEntry>();foreach(DictionaryEntry item in protectedParts)coreSaved.Add(item);
                var neighborSaved=new List<DictionaryEntry>();foreach(DictionaryEntry item in neighbors)neighborSaved.Add(item);
                int transverseProfiles=0;
                foreach(M.Part neighbor in neighbors.Values)
                {
                    var geo=context.GetType().GetMethod("Read",Flags).Invoke(context,new object[]{neighbor});
                    var a=Get(geo,"AxisStart") as G.Point;var b=Get(geo,"AxisEnd") as G.Point;
                    if(a==null||b==null)continue;
                    a=toBox.Transform(a);b=toBox.Transform(b);
                    if(Math.Abs(a.X-b.X)>.01||Math.Abs(a.Y-b.Y)>.01)continue;
                    var points=((G.Point[])Get(geo,"Vertices")).Select(toBox.Transform).ToArray();
                    if(points.Min(x=>x.Z)>box.MaxPoint.Z || points.Max(x=>x.Z)<box.MinPoint.Z)continue;
                    Check(points.All(x=>x.X>=direct.MinPoint.X+19.99&&x.X<=direct.MaxPoint.X-19.99&&x.Y>=direct.MinPoint.Y+19.99&&x.Y<=direct.MaxPoint.Y-19.99),"External profile cropped "+neighbor.Identifier.ID);
                    transverseProfiles++;
                }
                Console.WriteLine("VERIFY complete transverse neighbor profiles="+transverseProfiles+"; all core/connection plates +20 PASS");
                try
                {
                    protectedParts.Clear(); protectedParts.Add(main.Identifier.ID,main); neighbors.Clear();
                    var alone=(G.AABB)Call("BuildFitGeometryEnvelope",context,v,huge,20.0,false);
                    Check(G.Distance.PointToPoint(alone.MinPoint,tight.MinPoint)<1e-6&&G.Distance.PointToPoint(alone.MaxPoint,tight.MaxPoint)<1e-6,"No-neighbor shape fit differs from main solid + 20");
                    Console.WriteLine("PLAN no-connections simulation "+v.ViewType+" "+Pt(alone.MinPoint)+".."+Pt(alone.MaxPoint)+"; shape +20 PASS");
                }
                finally
                {
                    protectedParts.Clear();foreach(var item in coreSaved)protectedParts.Add(item.Key,item.Value);
                    neighbors.Clear();foreach(var item in neighborSaved)neighbors.Add(item.Key,item.Value);
                }
            }
            Console.WriteLine("VIEW "+v.Name+" "+v.ViewType+" scale="+v.Attributes.Scale+" BOX="+Pt(box.MinPoint)+".."+Pt(box.MaxPoint)+" CS="+Pt(v.DisplayCoordinateSystem.Origin)+" X="+v.DisplayCoordinateSystem.AxisX+" Y="+v.DisplayCoordinateSystem.AxisY+" viewCS="+Pt(v.ViewCoordinateSystem.Origin)+" X="+v.ViewCoordinateSystem.AxisX+" Y="+v.ViewCoordinateSystem.AxisY+" extension="+v.Attributes.ViewExtensionForNeighbourParts+" partial="+v.Attributes.PartialProfileLength);
            var parts=new Dictionary<int,M.Part>(); parts[main.Identifier.ID]=main;
            var objects=v.GetAllObjects(); var counts=new Dictionary<string,int>();
            while(objects.MoveNext())
            {
                var o=objects.Current; string name=o.GetType().Name; if(!counts.ContainsKey(name))counts[name]=0;counts[name]++;
                var prop=o.GetType().GetProperty("ModelIdentifier");
                var id=prop?.GetValue(o,null) as Tekla.Structures.Identifier;
                var p=id==null?null:m.SelectModelObject(id) as M.Part;
                if(p!=null){ parts[p.Identifier.ID]=p; var dp=o as D.Part; Console.WriteLine("DRAW "+name+" -> "+p.Identifier.ID+" representation="+dp?.Attributes.Representation+" partial="+dp?.Attributes.PartialProfileLength); }
                var markObject=o as D.Mark;
                if(markObject!=null){var mb=markObject.GetAxisAlignedBoundingBox();var rel=markObject.GetRelatedObjects();var ids=new List<int>();while(rel.MoveNext()){var rp=rel.Current as D.Part;if(rp!=null)ids.Add(rp.ModelIdentifier.ID);}Console.WriteLine("MARK parts="+string.Join(",",ids)+" box="+Pt(mb.MinPoint)+".."+Pt(mb.MaxPoint));}
            }
            Console.WriteLine("TYPES "+string.Join("; ",counts.Select(x=>x.Key+"="+x.Value)));
            foreach(var p in parts.Values.OrderBy(p=>p.Identifier.ID))
            {
                var points=new List<G.Point>(); var edges=p.GetSolid().GetEdgeEnumerator();
                while(edges.MoveNext()){var e=edges.Current as Tekla.Structures.Solid.Edge; if(e!=null){points.Add(toView.Transform(toGlobal.Transform(e.StartPoint)));points.Add(toView.Transform(toGlobal.Transform(e.EndPoint)));}}
                string mark="";p.GetReportProperty("PART_POS",ref mark);
                Console.WriteLine("PART "+p.Identifier.ID+" "+mark+" "+p.Profile.ProfileString+" asm="+p.GetAssembly().Identifier.ID+" XYZ="+Pt(new G.Point(points.Min(x=>x.X),points.Min(x=>x.Y),points.Min(x=>x.Z)))+".."+Pt(new G.Point(points.Max(x=>x.X),points.Max(x=>x.Y),points.Max(x=>x.Z))));
                Console.WriteLine(" AXIS "+string.Join(";",p.GetCenterLine(false).Cast<G.Point>().Select(x=>Pt(toView.Transform(toGlobal.Transform(x))))));
                var bolts=p.GetBolts(); while(bolts.MoveNext()) { var b=bolts.Current as M.BoltGroup;if(b==null)continue;Console.WriteLine(" BOLT "+b.Identifier.ID+" "+b.PartToBoltTo.Identifier.ID+"->"+b.PartToBeBolted.Identifier.ID+" others="+string.Join(",",b.OtherPartsToBolt.Cast<M.Part>().Select(x=>x.Identifier.ID))); }
                var welds=p.GetWelds();while(welds.MoveNext()){var w=welds.Current as M.BaseWeld;if(w!=null)Console.WriteLine(" WELD "+w.Identifier.ID+" "+w.MainObject.Identifier.ID+"->"+w.SecondaryObject.Identifier.ID);}
            }
        }
        Console.WriteLine("READ ONLY audit complete.");
    }
}
