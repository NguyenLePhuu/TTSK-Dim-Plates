#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

using DrawingPart = Tekla.Structures.Drawing.Part;
using DrawingView = Tekla.Structures.Drawing.View;
using ModelPart = Tekla.Structures.Model.Part;

namespace Tekla.Technology.Akit.UserScript
{
    public enum AutoSectionWorkerStatus
    {
        CreatedSingle,
        CreatedAssemblyBottom,
        PreflightFailed,
        CreateFailed,
        RolledBack,
        UnsafeRollbackFailed
    }

    public class AutoSectionWorkerResult
    {
        public AutoSectionWorkerStatus Status = AutoSectionWorkerStatus.PreflightFailed;
        public string Message = "";
        public DrawingView SectionB = null;
        public DrawingView SectionC = null;
        public bool IsSafeToContinue = true;
    }

    public class SectionScript
    {
        private const double TOL = 1.0;
        private const double SECTION_B_EXTRA_DEPTH = 5.0;
        private const double SECTION_C_EXTRA_START = 3.0;
        private const double DEFAULT_SECTION_GAP = 55.0;
        private const bool SECTION_LINE_LEFT_TO_RIGHT = false;
        private const string SECTION_MARK_ATTRIBUTE_NAME = "GEO_SECTION";

        private struct SectionGeometry
        {
            public Point BStart;
            public Point BEnd;
            public double BDepthUp;
            public double BDepthDown;
            public Point CStart;
            public Point CEnd;
            public double CDepthUp;
            public double CDepthDown;
        }

        private sealed class SectionAttributeSet
        {
            public DrawingView.ViewAttributes ViewAttributes;
            public SectionMarkBase.SectionMarkAttributes MarkAttributes;
        }

        public static AutoSectionWorkerResult RunSingleSafe(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName)
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            SectionGeometry geometry;
            SectionAttributeSet attributesB;
            SectionAttributeSet attributesC;
            Point topOrigin;
            Point frontOrigin;
            double savedTopScale;
            string preflightMessage;

            if (!TryPreflightSingle(
                drawing,
                model,
                part,
                topView,
                frontView,
                sectionViewAttributeName,
                out geometry,
                out attributesB,
                out attributesC,
                out topOrigin,
                out frontOrigin,
                out savedTopScale,
                out preflightMessage))
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = preflightMessage;
                return result;
            }

            DrawingView sectionB = null;
            DrawingView sectionC = null;
            SectionMark markB = null;
            SectionMark markC = null;

            double sectionGap = GetSectionGap(frontView);
            double insertBY = topOrigin.Y > frontOrigin.Y
                ? topOrigin.Y
                : frontOrigin.Y + sectionGap;
            Point insertB = new Point(frontOrigin.X, insertBY, 0.0);
            Point insertC = new Point(frontOrigin.X, frontOrigin.Y - sectionGap, 0.0);

            bool createB = CreateOneSectionView(
                frontView,
                "B",
                geometry.BStart,
                geometry.BEnd,
                insertB,
                geometry.BDepthUp,
                geometry.BDepthDown,
                attributesB,
                out sectionB,
                out markB);

