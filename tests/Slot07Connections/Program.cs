using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using G = Tekla.Structures.Geometry3d;
using S = Tekla.Technology.Akit.UserScript.PHU_Slot07_ColumnConnections;

partial class Program
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
    static G.Point P(double x, double y, double z = 0) { return new G.Point(x, y, z); }
    static S.Part Box(int id, double x0, double x1, double y0, double y1, string profile, bool plate = false)
    {
        var part = new S.Part { Id = id, Assembly = 1, Profile = profile, Position = "fixture", Plate = plate };
        foreach (double x in new[] { x0, x1 }) foreach (double y in new[] { y0, y1 }) foreach (double z in new[] { -75.0, 75.0 }) part.Vertices.Add(P(x, y, z));
        return part;
    }
    // Derived from A-C11-3: MainPart ends before the 2281 connection station.
    static S.View Fixture()
    {
        var view = new S.View { Id = 1, Scale = 15 };
        view.Main = Box(1, -150, 150, -19, 1181, "STKR300*300*16");
        view.Main.Reference.AddRange(new[] { P(0, -19), P(0, 1181) });
        view.Parts.Add(view.Main);
        var column = Box(2, -150, 150, 2131, 2281, "STKR300*300*16");
        column.Reference.AddRange(new[] { P(0, 2131), P(0, 2281) }); view.Parts.Add(column);
        var left = Box(3, -600, -150, 2131, 2281, "H150*150*7*10");
        left.Reference.AddRange(new[] { P(-600, 2281), P(0, 2281) });
        left.WebBolts.AddRange(new[] { P(-410, 2206), P(-470, 2206), P(-530, 2206) }); view.Parts.Add(left);
        var right = Box(4, 150, 1000, 2131, 2281, "H150*150*7*10");
        right.Reference.AddRange(new[] { P(0, 2281), P(1000, 2281) }); view.Parts.Add(right);
        view.Parts.Add(Box(5, -175, 175, 2128, 2147, "PL19", true));
        view.Parts.Add(Box(6, -175, 175, 2265, 2284, "PL19", true));
        return view;
    }
    static List<S.Plan> Build(S.View v) { return S.Build(v, new List<string>()); }
    static double[] Values(S.Plan plan)
    {
        var axis = new G.Vector(-plan.Normal.Y, plan.Normal.X, 0);
        var values = plan.Feet.Select(p => p.X * axis.X + p.Y * axis.Y).OrderBy(x => x).ToArray();
        return values.Skip(1).Select((v, i) => Math.Round(v - values[i], 3)).ToArray();
    }
    static S.Existing Existing(S.Plan p)
    { var e = new S.Existing { Normal = p.Normal }; e.Feet.AddRange(p.Feet.Select(v => new G.Point(v))); return e; }
    static void Transform(S.View view, double angle, double dx, double dy)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        foreach (var part in view.Parts) foreach (var list in new[] { part.Reference, part.Vertices, part.WebBolts })
            foreach (var p in list) { double x = p.X, y = p.Y; p.X = c * x - s * y + dx; p.Y = s * x + c * y + dy; }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--legacy-audit")
            { LegacyAudit.Run(args[1], args[2]); return 0; }
            if (args.Length == 3 && args[0] == "--inzai-audit")
            { InzaiDiagnosis.Read(args[1],args[2]); return 0; }
            if (args.Length == 4 && args[0] == "--apply-inzai")
            { ApplyInzai(args[1],Int32.Parse(args[2]),args[3]); return 0; }
            if (args.Length == 2 && args[0] == "--read-dimensions")
            { ReadDimensions(args[1]); return 0; }
            if (args.Length == 4 && args[0] == "--apply-product")
            {
                // Explicit MUTATING integration mode. Invoke the delivered engine,
                // pin drawing identity, preserve old objects, then verify rerun.
                var assembly = System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
                var formType = assembly.GetType("TTSK_AutoDim_Plates.MainForm", true);
                var handler = new Tekla.Structures.Drawing.DrawingHandler();
                var before = S.Read();
                Check(before.DrawingId == Int32.Parse(args[2]), "Active drawing differs from audited drawing");
                var layout = Layout(before.Drawing);
                var ids = DimensionIds(before.Drawing);
                var originalDimensions=NativeDimensionSignatures(before.Drawing);
                // No UI constructor, updater or persisted settings. Exercise the
                // same coordinator used by the Slot 07 button and batch flow.
                object form = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(formType);
                var execute = formType.GetMethod("RunDataCenterCurrentDrawing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
                Action run = () =>
                {
                    var result = execute.Invoke(form, new object[] { true });
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    string message = (string)result.GetType().GetField("Message", flags).GetValue(result);
                    Check((bool)result.GetType().GetField("Success", flags).GetValue(result), message);
                    Console.WriteLine("MUTATING Slot07 coordinator: " + message);
                };
                run();
                var after = S.Read();
                Check(after.DrawingId == before.DrawingId && after.Plans.Count == before.Plans.Count, "Drawing/plan count changed");
                Check(after.Plans.All(p => p.Satisfied), "Created dimensions fail read-back");
                Check(Layout(after.Drawing) == layout, "View layout or work plane changed");
                var afterIds = DimensionIds(after.Drawing);
                Check(ids.All(afterIds.Contains), "An existing dimension was removed");
                var retained=NativeDimensionSignatures(after.Drawing);
                Check(originalDimensions.All(p=>retained.ContainsKey(p.Key) && retained[p.Key]==p.Value),"Approved existing feet or distances changed");
                run();
                Check(afterIds.SetEquals(DimensionIds(handler.GetActiveDrawing())), "Rerun created duplicate dimensions");
                var repeat=NativeDimensionSignatures(handler.GetActiveDrawing());
                Check(retained.All(p=>repeat.ContainsKey(p.Key) && repeat[p.Key]==p.Value),"Rerun moved an existing dimension");
                System.IO.File.WriteAllText(args[3], after.Report());
                Console.WriteLine("PASS: live creation/read-back; existing IDs and layout preserved; second run adds zero.");
                return 0;
            }
            if (args.Length == 2 && args[0] == "--audit")
            {
                // No Apply/Run entry point: live inspection followed by in-memory replay only.
                var result = S.Read();
                System.IO.File.WriteAllText(args[1], result.Report(), new System.Text.UTF8Encoding(false));
                Console.WriteLine(result.Report());
                int replayed = 0;
                foreach (var snapshot in result.Plans.Select(p => p.View).Distinct())
                {
                    var expected=result.Plans.Where(p=>p.View==snapshot).Select(GeometrySignature).OrderBy(s=>s).ToArray();
                    snapshot.Dimensions.Clear(); // DTO list only; never a DrawingObject operation.
                    var missing = Build(snapshot);
                    missing.AddRange(S.BuildColumnGrid(snapshot, missing));
                    Check(missing.Select(GeometrySignature).OrderBy(s=>s).SequenceEqual(expected),"Empty-drawing replay changed semantic feet/side/placement");
                    Check(missing.All(p => !p.Satisfied), "Cleared snapshot should propose all missing dimensions");
                    replayed += missing.Count;
                }
                Check(replayed == result.Plans.Count, "Live snapshot replay changed geometry plan count");
                Console.WriteLine("PASS: read-only audit + missing-DIM replay on " + replayed + " plans. No drawing/model writes.");
                return 0;
            }
            if (args.Length > 0) throw new ArgumentException("Use no arguments for offline tests, or --audit <report-path> for read-only inspection.");
            var view = Fixture(); var plans = Build(view);
            PlateTests();
            DrawingRouteTests();
            foreach (string profile in new[] { "STKR300*300*16", "RHS300*300*16", "SHS300*16", "BOX300*300*16" })
            {
                Check(S.IsColumnRoute(profile, new[] { P(3200, -910, 400), P(3200, -910, 2500) }), "Vertical hollow column route");
                Check(S.IsColumnRoute(profile, new[] { P(3200, -910, 2500), P(3200, -910, 400) }), "Reversed global axis route");
                Check(!S.IsColumnRoute(profile, new[] { P(0, 0), P(2100, 0) }), "Horizontal hollow beam retains legacy Inzai path");
                Check(!S.IsColumnRoute(profile, new[] { P(0, 0), P(500, 0, 2100) }), "Inclined member must not use column branch");
            }
            foreach (string profile in new[] { "H300*300*10*15", "C200*90*8", "L100*100*10", "PL19", "", null })
                Check(!S.IsColumnRoute(profile, new[] { P(0, 0), P(0, 0, 2100) }), "Existing Inzai profile route retained");
            Check(!S.IsColumnRoute("STKR300*300*16", new[] { P(0, 0), P(Double.NaN, 0, 2100) }), "Invalid axis rejected");
            Check(!S.IsColumnRoute("STKR300*300*16", new[] { P(0, 0), P(0, 0) }), "Zero axis rejected");
            Check(plans.Count == 9, "Expected nine plans for two arms, one without bolts");
            Check(plans.All(p => !p.Satisfied), "Empty drawing must report missing dimensions");
            Check(Values(plans.Single(p => p.Kind == "OVERALL")).OrderBy(v => v).SequenceEqual(new[] { 600.0, 1000.0 }), "600/1000 must come from solid and column node");
            Check(Values(plans.Single(p => p.Kind == "EDGE-BOLT"))[0] == 70, "Nearest actual web bolt = 70");
            Check(Values(plans.Single(p => p.Kind == "REF-BOLT"))[0] == 75, "Web bolt to edge = 75");
            Check(plans.Where(p => p.Kind == "DEPTH").All(p => Values(p)[0] == 150), "Solid terminal depth = 150");
            Check(plans.Where(p => p.Kind.StartsWith("GAP")).Count() == 4 && plans.Where(p => p.Kind.StartsWith("GAP")).All(p => Values(p)[0] == 3), "Four actual plate gaps = 3");
            Check(plans.All(p => p.Distance > 0 && !Double.IsNaN(p.Distance)), "Valid placement distances");
            view.Dimensions.AddRange(plans.Select(Existing));
            Check(Build(view).All(p => p.Satisfied), "Repeat planning must reuse existing dimensions");
            view.Dimensions.RemoveAt(1);
            Check(Build(view).Count(p => !p.Satisfied) == 1, "Only removed dimension should be missing");
            var boltPlan = plans.Single(p => p.Kind == "REF-BOLT");
            var shifted = Existing(boltPlan); shifted.Feet.First(p => p.Y == 2206).X = -428;
            Check(S.Matches(boltPlan, shifted), "Sliding witness along same branch must not duplicate 75");
            shifted.Feet.ForEach(p => p.X += 2000);
            Check(!S.Matches(boltPlan, shifted), "Unrelated connection with same value must not match");
            var wrong = Existing(boltPlan); wrong.Feet[0].Y += 0.2;
            Check(!S.Matches(boltPlan, wrong), "Wrong witness foot must not satisfy plan");
            wrong = Existing(boltPlan); wrong.Normal = new G.Vector(0, 1, 0);
            Check(!S.Matches(boltPlan, wrong), "Wrong measurement axis must not match");
            var chain = plans.Single(p => p.Kind == "OVERALL"); var extra = Existing(chain); extra.Feet.Add(P(1200, 2281));
            Check(S.Matches(chain, extra), "Contiguous segment of longer chain is already dimensioned");
            extra.Feet.Add(P(500, 2281));
            Check(!S.Matches(chain, extra), "Split 1000 segment must not masquerade as unsplit 1000");
            foreach (double angle in new[] { 0.0, 0.31, 1.1, Math.PI })
            {
                var rotated = Fixture(); Transform(rotated, angle, 12345, -6789);
                var actual = Build(rotated);
                Check(actual.Count == 9, "Translated/rotated geometry must preserve topology");
                Check(actual.SelectMany(Values).OrderBy(v => v).SequenceEqual(plans.SelectMany(Values).OrderBy(v => v)), "Rotation must preserve dimension values");
            }
            var scale = Fixture(); scale.Scale = 30;
            Check(Build(scale).Count == 9 && Values(Build(scale).Single(p => p.Kind == "OVERALL")).OrderBy(v => v).SequenceEqual(new[] { 600.0, 1000.0 }), "View scale must not change geometry");
            var noBolts = Fixture(); noBolts.Parts.Single(p => p.Id == 3).WebBolts.Clear();
            Check(Build(noBolts).Count == 7 && Build(noBolts).All(p => !p.Kind.Contains("BOLT")), "No fabricated holes on bolt-free arms");
            var neighbor = Fixture(); neighbor.Parts.Single(p => p.Id == 4).Assembly = 99;
            Check(Build(neighbor).Count == 6 && Values(Build(neighbor).Single(p => p.Kind == "OVERALL"))[0] == 600, "Exclude other assembly");
            var detached = Fixture(); detached.Parts.RemoveAll(p => p.Id == 2);
            Check(Build(detached).Count == 0, "No guessed node beyond all column segments");
            var skew = Fixture(); skew.Parts.Single(p => p.Id == 4).Reference[1].Y += 200;
            Check(Build(skew).Count == 6, "Skew member must be skipped");
            var depth = Fixture(); var far = depth.Parts.Single(p => p.Id == 4);
            foreach (var p in far.Reference.Concat(far.Vertices)) p.Z += 500;
            Check(Build(depth).Count == 6, "Projection overlap without depth overlap is not a connection");
            var ambiguous = Fixture(); ambiguous.Parts.Add(Box(7, -175, 175, 2265, 2284, "PL19", true));
            Check(Build(ambiguous).Count(p => p.Kind == "GAP-HIGH") == 0, "Ambiguous plates must not produce guessed gaps");
            var noPlate = Fixture(); noPlate.Parts.RemoveAll(p => p.Plate);
            Check(Build(noPlate).Count == 5, "No fabricated gap without plate geometry");
            Console.WriteLine("PASS: " + count + " Slot07 offline assertions; no Tekla connection or drawing writes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static HashSet<int> DimensionIds(Tekla.Structures.Drawing.Drawing drawing)
    {
        var ids = new HashSet<int>();
        var views = drawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            var v = views.Current as Tekla.Structures.Drawing.View; if (v == null) continue;
            var objects = v.GetAllObjects(typeof(Tekla.Structures.Drawing.StraightDimensionSet));
            while (objects.MoveNext()) ids.Add(ObjectId(objects.Current));
        }
        return ids;
    }
    static int ObjectId(object o)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return ((Tekla.Structures.Identifier)o.GetType().GetProperty("Identifier", flags).GetValue(o, null)).ID;
    }
    static string Layout(Tekla.Structures.Drawing.Drawing drawing)
    {
        var lines = new List<string>();
        var views = drawing.GetSheet().GetAllViews();
        while (views.MoveNext())
        {
            var v = views.Current as Tekla.Structures.Drawing.View; if (v == null) continue;
            lines.Add(ObjectId(v) + "|" + v.Origin + "|" + v.Attributes.Scale + "|" + v.RestrictionBox.MinPoint + "|" + v.RestrictionBox.MaxPoint);
        }
        lines.Sort(StringComparer.Ordinal);
        lines.Add(new Tekla.Structures.Model.Model().GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal.ToString());
        return String.Join("\n", lines);
    }

    static void ReadDimensions(string path)
    {
        var model = new Tekla.Structures.Model.Model();
        var handler = new Tekla.Structures.Drawing.DrawingHandler();
        if (!model.GetConnectionStatus() || !handler.GetConnectionStatus()) throw new Exception("Tekla API disconnected");
        var drawing = handler.GetActiveDrawing();
        if (drawing == null) throw new Exception("No active drawing");
        var text = new System.Text.StringBuilder("READ ONLY DIMENSION FEET; no insert/modify/delete/commit.\n");
        text.AppendLine("Drawing=" + drawing.Name + " Mark=" + drawing.Mark + " Type=" + drawing.GetType().Name + " Id=" + ObjectId(drawing));
        text.AppendLine("Model=" + model.GetInfo().ModelPath);
        var views = drawing.GetSheet().GetAllViews(); int selected=0;
        while(views.MoveNext())
        {
            var view=views.Current as Tekla.Structures.Drawing.View; if(view==null)continue;
            var objects=view.GetAllObjects(typeof(Tekla.Structures.Drawing.StraightDimensionSet));
            while(objects.MoveNext())
            {
                var set=objects.Current as Tekla.Structures.Drawing.StraightDimensionSet; if(set==null)continue;
                var children=set.GetObjects(); var segments=new List<string>();
                while(children.MoveNext())
                {
                    var dim=children.Current as Tekla.Structures.Drawing.StraightDimension; if(dim==null)continue;
                    var value=Member(dim,"Value") as IEnumerable;
                    string label=value==null?Convert.ToString(Member(dim,"Value")):String.Concat(value.Cast<object>().OfType<Tekla.Structures.Drawing.TextElement>().Select(e=>e.Value));
                    segments.Add("  DIM="+ObjectId(dim)+" label="+label.Trim()+" start="+Member(dim,"StartPoint")+" end="+Member(dim,"EndPoint"));
                }
                selected++;
                text.AppendLine("VIEW="+ObjectId(view)+" Type="+view.ViewType+" Scale="+view.Attributes.Scale+" SheetOrigin="+view.Origin
                    +" ViewOrigin="+view.DisplayCoordinateSystem.Origin+" AxisX="+view.DisplayCoordinateSystem.AxisX+" AxisY="+view.DisplayCoordinateSystem.AxisY);
                text.AppendLine("SET="+ObjectId(set)+" up="+Member(set,"UpDirection")+" distance="+Member(set,"Distance"));
                foreach(string segment in segments)text.AppendLine(segment);
            }
        }
        if(ObjectId(handler.GetActiveDrawing())!=ObjectId(drawing))throw new Exception("Active drawing changed during read");
        text.AppendLine("AllSets="+selected);
        System.IO.File.WriteAllText(path,text.ToString(),new System.Text.UTF8Encoding(false));
        Console.WriteLine(text.ToString());
    }
    static object Member(object o,string name)
    {if(o==null)return null;var p=o.GetType().GetProperty(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);return p==null?null:p.GetValue(o,null);}
    static Dictionary<int,string> NativeDimensionSignatures(Tekla.Structures.Drawing.Drawing drawing)
    {
        var result=new Dictionary<int,string>();var views=drawing.GetSheet().GetAllViews();
        Func<G.Point,string> point=p=>p.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+p.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        while(views.MoveNext())
        {
            var view=views.Current as Tekla.Structures.Drawing.View;if(view==null)continue;
            var sets=view.GetAllObjects(typeof(Tekla.Structures.Drawing.StraightDimensionSet));
            while(sets.MoveNext())
            {
                var set=sets.Current as Tekla.Structures.Drawing.StraightDimensionSet;if(set==null)continue;
                var segments=new List<string>();var children=set.GetObjects();
                while(children.MoveNext())
                {var dim=children.Current as Tekla.Structures.Drawing.StraightDimension;if(dim!=null)segments.Add(point(dim.StartPoint)+"/"+point(dim.EndPoint));}
                result.Add(ObjectId(set),Member(set,"UpDirection")+"|"+Convert.ToDouble(Member(set,"Distance")).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+String.Join(";",segments.OrderBy(s=>s)));
            }
        }
        return result;
    }
    static string GeometrySignature(S.Plan p)
    {return p.Kind+"|"+p.Normal+"|"+p.Distance.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+String.Join(";",p.Feet.Select(f=>f.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+f.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));}
}
