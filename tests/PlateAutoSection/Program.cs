using System;
using System.Collections;
using System.Reflection;
using Tekla.Structures.Geometry3d;

// Pure reflection regressions: no Model, DrawingHandler or drawing writes.
class Program
{
    static Type worker, geometryType, segmentType;
    static int checks;
    static object Call(string method, params object[] args)
    { return worker.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
    static void Set(object o, string name, object value) { o.GetType().GetField(name).SetValue(o, value); }
    static void Require(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Main(string[] args)
    {
        var assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));
        worker = assembly.GetType("Tekla.Technology.Akit.UserScript.SectionScript", true);
        var dimEngine = assembly.GetType("Tekla.Technology.Akit.UserScript.Script", true);
        var markSide = dimEngine.GetMethod("IsDownwardDimensionForPartMark",BindingFlags.Static|BindingFlags.NonPublic);
        Require((bool)markSide.Invoke(null,new object[]{new Point(0,0,0),new Vector(0,-1,0),30d,0d}),"Bottom DIM must put mark above");
        Require((bool)markSide.Invoke(null,new object[]{new Point(0,0,0),new Vector(0,1,0),-30d,0d}),"Negative distance bottom DIM missed");
        Require(!(bool)markSide.Invoke(null,new object[]{new Point(0,0,0),new Vector(0,1,0),30d,0d}),"Top DIM must keep mark below");
        Require(!(bool)markSide.Invoke(null,new object[]{new Point(0,0,0),new Vector(-1,0,0),30d,0d}),"Side DIM must keep mark below");
        Require(!(bool)markSide.Invoke(null,new object[]{new Point(0,0,0),new Vector(0,-1,0),-30d,0d}),"Reversed normal is visually above");
        var markY=dimEngine.GetMethod("GetPartMarkTargetCenterY",BindingFlags.Static|BindingFlags.NonPublic);
        Require(Math.Abs((double)markY.Invoke(null,new object[]{-20d,80d,7d,0,true})-102)<.001,"Above reference gap changed");
        Require(Math.Abs((double)markY.Invoke(null,new object[]{-20d,80d,7d,0,false})+42)<.001,"Below existing gap changed");
        var smallPlanner = dimEngine.GetMethod("TryBuildSmallThicknessBevelPlans", BindingFlags.NonPublic | BindingFlags.Static);
        foreach (double scale in new[] { .5, 1.0 })
        foreach (int mirror in new[] { -1, 1 })
        foreach (bool reverse in new[] { false, true })
        {
            var polygon = new System.Collections.Generic.List<Point>();
            foreach (var p in new[] { new Point(0,1,0), new Point(4,-3,0), new Point(91,-3,0), new Point(94,1,0), new Point(94,3,0), new Point(0,3,0) })
                polygon.Add(new Point(100 + mirror * scale * p.X, -20 + scale * p.Y, 0));
            if (reverse) polygon.Reverse();
            object[] argsSmall = { polygon, 6 * scale, null };
            Require((bool)smallPlanner.Invoke(null, argsSmall), "Small Top thickness bevel rejected");
            var plans = (IList)argsSmall[2]; Require(plans.Count == 6, "Missing small bevel chain/total/run");
            foreach (var plan in plans) foreach (Point foot in (IEnumerable)plan.GetType().GetField("Feet").GetValue(plan))
                Require(polygon.Exists(p => Math.Abs(p.X-foot.X)<.0001 && Math.Abs(p.Y-foot.Y)<.0001), "Invented Top bevel foot");
        }
        object[] rectangleArgs = { new System.Collections.Generic.List<Point> { new Point(0,0,0),new Point(100,0,0),new Point(100,6,0),new Point(0,6,0) },6d,null };
        Require(!(bool)smallPlanner.Invoke(null,rectangleArgs), "Plain rectangle taken over by bevel fallback");
        foreach (double t in new[] { 6.0, 12.0, 16.0, 28.0 })
        foreach (double width in new[] { 94.0, 236.0, 950.0 })
        foreach (double sliver in new[] { 0.0, .119 })
        {
            var contour = new System.Collections.Generic.List<Point> {
                new Point(17,-t/2,0),new Point(17+width,-t/2,0),
                new Point(17+width-sliver,t/2,0),new Point(17,t/2,0) };
            object[] ordinary = { contour,t,null };
            Require(!(bool)smallPlanner.Invoke(null,ordinary), "Ordinary plate total path intercepted");
            object[] pair = { contour,-t/2,t/2,false,null,null };
            Require((bool)dimEngine.GetMethod("TryFindMaximumThicknessPair",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,pair), "No total thickness pair");
            Require(Math.Abs(((Point)pair[5]).Y-((Point)pair[4]).Y-t)<.000001, "Wrong ordinary total thickness");
        }
        foreach (double sliver in new[] { .0001, .119, .5, .99 })
        foreach (int mirror in new[] { -1, 1 })
        {
            object[] sliverArgs = { new System.Collections.Generic.List<Point> {
                new Point(0,-6,0),new Point(mirror*236,-6,0),new Point(mirror*(236-sliver),6,0),new Point(0,6,0) },12d,null };
            Require(!(bool)smallPlanner.Invoke(null,sliverArgs), "Projected rounded sliver must keep ordinary total DIM flow");
        }
        object[] legacyArgs = { new System.Collections.Generic.List<Point> { new Point(0,0,0),new Point(100,0,0),new Point(100,20,0),new Point(8,20,0),new Point(0,12,0) },20d,null };
        Require(!(bool)smallPlanner.Invoke(null,legacyArgs), "Established large bevel path was replaced");
        geometryType = assembly.GetType("Tekla.Technology.Akit.UserScript.PlateAutoSectionAnalysisResult", true);
        segmentType = worker.GetNestedType("ProjectedFrontSegment", BindingFlags.NonPublic);
        Require(!(bool)Call("IsPlateSectionAxisAllowed", new Point(0, 1, 0), 200.0), "Vertical bevel accepted");
        Require(!(bool)Call("IsPlateSectionAxisAllowed", new Point(0, -1, 0), 200.0), "Reversed vertical bevel accepted");
        Require(!(bool)Call("IsPlateSectionAxisAllowed", new Point(0.0000001, 1, 0), 200.0), "Vertical noise accepted");
        Require((bool)Call("IsPlateSectionAxisAllowed", new Point(1, 0, 0), 80.0), "Horizontal partial bevel rejected");
        Require((bool)Call("IsPlateSectionAxisAllowed", new Point(-1, 0, 0), 80.0), "Reversed horizontal bevel rejected");
        Require((bool)Call("IsPlateSectionAxisAllowed", new Point(.8, .6, 0), 80.0), "Oblique bevel rejected");
        var statusType = assembly.GetType("Tekla.Technology.Akit.UserScript.AutoSectionWorkerStatus", true);
        var resultType = assembly.GetType("Tekla.Technology.Akit.UserScript.AutoSectionWorkerResult", true);
        var formType = assembly.GetType("TTSK_AutoDim_Plates.MainForm", true);
        var executionType = formType.GetNestedType("AutoDimExecutionResult", BindingFlags.NonPublic);
        var form = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(formType);
        foreach (string name in new[] { "PreflightFailed", "CreateFailed", "RolledBack", "UnsafeRollbackFailed" })
        {
            var status = Enum.Parse(statusType, name);
            Require((bool)Call("IsPlateSectionFailure", status), "Failure swallowed: " + name);
            var result = Activator.CreateInstance(resultType);
            Set(result, "Status", status); Set(result, "IsPlateSection", true);
            var execution = Activator.CreateInstance(executionType, true);
            formType.GetMethod("ApplyAutoSectionWorkerResult", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(form, new[] { execution, result });
            Require(!(bool)executionType.GetField("CanSaveDrawing").GetValue(execution), "Failure permits completion: " + name);
            Require(executionType.GetField("SectionStatus").GetValue(execution).ToString() == name, "Failure hidden in UI: " + name);
        }
        foreach (string name in new[] { "NoSectionRequired", "CreatedSingle", "ExistingLayout" })
            Require(!(bool)Call("IsPlateSectionFailure", Enum.Parse(statusType, name)), "Success misclassified: " + name);
        foreach (double scale in new[] { 0.5, 1.0, 3.0 })
        foreach (double angle in new[] { 0.0, Math.PI / 2, 0.47 })
        foreach (int side in new[] { -1, 1 })
        foreach (bool reverse in new[] { false, true })
        {
            var d = new Point(Math.Cos(angle), Math.Sin(angle), 0);
            var n = new Point(-d.Y, d.X, 0);
            var origin = new Point(127, -63, 0);
            Func<double, double, Point> p = (t, v) => new Point(origin.X + scale * (t * d.X + v * n.X), origin.Y + scale * (t * d.Y + v * n.Y), 0);
            var geo = Activator.CreateInstance(geometryType);
            Set(geo, "AxisOrigin", origin); Set(geo, "LongitudinalDirection", d); Set(geo, "PerpendicularDirection", n);
            Set(geo, "MinLongitudinal", -110 * scale); Set(geo, "MaxLongitudinal", 110 * scale);
            Set(geo, "MidLongitudinal", 0.0); Set(geo, "MinPerpendicular", -170 * scale);
            Set(geo, "MaxPerpendicular", 170 * scale); Set(geo, "Thickness", 6 * scale);
            Func<double, double, double, bool, IList> witnesses = (a, b, level, caps) => {
                var segments = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(segmentType));
                Action<Point, Point> add = (start, end) => {
                    var s = Activator.CreateInstance(segmentType);
                    Set(s, "Start", reverse ? end : start); Set(s, "End", reverse ? start : end); segments.Add(s);
                };
                add(p(a, level), p(b, level));
                add(p(a, side * 170), p(b, side * 170));
                if (caps) { add(p(a, level), p(a, side * 170)); add(p(b, level), p(b, side * 170)); }
                // With no direct caps, rounded/multiple-edge terminations must
                // remain candidates until exact section geometry decides.
                return (IList)Call("FindPlateLongitudinalWitnesses", segments, geo);
            };
            foreach (bool caps in new[] { false, true })
            {
                var ws = witnesses(-105, 105, side * 166, caps);
                Require(ws.Count == 1, "Long bevel candidate lost at rounded/direct cap");
                Func<double, double, bool> correlate = (a, b) => (bool)Call("IsPlateCenterDiagonalCorrelated", new Point(a * scale, -scale, 0), new Point(b * scale, 3 * scale, 0), ws, geo);
                Require(correlate(side * 170, side * 166), "True exterior thickness bevel lost");
                Require(!correlate(side * 169, side * 166), "Interior hole/groove diagonal accepted");
                Require(!correlate(side * 170, side * 164), "Unrelated Front line inside diagonal range accepted");
                Require(!correlate(-side * 170, -side * 166), "Opposite boundary accepted");
                foreach (double noise in new[] { -.0003, .0003, -.0008, .0008 })
                    Require(correlate(side * 170 + noise / scale, side * 166),
                        "Solid intersection rounding rejected a real bevel");
                Require(!correlate(side * 170 + .02 / scale, side * 166),
                    "Real boundary mismatch accepted as rounding");
            }
            Require(witnesses(-105, 105, side * 170, false).Count == 0, "Plain outer edge accepted");
            Require(witnesses(-105, -100, side * 166, false).Count == 0, "Tiny end chamfer accepted");
            Require(witnesses(-105, -1, side * 166, false).Count == 1, "Off-center partial bevel candidate lost");
            Require(witnesses(-105, 105, 0, false).Count == 0, "Central internal edge accepted");
            var outline = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(segmentType));
            var edge = Activator.CreateInstance(segmentType);
            Set(edge, "Start", p(-105, side * 166)); Set(edge, "End", p(105, side * 166)); outline.Add(edge);
            Require(((IList)Call("FindPlateLongitudinalWitnesses", outline, geo)).Count == 0,
                "Outer contour inside global bounds mistaken for a bevel without paired boundary");
        }
        Console.WriteLine("PASS " + checks + " checks: rounded/direct ends, thin/resized/mirrored/rotated/reversed geometry, exterior correlation and negative cases.");
    }
}
