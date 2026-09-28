using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;
using M = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>Slot 07 connection plans for vertical hollow-column assemblies.
    /// Beam and legacy Inzai shape routes retain their own geometry contracts.</summary>
    internal static partial class PHU_Slot07_ColumnConnections
    {
        internal const double GeometryTolerance = 0.75;
        internal const double DimensionTolerance = 0.04;

        // Route from authoritative main-part geometry in GLOBAL coordinates,
        // never the current work plane, drawing name, project name or missing Top.
        internal static bool IsColumnRoute(string profile, IList<G.Point> globalReference)
        {
            return IsHollowProfile(profile) && IsVerticalReference(globalReference);
        }

        internal static bool IsVerticalReference(IList<G.Point> globalReference)
        {
            if (globalReference == null || globalReference.Count != 2
                || globalReference.Any(v => !Valid(v))) return false;
            double dx = globalReference[1].X - globalReference[0].X;
            double dy = globalReference[1].Y - globalReference[0].Y;
            double dz = globalReference[1].Z - globalReference[0].Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return length > 100 && Math.Abs(dz) / length >= 0.999;
        }

        private static bool IsHollowProfile(string profile)
        {
            string p = (profile ?? "").Trim().ToUpperInvariant();
            return p.StartsWith("STKR") || p.StartsWith("SHS") || p.StartsWith("RHS") || p.StartsWith("BOX");
        }

        internal static bool IsCurrentDrawingColumn(D.Drawing drawing)
        {
            if (!(drawing is D.AssemblyDrawing)) return false;
            string profile;
            var reference=ReadGlobalMainReference(drawing,out profile);
            return IsColumnRoute(profile,reference);
        }

        internal static bool IsCurrentDrawingVertical(D.Drawing drawing)
        {
            if (!(drawing is D.AssemblyDrawing)) return false;
            string profile;
            return IsVerticalReference(ReadGlobalMainReference(drawing,out profile));
        }

        private static List<G.Point> ReadGlobalMainReference(D.Drawing drawing,out string profile)
        {
            var model = new M.Model();
            if (!model.GetConnectionStatus()) throw new InvalidOperationException("Chưa kết nối Model API.");
            var main = PHU_MainPartResolver.Resolve(model, drawing);
            if (main == null) throw new InvalidOperationException("Không đọc được MainPart để phân luồng Slot 07.");
            var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var reference = new List<G.Point>();
            foreach (G.Point point in main.GetReferenceLine(false)) reference.Add(toGlobal.Transform(point));
            profile=main.Profile.ProfileString;
            return reference;
        }

        internal sealed class Part
        {
            public int Id, Assembly;
            public string Profile, Position;
            public bool Plate;
            public readonly List<G.Point> Reference = new List<G.Point>();
            public readonly List<G.Point> Vertices = new List<G.Point>();
            // Only bolt groups whose drilling axis faces this view.
            public readonly List<G.Point> WebBolts = new List<G.Point>();
        }

        internal sealed class View
        {
            public D.View Object;
            public int Id;
            public double Scale;
            public Part Main;
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Existing> Dimensions = new List<Existing>();
            public readonly List<Grid> Grids = new List<Grid>();
        }

        internal sealed class Existing
        {
            public G.Vector Normal;
            public readonly List<G.Point> Feet = new List<G.Point>();
        }

        internal sealed class Plan
        {
            public View View;
            public string Kind, Evidence;
            public G.Vector Normal;
            public double Distance;
            public double WitnessMin, WitnessMax;
            public List<G.Point> Feet;
            public bool Satisfied;
            public bool RequireExactFeet;
        }

        internal sealed class Result
        {
            public D.Drawing Drawing;
            public int DrawingId;
            public string DrawingName;
            public readonly List<Plan> Plans = new List<Plan>();
            public readonly List<string> Notes = new List<string>();
            public readonly List<string> AuditDetails = new List<string>();
            public string Report()
            {
                var text = new StringBuilder("SLOT 07 - COLUMN CONNECTION PLAN - READ ONLY\r\n");
                text.AppendLine("Drawing=" + DrawingName + " ID=" + DrawingId);
                text.AppendLine("Plans=" + Plans.Count + " Existing=" + Plans.Count(p => p.Satisfied)
                    + " Missing=" + Plans.Count(p => !p.Satisfied));
                foreach (var p in Plans)
                {
                    var axis = new G.Vector(-p.Normal.Y, p.Normal.X, 0);
                    var stations = Stations(p.Feet, axis);
                    text.AppendLine("VIEW=" + p.View.Id + " " + p.Kind + " " + (p.Satisfied ? "EXISTING" : "MISSING")
                        + " mm=" + String.Join(" / ", stations.Skip(1).Select((v, i) => F(v - stations[i])))
                        + " " + p.Evidence);
                    text.AppendLine("  feet=" + String.Join("; ", p.Feet.Select(v => "(" + F(v.X) + "," + F(v.Y) + ")"))
                        + " normal=" + p.Normal + " distance=" + F(p.Distance));
                }
                foreach (string note in Notes) text.AppendLine("NOTE " + note);
                foreach (string detail in AuditDetails) text.AppendLine("AUDIT " + detail);
                return text.ToString();
            }
        }

        private sealed class Arm
        {
            public Part Part;
            public G.Point Node, Low, High, Terminal;
            public G.Vector Out;
        }

        internal static Result Read()
        {
            var model = new M.Model();
            var handler = new D.DrawingHandler();
            if (!model.GetConnectionStatus() || !handler.GetConnectionStatus())
                throw new InvalidOperationException("Chưa kết nối Tekla.");
            D.Drawing drawing = handler.GetActiveDrawing();
            if (!(drawing is D.AssemblyDrawing))
                throw new InvalidOperationException("Slot 07 cần bản vẽ assembly cột đang mở.");
            if (!IsCurrentDrawingColumn(drawing))
                throw new InvalidOperationException("Bản vẽ không thuộc nhánh cột hộp đứng Slot 07; giữ luồng Inzai cũ.");
            M.Part main = PHU_MainPartResolver.Resolve(model, drawing);
            if (main == null) throw new InvalidOperationException("Không đọc được MainPart của bản vẽ.");
            int assembly = main.GetAssembly().Identifier.ID;
            var result = new Result { Drawing = drawing, DrawingId = Id(drawing), DrawingName = drawing.Name };
            result.AuditDetails.Add("Model=" + model.GetInfo().ModelPath + " Mark=" + drawing.Mark
                + " Main=" + main.Identifier.ID + " Profile=" + main.Profile.ProfileString);
            result.AuditDetails.Add("Coordinates: unchanged current work plane -> global -> view. Model/Drawing API connected.");
            var toGlobal = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var views = drawing.GetSheet().GetAllViews();
            while (views.MoveNext())
            {
                var view = views.Current as D.View;
                if (view == null || view.ViewType == D.View.ViewTypes.SectionView) continue;
                var snapshot = new View { Object = view, Id = Id(view), Scale = view.Attributes.Scale };
                result.AuditDetails.Add("VIEW=" + snapshot.Id + " Name=" + view.Name + " Type=" + view.ViewType
                    + " Scale=" + snapshot.Scale + " SheetOrigin=" + view.Origin + " Origin=" + view.DisplayCoordinateSystem.Origin
                    + " X=" + view.DisplayCoordinateSystem.AxisX + " Y=" + view.DisplayCoordinateSystem.AxisY);
                if (!Finite(snapshot.Scale) || snapshot.Scale <= 0) throw new InvalidOperationException("Scale không hợp lệ.");
                var toView = G.MatrixFactory.ToCoordinateSystem(view.DisplayCoordinateSystem);
                Func<G.Point, G.Point> transform = p => toView.Transform(toGlobal.Transform(p));
                var objects = view.GetAllObjects(typeof(D.Part));
                var seen = new HashSet<int>();
                while (objects.MoveNext())
                {
                    var dp = objects.Current as D.Part;
                    if (dp == null || !seen.Add(dp.ModelIdentifier.ID)) continue;
                    var part = model.SelectModelObject(dp.ModelIdentifier) as M.Part;
                    if (part == null || part.GetAssembly().Identifier.ID != assembly) continue;
                    try
                    {
                        var data = ReadPart(part, transform, result.Notes);
                        snapshot.Parts.Add(data);
                        if (part.Identifier.ID == main.Identifier.ID) snapshot.Main = data;
                    }
                    catch (Exception ex)
                    {
                        // An incomplete solid cannot supply a dimension witness point.
                        result.Notes.Add("VIEW=" + snapshot.Id + " bỏ P" + part.Identifier.ID + ": " + ex.Message);
                    }
                }
                if (snapshot.Main == null) continue;
                ReadExisting(snapshot);
                var connectionPlans = Build(snapshot, result.Notes);
                if (snapshot.Main.Reference.Count != 2
                    || Length(snapshot.Main.Reference[0], snapshot.Main.Reference[1]) < 100) continue;
                var globalOriginInView=toView.Transform(new G.Point(0,0,0));
                var globalUpInView=toView.Transform(new G.Point(0,0,1));
                if(Math.Abs(globalUpInView.X-globalOriginInView.X)>0.001
                    || Math.Abs(globalUpInView.Y-globalOriginInView.Y-1)>0.001
                    || Math.Abs(globalUpInView.Z-globalOriginInView.Z)>0.001)
                    throw new InvalidOperationException("VIEW="+snapshot.Id+" không có chiều đứng hướng lên theo cột; dừng để tránh đảo đáy/đỉnh plate.");
                ReadGrids(snapshot);
                result.Plans.AddRange(connectionPlans);
                result.Plans.AddRange(BuildColumnGrid(snapshot, connectionPlans));
            }
            if (Id(handler.GetActiveDrawing()) != result.DrawingId)
                throw new InvalidOperationException("Bản vẽ hiện tại đã đổi trong lúc đọc; dừng Slot 07.");
            if (result.Plans.Select(p=>p.View.Id).Distinct().Count()!=2)
                throw new InvalidOperationException("Nhánh cột hộp cần đúng hai hình chiếu đứng X-X/Y-Y rõ ràng.");
            return result;
        }

        private static Part ReadPart(M.Part part, Func<G.Point, G.Point> transform, List<string> notes)
        {
            var data = new Part { Id = part.Identifier.ID, Assembly = part.GetAssembly().Identifier.ID,
                Profile = part.Profile.ProfileString ?? "", Plate = part is M.ContourPlate };
            string position = "";
            part.GetReportProperty("PART_POS", ref position);
            data.Position = position;
            foreach (G.Point point in part.GetReferenceLine(false)) data.Reference.Add(transform(point));
            var edges = part.GetSolid().GetEdgeEnumerator();
            while (edges.MoveNext())
            {
                var edge = edges.Current as Tekla.Structures.Solid.Edge;
                if (edge == null) continue;
                data.Vertices.Add(transform(edge.StartPoint)); data.Vertices.Add(transform(edge.EndPoint));
            }
            if (data.Vertices.Count == 0 || data.Vertices.Any(p => !Valid(p)))
                throw new InvalidOperationException("Không đọc được solid đầy đủ.");
            try
            {
                var bolts = part.GetBolts();
                while (bolts.MoveNext())
                {
                    var group = bolts.Current as M.BoltGroup;
                    if (group == null) continue;
                    var cs = group.GetCoordinateSystem();
                    var drill = G.Vector.Cross(cs.AxisX, cs.AxisY);
                    var start = transform(cs.Origin);
                    var end = transform(new G.Point(cs.Origin.X + drill.X, cs.Origin.Y + drill.Y, cs.Origin.Z + drill.Z));
                    double length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) + Math.Pow(end.Z - start.Z, 2));
                    if (length < 0.001 || Math.Abs(end.Z - start.Z) / length < 0.98) continue;
                    foreach (G.Point point in group.BoltPositions) data.WebBolts.Add(transform(point));
                }
            }
            catch (Exception ex)
            {
                data.WebBolts.Clear();
                notes.Add("P" + data.Id + " chưa đọc đủ bu lông, bỏ DIM lỗ: " + ex.Message);
            }
            return data;
        }

        internal static List<Plan> Build(View view, List<string> notes)
        {
            var plans = new List<Plan>();
            Part main = view.Main;
            if (main == null || main.Reference.Count != 2 || main.Vertices.Count == 0) return plans;
            G.Vector up = Unit(main.Reference[0], main.Reference[1]);
            if (up == null) return plans;
            if (up.Y < -0.001 || (Math.Abs(up.Y) < 0.001 && up.X < 0)) up = Neg(up);
            var right = new G.Vector(up.Y, -up.X, 0);
            double width = main.Vertices.Max(p => Dot(p, right)) - main.Vertices.Min(p => Dot(p, right));
            if (Length(main.Reference[0], main.Reference[1]) < Math.Max(100, width * 2)) return plans;
            G.Point origin = main.Reference[0];
            double axis = Dot(origin, right);
            var columns = view.Parts.Where(p => !p.Plate && p.Assembly == main.Assembly && p.Reference.Count == 2
                && p.Reference.All(r => Math.Abs(Dot(r, right) - axis) <= GeometryTolerance)
                && Length(p.Reference[0], p.Reference[1]) > 10).ToList();
            var arms = new List<Arm>();
            foreach (Part part in view.Parts)
            {
                string profile = (part.Profile ?? "").ToUpperInvariant();
                if (part.Plate || part.Assembly != main.Assembly || part.Id == main.Id || part.Reference.Count != 2
                    || part.Vertices.Count == 0 || !(profile.StartsWith("H") || profile.StartsWith("I") || profile.StartsWith("BH"))) continue;
                var ends = part.Reference.OrderBy(p => Math.Abs(Dot(p, right) - axis)).ToArray();
                G.Point node = ends[0], outer = ends[1];
                var outward = Unit(node, outer);
                if (outward == null || Math.Abs(Dot(outward, up)) > 0.02
                    || Math.Abs(Dot(node, right) - axis) > GeometryTolerance
                    || Math.Abs(outer.Z - node.Z) > GeometryTolerance || Length(node, outer) < 100) continue;
                // The node may be on ANY collinear segment of the same column assembly.
                bool attached = columns.Any(c => Between(Dot(node, up), c.Reference.Min(p => Dot(p, up)), c.Reference.Max(p => Dot(p, up)))
                    && c.Vertices.Count > 0 && DepthOverlap(c, part)
                    && c.Reference.Any(p => Math.Abs(p.Z - node.Z) <= GeometryTolerance));
                if (!attached) continue;
                double terminal = part.Vertices.Max(p => Dot(p, outward));
                var face = part.Vertices.Where(p => Math.Abs(Dot(p, outward) - terminal) <= GeometryTolerance).ToList();
                if (face.Count < 2 || terminal - Dot(node, outward) < 100) continue;
                var low = face.OrderBy(p => Dot(p, up)).First();
                var high = face.OrderByDescending(p => Dot(p, up)).First();
                double depth = Dot(high, up) - Dot(low, up);
                if (depth < 10) continue;
                // REF must coincide with an actual terminal edge. A centre-line REF is not silently substituted.
                var endAtNodeLevel = face.OrderBy(p => Math.Abs(Dot(p, up) - Dot(node, up))).First();
                if (Math.Abs(Dot(endAtNodeLevel, up) - Dot(node, up)) > GeometryTolerance)
                { notes.Add("P" + part.Id + " REF không trùng mép ngoài; cần quy tắc riêng."); continue; }
                var arm = new Arm { Part = part, Node = node, Out = outward, Low = low, High = high, Terminal = endAtNodeLevel };
                arms.Add(arm);
                int firstPlan = plans.Count;
                string evidence = "P" + part.Id + "[" + part.Position + "]";
                Add(plans, view, "DEPTH", evidence + " solid terminal edges", outward, 42, low, high);
                var holes = part.WebBolts.Where(p => Valid(p) && Dot(p, up) > Dot(low, up) + GeometryTolerance
                    && Dot(p, up) < Dot(high, up) - GeometryTolerance
                    && Dot(p, outward) < terminal - GeometryTolerance
                    && Dot(p, outward) > Dot(node, outward)
                    && terminal - Dot(p, outward) <= Math.Max(150, Length(node, outer) * 0.3))
                    .OrderByDescending(p => Dot(p, outward)).ThenBy(p => Math.Abs(Dot(p, up) - Dot(node, up))).ToList();
                if (holes.Count > 0)
                {
                    G.Point hole = holes[0];
                    Add(plans, view, "EDGE-BOLT", evidence + " actual web bolt to terminal", up, 6, endAtNodeLevel, hole);
                    Add(plans, view, "REF-BOLT", evidence + " actual web bolt to reference-side edge", outward, 34, hole, endAtNodeLevel);
                }
                else notes.Add(evidence + " không có bu lông mặt bụng phù hợp; giữ DIM mép/định vị.");
                AddPlateGap(plans, view, arm, up, false, notes);
                AddPlateGap(plans, view, arm, up, true, notes);
                foreach (var plan in plans.Skip(firstPlan)) IncludeWitnessRegion(plan, part.Vertices);
            }
            var pending = new List<Arm>(arms);
            while (pending.Count > 0)
            {
                Arm first = pending[0];
                var group = pending.Where(a => Length(a.Node, first.Node) <= GeometryTolerance && Math.Abs(a.Node.Z - first.Node.Z) <= GeometryTolerance).ToList();
                pending.RemoveAll(a => group.Contains(a));
                if (group.Count > 2 || (group.Count == 2 && Dot(group[0].Out, group[1].Out) > -0.98))
                { notes.Add("VIEW=" + view.Id + " có nhiều nhánh cùng phía tại node; bỏ chuỗi định vị mơ hồ."); continue; }
                var feet = group.Select(a => a.Terminal).ToList(); feet.Add(first.Node);
                int before = plans.Count;
                Add(plans, view, "OVERALL", "column REF -> " + String.Join(",", group.Select(a => "P" + a.Part.Id)), up, 20, feet.ToArray());
                if (plans.Count > before)
                    foreach (var arm in group) IncludeWitnessRegion(plans[plans.Count - 1], arm.Part.Vertices);
            }
            foreach (Plan plan in plans) plan.Satisfied = view.Dimensions.Any(d => Matches(plan, d));
            return plans;
        }

        private static void AddPlateGap(List<Plan> plans, View view, Arm arm, G.Vector up, bool upper, List<string> notes)
        {
            G.Point edge = upper ? arm.High : arm.Low;
            double level = Dot(edge, up), node = Dot(arm.Node, arm.Out);
            var candidates = new List<Tuple<Part, G.Point, double>>();
            foreach (Part plate in view.Parts.Where(p => p.Plate && p.Assembly == arm.Part.Assembly && p.Vertices.Count > 0))
            {
                if (!DepthOverlap(plate, arm.Part) || !Between(node, plate.Vertices.Min(p => Dot(p, arm.Out)), plate.Vertices.Max(p => Dot(p, arm.Out)))) continue;
                double lo = plate.Vertices.Min(p => Dot(p, up)), hi = plate.Vertices.Max(p => Dot(p, up));
                if (hi - lo > Math.Min(50, (Dot(arm.High, up) - Dot(arm.Low, up)) / 2)) continue;
                if (!Between(level, lo, hi)) continue;
                double outside = upper ? hi : lo, gap = upper ? outside - level : level - outside;
                if (gap <= DimensionTolerance || gap > 25) continue;
                var face = plate.Vertices.Where(p => Math.Abs(Dot(p, up) - outside) <= GeometryTolerance)
                    .OrderByDescending(p => Dot(p, arm.Out)).First();
                candidates.Add(Tuple.Create(plate, face, gap));
            }
            if (candidates.Count != 1)
            {
                if (candidates.Count > 1) notes.Add("P" + arm.Part.Id + " có nhiều plate cho khe " + (upper ? "trên" : "dưới") + "; bỏ DIM khe.");
                return;
            }
            var match = candidates[0];
            Add(plans, view, upper ? "GAP-HIGH" : "GAP-LOW", "P" + arm.Part.Id + " edge -> PLATE P" + match.Item1.Id + " outside face", arm.Out, 22, edge, match.Item2);
        }

        private static void Add(List<Plan> plans, View view, string kind, string evidence, G.Vector normal, double paperDistance, params G.Point[] points)
        {
            var axis = new G.Vector(-normal.Y, normal.X, 0);
            var sorted = points.OrderBy(p => Dot(p, axis)).ToList();
            if (sorted.Count < 2 || sorted.Any(p => !Valid(p))) return;
            if (sorted.Skip(1).Where((p, i) => Dot(p, axis) - Dot(sorted[i], axis) <= DimensionTolerance).Any()) return;
            double line = sorted.Max(p => Dot(p, normal)) + paperDistance * view.Scale;
            plans.Add(new Plan { View = view, Kind = kind, Evidence = evidence, Normal = normal,
                Feet = sorted.Select(p => new G.Point(p.X, p.Y, 0)).ToList(), Distance = line - Dot(sorted[0], normal),
                WitnessMin = sorted.Min(p => Dot(p, normal)), WitnessMax = sorted.Max(p => Dot(p, normal)) });
        }

        private static void IncludeWitnessRegion(Plan plan, IEnumerable<G.Point> vertices)
        {
            plan.WitnessMin = Math.Min(plan.WitnessMin, vertices.Min(p => Dot(p, plan.Normal)));
            plan.WitnessMax = Math.Max(plan.WitnessMax, vertices.Max(p => Dot(p, plan.Normal)));
        }

        private static void ReadExisting(View view)
        {
            var objects = view.Object.GetAllObjects(typeof(D.StraightDimensionSet));
            while (objects.MoveNext())
            {
                var set = objects.Current as D.StraightDimensionSet;
                if (set == null) continue;
                var data = ReadSet(set);
                if (data != null) view.Dimensions.Add(data);
            }
        }

        private static Existing ReadSet(D.StraightDimensionSet set)
        {
            var normal = Member(set, "UpDirection") as G.Vector;
            if (normal == null) return null;
            var data = new Existing { Normal = normal };
            var children = set.GetObjects();
            while (children.MoveNext())
            {
                var child = children.Current as D.StraightDimension;
                if (child == null) continue;
                data.Feet.Add(child.StartPoint); data.Feet.Add(child.EndPoint);
            }
            return data.Feet.Count > 0 && data.Feet.All(Valid) ? data : null;
        }

        internal static bool Matches(Plan plan, Existing existing)
        {
            if (existing.Normal == null || existing.Feet.Count < 2) return false;
            double length = Math.Sqrt(existing.Normal.X * existing.Normal.X + existing.Normal.Y * existing.Normal.Y);
            if (length <= 0.0001 || Dot(plan.Normal, existing.Normal) / length < 0.999) return false;
            var axis = new G.Vector(-plan.Normal.Y, plan.Normal.X, 0);
            var expected = Stations(plan.Feet, axis); var actual = Stations(existing.Feet, axis);
            int start = actual.FindIndex(v => Math.Abs(v - expected[0]) <= DimensionTolerance);
            if (start < 0 || start + expected.Count > actual.Count
                || expected.Where((v, i) => Math.Abs(v - actual[start + i]) > DimensionTolerance).Any()) return false;
            if(plan.RequireExactFeet && !plan.Feet.All(p=>existing.Feet.Any(q=>Length(p,q)<=DimensionTolerance))) return false;
            // A contiguous part of an existing chain already satisfies the requested segments.
            // Witness feet may slide on the same branch, but not to an unrelated connection.
            var relevant = existing.Feet.Where(p => Dot(p, axis) >= expected[0] - DimensionTolerance
                && Dot(p, axis) <= expected[expected.Count - 1] + DimensionTolerance);
            return relevant.All(p => Dot(p, plan.Normal) >= plan.WitnessMin - 1 && Dot(p, plan.Normal) <= plan.WitnessMax + 1);
        }

        internal static string Execute()
        {
            Result result = Read();
            if (result.Plans.Count == 0) throw new InvalidOperationException("Slot 07 chưa nhận diện được cụm cột–dầm phù hợp. " + String.Join("; ", result.Notes));
            var missing = result.Plans.Where(p => !p.Satisfied).ToList();
            var created = new List<D.StraightDimensionSet>();
            var handler = new D.DrawingHandler();
            try
            {
                var factory = new D.StraightDimensionSetHandler();
                foreach (Plan plan in missing)
                {
                    if (Id(handler.GetActiveDrawing()) != result.DrawingId) throw new InvalidOperationException("Bản vẽ hiện tại đã đổi.");
                    var points = new D.PointList(); foreach (G.Point point in plan.Feet) points.Add(point);
                    var dimension = factory.CreateDimensionSet(plan.View.Object, points, plan.Normal, plan.Distance);
                    if (dimension == null) throw new InvalidOperationException("Không tạo được DIM " + plan.Kind);
                    created.Add(dimension);
                    if (plan.Feet.Count > 2)
                    {
                        var attributes = dimension.Attributes;
                        if (attributes == null) throw new InvalidOperationException("Không đọc được thuộc tính DIM chuỗi.");
                        var combined = attributes.CombinedDimension ?? new D.DimensionSetBaseAttributes.CombinedDimensionAttributes();
                        combined.Format = D.DimensionSetBaseAttributes.CombineFormats.Off;
                        combined.MinimumNumberToCombine = Math.Max(5, plan.Feet.Count);
                        attributes.CombinedDimension = combined;
                        dimension.Attributes = attributes;
                        if (!dimension.Modify() || !dimension.Select()
                            || dimension.Attributes.CombinedDimension.Format != D.DimensionSetBaseAttributes.CombineFormats.Off)
                            throw new InvalidOperationException("Không giữ được từng đoạn của DIM chuỗi " + plan.Kind);
                    }
                    var actual = ReadSet(dimension);
                    if (actual == null || actual.Feet.Count != (plan.Feet.Count - 1) * 2 || !Matches(plan, actual))
                        throw new InvalidOperationException("DIM vừa tạo không khớp số đoạn/chân DIM: " + plan.Kind);
                }
                if (created.Count > 0)
                {
                    if (Id(handler.GetActiveDrawing()) != result.DrawingId) throw new InvalidOperationException("Bản vẽ hiện tại đã đổi.");
                    result.Drawing.CommitChanges();
                }
            }
            catch (Exception ex)
            {
                int remaining = 0;
                foreach (var dimension in created)
                {
                    try { if (!dimension.Delete()) remaining++; } catch { remaining++; }
                }
                throw new InvalidOperationException("Slot 07 dừng: " + ex.Message
                    + (remaining > 0 ? " Còn " + remaining + " DIM mới cần kiểm tra." : " Đã thu hồi DIM vừa tạo; giữ nguyên DIM có sẵn."), ex);
            }
            return "Slot 07: tạo " + created.Count + " DIM, giữ " + (result.Plans.Count - missing.Count)
                + " DIM phù hợp có sẵn. Không đổi model/bố cục view."
                + (result.Notes.Count > 0 ? " " + String.Join("; ", result.Notes) : "");
        }

        private static List<double> Stations(IEnumerable<G.Point> points, G.Vector axis)
        {
            var result = new List<double>();
            foreach (double value in points.Select(p => Dot(p, axis)).OrderBy(v => v))
                if (result.Count == 0 || value - result[result.Count - 1] > DimensionTolerance) result.Add(value);
            return result;
        }
        private static bool DepthOverlap(Part a, Part b)
        { return Math.Min(a.Vertices.Max(p => p.Z), b.Vertices.Max(p => p.Z)) - Math.Max(a.Vertices.Min(p => p.Z), b.Vertices.Min(p => p.Z)) > 1; }
        private static bool Between(double value, double low, double high) { return value >= low - GeometryTolerance && value <= high + GeometryTolerance; }
        private static G.Vector Unit(G.Point a, G.Point b)
        { double length = Length(a, b); return length < 0.001 ? null : new G.Vector((b.X - a.X) / length, (b.Y - a.Y) / length, 0); }
        private static double Length(G.Point a, G.Point b) { return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)); }
        private static G.Vector Neg(G.Vector v) { return new G.Vector(-v.X, -v.Y, 0); }
        private static double Dot(G.Point p, G.Vector v) { return p.X * v.X + p.Y * v.Y; }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Valid(G.Point p) { return p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static object Member(object value, string name)
        { return value == null ? null : value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value, null); }
        private static int Id(object value)
        {
            var identifier = Member(value, "Identifier") as Tekla.Structures.Identifier;
            if (identifier == null || identifier.ID <= 0) throw new InvalidOperationException("Không đọc được ID bản vẽ/view.");
            return identifier.ID;
        }
    }
}
