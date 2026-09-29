using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Tekla.Structures.Geometry3d;

static class Program
{
    static Type engine, bounds;
    static int checks;
    static int Resolve(Point plate, Point start, Point end, double[][] data, bool[] related, bool known)
    {
        var boxes = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bounds));
        foreach (var b in data)
        {
            object value = Activator.CreateInstance(bounds);
            bounds.GetField("Valid").SetValue(value, true);
            string[] names = { "MinX", "MaxX", "MinY", "MaxY" };
            for (int j = 0; j < names.Length; j++) bounds.GetField(names[j]).SetValue(value, b[j]);
            boxes.Add(value);
        }
        return (int)engine.GetMethod("ResolveSelectedNeighborIndex", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { plate, start, end, boxes, new List<bool>(related), known });
    }
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        checks++;
    }
    static int Main(string[] args)
    {
        string runtime = Path.GetFullPath(args[0]);
        string tekla = Path.GetFullPath(args[1]);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { runtime, tekla })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        engine = Assembly.LoadFrom(Path.Combine(runtime, "TTSK Dim Plates.exe"))
            .GetType("Tekla.Technology.Akit.UserScript.PHU_Slot02_NeighborReferencePlateDim", true);
        bounds = engine.GetNestedType("Bounds2D", BindingFlags.NonPublic);
        // Unequal lengths make the opposite beam's center closer than the true
        // owner. Mixed-selection result must equal selecting each side alone.
        foreach (double mirror in new[] { 1.0, -1.0 })
        foreach (double shiftX in new[] { 0.0, 719.0 })
        foreach (double shiftY in new[] { 0.0, -347.0 })
        foreach (bool reverseAxis in new[] { false, true })
        foreach (bool reverseOrder in new[] { false, true })
        foreach (bool known in new[] { false, true })
        {
            Func<double, double, double[]> box = (x, y) => new[] {
                x + shiftX - 40, x + shiftX + 40,
                Math.Min(0, y * mirror) + shiftY, Math.Max(0, y * mirror) + shiftY };
            var a = new Point(shiftX, shiftY, 0);
            var b = new Point(shiftX + 3000, shiftY, 0);
            var p = new Point(1200 + shiftX, 80 * mirror + shiftY, 0);
            var owner = box(1203, 3000);
            var opposite = box(1620, -400);
            var sameSide = box(300, 3000);
            var data = reverseOrder ? new[] { sameSide, opposite, owner } : new[] { owner, opposite, sameSide };
            var related = reverseOrder ? new[] { false, false, true } : new[] { true, false, false };
            int result = Resolve(p, reverseAxis ? b : a, reverseAxis ? a : b, data, related, known);
            Check(result == (reverseOrder ? 2 : 0), "Mixed direction pairing changed");
            Check(Resolve(p, a, b, new[] { owner }, new[] { true }, known) == 0, "Single direction changed");
        }
        var start = new Point(0, 0, 0); var end = new Point(3000, 0, 0); var plate = new Point(500, 80, 0);
        var near = new[] { 460.0, 540, 0, 400 };
        var far = new[] { 1460.0, 1540, 0, 400 };
        Check(Resolve(plate, start, end, new[] { near, far }, new[] { false, true }, true) == 1, "Ownership must outrank proximity");
        Check(Resolve(plate, start, end, new[] { near }, new[] { false }, true) == -1, "Missing owner must not fall back");
        Check(Resolve(plate, start, end, new[] { near, near }, new[] { false, false }, false) == -1, "Ambiguous geometry must reject");
        Check(Resolve(new Point(500, 0, 0), start, end, new[] { near }, new[] { false }, false) == -1, "Unknown side must reject");
        Check(Resolve(new Point(500, 0, 0), start, end, new[] { near }, new[] { true }, true) == 0, "Known owner supports centered plate");
        Check(Resolve(plate, start, end, new[] { new[] { 460.0, 540, -400, 0 } }, new[] { false }, false) == -1, "Opposite-only fallback must reject");
        Check(Resolve(plate, start, new Point(0, 3000, 0), new[] { near }, new[] { true }, true) == -1, "Unsupported axis must reject");
        Check(Resolve(plate, start, end, new[] { new[] { double.NaN, 540, 0, 400 } }, new[] { true }, true) == -1, "Nonfinite geometry must reject");
        // Preserve the old Euclidean-center ranking for ordinary same-side
        // selections, including different neighbor lengths and shuffled order.
        var random = new Random(2042);
        for (int run = 0; run < 100; run++)
        {
            var data = new double[5][];
            int expected = -1;
            double best = double.MaxValue, second = double.MaxValue;
            for (int i = 0; i < data.Length; i++)
            {
                double x = random.NextDouble() * 2500, y = 200 + random.NextDouble() * 1800;
                data[i] = new[] { x - 30, x + 30, 0, 2 * y };
                double distance = Math.Sqrt((x - plate.X) * (x - plate.X) + (y - plate.Y) * (y - plate.Y));
                if (distance < best) { second = best; best = distance; expected = i; }
                else if (distance < second) second = distance;
            }
            if (second - best <= 1) expected = -1;
            Check(Resolve(plate, start, end, data, new bool[5], false) == expected, "Same-side legacy ranking changed");
        }
        // Tolerance ambiguity must be independent of selection order.
        var close = new[] { 460.0, 540, 0, 401 };
        Check(Resolve(plate, start, end, new[] { near, close }, new bool[2], false) == -1, "Near tie must reject");
        Check(Resolve(plate, start, end, new[] { close, near }, new bool[2], false) == -1, "Reverse near tie must reject");
        Console.WriteLine("PASS: " + checks + " Slot02 selection checks; no Tekla drawing mutations.");
        return 0;
    }
}
