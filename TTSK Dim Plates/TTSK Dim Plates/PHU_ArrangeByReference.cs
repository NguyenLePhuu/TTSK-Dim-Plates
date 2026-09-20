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
            public string Name, Type;
            public SnapshotView Presentation;
            public RevisionPartBounds Part;
            public D.View View;
            public G.Point Origin;
            public double Scale;
            public double[] LocalAxes;
        }
        public sealed class ArrangeReference
        {
            public D.Drawing Drawing;
            public string ModelPath;
            public double Width, Height;
            public List<ArrangeReferenceView> Views = new List<ArrangeReferenceView>();
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
            while (views.MoveNext())
            {
                var view = views.Current as D.View; if (view == null) continue;
                var presentation = current.SingleOrDefault(v => v.Id == Id(view));
                var parts = new List<D.Part>(); var objects = view.GetAllObjects();
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
                result.Views.Add(new ArrangeReferenceView { Name = (view.Name ?? "").Trim().ToUpperInvariant(),
                    Type = view.ViewType.ToString(), View = view, Origin = new G.Point(origin),
                    Scale = scale, Presentation = presentation, Part = bounds,
                    LocalAxes = LocalArrangeAxes(view.DisplayCoordinateSystem, partX, partY, partZ) });
            }
            if (result.Views.Count == 0) throw new InvalidOperationException("Không có view để căn.");
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
            if (reference.Count == 0 || reference.Count != target.Count)
                throw new InvalidOperationException("Số view khác bản chuẩn; bỏ qua toàn bộ bản vẽ.");
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
                // Section A/B can represent the same projection as Top/Front on a different member.
                // Compare view axes in the main part's local frame, never global axes or A/B ordering.
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

        public static string ApplyArrangeReference(ArrangeReference reference)
        {
            var current = CaptureArrangeReference();
            if (current.Drawing.IsSameDatabaseObject(reference.Drawing)) return "REFERENCE · giữ nguyên";
            var targets = PlanArrangeReference(reference, current);
            int moved = 0;
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
                return moved == 0 ? "OK · đã thẳng hàng" : "OK · căn " + moved + " view, giữ scale";
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
    }
}
