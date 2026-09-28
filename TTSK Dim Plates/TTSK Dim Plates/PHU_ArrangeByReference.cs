using System;
using System.Collections.Generic;
using System.Linq;
using D = Tekla.Structures.Drawing;
using G = Tekla.Structures.Geometry3d;
using M = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    public static partial class PHU_DimensionRevision
    {
        public sealed class ArrangeReferenceView
        {
            public int Id;
            public string Name, Type;
            public SnapshotView Presentation;
            public RevisionPartBounds Part;
            public D.View View;
            public G.Point Origin;
            public double Scale;
            public double[] LocalAxes;
            public double LongitudinalCoverage;
            public int LongitudinalAxisIndex;
        }
        public sealed class ArrangeReference
        {
            public D.Drawing Drawing;
            public string ModelPath;
            public double Width, Height;
            public List<ArrangeReferenceView> Views = new List<ArrangeReferenceView>();
            public List<ArrangeReferenceView> PreservedViews = new List<ArrangeReferenceView>();
        }

        // Explicit projections are main views; section views need geometric evidence.
        public static bool IsArrangeMainView(string type)
        {
            return type == "FrontView" || type == "TopView" || type == "BackView"
                || type == "BottomView" || type == "EndView" || type == "ModelView" || type == "_3DView";
        }

        public static bool IsArrangeMainView(ArrangeReferenceView view)
        {
            if (view == null) return false;
            if (IsArrangeMainView(view.Type)) return true;
            if (view.Type != "SectionView" || view.LocalAxes == null || view.LocalAxes.Length != 6
                || !view.LocalAxes.All(Finite) || !Finite(view.LongitudinalCoverage)
                || view.LongitudinalAxisIndex < 0 || view.LongitudinalAxisIndex > 2) return false;
            // The main-part longitudinal axis must lie in the display plane. A transverse A-A
            // has no longitudinal projection, regardless of its name or paper scale.
            int axis = view.LongitudinalAxisIndex;
            double lengthProjection = view.LocalAxes[axis] * view.LocalAxes[axis]
                + view.LocalAxes[3 + axis] * view.LocalAxes[3 + axis];
            return Math.Abs(lengthProjection - 1) < .001 && view.LongitudinalCoverage >= .55;
        }

        static double SectionLongitudinalCoverage(D.View view, IList<G.Point> mainVertices, G.Point origin, G.Vector axis)
        {
            if (mainVertices.Count == 0) throw new InvalidOperationException("Không đọc được solid main part để phân loại mặt cắt.");
            Func<G.Point, double> project = p => G.Vector.Dot(new G.Vector(p - origin), axis);
            var main = mainVertices.Select(project).ToArray();
            var box = view.RestrictionBox;
            if (box == null || box.MinPoint == null || box.MaxPoint == null
                || box.MaxPoint.X < box.MinPoint.X || box.MaxPoint.Y < box.MinPoint.Y || box.MaxPoint.Z < box.MinPoint.Z)
                throw new InvalidOperationException("Không đọc được vùng cắt của view " + view.Name);
            var toGlobal = G.MatrixFactory.FromCoordinateSystem(view.ViewCoordinateSystem);
            var clipped = new List<double>();
            foreach (double x in new[] { box.MinPoint.X, box.MaxPoint.X })
                foreach (double y in new[] { box.MinPoint.Y, box.MaxPoint.Y })
                    foreach (double z in new[] { box.MinPoint.Z, box.MaxPoint.Z })
                        clipped.Add(project(toGlobal.Transform(new G.Point(x, y, z))));
            if (!main.All(Finite) || !clipped.All(Finite))
                throw new InvalidOperationException("Hình học phân loại mặt cắt không hữu hạn: " + view.Name);
            double length = main.Max() - main.Min();
            if (length <= .001) throw new InvalidOperationException("Không xác định được chiều dài main part.");
            return Math.Max(0, Math.Min(main.Max(), clipped.Max()) - Math.Max(main.Min(), clipped.Min())) / length;
        }

        // Read the same paper-space main-part primitives as Revision; never use DIM/frame extents.
        public static ArrangeReference CaptureArrangeReference()
        {
            var handler = new D.DrawingHandler(); var model = new M.Model();
            if (!handler.GetConnectionStatus() || !model.GetConnectionStatus())
                throw new InvalidOperationException("Không kết nối Tekla.");
            var drawing = handler.GetActiveDrawing();
            if (drawing == null) throw new InvalidOperationException("Không có bản vẽ đang mở để đọc vị trí view.");
            if (!(drawing is D.SinglePartDrawing) && !(drawing is D.AssemblyDrawing))
                throw new InvalidOperationException("Chỉ hỗ trợ bản vẽ Single part / Assembly.");
            var main = PHU_MainPartResolver.Resolve(model, drawing);
            if (main == null) throw new InvalidOperationException("Không xác định được main part.");
            var partSystem = main.GetCoordinateSystem();
            var global = model.GetWorkPlaneHandler().GetCurrentTransformationPlane().TransformationMatrixToGlobal;
            var partOrigin = global.Transform(partSystem.Origin);
            var partX = new G.Vector(global.Transform(partSystem.Origin + partSystem.AxisX) - partOrigin); partX.Normalize();
            var partY = new G.Vector(global.Transform(partSystem.Origin + partSystem.AxisY) - partOrigin); partY.Normalize();
            var partZ = G.Vector.Cross(partX, partY); partZ.Normalize();
            var current = ReadCurrentPresentation(new List<string>());
            var sheet = drawing.GetSheet();
            var result = new ArrangeReference { Drawing = drawing, ModelPath = model.GetInfo().ModelPath,
                Width = sheet.Width, Height = sheet.Height };
            var views = sheet.GetAllViews();
            List<G.Point> mainVertices = null;
            int longitudinalAxisIndex = 0;
            G.Vector longitudinalAxis = partX;
            while (views.MoveNext())
            {
                var view = views.Current as D.View; if (view == null) continue;
                var candidate = new ArrangeReferenceView { Id = Id(view), Name = view.Name,
                    Type = view.ViewType.ToString(), Origin = new G.Point(view.Origin), Scale = view.Attributes.Scale,
                    LocalAxes = LocalArrangeAxes(view.DisplayCoordinateSystem, partX, partY, partZ) };
                if (candidate.Type == "SectionView")
                {
                    if (mainVertices == null)
                    {
                        mainVertices = new List<G.Point>();
                        try
                        {
                            var edges = main.GetSolid().GetEdgeEnumerator();
                            while (edges.MoveNext())
                            {
                                var edge = edges.Current as Tekla.Structures.Solid.Edge;
                                if (edge == null) continue;
                                mainVertices.Add(global.Transform(edge.StartPoint));
                                mainVertices.Add(global.Transform(edge.EndPoint));
                            }
                            var axes = new[] { partX, partY, partZ };
                            var axisOrigin = partOrigin;
                            var lengths = axes.Select(axis =>
                            {
                                var values = mainVertices.Select(p => G.Vector.Dot(new G.Vector(p - axisOrigin), axis)).ToArray();
                                return values.Length == 0 ? 0 : values.Max() - values.Min();
                            }).ToArray();
                            longitudinalAxisIndex = Enumerable.Range(0, axes.Length).OrderByDescending(i => lengths[i]).First();
                            longitudinalAxis = axes[longitudinalAxisIndex];
                        }
                        catch (Exception ex) { throw new InvalidOperationException("Không xác định được hình học main part để phân loại view; chưa căn bản vẽ.", ex); }
                    }
                    candidate.LongitudinalAxisIndex = longitudinalAxisIndex;
                    candidate.LongitudinalCoverage = SectionLongitudinalCoverage(view, mainVertices, partOrigin, longitudinalAxis);
                }
                if (!IsArrangeMainView(candidate))
                {
                    // Once classified as preserved, do not require presentation/main-part
                    // mapping and never include the view in the Modify/rollback loop.
                    result.PreservedViews.Add(candidate);
                    continue;
                }
                var presentation = current.SingleOrDefault(v => v.Id == Id(view));
                // Ask Tekla for this model part directly; drawings can contain
                // hundreds of neighbors, dimensions and marks in every view.
                var parts = new List<D.Part>(); var objects = view.GetModelObjects(main.Identifier);
                while (objects.MoveNext())
                {
                    var part = objects.Current as D.Part;
                    if (part != null && part.ModelIdentifier.ID == main.Identifier.ID) parts.Add(part);
                }
                if (parts.Count != 1 || presentation == null)
                    throw new InvalidOperationException("View " + view.Name + " thiếu main part/presentation duy nhất.");
                var bounds = presentation.PartBounds.SingleOrDefault(p => p.Id == Id(parts[0]));
                var origin = view.Origin; double scale = view.Attributes.Scale;
                if (bounds == null || !new[] { bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY,
                    origin.X, origin.Y, origin.Z, scale, presentation.X, presentation.Y }.All(Finite)
                    || bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY || scale <= 0
                    || Math.Abs(origin.X - presentation.X) > .01 || Math.Abs(origin.Y - presentation.Y) > .01)
                    throw new InvalidOperationException("View " + view.Name + " có hình học chưa đồng bộ.");
                result.Views.Add(new ArrangeReferenceView { Id = Id(view), Name = (view.Name ?? "").Trim().ToUpperInvariant(),
                    Type = view.ViewType.ToString(), View = view, Origin = new G.Point(origin),
                    Scale = scale, Presentation = presentation, Part = bounds,
                    LocalAxes = candidate.LocalAxes, LongitudinalCoverage = candidate.LongitudinalCoverage,
                    LongitudinalAxisIndex = candidate.LongitudinalAxisIndex });
            }
            if (result.Views.Count == 0) throw new InvalidOperationException("Không có view chính để căn; giữ nguyên các view mặt cắt/chi tiết.");
            return result;
        }

        static double[] LocalArrangeAxes(G.CoordinateSystem view, G.Vector x, G.Vector y, G.Vector z)
        {
            var vx = new G.Vector(view.AxisX); vx.Normalize();
            var vy = new G.Vector(view.AxisY); vy.Normalize();
            return new[] { G.Vector.Dot(vx,x), G.Vector.Dot(vx,y), G.Vector.Dot(vx,z),
                G.Vector.Dot(vy,x), G.Vector.Dot(vy,y), G.Vector.Dot(vy,z) };
        }

        static bool SameArrangeOrientation(ArrangeReferenceView a, ArrangeReferenceView b)
        {
            return a.LocalAxes != null && b.LocalAxes != null && a.LocalAxes.Length == 6 && b.LocalAxes.Length == 6
                && a.LocalAxes.Zip(b.LocalAxes, (x,y) => Math.Abs(x-y)).All(delta => delta < .001);
        }

        // Cross-drawing IDs have no semantic meaning. Require a unique type/name bijection.
        public static List<int> MatchArrangeViews(IList<ArrangeReferenceView> reference, IList<ArrangeReferenceView> target)
        {
            if (reference.Concat(target).Any(v => !IsArrangeMainView(v)))
                throw new InvalidOperationException("Chỉ được ghép view chính; không căn mặt cắt phụ/chi tiết.");
            if (reference.Count == 0 || reference.Count != target.Count)
                throw new InvalidOperationException("Số view chính khác bản chuẩn; bỏ qua toàn bộ bản vẽ.");
            var matches = new List<int>();
            foreach (var view in target)
            {
                var candidates = Enumerable.Range(0, reference.Count)
                    .Where(i => reference[i].Type == view.Type && reference[i].Name == view.Name).ToList();
                if (candidates.Count == 0 && view.Type != "UnknownViewType"
                    && target.Count(v => v.Type == view.Type) == 1)
                    candidates = Enumerable.Range(0, reference.Count).Where(i => reference[i].Type == view.Type).ToList();
                candidates = candidates.Where(i => reference[i].LocalAxes == null || view.LocalAxes == null
                    || SameArrangeOrientation(reference[i], view)).ToList();
                // Longitudinal Section A/B may serve as Front/Top. Transverse and
                // locally cropped sections have already been excluded by role.
                if (candidates.Count != 1)
                    candidates = Enumerable.Range(0, reference.Count)
                        .Where(i => SameArrangeOrientation(reference[i], view)).ToList();
                if (candidates.Count != 1 || matches.Contains(candidates[0]))
                    throw new InvalidOperationException("Không ghép duy nhất được view " + view.Name + " (" + view.Type + ").");
                matches.Add(candidates[0]);
            }
            return matches;
        }

        public static void ValidateArrangeEditability(D.Drawing drawing)
        {
            // Freeze controls automatic updates; Issue/Ready are lifecycle flags, not edit locks.
            // Do not toggle any of these flags just to move views.
            if (drawing.IsLocked)
                throw new InvalidOperationException("Bản vẽ bị khóa (Locked=True). Hãy mở khóa trong Document Manager.");
        }

        public static List<G.Point> PlanArrangeReference(ArrangeReference reference, ArrangeReference current)
        {
            if (!String.Equals(current.ModelPath, reference.ModelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Model đã thay đổi.");
            if (current.Drawing.GetType() != reference.Drawing.GetType()
                || Math.Abs(current.Width - reference.Width) > .01 || Math.Abs(current.Height - reference.Height) > .01)
                throw new InvalidOperationException("Loại bản vẽ/khổ giấy khác bản chuẩn.");
            ValidateArrangeEditability(current.Drawing);
            var matches = MatchArrangeViews(reference.Views, current.Views);
            var targets = current.Views.Select((v, i) => RevisionBalancedOrigin(reference.Views[matches[i]].Presentation,
                reference.Views[matches[i]].Part, v.Part, v.Origin.Z)).ToList();
            for (int i = 0; i < targets.Count; i++)
            {
                var p = targets[i]; var b = current.Views[i].Part;
                if (!Finite(p.X) || !Finite(p.Y) || p.X + b.MinX < 0 || p.X + b.MaxX > current.Width
                    || p.Y + b.MinY < 0 || p.Y + b.MaxY > current.Height)
                    throw new InvalidOperationException("Main part sẽ ra ngoài khổ giấy; chưa dịch chuyển view.");
            }
            return targets;
        }

        // Fresh presentation read checks actual geometry, not only the assigned Origin property.
        public static void VerifyArrangeReference(ArrangeReference reference, ArrangeReference before)
        {
            var after = CaptureArrangeReference();
            if (!after.Drawing.IsSameDatabaseObject(before.Drawing)
                || !String.Equals(after.ModelPath, before.ModelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Model/bản vẽ đã thay đổi khi xác minh vị trí.");
            var targets = PlanArrangeReference(reference, after);
            VerifyPreservedArrangeViews(before.PreservedViews, after.PreservedViews);
            var previous = before.Views.ToDictionary(v => v.Presentation.Id);
            if (after.Views.Count != previous.Count) throw new InvalidOperationException("Số view đã thay đổi.");
            for (int i = 0; i < after.Views.Count; i++)
            {
                ArrangeReferenceView old;
                var v = after.Views[i];
                if (!previous.TryGetValue(v.Presentation.Id, out old)
                    || Math.Abs(v.Scale - old.Scale) > .000001 || RevisionPositionDiffers(v.Origin, targets[i]))
                    throw new InvalidOperationException("View " + v.Name + " chưa thẳng hàng hoặc scale đã thay đổi.");
            }
        }

        public static void VerifyPreservedArrangeViews(IList<ArrangeReferenceView> before, IList<ArrangeReferenceView> after)
        {
            var previous = before.ToDictionary(v => v.Id);
            if (after.Count != previous.Count || after.Select(v => v.Id).Distinct().Count() != after.Count)
                throw new InvalidOperationException("Số view mặt cắt/chi tiết đã thay đổi.");
            foreach (var v in after)
            {
                ArrangeReferenceView old;
                if (!previous.TryGetValue(v.Id, out old) || v.Type != old.Type
                    || !new[] { v.Origin.X, v.Origin.Y, v.Origin.Z, v.Scale,
                        old.Origin.X, old.Origin.Y, old.Origin.Z, old.Scale }.All(Finite)
                    || RevisionPositionDiffers(v.Origin, old.Origin) || Math.Abs(v.Scale - old.Scale) > .000001)
                    throw new InvalidOperationException("View mặt cắt/chi tiết " + v.Name + " đã đổi vị trí hoặc scale.");
            }
        }

        public static string ApplyArrangeReference(ArrangeReference reference)
        {
            int moved;
            return ApplyArrangeReference(reference, CaptureArrangeReference(), out moved);
        }

        // Batch already captured this drawing immediately before applying. Reuse
        // that fresh snapshot instead of fetching its presentation/solid twice.
        public static string ApplyArrangeReference(ArrangeReference reference, ArrangeReference current, out int moved)
        {
            moved = 0;
            var activeDrawing = new D.DrawingHandler().GetActiveDrawing();
            if (activeDrawing == null || !activeDrawing.IsSameDatabaseObject(current.Drawing))
                throw new InvalidOperationException("Bản vẽ đã thay đổi trước khi căn view.");
            if (current.Drawing.IsSameDatabaseObject(reference.Drawing)) return "REFERENCE · giữ nguyên";
            var targets = PlanArrangeReference(reference, current);
            if (!current.Views.Where((v, i) => RevisionPositionDiffers(v.Origin, targets[i])).Any())
                return ArrangeReferenceResult(0, current.PreservedViews.Count);
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var v = current.Views[i];
                    if (!RevisionPositionDiffers(v.Origin, targets[i])) continue;
                    v.View.Origin = new G.Point(targets[i]);
                    if (!v.View.Modify()) throw new InvalidOperationException("Không dịch chuyển được view " + v.Name);
                    moved++;
                }
                if (moved > 0 && !current.Drawing.CommitChanges()) throw new InvalidOperationException("Commit view thất bại.");
                for (int i = 0; i < targets.Count; i++)
                    if (!current.Views[i].View.Select() || RevisionPositionDiffers(current.Views[i].View.Origin, targets[i])
                        || Math.Abs(current.Views[i].View.Attributes.Scale - current.Views[i].Scale) > .000001)
                        throw new InvalidOperationException("View/scale chưa khớp sau khi đọc lại.");
                VerifyArrangeReference(reference, current);
                return ArrangeReferenceResult(moved, current.PreservedViews.Count);
            }
            catch (Exception error)
            {
                var active = new D.DrawingHandler().GetActiveDrawing();
                if (active == null || !active.IsSameDatabaseObject(current.Drawing))
                    throw new InvalidOperationException("Bản vẽ đã chuyển; không thể hoàn tác. " + error.Message);
                foreach (var v in current.Views)
                {
                    v.View.Origin = new G.Point(v.Origin);
                    if (!v.View.Modify()) throw new InvalidOperationException("Hoàn tác view thất bại. " + error.Message);
                }
                if (!current.Drawing.CommitChanges()) throw new InvalidOperationException("Commit hoàn tác thất bại. " + error.Message);
                throw;
            }
        }

        static string ArrangeReferenceResult(int moved, int preserved)
        {
            return (moved == 0 ? "OK · view chính đã thẳng hàng" : "OK · căn " + moved + " view chính, giữ scale")
                + " · giữ nguyên " + preserved + " view mặt cắt/chi tiết";
        }
    }
}
