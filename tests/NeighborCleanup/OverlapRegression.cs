using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using G = Tekla.Structures.Geometry3d;
using N = Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;

internal static class OverlapRegression
{
    static int count;
    static void Assert(bool condition, string name)
    { if(!condition)throw new Exception(name); count++; }
    static N.ReferenceFace Face(double x, double minY, double maxY, double minZ, double maxZ, double normalX)
    {
        return new N.ReferenceFace { Normal=new G.Vector(normalX,0,0), Points=new[] {
            new G.Point(x,minY,minZ),new G.Point(x,maxY,minZ),new G.Point(x,maxY,maxZ),new G.Point(x,minY,maxZ) } };
    }
    static N.ReferenceShape Shape(N.ReferenceFace face)
    {
        var s=new N.ReferenceShape { AxisX=new G.Vector(0,1,0), AxisY=new G.Vector(0,0,1) };
        s.Faces.Add(face); return s;
    }
    internal static void Run()
    {
        Assert(N.ReferenceColorRole(157/255.0,157/255.0,169/255.0)==-1,"Permanent gray reference is unused");
        Assert(N.ReferenceColorRole(231/255.0,83/255.0,92/255.0)==1,"Red active reference");
        Assert(N.ReferenceColorRole(.1,.7,.3)==1,"Green active reference, no hard-coded red or class");
        Assert(N.ReferenceColorRole(.1,.3,.8)==1,"Blue active reference");
        Assert(N.ReferenceColorRole(0,0,0)==0 && N.ReferenceColorRole(1,1,1)==0,"Black/white are uncertain");
        Assert(N.ReferenceColorRole(.6,.5,.5)==0,"Transition band is uncertain");
        Assert(N.ReferenceColorRole(Double.NaN,.5,.5)==0 && N.ReferenceColorRole(5,.5,.5)==0,"Invalid color is uncertain");
        Assert(N.UniqueReferenceReplacement(new[] { 42 },new int[0])==42,"Unique active candidate");
        Assert(N.UniqueReferenceReplacement(new[] { 42,42 },new int[0])==42,"Deduplicate identity");
        Assert(N.UniqueReferenceReplacement(new[] { 42,43 },new int[0])==0,"Two active candidates are ambiguous");
        Assert(N.UniqueReferenceReplacement(new[] { 43,42 },new int[0])==0,"Order does not choose active candidate");
        Assert(N.UniqueReferenceReplacement(new[] { 42 },new[] { 43 })==0,"Unknown overlapping reference blocks guessing");
        Assert(N.UniqueReferenceReplacement(new int[0],new int[0])==0,"No replacement is ambiguous");
        var plate=Face(0,-5,5,20,980,1);
        var old=Face(0,-5000,5000,0,1000,-1);
        var active=Face(0,-250,250,-4,996,-1);
        Assert(N.ReferenceFaceCovers(old,plate) && N.ReferenceFaceCovers(active,plate),"Both intersect the same interface despite 4mm height offset");
        Assert(N.SharesPlateInterface(Shape(old),Shape(active),Shape(plate)),"Actual plate interface proves local overlap");
        Assert(!N.ReferenceFaceCovers(Face(.1,-250,250,-4,996,-1),plate),"Near projected edge is not contact");
        Assert(N.ReferenceFaceCovers(Face(.04,-250,250,-4,996,-1),plate),"Contact tolerance is model mm");
        Assert(!N.ReferenceFaceCovers(Face(0,50,550,-4,996,-1),plate),"Same plane but longitudinally elsewhere");
        Assert(!N.ReferenceFaceCovers(Face(0,-250,250,200,800,-1),plate),"Partial face overlap insufficient");
        Assert(!N.ReferenceFaceCovers(Face(0,-250,250,-4,996,1),plate),"Same-facing surfaces are not mating contact");
        var reversed=Face(0,-250,250,-4,996,-1); Array.Reverse(reversed.Points);
        Assert(N.ReferenceFaceCovers(reversed,plate),"Face winding independent");
        var concave=new N.ReferenceFace { Normal=new G.Vector(-1,0,0), Points=new[] {
            new G.Point(0,-250,-4),new G.Point(0,250,-4),new G.Point(0,250,996),
            new G.Point(0,0,400),new G.Point(0,-250,996) } };
        Assert(!N.ReferenceFaceCovers(concave,plate),"Notch cannot be filled with bounding box");
        Assert(N.ReferenceBoundsOverlap(Shape(concave),Shape(plate)),"Notch bbox is only ambiguity evidence to keep both");
        Assert(!N.ReferenceBoundsOverlap(Shape(old),Shape(Face(500,-5,5,20,980,1))),"Distant parallel support is not local overlap");
        var degenerate=Face(0,0,0,20,980,1);
        Assert(!N.ReferenceFaceCovers(old,degenerate),"Line contact is not mating face");
        var a=Shape(old); var b=Shape(active);
        Assert(N.ReferenceAxesParallel(a,b),"Parallel reference axes");
        b.AxisX=new G.Vector(0,-1,0); b.AxisY=new G.Vector(0,0,-1);
        Assert(N.ReferenceAxesParallel(a,b),"Reversed axes allowed");
        b.AxisX=new G.Vector(1,0,0);
        Assert(!N.ReferenceAxesParallel(a,b),"Crossing member excluded");
        // Quay/di chuyển toàn bộ hình học: không dùng X/Y cố định, chiều dài hay ID mẫu.
        Func<G.Point,G.Point> rotate=p=>new G.Point(1200-p.Z,500+p.X,-600+p.Y);
        Func<G.Vector,G.Vector> normal=v=>new G.Vector(-v.Z,v.X,v.Y);
        foreach(var f in new[] { old,active,plate }) { f.Points=f.Points.Select(rotate).ToArray(); f.Normal=normal(f.Normal); }
        Assert(N.ReferenceFaceCovers(old,plate) && N.ReferenceFaceCovers(active,plate),"Rotated translated contact");

        var plan=new N.Plan { Keep=new Dictionary<int,string> { { 1,"KEEP primary assembly" } } };
        plan.Nodes.Add(1,new N.Node { Id=1 }); plan.Nodes.Add(2,new N.Node { Id=2,Plate=true });
        plan.Nodes.Add(3,new N.Node { Id=3,Dummy=true }); plan.Nodes.Add(4,new N.Node { Id=4,Dummy=true });
        var protect=typeof(N).GetMethod("ProtectReferenceSupports",BindingFlags.Static|BindingFlags.NonPublic);
        protect.Invoke(null,new object[] { plan,new[] { 3,4 },new HashSet<int> { 2 },"Ambiguous overlap, keep both" });
        Assert(plan.Keep.ContainsKey(3) && plan.Keep.ContainsKey(4),"Ambiguous group keeps both, no stop flag");
        Assert(plan.ReferenceSupportPlates.Count==2 && plan.Edges.Count==0,"Keep both does not add graph edges");
        var items=new List<N.Item>();
        foreach(int view in new[] { 10,20 }) foreach(int id in new[] { 1,2,3,4 })
            items.Add(new N.Item { ModelId=id,ViewId=view,WasHidden=id>=3 });
        var restores=N.ResolveReferenceSupportRestores(plan.ReferenceSupportPlates,plan.Nodes,new HashSet<int>{1},items);
        Assert(restores.Count==4,"Both hidden members restored in Top and Front");
        Assert(items.Where(i=>!i.WasHidden && !plan.Keep.ContainsKey(i.ModelId)).All(i=>i.ModelId!=3 && i.ModelId!=4),"Neither member is hide target");
        Assert(plan.Warnings.Count==1,"Ambiguity is a warning, not abort");
        plan.Keep[3]="KEEP old dummy reason"; plan.ReferenceSupportPlates.Clear();
        protect.Invoke(null,new object[] { plan,new[] { 3,4 },new HashSet<int> { 2 },"Keep both even when old member was hidden" });
        Assert(plan.Keep[3]=="KEEP old dummy reason" && N.ResolveReferenceSupportRestores(plan.ReferenceSupportPlates,
            plan.Nodes,new HashSet<int>{1},items).Count==4,"Ambiguous old-kept hidden member is also restored without overwriting reason");
        Console.WriteLine("PASS "+count+" overlap/color/contact/keep-both regressions");
    }
}
