using System;
using System.IO;
using System.Reflection;
using System.Text;
using M=Tekla.Structures.Model;
using D=Tekla.Structures.Drawing;
using G=Tekla.Structures.Geometry3d;
using U=Tekla.Technology.Akit.UserScript;

static class InzaiDiagnosis
{
    internal static void Read(string product,string path)
    {
        var a=Assembly.LoadFrom(Path.GetFullPath(product));
        Func<string,Type> type=n=>a.GetType("Tekla.Technology.Akit.UserScript."+n,true);
        Func<string,string,object[],object> call=(n,m,p)=>type(n).GetMethod(m,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,p);
        var model=new M.Model();var handler=new D.DrawingHandler();
        if(!model.GetConnectionStatus()||!handler.GetConnectionStatus())throw new Exception("API disconnected");
        var drawing=handler.GetActiveDrawing();var main=U.PHU_MainPartResolver.Resolve(model,drawing);
        if(main==null)throw new Exception("No authoritative MainPart");
        var report=new StringBuilder("INZAI READ ONLY: no mutation/selection/work-plane changes.\n");
        report.AppendLine("Drawing="+drawing.Mark+" Name="+drawing.Name+" Model="+model.GetInfo().ModelPath);
        report.AppendLine("Main="+main.Identifier.ID+" Profile="+main.Profile.ProfileString+" Name="+main.Name);
        var global=model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
        foreach(G.Point p in main.GetReferenceLine(false))report.AppendLine("GLOBAL REF="+global.Transform(p));
        var views=drawing.GetSheet().GetAllViews();
        while(views.MoveNext())
        {
            var v=views.Current as D.View;if(v==null)continue;
            report.AppendLine("VIEW "+Id(v)+" name="+v.Name+" type="+v.ViewType+" scale="+v.Attributes.Scale+" Sheet="+v.Origin+" CS="+v.DisplayCoordinateSystem.Origin+" X="+v.DisplayCoordinateSystem.AxisX+" Y="+v.DisplayCoordinateSystem.AxisY);
        }
        using((IDisposable)call("PHU_ColumnProjectRouter","BeginInjaiSelection",new object[0]))
        {
            report.AppendLine(Convert.ToString(call("PHU_Slot07_DataCenterBeamType2Context","AuditCurrentDrawingRoute",new object[0])));
            using((IDisposable)type("PHU_Slot07_DataCenterContext").GetMethod("Begin",new[]{typeof(bool),typeof(bool)}).Invoke(null,new object[]{false,true}))
                report.AppendLine(Convert.ToString(call("PHU_Slot07_DataCenterBeamType2DimensionEngine","AuditType1SectionCurrentDrawingPlan",new object[0])));
            report.AppendLine("Vertical capture="+call("PHU_VerticalShapeViewLayoutContext","Begin",new object[]{model,drawing,main}));
            call("PHU_ColumnDimensionTierContext","Configure",new[]{(object)true,Enum.ToObject(type("PHU_ColumnShapeFamily"),1)});
            call("PHU_ColumnGridDimensionEngine","Configure",new object[]{true,3});
            report.AppendLine("Column begin="+call("PHU_ColumnGridDimensionEngine","Begin",new object[]{model,drawing,main}));
            object[] gateArgs={null};
            bool gate=(bool)call("PHU_ColumnGridDimensionEngine","PreflightPreparedGeometry",gateArgs);
            report.AppendLine("Typed Column preflight="+gate+" "+gateArgs[0]);
            if(!gate)throw new Exception("Expected real Inzai Grid geometry to pass preflight: "+gateArgs[0]);
            report.AppendLine(Convert.ToString(call("PHU_ColumnGridDimensionEngine","AuditPreparedGeometry",new object[0])));
            call("PHU_InzaiColumnNeighborDimensionEngine","Configure",new object[]{true});
            call("PHU_InzaiColumnNeighborDimensionEngine","Begin",new object[]{model,drawing,main});
            foreach(var p in new[]{new[]{"PHU_InzaiColumnNeighborDimensionEngine","AuditPreparedPlans"},new[]{"PHU_InzaiColumnSpliceDimensionEngine","AuditPreparedPlans"},new[]{"PHU_InzaiColumnSectionDimensionEngine","AuditCurrentDrawingPlan"}})
                report.AppendLine(Convert.ToString(call(p[0],p[1],new object[0])));
            call("PHU_ColumnGridDimensionEngine","Reset",new object[0]);
            object[] missingContext={null};
            if((bool)call("PHU_ColumnGridDimensionEngine","PreflightPreparedGeometry",missingContext))
                throw new Exception("Missing prepared context was incorrectly accepted");
            report.AppendLine("PASS: typed preflight rejects missing context before writes.");
        }
        File.WriteAllText(path,report.ToString());Console.WriteLine(report);
    }
    static int Id(object o)
    {return ((Tekla.Structures.Identifier)o.GetType().GetProperty("Identifier",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o,null)).ID;}
}
