// Auto Section: tấm Single Part và section B/C cho H/C; tìm thuộc tính view/mark.
// LUỒNG: AutoSectionCoordinator.Run phân loại → preflight theo loại → tạo view/mark → commit và đọc lại; lỗi được trả qua worker result.
// ĐIỂM SỬA TẤM: AnalyzePlateFrontBevelFamily/RunPlateSingleSafe; H/C: TryPreflightSingle/TryPreflightAssembly.
// ĐIỂM SỬA STYLE: SectionViewAttributeResolver.Resolve lấy thuộc tính; SECTION_MARK_ATTRIBUTE_NAME chọn file mark.
// THAM SỐ: *_PAPER_MM và DEFAULT_SECTION_GAP là mm giấy; độ sâu cắt/tolerance là mm hình học; góc *_DEGREES tính bằng độ.
// TRA CỨU: PROJECT_MAP.md ở gốc repo; chỉ mục kiểu/hàm và điểm chỉnh thông số.
// Mục lục:
//   PHU_Section.cs
//   PHU_SectionViewAttributeResolver.cs

#region PHU_Section - Tạo section view và section mark
#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using Tekla.Structures;
    using Tekla.Structures.Drawing;
    using Tekla.Structures.Geometry3d;
    using Tekla.Structures.Model;
    using DrawingPart = Tekla.Structures.Drawing.Part;
    using DrawingView = Tekla.Structures.Drawing.View;
    using ModelPart = Tekla.Structures.Model.Part;

    public enum AutoSectionWorkerStatus
    {
        NoSectionRequired,
        ExistingLayout,
        PartialLayout,
        PrecheckUnknown,
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
        public int OriginalHoleResult = -1;
        public bool RequiresDimensionPass = false;
        public bool HasSingleLayout = false;
        public bool RelocateMarksAfterDimension = false;
        public bool IsPlateSection = false;
        public DrawingView SectionA = null;
        public DrawingView SectionB = null;
        public DrawingView SectionC = null;
        public SectionMark MarkA = null;
        public SectionMark MarkB = null;
        public SectionMark MarkC = null;
        public bool IsSafeToContinue = true;
    }

    public enum AutoSectionTargetKind
    {
        Plate,
        ShapeIH,
        ShapeC
    }

    /// <summary>
    /// Phân tuyến Auto Section: tấm dùng worker bevel riêng, H/C dùng worker B/C.
    /// Hai nhóm khác điều kiện kích hoạt, số view và cách sử dụng TopView.
    /// </summary>
    public static class AutoSectionCoordinator
    {
        public static AutoSectionWorkerResult Run(
            Drawing drawing,
            Model model,
            ModelPart part,
            AutoSectionTargetKind targetKind,
            SectionViewAttributeResolution sectionAttributeResolution
        )
        {
            try
            {
                if (targetKind == AutoSectionTargetKind.Plate)
                {
                    return SectionScript.RunPlateSingleSafe(
                        drawing,
                        model,
                        part,
                        GetResolvedAttributeName(sectionAttributeResolution)
                    );
                }

                bool useLegacyPrecheck =
                    targetKind == AutoSectionTargetKind.ShapeIH
                    || (targetKind == AutoSectionTargetKind.ShapeC && IsBracketShape(part));

                HShapeAutoSectionPrecheckResult precheck = useLegacyPrecheck
                    ? ShapeScript.PrepareAutoSectionPrecheck(drawing, model, part)
                    : ShapeCScript.PrepareAutoSectionPrecheck(drawing, model, part);

                AutoSectionWorkerResult result = new AutoSectionWorkerResult();
                result.OriginalHoleResult = precheck != null ? precheck.HoleResult : -1;

                if (precheck == null)
                {
                    result.Status = AutoSectionWorkerStatus.PrecheckUnknown;
                    result.Message = "Precheck Auto Section khong tra ket qua.";
                    return result;
                }

                if (precheck.HasPartialSectionLayout)
                {
                    result.Status = AutoSectionWorkerStatus.PartialLayout;
                    result.Message = "Section layout dang co mot phan; khong tu repair.";
                    return result;
                }

                if (
                    (drawing is SinglePartDrawing && precheck.HasCompleteSingleLayout)
                    || (drawing is AssemblyDrawing && precheck.HasCompleteAssemblyLayout)
                )
                {
                    result.Status = AutoSectionWorkerStatus.ExistingLayout;
                    result.Message = "Section layout da ton tai.";
                    return result;
                }

                if (!precheck.IsValid)
                {
                    result.Status = AutoSectionWorkerStatus.PrecheckUnknown;
                    result.Message = precheck.Message;
                    return result;
                }

                if (!precheck.HasTopBottomDifference)
                {
                    result.Status = AutoSectionWorkerStatus.NoSectionRequired;
                    result.Message = precheck.Message;
                    return result;
                }

                string attributeName = GetResolvedAttributeName(sectionAttributeResolution);
                if (String.IsNullOrWhiteSpace(attributeName))
                {
                    result.Status = AutoSectionWorkerStatus.PreflightFailed;
                    result.Message =
                        "Khong xac dinh duoc Section view property sau khi precheck ket luan "
                        + "can tao Section. "
                        + (
                            sectionAttributeResolution == null
                                ? "Resolver did not run."
                                : sectionAttributeResolution.Error
                        );
                    return result;
                }

                if (drawing is SinglePartDrawing)
                {
                    result = SectionScript.RunSingleSafe(
                        drawing,
                        model,
                        part,
                        precheck.TopView,
                        precheck.FrontView,
                        attributeName
                    );
                }
                else if (drawing is AssemblyDrawing)
                {
                    result = SectionScript.RunAssemblySafe(
                        drawing,
                        model,
                        part,
                        precheck.TopView,
                        precheck.FrontView,
                        attributeName
                    );
                }
                else
                {
                    result.Status = AutoSectionWorkerStatus.PreflightFailed;
                    result.Message = "Drawing khong phai Single Part hoac Assembly.";
                    return result;
                }

                result.OriginalHoleResult = precheck.HoleResult;
                result.RequiresDimensionPass =
                    result.Status == AutoSectionWorkerStatus.CreatedSingle
                    || result.Status == AutoSectionWorkerStatus.CreatedAssemblyBottom;
                result.HasSingleLayout =
                    result.Status == AutoSectionWorkerStatus.CreatedSingle;
                result.RelocateMarksAfterDimension = result.RequiresDimensionPass;
                return result;
            }
            catch (Exception ex)
            {
                AutoSectionWorkerResult failed = new AutoSectionWorkerResult();
                failed.IsPlateSection = targetKind == AutoSectionTargetKind.Plate;
                failed.Status = AutoSectionWorkerStatus.PreflightFailed;
                failed.Message = "Auto Section coordinator loi: " + ex.Message;
                return failed;
            }
        }

        private static string GetResolvedAttributeName(
            SectionViewAttributeResolution resolution
        )
        {
            return resolution != null
                && resolution.Success
                && !String.IsNullOrWhiteSpace(resolution.AttributeName)
                    ? resolution.AttributeName
                    : "";
        }

        private static bool IsBracketShape(ModelPart part)
        {
            if (part == null)
                return false;

            try
            {
                string profile = "";
                part.GetReportProperty("PROFILE", ref profile);
                return !String.IsNullOrWhiteSpace(profile)
                    && profile.Trim().StartsWith("[", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public enum PlateAutoSectionDecision
    {
        NoSection,
        Required,
        ExistingEquivalent,
        Rejected
    }

    public sealed class PlateAutoSectionAnalysisResult
    {
        public string SectionName = "A";
        public List<PlateAutoSectionAnalysisResult> Sections = new List<PlateAutoSectionAnalysisResult>();
        public PlateAutoSectionDecision Decision = PlateAutoSectionDecision.Rejected;
        public string Message = "";
        public string FrontProof = "";
        public string CenterProof = "";
        public string ExistingSectionProof = "";
        public DrawingView FrontView = null;
        public DrawingView TopView = null;
        public DrawingView ExistingSectionA = null;
        public Point AxisOrigin = null;
        public Point LongitudinalDirection = null;
        public Point PerpendicularDirection = null;
        public Point CutStart = null;
        public Point CutEnd = null;
        public Point InsertionPoint = null;
        public double MinLongitudinal;
        public double MaxLongitudinal;
        public double MinPerpendicular;
        public double MaxPerpendicular;
        public double MidLongitudinal;
        public double Thickness;
        public double DepthUp;
        public double DepthDown;
        public double FrontScale;
        public int SectionViewCountBefore;
    }

    public class SectionScript
    {
        private const double TOL = 1.0;
        // CHỈNH MẶT CẮT H/C (mm hình học): thêm sâu B, dịch vị trí đầu C; ảnh hưởng phần hình học được cắt.
        private const double SECTION_B_EXTRA_DEPTH = 5.0;
        private const double SECTION_C_EXTRA_START = 3.0;
        // CHỈNH NHẬN RÃNH (mm): giới hạn kích thước và dung sai gộp điểm; MAX_GEOMETRY_ITEMS giới hạn lượng dữ liệu đọc.
        private const double SECTION_NOTCH_MIN_SIZE = 15.0;
        private const double SECTION_NOTCH_MAX_SIZE = 250.0;
        private const double SECTION_NOTCH_POINT_MERGE_TOL = 0.5;
        private const int SECTION_NOTCH_MAX_GEOMETRY_ITEMS = 20000;
        // CHỈNH BỐ CỤC (mm giấy): GetSectionGap lấy max(giá trị này, 0.65 × chiều cao view tham chiếu).
        private const double DEFAULT_SECTION_GAP = 55.0;
        private const double SECTION_MARK_BEYOND_DIM_GAP_PAPER_MM = 5.0;
        // CHỈNH HƯỚNG CẮT B/C: true đi trái→phải, false phải→trái; đổi có thể đảo hướng nhìn section.
        private const bool SECTION_LINE_LEFT_TO_RIGHT = false;
        // CHỈNH STYLE: tên file thuộc tính Tekla phải tồn tại/nạp được; không phải nhãn section A/B/C.
        private const string SECTION_MARK_ATTRIBUTE_NAME = "GEO_SECTION";
        private const string MERGED_SECTION_VIEW_ATTRIBUTE_NAME =
            "TTSK_GEO_SECTION_MERGED";
        // CHỈNH NHẬN BEVEL TẤM: dung sai mm, góc song song độ và tỷ số lệch biên không đơn vị.
        // Nới các ngưỡng có thể nhận nhầm cạnh; khác với nhóm bố trí mark/view bên dưới.
        private const double PLATE_GEOMETRY_TOL = 0.5;
        private const double PLATE_PARALLEL_ANGLE_DEGREES = 3.0;
        private const double PLATE_MAX_BOUNDARY_OFFSET_RATIO = 0.30;
        // CHỈNH BỐ CỤC TẤM (mm giấy): kéo dài mark được nhân scale Front; gap đặt view dùng trực tiếp trên sheet.
        private const double PLATE_SECTION_MARK_EXTENSION_PAPER_MM = 4.0;
        private const double PLATE_SECTION_INSERT_GAP_PAPER_MM = 12.0;
        // DUNG SAI SỐ: nhóm adaptive ổn định phép tính giao/chiếu; không dùng để chỉnh chiều dài chân DIM.
        private const double PLATE_NUMERIC_EPSILON = 0.000001;
        private const double PLATE_MIN_ADAPTIVE_TOL = 0.0001;
        private const double PLATE_MAX_ADAPTIVE_TOL = 0.01;
        private const double PLATE_ADAPTIVE_TOL_RATIO = 0.000001;

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
            public FrontNotchDetectionStatus Status = FrontNotchDetectionStatus.NotChecked;

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

        private sealed class PlateLongitudinalWitness
        {
            public Point Start;
            public Point End;
            public double Level;
            public double Offset;
            public double MinT;
            public double MaxT;
            public int BoundarySide;
        }

        private sealed class PlateProjectedAxisCandidate
        {
            public Point Origin;
            public Point Direction;
            public double RepresentativeLength;
            public double SupportLength;
        }

        private sealed class PlateAxisEvaluation
        {
            public PlateAutoSectionAnalysisResult Geometry;
            public List<PlateLongitudinalWitness> Witnesses;
            public List<Point> CenterHull;
            public List<Point> BevelSegments;
            public bool HasFrontWitness;
            public bool HasCenterBevel;
            public double SupportLength;
            public string FrontProof;
            public string CenterProof;
        }

        public static AutoSectionWorkerResult RunPlateSingleSafe(
            Drawing drawing,
            Model model,
            ModelPart part,
            string sectionViewAttributeName
        )
        {
            if (FindExistingTargetPlateSection(drawing, part) != null)
                return new AutoSectionWorkerResult { IsPlateSection = true,
                    Status = AutoSectionWorkerStatus.ExistingLayout,
                    Message = "Plate da co mat cat cua chi tiet; giu nguyen va tiep tuc DIM." };
            var combined = new AutoSectionWorkerResult { IsPlateSection = true,
                Status = AutoSectionWorkerStatus.NoSectionRequired };
            var messages = new List<string>();
            // Analyze each family again after the preceding section is committed;
            // the second worker must capture fresh view counts and insertion bounds.
            for (int family = 0; family < 2; family++)
            {
                try
                {
                    var analysis = AnalyzePlateFrontBevelFamily(drawing, model, part, family);
                    var one = RunPlateSectionPlanSafe(drawing, model, part, sectionViewAttributeName, analysis);
                    messages.Add(analysis.SectionName + ": " + one.Message);
                    if (!one.IsSafeToContinue || IsPlateSectionFailure(one.Status))
                    {
                        combined.Status = one.Status;
                        combined.IsSafeToContinue = one.IsSafeToContinue;
                        combined.Message = String.Join(" | ", messages.ToArray());
                        return combined;
                    }
                    if (one.Status == AutoSectionWorkerStatus.CreatedSingle)
                        combined.Status = AutoSectionWorkerStatus.CreatedSingle;
                    else if (one.Status == AutoSectionWorkerStatus.ExistingLayout
                        && combined.Status != AutoSectionWorkerStatus.CreatedSingle)
                        combined.Status = AutoSectionWorkerStatus.ExistingLayout;
                    if (analysis.SectionName == "A") { combined.SectionA = one.SectionA; combined.MarkA = one.MarkA; }
                    else { combined.SectionB = one.SectionA; combined.MarkB = one.MarkA; }
                }
                catch (Exception ex)
                {
                    combined.Status = AutoSectionWorkerStatus.PreflightFailed;
                    messages.Add((family == 0 ? "A: " : "B: ") + ex.Message);
                    combined.Message = String.Join(" | ", messages.ToArray());
                    return combined;
                }
            }
            combined.Message = String.Join(" | ", messages.ToArray());
            return combined;
        }

        private static bool IsPlateSectionFailure(AutoSectionWorkerStatus status)
        {
            return status == AutoSectionWorkerStatus.PreflightFailed
                || status == AutoSectionWorkerStatus.CreateFailed
                || status == AutoSectionWorkerStatus.RolledBack
                || status == AutoSectionWorkerStatus.UnsafeRollbackFailed;
        }

        private static AutoSectionWorkerResult RunPlateSectionPlanSafe(
            Drawing drawing, Model model, ModelPart part, string sectionViewAttributeName,
            PlateAutoSectionAnalysisResult analysis)
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            result.IsPlateSection = true;

            if (analysis == null)
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = "Plate Auto Section analysis khong tra ket qua.";
                return result;
            }

            if (analysis.Decision == PlateAutoSectionDecision.NoSection)
            {
                result.Status = AutoSectionWorkerStatus.NoSectionRequired;
                result.Message = analysis.Message;
                return result;
            }

            if (analysis.Decision == PlateAutoSectionDecision.ExistingEquivalent)
            {
                SectionAttributeSet existingAttributes;
                string existingAttributeMessage;
                if (
                    !TryLoadSectionAttributes(
                        analysis.SectionName,
                        analysis.FrontScale,
                        sectionViewAttributeName,
                        out existingAttributes,
                        out existingAttributeMessage
                    )
                )
                {
                    result.Status = AutoSectionWorkerStatus.PreflightFailed;
                    result.Message =
                        "Khong load duoc standard de sua label Section A hien huu. "
                        + existingAttributeMessage;
                    result.SectionA = analysis.ExistingSectionA;
                    return result;
                }

                string repairMessage;
                bool rollbackSafe;
                if (
                    !TryRepairExistingPlateSectionViewLabel(
                        drawing,
                        analysis.ExistingSectionA,
                        existingAttributes.ViewAttributes,
                        out repairMessage,
                        out rollbackSafe
                    )
                )
                {
                    result.Status = rollbackSafe
                        ? AutoSectionWorkerStatus.PreflightFailed
                        : AutoSectionWorkerStatus.UnsafeRollbackFailed;
                    result.IsSafeToContinue = rollbackSafe;
                    result.Message = repairMessage;
                    result.SectionA = analysis.ExistingSectionA;
                    return result;
                }
                result.Status = AutoSectionWorkerStatus.ExistingLayout;
                result.Message = analysis.Message + " " + repairMessage;
                result.SectionA = analysis.ExistingSectionA;
                return result;
            }

            if (analysis.Decision == PlateAutoSectionDecision.Rejected)
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = analysis.Message;
                return result;
            }

            if (analysis.Decision != PlateAutoSectionDecision.Required)
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = analysis.Message;
                return result;
            }

            SectionAttributeSet attributesA;
            string attributeMessage;
            if (
                !TryLoadSectionAttributes(
                    analysis.SectionName,
                    analysis.FrontScale,
                    sectionViewAttributeName,
                    out attributesA,
                    out attributeMessage
                )
            )
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message =
                    "Plate can A-A nhung Section standard khong san sang. "
                    + attributeMessage;
                return result;
            }

            string identityMessage;
            if (!IsActivePlateDrawingIdentityUnchanged(drawing, part, out identityMessage))
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = identityMessage;
                return result;
            }

            Point frontOrigin = ClonePoint(analysis.FrontView.Origin);
            Point topOrigin = ClonePoint(analysis.TopView.Origin);
            int frontIdentifier = GetViewIdentifier(analysis.FrontView);
            int topIdentifier = GetViewIdentifier(analysis.TopView);
            int beforeCount = CountSectionViews(drawing);

            if (
                beforeCount != analysis.SectionViewCountBefore
                || frontIdentifier <= 0
                || topIdentifier <= 0
                || !IsFinitePoint(frontOrigin)
                || !IsFinitePoint(topOrigin)
            )
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message =
                    "Drawing/view state da thay doi sau Plate preflight; khong tao A-A.";
                return result;
            }

            DrawingView sectionA = null;
            SectionMark markA = null;
            bool created = CreateOneSectionView(
                analysis.FrontView,
                analysis.SectionName,
                analysis.CutStart,
                analysis.CutEnd,
                analysis.InsertionPoint,
                analysis.DepthUp,
                analysis.DepthDown,
                attributesA,
                out sectionA,
                out markA
            );

            result.SectionA = sectionA;
            result.MarkA = markA;

            if (!created)
            {
                return FinishPlateCreateFailure(
                    drawing,
                    "Khong tao duoc Plate Section A-A.",
                    sectionA,
                    markA,
                    analysis.FrontView,
                    analysis.TopView
                );
            }

            // Tekla can assign the first available letter during creation. Apply
            // the geometry-derived name on the created mark before read-back.
            try
            {
                // CreateSectionView may offset the returned view origin from
                // its insertion point (notably for horizontal thickness views).
                // Apply the plate's sheet-space placement explicitly before
                // validating it; do not alter the Front or Top view origins.
                sectionA.Origin = ClonePoint(analysis.InsertionPoint);
                if (!sectionA.Modify())
                    return FinishPlateCreateFailure(drawing, "Cannot place Plate Section " + analysis.SectionName,
                        sectionA, markA, analysis.FrontView, analysis.TopView);
                markA.Attributes.MarkName = analysis.SectionName;
                if (!markA.Modify())
                    return FinishPlateCreateFailure(drawing, "Cannot set section mark name " + analysis.SectionName,
                        sectionA, markA, analysis.FrontView, analysis.TopView);
            }
            catch (Exception ex)
            {
                return FinishPlateCreateFailure(drawing, "Cannot set section mark name: " + ex.Message,
                    sectionA, markA, analysis.FrontView, analysis.TopView);
            }

            string validationMessage;
            if (
                !CommitAndValidatePlateSection(
                    drawing,
                    part,
                    analysis,
                    sectionA,
                    markA,
                    beforeCount,
                    frontIdentifier,
                    topIdentifier,
                    frontOrigin,
                    topOrigin,
                    attributesA.ViewAttributes,
                    out validationMessage
                )
            )
            {
                return FinishPlateCreateFailure(
                    drawing,
                    validationMessage,
                    sectionA,
                    markA,
                    analysis.FrontView,
                    analysis.TopView
                );
            }

            result.Status = AutoSectionWorkerStatus.CreatedSingle;
            result.Message =
                "Plate: da tao Section " + analysis.SectionName + " tai trung diem; giu nguyen Front/Top. "
                + "View="
                + sectionViewAttributeName
                + " loaded; Mark="
                + SECTION_MARK_ATTRIBUTE_NAME
                + " loaded. "
                + analysis.FrontProof
                + " "
                + analysis.CenterProof;
            result.RequiresDimensionPass = false;
            result.HasSingleLayout = true;
            result.RelocateMarksAfterDimension = false;
            return result;
        }

        /// <summary>
        /// Read-only plate classifier. It changes the model work plane only
        /// temporarily and restores the original plane in finally.
        /// </summary>
        public static PlateAutoSectionAnalysisResult AnalyzePlateFrontBevel(
            Drawing drawing,
            Model model,
            ModelPart part
        )
        {
            DrawingView existing = FindExistingTargetPlateSection(drawing, part);
            if (existing != null)
                return new PlateAutoSectionAnalysisResult {
                    Decision = PlateAutoSectionDecision.ExistingEquivalent,
                    ExistingSectionA = existing,
                    Message = "Plate da co mat cat cua chi tiet; giu nguyen va tiep tuc DIM."
                };
            var straight = AnalyzePlateFrontBevelFamily(drawing, model, part, 0);
            var oblique = AnalyzePlateFrontBevelFamily(drawing, model, part, 1);
            var summary = new PlateAutoSectionAnalysisResult();
            summary.Sections.Add(straight);
            summary.Sections.Add(oblique);
            summary.Decision = PlateAutoSectionDecision.NoSection;
            foreach (var section in summary.Sections)
            {
                if (section.Decision == PlateAutoSectionDecision.Rejected)
                {
                    summary.Decision = PlateAutoSectionDecision.Rejected;
                    break;
                }
                if (section.Decision == PlateAutoSectionDecision.Required)
                    summary.Decision = PlateAutoSectionDecision.Required;
                else if (section.Decision == PlateAutoSectionDecision.ExistingEquivalent
                    && summary.Decision != PlateAutoSectionDecision.Required)
                    summary.Decision = PlateAutoSectionDecision.ExistingEquivalent;
            }
            summary.Message = "A: " + straight.Message + " | B: " + oblique.Message;
            if (summary.Decision == PlateAutoSectionDecision.Rejected)
                summary.Message = "Plate Auto Section precheck failed: " + String.Join(" | ",
                    summary.Sections.FindAll(s => s.Decision == PlateAutoSectionDecision.Rejected)
                        .ConvertAll(s => s.SectionName + ": " + s.Message).ToArray());
            return summary;
        }

        private static DrawingView FindExistingTargetPlateSection(Drawing drawing, ModelPart part)
        {
            if (!(drawing is SinglePartDrawing) || part == null || part.Identifier == null)
                return null;
            DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
            while (views.MoveNext())
            {
                DrawingView view = views.Current as DrawingView;
                if (IsSectionView(view) && ViewContainsPart(view, part.Identifier))
                    return view;
            }
            return null;
        }

        private static PlateAutoSectionAnalysisResult AnalyzePlateFrontBevelFamily(
            Drawing drawing, Model model, ModelPart part, int family, bool inspectExisting = true)
        {
            PlateAutoSectionAnalysisResult result = new PlateAutoSectionAnalysisResult();
            // Name by qualifying geometry, not by family index. An oblique-only
            // plate still has its first section A; B is reserved for the second family.
            bool hasStraightBevel = family == 1
                && AnalyzePlateFrontBevelFamily(drawing, model, part, 0, false).Decision
                    == PlateAutoSectionDecision.Required;
            result.SectionName = ResolvePlateSectionName(family == 1, hasStraightBevel);
            TransformationPlane oldPlane = null;

            try
            {
                if (!(drawing is SinglePartDrawing))
                    return RejectPlate(result, "Plate Auto Section chi ho tro SinglePartDrawing.");
                if (model == null || !model.GetConnectionStatus() || part == null)
                    return RejectPlate(result, "Khong ket noi duoc Model/Plate de audit A-A.");
                if (part.Identifier == null || part.Identifier.ID <= 0)
                    return RejectPlate(result, "Plate ModelIdentifier khong hop le.");

                result.FrontView = FindUniqueSemanticView(drawing, "FrontView");
                result.TopView = FindUniqueSemanticView(drawing, "TopView");
                if (result.FrontView == null || result.TopView == null)
                {
                    return RejectPlate(
                        result,
                        "Can dung 1 FrontView va 1 TopView de audit Plate A-A."
                    );
                }
                if (
                    !ViewContainsPart(result.FrontView, part.Identifier)
                    || !ViewContainsPart(result.TopView, part.Identifier)
                )
                {
                    return RejectPlate(result, "Front/Top khong cung chua target Plate.");
                }

                result.FrontScale = GetViewScale(result.FrontView);
                if (!IsFinite(result.FrontScale) || result.FrontScale <= 0.0)
                    return RejectPlate(result, "Khong doc duoc scale FrontView.");

                oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                if (
                    !model
                        .GetWorkPlaneHandler()
                        .SetCurrentTransformationPlane(
                            new TransformationPlane(result.FrontView.DisplayCoordinateSystem)
                        )
                )
                {
                    return RejectPlate(result, "Khong dat duoc work plane FrontView de audit.");
                }

                Solid solid = part.GetSolid();
                if (solid == null || solid.MinimumPoint == null || solid.MaximumPoint == null)
                    return RejectPlate(result, "Khong doc duoc exact Solid cua Plate.");

                List<Point> projectedPoints;
                List<ProjectedFrontSegment> projectedSegments;
                if (
                    !TryCollectPlateProjectedSolidGeometry(
                        solid,
                        out projectedPoints,
                        out projectedSegments
                    )
                )
                {
                    return RejectPlate(result, "Khong enumerate duoc exact Solid edges cua Plate.");
                }

                List<PlateProjectedAxisCandidate> axisCandidates =
                    BuildProjectedAxisCandidates(projectedSegments, true);
                if (axisCandidates.Count == 0)
                    return RejectPlate(result, "Khong tim duoc ho canh dai trong FrontView.");

                List<PlateAxisEvaluation> qualified = new List<PlateAxisEvaluation>();
                List<string> candidateDiagnostics = new List<string>();
                bool anyFrontWitness = false;
                double thickness = Math.Abs(solid.MaximumPoint.Z - solid.MinimumPoint.Z);
                if (!IsFinite(thickness))
                    thickness = 0.0;

                for (int axisIndex = 0; axisIndex < axisCandidates.Count; axisIndex++)
                {
                    PlateProjectedAxisCandidate axis = axisCandidates[axisIndex];
                    // Front-vertical bevels are outside the plate Auto Section
                    // contract. Family 0 is horizontal; family 1 is oblique.
                    if (!IsPlateSectionAxisAllowed(axis.Direction, axis.RepresentativeLength))
                        continue;
                    PlateAutoSectionAnalysisResult geometry =
                        new PlateAutoSectionAnalysisResult();
                    geometry.AxisOrigin = ClonePoint(axis.Origin);
                    geometry.LongitudinalDirection = ClonePoint(axis.Direction);
                    geometry.PerpendicularDirection = new Point(
                        -axis.Direction.Y,
                        axis.Direction.X,
                        0.0
                    );
                    geometry.Thickness = thickness;
                    if (
                        !TryGetPlateProjectedExtents(
                            projectedPoints,
                            geometry.AxisOrigin,
                            geometry.LongitudinalDirection,
                            geometry.PerpendicularDirection,
                            out geometry.MinLongitudinal,
                            out geometry.MaxLongitudinal,
                            out geometry.MinPerpendicular,
                            out geometry.MaxPerpendicular
                        )
                    )
                        continue;
                    geometry.MidLongitudinal =
                        (geometry.MinLongitudinal + geometry.MaxLongitudinal) * 0.5;
                    bool obliqueAxis = Math.Min(Math.Abs(axis.Direction.X), Math.Abs(axis.Direction.Y))
                        * (geometry.MaxLongitudinal - geometry.MinLongitudinal) > 0.001;
                    if (obliqueAxis != (family == 1))
                        continue;

                    List<PlateLongitudinalWitness> axisWitnesses =
                        FindPlateLongitudinalWitnesses(projectedSegments, geometry);
                    var allWitnesses = new List<PlateLongitudinalWitness>(axisWitnesses);
                    allWitnesses.Sort((a, b) => {
                        int span = (b.MaxT - b.MinT).CompareTo(a.MaxT - a.MinT);
                        return span != 0 ? span : a.MinT.CompareTo(b.MinT);
                    });
                    var stations = new List<double> { geometry.MidLongitudinal };
                    foreach (var witness in allWitnesses)
                        stations.Add((witness.MinT + witness.MaxT) * 0.5);
                    List<Point> axisCenterHull = new List<Point>();
                    List<Point> axisBevelSegments = new List<Point>();
                    bool axisHasCenterBevel = false;
                    double stationMargin = Math.Max(0.001, GetPlateAdaptiveTolerance(geometry)) * 2.0;
                    foreach (double station in stations)
                    {
                        geometry.MidLongitudinal = station;
                        axisWitnesses = allWitnesses.FindAll(w =>
                            w.MinT + stationMargin < station && w.MaxT - stationMargin > station);
                        if (axisWitnesses.Count == 0) continue;
                        axisHasCenterBevel = TryGetPlateCenterBevelProof(solid, geometry,
                            axisWitnesses, out axisCenterHull, out axisBevelSegments);
                        if (axisHasCenterBevel) break;
                    }
                    bool axisHasFrontWitness = axisWitnesses.Count > 0;
                    anyFrontWitness = anyFrontWitness || axisHasFrontWitness;

                    PlateAxisEvaluation evaluation = new PlateAxisEvaluation();
                    evaluation.Geometry = geometry;
                    evaluation.Witnesses = axisWitnesses;
                    evaluation.CenterHull = axisCenterHull;
                    evaluation.BevelSegments = axisBevelSegments;
                    evaluation.HasFrontWitness = axisHasFrontWitness;
                    evaluation.HasCenterBevel = axisHasCenterBevel;
                    evaluation.SupportLength = axis.SupportLength;
                    evaluation.FrontProof = DescribePlateFrontProof(
                        axisWitnesses,
                        geometry
                    );
                    evaluation.CenterProof = DescribePlateCenterProof(
                        axisCenterHull,
                        axisBevelSegments,
                        axisHasCenterBevel
                    );
                    candidateDiagnostics.Add(
                        "D=("
                        + FormatDiagnosticNumber(axis.Direction.X)
                        + ","
                        + FormatDiagnosticNumber(axis.Direction.Y)
                        + ") "
                        + evaluation.FrontProof
                        + " "
                        + evaluation.CenterProof
                    );
                    if (axisHasFrontWitness && axisHasCenterBevel)
                        qualified.Add(evaluation);
                }

                if (qualified.Count == 0)
                {
                    if (!anyFrontWitness)
                    {
                        result.Decision = PlateAutoSectionDecision.NoSection;
                        result.Message =
                            "Plate khong co Front longitudinal bevel witness; "
                            + "center-only bevel neu co la end/face feature. "
                            + "Khong tao Section, tiep tuc Plate AutoDim. "
                            + String.Join(" | ", candidateDiagnostics.ToArray());
                        return result;
                    }
                    return RejectPlate(
                        result,
                        "Plate bevel evidence ambiguous; khong co ho canh nao dong thoi "
                        + "dat Front witness va center cross-section bevel. "
                        + String.Join(" | ", candidateDiagnostics.ToArray())
                    );
                }

                qualified.Sort(
                    delegate(PlateAxisEvaluation first, PlateAxisEvaluation second)
                    {
                        return second.SupportLength.CompareTo(first.SupportLength);
                    }
                );
                if (
                    qualified.Count > 1
                    && Math.Abs(
                        qualified[0].SupportLength - qualified[1].SupportLength
                    ) <= PLATE_NUMERIC_EPSILON
                )
                {
                    return RejectPlate(
                        result,
                        "Nhieu ho canh bevel cung dat HIGH confidence; khong tu chon mat cat. "
                        + String.Join(" | ", candidateDiagnostics.ToArray())
                    );
                }

                PlateAxisEvaluation selected = qualified[0];
                result.AxisOrigin = selected.Geometry.AxisOrigin;
                result.LongitudinalDirection = selected.Geometry.LongitudinalDirection;
                result.PerpendicularDirection = selected.Geometry.PerpendicularDirection;
                result.MinLongitudinal = selected.Geometry.MinLongitudinal;
                result.MaxLongitudinal = selected.Geometry.MaxLongitudinal;
                result.MinPerpendicular = selected.Geometry.MinPerpendicular;
                result.MaxPerpendicular = selected.Geometry.MaxPerpendicular;
                result.MidLongitudinal = selected.Geometry.MidLongitudinal;
                result.Thickness = selected.Geometry.Thickness;
                result.FrontProof = selected.FrontProof;
                result.CenterProof = selected.CenterProof;

                double markExtension =
                    PLATE_SECTION_MARK_EXTENSION_PAPER_MM * result.FrontScale;
                Point centerPoint = AddPlateVector(
                    result.AxisOrigin,
                    result.LongitudinalDirection,
                    result.MidLongitudinal
                );
                result.CutStart = AddPlateVector(
                    centerPoint,
                    result.PerpendicularDirection,
                    result.MinPerpendicular - markExtension
                );
                result.CutEnd = AddPlateVector(
                    centerPoint,
                    result.PerpendicularDirection,
                    result.MaxPerpendicular + markExtension
                );
                OrientObliquePlateCutDownward(result);

                double frontPaperWidth = GetViewPaperWidth(result.FrontView);
                double sectionPaperHalfWidth = result.Thickness / result.FrontScale * 0.5;
                result.InsertionPoint = new Point(
                    result.FrontView.Origin.X
                        + frontPaperWidth * 0.5
                        + PLATE_SECTION_INSERT_GAP_PAPER_MM
                        + sectionPaperHalfWidth,
                    result.FrontView.Origin.Y,
                    0.0
                );
                if (family == 1)
                    result.InsertionPoint.Y -= GetViewPaperHeight(result.FrontView) + PLATE_SECTION_INSERT_GAP_PAPER_MM;
                // Section depth is a narrow longitudinal neighborhood around
                // the midpoint. Plate thickness is never an eligibility gate.
                result.DepthUp = Math.Max(
                    0.1,
                    (result.MaxLongitudinal - result.MinLongitudinal) * 0.01
                );
                result.DepthDown = result.DepthUp;
                foreach (var witness in selected.Witnesses)
                {
                    double clearance = Math.Min(result.MidLongitudinal - witness.MinT,
                        witness.MaxT - result.MidLongitudinal);
                    result.DepthUp = Math.Min(result.DepthUp, clearance * 0.5);
                }
                result.DepthDown = result.DepthUp;
                result.SectionViewCountBefore = CountSectionViews(drawing);

                if (!inspectExisting)
                {
                    result.Decision = PlateAutoSectionDecision.Required;
                    result.Message = result.FrontProof + " " + result.CenterProof;
                    return result; // Read-only plan audit; never used by the writer.
                }

                DrawingView equivalent;
                bool collision;
                string existingMessage;
                if (
                    !TryClassifyExistingPlateCenterSection(
                        drawing,
                        part,
                        result,
                        out equivalent,
                        out collision,
                        out existingMessage,
                        solid
                    )
                )
                {
                    return RejectPlate(result, existingMessage);
                }

                result.ExistingSectionProof = existingMessage;
                if (collision)
                    return RejectPlate(result, existingMessage);
                if (equivalent != null)
                {
                    result.Decision = PlateAutoSectionDecision.ExistingEquivalent;
                    result.ExistingSectionA = equivalent;
                    result.Message =
                        "Plate da co Section " + result.SectionName + " tuong duong; khong tao trung. "
                        + result.FrontProof
                        + " "
                        + result.CenterProof
                        + " "
                        + existingMessage;
                    return result;
                }

                result.Decision = PlateAutoSectionDecision.Required;
                result.Message =
                    "Plate can Section " + result.SectionName + " tai vi tri vat da xac nhan. "
                    + result.FrontProof
                    + " "
                    + result.CenterProof
                    + " "
                    + existingMessage;
                return result;
            }
            catch (Exception ex)
            {
                return RejectPlate(result, "Plate Auto Section audit loi: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (oldPlane != null && model != null)
                        model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }
        }

        public static AutoSectionWorkerResult RunSingleSafe(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName
        )
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            SectionGeometry geometry;
            SectionAttributeSet attributesB;
            SectionAttributeSet attributesC;
            Point topOrigin;
            Point frontOrigin;
            double savedTopScale;
            string preflightMessage;

            if (
                !TryPreflightSingle(
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
                    out preflightMessage
                )
            )
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
            double insertBY =
                topOrigin.Y > frontOrigin.Y ? topOrigin.Y : frontOrigin.Y + sectionGap;
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
                out markB
            );

            if (!createB)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Section B.",
                    sectionB,
                    markB,
                    sectionC,
                    markC
                );

            if (!CommitAndValidateCreatedSection(drawing, part, sectionB))
                return FinishCreateFailure(
                    drawing,
                    "Section B tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    sectionC,
                    markC
                );

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
                out markC
            );

            if (!createC)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Section C.",
                    sectionB,
                    markB,
                    sectionC,
                    markC
                );

            if (!CommitAndValidateCreatedSection(drawing, part, sectionC))
                return FinishCreateFailure(
                    drawing,
                    "Section C tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    sectionC,
                    markC
                );

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
                    markC
                );

                bool topIsSafe = IsViewPresent(drawing, topView);
                result.SectionB = sectionB;
                result.SectionC = sectionC;
                result.MarkB = markB;
                result.MarkC = markC;

                if (rollbackSucceeded && topIsSafe)
                {
                    result.Status = AutoSectionWorkerStatus.RolledBack;
                    result.Message =
                        "Khong xoa duoc TopView; Section B/C da rollback, TopView van duoc giu.";
                    return result;
                }

                result.Status = AutoSectionWorkerStatus.UnsafeRollbackFailed;
                result.IsSafeToContinue = false;
                result.Message =
                    "Xoa TopView hoac rollback B/C that bai; drawing khong an toan de save.";
                return result;
            }

            result.Status = AutoSectionWorkerStatus.CreatedSingle;
            result.Message = "Single: da tao Section B/C va xoa dung TopView goc.";
            result.SectionB = sectionB;
            result.SectionC = sectionC;
            result.MarkB = markB;
            result.MarkC = markC;
            return result;
        }

        public static AutoSectionWorkerResult RunAssemblySafe(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            string sectionViewAttributeName
        )
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            SectionGeometry geometry;
            SectionAttributeSet attributesB;
            Point frontOrigin;
            string preflightMessage;

            if (
                !TryPreflightAssembly(
                    drawing,
                    model,
                    part,
                    topView,
                    frontView,
                    sectionViewAttributeName,
                    out geometry,
                    out attributesB,
                    out frontOrigin,
                    out preflightMessage
                )
            )
            {
                result.Status = AutoSectionWorkerStatus.PreflightFailed;
                result.Message = preflightMessage;
                return result;
            }

            DrawingView sectionB = null;
            SectionMark markB = null;
            Point insertB = new Point(frontOrigin.X, frontOrigin.Y - GetSectionGap(frontView), 0.0);

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
                out markB
            );

            if (!created)
                return FinishCreateFailure(
                    drawing,
                    "Khong tao duoc Bottom Section B cho Assembly.",
                    sectionB,
                    markB,
                    null,
                    null
                );

            if (!CommitAndValidateCreatedSection(drawing, part, sectionB))
                return FinishCreateFailure(
                    drawing,
                    "Bottom Section B tao xong nhung khong validate duoc.",
                    sectionB,
                    markB,
                    null,
                    null
                );

            result.Status = AutoSectionWorkerStatus.CreatedAssemblyBottom;
            result.Message = "Assembly: da giu Top/Front va tao Bottom Section ten B.";
            result.SectionB = sectionB;
            result.MarkB = markB;
            return result;
        }

        public static bool RelocateCreatedMarksBeyondOutermostLeftDimension(
            Drawing drawing,
            AutoSectionWorkerResult workerResult,
            out string message
        )
        {
            message = "";

            if (drawing == null || workerResult == null)
            {
                message = "Khong co drawing hoac ket qua Auto Section de dat lai Section Mark.";
                return false;
            }

            List<SectionMark> marks = new List<SectionMark>();
            if (workerResult.MarkB != null)
                marks.Add(workerResult.MarkB);
            if (
                workerResult.MarkC != null
                && !System.Object.ReferenceEquals(workerResult.MarkC, workerResult.MarkB)
            )
                marks.Add(workerResult.MarkC);

            if (marks.Count == 0)
            {
                message = "Auto Section khong tra ve Section Mark vua tao.";
                return false;
            }

            DrawingView sourceView = null;
            try
            {
                sourceView = marks[0].GetView() as DrawingView;
            }
            catch { }

            if (sourceView == null)
            {
                message = "Khong xac dinh duoc FrontView cua Section Mark vua tao.";
                return false;
            }

            double outermostLeftDimensionX;
            if (!TryFindOutermostLeftVerticalDimensionLine(sourceView, out outermostLeftDimensionX))
            {
                message = "Khong tim thay dim dung ben trai FrontView de dat Section Mark ra ngoai dim tong.";
                return false;
            }

            double viewScale = GetViewScale(sourceView);
            if (!IsFinite(viewScale) || viewScale <= 0.0)
            {
                message = "Khong doc duoc scale FrontView de doi 5 mm giay sang toa do view.";
                return false;
            }

            double targetX =
                outermostLeftDimensionX - SECTION_MARK_BEYOND_DIM_GAP_PAPER_MM * viewScale;
            if (!IsFinite(targetX) || targetX >= outermostLeftDimensionX - TOL)
            {
                message = "Vi tri Section Mark sau dim tong khong hop le.";
                return false;
            }

            List<Point> originalLeftPoints = new List<Point>();
            List<Point> originalRightPoints = new List<Point>();

            for (int i = 0; i < marks.Count; i++)
            {
                SectionMark mark = marks[i];
                try
                {
                    mark.Select();
                }
                catch { }

                Point leftPoint = mark.LeftPoint;
                Point rightPoint = mark.RightPoint;
                if (!IsFinitePoint(leftPoint) || !IsFinitePoint(rightPoint))
                {
                    message = "Khong doc duoc hai dau cua Section Mark vua tao.";
                    return false;
                }

                originalLeftPoints.Add(ClonePoint(leftPoint));
                originalRightPoints.Add(ClonePoint(rightPoint));
            }

            for (int i = 0; i < marks.Count; i++)
            {
                SectionMark mark = marks[i];
                Point leftPoint = mark.LeftPoint;
                Point rightPoint = mark.RightPoint;

                if (leftPoint.X <= rightPoint.X)
                    leftPoint.X = targetX;
                else
                    rightPoint.X = targetX;

                if (!mark.Modify())
                {
                    RestoreSectionMarkPoints(
                        drawing,
                        marks,
                        originalLeftPoints,
                        originalRightPoints
                    );
                    message = "Tekla API khong Modify duoc Section Mark ra sau dim tong.";
                    return false;
                }
            }

            if (!SafeCommit(drawing))
            {
                RestoreSectionMarkPoints(
                    drawing,
                    marks,
                    originalLeftPoints,
                    originalRightPoints
                );
                message = "Khong Commit duoc vi tri Section Mark sau dim tong.";
                return false;
            }

            for (int i = 0; i < marks.Count; i++)
            {
                SectionMark mark = marks[i];
                try
                {
                    mark.Select();
                }
                catch { }

                double actualLabelSideX = Math.Min(mark.LeftPoint.X, mark.RightPoint.X);
                if (!IsFinite(actualLabelSideX) || Math.Abs(actualLabelSideX - targetX) > TOL)
                {
                    RestoreSectionMarkPoints(
                        drawing,
                        marks,
                        originalLeftPoints,
                        originalRightPoints
                    );
                    message = "Section Mark Modify xong nhung doc lai khong dung vi tri du kien.";
                    return false;
                }
            }

            message =
                "Da dat "
                + marks.Count.ToString(CultureInfo.InvariantCulture)
                + " Section Mark ra sau dim tong ben trai 5 mm giay.";
            return true;
        }

        private static bool TryFindOutermostLeftVerticalDimensionLine(
            DrawingView sourceView,
            out double outermostX
        )
        {
            outermostX = Double.PositiveInfinity;
            bool found = false;

            try
            {
                DrawingObjectEnumerator dimensions = sourceView.GetAllObjects(
                    typeof(StraightDimension)
                );

                while (dimensions != null && dimensions.MoveNext())
                {
                    StraightDimension dimension = dimensions.Current as StraightDimension;
                    if (dimension == null)
                        continue;

                    double lineX;
                    if (!TryGetLeftVerticalDimensionLineX(dimension, out lineX))
                        continue;

                    if (!found || lineX < outermostX)
                    {
                        outermostX = lineX;
                        found = true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return found && IsFinite(outermostX);
        }

        private static bool TryGetLeftVerticalDimensionLineX(
            StraightDimension dimension,
            out double lineX
        )
        {
            lineX = 0.0;
            if (dimension == null)
                return false;

            try
            {
                Vector up = dimension.UpDirection;
                double distance = dimension.Distance;
                Point start = dimension.StartPoint;
                Point end = dimension.EndPoint;
                if (
                    up == null
                    || !IsFinitePoint(start)
                    || !IsFinitePoint(end)
                    || !IsFinite(distance)
                )
                    return false;

                double upLength = Math.Sqrt(up.X * up.X + up.Y * up.Y);
                if (!IsFinite(upLength) || upLength <= 1e-9)
                    return false;

                double upX = up.X / upLength;
                double upY = up.Y / upLength;
                if (Math.Abs(upX) < 0.90 || Math.Abs(upY) > 0.10)
                    return false;

                if (Math.Abs(end.Y - start.Y) <= TOL)
                    return false;

                lineX = start.X + distance * upX;
                return IsFinite(lineX) && lineX < Math.Min(start.X, end.X) - TOL;
            }
            catch
            {
                return false;
            }
        }

        private static void RestoreSectionMarkPoints(
            Drawing drawing,
            List<SectionMark> marks,
            List<Point> leftPoints,
            List<Point> rightPoints
        )
        {
            if (marks == null || leftPoints == null || rightPoints == null)
                return;

            int count = Math.Min(marks.Count, Math.Min(leftPoints.Count, rightPoints.Count));
            for (int i = 0; i < count; i++)
            {
                try
                {
                    Point liveLeft = marks[i].LeftPoint;
                    Point liveRight = marks[i].RightPoint;
                    Point originalLeft = leftPoints[i];
                    Point originalRight = rightPoints[i];
                    liveLeft.X = originalLeft.X;
                    liveLeft.Y = originalLeft.Y;
                    liveLeft.Z = originalLeft.Z;
                    liveRight.X = originalRight.X;
                    liveRight.Y = originalRight.Y;
                    liveRight.Z = originalRight.Z;
                    marks[i].Modify();
                }
                catch { }
            }

            SafeCommit(drawing);
        }

        private static PlateAutoSectionAnalysisResult RejectPlate(
            PlateAutoSectionAnalysisResult result,
            string message
        )
        {
            if (result == null)
                result = new PlateAutoSectionAnalysisResult();
            result.Decision = PlateAutoSectionDecision.Rejected;
            result.Message = message ?? "Plate Auto Section preflight bi tu choi.";
            return result;
        }

        private static DrawingView FindUniqueSemanticView(Drawing drawing, string viewTypeName)
        {
            DrawingView found = null;
            int count = 0;

            try
            {
                DrawingObjectEnumerator views = drawing == null || drawing.GetSheet() == null
                    ? null
                    : drawing.GetSheet().GetAllViews();
                while (views != null && views.MoveNext())
                {
                    DrawingView view = views.Current as DrawingView;
                    if (view == null || view.ViewType.ToString() != viewTypeName)
                        continue;
                    found = view;
                    count++;
                }
            }
            catch
            {
                return null;
            }

            return count == 1 ? found : null;
        }

        private static bool TryCollectPlateProjectedSolidGeometry(
            Solid solid,
            out List<Point> points,
            out List<ProjectedFrontSegment> segments
        )
        {
            points = new List<Point>();
            segments = new List<ProjectedFrontSegment>();
            if (solid == null)
                return false;

            try
            {
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                int guard = 0;
                while (edges != null && edges.MoveNext())
                {
                    guard++;
                    if (guard > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                        return false;
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null || edge.StartPoint == null || edge.EndPoint == null)
                        continue;
                    Point start = new Point(edge.StartPoint.X, edge.StartPoint.Y, 0.0);
                    Point end = new Point(edge.EndPoint.X, edge.EndPoint.Y, 0.0);
                    if (
                        !IsFinitePoint(start)
                        || !IsFinitePoint(end)
                        || DistancePlate2D(start, end) <= PLATE_NUMERIC_EPSILON
                    )
                        continue;

                    AddUniquePlateProjectedPoint(points, start);
                    AddUniquePlateProjectedPoint(points, end);
                    AddUniquePlateProjectedSegment(segments, start, end);
                }
            }
            catch
            {
                return false;
            }

            return points.Count >= 3 && segments.Count >= 3;
        }

        private static void AddUniquePlateProjectedPoint(List<Point> points, Point candidate)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (DistancePlate2D(points[i], candidate) <= PLATE_NUMERIC_EPSILON * 100.0)
                    return;
            }
            points.Add(ClonePoint(candidate));
        }

        private static void AddUniquePlateProjectedSegment(
            List<ProjectedFrontSegment> segments,
            Point start,
            Point end
        )
        {
            double tolerance = PLATE_NUMERIC_EPSILON * 100.0;
            for (int i = 0; i < segments.Count; i++)
            {
                bool same =
                    DistancePlate2D(segments[i].Start, start) <= tolerance
                    && DistancePlate2D(segments[i].End, end) <= tolerance;
                bool reversed =
                    DistancePlate2D(segments[i].Start, end) <= tolerance
                    && DistancePlate2D(segments[i].End, start) <= tolerance;
                if (same || reversed)
                    return;
            }
            ProjectedFrontSegment segment = new ProjectedFrontSegment();
            segment.Start = ClonePoint(start);
            segment.End = ClonePoint(end);
            segments.Add(segment);
        }

        private static bool TryResolvePlateLongitudinalAxis(
            List<ProjectedFrontSegment> segments,
            out Point origin,
            out Point direction
        )
        {
            origin = null;
            direction = null;
            double bestLength = 0.0;
            Point bestStart = null;
            Point bestEnd = null;
            TrySelectSupportedProjectedAxis(
                segments,
                out bestStart,
                out bestEnd,
                out bestLength
            );

            if (
                bestStart == null
                || bestEnd == null
                || bestLength <= PLATE_NUMERIC_EPSILON
            )
                return false;

            double dx = (bestEnd.X - bestStart.X) / bestLength;
            double dy = (bestEnd.Y - bestStart.Y) / bestLength;
            if (dx < -0.000001 || (Math.Abs(dx) <= 0.000001 && dy < 0.0))
            {
                dx = -dx;
                dy = -dy;
            }

            origin = new Point(bestStart.X, bestStart.Y, 0.0);
            direction = new Point(dx, dy, 0.0);
            return IsFinitePoint(origin) && IsFinitePoint(direction);
        }

        private static bool TrySelectSupportedProjectedAxis(
            List<ProjectedFrontSegment> segments,
            out Point start,
            out Point end,
            out double length
        )
        {
            start = null;
            end = null;
            length = 0.0;
            List<PlateProjectedAxisCandidate> candidates =
                BuildProjectedAxisCandidates(segments);
            if (candidates.Count == 0)
                return false;
            PlateProjectedAxisCandidate selected = candidates[0];
            start = ClonePoint(selected.Origin);
            length = selected.RepresentativeLength;
            end = AddPlateVector(start, selected.Direction, length);
            return IsFinitePoint(start) && IsFinitePoint(end);
        }

        private static List<PlateProjectedAxisCandidate> BuildProjectedAxisCandidates(
            List<ProjectedFrontSegment> segments, bool includePartialEdges = false
        )
        {
            List<PlateProjectedAxisCandidate> result =
                new List<PlateProjectedAxisCandidate>();
            if (segments == null || segments.Count == 0)
                return result;

            double maximumLength = 0.0;
            for (int i = 0; i < segments.Count; i++)
                maximumLength = Math.Max(
                    maximumLength,
                    DistancePlate2D(segments[i].Start, segments[i].End)
                );
            if (maximumLength <= PLATE_NUMERIC_EPSILON)
                return result;

            double sinTolerance = Math.Sin(
                PLATE_PARALLEL_ANGLE_DEGREES * Math.PI / 180.0
            );
            for (int i = 0; i < segments.Count; i++)
            {
                ProjectedFrontSegment segment = segments[i];
                double segmentLength = DistancePlate2D(segment.Start, segment.End);
                if (segmentLength <= PLATE_NUMERIC_EPSILON
                    || (!includePartialEdges && segmentLength < maximumLength * 0.5))
                    continue;
                double dx = (segment.End.X - segment.Start.X) / segmentLength;
                double dy = (segment.End.Y - segment.Start.Y) / segmentLength;
                if (dx < -PLATE_NUMERIC_EPSILON
                    || (Math.Abs(dx) <= PLATE_NUMERIC_EPSILON && dy < 0.0))
                {
                    dx = -dx;
                    dy = -dy;
                }

                PlateProjectedAxisCandidate family = null;
                for (int j = 0; j < result.Count; j++)
                {
                    if (
                        Math.Abs(
                            result[j].Direction.X * dy
                            - result[j].Direction.Y * dx
                        ) <= sinTolerance
                    )
                    {
                        family = result[j];
                        break;
                    }
                }
                if (family == null)
                {
                    family = new PlateProjectedAxisCandidate();
                    family.Origin = ClonePoint(segment.Start);
                    family.Direction = new Point(dx, dy, 0.0);
                    family.RepresentativeLength = segmentLength;
                    result.Add(family);
                }
                family.SupportLength += segmentLength;
                if (segmentLength > family.RepresentativeLength)
                {
                    family.Origin = ClonePoint(segment.Start);
                    family.RepresentativeLength = segmentLength;
                }
            }

            result.Sort(
                delegate(
                    PlateProjectedAxisCandidate first,
                    PlateProjectedAxisCandidate second
                )
                {
                    int support = second.SupportLength.CompareTo(first.SupportLength);
                    if (support != 0)
                        return support;
                    return second.RepresentativeLength.CompareTo(
                        first.RepresentativeLength
                    );
                }
            );
            return result;
        }

        private static bool TryGetPlateProjectedExtents(
            List<Point> points,
            Point origin,
            Point direction,
            Point perpendicular,
            out double minT,
            out double maxT,
            out double minN,
            out double maxN
        )
        {
            minT = Double.PositiveInfinity;
            maxT = Double.NegativeInfinity;
            minN = Double.PositiveInfinity;
            maxN = Double.NegativeInfinity;
            if (points == null || origin == null || direction == null || perpendicular == null)
                return false;

            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (!IsFinitePoint(point))
                    continue;
                double t = PlateProjection(point, origin, direction);
                double n = PlateProjection(point, origin, perpendicular);
                minT = Math.Min(minT, t);
                maxT = Math.Max(maxT, t);
                minN = Math.Min(minN, n);
                maxN = Math.Max(maxN, n);
            }

            return IsFinite(minT)
                && IsFinite(maxT)
                && IsFinite(minN)
                && IsFinite(maxN)
                && maxT - minT > PLATE_NUMERIC_EPSILON
                && maxN - minN > PLATE_NUMERIC_EPSILON;
        }

        private static string ResolvePlateSectionName(bool oblique, bool hasStraightBevel)
        {
            return oblique && hasStraightBevel ? "B" : "A";
        }

        private static void OrientObliquePlateCutDownward(PlateAutoSectionAnalysisResult analysis)
        {
            if (analysis == null || analysis.LongitudinalDirection == null
                || !IsFinitePoint(analysis.CutStart) || !IsFinitePoint(analysis.CutEnd))
                return;

            double span = analysis.MaxLongitudinal - analysis.MinLongitudinal;
            Point axis = analysis.LongitudinalDirection;
            // Preserve horizontal/vertical edge families, including projection noise.
            if (!IsFinite(span) || span <= 0.0
                || Math.Min(Math.Abs(axis.X), Math.Abs(axis.Y)) * span <= 0.001)
                return;

            // Tekla's view arrow is the left normal (-dy, dx) of start -> end.
            // A negative cut dx makes the arrow look down in Front coordinates.
            // Test the cut itself: mirroring the bevel reverses the required order.
            // Swap endpoints only; the center station and plane remain identical.
            if (analysis.CutEnd.X > analysis.CutStart.X)
            {
                Point start = analysis.CutStart;
                analysis.CutStart = analysis.CutEnd;
                analysis.CutEnd = start;
            }
        }

        private static bool IsPlateSectionAxisAllowed(Point direction, double span)
        {
            return direction != null && IsFinitePoint(direction) && IsFinite(span)
                && span > PLATE_NUMERIC_EPSILON && Math.Abs(direction.X) * span > 0.001;
        }

        private static List<PlateLongitudinalWitness> FindPlateLongitudinalWitnesses(
            List<ProjectedFrontSegment> segments,
            PlateAutoSectionAnalysisResult analysis
        )
        {
            List<PlateLongitudinalWitness> result =
                new List<PlateLongitudinalWitness>();
            if (segments == null || analysis == null)
                return result;

            double length = analysis.MaxLongitudinal - analysis.MinLongitudinal;
            double width = analysis.MaxPerpendicular - analysis.MinPerpendicular;
            // Oblique projections of the same solid edge can differ by sub-micron
            // rounding. Keep this floor local to Front witness matching.
            double geometryTolerance = Math.Max(0.001, GetPlateAdaptiveTolerance(analysis));
            double minimumSpan = Math.Max(geometryTolerance * 4.0, analysis.Thickness * 2.0);
            double maximumOffset = width * PLATE_MAX_BOUNDARY_OFFSET_RATIO;
            double sinTolerance = Math.Sin(
                PLATE_PARALLEL_ANGLE_DEGREES * Math.PI / 180.0
            );

            for (int i = 0; i < segments.Count; i++)
            {
                ProjectedFrontSegment segment = segments[i];
                double segmentLength = DistancePlate2D(segment.Start, segment.End);
                if (segmentLength <= PLATE_NUMERIC_EPSILON)
                    continue;

                double segmentDx = (segment.End.X - segment.Start.X) / segmentLength;
                double segmentDy = (segment.End.Y - segment.Start.Y) / segmentLength;
                double parallelCross = Math.Abs(
                    segmentDx * analysis.LongitudinalDirection.Y
                    - segmentDy * analysis.LongitudinalDirection.X
                );
                if (parallelCross > sinTolerance)
                    continue;

                double startT = PlateProjection(
                    segment.Start,
                    analysis.AxisOrigin,
                    analysis.LongitudinalDirection
                );
                double endT = PlateProjection(
                    segment.End,
                    analysis.AxisOrigin,
                    analysis.LongitudinalDirection
                );
                double minT = Math.Min(startT, endT);
                double maxT = Math.Max(startT, endT);
                double span = maxT - minT;
                if (span < minimumSpan)
                    continue;

                double startN = PlateProjection(
                    segment.Start,
                    analysis.AxisOrigin,
                    analysis.PerpendicularDirection
                );
                double endN = PlateProjection(
                    segment.End,
                    analysis.AxisOrigin,
                    analysis.PerpendicularDirection
                );
                if (Math.Abs(startN - endN) > geometryTolerance)
                    continue;

                double level = (startN + endN) * 0.5;
                double offsetToMin = Math.Abs(level - analysis.MinPerpendicular);
                double offsetToMax = Math.Abs(analysis.MaxPerpendicular - level);
                int boundarySide = offsetToMax <= offsetToMin ? 1 : -1;
                double offset = Math.Min(offsetToMin, offsetToMax);
                if (
                    offset <= geometryTolerance
                    || offset > maximumOffset
                    || span < offset * 2.0
                )
                    continue;
                // A sloping outer contour can lie inside the whole-part bounds
                // without being an internal bevel edge. Require its paired
                // parallel exterior edge over the same longitudinal region.
                double boundary = boundarySide > 0 ? analysis.MaxPerpendicular : analysis.MinPerpendicular;
                if (!HasPlateParallelBoundarySupport(segments, analysis, boundary,
                    minT, maxT, minimumSpan, geometryTolerance)) continue;

                // This is a Front candidate, not sufficient proof of a bevel.
                // At rounded corners or partial edge treatments, its endpoints
                // can reach the outer boundary through several solid edges.
                // Requiring a direct endpoint-to-boundary edge rejects those
                // plates even when the exact center slice contains the bevel.
                // AnalyzePlateFrontBevelFamily must also prove a diagonal on
                // that same exterior boundary in TryGetPlateCenterBevelProof.

                bool duplicate = false;
                for (int j = 0; j < result.Count; j++)
                {
                    if (
                        result[j].BoundarySide == boundarySide
                        && Math.Abs(result[j].Level - level) <= geometryTolerance
                        && Math.Abs(result[j].MinT - minT) <= geometryTolerance
                        && Math.Abs(result[j].MaxT - maxT) <= geometryTolerance
                    )
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                    continue;

                PlateLongitudinalWitness witness = new PlateLongitudinalWitness();
                witness.Start = ClonePoint(segment.Start);
                witness.End = ClonePoint(segment.End);
                witness.Level = level;
                witness.Offset = offset;
                witness.MinT = minT;
                witness.MaxT = maxT;
                witness.BoundarySide = boundarySide;
                result.Add(witness);
            }

            result.Sort(
                delegate(PlateLongitudinalWitness first, PlateLongitudinalWitness second)
                {
                    int sideCompare = second.BoundarySide.CompareTo(first.BoundarySide);
                    return sideCompare != 0 ? sideCompare : first.Level.CompareTo(second.Level);
                }
            );
            return result;
        }

        private static bool HasPlateParallelBoundarySupport(List<ProjectedFrontSegment> segments,
            PlateAutoSectionAnalysisResult geometry, double boundary, double minT, double maxT,
            double minimumOverlap, double tolerance)
        {
            foreach (var segment in segments)
            {
                double aN = PlateProjection(segment.Start, geometry.AxisOrigin, geometry.PerpendicularDirection);
                double bN = PlateProjection(segment.End, geometry.AxisOrigin, geometry.PerpendicularDirection);
                if (Math.Abs(aN - boundary) > tolerance * 2.0
                    || Math.Abs(bN - boundary) > tolerance * 2.0) continue;
                double aT = PlateProjection(segment.Start, geometry.AxisOrigin, geometry.LongitudinalDirection);
                double bT = PlateProjection(segment.End, geometry.AxisOrigin, geometry.LongitudinalDirection);
                if (Math.Min(maxT, Math.Max(aT, bT)) - Math.Max(minT, Math.Min(aT, bT))
                    >= minimumOverlap) return true;
            }
            return false;
        }

        private static bool TryGetPlateCenterBevelProof(
            Solid solid,
            PlateAutoSectionAnalysisResult analysis,
            List<PlateLongitudinalWitness> witnesses,
            out List<Point> centerPolygon,
            out List<Point> bevelSegments
        )
        {
            centerPolygon = new List<Point>();
            bevelSegments = new List<Point>();
            if (solid == null || analysis == null)
                return false;

            try
            {
                double geometryTolerance = GetPlateAdaptiveTolerance(analysis);
                Point center = AddPlateVector(
                    analysis.AxisOrigin,
                    analysis.LongitudinalDirection,
                    analysis.MidLongitudinal
                );
                double padding = Math.Max(
                    geometryTolerance * 4.0,
                    (analysis.MaxPerpendicular - analysis.MinPerpendicular) * 0.02
                );
                Point p1 = AddPlateVector(
                    AddPlateVector(
                        center,
                        analysis.PerpendicularDirection,
                        analysis.MinPerpendicular - padding
                    ),
                    new Point(0.0, 0.0, 1.0),
                    solid.MinimumPoint.Z - padding
                );
                Point p2 = AddPlateVector(
                    AddPlateVector(
                        center,
                        analysis.PerpendicularDirection,
                        analysis.MaxPerpendicular + padding
                    ),
                    new Point(0.0, 0.0, 1.0),
                    solid.MinimumPoint.Z - padding
                );
                Point p3 = AddPlateVector(
                    AddPlateVector(
                        center,
                        analysis.PerpendicularDirection,
                        analysis.MinPerpendicular - padding
                    ),
                    new Point(0.0, 0.0, 1.0),
                    solid.MaximumPoint.Z + padding
                );

                centerPolygon = GetLargestPlateIntersectionPolygon(
                    solid.IntersectAllFaces(p1, p2, p3),
                    analysis
                );
                if (centerPolygon.Count < 3)
                    return false;

                for (int i = 0; i < centerPolygon.Count; i++)
                {
                    Point start = centerPolygon[i];
                    Point end = centerPolygon[(i + 1) % centerPolygon.Count];
                    double run = Math.Abs(end.X - start.X);
                    double rise = Math.Abs(end.Y - start.Y);
                    if (run < geometryTolerance || rise < geometryTolerance)
                        continue;
                    if (!IsPlateCenterDiagonalCorrelated(start, end, witnesses, analysis))
                        continue;
                    bevelSegments.Add(ClonePoint(start));
                    bevelSegments.Add(ClonePoint(end));
                }
            }
            catch
            {
                centerPolygon.Clear();
                bevelSegments.Clear();
                return false;
            }

            return bevelSegments.Count >= 2;
        }

        private static bool IsPlateCenterDiagonalCorrelated(
            Point start,
            Point end,
            List<PlateLongitudinalWitness> witnesses,
            PlateAutoSectionAnalysisResult analysis
        )
        {
            if (start == null || end == null || analysis == null)
                return false;
            if (witnesses == null || witnesses.Count == 0)
                return true;

            double minN = Math.Min(start.X, end.X);
            double maxN = Math.Max(start.X, end.X);
            // IntersectAllFaces and projected solid endpoints have independent
            // rounding. Use the same matching tolerance as the Front witness
            // extractor; a stricter slice check rejects valid oblique bevels.
            double geometryTolerance = Math.Max(0.001, GetPlateAdaptiveTolerance(analysis));
            for (int i = 0; i < witnesses.Count; i++)
            {
                PlateLongitudinalWitness witness = witnesses[i];
                if (witness.BoundarySide > 0)
                {
                    if (
                        Math.Abs(maxN - analysis.MaxPerpendicular) <= geometryTolerance * 2.0
                        && Math.Abs(minN - witness.Level) <= geometryTolerance * 2.0
                    )
                        return true;
                }
                else if (
                    Math.Abs(minN - analysis.MinPerpendicular) <= geometryTolerance * 2.0
                    && Math.Abs(maxN - witness.Level) <= geometryTolerance * 2.0
                )
                    return true;
            }

            return false;
        }

        private static List<Point> GetLargestPlateIntersectionPolygon(
            IEnumerator intersections,
            PlateAutoSectionAnalysisResult analysis
        )
        {
            List<List<Point>> lists = new List<List<Point>>();
            while (intersections != null && intersections.MoveNext())
                CollectPlatePointLists(intersections.Current, lists, 0);

            List<Point> best = new List<Point>();
            double bestScore = -1.0;
            for (int i = 0; i < lists.Count; i++)
            {
                List<Point> crossPoints = new List<Point>();
                for (int j = 0; j < lists[i].Count; j++)
                {
                    Point source = lists[i][j];
                    double n = PlateProjection(
                        source,
                        analysis.AxisOrigin,
                        analysis.PerpendicularDirection
                    );
                    AddUniquePlateCrossPoint(
                        crossPoints,
                        new Point(n, source.Z, 0.0),
                        GetPlateAdaptiveTolerance(analysis) * 0.1
                    );
                }

                crossPoints = NormalizePlateIntersectionPolygon(
                    crossPoints,
                    GetPlateAdaptiveTolerance(analysis) * 0.1
                );
                if (crossPoints.Count < 3)
                    continue;
                double minX = Double.PositiveInfinity;
                double maxX = Double.NegativeInfinity;
                double minY = Double.PositiveInfinity;
                double maxY = Double.NegativeInfinity;
                for (int j = 0; j < crossPoints.Count; j++)
                {
                    minX = Math.Min(minX, crossPoints[j].X);
                    maxX = Math.Max(maxX, crossPoints[j].X);
                    minY = Math.Min(minY, crossPoints[j].Y);
                    maxY = Math.Max(maxY, crossPoints[j].Y);
                }
                double score = Math.Abs(maxX - minX) * Math.Abs(maxY - minY);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = crossPoints;
                }
            }

            return best;
        }

        private static void CollectPlatePointLists(
            object value,
            List<List<Point>> result,
            int depth
        )
        {
            if (value == null || result == null || depth > 6)
                return;
            Point point = value as Point;
            if (point != null)
            {
                List<Point> one = new List<Point>();
                one.Add(ClonePoint(point));
                result.Add(one);
                return;
            }
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return;

            List<Point> direct = new List<Point>();
            foreach (object item in enumerable)
            {
                Point directPoint = item as Point;
                if (directPoint != null)
                    direct.Add(ClonePoint(directPoint));
                else
                    CollectPlatePointLists(item, result, depth + 1);
            }
            if (direct.Count > 0)
                result.Add(direct);
        }

        private static List<Point> NormalizePlateIntersectionPolygon(
            List<Point> points,
            double tolerance
        )
        {
            List<Point> result = new List<Point>();
            if (points == null)
                return result;
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (!IsFinitePoint(point))
                    continue;
                if (
                    result.Count == 0
                    || DistancePlate2D(result[result.Count - 1], point) > tolerance
                )
                    result.Add(ClonePoint(point));
            }
            if (
                result.Count > 2
                && DistancePlate2D(result[0], result[result.Count - 1])
                    <= tolerance
            )
                result.RemoveAt(result.Count - 1);
            return result;
        }

        private static void AddUniquePlateCrossPoint(
            List<Point> points,
            Point point,
            double tolerance
        )
        {
            if (points == null || point == null)
                return;
            for (int i = 0; i < points.Count; i++)
            {
                if (DistancePlate2D(points[i], point) <= tolerance)
                    return;
            }
            points.Add(point);
        }

        private static string DescribePlateFrontProof(
            List<PlateLongitudinalWitness> witnesses,
            PlateAutoSectionAnalysisResult analysis
        )
        {
            if (witnesses == null || witnesses.Count == 0)
                return "FrontProof=NONE";
            List<string> values = new List<string>();
            double plateLength = analysis.MaxLongitudinal - analysis.MinLongitudinal;
            for (int i = 0; i < witnesses.Count; i++)
            {
                PlateLongitudinalWitness witness = witnesses[i];
                double spanRatio = plateLength > 0.0
                    ? (witness.MaxT - witness.MinT) / plateLength
                    : 0.0;
                values.Add(
                    (witness.BoundarySide > 0 ? "UPPER" : "LOWER")
                    + " level="
                    + FormatDiagnosticNumber(witness.Level)
                    + " offset="
                    + FormatDiagnosticNumber(witness.Offset)
                    + " spanRatio="
                    + FormatDiagnosticNumber(spanRatio)
                );
            }
            return "FrontProof=HIGH[" + String.Join(";", values.ToArray()) + "]";
        }

        private static string DescribePlateCenterProof(
            List<Point> centerPolygon,
            List<Point> bevelSegments,
            bool hasCenterBevel
        )
        {
            int pointCount = centerPolygon == null ? 0 : centerPolygon.Count;
            int bevelCount = bevelSegments == null ? 0 : bevelSegments.Count / 2;
            return hasCenterBevel
                ? "CenterProof=HIGH[polygon="
                    + pointCount.ToString(CultureInfo.InvariantCulture)
                    + ",diagonal="
                    + bevelCount.ToString(CultureInfo.InvariantCulture)
                    + "]"
                : "CenterProof=RECTANGULAR_OR_NO_DIAGONAL[polygon="
                    + pointCount.ToString(CultureInfo.InvariantCulture)
                    + "]";
        }

        private static Point AddPlateVector(Point origin, Point direction, double amount)
        {
            return new Point(
                origin.X + direction.X * amount,
                origin.Y + direction.Y * amount,
                origin.Z + direction.Z * amount
            );
        }

        private static double PlateProjection(Point point, Point origin, Point direction)
        {
            return (point.X - origin.X) * direction.X
                + (point.Y - origin.Y) * direction.Y
                + (point.Z - origin.Z) * direction.Z;
        }

        private static double DistancePlate2D(Point first, Point second)
        {
            if (first == null || second == null)
                return Double.PositiveInfinity;
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double GetPlateAdaptiveTolerance(
            PlateAutoSectionAnalysisResult analysis
        )
        {
            if (analysis == null)
                return PLATE_MIN_ADAPTIVE_TOL;
            double width = Math.Abs(
                analysis.MaxPerpendicular - analysis.MinPerpendicular
            );
            // Numerical tolerance follows projected drawing size only. Plate
            // thickness never participates in the section trigger.
            double characteristic = width;
            if (!IsFinite(characteristic) || characteristic <= PLATE_NUMERIC_EPSILON)
                characteristic = 1.0;
            return Math.Max(
                PLATE_MIN_ADAPTIVE_TOL,
                Math.Min(
                    PLATE_MAX_ADAPTIVE_TOL,
                    characteristic * PLATE_ADAPTIVE_TOL_RATIO
                )
            );
        }

        private static bool TryClassifyExistingPlateCenterSection(
            Drawing drawing,
            ModelPart part,
            PlateAutoSectionAnalysisResult analysis,
            out DrawingView equivalent,
            out bool collision,
            out string message,
            Solid frontSolid
        )
        {
            equivalent = null;
            collision = false;
            message = "ExistingSection=NONE";
            int equivalentCount = 0;

            try
            {
                DrawingObjectEnumerator views = drawing == null || drawing.GetSheet() == null
                    ? null
                    : drawing.GetSheet().GetAllViews();
                while (views != null && views.MoveNext())
                {
                    DrawingView section = views.Current as DrawingView;
                    if (section == null || !IsSectionView(section))
                        continue;

                    bool nameA = String.Equals(
                        section.Name == null ? "" : section.Name.Trim(),
                        analysis.SectionName,
                        StringComparison.OrdinalIgnoreCase
                    );
                    string geometryMessage;
                    bool sameGeometry = IsEquivalentPlateCenterSectionGeometry(
                        section,
                        part,
                        analysis,
                        out geometryMessage,
                        frontSolid
                    );

                    if (nameA && !sameGeometry)
                    {
                        collision = true;
                        message =
                            "Section name " + analysis.SectionName + " da bi chiem boi geometry khac: "
                            + geometryMessage;
                        return true;
                    }
                    if (!nameA && sameGeometry)
                    {
                        collision = true;
                        message =
                            "Da co center section tuong duong nhung khong mang ten A; "
                            + "khong tao duplicate: "
                            + geometryMessage;
                        return true;
                    }
                    if (nameA && sameGeometry)
                    {
                        equivalent = section;
                        equivalentCount++;
                        message = "ExistingSection=EQUIVALENT_A[" + geometryMessage + "]";
                    }
                }

                if (equivalentCount > 1)
                {
                    collision = true;
                    equivalent = null;
                    message = "Co nhieu hon 1 Section A-A tuong duong; khong tiep tuc.";
                }

                return true;
            }
            catch (Exception ex)
            {
                message = "Khong audit duoc existing Section A: " + ex.Message;
                return false;
            }
        }

        private static bool IsEquivalentPlateCenterSectionGeometry(
            DrawingView section,
            ModelPart part,
            PlateAutoSectionAnalysisResult analysis,
            out string message,
            Solid frontSolid = null
        )
        {
            message = "";
            if (
                section == null
                || part == null
                || part.Identifier == null
                || analysis == null
                || analysis.FrontView == null
            )
            {
                message = "input null";
                return false;
            }
            if (!ViewContainsPart(section, part.Identifier))
            {
                message = "khong chua target Plate";
                return false;
            }

            try
            {
                CoordinateSystem frontCs = analysis.FrontView.DisplayCoordinateSystem;
                CoordinateSystem sectionCs = section.DisplayCoordinateSystem;
                if (
                    frontCs == null
                    || sectionCs == null
                    || frontCs.Origin == null
                    || sectionCs.Origin == null
                )
                {
                    message = "coordinate system null";
                    return false;
                }

                Point sectionOriginInFront = MatrixFactory
                    .ToCoordinateSystem(frontCs)
                    .Transform(sectionCs.Origin);
                double station = PlateProjection(
                    sectionOriginInFront,
                    analysis.AxisOrigin,
                    analysis.LongitudinalDirection
                );
                double plateLength =
                    analysis.MaxLongitudinal - analysis.MinLongitudinal;
                double stationTolerance = Math.Max(
                    PLATE_GEOMETRY_TOL * 2.0,
                    plateLength * 0.01
                );
                bool differentStation = Math.Abs(station - analysis.MidLongitudinal) > stationTolerance;

                Vector frontLongitudinalGlobal = AddScaledVectors(
                    frontCs.AxisX,
                    analysis.LongitudinalDirection.X,
                    frontCs.AxisY,
                    analysis.LongitudinalDirection.Y
                );
                Vector frontNormalGlobal = CrossVector(frontCs.AxisX, frontCs.AxisY);
                Vector sectionNormalGlobal = CrossVector(sectionCs.AxisX, sectionCs.AxisY);
                double angularCos = Math.Cos(
                    PLATE_PARALLEL_ANGLE_DEGREES * Math.PI / 180.0
                );
                if (
                    Math.Abs(NormalizedVectorDot(sectionNormalGlobal, frontLongitudinalGlobal))
                    < angularCos
                )
                {
                    message = "cut plane khong vuong goc truc doc Plate";
                    return false;
                }
                bool thicknessX = Math.Abs(NormalizedVectorDot(sectionCs.AxisX, frontNormalGlobal)) >= angularCos;
                bool thicknessY = Math.Abs(NormalizedVectorDot(sectionCs.AxisY, frontNormalGlobal)) >= angularCos;
                if (!thicknessX && !thicknessY)
                {
                    message = "Section axes do not represent true plate thickness";
                    return false;
                }
                // A manually positioned cut within the same constant bevel
                // region is equivalent even when it is not at our chosen
                // midpoint. Prove its exact profile instead of moving it or
                // creating a duplicate. Wrong directions were rejected above.
                string profileDiagnostic;
                if (differentStation && !HasEquivalentPlateBevelAtStation(frontSolid, analysis, station, out profileDiagnostic))
                {
                    message = "Existing cut station has a different bevel profile: "
                        + FormatDiagnosticNumber(station) + " " + profileDiagnostic;
                    return false;
                }

                double scale = GetViewScale(section);
                if (
                    !IsFinite(scale)
                    || Math.Abs(scale - analysis.FrontScale) > 0.01
                )
                {
                    message = "Section scale khong trung Front scale";
                    return false;
                }

                message =
                    "station="
                    + FormatDiagnosticNumber(station)
                    + ",mid="
                    + FormatDiagnosticNumber(analysis.MidLongitudinal)
                    + (thicknessX ? ",localX=thickness,scale=" : ",localY=thickness,scale=")
                    + FormatDiagnosticNumber(scale);
                return true;
            }
            catch (Exception ex)
            {
                message = "equivalence audit loi: " + ex.Message;
                return false;
            }
        }

        private static bool HasEquivalentPlateBevelAtStation(
            Solid solid, PlateAutoSectionAnalysisResult analysis, double station, out string diagnostic)
        {
            diagnostic = "No continuous Front witness.";
            try
            {
                // Reuse the analyzer's Solid in Front coordinates. Re-reading
                // or reselecting the part under a nested work plane would mix
                // the model object's cached coordinates with the slice frame.
                if (solid == null) return false;
                List<Point> points;
                List<ProjectedFrontSegment> segments;
                if (!TryCollectPlateProjectedSolidGeometry(solid, out points, out segments)) return false;
                var geometry = new PlateAutoSectionAnalysisResult {
                    AxisOrigin = analysis.AxisOrigin,
                    LongitudinalDirection = analysis.LongitudinalDirection,
                    PerpendicularDirection = analysis.PerpendicularDirection,
                    MinLongitudinal = analysis.MinLongitudinal, MaxLongitudinal = analysis.MaxLongitudinal,
                    MinPerpendicular = analysis.MinPerpendicular, MaxPerpendicular = analysis.MaxPerpendicular,
                    Thickness = analysis.Thickness, MidLongitudinal = analysis.MidLongitudinal
                };
                var witnesses = FindPlateLongitudinalWitnesses(segments, geometry);
                double margin = Math.Max(0.001, GetPlateAdaptiveTolerance(geometry)) * 2.0;
                // Require the same continuous witness region at both stations.
                witnesses.RemoveAll(w => Math.Min(station, analysis.MidLongitudinal) <= w.MinT + margin
                    || Math.Max(station, analysis.MidLongitudinal) >= w.MaxT - margin);
                if (witnesses.Count == 0) return false;
                List<Point> planned, actual, bevels;
                diagnostic = "Planned slice has no correlated bevel.";
                if (!TryGetPlateCenterBevelProof(solid, geometry, witnesses, out planned, out bevels)) return false;
                geometry.MidLongitudinal = station;
                diagnostic = "Existing slice has no correlated bevel.";
                if (!TryGetPlateCenterBevelProof(solid, geometry, witnesses, out actual, out bevels)) return false;
                diagnostic = "Slice polygon mismatch.";
                if (planned.Count != actual.Count) return false;
                double tolerance = Math.Max(0.01, GetPlateAdaptiveTolerance(geometry) * 2.0);
                foreach (Point point in planned)
                    if (!actual.Exists(p => DistancePlate2D(p, point) <= tolerance)) return false;
                return true;
            }
            catch (Exception ex) { diagnostic = ex.Message; return false; }
        }

        private static bool IsSectionView(DrawingView view)
        {
            try
            {
                return view != null
                    && view.ViewType.ToString().IndexOf(
                        "SectionView",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static Vector AddScaledVectors(
            Vector first,
            double firstScale,
            Vector second,
            double secondScale
        )
        {
            return new Vector(
                first.X * firstScale + second.X * secondScale,
                first.Y * firstScale + second.Y * secondScale,
                first.Z * firstScale + second.Z * secondScale
            );
        }

        private static Vector CrossVector(Vector first, Vector second)
        {
            return new Vector(
                first.Y * second.Z - first.Z * second.Y,
                first.Z * second.X - first.X * second.Z,
                first.X * second.Y - first.Y * second.X
            );
        }

        private static double NormalizedVectorDot(Vector first, Vector second)
        {
            if (first == null || second == null)
                return 0.0;
            double firstLength = Math.Sqrt(
                first.X * first.X + first.Y * first.Y + first.Z * first.Z
            );
            double secondLength = Math.Sqrt(
                second.X * second.X + second.Y * second.Y + second.Z * second.Z
            );
            if (firstLength <= 0.000001 || secondLength <= 0.000001)
                return 0.0;
            return (
                    first.X * second.X
                    + first.Y * second.Y
                    + first.Z * second.Z
                )
                / (firstLength * secondLength);
        }

        private static int CountSectionViews(Drawing drawing)
        {
            int count = 0;
            try
            {
                DrawingObjectEnumerator views = drawing == null || drawing.GetSheet() == null
                    ? null
                    : drawing.GetSheet().GetAllViews();
                while (views != null && views.MoveNext())
                {
                    if (IsSectionView(views.Current as DrawingView))
                        count++;
                }
            }
            catch
            {
                return -1;
            }
            return count;
        }

        private static double GetViewPaperWidth(DrawingView view)
        {
            try
            {
                if (view != null && IsFinite(view.Width) && view.Width > PLATE_GEOMETRY_TOL)
                    return view.Width;
            }
            catch { }
            return 0.0;
        }

        private static bool IsActivePlateDrawingIdentityUnchanged(
            Drawing expectedDrawing,
            ModelPart part,
            out string message
        )
        {
            message = "";
            try
            {
                DrawingHandler handler = new DrawingHandler();
                if (!handler.GetConnectionStatus())
                {
                    message = "DrawingHandler mat ket noi ngay truoc khi tao Plate A-A.";
                    return false;
                }
                SinglePartDrawing expected = expectedDrawing as SinglePartDrawing;
                SinglePartDrawing active = handler.GetActiveDrawing() as SinglePartDrawing;
                if (
                    expected == null
                    || active == null
                    || active.PartIdentifier == null
                    || part == null
                    || part.Identifier == null
                    || active.PartIdentifier.ID != part.Identifier.ID
                )
                {
                    message = "Active drawing identity da thay doi; huy tao Plate A-A.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                message = "Khong verify duoc active drawing identity: " + ex.Message;
                return false;
            }
        }

        private static bool CommitAndValidatePlateSection(
            Drawing drawing,
            ModelPart part,
            PlateAutoSectionAnalysisResult analysis,
            DrawingView sectionA,
            SectionMark markA,
            int beforeCount,
            int frontIdentifier,
            int topIdentifier,
            Point frontOrigin,
            Point topOrigin,
            DrawingView.ViewAttributes expectedViewAttributes,
            out string message
        )
        {
            message = "";
            if (!SafeCommit(drawing))
            {
                message = "Khong commit duoc Plate Section A-A de validate.";
                return false;
            }
            Thread.Sleep(150);

            int afterCount = CountSectionViews(drawing);
            if (beforeCount < 0 || afterCount != beforeCount + 1)
            {
                message =
                    "Plate post-create section count khong dung: before="
                    + beforeCount.ToString(CultureInfo.InvariantCulture)
                    + " after="
                    + afterCount.ToString(CultureInfo.InvariantCulture);
                return false;
            }
            if (
                sectionA == null
                || markA == null
                || !IsViewPresent(drawing, sectionA)
                || !ViewContainsPart(sectionA, part.Identifier)
                || !String.Equals(sectionA.Name, analysis.SectionName, StringComparison.OrdinalIgnoreCase)
            )
            {
                message = "Plate Section/Mark A-A khong materialize dung sau create.";
                return false;
            }

            string geometryMessage;
            if (
                !IsEquivalentPlateCenterSectionGeometry(
                    sectionA,
                    part,
                    analysis,
                    out geometryMessage
                )
            )
            {
                message = "Plate Section A post-create sai geometry: " + geometryMessage;
                return false;
            }

            string markName;
            if (!TryReadSectionMarkName(markA, out markName) || markName != analysis.SectionName)
            {
                message = "Section Mark name mismatch: expected=" + analysis.SectionName
                    + ", actual=" + markName;
                return false;
            }

            string labelMessage = "Expected View label attributes are null.";
            if (
                expectedViewAttributes == null
                || !ViewLabelMatches(
                    sectionA.Attributes,
                    expectedViewAttributes,
                    out labelMessage
                )
            )
            {
                message = "Plate Section A View label sai GEO_SECTION: " + labelMessage;
                return false;
            }

            DrawingView frontNow = FindViewByIdentifier(drawing, frontIdentifier);
            DrawingView topNow = FindViewByIdentifier(drawing, topIdentifier);
            if (
                frontNow == null
                || topNow == null
                || !SamePointWithin(frontNow.Origin, frontOrigin, 0.01)
                || !SamePointWithin(topNow.Origin, topOrigin, 0.01)
            )
            {
                message = "Plate worker da lam mat hoac di chuyen Front/Top; rollback A-A.";
                return false;
            }

            double frontRight = frontNow.Origin.X + GetViewPaperWidth(frontNow) * 0.5;
            if (
                !IsFinite(sectionA.Origin.X)
                || !IsFinite(sectionA.Origin.Y)
                || sectionA.Origin.X <= frontRight
                || Math.Abs(sectionA.Origin.Y - analysis.InsertionPoint.Y) > 1.0
            )
            {
                message =
                    "Plate Section A placement mismatch: actual=" + sectionA.Origin
                    + ", planned=" + analysis.InsertionPoint + ", frontRight="
                    + FormatDiagnosticNumber(frontRight) + "; rollback.";
                return false;
            }

            return true;
        }

        private static bool TryRepairExistingPlateSectionViewLabel(
            Drawing drawing,
            DrawingView sectionView,
            DrawingView.ViewAttributes standardViewAttributes,
            out string message,
            out bool rollbackSafe
        )
        {
            message = "";
            rollbackSafe = true;
            if (
                drawing == null
                || sectionView == null
                || standardViewAttributes == null
                || standardViewAttributes.TagsAttributes == null
                || standardViewAttributes.TagsAttributes.TagA1 == null
                || standardViewAttributes.TagsAttributes.TagA1.TagContent == null
            )
            {
                message = "Input sua View label Section A khong hop le.";
                return false;
            }

            try
            {
                sectionView.Select();
                DrawingView.ViewAttributes current = sectionView.Attributes;
                if (
                    current == null
                    || current.TagsAttributes == null
                    || current.TagsAttributes.TagA1 == null
                )
                {
                    message = "Section A hien huu khong expose View label TagA1.";
                    return false;
                }

                string expectedSignature = GetViewLabelSignature(standardViewAttributes);
                DrawingView.ViewAttributes originalAttributes = current;
                Point originalOrigin = ClonePoint(sectionView.Origin);
                sectionView.Attributes = standardViewAttributes;
                if (IsFinitePoint(originalOrigin))
                    sectionView.Origin = originalOrigin;

                if (!sectionView.Modify() || !SafeCommit(drawing))
                {
                    return RollbackExistingPlateViewLabel(
                        drawing,
                        sectionView,
                        originalAttributes,
                        "Modify/Commit View label Section A that bai.",
                        out message,
                        out rollbackSafe
                    );
                }

                Thread.Sleep(100);
                sectionView.Select();
                string readBackMessage;
                if (!ViewLabelMatches(sectionView.Attributes, standardViewAttributes, out readBackMessage))
                {
                    return RollbackExistingPlateViewLabel(
                        drawing,
                        sectionView,
                        originalAttributes,
                        "Read-back View label Section A sai: " + readBackMessage,
                        out message,
                        out rollbackSafe
                    );
                }

                message =
                    "Section A View label da dong bo tu "
                    + SECTION_MARK_ATTRIBUTE_NAME
                    + " ("
                    + expectedSignature
                    + ").";
                return true;
            }
            catch (Exception ex)
            {
                message = "Sua View label Section A loi: " + ex.Message;
                return false;
            }
        }

        private static bool RollbackExistingPlateViewLabel(
            Drawing drawing,
            DrawingView sectionView,
            DrawingView.ViewAttributes originalAttributes,
            string failure,
            out string message,
            out bool rollbackSafe
        )
        {
            rollbackSafe = false;
            message = failure;
            try
            {
                sectionView.Select();
                sectionView.Attributes = originalAttributes;
                bool modified = sectionView.Modify();
                bool committed = SafeCommit(drawing);
                rollbackSafe = modified && committed;
            }
            catch
            {
                rollbackSafe = false;
            }

            message += rollbackSafe
                ? " Label cu da duoc phuc hoi."
                : " Khong chung minh duoc rollback label; KHONG SAVE.";
            return false;
        }

        private static bool ViewLabelMatches(
            DrawingView.ViewAttributes actual,
            DrawingView.ViewAttributes expected,
            out string message
        )
        {
            string actualSignature = GetViewLabelSignature(actual);
            string expectedSignature = GetViewLabelSignature(expected);
            if (String.IsNullOrWhiteSpace(expectedSignature))
            {
                message = "Expected View label signature rong.";
                return false;
            }
            if (!String.Equals(actualSignature, expectedSignature, StringComparison.Ordinal))
            {
                message =
                    "actual=[" + actualSignature + "] expected=[" + expectedSignature + "]";
                return false;
            }
            if (
                actual.MarkSymbolColor != expected.MarkSymbolColor
                || actual.MarkSymbolAttributes == null
                || expected.MarkSymbolAttributes == null
                || actual.MarkSymbolAttributes.Shape != expected.MarkSymbolAttributes.Shape
                || Math.Abs(
                    actual.MarkSymbolAttributes.Size - expected.MarkSymbolAttributes.Size
                ) > 0.001
                || actual.MarkSymbolAttributes.LineLengthType
                    != expected.MarkSymbolAttributes.LineLengthType
                || Math.Abs(
                    actual.MarkSymbolAttributes.LineLength
                        - expected.MarkSymbolAttributes.LineLength
                ) > 0.001
                || actual.LabelPositionVertical != expected.LabelPositionVertical
                || actual.LabelPositionHorizontal != expected.LabelPositionHorizontal
            )
            {
                message = "View label symbol/line/position khong trung GEO_SECTION.";
                return false;
            }
            message = expectedSignature;
            return true;
        }

        private static string GetViewLabelSignature(DrawingView.ViewAttributes attributes)
        {
            try
            {
                if (attributes == null || attributes.TagsAttributes == null)
                    return "";

                List<string> parts = new List<string>();
                for (int tagIndex = 1; tagIndex <= 5; tagIndex++)
                {
                    DrawingView.ViewMarkTagAttributes tag = GetViewLabelTag(
                        attributes.TagsAttributes,
                        tagIndex
                    );
                    if (tag == null)
                        continue;
                    parts.Add(
                        "TAG"
                        + tagIndex.ToString(CultureInfo.InvariantCulture)
                        + "|LOC="
                        + tag.Location.ToString()
                        + "|ALIGN="
                        + tag.TagAlignment.ToString()
                        + "|OFFSET="
                        + tag.Offset.X.ToString("0.###", CultureInfo.InvariantCulture)
                        + ","
                        + tag.Offset.Y.ToString("0.###", CultureInfo.InvariantCulture)
                    );
                    IEnumerable elements = tag.TagContent as IEnumerable;
                    if (elements == null)
                        continue;
                    foreach (object value in elements)
                    {
                        PropertyElement property = value as PropertyElement;
                        TextElement text = value as TextElement;
                        FontAttributes font = property != null
                            ? property.Font
                            : text != null ? text.Font : null;
                        if (font == null)
                            continue;
                        string kind = property != null
                            ? "PROPERTY:"
                                + GetPropertyElementSubtype(property).ToString(
                                    CultureInfo.InvariantCulture
                                )
                            : "TEXT:" + (text.Value ?? "");
                        parts.Add(
                            kind
                            + "|"
                            + (font.Name ?? "")
                            + "|"
                            + font.Height.ToString("0.###", CultureInfo.InvariantCulture)
                            + "|"
                            + font.Color.ToString()
                            + "|"
                            + font.Bold.ToString()
                            + "|"
                            + font.Italic.ToString()
                        );
                    }
                }
                return String.Join(";", parts.ToArray());
            }
            catch
            {
                return "";
            }
        }

        private static int GetPropertyElementSubtype(PropertyElement property)
        {
            if (property == null || property.PropertyType == null)
                return -1;
            try
            {
                PropertyInfo valueProperty = property.PropertyType.GetType().GetProperty(
                    "PropertyType",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                object value = valueProperty == null
                    ? null
                    : valueProperty.GetValue(property.PropertyType, null);
                return value == null
                    ? -1
                    : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }

        private static bool TryReadSectionMarkName(SectionMark mark, out string markName)
        {
            markName = "";
            if (mark == null)
                return false;
            try
            {
                mark.Select();
                object attributes = mark.Attributes;
                PropertyInfo property = attributes == null
                    ? null
                    : attributes.GetType().GetProperty(
                        "MarkName",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                if (property == null || !property.CanRead)
                    return false;
                markName = Convert.ToString(
                    property.GetValue(attributes, null),
                    CultureInfo.InvariantCulture
                );
                return !String.IsNullOrWhiteSpace(markName);
            }
            catch
            {
                return false;
            }
        }

        private static AutoSectionWorkerResult FinishPlateCreateFailure(
            Drawing drawing,
            string message,
            DrawingView sectionA,
            SectionMark markA,
            DrawingView frontView,
            DrawingView topView
        )
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            result.IsPlateSection = true;
            result.Message = message;
            result.SectionA = sectionA;
            result.MarkA = markA;
            if (sectionA == null && markA == null)
            {
                result.Status = AutoSectionWorkerStatus.CreateFailed;
                return result;
            }

            int markIdentifier = GetDrawingObjectIdentifier(markA);
            bool deleteReturned = true;
            if (markA != null)
                deleteReturned = SafeDelete(markA) && deleteReturned;
            if (sectionA != null)
                deleteReturned = SafeDelete(sectionA) && deleteReturned;
            bool commitReturned = SafeCommit(drawing);
            Thread.Sleep(100);
            bool viewRemoved = sectionA == null || !IsViewPresent(drawing, sectionA);
            bool markRemoved =
                markA == null || !IsSectionMarkPresent(drawing, markIdentifier);
            bool stableViews = IsViewPresent(drawing, frontView) && IsViewPresent(drawing, topView);

            if (deleteReturned && commitReturned && viewRemoved && markRemoved && stableViews)
            {
                result.Status = AutoSectionWorkerStatus.RolledBack;
                result.Message += " Section/Mark A vua tao da rollback; Front/Top duoc giu.";
                return result;
            }

            result.Status = AutoSectionWorkerStatus.UnsafeRollbackFailed;
            result.IsSafeToContinue = false;
            result.Message += " Rollback A-A khong duoc chung minh an toan; KHONG SAVE.";
            return result;
        }

        private static int GetDrawingObjectIdentifier(DrawingObject drawingObject)
        {
            if (drawingObject == null)
                return 0;
            string[] names = new string[]
            {
                "Identifier",
                "DrawingIdentifier",
                "ViewIdentifier"
            };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo property = drawingObject.GetType().GetProperty(
                        names[i],
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                    Identifier identifier = property == null
                        ? null
                        : property.GetValue(drawingObject, null) as Identifier;
                    if (identifier != null && identifier.ID > 0)
                        return identifier.ID;
                }
                catch { }
            }
            return 0;
        }

        private static bool IsSectionMarkPresent(Drawing drawing, int identifier)
        {
            if (drawing == null || identifier <= 0)
                return false;
            try
            {
                DrawingObjectEnumerator marks = drawing.GetSheet().GetAllObjects(
                    typeof(SectionMark)
                );
                while (marks != null && marks.MoveNext())
                {
                    SectionMark mark = marks.Current as SectionMark;
                    if (GetDrawingObjectIdentifier(mark) == identifier)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static DrawingView FindViewByIdentifier(Drawing drawing, int identifier)
        {
            if (drawing == null || identifier <= 0)
                return null;
            try
            {
                DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
                while (views != null && views.MoveNext())
                {
                    DrawingView view = views.Current as DrawingView;
                    if (GetViewIdentifier(view) == identifier)
                        return view;
                }
            }
            catch { }
            return null;
        }

        private static bool SamePointWithin(Point first, Point second, double tolerance)
        {
            if (first == null || second == null)
                return false;
            return Math.Abs(first.X - second.X) <= tolerance
                && Math.Abs(first.Y - second.Y) <= tolerance
                && Math.Abs(first.Z - second.Z) <= tolerance;
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
            out string message
        )
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

            if (!TryGetSectionGeometry(model, part, frontView, out geometry, out message))
                return false;

            if (
                !TryLoadSectionAttributes(
                    "B",
                    frontScale,
                    sectionViewAttributeName,
                    out attributesB,
                    out message
                )
            )
                return false;
            if (
                !TryLoadSectionAttributes(
                    "C",
                    frontScale,
                    sectionViewAttributeName,
                    out attributesC,
                    out message
                )
            )
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
            out string message
        )
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

            if (!TryGetSectionGeometry(model, part, frontView, out geometry, out message))
                return false;

            if (
                !TryLoadSectionAttributes(
                    "B",
                    frontScale,
                    sectionViewAttributeName,
                    out attributesB,
                    out message
                )
            )
                return false;

            return true;
        }

        private static bool ValidateCommonInput(
            Drawing drawing,
            Model model,
            ModelPart part,
            DrawingView topView,
            DrawingView frontView,
            out string message
        )
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

            if (
                topView == null
                || frontView == null
                || System.Object.ReferenceEquals(topView, frontView)
            )
            {
                message = "TopView/FrontView khong hop le.";
                return false;
            }

            if (!IsViewPresent(drawing, topView) || !IsViewPresent(drawing, frontView))
            {
                message = "TopView hoac FrontView khong thuoc drawing hien tai.";
                return false;
            }

            if (
                !ViewContainsPart(topView, part.Identifier)
                || !ViewContainsPart(frontView, part.Identifier)
            )
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
            out string message
        )
        {
            geometry = new SectionGeometry();
            message = "";
            TransformationPlane oldPlane = null;
            ResetGeometryDiagnostic();

            string profileText;
            string normalizedProfile;
            AutoSectionProfileKind profileKind;

            if (
                !TryClassifyAutoSectionProfile(
                    part,
                    out profileText,
                    out normalizedProfile,
                    out profileKind,
                    out message
                )
            )
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
                model
                    .GetWorkPlaneHandler()
                    .SetCurrentTransformationPlane(
                        new TransformationPlane(frontView.DisplayCoordinateSystem)
                    );

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

                if (
                    !IsFinite(minX)
                    || !IsFinite(maxX)
                    || !IsFinite(minY)
                    || !IsFinite(maxY)
                    || maxX - minX <= TOL
                    || maxY - minY <= TOL
                )
                {
                    message = "Solid cua ModelPart khong co extents hop le trong FrontView.";
                    return false;
                }

                AddGeometryDiagnostic(
                    "FrontExtents="
                        + FormatDiagnosticNumber(minX)
                        + ","
                        + FormatDiagnosticNumber(minY)
                        + " -> "
                        + FormatDiagnosticNumber(maxX)
                        + ","
                        + FormatDiagnosticNumber(maxY)
                );

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
                        "LegacyFlangeThickness=" + FormatDiagnosticNumber(legacyFlangeThickness)
                    );
                }

                FrontNotchGeometry notchGeometry;
                FrontNotchDetectionStatus notchStatus = TryGetFrontNotchGeometry(
                    solid,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    out notchGeometry
                );

                AddGeometryDiagnostic("NotchStatus=" + notchStatus);

                double bCutY;
                double cCutY;
                double bDepth;
                double cDepth;

                if (profileKind == AutoSectionProfileKind.ShapeCOrdinary)
                {
                    if (
                        notchStatus == FrontNotchDetectionStatus.Failed
                        || notchStatus == FrontNotchDetectionStatus.NotChecked
                    )
                    {
                        message =
                            "Khong validate duoc notch cua Shape C thong thuong; "
                            + "dung o Preflight.";
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    List<Point> projectedPoints;
                    List<ProjectedFrontSegment> projectedSegments;
                    if (
                        !TryCollectProjectedFrontSolidGeometry(
                            solid,
                            out projectedPoints,
                            out projectedSegments
                        )
                    )
                    {
                        message = "Khong doc duoc canh Solid that cua Shape C trong FrontView.";
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    AddGeometryDiagnostic(
                        "ProjectedGeometry points="
                            + projectedPoints.Count
                            + " segments="
                            + projectedSegments.Count
                    );

                    CFlangeGeometry flangeGeometry;
                    if (
                        !TryResolveOrdinaryCFlangeGeometry(
                            projectedSegments,
                            minX,
                            maxX,
                            minY,
                            maxY,
                            out flangeGeometry,
                            out message
                        )
                    )
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }

                    AddGeometryDiagnostic("COpeningSide=" + flangeGeometry.OpeningSide);
                    AddGeometryDiagnostic(
                        "CFlangeEdges outerTop="
                            + FormatDiagnosticNumber(flangeGeometry.OuterTopY)
                            + " innerTop="
                            + FormatDiagnosticNumber(flangeGeometry.InnerTopY)
                            + " innerBottom="
                            + FormatDiagnosticNumber(flangeGeometry.InnerBottomY)
                            + " outerBottom="
                            + FormatDiagnosticNumber(flangeGeometry.OuterBottomY)
                    );

                    if (
                        !TryResolveOrdinaryCSectionDepthFromNotches(
                            minY,
                            maxY,
                            flangeGeometry,
                            notchStatus,
                            notchGeometry,
                            out bCutY,
                            out bDepth,
                            out cCutY,
                            out cDepth,
                            out message
                        )
                    )
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }
                }
                else
                {
                    if (
                        !TryResolveSectionDepthFromNotches(
                            minY,
                            maxY,
                            legacyFlangeThickness,
                            notchStatus,
                            notchGeometry,
                            out bCutY,
                            out bDepth,
                            out cCutY,
                            out cDepth,
                            out message
                        )
                    )
                    {
                        AddGeometryDiagnostic(message);
                        return false;
                    }
                }

                AddGeometryDiagnostic(
                    "FinalSection B(cutY="
                        + FormatDiagnosticNumber(bCutY)
                        + ",depth="
                        + FormatDiagnosticNumber(bDepth)
                        + ") C(cutY="
                        + FormatDiagnosticNumber(cCutY)
                        + ",depth="
                        + FormatDiagnosticNumber(cDepth)
                        + ")"
                );

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
                catch { }
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
            out string message
        )
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
                message = "Khong doc duoc PROFILE hoac Profile.ProfileString de Auto Section.";
                return false;
            }

            if (
                normalizedProfile.StartsWith("BH")
                || normalizedProfile.StartsWith("RH")
                || normalizedProfile.StartsWith("HM")
                || normalizedProfile.StartsWith("HN")
                || normalizedProfile.StartsWith("HW")
                || normalizedProfile.StartsWith("H")
                || normalizedProfile.StartsWith("I")
            )
            {
                kind = AutoSectionProfileKind.ShapeIH;
                return true;
            }

            if (normalizedProfile.StartsWith("["))
            {
                kind = AutoSectionProfileKind.ShapeCBracket;
                return true;
            }

            if (
                normalizedProfile.StartsWith("CH")
                || normalizedProfile.StartsWith("CHANNEL")
                || normalizedProfile.StartsWith("C")
            )
            {
                kind = AutoSectionProfileKind.ShapeCOrdinary;
                return true;
            }

            message =
                "Profile khong thuoc I/H, Shape [ hoac Shape C duoc Auto Section ho tro: "
                + profileText;
            return false;
        }

        private static string GetAutoSectionProfileText(ModelPart part)
        {
            try
            {
                string profile = "";
                if (
                    part != null
                    && part.GetReportProperty("PROFILE", ref profile)
                    && !String.IsNullOrWhiteSpace(profile)
                )
                    return profile.Trim();
            }
            catch { }

            try
            {
                if (part == null)
                    return "";

                PropertyInfo profileProperty = part.GetType()
                    .GetProperty(
                        "Profile",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                object profileObject =
                    profileProperty != null && profileProperty.CanRead
                        ? profileProperty.GetValue(part, null)
                        : null;

                if (profileObject == null)
                    return "";

                PropertyInfo profileStringProperty = profileObject
                    .GetType()
                    .GetProperty(
                        "ProfileString",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );

                object value =
                    profileStringProperty != null && profileStringProperty.CanRead
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
            out string message
        )
        {
            geometry = null;
            message = "";

            try
            {
                if (segments == null || segments.Count < 4)
                {
                    message = "Shape C khong co du canh Solid that de tim hai mep trong.";
                    return false;
                }

                double width = maxX - minX;
                double height = maxY - minY;
                double edgeTol = Math.Max(2.0, TOL + 1.0);

                if (!IsFinite(width) || !IsFinite(height) || width <= edgeTol || height <= edgeTol)
                {
                    message = "Extents Shape C khong hop le de phan tich mep canh.";
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

                if (
                    !TrySelectCFlangeInnerLevel(
                        horizontalLevels,
                        segments,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        true,
                        edgeTol,
                        out innerTopY,
                        out topCoverage
                    )
                )
                {
                    message = "Khong tim duoc mep trong phia duoi cua canh tren Shape C.";
                    return false;
                }

                if (
                    !TrySelectCFlangeInnerLevel(
                        horizontalLevels,
                        segments,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        false,
                        edgeTol,
                        out innerBottomY,
                        out bottomCoverage
                    )
                )
                {
                    message = "Khong tim duoc mep trong phia tren cua canh duoi Shape C.";
                    return false;
                }

                double topFlangeDepth = maxY - innerTopY;
                double bottomFlangeDepth = innerBottomY - minY;

                if (
                    !IsFinite(topFlangeDepth)
                    || !IsFinite(bottomFlangeDepth)
                    || topFlangeDepth <= TOL
                    || bottomFlangeDepth <= TOL
                    || topFlangeDepth >= height * 0.45
                    || bottomFlangeDepth >= height * 0.45
                    || innerTopY <= innerBottomY + edgeTol
                )
                {
                    message = "Hai mep trong Shape C khong tao thanh hai canh tren/duoi hop le.";
                    return false;
                }

                double minimumCoverage = Math.Max(5.0, width * 0.15);
                if (topCoverage < minimumCoverage || bottomCoverage < minimumCoverage)
                {
                    message = "Do dai canh that tai hai mep trong Shape C khong du de validate.";
                    return false;
                }

                geometry = new CFlangeGeometry();
                geometry.OuterTopY = maxY;
                geometry.OuterBottomY = minY;
                geometry.InnerTopY = innerTopY;
                geometry.InnerBottomY = innerBottomY;

                COpeningSide openingSide;
                if (
                    !TryResolveOrdinaryCOpeningTopology(
                        segments,
                        minX,
                        maxX,
                        minY,
                        maxY,
                        edgeTol,
                        out openingSide,
                        out message
                    )
                )
                {
                    geometry = null;
                    return false;
                }

                geometry.OpeningSide = openingSide;

                AddGeometryDiagnostic(
                    "CFlangeCoverage top="
                        + FormatDiagnosticNumber(topCoverage)
                        + " bottom="
                        + FormatDiagnosticNumber(bottomCoverage)
                );

                return true;
            }
            catch (Exception ex)
            {
                message = "Phan tich mep canh Shape C loi: " + ex.Message;
                return false;
            }
        }

        private static void AddUniqueCoordinate(List<double> values, double value, double tolerance)
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
            out double selectedCoverage
        )
        {
            selectedY = 0.0;
            selectedCoverage = 0.0;

            if (levels == null || levels.Count == 0)
                return false;

            double middleY = (minY + maxY) * 0.5;
            double bestCoverage = 0.0;

            foreach (double y in levels)
            {
                if ((topSide && y <= middleY + tolerance) || (!topSide && y >= middleY - tolerance))
                    continue;

                double coverage = GetHorizontalCoverageAtY(segments, y, minX, maxX, tolerance);

                if (coverage > bestCoverage)
                    bestCoverage = coverage;
            }

            if (bestCoverage <= TOL)
                return false;

            double minimumCoverage = Math.Max(
                Math.Max(5.0, (maxX - minX) * 0.15),
                bestCoverage * 0.70
            );
            bool found = false;

            foreach (double y in levels)
            {
                if ((topSide && y <= middleY + tolerance) || (!topSide && y >= middleY - tolerance))
                    continue;

                double coverage = GetHorizontalCoverageAtY(segments, y, minX, maxX, tolerance);

                if (coverage < minimumCoverage)
                    continue;

                if (!found || (topSide && y > selectedY) || (!topSide && y < selectedY))
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
            double tolerance
        )
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

                if (dx <= tolerance || dy > tolerance || Math.Abs(y - targetY) > tolerance)
                    continue;

                ProjectedInterval interval = new ProjectedInterval();
                interval.Min = Math.Max(minX, Math.Min(segment.Start.X, segment.End.X));
                interval.Max = Math.Min(maxX, Math.Max(segment.Start.X, segment.End.X));

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
            double tolerance
        )
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

                if (dy <= tolerance || dx > tolerance || Math.Abs(x - targetX) > tolerance)
                    continue;

                ProjectedInterval interval = new ProjectedInterval();
                interval.Min = Math.Max(minY, Math.Min(segment.Start.Y, segment.End.Y));
                interval.Max = Math.Min(maxY, Math.Max(segment.Start.Y, segment.End.Y));

                if (interval.Max - interval.Min > TOL)
                    intervals.Add(interval);
            }

            return GetMergedIntervalCoverage(intervals, tolerance);
        }

        private static double GetMergedIntervalCoverage(
            List<ProjectedInterval> intervals,
            double tolerance
        )
        {
            if (intervals == null || intervals.Count == 0)
                return 0.0;

            intervals.Sort(
                delegate(ProjectedInterval first, ProjectedInterval second)
                {
                    return first.Min.CompareTo(second.Min);
                }
            );

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
            out string message
        )
        {
            openingSide = COpeningSide.Unknown;
            message = "";

            double sideTolerance = Math.Max(edgeTolerance, (maxX - minX) * 0.03);
            double leftCoverage = GetVerticalCoverageAtX(segments, minX, minY, maxY, sideTolerance);
            double rightCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                minY,
                maxY,
                sideTolerance
            );
            double minimumDifference = Math.Max(2.0, (maxY - minY) * 0.05);

            AddGeometryDiagnostic(
                "CVerticalCoverage left="
                    + FormatDiagnosticNumber(leftCoverage)
                    + " right="
                    + FormatDiagnosticNumber(rightCoverage)
            );

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
                sideTolerance
            );
            double leftUpperCoverage = GetVerticalCoverageAtX(
                segments,
                minX,
                middleY,
                maxY,
                sideTolerance
            );
            double rightLowerCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                minY,
                middleY,
                sideTolerance
            );
            double rightUpperCoverage = GetVerticalCoverageAtX(
                segments,
                maxX,
                middleY,
                maxY,
                sideTolerance
            );

            AddGeometryDiagnostic(
                "CSplitCoverage left(lower="
                    + FormatDiagnosticNumber(leftLowerCoverage)
                    + ",upper="
                    + FormatDiagnosticNumber(leftUpperCoverage)
                    + ") right(lower="
                    + FormatDiagnosticNumber(rightLowerCoverage)
                    + ",upper="
                    + FormatDiagnosticNumber(rightUpperCoverage)
                    + ")"
            );

            if (Math.Max(leftCoverage, rightCoverage) >= partHeight * 0.75)
            {
                message =
                    "Hinh hoc C co canh dung lien tuc gan het chieu cao; "
                    + "khong du chac chan de xu ly nhu C thong thuong thay vi Shape [.";
                return false;
            }

            double minimumHalfCoverage = Math.Max(2.0, partHeight * 0.01);
            bool hasLeftSplit =
                leftLowerCoverage >= minimumHalfCoverage
                && leftUpperCoverage >= minimumHalfCoverage;
            bool hasRightSplit =
                rightLowerCoverage >= minimumHalfCoverage
                && rightUpperCoverage >= minimumHalfCoverage;

            if (!hasLeftSplit && !hasRightSplit)
            {
                message =
                    "Hinh hoc C khong co du hai doan canh dung tren/duoi "
                    + "de xac nhan C thong thuong.";
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
            out FrontNotchGeometry geometry
        )
        {
            geometry = new FrontNotchGeometry();

            try
            {
                List<Point> points;
                List<ProjectedFrontSegment> segments;

                if (!TryCollectProjectedFrontSolidGeometry(solid, out points, out segments))
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
                    geometry
                );

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
            out List<ProjectedFrontSegment> segments
        )
        {
            points = new List<Point>();
            segments = new List<ProjectedFrontSegment>();

            if (solid == null)
                return false;

            int geometryItemCount = 0;

            try
            {
                Tekla.Structures.Solid.FaceEnumerator faces = solid.GetFaceEnumerator();

                while (faces != null && faces.MoveNext())
                {
                    geometryItemCount++;
                    if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                        return false;

                    Tekla.Structures.Solid.Face face = faces.Current;
                    if (face == null)
                        continue;

                    Tekla.Structures.Solid.LoopEnumerator loops = face.GetLoopEnumerator();

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

                            if (!TryAddUniqueProjectedPoint(points, vertices.Current))
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
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();

                while (edges != null && edges.MoveNext())
                {
                    geometryItemCount++;
                    if (geometryItemCount > SECTION_NOTCH_MAX_GEOMETRY_ITEMS)
                        return false;

                    Tekla.Structures.Solid.Edge edge = edges.Current as Tekla.Structures.Solid.Edge;

                    if (edge == null || edge.StartPoint == null || edge.EndPoint == null)
                        continue;

                    Point start = new Point(edge.StartPoint.X, edge.StartPoint.Y, 0.0);
                    Point end = new Point(edge.EndPoint.X, edge.EndPoint.Y, 0.0);

                    if (
                        !TryAddUniqueProjectedPoint(points, start)
                        || !TryAddUniqueProjectedPoint(points, end)
                        || !TryAddUniqueProjectedSegment(segments, start, end)
                    )
                        return false;
                }
            }
            catch
            {
                return false;
            }

            return points.Count >= 4 && segments.Count >= 4;
        }

        private static bool TryAddUniqueProjectedPoint(List<Point> points, Point point)
        {
            if (points == null)
                return false;

            if (point == null || !IsFinite(point.X) || !IsFinite(point.Y))
                return true;

            foreach (Point current in points)
            {
                if (AreProjectedPointsNear(current, point, SECTION_NOTCH_POINT_MERGE_TOL))
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
            Point end
        )
        {
            if (segments == null || start == null || end == null)
                return false;

            if (!IsFinite(start.X) || !IsFinite(start.Y) || !IsFinite(end.X) || !IsFinite(end.Y))
                return true;

            if (AreProjectedPointsNear(start, end, SECTION_NOTCH_POINT_MERGE_TOL))
                return true;

            foreach (ProjectedFrontSegment current in segments)
            {
                bool sameDirection =
                    AreProjectedPointsNear(current.Start, start, SECTION_NOTCH_POINT_MERGE_TOL)
                    && AreProjectedPointsNear(current.End, end, SECTION_NOTCH_POINT_MERGE_TOL);

                bool oppositeDirection =
                    AreProjectedPointsNear(current.Start, end, SECTION_NOTCH_POINT_MERGE_TOL)
                    && AreProjectedPointsNear(current.End, start, SECTION_NOTCH_POINT_MERGE_TOL);

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

        private static bool AreProjectedPointsNear(Point first, Point second, double tolerance)
        {
            return first != null
                && second != null
                && Math.Abs(first.X - second.X) <= tolerance
                && Math.Abs(first.Y - second.Y) <= tolerance;
        }

        private static FrontNotchDetectionStatus TryDetectFrontCornerNotches(
            List<Point> points,
            List<ProjectedFrontSegment> segments,
            double minX,
            double maxX,
            double minY,
            double maxY,
            FrontNotchGeometry geometry
        )
        {
            try
            {
                if (
                    geometry == null
                    || points == null
                    || segments == null
                    || points.Count < 4
                    || segments.Count < 4
                )
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
                    out geometry.TopLeftInner
                );

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
                    out geometry.TopRightInner
                );

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
                    out geometry.BottomLeftInner
                );

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
                    out geometry.BottomRightInner
                );

                geometry.HasAnyTopNotch = geometry.HasTopLeft || geometry.HasTopRight;
                geometry.HasAnyBottomNotch = geometry.HasBottomLeft || geometry.HasBottomRight;

                geometry.LowestTopNotchY = double.MaxValue;
                if (geometry.HasTopLeft)
                {
                    geometry.LowestTopNotchY = Math.Min(
                        geometry.LowestTopNotchY,
                        geometry.TopLeftOuter.Y
                    );
                }
                if (geometry.HasTopRight)
                {
                    geometry.LowestTopNotchY = Math.Min(
                        geometry.LowestTopNotchY,
                        geometry.TopRightOuter.Y
                    );
                }

                geometry.HighestBottomNotchY = double.MinValue;
                if (geometry.HasBottomLeft)
                {
                    geometry.HighestBottomNotchY = Math.Max(
                        geometry.HighestBottomNotchY,
                        geometry.BottomLeftOuter.Y
                    );
                }
                if (geometry.HasBottomRight)
                {
                    geometry.HighestBottomNotchY = Math.Max(
                        geometry.HighestBottomNotchY,
                        geometry.BottomRightOuter.Y
                    );
                }

                return geometry.HasAnyTopNotch || geometry.HasAnyBottomNotch
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
            out Point inner
        )
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
                    ? point.Y < maxY - edgeTol && point.Y >= maxY - SECTION_NOTCH_MAX_SIZE
                    : point.Y > minY + edgeTol && point.Y <= minY + SECTION_NOTCH_MAX_SIZE;

                if (onSideEdge && inVerticalCornerBand)
                {
                    if (
                        outer == null
                        || (topSide && point.Y > outer.Y)
                        || (!topSide && point.Y < outer.Y)
                    )
                    {
                        outer = new Point(point.X, point.Y, 0.0);
                    }
                }

                bool onHorizontalEdge = topSide
                    ? Math.Abs(point.Y - maxY) <= edgeTol
                    : Math.Abs(point.Y - minY) <= edgeTol;

                bool inHorizontalCornerBand = leftSide
                    ? point.X > minX + edgeTol && point.X <= minX + SECTION_NOTCH_MAX_SIZE
                    : point.X < maxX - edgeTol && point.X >= maxX - SECTION_NOTCH_MAX_SIZE;

                if (onHorizontalEdge && inHorizontalCornerBand)
                {
                    if (
                        inner == null
                        || (leftSide && point.X > inner.X)
                        || (!leftSide && point.X < inner.X)
                    )
                    {
                        inner = new Point(point.X, point.Y, 0.0);
                    }
                }
            }

            if (outer == null || inner == null)
                return false;

            if ((leftSide && inner.X >= maxX - edgeTol) || (!leftSide && inner.X <= minX + edgeTol))
                return false;

            double width = leftSide ? Math.Abs(inner.X - minX) : Math.Abs(maxX - inner.X);
            double depth = topSide ? Math.Abs(maxY - outer.Y) : Math.Abs(outer.Y - minY);

            if (
                width < SECTION_NOTCH_MIN_SIZE
                || depth < SECTION_NOTCH_MIN_SIZE
                || width > SECTION_NOTCH_MAX_SIZE
                || depth > SECTION_NOTCH_MAX_SIZE
            )
                return false;

            if (!HasAxisAlignedNotchEvidence(segments, outer, inner, edgeTol))
                return false;

            return true;
        }

        private static bool HasAxisAlignedNotchEvidence(
            List<ProjectedFrontSegment> segments,
            Point outer,
            Point inner,
            double edgeTol
        )
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
                    (
                        AreProjectedPointsNear(start, outer, edgeTol)
                        && AreProjectedPointsNear(end, inner, edgeTol)
                    )
                    || (
                        AreProjectedPointsNear(start, inner, edgeTol)
                        && AreProjectedPointsNear(end, outer, edgeTol)
                    );

                if (joinsOuterAndInner && dx > edgeTol && dy > edgeTol)
                    hasDirectDiagonal = true;

                double segmentMinX = Math.Min(start.X, end.X);
                double segmentMaxX = Math.Max(start.X, end.X);
                double segmentMinY = Math.Min(start.Y, end.Y);
                double segmentMaxY = Math.Max(start.Y, end.Y);

                if (
                    dx > SECTION_NOTCH_POINT_MERGE_TOL
                    && dy <= edgeTol
                    && Math.Abs((start.Y + end.Y) * 0.5 - outer.Y) <= edgeTol
                    && segmentMaxX >= minCornerX
                    && segmentMinX <= maxCornerX
                )
                {
                    hasHorizontalLeg = true;
                }

                if (
                    dy > SECTION_NOTCH_POINT_MERGE_TOL
                    && dx <= edgeTol
                    && Math.Abs((start.X + end.X) * 0.5 - inner.X) <= edgeTol
                    && segmentMaxY >= minCornerY
                    && segmentMinY <= maxCornerY
                )
                {
                    hasVerticalLeg = true;
                }
            }

            return hasHorizontalLeg && hasVerticalLeg && !hasDirectDiagonal;
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
            out string message
        )
        {
            message = "";
            bCutY = maxY;
            bDepth = flangeThickness + SECTION_B_EXTRA_DEPTH;
            cCutY = minY + flangeThickness + SECTION_C_EXTRA_START;
            cDepth = cCutY - minY;

            bool hasTopNotch =
                notchStatus == FrontNotchDetectionStatus.Found
                && notchGeometry != null
                && notchGeometry.HasAnyTopNotch;
            bool hasBottomNotch =
                notchStatus == FrontNotchDetectionStatus.Found
                && notchGeometry != null
                && notchGeometry.HasAnyBottomNotch;

            if (hasTopNotch)
            {
                bDepth = maxY - notchGeometry.LowestTopNotchY + SECTION_B_EXTRA_DEPTH;
            }

            if (hasBottomNotch)
            {
                cCutY = notchGeometry.HighestBottomNotchY + SECTION_C_EXTRA_START;
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
                out message
            );
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
            out string message
        )
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
            bDepth = flangeGeometry.OuterTopY - flangeGeometry.InnerTopY + SECTION_B_EXTRA_DEPTH;
            cCutY = flangeGeometry.InnerBottomY + SECTION_C_EXTRA_START;
            cDepth = cCutY - flangeGeometry.OuterBottomY;

            double baseBDepth = bDepth;
            double baseCCutY = cCutY;
            bool hasTopNotch =
                notchStatus == FrontNotchDetectionStatus.Found
                && notchGeometry != null
                && notchGeometry.HasAnyTopNotch;
            bool hasBottomNotch =
                notchStatus == FrontNotchDetectionStatus.Found
                && notchGeometry != null
                && notchGeometry.HasAnyBottomNotch;

            if (hasTopNotch)
            {
                double notchBDepth = maxY - notchGeometry.LowestTopNotchY + SECTION_B_EXTRA_DEPTH;
                bDepth = Math.Max(baseBDepth, notchBDepth);
                AddGeometryDiagnostic(
                    "TopLimit flange="
                        + FormatDiagnosticNumber(baseBDepth)
                        + " notch="
                        + FormatDiagnosticNumber(notchBDepth)
                );
            }
            else
            {
                AddGeometryDiagnostic(
                    "TopLimit flange=" + FormatDiagnosticNumber(baseBDepth) + " notch=none"
                );
            }

            if (hasBottomNotch)
            {
                double notchCCutY = notchGeometry.HighestBottomNotchY + SECTION_C_EXTRA_START;
                cCutY = Math.Max(baseCCutY, notchCCutY);
                cDepth = cCutY - minY;
                AddGeometryDiagnostic(
                    "BottomLimit flangeCutY="
                        + FormatDiagnosticNumber(baseCCutY)
                        + " notchCutY="
                        + FormatDiagnosticNumber(notchCCutY)
                );
            }
            else
            {
                AddGeometryDiagnostic(
                    "BottomLimit flangeCutY=" + FormatDiagnosticNumber(baseCCutY) + " notch=none"
                );
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
                out message
            );
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
            out string message
        )
        {
            message = "";
            double partHeight = maxY - minY;

            if (!IsFinite(partHeight) || partHeight <= TOL)
            {
                message = "Chieu cao Shape C khong hop le de tinh Section B/C.";
                return false;
            }

            if (!IsFinite(bCutY) || !IsFinite(bDepth) || bDepth <= TOL)
            {
                message = "Section B tinh theo mep canh Shape C khong hop le.";
                return false;
            }

            if (bDepth >= partHeight)
            {
                message = "Section B theo mep canh/notch Shape C vuot chieu cao part.";
                return false;
            }

            if (bCutY > maxY + TOL || bCutY < minY - TOL)
            {
                message = "Section B Shape C nam ngoai chieu cao part.";
                return false;
            }

            if (!IsFinite(cCutY) || !IsFinite(cDepth) || cDepth <= TOL)
            {
                message = "Section C tinh theo mep canh Shape C khong hop le.";
                return false;
            }

            if (cDepth >= partHeight)
            {
                message = "Section C theo mep canh/notch Shape C vuot chieu cao part.";
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
                    maxY - notchGeometry.LowestTopNotchY + SECTION_B_EXTRA_DEPTH;

                if (!IsFinite(requiredBDepth) || bDepth < requiredBDepth - TOL)
                {
                    message = "Section B Shape C khong bao phu day notch tren.";
                    return false;
                }
            }

            if (hasBottomNotch)
            {
                double requiredCCutY = notchGeometry.HighestBottomNotchY + SECTION_C_EXTRA_START;

                if (!IsFinite(requiredCCutY) || cCutY < requiredCCutY - TOL)
                {
                    message = "Section C Shape C khong bao phu dinh notch duoi.";
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
            out string message
        )
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
                    maxY - notchGeometry.LowestTopNotchY + SECTION_B_EXTRA_DEPTH;

                if (!IsFinite(requiredBDepth) || bDepth < requiredBDepth - TOL)
                {
                    message = "Section B depth khong bao phu day notch tren.";
                    return false;
                }
            }

            if (hasBottomNotch)
            {
                double requiredCCutY = notchGeometry.HighestBottomNotchY + SECTION_C_EXTRA_START;

                if (!IsFinite(requiredCCutY) || cCutY < requiredCCutY - TOL)
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
            out string message
        )
        {
            attributeSet = null;
            message = "";

            try
            {
                if (string.IsNullOrWhiteSpace(sectionViewAttributeName))
                {
                    message =
                        "Khong doc duoc View properties cua hang Section views "
                        + "truoc khi Load Standard.";
                    return false;
                }

                string mergedViewAttributeName;
                string mergeMessage;
                if (
                    !TryBuildMergedSectionViewAttribute(
                        sectionViewAttributeName,
                        SECTION_MARK_ATTRIBUTE_NAME,
                        out mergedViewAttributeName,
                        out mergeMessage
                    )
                )
                {
                    message = mergeMessage;
                    return false;
                }

                DrawingView.ViewAttributes viewAttributes = new DrawingView.ViewAttributes();

                if (!viewAttributes.LoadAttributes(mergedViewAttributeName))
                {
                    message =
                        "Khong load duoc merged Section View attribute: "
                        + mergedViewAttributeName;
                    return false;
                }

                SectionMarkBase.SectionMarkAttributes markAttributes =
                    new SectionMarkBase.SectionMarkAttributes();

                if (!markAttributes.LoadAttributes(SECTION_MARK_ATTRIBUTE_NAME))
                {
                    message =
                        "Thieu hoac khong load duoc section mark attribute: "
                        + SECTION_MARK_ATTRIBUTE_NAME;
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

        private static bool TryBuildMergedSectionViewAttribute(
            string baseViewAttributeName,
            string sectionStandardName,
            out string mergedAttributeName,
            out string message
        )
        {
            mergedAttributeName = "";
            message = "";
            try
            {
                string baseFile = ResolveEnvironmentAttributeFile(
                    baseViewAttributeName,
                    ".vi"
                );
                string sectionFile = ResolveSectionStandardFile(sectionStandardName);
                if (String.IsNullOrWhiteSpace(baseFile) || !File.Exists(baseFile))
                {
                    message = "Khong tim thay base View property: " + baseViewAttributeName;
                    return false;
                }
                if (String.IsNullOrWhiteSpace(sectionFile) || !File.Exists(sectionFile))
                {
                    message = "Khong tim thay Section standard file: " + sectionStandardName;
                    return false;
                }

                Encoding encoding = ResolveTeklaAttributeEncoding(baseFile);
                string[] baseLines = File.ReadAllLines(baseFile, encoding);
                string[] sectionLines = File.ReadAllLines(sectionFile, encoding);
                Dictionary<string, string> sectionViewLabelLines =
                    new Dictionary<string, string>(StringComparer.Ordinal);

                for (int i = 0; i < sectionLines.Length; i++)
                {
                    string key = GetTeklaSettingKey(sectionLines[i]);
                    if (!IsSectionViewLabelSettingKey(key))
                        continue;
                    // Native .vi loading uses the serialized GEO_SECTION
                    // subtype directly: 81 maps to Section name. Do not apply
                    // the +1 conversion required by the reflection/API fallback.
                    sectionViewLabelLines[key] = sectionLines[i];
                }

                string symbolColorLine;
                if (
                    sectionViewLabelLines.TryGetValue(
                        "ViewLabelMarkSymbolColor",
                        out symbolColorLine
                    )
                )
                {
                    sectionViewLabelLines["label_colour"] =
                        "label_colour " + GetTeklaSettingValue(symbolColorLine);
                    sectionViewLabelLines["label_colour_en"] = "label_colour_en 1";
                }
                string verticalPositionLine;
                if (
                    sectionViewLabelLines.TryGetValue(
                        "ViewLabelPosition",
                        out verticalPositionLine
                    )
                )
                {
                    sectionViewLabelLines["label_position"] =
                        "label_position " + GetTeklaSettingValue(verticalPositionLine);
                    sectionViewLabelLines["label_position_en"] = "label_position_en 1";
                }

                if (
                    !sectionViewLabelLines.ContainsKey("ViewLabelMarkA1.ContentString")
                    || !sectionViewLabelLines.ContainsKey("ViewLabelMarkSymbolColor")
                )
                {
                    message = "Section standard khong co day du ViewLabel settings.";
                    return false;
                }

                List<string> mergedLines = new List<string>();
                HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < baseLines.Length; i++)
                {
                    string key = GetTeklaSettingKey(baseLines[i]);
                    string replacement;
                    if (
                        IsSectionViewLabelSettingKey(key)
                        && sectionViewLabelLines.TryGetValue(key, out replacement)
                    )
                    {
                        mergedLines.Add(replacement);
                        written.Add(key);
                    }
                    else
                    {
                        mergedLines.Add(baseLines[i]);
                    }
                }

                foreach (KeyValuePair<string, string> item in sectionViewLabelLines)
                {
                    if (!written.Contains(item.Key))
                        mergedLines.Add(item.Value);
                }

                string outputDirectory = Path.GetDirectoryName(baseFile);
                if (String.IsNullOrWhiteSpace(outputDirectory))
                {
                    message = "Khong resolve duoc attributes directory.";
                    return false;
                }
                string outputFile = Path.Combine(
                    outputDirectory,
                    MERGED_SECTION_VIEW_ATTRIBUTE_NAME + ".vi"
                );
                string temporaryFile = outputFile + ".tmp";
                File.WriteAllLines(temporaryFile, mergedLines.ToArray(), encoding);
                if (File.Exists(outputFile))
                    File.Delete(outputFile);
                File.Move(temporaryFile, outputFile);

                mergedAttributeName = MERGED_SECTION_VIEW_ATTRIBUTE_NAME;
                message =
                    "Merged "
                    + Path.GetFileName(baseFile)
                    + " + "
                    + Path.GetFileName(sectionFile)
                    + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = "Tao merged Section View property loi: " + ex.Message;
                return false;
            }
        }

        private static string ResolveEnvironmentAttributeFile(
            string attributeName,
            string extension
        )
        {
            if (String.IsNullOrWhiteSpace(attributeName))
                return "";
            string fileName = attributeName.Trim();
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                fileName += extension;
            try
            {
                FileInfo file =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles.GetAttributeFile(fileName);
                return file != null && file.Exists ? file.FullName : "";
            }
            catch
            {
                return "";
            }
        }

        private static string GetTeklaSettingKey(string line)
        {
            string trimmed = line == null ? "" : line.Trim();
            if (String.IsNullOrWhiteSpace(trimmed))
                return "";
            int separator = trimmed.IndexOfAny(new char[] { ' ', '\t' });
            return separator < 0 ? trimmed : trimmed.Substring(0, separator);
        }

        private static Encoding ResolveTeklaAttributeEncoding(string file)
        {
            try
            {
                byte[] headerBytes = new byte[128];
                int read;
                using (FileStream stream = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                ))
                {
                    read = stream.Read(headerBytes, 0, headerBytes.Length);
                }
                string header = Encoding.ASCII.GetString(headerBytes, 0, read);
                string firstLine = header.Split(new char[] { '\r', '\n' })[0].Trim();
                const string prefix = "encoding ";
                int codePage;
                if (
                    firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && Int32.TryParse(
                        firstLine.Substring(prefix.Length).Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out codePage
                    )
                )
                {
                    return Encoding.GetEncoding(codePage);
                }
            }
            catch { }

            return Encoding.Default;
        }

        private static string GetTeklaSettingValue(string line)
        {
            string trimmed = line == null ? "" : line.Trim();
            int separator = trimmed.IndexOfAny(new char[] { ' ', '\t' });
            return separator < 0 ? "" : trimmed.Substring(separator + 1).Trim();
        }

        private static bool IsSectionViewLabelSettingKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key))
                return false;
            return key.StartsWith("ViewLabel", StringComparison.Ordinal)
                || key.StartsWith("aViewLabel", StringComparison.Ordinal)
                || key.StartsWith("ViewMarkA", StringComparison.Ordinal)
                || String.Equals(key, "label_text", StringComparison.Ordinal)
                || String.Equals(key, "label_colour", StringComparison.Ordinal)
                || String.Equals(key, "label_colour_en", StringComparison.Ordinal)
                || String.Equals(key, "label_position", StringComparison.Ordinal)
                || String.Equals(key, "label_position_en", StringComparison.Ordinal)
                || String.Equals(key, "label_height", StringComparison.Ordinal)
                || String.Equals(key, "label_height_en", StringComparison.Ordinal)
                || String.Equals(key, "label_en", StringComparison.Ordinal);
        }

        private static DrawingView.ViewMarkTagAttributes GetViewLabelTag(
            DrawingView.ViewMarkTagsAttributes tags,
            int index
        )
        {
            if (tags == null)
                return null;
            switch (index)
            {
                case 1:
                    return tags.TagA1;
                case 2:
                    return tags.TagA2;
                case 3:
                    return tags.TagA3;
                case 4:
                    return tags.TagA4;
                case 5:
                    return tags.TagA5;
                default:
                    return null;
            }
        }

        private static string ResolveSectionStandardFile(string sectionStandardName)
        {
            string fileName = sectionStandardName.Trim();
            if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                fileName += ".cs";

            try
            {
                FileInfo environmentFile =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles.GetAttributeFile(fileName);
                if (environmentFile != null && environmentFile.Exists)
                    return environmentFile.FullName;
            }
            catch { }

            return "";
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
            out SectionMark sectionMark
        )
        {
            sectionView = null;
            sectionMark = null;

            try
            {
                if (
                    frontView == null
                    || attributeSet == null
                    || !IsFinitePoint(startPoint)
                    || !IsFinitePoint(endPoint)
                    || !IsFinitePoint(insertionPoint)
                    || !IsFinite(depthUp)
                    || !IsFinite(depthDown)
                )
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
                    out sectionMark
                );

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
            DrawingView sectionView
        )
        {
            if (!SafeCommit(drawing))
                return false;

            Thread.Sleep(150);

            return sectionView != null
                && IsViewPresent(drawing, sectionView)
                && ViewContainsPart(sectionView, part.Identifier);
        }

        private static AutoSectionWorkerResult FinishCreateFailure(
            Drawing drawing,
            string message,
            DrawingView sectionB,
            SectionMark markB,
            DrawingView sectionC,
            SectionMark markC
        )
        {
            AutoSectionWorkerResult result = new AutoSectionWorkerResult();
            result.Message = message;
            result.SectionB = sectionB;
            result.SectionC = sectionC;

            bool hasCreatedReference =
                sectionB != null || markB != null || sectionC != null || markC != null;

            if (!hasCreatedReference)
            {
                result.Status = AutoSectionWorkerStatus.CreateFailed;
                return result;
            }

            if (RollbackCreatedSections(drawing, sectionB, markB, sectionC, markC))
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
            SectionMark markC
        )
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
                (sectionB == null || !IsViewPresent(drawing, sectionB))
                && (sectionC == null || !IsViewPresent(drawing, sectionC));

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

                    if (
                        System.Object.ReferenceEquals(current, target)
                        || (targetIdentifier > 0 && GetViewIdentifier(current) == targetIdentifier)
                    )
                        return true;
                }
            }
            catch { }

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
                    PropertyInfo property = view.GetType()
                        .GetProperty(
                            propertyName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        );

                    if (property == null || !property.CanRead)
                        continue;

                    Identifier identifier = property.GetValue(view, null) as Identifier;
                    if (identifier != null && identifier.ID > 0)
                        return identifier.ID;
                }
                catch { }
            }

            return 0;
        }

        private static bool ViewContainsPart(DrawingView view, Identifier partIdentifier)
        {
            try
            {
                if (view == null || partIdentifier == null)
                    return false;

                DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));

                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (drawingPart == null || drawingPart.ModelIdentifier == null)
                        continue;

                    if (drawingPart.ModelIdentifier.ID == partIdentifier.ID)
                        return true;
                }
            }
            catch { }

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
            catch { }

            return 0.0;
        }

        private static double GetViewScale(DrawingView view)
        {
            try
            {
                if (view != null && view.Attributes != null)
                    return view.Attributes.Scale;
            }
            catch { }

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

                string normalized = profile
                    .ToUpperInvariant()
                    .Replace("BH", "")
                    .Replace("H", "")
                    .Replace("I", "")
                    .Replace("PL", "")
                    .Replace(" ", "")
                    .Replace(",", ".");

                string[] tokens = normalized.Split(
                    new char[] { '*', 'X', 'x', '-' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                List<double> values = new List<double>();
                foreach (string token in tokens)
                {
                    double value;
                    if (
                        Double.TryParse(
                            token,
                            NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out value
                        )
                        && value > 0.0
                    )
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
                        if (
                            part.GetReportProperty(propertyName, ref value)
                            && IsFinite(value)
                            && value > 0.0
                        )
                            return value;
                    }
                    catch { }
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
            return point == null ? null : new Point(point.X, point.Y, point.Z);
        }

        private static bool IsFinitePoint(Point point)
        {
            return point != null && IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
#pragma warning restore
#endregion

#region PHU_SectionViewAttributeResolver - Tìm và nạp thuộc tính section
#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using Tekla.Structures.Drawing;
    using Tekla.Structures.Model;

    public sealed class SectionViewAttributeResolution
    {
        public bool Success;
        public string AttributeName = "";
        public string Source = "";
        public string DrawingAttributeName = "";
        public string DrawingPropertyFile = "";
        public string EnabledCode = "";
        public string RawDrawingViews = "";
        public string Error = "";
        public string LiveDialogError = "";
        public bool LiveDialogStateSafe = true;
        public readonly List<string> ActiveViewAttributeNames = new List<string>();
        public readonly List<string> MatchingPropertyFiles = new List<string>();
        public readonly List<string> LiveDialogValues = new List<string>();
    }

    // Reads the currently loaded Section View properties value from the
    // Drawing Properties dialog. The target mirrors Viewpropertive1.cs:
    // View creation tree -> Views tab -> row 6. This must run BEFORE
    // PHU_LoadStandardService changes the drawing/view attributes.
    public static class SectionViewAttributeResolver
    {
        private static readonly object LiveDialogSyncRoot = new object();
        private const string DrawingViewsKey = "dv.aDrawingViews";
        private const string SectionViewTypeCode = "7";
        // GIAO THỨC TEKLA UI: ID dialog/control và index dòng phụ thuộc phiên bản Tekla, không phải thiết lập người dùng.
        private const string SinglePartDrawingPropertiesDialogId = "wdraw_dial";
        private const string AssemblyDrawingPropertiesDialogId = "adraw_dial";
        private const string DrawingPropertiesMenuTreeId = "gratCastUnitDrawingAttributesMenuTree";
        private const string DrawingPropertiesViewCreationNode = "View creation";
        private const string DrawingPropertiesMainContainerId = "contMain";
        private const string DrawingPropertiesViewsTabId = "tabViews";
        private const string DrawingPropertiesViewsTableId = "table_ViewsTable";
        private const int SectionViewTableRowIndex = 6;
        private const int WmClose = 0x0010;
        private const int WmGetText = 0x000D;
        private const int BmClick = 0x00F5;
        private const int CbGetCurrentSelection = 0x0147;
        private const int CbGetListText = 0x0148;
        private const uint SendMessageAbortIfHung = 0x0002;

        private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam
        );

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(
            IntPtr parentWindow,
            EnumWindowsCallback callback,
            IntPtr parameter
        );

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId
        );

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr windowHandle,
            StringBuilder text,
            int maximumCount
        );

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr windowHandle,
            StringBuilder className,
            int maximumCount
        );

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutText(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            StringBuilder lParam,
            uint flags,
            uint timeout,
            out IntPtr result
        );

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        private static extern IntPtr SendMessageTimeoutPointer(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out IntPtr result
        );

        private sealed class DrawingViewRow
        {
            public string TypeCode = "";
            public string EnabledCode = "";
            public string ReservedValue = "";
            public string ViewAttributeName = "";
        }

        private sealed class DrawingPropertyData
        {
            public string File = "";
            public string RawDrawingViews = "";
            public readonly List<DrawingViewRow> Rows = new List<DrawingViewRow>();
        }

        private sealed class CandidateMatch
        {
            public DrawingPropertyData Data;
            public DrawingViewRow SectionRow;
            public int Score;
        }

        private sealed class NativeWindowInfo
        {
            public IntPtr Handle;
            public string Text = "";
            public string ClassName = "";
        }

        public static SectionViewAttributeResolution Resolve(Drawing drawing, Model model)
        {
            SectionViewAttributeResolution result = new SectionViewAttributeResolution();

            if (drawing == null)
            {
                result.Error = "Active drawing is null.";
                return result;
            }

            bool isSinglePart = drawing is SinglePartDrawing;
            bool isAssembly = drawing is AssemblyDrawing;
            if (!isSinglePart && !isAssembly)
            {
                result.Error = "Drawing is not SinglePartDrawing or AssemblyDrawing.";
                return result;
            }

            string extension = isSinglePart ? ".wd" : ".ad";
            string modelPath = ReadModelPath(model);

            result.DrawingAttributeName = ReadAttributeFilename(drawing);
            ReadActiveViewAttributeNames(drawing, result.ActiveViewAttributeNames);

            // Authoritative source: read the View properties cell that Tekla
            // has currently loaded for the Section row in Drawing Properties.
            // This targets the same tree/tab/table/row as Viewpropertive1.cs,
            // without invoking its deselect/Modify/Apply/OK commands.
            List<string> viewAttributeFiles = CollectAttributeFiles(".vi", modelPath);
            List<string> drawingPropertyFiles = CollectAttributeFiles(extension, modelPath);
            bool liveDialogResolved = TryResolveFromLiveDrawingPropertiesDialog(
                result,
                viewAttributeFiles,
                drawingPropertyFiles,
                isAssembly
            );
            if (liveDialogResolved)
            {
                return result;
            }

            if (!result.LiveDialogStateSafe)
            {
                if (string.IsNullOrWhiteSpace(result.Error))
                {
                    result.Error =
                        "Drawing Properties cleanup was not confirmed; "
                        + "Section and Dim were stopped to protect the Tekla session.";
                }

                return result;
            }

            // Legacy read-only fallbacks remain available for diagnostics and
            // compatibility if the live dialog cannot be inspected.
            string exactPropertyFile = ResolveAttributeFile(
                result.DrawingAttributeName,
                extension,
                modelPath
            );

            if (!string.IsNullOrWhiteSpace(exactPropertyFile))
            {
                DrawingPropertyData exactData = ReadDrawingProperty(exactPropertyFile);
                DrawingViewRow exactSection = FindSectionRow(exactData);

                if (exactSection != null)
                {
                    return CompleteFromPropertyFile(
                        result,
                        exactData,
                        exactSection,
                        "Fallback: Drawing.AttributeFilename"
                    );
                }
            }

            // If a real section view already exists, its loaded ViewAttributes
            // are more reliable than guessing a drawing property file.
            string existingSectionAttribute = ReadExistingSectionViewAttribute(drawing);
            if (!string.IsNullOrWhiteSpace(existingSectionAttribute))
            {
                result.Success = true;
                result.AttributeName = existingSectionAttribute;
                result.Source = "Fallback: Existing SectionView.Attributes";
                return result;
            }

            List<string> derivedPropertyNames = new List<string>();
            List<string> candidateFiles = new List<string>();

            for (int i = 0; i < result.ActiveViewAttributeNames.Count; i++)
            {
                List<string> derived = BuildDrawingPropertyNameCandidates(
                    result.ActiveViewAttributeNames[i]
                );

                for (int j = 0; j < derived.Count; j++)
                {
                    AddUnique(derivedPropertyNames, derived[j]);
                    AddUnique(
                        candidateFiles,
                        ResolveAttributeFile(derived[j], extension, modelPath)
                    );
                }
            }

            List<string> allPropertyFiles = CollectAttributeFiles(extension, modelPath);
            for (int i = 0; i < allPropertyFiles.Count; i++)
                AddUnique(candidateFiles, allPropertyFiles[i]);

            List<CandidateMatch> matches = new List<CandidateMatch>();
            for (int i = 0; i < candidateFiles.Count; i++)
            {
                DrawingPropertyData data = ReadDrawingProperty(candidateFiles[i]);
                DrawingViewRow sectionRow = FindSectionRow(data);
                if (sectionRow == null)
                    continue;

                int score = ScoreCandidate(
                    data,
                    result.ActiveViewAttributeNames,
                    derivedPropertyNames
                );
                if (score <= 0)
                    continue;

                CandidateMatch match = new CandidateMatch();
                match.Data = data;
                match.SectionRow = sectionRow;
                match.Score = score;
                matches.Add(match);
            }

            if (matches.Count == 0)
            {
                result.Error =
                    "Cannot identify the original drawing property. "
                    + "Drawing.AttributeFilename is empty and current view "
                    + "attributes do not uniquely reference a .wd/.ad file. "
                    + "Targeted Drawing Properties Views row 6 also failed: "
                    + result.LiveDialogError;
                return result;
            }

            int bestScore = int.MinValue;
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Score > bestScore)
                    bestScore = matches[i].Score;
            }

            List<CandidateMatch> bestMatches = new List<CandidateMatch>();
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Score == bestScore)
                {
                    bestMatches.Add(matches[i]);
                    AddUnique(result.MatchingPropertyFiles, matches[i].Data.File);
                }
            }

            string commonSectionAttribute = NormalizeViewAttributeName(
                bestMatches[0].SectionRow.ViewAttributeName
            );
            for (int i = 1; i < bestMatches.Count; i++)
            {
                string candidateSectionAttribute = NormalizeViewAttributeName(
                    bestMatches[i].SectionRow.ViewAttributeName
                );

                if (
                    !string.Equals(
                        commonSectionAttribute,
                        candidateSectionAttribute,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    result.Error =
                        "Ambiguous drawing property match. More than one "
                        + ".wd/.ad file matches the active views but their "
                        + "Section views properties are different. Targeted "
                        + "Drawing Properties Views row 6 also failed: "
                        + result.LiveDialogError;
                    return result;
                }
            }

            string source =
                bestMatches.Count == 1
                    ? "Fallback: Matched active view attributes"
                    : "Fallback: Matched active views; all best files use same Section property";

            return CompleteFromPropertyFile(
                result,
                bestMatches[0].Data,
                bestMatches[0].SectionRow,
                source
            );
        }

        private static bool TryResolveFromLiveDrawingPropertiesDialog(
            SectionViewAttributeResolution result,
            List<string> viewAttributeFiles,
            List<string> drawingPropertyFiles,
            bool isAssemblyDrawing
        )
        {
            lock (LiveDialogSyncRoot)
            {
                return TryResolveFromLiveDrawingPropertiesDialogLocked(
                    result,
                    viewAttributeFiles,
                    drawingPropertyFiles,
                    isAssemblyDrawing
                );
            }
        }

        private static bool TryResolveFromLiveDrawingPropertiesDialogLocked(
            SectionViewAttributeResolution result,
            List<string> viewAttributeFiles,
            List<string> drawingPropertyFiles,
            bool isAssemblyDrawing
        )
        {
            if (viewAttributeFiles == null || viewAttributeFiles.Count == 0)
            {
                result.LiveDialogError =
                    "No .vi View properties files are available for comparison.";
                return false;
            }

            Process teklaProcess = FindTeklaProcess();
            if (teklaProcess == null)
            {
                result.LiveDialogError = "TeklaStructures process was not found.";
                return false;
            }

            Thread dialogThread = null;
            bool dialogThreadStarted = false;
            IntPtr dialogHandle = IntPtr.Zero;
            IntPtr viewPropertiesDialogHandle = IntPtr.Zero;
            bool drawingPropertiesDialogOwned = false;

            try
            {
                List<NativeWindowInfo> beforeWindows = GetVisibleNativeWindows(teklaProcess.Id);
                HashSet<long> beforeHandles = new HashSet<long>();
                for (int i = 0; i < beforeWindows.Count; i++)
                    beforeHandles.Add(beforeWindows[i].Handle.ToInt64());

                // Respect the runtime Drawing type exactly. A SinglePartDrawing
                // must use wdraw_dial; only an AssemblyDrawing may use adraw_dial.
                // The old DrawingInternal export could open the Assembly dialog
                // while a SinglePartDrawing was active, so its HWND and the Akit
                // target belonged to different dialogs.
                dialogHandle = FindVisibleExpectedDrawingPropertiesDialog(
                    beforeWindows,
                    isAssemblyDrawing
                );

                string invokeError = "";
                if (dialogHandle == IntPtr.Zero)
                {
                    dialogThread = new Thread(
                        delegate()
                        {
                            try
                            {
                                InvokeDisplayDrawingPropertiesDialog(
                                    teklaProcess.Id,
                                    isAssemblyDrawing
                                );
                            }
                            catch (Exception ex)
                            {
                                Exception real = ex.InnerException ?? ex;
                                invokeError = real.GetType().Name + ": " + real.Message;
                            }
                        }
                    );
                    dialogThread.IsBackground = true;
                    try
                    {
                        dialogThread.SetApartmentState(ApartmentState.STA);
                    }
                    catch { }
                    dialogThread.Start();
                    dialogThreadStarted = true;

                    for (int attempt = 0; attempt < 60; attempt++)
                    {
                        Thread.Sleep(100);
                        List<NativeWindowInfo> currentWindows = GetVisibleNativeWindows(
                            teklaProcess.Id
                        );

                        for (
                            int windowIndex = 0;
                            windowIndex < currentWindows.Count;
                            windowIndex++
                        )
                        {
                            NativeWindowInfo window = currentWindows[windowIndex];
                            long handleValue = window.Handle.ToInt64();
                            if (
                                handleValue == 0
                                || beforeHandles.Contains(handleValue)
                                || IsIgnoredNativeWindowClass(window.ClassName)
                                || !IsExpectedDrawingPropertiesDialog(
                                    window,
                                    isAssemblyDrawing
                                )
                            )
                            {
                                continue;
                            }

                            dialogHandle = window.Handle;
                            drawingPropertiesDialogOwned = true;
                            break;
                        }

                        if (dialogHandle != IntPtr.Zero)
                            break;

                        if (
                            !dialogThread.IsAlive
                            && !string.IsNullOrWhiteSpace(invokeError)
                        )
                        {
                            break;
                        }
                    }
                }

                if (dialogHandle == IntPtr.Zero)
                {
                    result.LiveDialogError =
                        (isAssemblyDrawing
                            ? "Assembly drawing properties"
                            : "Single-part drawing properties")
                        + " dialog did not open"
                        + (string.IsNullOrWhiteSpace(invokeError) ? "." : ": " + invokeError);
                    return false;
                }

                try
                {
                    if (!drawingPropertiesDialogOwned)
                    {
                        string existingDialogDiagnostic;
                        if (
                            TryCompleteFromCurrentDrawingProperty(
                                result,
                                dialogHandle,
                                drawingPropertyFiles,
                                viewAttributeFiles,
                                isAssemblyDrawing,
                                out existingDialogDiagnostic
                            )
                        )
                        {
                            return true;
                        }

                        result.LiveDialogError =
                            "A matching Drawing Properties dialog was already open. "
                            + "It was not changed because it may contain unsaved user edits. "
                            + existingDialogDiagnostic;
                        return false;
                    }

                    string getError;
                    if (!TryLoadCurrentDrawingProperties(dialogHandle, out getError))
                    {
                        result.LiveDialogError = getError;
                        return false;
                    }

                    string propertyDiagnostic = "";
                    for (int propertyAttempt = 0; propertyAttempt < 30; propertyAttempt++)
                    {
                        Thread.Sleep(100);
                        if (
                            TryCompleteFromCurrentDrawingProperty(
                                result,
                                dialogHandle,
                                drawingPropertyFiles,
                                viewAttributeFiles,
                                isAssemblyDrawing,
                                out propertyDiagnostic
                            )
                        )
                        {
                            return true;
                        }
                    }

                    string targetError;
                    if (
                        !TryTargetSectionViewRowWithAkit(
                            teklaProcess.Id,
                            isAssemblyDrawing,
                            out targetError
                        )
                    )
                    {
                        result.LiveDialogError =
                            propertyDiagnostic
                            + " | Drawing Properties dialog opened, but Views row 6 "
                            + "could not be selected: "
                            + targetError;
                        return false;
                    }

                    string openViewPropertiesError;
                    if (
                        !TryOpenSelectedViewPropertiesDialog(
                            dialogHandle,
                            teklaProcess.Id,
                            out viewPropertiesDialogHandle,
                            out openViewPropertiesError
                        )
                    )
                    {
                        result.LiveDialogError =
                            propertyDiagnostic
                            + " | Views row 6 was selected, but its View properties "
                            + "dialog did not open: "
                            + openViewPropertiesError;
                        return false;
                    }

                    Thread.Sleep(250);

                    List<string> preferredValues = new List<string>();
                    CollectNativeWindowValues(
                        viewPropertiesDialogHandle,
                        result.LiveDialogValues,
                        preferredValues
                    );

                    string viewAttributeFile = FindViewAttributeFileFromDialogValues(
                        preferredValues,
                        viewAttributeFiles
                    );

                    if (string.IsNullOrWhiteSpace(viewAttributeFile))
                    {
                        viewAttributeFile = FindViewAttributeFileFromDialogValues(
                            result.LiveDialogValues,
                            viewAttributeFiles
                        );
                    }

                    if (string.IsNullOrWhiteSpace(viewAttributeFile))
                    {
                        Thread.Sleep(300);
                        CollectNativeWindowValues(
                            viewPropertiesDialogHandle,
                            result.LiveDialogValues,
                            preferredValues
                        );

                        viewAttributeFile = FindViewAttributeFileFromDialogValues(
                            preferredValues,
                            viewAttributeFiles
                        );

                        if (string.IsNullOrWhiteSpace(viewAttributeFile))
                        {
                            viewAttributeFile = FindViewAttributeFileFromDialogValues(
                                result.LiveDialogValues,
                                viewAttributeFiles
                            );
                        }
                    }

                    if (string.IsNullOrWhiteSpace(viewAttributeFile))
                    {
                        result.LiveDialogError =
                            "The View properties dialog for Views row 6 opened, "
                            + "but its Save/Load value did not match a known .vi file.";
                        return false;
                    }

                    result.Success = true;
                    result.AttributeName = Path.GetFileNameWithoutExtension(viewAttributeFile);
                    result.Source = isAssemblyDrawing
                        ? "Live Assembly Drawing Properties / Views row 6 / View properties"
                        : "Live Drawing Properties / Views row 6 / View properties";
                    result.Error = "";
                    result.LiveDialogError = "";
                    return true;
                }
                catch (Exception ex)
                {
                    Exception real = ex.InnerException ?? ex;
                    result.LiveDialogError = real.GetType().Name + ": " + real.Message;
                    return false;
                }
            }
            finally
            {
                bool viewPropertiesClosed = CloseNativeWindowAndWait(
                    viewPropertiesDialogHandle,
                    3000
                );
                bool drawingPropertiesClosed =
                    !drawingPropertiesDialogOwned
                    || CloseNativeWindowAndWait(dialogHandle, 3000);

                if (
                    !viewPropertiesClosed
                    && (
                        viewPropertiesDialogHandle == IntPtr.Zero
                        || !IsWindow(viewPropertiesDialogHandle)
                    )
                )
                {
                    viewPropertiesClosed = true;
                }

                if (
                    !drawingPropertiesClosed
                    && (dialogHandle == IntPtr.Zero || !IsWindow(dialogHandle))
                )
                {
                    drawingPropertiesClosed = true;
                }

                bool dialogThreadStopped = true;

                if (dialogThreadStarted && dialogThread != null)
                {
                    try
                    {
                        dialogThreadStopped = dialogThread.Join(5000);
                    }
                    catch
                    {
                        dialogThreadStopped = false;
                    }
                }

                if (!viewPropertiesClosed || !drawingPropertiesClosed || !dialogThreadStopped)
                {
                    result.LiveDialogStateSafe = false;
                    result.Success = false;
                    result.AttributeName = "";

                    string cleanupDetails =
                        "ViewPropertiesClosed="
                        + viewPropertiesClosed
                        + ", DrawingPropertiesClosed="
                        + drawingPropertiesClosed
                        + ", DialogThreadStopped="
                        + dialogThreadStopped
                        + ".";

                    result.LiveDialogError = string.IsNullOrWhiteSpace(result.LiveDialogError)
                        ? cleanupDetails
                        : result.LiveDialogError + " | " + cleanupDetails;
                    result.Error =
                        "Unsafe Drawing Properties cleanup. "
                        + cleanupDetails
                        + " Section and Dim were stopped.";
                }
            }
        }

        private static Process FindTeklaProcess()
        {
            try
            {
                return TTSK_AutoDim_Plates.TeklaSessionCoordinator.GetBoundProcess();
            }
            catch { }

            return null;
        }

        private static List<NativeWindowInfo> GetVisibleNativeWindows(int processId)
        {
            List<NativeWindowInfo> result = new List<NativeWindowInfo>();

            try
            {
                EnumWindowsCallback callback = delegate(IntPtr windowHandle, IntPtr parameter)
                {
                    uint ownerProcessId;
                    GetWindowThreadProcessId(windowHandle, out ownerProcessId);

                    if (ownerProcessId != (uint)processId || !IsWindowVisible(windowHandle))
                    {
                        return true;
                    }

                    NativeWindowInfo info = new NativeWindowInfo();
                    info.Handle = windowHandle;
                    info.Text = ReadNativeWindowText(windowHandle);
                    info.ClassName = ReadNativeWindowClass(windowHandle);
                    result.Add(info);
                    return true;
                };

                EnumWindows(callback, IntPtr.Zero);
            }
            catch { }

            return result;
        }

        private static bool IsIgnoredNativeWindowClass(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return false;

            return className.IndexOf("ComboLBox", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("tooltips_class", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ScoreNativeDialogWindow(NativeWindowInfo window)
        {
            int score = 0;
            if (window == null)
                return score;

            if (
                !string.IsNullOrWhiteSpace(window.Text)
                && window.Text.IndexOf("propert", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                score += 100;
            }

            if (
                !string.IsNullOrWhiteSpace(window.ClassName)
                && window.ClassName.StartsWith("Afx", StringComparison.OrdinalIgnoreCase)
            )
            {
                score += 50;
            }

            if (string.Equals(window.ClassName, "#32770", StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }

            return score;
        }

        private static IntPtr FindVisibleExpectedDrawingPropertiesDialog(
            List<NativeWindowInfo> windows,
            bool isAssemblyDrawing
        )
        {
            if (windows == null)
                return IntPtr.Zero;

            for (int i = 0; i < windows.Count; i++)
            {
                if (IsExpectedDrawingPropertiesDialog(windows[i], isAssemblyDrawing))
                    return windows[i].Handle;
            }

            return IntPtr.Zero;
        }

        private static bool IsExpectedDrawingPropertiesDialog(
            NativeWindowInfo window,
            bool isAssemblyDrawing
        )
        {
            if (window == null || string.IsNullOrWhiteSpace(window.Text))
                return false;

            bool isSingleTitle =
                window.Text.IndexOf(
                    "Single-part drawing properties",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
            bool isAssemblyTitle =
                window.Text.IndexOf(
                    "Assembly drawing properties",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;

            return isAssemblyDrawing ? isAssemblyTitle : isSingleTitle;
        }

        private static bool TryLoadCurrentDrawingProperties(
            IntPtr drawingPropertiesDialogHandle,
            out string error
        )
        {
            error = "";
            IntPtr getButton = FindVisibleNativeChildButton(
                drawingPropertiesDialogHandle,
                "Get"
            );
            if (getButton == IntPtr.Zero)
            {
                error = "The current Drawing Properties Get button was not found.";
                return false;
            }

            if (!PostMessage(getButton, BmClick, IntPtr.Zero, IntPtr.Zero))
            {
                error = "Cannot load the active drawing values with Get.";
                return false;
            }

            return true;
        }

        private static bool TryCompleteFromCurrentDrawingProperty(
            SectionViewAttributeResolution result,
            IntPtr drawingPropertiesDialogHandle,
            List<string> drawingPropertyFiles,
            List<string> viewAttributeFiles,
            bool isAssemblyDrawing,
            out string diagnostic
        )
        {
            diagnostic = "The current Drawing Properties Save/Load value "
                + "did not resolve to one usable drawing property file.";
            if (
                result == null
                || drawingPropertiesDialogHandle == IntPtr.Zero
                || drawingPropertyFiles == null
                || drawingPropertyFiles.Count == 0
            )
                return false;

            List<string> allValues = new List<string>();
            List<string> preferredValues = new List<string>();
            CollectNativeWindowValues(
                drawingPropertiesDialogHandle,
                allValues,
                preferredValues
            );

            for (int i = 0; i < allValues.Count; i++)
                AddUniqueLimited(result.LiveDialogValues, allValues[i], 150);

            string drawingPropertyFile = FindViewAttributeFileFromDialogValues(
                preferredValues,
                drawingPropertyFiles
            );
            if (string.IsNullOrWhiteSpace(drawingPropertyFile))
            {
                drawingPropertyFile = FindViewAttributeFileFromDialogValues(
                    allValues,
                    drawingPropertyFiles
                );
            }
            if (string.IsNullOrWhiteSpace(drawingPropertyFile))
                return false;

            DrawingPropertyData data = ReadDrawingProperty(drawingPropertyFile);
            DrawingViewRow sectionRow = FindSectionRow(data);
            if (sectionRow == null)
            {
                diagnostic = "The selected drawing property has no Section views row.";
                return false;
            }
            // The row's creation switch controls Tekla's automatic drawing
            // generation, not whether its View property can be loaded for an
            // explicit Auto Section command. Preserve EnabledCode as evidence,
            // but resolve the configured property even when creation is off.

            string attributeName = NormalizeViewAttributeName(
                sectionRow.ViewAttributeName
            );
            List<string> attributeValue = new List<string>();
            attributeValue.Add(attributeName);
            string viewAttributeFile = FindViewAttributeFileFromDialogValues(
                attributeValue,
                viewAttributeFiles
            );
            if (string.IsNullOrWhiteSpace(viewAttributeFile))
            {
                diagnostic = "Section views references missing View property: "
                    + attributeName
                    + ".";
                return false;
            }

            result.DrawingAttributeName = Path.GetFileNameWithoutExtension(
                drawingPropertyFile
            );
            CompleteFromPropertyFile(
                result,
                data,
                sectionRow,
                isAssemblyDrawing
                    ? "Live Assembly drawing properties / Get / drawing property file"
                    : "Live Single-part drawing properties / Get / drawing property file"
            );
            result.Error = "";
            result.LiveDialogError = "";
            diagnostic = "";
            return result.Success;
        }

        private static void CollectNativeWindowValues(
            IntPtr dialogHandle,
            List<string> allValues,
            List<string> preferredValues
        )
        {
            if (dialogHandle == IntPtr.Zero)
                return;

            AddNativeControlValue(dialogHandle, allValues, preferredValues);

            try
            {
                EnumWindowsCallback callback = delegate(IntPtr childHandle, IntPtr parameter)
                {
                    AddNativeControlValue(childHandle, allValues, preferredValues);
                    return true;
                };

                EnumChildWindows(dialogHandle, callback, IntPtr.Zero);
            }
            catch { }
        }

        private static void AddNativeControlValue(
            IntPtr windowHandle,
            List<string> allValues,
            List<string> preferredValues
        )
        {
            // Tekla creates the controls for inactive property pages too.
            // Reading only visible controls prevents a hidden page's
            // "standard" value from being mistaken for the selected row.
            if (!IsWindowVisible(windowHandle))
                return;

            string className = ReadNativeWindowClass(windowHandle);
            string text = ReadNativeWindowText(windowHandle);

            AddUniqueLimited(allValues, text, 150);

            bool isPreferredControl =
                className.IndexOf("ComboBox", StringComparison.OrdinalIgnoreCase) >= 0
                || className.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isPreferredControl)
                AddUniqueLimited(preferredValues, text, 150);

            if (className.IndexOf("ComboBox", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string selectedText = ReadNativeComboSelection(windowHandle);
                AddUniqueLimited(allValues, selectedText, 150);
                AddUniqueLimited(preferredValues, selectedText, 150);
            }
        }

        private static string ReadNativeWindowClass(IntPtr windowHandle)
        {
            try
            {
                StringBuilder value = new StringBuilder(256);
                GetClassName(windowHandle, value, value.Capacity);
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadNativeWindowText(IntPtr windowHandle)
        {
            try
            {
                StringBuilder value = new StringBuilder(1024);
                GetWindowText(windowHandle, value, value.Capacity);
                if (value.Length > 0)
                    return value.ToString();

                IntPtr messageResult;
                SendMessageTimeoutText(
                    windowHandle,
                    WmGetText,
                    new IntPtr(value.Capacity),
                    value,
                    SendMessageAbortIfHung,
                    200,
                    out messageResult
                );
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadNativeComboSelection(IntPtr comboHandle)
        {
            try
            {
                IntPtr selectionResult;
                SendMessageTimeoutPointer(
                    comboHandle,
                    CbGetCurrentSelection,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    SendMessageAbortIfHung,
                    200,
                    out selectionResult
                );

                int selectionIndex = selectionResult.ToInt32();
                if (selectionIndex < 0)
                    return "";

                StringBuilder value = new StringBuilder(1024);
                IntPtr textResult;
                SendMessageTimeoutText(
                    comboHandle,
                    CbGetListText,
                    new IntPtr(selectionIndex),
                    value,
                    SendMessageAbortIfHung,
                    200,
                    out textResult
                );
                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static void InvokeDisplayDrawingPropertiesDialog(
            int teklaProcessId,
            bool isAssemblyDrawing
        )
        {
            object proxy = null;
            MethodInfo unsubscribe = null;
            bool subscribed = false;

            try
            {
                Process teklaProcess = Process.GetProcessById(teklaProcessId);
                string teklaBin = teklaProcess.MainModule == null
                    ? ""
                    : Path.GetDirectoryName(teklaProcess.MainModule.FileName);
                if (string.IsNullOrWhiteSpace(teklaBin))
                    teklaBin = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location);

                string macroAkitFile = Path.Combine(teklaBin, "Tekla.Macros.Akit.dll");
                string akitFile = Path.Combine(teklaBin, "Akit5.dll");
                if (!File.Exists(macroAkitFile) || !File.Exists(akitFile))
                    throw new FileNotFoundException(
                        "Tekla Akit assemblies were not found in " + teklaBin + "."
                    );

                System.Reflection.Assembly macroAkitAssembly =
                    System.Reflection.Assembly.LoadFrom(macroAkitFile);
                System.Reflection.Assembly akitAssembly =
                    System.Reflection.Assembly.LoadFrom(akitFile);
                Type proxyType = macroAkitAssembly.GetType(
                    "Tekla.Macros.Akit.DynamicScriptMessengerClientProxy",
                    true
                );
                Type scriptType = akitAssembly.GetType(
                    "Tekla.Technology.Akit.IScript",
                    true
                );

                MethodInfo create = proxyType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static
                );
                MethodInfo subscribe = proxyType.GetMethod(
                    "Subscribe",
                    BindingFlags.Public | BindingFlags.Instance
                );
                unsubscribe = proxyType.GetMethod(
                    "Unsubscribe",
                    BindingFlags.Public | BindingFlags.Instance
                );
                MethodInfo getRemoteScript = proxyType.GetMethod(
                    "GetRemoteScriptAdapter",
                    BindingFlags.Public | BindingFlags.Instance
                );
                MethodInfo callback = scriptType.GetMethod(
                    "Callback",
                    new Type[] { typeof(string), typeof(string), typeof(string) }
                );
                if (
                    create == null
                    || subscribe == null
                    || unsubscribe == null
                    || getRemoteScript == null
                    || callback == null
                )
                {
                    throw new MissingMethodException(
                        "Required Tekla Akit drawing-property methods are missing."
                    );
                }

                proxy = create.Invoke(null, new object[] { null });
                if (proxy == null)
                    throw new InvalidOperationException("Tekla Akit proxy is null.");

                object subscribeResult = subscribe.Invoke(
                    proxy,
                    new object[] { teklaProcessId }
                );
                subscribed = subscribeResult is bool && (bool)subscribeResult;
                if (!subscribed)
                    throw new InvalidOperationException(
                        "Cannot subscribe to the active Tekla process."
                    );

                object script = getRemoteScript.Invoke(
                    proxy,
                    new object[] { teklaProcessId }
                );
                if (script == null)
                    throw new InvalidOperationException(
                        "Tekla remote Akit script adapter is null."
                    );

                string dialogId = GetDrawingPropertiesDialogId(isAssemblyDrawing);
                callback.Invoke(
                    script,
                    new object[]
                    {
                        "acmd_display_attr_dialog",
                        dialogId,
                        "main_frame"
                    }
                );
            }
            finally
            {
                if (subscribed && proxy != null && unsubscribe != null)
                {
                    try
                    {
                        unsubscribe.Invoke(proxy, new object[] { teklaProcessId });
                    }
                    catch { }
                }

                DisposeAkitMessengerProxy(proxy);
            }
        }

        private static string GetDrawingPropertiesDialogId(bool isAssemblyDrawing)
        {
            return isAssemblyDrawing
                ? AssemblyDrawingPropertiesDialogId
                : SinglePartDrawingPropertiesDialogId;
        }

        private static bool TryTargetSectionViewRowWithAkit(
            int teklaProcessId,
            bool isAssemblyDrawing,
            out string error
        )
        {
            error = "";
            object proxy = null;
            MethodInfo unsubscribe = null;
            bool subscribed = false;

            try
            {
                string teklaBin = "";
                try
                {
                    Process teklaProcess = Process.GetProcessById(teklaProcessId);
                    if (teklaProcess.MainModule != null)
                    {
                        teklaBin = Path.GetDirectoryName(teklaProcess.MainModule.FileName);
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(teklaBin))
                {
                    teklaBin = Path.GetDirectoryName(typeof(DrawingHandler).Assembly.Location);
                }
                string macroAkitFile = Path.Combine(teklaBin, "Tekla.Macros.Akit.dll");
                string akitFile = Path.Combine(teklaBin, "Akit5.dll");

                if (!File.Exists(macroAkitFile) || !File.Exists(akitFile))
                {
                    error = "Tekla Akit assemblies were not found.";
                    return false;
                }

                System.Reflection.Assembly macroAkitAssembly = System.Reflection.Assembly.LoadFrom(
                    macroAkitFile
                );
                System.Reflection.Assembly akitAssembly = System.Reflection.Assembly.LoadFrom(
                    akitFile
                );

                Type proxyType = macroAkitAssembly.GetType(
                    "Tekla.Macros.Akit.DynamicScriptMessengerClientProxy",
                    true
                );
                Type scriptType = akitAssembly.GetType("Tekla.Technology.Akit.IScript", true);

                MethodInfo create = proxyType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static
                );
                MethodInfo subscribe = proxyType.GetMethod(
                    "Subscribe",
                    BindingFlags.Public | BindingFlags.Instance
                );
                unsubscribe = proxyType.GetMethod(
                    "Unsubscribe",
                    BindingFlags.Public | BindingFlags.Instance
                );
                MethodInfo getRemoteScript = proxyType.GetMethod(
                    "GetRemoteScriptAdapter",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (
                    create == null
                    || subscribe == null
                    || unsubscribe == null
                    || getRemoteScript == null
                )
                {
                    error = "Required Tekla Akit messenger methods are missing.";
                    return false;
                }

                proxy = create.Invoke(null, new object[] { null });
                if (proxy == null)
                {
                    error = "Tekla Akit messenger proxy is null.";
                    return false;
                }

                object subscribeResult = subscribe.Invoke(proxy, new object[] { teklaProcessId });
                subscribed = subscribeResult is bool && (bool)subscribeResult;
                if (!subscribed)
                {
                    error = "Cannot subscribe to the active Tekla process.";
                    return false;
                }

                object script = getRemoteScript.Invoke(proxy, new object[] { teklaProcessId });
                if (script == null)
                {
                    error = "Tekla remote Akit script adapter is null.";
                    return false;
                }

                MethodInfo tabChange = scriptType.GetMethod(
                    "TabChange",
                    new Type[] { typeof(string), typeof(string), typeof(string) }
                );
                MethodInfo treeSelect = scriptType.GetMethod(
                    "TreeSelect",
                    new Type[] { typeof(string), typeof(string), typeof(string) }
                );
                MethodInfo tableSelect = scriptType.GetMethod(
                    "TableSelect",
                    new Type[] { typeof(string), typeof(string), typeof(int[]) }
                );

                if (
                    treeSelect == null
                    || tableSelect == null
                    || (!isAssemblyDrawing && tabChange == null)
                )
                {
                    error = "Required Tekla Akit dialog methods are missing.";
                    return false;
                }

                string drawingPropertiesDialogId = GetDrawingPropertiesDialogId(
                    isAssemblyDrawing
                );

                treeSelect.Invoke(
                    script,
                    new object[]
                    {
                        drawingPropertiesDialogId,
                        DrawingPropertiesMenuTreeId,
                        DrawingPropertiesViewCreationNode
                    }
                );
                Thread.Sleep(150);
                if (!isAssemblyDrawing)
                {
                    tabChange.Invoke(
                        script,
                        new object[]
                        {
                            drawingPropertiesDialogId,
                            DrawingPropertiesMainContainerId,
                            DrawingPropertiesViewsTabId
                        }
                    );
                    Thread.Sleep(150);
                }
                tableSelect.Invoke(
                    script,
                    new object[]
                    {
                        drawingPropertiesDialogId,
                        DrawingPropertiesViewsTableId,
                        new int[] { SectionViewTableRowIndex }
                    }
                );
                return true;
            }
            catch (Exception ex)
            {
                Exception real = ex.InnerException ?? ex;
                error = real.GetType().Name + ": " + real.Message;
                return false;
            }
            finally
            {
                if (subscribed && proxy != null && unsubscribe != null)
                {
                    try
                    {
                        unsubscribe.Invoke(proxy, new object[] { teklaProcessId });
                    }
                    catch { }
                }

                DisposeAkitMessengerProxy(proxy);
            }
        }

        private static void DisposeAkitMessengerProxy(object proxy)
        {
            if (proxy == null)
                return;

            try
            {
                Type proxyType = proxy.GetType();
                PropertyInfo messengerClientProperty = proxyType.GetProperty(
                    "MessengerClient",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                object messengerClient =
                    messengerClientProperty == null
                        ? null
                        : messengerClientProperty.GetValue(proxy, null);
                if (messengerClient == null)
                    return;

                PropertyInfo messengerProperty = messengerClient
                    .GetType()
                    .GetProperty(
                        "Messenger",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                object messenger =
                    messengerProperty == null
                        ? null
                        : messengerProperty.GetValue(messengerClient, null);

                IDisposable disposableMessenger = messenger as IDisposable;
                if (disposableMessenger != null)
                    disposableMessenger.Dispose();
            }
            catch { }
        }

        private static bool TryOpenSelectedViewPropertiesDialog(
            IntPtr drawingPropertiesDialogHandle,
            int teklaProcessId,
            out IntPtr viewPropertiesDialogHandle,
            out string error
        )
        {
            viewPropertiesDialogHandle = IntPtr.Zero;
            error = "";

            IntPtr viewPropertiesButton = FindVisibleNativeChildButton(
                drawingPropertiesDialogHandle,
                "View properties"
            );
            if (viewPropertiesButton == IntPtr.Zero)
            {
                error = "The visible View properties button was not found.";
                return false;
            }

            List<NativeWindowInfo> beforeWindows = GetVisibleNativeWindows(teklaProcessId);
            HashSet<long> beforeHandles = new HashSet<long>();
            for (int i = 0; i < beforeWindows.Count; i++)
                beforeHandles.Add(beforeWindows[i].Handle.ToInt64());

            if (!PostMessage(viewPropertiesButton, BmClick, IntPtr.Zero, IntPtr.Zero))
            {
                error = "Cannot click the View properties button.";
                return false;
            }

            for (int attempt = 0; attempt < 40; attempt++)
            {
                Thread.Sleep(100);
                List<NativeWindowInfo> currentWindows = GetVisibleNativeWindows(teklaProcessId);
                int bestScore = int.MinValue;

                for (int windowIndex = 0; windowIndex < currentWindows.Count; windowIndex++)
                {
                    NativeWindowInfo window = currentWindows[windowIndex];
                    long handleValue = window.Handle.ToInt64();
                    if (
                        handleValue == 0
                        || beforeHandles.Contains(handleValue)
                        || IsIgnoredNativeWindowClass(window.ClassName)
                    )
                    {
                        continue;
                    }

                    int score = ScoreNativeDialogWindow(window);
                    if (score <= 0 || score <= bestScore)
                        continue;

                    bestScore = score;
                    viewPropertiesDialogHandle = window.Handle;
                }

                if (viewPropertiesDialogHandle != IntPtr.Zero)
                    return true;
            }

            error = "No new View properties window was detected.";
            return false;
        }

        private static IntPtr FindVisibleNativeChildButton(IntPtr parentWindow, string expectedText)
        {
            IntPtr result = IntPtr.Zero;
            if (parentWindow == IntPtr.Zero || string.IsNullOrWhiteSpace(expectedText))
            {
                return result;
            }

            try
            {
                EnumWindowsCallback callback = delegate(IntPtr childHandle, IntPtr parameter)
                {
                    if (!IsWindowVisible(childHandle))
                        return true;

                    string className = ReadNativeWindowClass(childHandle);
                    if (!string.Equals(className, "Button", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    string text = ReadNativeWindowText(childHandle).Replace("&", "").Trim();
                    if (!string.Equals(text, expectedText, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    result = childHandle;
                    return false;
                };

                EnumChildWindows(parentWindow, callback, IntPtr.Zero);
            }
            catch { }

            return result;
        }

        private static string FindViewAttributeFileFromDialogValues(
            List<string> values,
            List<string> viewAttributeFiles
        )
        {
            if (values == null || viewAttributeFiles == null)
                return "";

            for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
            {
                string dialogValue = NormalizeDialogViewAttributeName(values[valueIndex]);
                if (string.IsNullOrWhiteSpace(dialogValue))
                    continue;

                for (int fileIndex = 0; fileIndex < viewAttributeFiles.Count; fileIndex++)
                {
                    string file = viewAttributeFiles[fileIndex];
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);
                    if (!string.Equals(dialogValue, baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return file;
                }
            }

            return "";
        }

        private static string NormalizeDialogViewAttributeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string result = value.Trim().Trim('"').Trim('*').Trim();
            try
            {
                string extension = Path.GetExtension(result);
                if (
                    string.Equals(extension, ".vi", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".wd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".ad", StringComparison.OrdinalIgnoreCase)
                )
                {
                    result = Path.GetFileNameWithoutExtension(result);
                }
            }
            catch { }

            return result;
        }

        private static bool CloseNativeWindowAndWait(
            IntPtr nativeWindowHandle,
            int timeoutMilliseconds
        )
        {
            try
            {
                if (nativeWindowHandle == IntPtr.Zero)
                    return true;

                if (!IsWindow(nativeWindowHandle))
                    return true;

                bool closePosted = PostMessage(
                    nativeWindowHandle,
                    WmClose,
                    IntPtr.Zero,
                    IntPtr.Zero
                );

                if (!closePosted && IsWindow(nativeWindowHandle))
                    return false;

                int elapsedMilliseconds = 0;
                while (IsWindow(nativeWindowHandle) && elapsedMilliseconds < timeoutMilliseconds)
                {
                    Thread.Sleep(50);
                    elapsedMilliseconds += 50;
                }

                return !IsWindow(nativeWindowHandle);
            }
            catch
            {
                return false;
            }
        }

        private static void AddUniqueLimited(List<string> values, string value, int maximumCount)
        {
            if (values == null || values.Count >= maximumCount || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            AddUnique(values, value.Trim());
        }

        private static SectionViewAttributeResolution CompleteFromPropertyFile(
            SectionViewAttributeResolution result,
            DrawingPropertyData data,
            DrawingViewRow sectionRow,
            string source
        )
        {
            string attributeName = NormalizeViewAttributeName(sectionRow.ViewAttributeName);

            if (string.IsNullOrWhiteSpace(attributeName))
            {
                result.Error = "Section views row exists but View properties is empty.";
                return result;
            }

            result.Success = true;
            result.AttributeName = attributeName;
            result.Source = source;
            result.DrawingPropertyFile = data.File;
            result.EnabledCode = sectionRow.EnabledCode;
            result.RawDrawingViews = data.RawDrawingViews;
            AddUnique(result.MatchingPropertyFiles, data.File);
            return result;
        }

        private static string NormalizeViewAttributeName(string value)
        {
            // Tekla represents an omitted property name as the standard View
            // property when the row is otherwise usable.
            return string.IsNullOrWhiteSpace(value) ? "standard" : value.Trim().Trim('"');
        }

        private static int ScoreCandidate(
            DrawingPropertyData data,
            List<string> activeViewAttributeNames,
            List<string> derivedPropertyNames
        )
        {
            int score = 0;
            string propertyBaseName = Path.GetFileNameWithoutExtension(data.File);

            for (int i = 0; i < derivedPropertyNames.Count; i++)
            {
                if (
                    string.Equals(
                        propertyBaseName,
                        derivedPropertyNames[i],
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    score += 1000;
                    break;
                }
            }

            for (int hintIndex = 0; hintIndex < activeViewAttributeNames.Count; hintIndex++)
            {
                bool found = false;
                for (int rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
                {
                    if (
                        string.Equals(
                            data.Rows[rowIndex].ViewAttributeName,
                            activeViewAttributeNames[hintIndex],
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    score += 100;
            }

            return score;
        }

        private static DrawingPropertyData ReadDrawingProperty(string file)
        {
            DrawingPropertyData data = new DrawingPropertyData();
            data.File = file ?? "";

            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return data;

            try
            {
                string[] lines = File.ReadAllLines(file, Encoding.Default);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (
                        line == null
                        || !line.TrimStart()
                            .StartsWith(DrawingViewsKey, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        continue;
                    }

                    int firstQuote = line.IndexOf('"');
                    int lastQuote = line.LastIndexOf('"');
                    if (firstQuote < 0 || lastQuote <= firstQuote)
                        break;

                    data.RawDrawingViews = line.Substring(
                        firstQuote + 1,
                        lastQuote - firstQuote - 1
                    );

                    string[] values = data.RawDrawingViews.Split(
                        new char[] { ',' },
                        StringSplitOptions.None
                    );

                    for (int valueIndex = 0; valueIndex + 3 < values.Length; valueIndex += 4)
                    {
                        DrawingViewRow row = new DrawingViewRow();
                        row.TypeCode = values[valueIndex].Trim();
                        row.EnabledCode = values[valueIndex + 1].Trim();
                        row.ReservedValue = values[valueIndex + 2].Trim();
                        row.ViewAttributeName = values[valueIndex + 3].Trim().Trim('"');
                        data.Rows.Add(row);
                    }

                    break;
                }
            }
            catch { }

            return data;
        }

        private static DrawingViewRow FindSectionRow(DrawingPropertyData data)
        {
            if (data == null)
                return null;

            for (int i = 0; i < data.Rows.Count; i++)
            {
                if (
                    string.Equals(
                        data.Rows[i].TypeCode,
                        SectionViewTypeCode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return data.Rows[i];
                }
            }

            return null;
        }

        private static string ReadExistingSectionViewAttribute(Drawing drawing)
        {
            try
            {
                DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null || !IsSectionView(view))
                        continue;

                    string value = ReadAttributeFilename(view.Attributes);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
            catch { }

            return "";
        }

        private static bool IsSectionView(View view)
        {
            if (view == null)
                return false;

            string typeName = view.ViewType.ToString();
            return string.Equals(typeName, "SectionView", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Section", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadActiveViewAttributeNames(Drawing drawing, List<string> result)
        {
            try
            {
                DrawingObjectEnumerator views = drawing.GetSheet().GetAllViews();
                while (views.MoveNext())
                {
                    View view = views.Current as View;
                    if (view == null)
                        continue;

                    AddUnique(result, ReadAttributeFilename(view.Attributes));
                }
            }
            catch { }
        }

        private static string ReadAttributeFilename(object source)
        {
            if (source == null)
                return "";

            for (Type type = source.GetType(); type != null; type = type.BaseType)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        "AttributeFilename",
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.DeclaredOnly
                    );

                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        object value = property.GetValue(source, null);
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            return value.ToString().Trim();
                        }
                    }
                }
                catch { }

                try
                {
                    string[] fieldNames = new string[]
                    {
                        "AttributeFilename",
                        "_AttributeFilename"
                    };

                    for (int i = 0; i < fieldNames.Length; i++)
                    {
                        FieldInfo field = type.GetField(
                            fieldNames[i],
                            BindingFlags.Public
                                | BindingFlags.NonPublic
                                | BindingFlags.Instance
                                | BindingFlags.DeclaredOnly
                        );

                        if (field == null)
                            continue;

                        object value = field.GetValue(source);
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        {
                            return value.ToString().Trim();
                        }
                    }
                }
                catch { }
            }

            return "";
        }

        private static List<string> BuildDrawingPropertyNameCandidates(string viewAttributeName)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(viewAttributeName))
                return result;

            string name = Path.GetFileNameWithoutExtension(viewAttributeName.Trim().Trim('"'));
            if (name.StartsWith("new_", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(4);
            }

            AddUnique(result, name);

            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < name.Length - 1)
            {
                int suffixNumber;
                if (int.TryParse(name.Substring(lastUnderscore + 1), out suffixNumber))
                {
                    AddUnique(result, name.Substring(0, lastUnderscore));
                }
            }

            return result;
        }

        private static List<string> CollectAttributeFiles(string extension, string modelPath)
        {
            List<string> result = new List<string>();
            string suffix = extension.TrimStart('.');

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                try
                {
                    string directory = Path.Combine(modelPath, "attributes");
                    if (Directory.Exists(directory))
                    {
                        string[] files = Directory.GetFiles(
                            directory,
                            "*." + suffix,
                            SearchOption.TopDirectoryOnly
                        );
                        for (int i = 0; i < files.Length; i++)
                            AddUnique(result, files[i]);
                    }
                }
                catch { }
            }

            try
            {
                List<string> names =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles.GetAttributeFiles(suffix);
                if (names != null)
                {
                    for (int i = 0; i < names.Count; i++)
                    {
                        string file = ResolveAttributeFile(names[i], extension, modelPath);
                        AddUnique(result, file);
                    }
                }
            }
            catch { }

            return result;
        }

        private static string ResolveAttributeFile(
            string attributeName,
            string expectedExtension,
            string modelPath
        )
        {
            if (string.IsNullOrWhiteSpace(attributeName))
                return "";

            string trimmedName = attributeName.Trim().Trim('"');
            try
            {
                if (File.Exists(trimmedName))
                    return Path.GetFullPath(trimmedName);
            }
            catch { }

            string fileName = Path.GetFileName(trimmedName);
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                fileName += expectedExtension;

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                try
                {
                    string file = Path.Combine(Path.Combine(modelPath, "attributes"), fileName);
                    if (File.Exists(file))
                        return file;
                }
                catch { }
            }

            try
            {
                FileInfo file =
                    Tekla.Structures.Dialog.UIControls.EnvironmentFiles.GetAttributeFile(fileName);
                if (file != null && file.Exists)
                    return file.FullName;
            }
            catch { }

            return "";
        }

        private static string ReadModelPath(Model model)
        {
            try
            {
                if (model == null || !model.GetConnectionStatus())
                    return "";

                ModelInfo info = model.GetInfo();
                return info == null ? "" : (info.ModelPath ?? "");
            }
            catch
            {
                return "";
            }
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
                return;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            values.Add(value);
        }
    }
}
#pragma warning restore
#endregion
