#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const double SECTION_NOTCH_MIN_SIZE = 15.0;
        private const double SECTION_NOTCH_MAX_SIZE = 250.0;
        private const double SECTION_NOTCH_POINT_MERGE_TOL = 0.5;
        private const int SECTION_NOTCH_MAX_GEOMETRY_ITEMS = 20000;
        private const double DEFAULT_SECTION_GAP = 55.0;
        private const bool SECTION_LINE_LEFT_TO_RIGHT = false;
        private const string SECTION_MARK_ATTRIBUTE_NAME = "GEO_SECTION";

        public static bool EnableGeometryDiagnostics { get; set; }
        public static string LastGeometryDiagnostic { get; private set; } = "";

        private enum AutoSectionProfileKind
        {
            Unsupported,
            ShapeIH,
            ShapeCBracket,
            ShapeCOrdinary
        }

        private enum COpeningSide
        {
            Unknown,
            Left,
            Right
        }

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

        private sealed class CFlangeGeometry
        {
            public double OuterTopY;
            public double OuterBottomY;
            public double InnerTopY;
            public double InnerBottomY;
            public COpeningSide OpeningSide = COpeningSide.Unknown;
        }

        private struct ProjectedInterval
        {
            public double Min;
            public double Max;
        }

        private enum FrontNotchDetectionStatus
        {
            NotChecked,
            NoNotch,
            Found,
            Failed
        }

        private sealed class FrontNotchGeometry
        {
            public FrontNotchDetectionStatus Status =
                FrontNotchDetectionStatus.NotChecked;

            public bool HasTopLeft;
            public bool HasTopRight;
            public bool HasBottomLeft;
            public bool HasBottomRight;

            public Point TopLeftOuter;
            public Point TopLeftInner;
            public Point TopRightOuter;
            public Point TopRightInner;
            public Point BottomLeftOuter;
            public Point BottomLeftInner;
            public Point BottomRightOuter;
            public Point BottomRightInner;

            public bool HasAnyTopNotch;
            public bool HasAnyBottomNotch;
            public double LowestTopNotchY;
            public double HighestBottomNotchY;
        }

        private struct ProjectedFrontSegment
        {
            public Point Start;
            public Point End;
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
            ResetGeometryDiagnostic();

            string profileText;
            string normalizedProfile;
            AutoSectionProfileKind profileKind;

            if (!TryClassifyAutoSectionProfile(
                part,
                out profileText,
                out normalizedProfile,
                out profileKind,
                out message))
            {
                AddGeometryDiagnostic("Profile classification failed: " + message);
                return false;
            }

            AddGeometryDiagnostic("Profile=" + profileText);
            AddGeometryDiagnostic("NormalizedProfile=" + normalizedProfile);
            AddGeometryDiagnostic("ProfileKind=" + profileKind);

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

                AddGeometryDiagnostic(
                    "FrontExtents=" +
                    FormatDiagnosticNumber(minX) + "," +
                    FormatDiagnosticNumber(minY) + " -> " +
                    FormatDiagnosticNumber(maxX) + "," +
                    FormatDiagnosticNumber(maxY));

                double legacyFlangeThickness = 0.0;
                if (profileKind != AutoSectionProfileKind.ShapeCOrdinary)
                {
                    // Preserve the established H/I and "[" read/check order.
                    legacyFlangeThickness = GetFlangeThickness(part);
                    if (legacyFlangeThickness <= 0.0)
                    {
                        message =
                            profileKind == AutoSectionProfileKind.ShapeCBracket
                                ? "Khong doc duoc do day canh cua profile Shape [."
                                : "Khong doc duoc do day canh cua profile I/H.";
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    AddGeometryDiagnostic(
                        "LegacyFlangeThickness=" +
                        FormatDiagnosticNumber(legacyFlangeThickness));
                }

                FrontNotchGeometry notchGeometry;
                FrontNotchDetectionStatus notchStatus =
                    TryGetFrontNotchGeometry(
                        solid,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        out notchGeometry);

                AddGeometryDiagnostic("NotchStatus=" + notchStatus);

                double bCutY;
                double cCutY;
                double bDepth;
                double cDepth;

                if (profileKind == AutoSectionProfileKind.ShapeCOrdinary)
                {
                    if (notchStatus == FrontNotchDetectionStatus.Failed ||
                        notchStatus == FrontNotchDetectionStatus.NotChecked)
                    {
                        message =
                            "Khong validate duoc notch cua Shape C thong thuong; " +
                            "dung o Preflight.";
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    List<Point> projectedPoints;
                    List<ProjectedFrontSegment> projectedSegments;
                    if (!TryCollectProjectedFrontSolidGeometry(
                        solid,
                        out projectedPoints,
                        out projectedSegments))
                    {
                        message =
                            "Khong doc duoc canh Solid that cua Shape C trong FrontView.";
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    AddGeometryDiagnostic(
                        "ProjectedGeometry points=" +
                        projectedPoints.Count +
                        " segments=" +
                        projectedSegments.Count);

                    CFlangeGeometry flangeGeometry;
                    if (!TryResolveOrdinaryCFlangeGeometry(
                        projectedSegments,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        out flangeGeometry,
                        out message))
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    AddGeometryDiagnostic(
                        "COpeningSide=" + flangeGeometry.OpeningSide);
                    AddGeometryDiagnostic(
                        "CFlangeEdges outerTop=" +
                        FormatDiagnosticNumber(flangeGeometry.OuterTopY) +
                        " innerTop=" +
                        FormatDiagnosticNumber(flangeGeometry.InnerTopY) +
                        " innerBottom=" +
                        FormatDiagnosticNumber(flangeGeometry.InnerBottomY) +
                        " outerBottom=" +
                        FormatDiagnosticNumber(flangeGeometry.OuterBottomY));

                    if (!TryResolveOrdinaryCSectionDepthFromNotches(
                        minY,
                        maxY,
                        flangeGeometry,
                        notchStatus,
                        notchGeometry,
                        out bCutY,
                        out bDepth,
                        out cCutY,
                        out cDepth,
                        out message))
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }
                }
                else
                {
                    if (!TryResolveSectionDepthFromNotches(
                        minY,
                        maxY,
                        legacyFlangeThickness,
                        notchStatus,
                        notchGeometry,
                        out bCutY,
                        out bDepth,
                        out cCutY,
                        out cDepth,
                        out message))
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }
                }

                AddGeometryDiagnostic(
                    "FinalSection B(cutY=" +
                    FormatDiagnosticNumber(bCutY) +
                    ",depth=" +
                    FormatDiagnosticNumber(bDepth) +
                    ") C(cutY=" +
                    FormatDiagnosticNumber(cCutY) +
                    ",depth=" +
                    FormatDiagnosticNumber(cDepth) +
                    ")");

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
                AddGeometryDiagnostic(message);
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

        private static void ResetGeometryDiagnostic()
        {
            LastGeometryDiagnostic = "";
        }

        private static void AddGeometryDiagnostic(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return;

            if (String.IsNullOrEmpty(LastGeometryDiagnostic))
                LastGeometryDiagnostic = text;
            else
                LastGeometryDiagnostic += Environment.NewLine + text;

            if (EnableGeometryDiagnostics)
                Trace.WriteLine("[TTSK AutoSection] " + text);
        }

        private static string FormatDiagnosticNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static bool TryClassifyAutoSectionProfile(
            ModelPart part,
            out string profileText,
            out string normalizedProfile,
            out AutoSectionProfileKind kind,
            out string message)
        {
            profileText = "";
            normalizedProfile = "";
            kind = AutoSectionProfileKind.Unsupported;
            message = "";

            if (part == null)
            {
                message = "Khong co ModelPart de nhan dien profile Auto Section.";
                return false;
            }

            profileText = GetAutoSectionProfileText(part);
            normalizedProfile = NormalizeAutoSectionProfileText(profileText);

            if (String.IsNullOrEmpty(normalizedProfile))
            {
                message =
                    "Khong doc duoc PROFILE hoac Profile.ProfileString de Auto Section.";
                return false;
            }

            if (normalizedProfile.StartsWith("BH") ||
                normalizedProfile.StartsWith("RH") ||
                normalizedProfile.StartsWith("HM") ||
                normalizedProfile.StartsWith("HN") ||
                normalizedProfile.StartsWith("HW") ||
                normalizedProfile.StartsWith("H") ||
                normalizedProfile.StartsWith("I"))
            {
                kind = AutoSectionProfileKind.ShapeIH;
                return true;
            }

            if (normalizedProfile.StartsWith("["))
            {
                kind = AutoSectionProfileKind.ShapeCBracket;
                return true;
            }

            if (normalizedProfile.StartsWith("CH") ||
                normalizedProfile.StartsWith("CHANNEL") ||
                normalizedProfile.StartsWith("C"))
            {
                kind = AutoSectionProfileKind.ShapeCOrdinary;
                return true;
            }

            message =
                "Profile khong thuoc I/H, Shape [ hoac Shape C duoc Auto Section ho tro: " +
                profileText;
            return false;
        }

        private static string GetAutoSectionProfileText(ModelPart part)
        {
            try
            {
                string profile = "";
                if (part != null &&
                    part.GetReportProperty("PROFILE", ref profile) &&
                    !String.IsNullOrWhiteSpace(profile))
                    return profile.Trim();
            }
            catch
            {
            }

            try
            {
                if (part == null)
                    return "";

                PropertyInfo profileProperty = part.GetType().GetProperty(
                    "Profile",
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance);

                object profileObject = profileProperty != null &&
                    profileProperty.CanRead
                        ? profileProperty.GetValue(part, null)
                        : null;

                if (profileObject == null)
                    return "";

                PropertyInfo profileStringProperty =
                    profileObject.GetType().GetProperty(
                        "ProfileString",
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);

                object value = profileStringProperty != null &&
                    profileStringProperty.CanRead
                        ? profileStringProperty.GetValue(profileObject, null)
                        : null;

                return value != null ? value.ToString().Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string NormalizeAutoSectionProfileText(string profile)
        {
            if (profile == null)
                return "";

            string normalized = profile.Trim().ToUpperInvariant();
            normalized = normalized.Replace(" ", "");
            normalized = normalized.Replace("-", "");
            normalized = normalized.Replace("_", "");
            normalized = normalized.Replace("*", "X");
            return normalized;
        }

        private static bool TryResolveOrdinaryCFlangeGeometry(
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out CFlangeGeometry geometry,
            out string message)
        {
            geometry = null;
            message = "";

            try
            {
                if (segments == null || segments.Count < 4)
                {
                    message =
                        "Shape C khong co du canh Solid that de tim hai mep trong.";
                    return false;
                }

                double width = maxX - minX;
                double height = maxY - minY;
                double edgeTol = Math.Max(2.0, TOL + 1.0);

                if (!IsFinite(width) || !IsFinite(height) ||
                    width <= edgeTol || height <= edgeTol)
                {
                    message =
                        "Extents Shape C khong hop le de phan tich mep canh.";
                    return false;
                }

                List<double> horizontalLevels = new List<double>();

                foreach (ProjectedFrontSegment segment in segments)
                {
                    if (segment.Start == null || segment.End == null)
                        continue;

                    double dx = Math.Abs(segment.End.X - segment.Start.X);
                    double dy = Math.Abs(segment.End.Y - segment.Start.Y);

                    if (dx <= edgeTol || dy > edgeTol)
                        continue;

                    double y = (segment.Start.Y + segment.End.Y) * 0.5;
                    if (y <= minY + edgeTol || y >= maxY - edgeTol)
                        continue;

                    AddUniqueCoordinate(horizontalLevels, y, edgeTol);
                }

                double innerTopY;
                double innerBottomY;
                double topCoverage;
                double bottomCoverage;

                if (!TrySelectCFlangeInnerLevel(
                    horizontalLevels,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    true,
                    edgeTol,
                    out innerTopY,
                    out topCoverage))
                {
                    message =
                        "Khong tim duoc mep trong phia duoi cua canh tren Shape C.";
                    return false;
                }

                if (!TrySelectCFlangeInnerLevel(
                    horizontalLevels,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    false,
                    edgeTol,
                    out innerBottomY,
                    out bottomCoverage))
                {
                    message =
                        "Khong tim duoc mep trong phia tren cua canh duoi Shape C.";
                    return false;
                }

                double topFlangeDepth = maxY - innerTopY;
                double bottomFlangeDepth = innerBottomY - minY;

                if (!IsFinite(topFlangeDepth) ||
                    !IsFinite(bottomFlangeDepth) ||
                    topFlangeDepth <= TOL ||
                    bottomFlangeDepth <= TOL ||
                    topFlangeDepth >= height * 0.45 ||
                    bottomFlangeDepth >= height * 0.45 ||
                    innerTopY <= innerBottomY + edgeTol)
                {
                    message =
                        "Hai mep trong Shape C khong tao thanh hai canh tren/duoi hop le.";
                    return false;
                }

                double minimumCoverage = Math.Max(5.0, width * 0.15);
                if (topCoverage < minimumCoverage ||
                    bottomCoverage < minimumCoverage)
                {
                    message =
                        "Do dai canh that tai hai mep trong Shape C khong du de validate.";
                    return false;
                }

                geometry = new CFlangeGeometry();
                geometry.OuterTopY = maxY;
                geometry.OuterBottomY = minY;
                geometry.InnerTopY = innerTopY;
                geometry.InnerBottomY = innerBottomY;

                COpeningSide openingSide;
                if (!TryResolveOrdinaryCOpeningTopology(
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    edgeTol,
                    out openingSide,
                    out message))
                {
                    geometry = null;
                    return false;
                }

                geometry.OpeningSide = openingSide;

                AddGeometryDiagnostic(
                    "CFlangeCoverage top=" +
                    FormatDiagnosticNumber(topCoverage) +
                    " bottom=" +
                    FormatDiagnosticNumber(bottomCoverage));

                return true;
            }
            catch (Exception ex)
            {
                message = "Phan tich mep canh Shape C loi: " + ex.Message;
                return false;
            }
        }

        private static void AddUniqueCoordinate(
            List<double> values,
            double value,
            double tolerance)
        {
            if (values == null || !IsFinite(value))
                return;

            for (int i = 0; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - value) <= tolerance)
                {
                    values[i] = (values[i] + value) * 0.5;
                    return;
                }
            }

            values.Add(value);
        }

        private static bool TrySelectCFlangeInnerLevel(
            List<double> levels,
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool topSide,
            double tolerance,
            out double selectedY,
            out double selectedCoverage)
        {
            selectedY = 0.0;
            selectedCoverage = 0.0;

            if (levels == null || levels.Count == 0)
                return false;

            double middleY = (minY + maxY) * 0.5;
            double bestCoverage = 0.0;

            foreach (double y in levels)
            {
                if ((topSide && y <= middleY + tolerance) ||
                    (!topSide && y >= middleY - tolerance))
                    continue;

                double coverage = GetHorizontalCoverageAtY(
                    segments,
                    y,
                    minX,
                    maxX,
                    tolerance);

                if (coverage > bestCoverage)
                    bestCoverage = coverage;
            }

            if (bestCoverage <= TOL)
                return false;

            double minimumCoverage = Math.Max(
                Math.Max(5.0, (maxX - minX) * 0.15),
                bestCoverage * 0.70);
            bool found = false;

            foreach (double y in levels)
            {
                if ((topSide && y <= middleY + tolerance) ||
                    (!topSide && y >= middleY - tolerance))
                    continue;

                double coverage = GetHorizontalCoverageAtY(
                    segments,
                    y,
                    minX,
                    maxX,
                    tolerance);

                if (coverage < minimumCoverage)
                    continue;

                if (!found ||
                    (topSide && y > selectedY) ||
                    (!topSide && y < selectedY))
                {
                    selectedY = y;
                    selectedCoverage = coverage;
                    found = true;
                }
            }

            return found;
        }

        private static double GetHorizontalCoverageAtY(
            List<ProjectedFrontSegment> segments,
            double targetY,
            double minX,
            double maxX,
            double tolerance)
        {
            List<ProjectedInterval> intervals = new List<ProjectedInterval>();

            if (segments == null)
                return 0.0;

            foreach (ProjectedFrontSegment segment in segments)
            {
                if (segment.Start == null || segment.End == null)
                    continue;

                double dx = Math.Abs(segment.End.X - segment.Start.X);
                double dy = Math.Abs(segment.End.Y - segment.Start.Y);
                double y = (segment.Start.Y + segment.End.Y) * 0.5;

                if (dx <= tolerance ||
                    dy > tolerance ||
                    Math.Abs(y - targetY) > tolerance)
                    continue;

                ProjectedInterval interval = new ProjectedInterval();
                interval.Min = Math.Max(
                    minX,
                    Math.Min(segment.Start.X, segment.End.X));
                interval.Max = Math.Min(
                    maxX,
                    Math.Max(segment.Start.X, segment.End.X));

                if (interval.Max - interval.Min > TOL)
                    intervals.Add(interval);
            }

            return GetMergedIntervalCoverage(intervals, tolerance);
        }

        private static double GetVerticalCoverageAtX(
            List<ProjectedFrontSegment> segments,
            double targetX,
            double minY,
            double maxY,
            double tolerance)
        {
            List<ProjectedInterval> intervals = new List<ProjectedInterval>();

            if (segments == null)
                return 0.0;

            foreach (ProjectedFrontSegment segment in segments)
            {
                if (segment.Start == null || segment.End == null)
                    continue;

                double dx = Math.Abs(segment.End.X - segment.Start.X);
                double dy = Math.Abs(segment.End.Y - segment.Start.Y);
                double x = (segment.Start.X + segment.End.X) * 0.5;

                if (dy <= tolerance ||
                    dx > tolerance ||
                    Math.Abs(x - targetX) > tolerance)
                    continue;

                ProjectedInterval interval = new ProjectedInterval();
                interval.Min = Math.Max(
                    minY,
                    Math.Min(segment.Start.Y, segment.End.Y));
                interval.Max = Math.Min(
                    maxY,
                    Math.Max(segment.Start.Y, segment.End.Y));

                if (interval.Max - interval.Min > TOL)
                    intervals.Add(interval);
            }

            return GetMergedIntervalCoverage(intervals, tolerance);
        }

        private static double GetMergedIntervalCoverage(
            List<ProjectedInterval> intervals,
            double tolerance)
        {
            if (intervals == null || intervals.Count == 0)
                return 0.0;

            intervals.Sort(delegate (
                ProjectedInterval first,
                ProjectedInterval second)
            {
                return first.Min.CompareTo(second.Min);
            });

            double coverage = 0.0;
            double currentMin = intervals[0].Min;
            double currentMax = intervals[0].Max;

            for (int i = 1; i < intervals.Count; i++)
            {
                ProjectedInterval interval = intervals[i];
                if (interval.Min <= currentMax + tolerance)
                {
                    currentMax = Math.Max(currentMax, interval.Max);
                }
                else
                {
                    coverage += Math.Max(0.0, currentMax - currentMin);
                    currentMin = interval.Min;
                    currentMax = interval.Max;
                }
            }

            coverage += Math.Max(0.0, currentMax - currentMin);
            return coverage;
        }

        private static bool TryResolveOrdinaryCOpeningTopology(
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            double edgeTolerance,
            out COpeningSide openingSide,
            out string message)
        {
            openingSide = COpeningSide.Unknown;
            message = "";

            double sideTolerance = Math.Max(
                edgeTolerance,
                (maxX - minX) * 0.03);
            double leftCoverage = GetVerticalCoverageAtX(
                segments,
                minX,
                minY,
                maxY,
                sideTolerance);
            double rightCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                minY,
                maxY,
                sideTolerance);
            double minimumDifference = Math.Max(
                2.0,
                (maxY - minY) * 0.05);

            AddGeometryDiagnostic(
                "CVerticalCoverage left=" +
                FormatDiagnosticNumber(leftCoverage) +
                " right=" +
                FormatDiagnosticNumber(rightCoverage));

            if (leftCoverage + minimumDifference < rightCoverage)
                openingSide = COpeningSide.Left;
            else if (rightCoverage + minimumDifference < leftCoverage)
                openingSide = COpeningSide.Right;
            else
                openingSide = COpeningSide.Unknown;

            double partHeight = maxY - minY;
            double middleY = (minY + maxY) * 0.5;
            double leftLowerCoverage = GetVerticalCoverageAtX(
                segments,
                minX,
                minY,
                middleY,
                sideTolerance);
            double leftUpperCoverage = GetVerticalCoverageAtX(
                segments,
                minX,
                middleY,
                maxY,
                sideTolerance);
            double rightLowerCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                minY,
                middleY,
                sideTolerance);
            double rightUpperCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                middleY,
                maxY,
                sideTolerance);

            AddGeometryDiagnostic(
                "CSplitCoverage left(lower=" +
                FormatDiagnosticNumber(leftLowerCoverage) +
                ",upper=" +
                FormatDiagnosticNumber(leftUpperCoverage) +
                ") right(lower=" +
                FormatDiagnosticNumber(rightLowerCoverage) +
                ",upper=" +
                FormatDiagnosticNumber(rightUpperCoverage) +
                ")");

            if (Math.Max(leftCoverage, rightCoverage) >= partHeight * 0.75)
            {
                message =
                    "Hinh hoc C co canh dung lien tuc gan het chieu cao; " +
                    "khong du chac chan de xu ly nhu C thong thuong thay vi Shape [.";
                return false;
            }

            double minimumHalfCoverage = Math.Max(2.0, partHeight * 0.01);
            bool hasLeftSplit =
                leftLowerCoverage >= minimumHalfCoverage &&
                leftUpperCoverage >= minimumHalfCoverage;
            bool hasRightSplit =
                rightLowerCoverage >= minimumHalfCoverage &&
                rightUpperCoverage >= minimumHalfCoverage;

            if (!hasLeftSplit && !hasRightSplit)
            {
                message =
                    "Hinh hoc C khong co du hai doan canh dung tren/duoi " +
                    "de xac nhan C thong thuong.";
                return false;
            }

            return true;
        }

        private static FrontNotchDetectionStatus TryGetFrontNotchGeometry(
            Solid solid,
            double minX,
            double maxX,
            double minY,
            double maxY,
            out FrontNotchGeometry geometry)
        {
            geometry = new FrontNotchGeometry();

            try
            {
                List<Point> points;
                List<ProjectedFrontSegment> segments;

                if (!TryCollectProjectedFrontSolidGeometry(
                    solid,
                    out points,
                    out segments))
                {
                    geometry.Status = FrontNotchDetectionStatus.Failed;
                    return geometry.Status;
                }

                geometry.Status = TryDetectFrontCornerNotches(
                    points,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    geometry);

                return geometry.Status;
            }
            catch
            {
                geometry.Status = FrontNotchDetectionStatus.Failed;
                return geometry.Status;
            }
        }

        private static bool TryCollectProjectedFrontSolidGeometry(
            Solid solid,
            out List<Point> points,
            out List<ProjectedFrontSegment> segments)
        {
            points = new List<Point>();
            segments = new List<ProjectedFrontSegment>();

            if (solid == null)
                return false;

            int geometryItemCount = 0;

            try
            {
                Tekla.Structures.Solid.FaceEnumerator faces =
                    solid.GetFaceEnumerator();

                while (faces != null && faces.MoveNext())
                {
                    geometryItemCount++;
                    if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                        return false;

                    Tekla.Structures.Solid.Face face = faces.Current;
                    if (face == null)
                        continue;

                    Tekla.Structures.Solid.LoopEnumerator loops =
                        face.GetLoopEnumerator();

                    while (loops != null && loops.MoveNext())
                    {
                        geometryItemCount++;
                        if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                            return false;

                        Tekla.Structures.Solid.Loop loop = loops.Current;
                        if (loop == null)
                            continue;

                        Tekla.Structures.Solid.VertexEnumerator vertices =
                            loop.GetVertexEnumerator();

                        while (vertices != null && vertices.MoveNext())
                        {
                            geometryItemCount++;
                            if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                                return false;

                            if (!TryAddUniqueProjectedPoint(
                                points,
                                vertices.Current))
                                return false;
                        }
                    }
                }
            }
            catch
            {
                points.Clear();
            }

            try
            {
                Tekla.Structures.Solid.EdgeEnumerator edges =
                    solid.GetEdgeEnumerator();

                while (edges != null && edges.MoveNext())
                {
                    geometryItemCount++;
                    if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                        return false;

                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;

                    if (edge == null ||
                        edge.StartPoint == null ||
                        edge.EndPoint == null)
                        continue;

                    Point start = new Point(
                        edge.StartPoint.X,
                        edge.StartPoint.Y,
                        0.0);
                    Point end = new Point(
                        edge.EndPoint.X,
                        edge.EndPoint.Y,
                        0.0);

                    if (!TryAddUniqueProjectedPoint(points, start) ||
                        !TryAddUniqueProjectedPoint(points, end) ||
                        !TryAddUniqueProjectedSegment(segments, start, end))
                        return false;
                }
            }
            catch
            {
                return false;
            }

            return points.Count >= 4 && segments.Count >= 4;
        }

        private static bool TryAddUniqueProjectedPoint(
            List<Point> points,
            Point point)
        {
            if (points == null)
                return false;

            if (point == null ||
                !IsFinite(point.X) ||
                !IsFinite(point.Y))
                return true;

            foreach (Point current in points)
            {
                if (AreProjectedPointsNear(
                    current,
                    point,
                    SECTION_NOTCH_POINT_MERGE_TOL))
                    return true;
            }

            if (points.Count >= SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                return false;

            points.Add(new Point(point.X, point.Y, 0.0));
            return true;
        }

        private static bool TryAddUniqueProjectedSegment(
            List<ProjectedFrontSegment> segments,
            Point start,
            Point end)
        {
            if (segments == null || start == null || end == null)
                return false;

            if (!IsFinite(start.X) || !IsFinite(start.Y) ||
                !IsFinite(end.X) || !IsFinite(end.Y))
                return true;

            if (AreProjectedPointsNear(
                start,
                end,
                SECTION_NOTCH_POINT_MERGE_TOL))
                return true;

            foreach (ProjectedFrontSegment current in segments)
            {
                bool sameDirection =
                    AreProjectedPointsNear(
                        current.Start,
                        start,
                        SECTION_NOTCH_POINT_MERGE_TOL) &&
                    AreProjectedPointsNear(
                        current.End,
                        end,
                        SECTION_NOTCH_POINT_MERGE_TOL);

                bool oppositeDirection =
                    AreProjectedPointsNear(
                        current.Start,
                        end,
                        SECTION_NOTCH_POINT_MERGE_TOL) &&
                    AreProjectedPointsNear(
                        current.End,
                        start,
                        SECTION_NOTCH_POINT_MERGE_TOL);

                if (sameDirection || oppositeDirection)
                    return true;
            }

            if (segments.Count >= SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                return false;

            ProjectedFrontSegment segment = new ProjectedFrontSegment();
            segment.Start = new Point(start.X, start.Y, 0.0);
            segment.End = new Point(end.X, end.Y, 0.0);
            segments.Add(segment);
            return true;
        }

        private static bool AreProjectedPointsNear(
            Point first,
            Point second,
            double tolerance)
        {
            return first != null &&
                   second != null &&
                   Math.Abs(first.X - second.X) <= tolerance &&
                   Math.Abs(first.Y - second.Y) <= tolerance;
        }

        private static FrontNotchDetectionStatus TryDetectFrontCornerNotches(
            List<Point> points,
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            FrontNotchGeometry geometry)
        {
            try
            {
                if (geometry == null ||
                    points == null ||
                    segments == null ||
                    points.Count < 4 ||
                    segments.Count < 4)
                    return FrontNotchDetectionStatus.Failed;

                geometry.HasTopLeft = TryDetectOneFrontCornerNotch(
                    points,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    true,
                    true,
                    out geometry.TopLeftOuter,
                    out geometry.TopLeftInner);

                geometry.HasTopRight = TryDetectOneFrontCornerNotch(
                    points,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    false,
                    true,
                    out geometry.TopRightOuter,
                    out geometry.TopRightInner);

                geometry.HasBottomLeft = TryDetectOneFrontCornerNotch(
                    points,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    true,
                    false,
                    out geometry.BottomLeftOuter,
                    out geometry.BottomLeftInner);

                geometry.HasBottomRight = TryDetectOneFrontCornerNotch(
                    points,
                    segments,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    false,
                    false,
                    out geometry.BottomRightOuter,
                    out geometry.BottomRightInner);

                geometry.HasAnyTopNotch =
                    geometry.HasTopLeft || geometry.HasTopRight;
                geometry.HasAnyBottomNotch =
                    geometry.HasBottomLeft || geometry.HasBottomRight;

                geometry.LowestTopNotchY = double.MaxValue;
                if (geometry.HasTopLeft)
                {
                    geometry.LowestTopNotchY = Math.Min(
                        geometry.LowestTopNotchY,
                        geometry.TopLeftOuter.Y);
                }
                if (geometry.HasTopRight)
                {
                    geometry.LowestTopNotchY = Math.Min(
                        geometry.LowestTopNotchY,
                        geometry.TopRightOuter.Y);
                }

                geometry.HighestBottomNotchY = double.MinValue;
                if (geometry.HasBottomLeft)
                {
                    geometry.HighestBottomNotchY = Math.Max(
                        geometry.HighestBottomNotchY,
                        geometry.BottomLeftOuter.Y);
                }
                if (geometry.HasBottomRight)
                {
                    geometry.HighestBottomNotchY = Math.Max(
                        geometry.HighestBottomNotchY,
                        geometry.BottomRightOuter.Y);
                }

                return geometry.HasAnyTopNotch ||
                       geometry.HasAnyBottomNotch
                    ? FrontNotchDetectionStatus.Found
                    : FrontNotchDetectionStatus.NoNotch;
            }
            catch
            {
                return FrontNotchDetectionStatus.Failed;
            }
        }

        private static bool TryDetectOneFrontCornerNotch(
            List<Point> points,
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            bool leftSide,
            bool topSide,
            out Point outer,
            out Point inner)
        {
            outer = null;
            inner = null;

            double edgeTol = Math.Max(2.0, TOL + 1.0);

            foreach (Point point in points)
            {
                if (point == null)
                    continue;

                bool onSideEdge = leftSide
                    ? Math.Abs(point.X - minX) <= edgeTol
                    : Math.Abs(point.X - maxX) <= edgeTol;

                bool inVerticalCornerBand = topSide
                    ? point.Y < maxY - edgeTol &&
                      point.Y >= maxY - SECTION_NOTCH_MAX_SIZE
                    : point.Y > minY + edgeTol &&
                      point.Y <= minY + SECTION_NOTCH_MAX_SIZE;

                if (onSideEdge && inVerticalCornerBand)
                {
                    if (outer == null ||
                        (topSide && point.Y > outer.Y) ||
                        (!topSide && point.Y < outer.Y))
                    {
                        outer = new Point(point.X, point.Y, 0.0);
                    }
                }

                bool onHorizontalEdge = topSide
                    ? Math.Abs(point.Y - maxY) <= edgeTol
                    : Math.Abs(point.Y - minY) <= edgeTol;

                bool inHorizontalCornerBand = leftSide
                    ? point.X > minX + edgeTol &&
                      point.X <= minX + SECTION_NOTCH_MAX_SIZE
                    : point.X < maxX - edgeTol &&
                      point.X >= maxX - SECTION_NOTCH_MAX_SIZE;

                if (onHorizontalEdge && inHorizontalCornerBand)
                {
                    if (inner == null ||
                        (leftSide && point.X > inner.X) ||
                        (!leftSide && point.X < inner.X))
                    {
                        inner = new Point(point.X, point.Y, 0.0);
                    }
                }
            }

            if (outer == null || inner == null)
                return false;

            if ((leftSide && inner.X >= maxX - edgeTol) ||
                (!leftSide && inner.X <= minX + edgeTol))
                return false;

            double width = leftSide
                ? Math.Abs(inner.X - minX)
                : Math.Abs(maxX - inner.X);
            double depth = topSide
                ? Math.Abs(maxY - outer.Y)
                : Math.Abs(outer.Y - minY);

            if (width < SECTION_NOTCH_MIN_SIZE ||
                depth < SECTION_NOTCH_MIN_SIZE ||
                width > SECTION_NOTCH_MAX_SIZE ||
                depth > SECTION_NOTCH_MAX_SIZE)
                return false;

            if (!HasAxisAlignedNotchEvidence(
                segments,
                outer,
                inner,
                edgeTol))
                return false;

            return true;
        }

        private static bool HasAxisAlignedNotchEvidence(
            List<ProjectedFrontSegment> segments,
            Point outer,
            Point inner,
            double edgeTol)
        {
            bool hasHorizontalLeg = false;
            bool hasVerticalLeg = false;
            bool hasDirectDiagonal = false;

            double minCornerX = Math.Min(outer.X, inner.X) - edgeTol;
            double maxCornerX = Math.Max(outer.X, inner.X) + edgeTol;
            double minCornerY = Math.Min(outer.Y, inner.Y) - edgeTol;
            double maxCornerY = Math.Max(outer.Y, inner.Y) + edgeTol;

            foreach (ProjectedFrontSegment segment in segments)
            {
                Point start = segment.Start;
                Point end = segment.End;
                if (start == null || end == null)
                    continue;

                double dx = Math.Abs(end.X - start.X);
                double dy = Math.Abs(end.Y - start.Y);

                bool joinsOuterAndInner =
                    (AreProjectedPointsNear(start, outer, edgeTol) &&
                     AreProjectedPointsNear(end, inner, edgeTol)) ||
                    (AreProjectedPointsNear(start, inner, edgeTol) &&
                     AreProjectedPointsNear(end, outer, edgeTol));

                if (joinsOuterAndInner && dx > edgeTol && dy > edgeTol)
                    hasDirectDiagonal = true;

                double segmentMinX = Math.Min(start.X, end.X);
                double segmentMaxX = Math.Max(start.X, end.X);
                double segmentMinY = Math.Min(start.Y, end.Y);
                double segmentMaxY = Math.Max(start.Y, end.Y);

                if (dx > SECTION_NOTCH_POINT_MERGE_TOL &&
                    dy <= edgeTol &&
                    Math.Abs((start.Y + end.Y) * 0.5 - outer.Y) <= edgeTol &&
                    segmentMaxX >= minCornerX &&
                    segmentMinX <= maxCornerX)
                {
                    hasHorizontalLeg = true;
                }

                if (dy > SECTION_NOTCH_POINT_MERGE_TOL &&
                    dx <= edgeTol &&
                    Math.Abs((start.X + end.X) * 0.5 - inner.X) <= edgeTol &&
                    segmentMaxY >= minCornerY &&
                    segmentMinY <= maxCornerY)
                {
                    hasVerticalLeg = true;
                }
            }

            return hasHorizontalLeg &&
                   hasVerticalLeg &&
                   !hasDirectDiagonal;
        }

        private static bool TryResolveSectionDepthFromNotches(
            double minY,
            double maxY,
            double flangeThickness,
            FrontNotchDetectionStatus notchStatus,
            FrontNotchGeometry notchGeometry,
            out double bCutY,
            out double bDepth,
            out double cCutY,
            out double cDepth,
            out string message)
        {
            message = "";
            bCutY = maxY;
            bDepth = flangeThickness + SECTION_B_EXTRA_DEPTH;
            cCutY = minY + flangeThickness + SECTION_C_EXTRA_START;
            cDepth = cCutY - minY;

            bool hasTopNotch =
                notchStatus == FrontNotchDetectionStatus.Found &&
                notchGeometry != null &&
                notchGeometry.HasAnyTopNotch;
            bool hasBottomNotch =
                notchStatus == FrontNotchDetectionStatus.Found &&
                notchGeometry != null &&
                notchGeometry.HasAnyBottomNotch;

            if (hasTopNotch)
            {
                bDepth =
                    maxY -
                    notchGeometry.LowestTopNotchY +
                    SECTION_B_EXTRA_DEPTH;
            }

            if (hasBottomNotch)
            {
                cCutY =
                    notchGeometry.HighestBottomNotchY +
                    SECTION_C_EXTRA_START;
                cDepth = cCutY - minY;
            }

            return ValidateResolvedSectionGeometry(
                minY,
                maxY,
                bCutY,
                bDepth,
                cCutY,
                cDepth,
                hasTopNotch,
                hasBottomNotch,
                notchGeometry,
                out message);
        }

        private static bool TryResolveOrdinaryCSectionDepthFromNotches(
            double minY,
            double maxY,
            CFlangeGeometry flangeGeometry,
            FrontNotchDetectionStatus notchStatus,
            FrontNotchGeometry notchGeometry,
            out double bCutY,
            out double bDepth,
            out double cCutY,
            out double cDepth,
            out string message)
        {
            message = "";
            bCutY = 0.0;
            bDepth = 0.0;
            cCutY = 0.0;
            cDepth = 0.0;

            if (flangeGeometry == null)
            {
                message = "Thieu geometry hai mep canh Shape C.";
                return false;
            }

            bCutY = flangeGeometry.OuterTopY;
            bDepth =
                flangeGeometry.OuterTopY -
                flangeGeometry.InnerTopY +
                SECTION_B_EXTRA_DEPTH;
            cCutY =
                flangeGeometry.InnerBottomY +
                SECTION_C_EXTRA_START;
            cDepth = cCutY - flangeGeometry.OuterBottomY;

            double baseBDepth = bDepth;
            double baseCCutY = cCutY;
            bool hasTopNotch =
                notchStatus == FrontNotchDetectionStatus.Found &&
                notchGeometry != null &&
                notchGeometry.HasAnyTopNotch;
            bool hasBottomNotch =
                notchStatus == FrontNotchDetectionStatus.Found &&
                notchGeometry != null &&
                notchGeometry.HasAnyBottomNotch;

            if (hasTopNotch)
            {
                double notchBDepth =
                    maxY -
                    notchGeometry.LowestTopNotchY +
                    SECTION_B_EXTRA_DEPTH;
                bDepth = Math.Max(baseBDepth, notchBDepth);
                AddGeometryDiagnostic(
                    "TopLimit flange=" +
                    FormatDiagnosticNumber(baseBDepth) +
                    " notch=" +
                    FormatDiagnosticNumber(notchBDepth));
            }
            else
            {
                AddGeometryDiagnostic(
                    "TopLimit flange=" +
                    FormatDiagnosticNumber(baseBDepth) +
                    " notch=none");
            }

            if (hasBottomNotch)
            {
                double notchCCutY =
                    notchGeometry.HighestBottomNotchY +
                    SECTION_C_EXTRA_START;
                cCutY = Math.Max(baseCCutY, notchCCutY);
                cDepth = cCutY - minY;
                AddGeometryDiagnostic(
                    "BottomLimit flangeCutY=" +
                    FormatDiagnosticNumber(baseCCutY) +
                    " notchCutY=" +
                    FormatDiagnosticNumber(notchCCutY));
            }
            else
            {
                AddGeometryDiagnostic(
                    "BottomLimit flangeCutY=" +
                    FormatDiagnosticNumber(baseCCutY) +
                    " notch=none");
            }

            return ValidateResolvedOrdinaryCSectionGeometry(
                minY,
                maxY,
                bCutY,
                bDepth,
                cCutY,
                cDepth,
                hasTopNotch,
                hasBottomNotch,
                notchGeometry,
                out message);
        }

        private static bool ValidateResolvedOrdinaryCSectionGeometry(
            double minY,
            double maxY,
            double bCutY,
            double bDepth,
            double cCutY,
            double cDepth,
            bool hasTopNotch,
            bool hasBottomNotch,
            FrontNotchGeometry notchGeometry,
            out string message)
        {
            message = "";
            double partHeight = maxY - minY;

            if (!IsFinite(partHeight) || partHeight <= TOL)
            {
                message =
                    "Chieu cao Shape C khong hop le de tinh Section B/C.";
                return false;
            }

            if (!IsFinite(bCutY) || !IsFinite(bDepth) || bDepth <= TOL)
            {
                message =
                    "Section B tinh theo mep canh Shape C khong hop le.";
                return false;
            }

            if (bDepth >= partHeight)
            {
                message =
                    "Section B theo mep canh/notch Shape C vuot chieu cao part.";
                return false;
            }

            if (bCutY > maxY + TOL || bCutY < minY - TOL)
            {
                message = "Section B Shape C nam ngoai chieu cao part.";
                return false;
            }

            if (!IsFinite(cCutY) || !IsFinite(cDepth) || cDepth <= TOL)
            {
                message =
                    "Section C tinh theo mep canh Shape C khong hop le.";
                return false;
            }

            if (cDepth >= partHeight)
            {
                message =
                    "Section C theo mep canh/notch Shape C vuot chieu cao part.";
                return false;
            }

            if (cCutY >= maxY || cCutY <= minY)
            {
                message = "Section C Shape C nam ngoai mien trong cua part.";
                return false;
            }

            if (hasTopNotch)
            {
                double requiredBDepth =
                    maxY -
                    notchGeometry.LowestTopNotchY +
                    SECTION_B_EXTRA_DEPTH;

                if (!IsFinite(requiredBDepth) ||
                    bDepth < requiredBDepth - TOL)
                {
                    message =
                        "Section B Shape C khong bao phu day notch tren.";
                    return false;
                }
            }

            if (hasBottomNotch)
            {
                double requiredCCutY =
                    notchGeometry.HighestBottomNotchY +
                    SECTION_C_EXTRA_START;

                if (!IsFinite(requiredCCutY) ||
                    cCutY < requiredCCutY - TOL)
                {
                    message =
                        "Section C Shape C khong bao phu dinh notch duoi.";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateResolvedSectionGeometry(
            double minY,
            double maxY,
            double bCutY,
            double bDepth,
            double cCutY,
            double cDepth,
            bool hasTopNotch,
            bool hasBottomNotch,
            FrontNotchGeometry notchGeometry,
            out string message)
        {
            message = "";
            double partHeight = maxY - minY;

            if (!IsFinite(partHeight) || partHeight <= TOL)
            {
                message = "Chieu cao part khong hop le de tinh Section B/C.";
                return false;
            }

            if (!IsFinite(bCutY) || !IsFinite(bDepth))
            {
                message = "Section B geometry khong phai gia tri huu han.";
                return false;
            }

            if (bDepth <= TOL)
            {
                message = "Section B depth khong hop le.";
                return false;
            }

            if (bDepth >= partHeight)
            {
                message = hasTopNotch
                    ? "Section B depth tinh theo notch vuot qua chieu cao part."
                    : "Section B depth tinh tu do day canh vuot qua chieu cao part.";
                return false;
            }

            if (bCutY > maxY + TOL || bCutY < minY - TOL)
            {
                message = "Section B cut line nam ngoai chieu cao part.";
                return false;
            }

            if (!IsFinite(cCutY) || !IsFinite(cDepth))
            {
                message = "Section C geometry khong phai gia tri huu han.";
                return false;
            }

            if (cDepth <= TOL)
            {
                message = "Section C depth khong hop le.";
                return false;
            }

            if (cDepth >= partHeight)
            {
                message = hasBottomNotch
                    ? "Section C depth tinh theo notch vuot qua chieu cao part."
                    : "Section C depth tinh tu do day canh vuot qua chieu cao part.";
                return false;
            }

            if (cCutY >= maxY || cCutY <= minY)
            {
                message = "Section C cut line nam ngoai mien trong cua part.";
                return false;
            }

            if (hasTopNotch)
            {
                double requiredBDepth =
                    maxY -
                    notchGeometry.LowestTopNotchY +
                    SECTION_B_EXTRA_DEPTH;

                if (!IsFinite(requiredBDepth) ||
                    bDepth < requiredBDepth - TOL)
                {
                    message = "Section B depth khong bao phu day notch tren.";
                    return false;
                }
            }

            if (hasBottomNotch)
            {
                double requiredCCutY =
                    notchGeometry.HighestBottomNotchY +
                    SECTION_C_EXTRA_START;

                if (!IsFinite(requiredCCutY) ||
                    cCutY < requiredCCutY - TOL)
                {
                    message = "Section C cut line khong bao phu dinh notch duoi.";
                    return false;
                }
            }

            return true;
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
