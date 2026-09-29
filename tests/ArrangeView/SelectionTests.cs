using System;
using System.Linq;
using Tekla.Structures.Geometry3d;
using A=TTSK_AutoDim_Plates.PHU_ArrangeView;

static class SelectionTests
{
    internal static void Run(Action<bool,string> check)
    {
        var all=new[]{1,2,3,4,5,6};var automatic=new[]{4,5};var protectedIds=new[]{1,2,3};
        check(A.ResolveMovableIds(all,automatic,new[]{3,5},protectedIds,true).SetEquals(new[]{5}),"selected subset excludes special Bottom");
        check(A.ResolveMovableIds(all,automatic,new[]{3},protectedIds,true).Count==0,"only special Bottom never triggers auto");
        check(A.ResolveMovableIds(all,automatic,new int[0],protectedIds,true).Count==0,"non-view selection never triggers auto");
        check(A.ResolveMovableIds(all,automatic,new int[0],protectedIds,false).SetEquals(automatic),"empty selection retains automatic flow");
        check(A.ResolveMovableIds(all,automatic,new[]{6,6,99},protectedIds,true).SetEquals(new[]{6}),"explicit unknown section bypasses heuristic, deduplicates and stays in active drawing");
        check(A.ResolveMovableIds(all,all,all,protectedIds,false).SetEquals(new[]{4,5,6}),"protection applies in auto mode");
        foreach(double angle in new[]{0.0,.7,2.0})foreach(double scale in new[]{.5,1.0,3.0})foreach(bool mirror in new[]{false,true})
        {
            double sign=mirror?-1:1;
            var longitudinal=new Vector(Math.Cos(angle)*sign,Math.Sin(angle)*sign,0);
            var lateral=new Vector(-Math.Sin(angle),Math.Cos(angle),0);
            var up=new Vector(0,0,1);
            var front=new CoordinateSystem(new Point(731,286,44),longitudinal,up);
            var bottom=new CoordinateSystem(new Point(-45,129,12),longitudinal,lateral);
            var transverse=new CoordinateSystem(new Point(),lateral,up);
            var longBox=new AABB(new Point(-100*scale,-300*scale,-20),new Point(5200*scale,300*scale,20));
            var frontBox=new AABB(new Point(-100*scale,-200*scale,-20),new Point(5200*scale,650*scale,20));
            check(A.IsLongitudinalMainSection(bottom,longBox,front,frontBox),"longitudinal Bottom protected independent of translation/rotation/size");
            check(!A.IsLongitudinalMainSection(transverse,longBox,front,frontBox),"even a wide transverse section remains movable");
            check(!A.IsLongitudinalMainSection(front,longBox,front,frontBox),"same-plane detail is not special Bottom");
            var small=new AABB(new Point(0,0,0),new Point(300*scale,300*scale,10));
            check(!A.IsLongitudinalMainSection(bottom,small,front,frontBox),"short local longitudinal detail remains movable");
            var rotatedBottom=new CoordinateSystem(new Point(),lateral,longitudinal);
            var rotatedBox=new AABB(new Point(-300*scale,-100*scale,-20),new Point(300*scale,5200*scale,20));
            check(A.IsLongitudinalMainSection(rotatedBottom,rotatedBox,front,frontBox),"in-plane rotated Bottom protected");
        }
        check(!A.IsLongitudinalMainSection(null,null,null,null),"missing geometry is not inferred");
    }
}