            if (!createB)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Section B.",
                    sectionB,
                    markB,
                    sectionC,
                    markC);

            if (!CommitAndValidateCreatedSection(drawing, part, sectionB))
                return FinishCreateFailure(
                    drawing,
                    "Section B tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    sectionC,
                    markC);

            bool createC = CreateOneSectionView(
                frontView,
                "C",
                geometry.CStart,
                geometry.CEnd,
                insertC,
                geometry.CDepthUp,
                geometry.CDepthDown,
                attributesC,
                out sectionC,
                out markC);

            if (!createC)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Section C.",
                    sectionB,
                    markB,
                    sectionC,
                    markC);

            if (!CommitAndValidateCreatedSection(drawing, part, sectionC))
                return FinishCreateFailure(
                    drawing,
                    "Section C tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    sectionC,
                    markC);

            bool topDeleteReturned = SafeDelete(topView);
            bool deleteCommitReturned = SafeCommit(drawing);
            bool topStillExists = IsViewPresent(drawing, topView);

            if (!topDeleteReturned || !deleteCommitReturned || topStillExists)
            {
                bool rollbackSucceeded = RollbackCreatedSections(
                    drawing,
                    sectionB,
                    markB,
                    sectionC,
                    markC);

                bool topIsSafe = IsViewPresent(drawing, topView);
                result.SectionB = sectionB;
                result.SectionC = sectionC;

                if (rollbackSucceeded && topIsSafe)
                {
                    result.Status = AutoSectionWorkerStatus.RolledBack;
                    result.Message = "Khong xoa duoc TopView; Section B/C da rollback, TopView van duoc giu.";
                    return result;
                }

                result.Status = AutoSectionWorkerStatus.UnsafeRollbackFailed;
                result.IsSafeToContinue = false;
                result.Message = "Xoa TopView hoac rollback B/C that bai; drawing khong an toan de save.";
                return result;
            }

            result.Status = AutoSectionWorkerStatus.CreatedSingle;
            result.Message = "Single: da tao Section B/C va xoa dung TopView goc.";
            result.SectionB = sectionB;
            result.SectionC = sectionC;
            return result;
        }

        public static AutoSectionWorkerResult RunAssemblySafe(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName)
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            SectionGeometry geometry;
            SectionAttributeSet attributesB;
            Point frontOrigin;
            string preflightMessage;

            if (!TryPreflightAssembly(
                drawing,
                model,
                part,
                topView,
                frontView,
                sectionViewAttributeName,
                out geometry,
                out attributesB,
                out frontOrigin,
                out preflightMessage))
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = preflightMessage;
                return result;
            }

            DrawingView sectionB = null;
            SectionMark markB = null;
            Point insertB = new Point(
                frontOrigin.X,
                frontOrigin.Y - GetSectionGap(frontView),
                0.0);

            bool created = CreateOneSectionView(
                frontView,
                "B",
                geometry.CStart,
                geometry.CEnd,
                insertB,
                geometry.CDepthUp,
                geometry.CDepthDown,
                attributesB,
                out sectionB,
                out markB);

            if (!created)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Bottom Section B cho Assembly.",
                    sectionB,
                    markB,
                    null,
                    null);

            if (!CommitAndValidateCreatedSection(drawing, part, sectionB))
                return FinishCreateFailure(
                    drawing,
                    "Bottom Section B tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    null,
                    null);

            result.Status = AutoSectionWorkerStatus.CreatedAssemblyBottom;
            result.Message = "Assembly: da giu Top/Front va tao Bottom Section ten B.";
            result.SectionB = sectionB;
            return result;
        }

        private static bool TryPreflightSingle(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName,
            out SectionGeometry geometry,
            out SectionAttributeSet attributesB,
            out SectionAttributeSet attributesC,
            out Point topOrigin,
            out Point frontOrigin,
            out double savedTopScale,
            out string message)
        {
            geometry = new SectionGeometry();
            attributesB = null;
            attributesC = null;
            topOrigin = null;
            frontOrigin = null;
            savedTopScale = 0.0;
            message = "";

            if (!(drawing is SinglePartDrawing))
            {
                message = "RunSingleSafe chi nhan SinglePartDrawing.";
                return false;
            }

            if (!ValidateCommonInput(drawing, model, part, topView, frontView, out message))
                return false;

            topOrigin = ClonePoint(topView.Origin);
            frontOrigin = ClonePoint(frontView.Origin);
            savedTopScale = GetViewScale(topView);

            if (!IsFinitePoint(topOrigin) || !IsFinitePoint(frontOrigin))
            {
                message = "Khong doc duoc origin Top/Front.";
                return false;
            }

            if (!IsFinite(savedTopScale) || savedTopScale <= 0.0)
            {
                message = "Khong doc duoc scale TopView de preflight Auto Section.";
                return false;
            }

            double frontScale = GetViewScale(frontView);
            if (!IsFinite(frontScale) || frontScale <= 0.0)
            {
                message = "Khong doc duoc scale FrontView de copy sang Section B/C.";
                return false;
            }

            if (!TryGetSectionGeometry(
                model,
                part,
                frontView,
                out geometry,
                out message))
                return false;

            if (!TryLoadSectionAttributes(
                "B",
                frontScale,
                sectionViewAttributeName,
                out attributesB,
                out message))
                return false;
            if (!TryLoadSectionAttributes(
                "C",
                frontScale,
                sectionViewAttributeName,
                out attributesC,
                out message))
                return false;

            return true;
        }

        private static bool TryPreflightAssembly(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName,
            out SectionGeometry geometry,
            out SectionAttributeSet attributesB,
            out Point frontOrigin,
            out string message)
        {
            geometry = new SectionGeometry();
            attributesB = null;
            frontOrigin = null;
            message = "";

            if (!(drawing is AssemblyDrawing))
            {
                message = "RunAssemblySafe chi nhan AssemblyDrawing.";
                return false;
            }

            if (!ValidateCommonInput(drawing, model, part, topView, frontView, out message))
                return false;

            frontOrigin = ClonePoint(frontView.Origin);
            if (!IsFinitePoint(frontOrigin))
            {
                message = "Khong doc duoc origin FrontView.";
                return false;
            }

            double frontScale = GetViewScale(frontView);
            if (!IsFinite(frontScale) || frontScale <= 0.0)
            {
                message = "Khong doc duoc scale FrontView de copy sang Bottom Section.";
                return false;
            }

            if (!TryGetSectionGeometry(
                model,
                part,
                frontView,
                out geometry,
                out message))
                return false;

            if (!TryLoadSectionAttributes(
                "B",
                frontScale,
                sectionViewAttributeName,
                out attributesB,
                out message))
                return false;

            return true;
        }

        private static bool ValidateCommonInput(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            out string message)
        {
            message = "";

            if (drawing == null || model == null || part == null)
            {
                message = "Input drawing/model/part null.";
                return false;
            }

            if (!model.GetConnectionStatus())
            {
                message = "Khong ket noi duoc model Tekla.";
                return false;
            }

            if (part.Identifier == null || part.Identifier.ID <= 0)
            {
                message = "ModelPart khong co Identifier hop le.";
                return false;
            }

            if (topView == null || frontView == null || System.Object.ReferenceEquals(topView, frontView))
            {
                message = "TopView/FrontView khong hop le.";
                return false;
            }

            if (!IsViewPresent(drawing, topView) || !IsViewPresent(drawing, frontView))
            {
                message = "TopView hoac FrontView khong thuoc drawing hien tai.";
                return false;
            }

            if (!ViewContainsPart(topView, part.Identifier) ||
                !ViewContainsPart(frontView, part.Identifier))
            {
                message = "TopView/FrontView khong chua dung ModelPart duoc truyen vao.";
                return false;
            }

            return true;
        }

        private static bool TryGetSectionGeometry(
            Model model,
            ModelPart part,
            DrawingView frontView,
            out SectionGeometry geometry,
            out string message)
        {
            geometry = new SectionGeometry();
            message = "";
            TransformationPlane oldPlane = null;

            try
            {
                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TransformationPlane(frontView.DisplayCoordinateSystem));

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                {
                    message = "Khong doc duoc solid cua ModelPart.";
                    return false;
                }

                Point min = solid.MinimumPoint;
                Point max = solid.MaximumPoint;
                double minX = min.X;
                double maxX = max.X;
                double minY = min.Y;
                double maxY = max.Y;

                if (!IsFinite(minX) || !IsFinite(maxX) ||
                    !IsFinite(minY) || !IsFinite(maxY) ||
                    maxX - minX <= TOL || maxY - minY <= TOL)
                {
                    message = "Solid cua ModelPart khong co extents hop le trong FrontView.";
                    return false;
                }

                double flangeThickness = GetFlangeThickness(part);
                if (flangeThickness <= 0.0)
                {
                    message = "Khong doc duoc do day canh cua profile I/H.";
                    return false;
                }

                double bCutY = maxY;
                double cCutY = minY + flangeThickness + SECTION_C_EXTRA_START;
                double bDepth = flangeThickness + SECTION_B_EXTRA_DEPTH;
                double cDepth = cCutY - minY;

                if (!IsFinite(bDepth) || !IsFinite(cDepth) ||
                    bDepth <= TOL || cDepth <= TOL ||
                    bDepth >= maxY - minY || cDepth >= maxY - minY)
                {
                    message = "Depth Section B/C tinh tu do day canh khong hop le.";
                    return false;
                }

                Point bLeft = new Point(minX, bCutY, 0.0);
                Point bRight = new Point(maxX, bCutY, 0.0);
                Point cLeft = new Point(minX, cCutY, 0.0);
                Point cRight = new Point(maxX, cCutY, 0.0);

                bool sectionLineLeftToRight = SECTION_LINE_LEFT_TO_RIGHT;
                if (sectionLineLeftToRight)
                {
                    geometry.BStart = bLeft;
                    geometry.BEnd = bRight;
                    geometry.CStart = cLeft;
                    geometry.CEnd = cRight;
                }
                else
                {
                    geometry.BStart = bRight;
                    geometry.BEnd = bLeft;
                    geometry.CStart = cRight;
                    geometry.CEnd = cLeft;
                }

                geometry.BDepthUp = bDepth;
                geometry.BDepthDown = 0.0;
                geometry.CDepthUp = cDepth;
                geometry.CDepthDown = 0.0;
                return true;
            }
            catch (Exception ex)
            {
                message = "Tinh geometry Section loi: " + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (oldPlane != null)
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static bool TryLoadSectionAttributes(
            string sectionName,
            double frontScale,
            string sectionViewAttributeName,
            out SectionAttributeSet attributeSet,
            out string message)
        {
            attributeSet = null;
            message = "";

            try
            {
                if (string.IsNullOrWhiteSpace(sectionViewAttributeName))
                {
                    message =
                        "Khong doc duoc View properties cua hang Section views " +
                        "truoc khi Load Standard.";
                    return false;
                }

                DrawingView.ViewAttributes viewAttributes =
                    new DrawingView.ViewAttributes();

                if (!viewAttributes.LoadAttributes(sectionViewAttributeName))
                {
                    message = "Thieu hoac khong load duoc view attribute: " +
                        sectionViewAttributeName;
                    return false;
                }

                SectionMarkBase.SectionMarkAttributes markAttributes =
                    new SectionMarkBase.SectionMarkAttributes();

                if (!markAttributes.LoadAttributes(SECTION_MARK_ATTRIBUTE_NAME))
                {
                    message = "Thieu hoac khong load duoc section mark attribute: " +
                        SECTION_MARK_ATTRIBUTE_NAME;
                    return false;
                }

                if (frontScale > 0.0 && IsFinite(frontScale))
                    viewAttributes.Scale = frontScale;

                markAttributes.MarkName = sectionName;
                attributeSet = new SectionAttributeSet();
                attributeSet.ViewAttributes = viewAttributes;
                attributeSet.MarkAttributes = markAttributes;
                return true;
            }
            catch (Exception ex)
            {
                message = "Load section attribute loi: " + ex.Message;
                return false;
            }
        }

        private static bool CreateOneSectionView(
            DrawingView frontView,
            string sectionName,
            Point startPoint,
            Point endPoint,
            Point insertionPoint,
            double depthUp,
            double depthDown,
            SectionAttributeSet attributeSet,
            out DrawingView sectionView,
            out SectionMark sectionMark)
        {
            sectionView = null;
            sectionMark = null;

            try
            {
                if (frontView == null || attributeSet == null ||
                    !IsFinitePoint(startPoint) || !IsFinitePoint(endPoint) ||
                    !IsFinitePoint(insertionPoint) ||
                    !IsFinite(depthUp) || !IsFinite(depthDown))
                    return false;

                bool created = DrawingView.CreateSectionView(
                    frontView,
                    ClonePoint(startPoint),
                    ClonePoint(endPoint),
                    ClonePoint(insertionPoint),
                    depthUp,
                    depthDown,
                    attributeSet.ViewAttributes,
                    attributeSet.MarkAttributes,
                    out sectionView,
                    out sectionMark);

                if (sectionView != null)
                {
                    sectionView.Name = sectionName;
                    if (!sectionView.Modify())
                        return false;
                }

                if (sectionMark != null && !sectionMark.Modify())
                    return false;

                return created && sectionView != null && sectionMark != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool CommitAndValidateCreatedSection(
            Drawing drawing,
            ModelPart part,
            DrawingView sectionView)
        {
            if (!SafeCommit(drawing))
                return false;

            Thread.Sleep(150);

            return sectionView != null &&
                   IsViewPresent(drawing, sectionView) &&
                   ViewContainsPart(sectionView, part.Identifier);
        }

        private static AutoSectionWorkerResult FinishCreateFailure(
            Drawing drawing,
            string message,
            DrawingView sectionB,
            SectionMark markB,
            DrawingView sectionC,
            SectionMark markC)
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            result.Message = message;
            result.SectionB = sectionB;
            result.SectionC = sectionC;

            bool hasCreatedReference = sectionB != null || markB != null ||
                sectionC != null || markC != null;

            if (!hasCreatedReference)
            {
                result.Status = AutoSectionWorkerStatus.CreateFailed;
                return result;
            }

            if (RollbackCreatedSections(
                drawing,
                sectionB,
                markB,
                sectionC,
                markC))
            {
                result.Status = AutoSectionWorkerStatus.RolledBack;
                result.Message += " Cac object vua tao da rollback.";
                return result;
            }

            result.Status = AutoSectionWorkerStatus.UnsafeRollbackFailed;
            result.IsSafeToContinue = false;
            result.Message += " Rollback that bai; drawing khong an toan de save.";
            return result;
        }

        private static bool RollbackCreatedSections(
            Drawing drawing,
            DrawingView sectionB,
            SectionMark markB,
            DrawingView sectionC,
            SectionMark markC)
        {
            bool deleteReturned = true;

            if (markC != null)
                deleteReturned = SafeDelete(markC) && deleteReturned;
            if (sectionC != null)
                deleteReturned = SafeDelete(sectionC) && deleteReturned;
            if (markB != null)
                deleteReturned = SafeDelete(markB) && deleteReturned;
            if (sectionB != null)
                deleteReturned = SafeDelete(sectionB) && deleteReturned;

            bool commitReturned = SafeCommit(drawing);
            Thread.Sleep(100);

            bool viewsRemoved =
                (sectionB == null || !IsViewPresent(drawing, sectionB)) &&
                (sectionC == null || !IsViewPresent(drawing, sectionC));

            return deleteReturned && commitReturned && viewsRemoved;
        }

        private static bool IsViewPresent(Drawing drawing, DrawingView target)
        {
            try
            {
                if (drawing == null || target == null || drawing.GetSheet() == null)
                    return false;

                int targetIdentifier = GetViewIdentifier(target);

                DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
                while (views != null && views.MoveNext())
                {
                    DrawingView current = views.Current as DrawingView;
                    if (current == null)
                        continue;

                    if (System.Object.ReferenceEquals(current, target) ||
                        (targetIdentifier > 0 &&
                         GetViewIdentifier(current) == targetIdentifier))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static int GetViewIdentifier(DrawingView view)
        {
            if (view == null)
                return 0;

            string[] propertyNames = new string[]
            {
                "ViewIdentifier",
                "Identifier",
                "DrawingIdentifier"
            };

            foreach (string propertyName in propertyNames)
            {
                try
                {
                    PropertyInfo property = view.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance);

                    if (property == null || !property.CanRead)
                        continue;

                    Identifier identifier = property.GetValue(view, null) as Identifier;
                    if (identifier != null && identifier.ID > 0)
                        return identifier.ID;
                }
                catch
                {
                }
            }

            return 0;
        }

        private static bool ViewContainsPart(DrawingView view, Identifier partIdentifier)
        {
            try
            {
                if (view == null || partIdentifier == null)
                    return false;

                DrawingObjectEnumerator parts =
                    view.GetAllObjects(typeof(DrawingPart));

                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (drawingPart == null || drawingPart.ModelIdentifier == null)
                        continue;

                    if (drawingPart.ModelIdentifier.ID == partIdentifier.ID)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool SafeCommit(Drawing drawing)
        {
            try
            {
                return drawing != null && drawing.CommitChanges();
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeDelete(DrawingObject drawingObject)
        {
            try
            {
                return drawingObject != null && drawingObject.Delete();
            }
            catch
            {
                return false;
            }
        }

        private static double GetSectionGap(DrawingView referenceView)
        {
            double height = GetViewPaperHeight(referenceView);
            return height > TOL
                ? Math.Max(DEFAULT_SECTION_GAP, height * 0.65)
                : DEFAULT_SECTION_GAP;
        }

        private static double GetViewPaperHeight(DrawingView view)
        {
            try
            {
                if (view != null && IsFinite(view.Height) && view.Height > TOL)
                    return view.Height;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetViewScale(DrawingView view)
        {
            try
            {
                if (view != null && view.Attributes != null)
                    return view.Attributes.Scale;
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetFlangeThicknessFromProfile(ModelPart part)
        {
            try
            {
                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);
                if (String.IsNullOrEmpty(profile))
                    return 0.0;

                string normalized = profile.ToUpperInvariant()
                    .Replace("BH", "")
                    .Replace("H", "")
                    .Replace("I", "")
                    .Replace("PL", "")
                    .Replace(" ", "")
                    .Replace(",", ".");

                string[] tokens = normalized.Split(
                    new char[] { '*', 'X', 'x', '-' },
                    StringSplitOptions.RemoveEmptyEntries);

                List<double> values = new List<double>();
                foreach (string token in tokens)
                {
                    double value;
                    if (Double.TryParse(
                        token,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out value) && value > 0.0)
                        values.Add(value);
                }

                return values.Count >= 4 ? values[values.Count - 1] : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetFlangeThickness(ModelPart part)
        {
            try
            {
                if (part == null)
                    return 0.0;

                string[] reportProperties = new string[]
                {
                    "PROFILE.FLANGE_THICKNESS",
                    "PROFILE.FLANGE_THICKNESS_1",
                    "PROFILE.TF",
                    "PROFILE_TF",
                    "FLANGE_THICKNESS",
                    "TF"
                };

                foreach (string propertyName in reportProperties)
                {
                    double value = 0.0;
                    try
                    {
                        if (part.GetReportProperty(propertyName, ref value) &&
                            IsFinite(value) &&
                            value > 0.0)
                            return value;
                    }
                    catch
                    {
                    }
                }

                return GetFlangeThicknessFromProfile(part);
            }
            catch
            {
                return 0.0;
            }
        }

        private static Point ClonePoint(Point point)
        {
            return point == null
                ? null
                : new Point(point.X, point.Y, point.Z);
        }

        private static bool IsFinitePoint(Point point)
        {
            return point != null &&
                   IsFinite(point.X) &&
                   IsFinite(point.Y) &&
                   IsFinite(point.Z);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
