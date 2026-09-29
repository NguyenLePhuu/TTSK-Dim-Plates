using System;
using System.Reflection;
using Tekla.Structures.Drawing;

static class SelectionAudit
{
    internal static int Id(View view)
    {
        return ((Tekla.Structures.Identifier)typeof(View).GetProperty("Identifier", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view,null)).ID;
    }
    internal static void Run()
    {
        Console.WriteLine("ARRANGE VIEW INSPECTION - READ ONLY; no Modify/Commit/work-plane changes");
        var model=new Tekla.Structures.Model.Model();
        var handler=new DrawingHandler();
        Console.WriteLine("Model="+model.GetConnectionStatus()+" Drawing="+handler.GetConnectionStatus());
        if(!model.GetConnectionStatus()||!handler.GetConnectionStatus())throw new Exception("Connection failed");
        var drawing=handler.GetActiveDrawing();
        if(drawing==null)throw new Exception("No active drawing");
        Console.WriteLine("Drawing="+drawing.Mark+" Name="+drawing.Name+" Type="+drawing.GetType().Name+" Model="+model.GetInfo().ModelPath);
        var views=drawing.GetSheet().GetAllViews();
        while(views.MoveNext())
        {
            var v=views.Current as View;if(v==null)continue;
            var cs=v.DisplayCoordinateSystem;var b=v.GetAxisAlignedBoundingBox();var r=v.RestrictionBox;
            Console.WriteLine("VIEW id="+Id(v)+" name="+v.Name+" type="+v.ViewType+" scale="+v.Attributes.Scale+" origin="+v.Origin
                +" bounds="+b.MinPoint+" -> "+b.MaxPoint+" restriction="+r.MinPoint+" -> "+r.MaxPoint
                +" CSorigin="+cs.Origin+" X="+cs.AxisX+" Y="+cs.AxisY);
        }
        var selection=handler.GetDrawingObjectSelector().GetSelected();
        while(selection.MoveNext())
        {
            var v=selection.Current as View;
            Console.WriteLine("SELECTED "+selection.Current.GetType().Name+(v==null?"":" id="+Id(v)+" name="+v.Name));
            var r=selection.Current as Tekla.Structures.Drawing.Rectangle;
            if(r!=null)Console.WriteLine("RECT start="+r.StartPoint+" end="+r.EndPoint+" width="+r.Width+" height="+r.Height+" angle="+r.Angle+" owner="+r.GetView().GetType().Name);
        }
    }
}
