using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using M = Tekla.Structures.Model;
using D = Tekla.Structures.Drawing;
using N = Tekla.Technology.Akit.UserScript.PHU_NeighborCleanup;

// Chỉ đọc bản vẽ/quan hệ; không đổi selection, work plane hay gọi Apply/Run.
internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            string product = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
                if (new AssemblyName(e.Name).Name == "TTSK Dim Plates") return Assembly.LoadFrom(product);
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(product), new AssemblyName(e.Name).Name + ".dll");
                if (System.IO.File.Exists(path)) return Assembly.LoadFrom(path);
                path = System.IO.Path.Combine(args[3], new AssemblyName(e.Name).Name + ".dll");
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            var assembly = Assembly.LoadFrom(product);
            if (args[1] == "offline") { Regression.Run(); OverlapRegression.Run(); return 0; }
            var coordinator = assembly.GetType("TTSK_AutoDim_Plates.TeklaSessionCoordinator");
            Environment.SetEnvironmentVariable("TTSK_SESSION_PID", args[1]);
            Environment.SetEnvironmentVariable("TTSK_SESSION_START", args[2]);
            coordinator.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            coordinator.GetMethod("VerifyConnection", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Audit();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Audit()
    {
        var model = new M.Model(); var handler = new D.DrawingHandler();
        Console.WriteLine("READ ONLY Model=" + model.GetConnectionStatus() + " Drawing=" + handler.GetConnectionStatus());
        var drawing = handler.GetActiveDrawing();
        if (drawing == null) throw new Exception("No active drawing");
        Console.WriteLine("MODEL=" + model.GetInfo().ModelPath);
        Console.WriteLine("DRAWING=" + drawing.GetType().Name + " " + drawing.Name + " " + drawing.Mark);
        var selected = new M.UI.ModelObjectSelector().GetSelectedObjects();
        while (selected.MoveNext())
        {
            var part = selected.Current as M.Part;
            if (part != null) { Console.WriteLine("SELECTED3D P" + part.Identifier.ID); PrintMetadata(part); }
        }
        var plan = N.Analyze();
        Console.WriteLine(plan.Report());
        foreach (var n in plan.Nodes.Values.OrderBy(n => n.Id))
            Console.WriteLine("NODE=" + n.Id + " assembly=" + n.Assembly + " plate=" + n.Plate + " dummy=" + n.Dummy + " " + n.Label);
        var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
        var views = drawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            var view = views.Current as D.View; if (view == null) continue;
            Console.WriteLine("VIEWINFO name=" + view.Name + " type=" + view.ViewType + " scale=" + view.Attributes.Scale
                + " origin=" + view.DisplayCoordinateSystem.Origin + " X=" + view.DisplayCoordinateSystem.AxisX + " Y=" + view.DisplayCoordinateSystem.AxisY);
            var parts = view.GetAllObjects(typeof(D.Part));
            var toView = Tekla.Structures.Geometry3d.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
            while (parts.MoveNext())
            {
                var dp = (D.Part)parts.Current;
                Console.WriteLine("VIEWPART " + view.Name + " P" + dp.ModelIdentifier.ID + " hidden=" + dp.Hideable.IsHidden);
                var mp = model.SelectModelObject(dp.ModelIdentifier) as M.Part;
                var points = new System.Collections.Generic.List<Tekla.Structures.Geometry3d.Point>();
                var faces = mp.GetSolid().GetFaceEnumerator();
                while (faces.MoveNext())
                {
                    var face = faces.Current as Tekla.Structures.Solid.Face; if (face == null) continue;
                    var loops = face.GetLoopEnumerator();
                    while (loops.MoveNext())
                    {
                        var loop = loops.Current as Tekla.Structures.Solid.Loop; if (loop == null) continue;
                        var vertices = loop.GetVertexEnumerator();
                        while (vertices.MoveNext()) points.Add(toView.Transform(toGlobal.Transform((Tekla.Structures.Geometry3d.Point)vertices.Current)));
                    }
                }
                if (points.Count > 0) Console.WriteLine("BOUNDS " + view.ViewType + " P" + dp.ModelIdentifier.ID
                    + " X=" + points.Min(p => p.X) + ".." + points.Max(p => p.X)
                    + " Y=" + points.Min(p => p.Y) + ".." + points.Max(p => p.Y)
                    + " Z=" + points.Min(p => p.Z) + ".." + points.Max(p => p.Z));
            }
        }
        foreach (var n in plan.Nodes.Values.OrderBy(n => n.Id))
        {
            PrintMetadata(n.Part);
            var father = n.Part.GetFatherComponent();
            if (father != null) PrintComponent(n.Id, "FATHER", father);
            var components = n.Part.GetComponents();
            while (components.MoveNext())
            {
                var component = components.Current as M.BaseComponent;
                if (component != null) PrintComponent(n.Id, "INPUT", component);
            }
            var bolts = n.Part.GetBolts();
            while (bolts.MoveNext())
            {
                var b = bolts.Current as M.BoltGroup; if (b == null) continue;
                var members = new System.Collections.Generic.List<M.Part> { b.PartToBoltTo, b.PartToBeBolted };
                foreach (M.Part part in b.OtherPartsToBolt) members.Add(part);
                Console.WriteLine("DROPPED_PLATE_BOLT P" + n.Id + " bolt=" + b.Identifier.ID + " members="
                    + String.Join(",", members.Where(p => p != null).Select(p => p.Identifier.ID)));
            }
            var welds = n.Part.GetWelds();
            while (welds.MoveNext())
            {
                var w = welds.Current as M.BaseWeld; if (w == null) continue;
                Console.WriteLine("DROPPED_PLATE_WELD P" + n.Id + " weld=" + w.Identifier.ID + " members="
                    + w.MainObject.Identifier.ID + "," + w.SecondaryObject.Identifier.ID);
            }
        }
        if (!handler.GetActiveDrawing().IsSameDatabaseObject(drawing)) throw new Exception("Drawing changed during audit");
        Console.WriteLine("PASS: read only; no model/drawing/selection/work-plane mutation.");
    }

    static void PrintComponent(int partId, string role, M.BaseComponent component)
    {
        var read = typeof(N).GetMethod("ReadComponentMembers", BindingFlags.Static | BindingFlags.NonPublic);
        var members = (System.Collections.Generic.HashSet<int>)read.Invoke(null, new object[] { component });
        Console.WriteLine("COMPONENT P" + partId + " " + role + " id=" + component.Identifier.ID + " type=" + component.GetType().Name
            + " members=" + String.Join(",", members.OrderBy(id => id)));
        var connection = component as M.Connection;
        if (connection != null) Console.WriteLine("CONNECTION P" + partId + " main=" + connection.GetPrimaryObject().Identifier.ID
            + " secondary=" + String.Join(",", connection.GetSecondaryObjects().Cast<M.ModelObject>().Select(o => o.Identifier.ID)));
    }

    static void PrintMetadata(M.Part part)
    {
        var color = new M.UI.Color();
        if (M.UI.ModelObjectVisualization.GetRepresentation(part, ref color))
            Console.WriteLine("COLOR P"+part.Identifier.ID+" RGB="+color.Red+","+color.Green+","+color.Blue);
        Console.WriteLine("META P" + part.Identifier.ID + " type=" + part.GetType().Name + " name=" + part.Name
            + " profile=" + part.Profile.ProfileString + " class=" + part.Class + " material=" + part.Material.MaterialString
            + " finish=" + part.Finish);
        var user = new System.Collections.Hashtable();
        if (part.GetAllUserProperties(ref user))
            foreach (System.Collections.DictionaryEntry prop in user.Cast<System.Collections.DictionaryEntry>().OrderBy(p=>p.Key.ToString()))
                Console.WriteLine("UDA P" + part.Identifier.ID + " " + prop.Key + "=" + prop.Value);
        var cs = part.GetCoordinateSystem();
        Console.WriteLine("PARTCS P"+part.Identifier.ID+" origin="+cs.Origin+" X="+cs.AxisX+" Y="+cs.AxisY);
        var contourPlate=part as M.ContourPlate;
        if(contourPlate!=null)
            foreach(M.ContourPoint point in contourPlate.Contour.ContourPoints)
                Console.WriteLine("CONTOUR P"+part.Identifier.ID+" point="+point+" chamfer="+point.Chamfer.Type
                    +" chamferX="+point.Chamfer.X+" chamferY="+point.Chamfer.Y);
        var beam=part as M.Beam;
        if(beam!=null)
        {
            Console.WriteLine("BEAM P"+part.Identifier.ID+" start="+beam.StartPoint+" end="+beam.EndPoint);
            {
                // Bằng chứng mặt phẳng solid trong work plane hiện hành, không đổi plane.
                var faces=part.GetSolid().GetFaceEnumerator();
                while(faces.MoveNext())
                {
                    var face=faces.Current as Tekla.Structures.Solid.Face;
                    if(face==null || Math.Abs(face.Normal.X)<0.9999)continue;
                    var loops=face.GetLoopEnumerator(); var points=new System.Collections.Generic.List<Tekla.Structures.Geometry3d.Point>();
                    while(loops.MoveNext())
                    {
                        var loop=loops.Current as Tekla.Structures.Solid.Loop; if(loop==null)continue;
                        var vertices=loop.GetVertexEnumerator();
                        while(vertices.MoveNext())points.Add((Tekla.Structures.Geometry3d.Point)vertices.Current);
                    }
                    if(points.Count>0)Console.WriteLine("SOLID_X_FACE P"+part.Identifier.ID+" normal="+face.Normal
                        +" X="+points.Min(p=>p.X)+".."+points.Max(p=>p.X)
                        +" Y="+points.Min(p=>p.Y)+".."+points.Max(p=>p.Y)
                        +" Z="+points.Min(p=>p.Z)+".."+points.Max(p=>p.Z));
                }
            }
        }
    }
}
