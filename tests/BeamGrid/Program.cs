using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using D = Tekla.Structures.Drawing;
using M = Tekla.Structures.Model;
using G = Tekla.Structures.Geometry3d;

class Program
{
    internal static Type Engine;
    internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static object Get(object o, string n) { return o.GetType().GetField(n, Flags)?.GetValue(o) ?? o.GetType().GetProperty(n, Flags)?.GetValue(o, null); }
    internal static object Call(string name, params object[] args) { return Engine.GetMethod(name, Flags).Invoke(null, args); }
    static int Main(string[] args)
    {
        try
        {
            Engine = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1])).GetType("Tekla.Technology.Akit.UserScript.PHU_BeamGridDimensionEngine", true);
            if (args[0] == "--audit") Audit();
            else Regression.Run(args[0] == "--baseline");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Audit()
    {
        Console.WriteLine("ACTIVE DRAWING AUDIT - READ ONLY; current plane -> global -> view; no drawing/work-plane mutation.");
        var m = new M.Model(); var h = new D.DrawingHandler();
        Console.WriteLine("Model=" + m.GetConnectionStatus() + " Drawing=" + h.GetConnectionStatus());
        if (!m.GetConnectionStatus() || !h.GetConnectionStatus()) throw new Exception("Connection failed");
        var d = h.GetActiveDrawing(); if (d == null) throw new Exception("No active drawing");
        Console.WriteLine("Drawing=" + d.GetType().Name + " " + d.Name + " " + d.Mark + " Model=" + m.GetInfo().ModelPath);
        var ad = d as D.AssemblyDrawing; var sd = d as D.SinglePartDrawing;
        var obj = m.SelectModelObject(ad != null ? ad.AssemblyIdentifier : sd.PartIdentifier);
        var main = obj as M.Part ?? (obj as M.Assembly)?.GetMainPart() as M.Part;
        if (main == null) throw new Exception("No authoritative main part");
        Console.WriteLine("Main=" + main.Identifier.ID + " " + main.Profile.ProfileString);
        var transform = m.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
        var views = d.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            var v = views.Current as D.View; if (v == null) continue;
            Console.WriteLine("VIEW " + Get(v,"Identifier") + " " + v.Name + " " + v.ViewType + " Scale=" + v.Attributes.Scale + " CS=" + v.DisplayCoordinateSystem);
            try
            {
                Call("Configure",true,2);
                string key = v.ViewType.ToString() == "FrontView" || v.ViewType.ToString() == "BackView" ? "FRONT" : "TOP";
                var geo = Call("ReadViewGeometry", m, main, v, key, transform);
                Console.WriteLine("REF " + Point(Get(geo,"RefLeft")) + " -> " + Point(Get(geo,"RefRight")) + " cutSpan=" + Get(geo,"MainSpanStart") + ".." + Get(geo,"MainSpanEnd"));
                foreach (var axis in (IEnumerable)Get(geo,"GridAxes"))
                    Console.WriteLine("GRID " + Get(axis,"Label") + " id=" + Get(axis,"ModelIdentifier") + " cross=" + Get(axis,"IsCrossAxis") + " station=" + Get(axis,"Coordinate") + " endpoints=" + Point(Get(axis,"StartPoint")) + ".." + Point(Get(axis,"EndPoint")));
                foreach (int mode in new[]{1,2,3})
                    try { var resolved=Call("ResolveAxes",geo,mode); Console.WriteLine("MODE="+mode+" interior="+PointAxis(Get(resolved,"InteriorEnd"))+" end="+PointAxis(Get(resolved,"SingleEnd"))); }
                    catch(Exception ex) { Console.WriteLine("MODE="+mode+" rejected: "+ex.GetBaseException().Message); }
            }
            catch(Exception ex) { Console.WriteLine("Geometry rejected: " + ex.GetBaseException().Message); }
            var dims = v.GetAllObjects(typeof(D.StraightDimensionSet));
            while(dims.MoveNext())
            {
                var dim = dims.Current;
                var points=(IEnumerable)Get(dim,"DimensionPoints");
                Console.WriteLine("DIM " + Get(dim,"Identifier") + " normal=" + Get(dim,"UpDirection") + " distance=" + Get(dim,"Distance") + " feet=" + String.Join(";",points.Cast<object>().Select(p=>p.ToString())));
            }
        }
        Console.WriteLine("READ ONLY audit complete.");
    }
    static string Point(object p) { return p == null ? "null" : Get(p,"X") + "," + Get(p,"Y"); }
    static string PointAxis(object a) { return a == null ? "none" : Convert.ToString(Get(a,"Coordinate")); }
}
