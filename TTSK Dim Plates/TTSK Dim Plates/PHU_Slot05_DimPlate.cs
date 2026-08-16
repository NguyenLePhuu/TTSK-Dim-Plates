#pragma warning disable 1633

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Drawing;

using TSM = Tekla.Structures.Model;
using TSD = Tekla.Structures.Drawing;
using ModelPart = Tekla.Structures.Model.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using ModelBoltGroup = Tekla.Structures.Model.BoltGroup;
using DrawingPart = Tekla.Structures.Drawing.Part;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 05 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05.Run()
    public class PHU_AutoDimSlot05
    {
        public static string Run()
        {
            PHU_Slot05_SelectedPlateEdgeHoleDim.Run();
            return "OK";
        }

        // Read-only entry point cho kiểm tra active drawing trước khi chạy thật.
        public static string AuditPlan()
        {
            return PHU_Slot05_SelectedPlateEdgeHoleDim.AuditAutomaticTarget();
        }
    }

    // Slot 05 thuật toán 2 cho MainForm switch:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot05_TopBottomMode.Run()
    public class PHU_AutoDimSlot05_TopBottomMode
    {
        public static string Run()
        {
            PHU_Slot05_SelectedPlateEdgeHoleDim.RunTopBottomMode();
            return "OK";
        }
    }

    public class PHU_Slot05_SelectedPlateEdgeHoleDim
    {
        private const double TOL = 1.0;
        private const double POINT_DUP_TOL = 0.5;
        private const double PLATE_BOUND_TOL = 25.0;

        // SLOT05 - QUY TẮC TẦNG DIM
        // Tầng 1 ngang: mép plate -> tất cả lỗ trong plate -> mép plate.
        // Tầng 2 ngang: mép main -> mép plate -> mép plate -> mép main.
        // Dim dọc: mép main -> mép plate -> mép plate -> mép main.
        private const double SLOT05_DIM_TIER_BASE = 150.0;
        private const double SLOT05_DIM_TIER_STEP = 150.0;

        // Tolerance nhận biết plate nằm gần mép trái/phải main.
        private const double SLOT05_NEAR_MAIN_EDGE_TOL = 300.0;

        // Tầng ngang nội bộ plate: tất cả plate dùng chung 1 tầng, không tăng tầng theo số plate.
        private const double SLOT05_INTERNAL_PLATE_HOLE_TIER = 150.0;

        // Tầng ngang tổng main/plate.
        private const double SLOT05_MAIN_PLATE_CHAIN_TIER = 300.0;

        // AUTO 5-1 - nhận diện đúng loại plate ôm/bậc quanh mép main như hình mẫu.
        // Các tolerance này chỉ dùng để phân loại quan hệ; chân dim vẫn lấy từ hình học thật.
        private const double SLOT05_AUTO_EDGE_CONTACT_TOL = 5.0;
        private const double SLOT05_AUTO_PROFILE_POINT_TOL = 2.0;
        private const double SLOT05_AUTO_MIN_SIDE_OVERLAP_RATIO = 0.35;
        private const double SLOT05_AUTO_MAX_NORMAL_SIZE_RATIO = 0.50;
        private const int SLOT05_AUTO_MIN_PROFILE_POINTS = 6;
        private const double SLOT05_AUTO_MIN_LONGITUDINAL_ASPECT = 3.0;
        private const double SLOT05_AUTO_MIN_PLATE_FACE_RATIO = 0.20;
        private const double SLOT05_AUTO_STRAIGHT_MIN_SPAN_RATIO = 0.50;
        private const double SLOT05_AUTO_STRAIGHT_MAX_NORMAL_RATIO = 0.30;
        private const double SLOT05_SECTION_NEAR_TIER_PAPER = 18.0;
        private const double SLOT05_SECTION_FAR_TIER_PAPER = 38.0;
        private const double SLOT05_SECTION_SIGNATURE_TOL = 1.0;

        public static string AuditAutomaticTarget()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("SLOT05 5-1 AUTO LINK AUDIT - READ ONLY");

            TSD.DrawingHandler drawingHandler = new TSD.DrawingHandler();
            if (!drawingHandler.GetConnectionStatus())
                return text.AppendLine("ERROR DrawingHandler is not connected.").ToString();

            TSD.Drawing drawing = drawingHandler.GetActiveDrawing();
            if (drawing == null)
                return text.AppendLine("ERROR No active drawing.").ToString();

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
                return text.AppendLine("ERROR Model is not connected.").ToString();

            ModelPart mainPart = PHU_MainPartResolver.Resolve(model, drawing);
            if (mainPart == null)
                return text.AppendLine("ERROR Main Part could not be resolved.").ToString();

            text.Append("DrawingType=").Append(drawing.GetType().FullName).AppendLine();
            text.Append("Main=").Append(DescribePartForAudit(mainPart)).AppendLine();

            string diagnostic;
            Slot05AutoTarget target = FindAutomaticSlot05Target(
                model,
                drawing,
                mainPart,
                out diagnostic);

            if (target != null && target.Plates.Count > 0)
            {
                TSM.TransformationPlane oldPlaneAudit = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TSM.TransformationPlane(target.View.DisplayCoordinateSystem));
                    Bounds2D mBox = GetPartBounds2D(model.SelectModelObject(mainPart.Identifier) as ModelPart);
                    Slot05MainOrientation ori = DetectOrientation(mBox);
                    text.Append("Orientation=").Append(ori.ToString()).AppendLine();
                }
                finally
                {
                    try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlaneAudit); } catch { }
                }
            }
            text.Append("Decision=").Append(target == null ? "REJECT" : "ACCEPT").AppendLine();
            text.Append("Reason=").Append(diagnostic).AppendLine();

            if (target == null || target.View == null)
            {
                text.AppendLine("No dimension was created, deleted, modified or committed.");
                return text.ToString();
            }

            text.Append("ClassificationView=")
                .Append(DescribeViewForAudit(target.ClassificationView)).AppendLine();
            text.Append("TargetFrontView=")
                .Append(DescribeViewForAudit(target.View)).AppendLine();
            text.Append("AcceptedModelPlateCount=")
                .Append(target.MatchedPlateCount).AppendLine();
            text.Append("DimensionGeometryGroupCount=")
                .Append(target.Plates.Count).AppendLine();
            text.Append("DirectBoltConnectionCount=")
                .Append(target.DirectConnectionCount).AppendLine();
            text.Append("GeometryScore=")
                .Append(target.GeometryScore.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine();
            for (int selectedIndex = 0; selectedIndex < target.Plates.Count; selectedIndex++)
            {
                text.Append("TargetPlate[").Append(selectedIndex).Append("]=")
                    .Append(DescribePartForAudit(target.Plates[selectedIndex]))
                    .AppendLine();
            }

            string frontFallbackDiagnostic;
            Slot05AutoTarget frontFallback = FindFrontFaceFallbackTarget(
                model,
                drawing,
                mainPart,
                out frontFallbackDiagnostic);
            text.Append("FrontOnlyFallbackDecision=")
                .Append(frontFallback == null ? "REJECT" : "ACCEPT")
                .Append(" count=")
                .Append(frontFallback == null ? 0 : frontFallback.Plates.Count)
                .Append(" matchesPrimary=")
                .Append(frontFallback != null && PartIdentifierSetsMatch(
                    frontFallback.Plates,
                    target.Plates))
                .AppendLine();
            text.Append("FrontOnlyFallbackReason=")
                .Append(frontFallbackDiagnostic).AppendLine();

            string sectionDiagnostic;
            List<Slot05SectionDimPlan> sectionPlans = BuildSlot05SectionDimPlans(
                model,
                mainPart,
                target,
                out sectionDiagnostic);
            bool sectionReady = sectionPlans.Count > 0;
            text.Append("SectionDecision=")
                .Append(sectionReady ? "ACCEPT" : "SKIP").AppendLine();
            text.Append("SectionReason=").Append(sectionDiagnostic).AppendLine();
            text.Append("MatchingSectionViewCount=")
                .Append(target.SectionViews.Count).AppendLine();
            for (int sectionIndex = 0;
                sectionIndex < sectionPlans.Count;
                sectionIndex++)
            {
                Slot05SectionDimPlan sectionPlan = sectionPlans[sectionIndex];
                text.Append("SectionPlan[").Append(sectionIndex).Append("]View=")
                    .Append(DescribeViewForAudit(sectionPlan.View)).AppendLine();
                text.Append("SectionPlan[").Append(sectionIndex)
                    .Append("]PlateChain=")
                    .Append(FormatPointsForAudit(new List<Point>(sectionPlan.PlateChain)))
                    .Append(" direction=")
                    .Append(FormatVectorForAudit(sectionPlan.Direction))
                    .Append(" distance=")
                    .Append(sectionPlan.PlateChainDistance.ToString(
                        "0.###", CultureInfo.InvariantCulture))
                    .AppendLine();
                text.Append("SectionPlan[").Append(sectionIndex)
                    .Append("]BoltChain=")
                    .Append(FormatPointsForAudit(new List<Point>(sectionPlan.BoltChain)))
                    .Append(" direction=")
                    .Append(FormatVectorForAudit(sectionPlan.Direction))
                    .Append(" distance=")
                    .Append(sectionPlan.BoltChainDistance.ToString(
                        "0.###", CultureInfo.InvariantCulture))
                    .AppendLine();
            }

            AppendSlot05ClassificationAudit(
                text,
                model,
                mainPart,
                target.ClassificationView,
                target.AllMatchedPlates);

            AppendSlot05CrossViewAudit(
                text,
                model,
                drawing,
                mainPart,
                target.AllMatchedPlates);

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(target.View.DisplayCoordinateSystem));

                Bounds2D mainBox = GetPartBounds2D(mainPart);
                text.Append("MainBounds=").Append(FormatBoundsForAudit(mainBox)).AppendLine();

                int representativePlatesWithHoles = 0;
                for (int representativeIndex = 0;
                    representativeIndex < target.Plates.Count;
                    representativeIndex++)
                {
                    ModelPart representative = target.Plates[representativeIndex];
                    Bounds2D representativeBounds = GetPartBounds2D(representative);
                    List<Point> representativeHoles = GetBoltCentersInsidePlate(
                        model,
                        target.View,
                        representative,
                        representativeBounds);
                    if (representativeHoles.Count > 0)
                        representativePlatesWithHoles++;
                    text.Append("DIM_TARGET_PLATE ")
                        .Append(DescribePartForAudit(representative))
                        .Append(" bounds=")
                        .Append(FormatBoundsForAudit(representativeBounds))
                        .Append(" holeCount=").Append(representativeHoles.Count)
                        .Append(" holeXY=").Append(FormatPointsForAudit(representativeHoles))
                        .Append(" holeGaps=")
                        .Append(FormatHoleGapsForAudit(representative, representativeHoles))
                        .AppendLine();
                }

                int expectedDimensionPlans = target.Plates.Count > 0 ? 1 : 0;
                expectedDimensionPlans += representativePlatesWithHoles * 2;
                text.Append("PlanPreflight=baseMainPlateChain:")
                    .Append(target.Plates.Count > 0 ? 1 : 0)
                    .Append(" internalPlateHoleChains:")
                    .Append(representativePlatesWithHoles)
                    .Append(" verticalChains:")
                    .Append(representativePlatesWithHoles)
                    .Append(" totalExpected:")
                    .Append(expectedDimensionPlans)
                    .AppendLine();
                text.Append("SectionPlanPreflight=")
                    .Append(sectionPlans.Count * 2)
                    .Append(" combinedTotalExpected=")
                    .Append(expectedDimensionPlans + (sectionPlans.Count * 2))
                    .AppendLine();
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }

            text.AppendLine("No dimension was created, deleted, modified or committed.");
            return text.ToString();
        }

        private static void AppendSlot05ClassificationAudit(
            StringBuilder text,
            TSM.Model model,
            ModelPart mainPart,
            TSD.View classificationView,
            List<ModelPart> matchedPlates)
        {
            if (text == null || model == null || mainPart == null ||
                classificationView == null || matchedPlates == null)
            {
                return;
            }

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(classificationView.DisplayCoordinateSystem));
                List<ModelPart> viewParts = GetAllModelPartsInView(model, classificationView);
                ModelPart mainInViewPlane = model.SelectModelObject(
                    mainPart.Identifier) as ModelPart;
                Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);

                for (int i = 0; i < matchedPlates.Count; i++)
                {
                    Identifier id = matchedPlates[i] == null
                        ? null
                        : matchedPlates[i].Identifier;
                    ModelPart candidate = FindPartByIdentifier(viewParts, id);
                    if (candidate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(candidate);
                    List<Point> profilePoints = GetExactProjectedProfilePoints(candidate);
                    double finalScore;
                    bool directConnection;
                    bool accepted = TryAnalyzeAutomaticWrapPlate(
                        mainInViewPlane,
                        mainBox,
                        candidate,
                        out finalScore,
                        out directConnection);
                    text.Append(accepted ? "LINK_ACCEPTED " : "LINK_REJECTED ")
                        .Append(DescribePartForAudit(candidate))
                        .Append(" bounds=").Append(FormatBoundsForAudit(plateBox))
                        .Append(" profilePoints=").Append(profilePoints.Count)
                        .Append(" profileXY=").Append(FormatPointsForAudit(profilePoints))
                        .Append(" score=")
                        .Append(accepted
                            ? finalScore.ToString("0.###", CultureInfo.InvariantCulture)
                            : "NA")
                        .AppendLine();
                }
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static void AppendSlot05CrossViewAudit(
            StringBuilder text,
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart mainPart,
            List<ModelPart> matchedPlates)
        {
            if (text == null || model == null || drawing == null || mainPart == null ||
                matchedPlates == null || matchedPlates.Count == 0)
            {
                return;
            }

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                    List<ModelPart> viewParts = GetAllModelPartsInView(model, view);
                    ModelPart mainInViewPlane = model.SelectModelObject(
                        mainPart.Identifier) as ModelPart;
                    if (!ContainsPartIdentifier(viewParts, mainPart.Identifier) ||
                        mainInViewPlane == null)
                    {
                        continue;
                    }

                    Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);

                    int visibleCount = 0;
                    double minCenter = double.PositiveInfinity;
                    double maxCenter = double.NegativeInfinity;
                    double mainWidth = mainBox.Valid ? mainBox.MaxX - mainBox.MinX : 0.0;
                    double mainHeight = mainBox.Valid ? mainBox.MaxY - mainBox.MinY : 0.0;
                    bool mainAxisIsX = mainWidth >= mainHeight;
                    StringBuilder platesText = new StringBuilder();
                    for (int i = 0; i < matchedPlates.Count; i++)
                    {
                        ModelPart visible = FindPartByIdentifier(
                            viewParts,
                            matchedPlates[i] == null ? null : matchedPlates[i].Identifier);
                        if (visible == null)
                            continue;

                        Bounds2D plateBox = GetPartBounds2D(visible);
                        if (!plateBox.Valid)
                            continue;

                        visibleCount++;
                        double center = mainAxisIsX
                            ? (plateBox.MinX + plateBox.MaxX) / 2.0
                            : (plateBox.MinY + plateBox.MaxY) / 2.0;
                        minCenter = Math.Min(minCenter, center);
                        maxCenter = Math.Max(maxCenter, center);
                        if (platesText.Length > 0)
                            platesText.Append(" | ");
                        platesText.Append(visible.Identifier.ID)
                            .Append(":")
                            .Append(FormatBoundsForAudit(plateBox));
                    }

                    double mainLong = Math.Max(mainWidth, mainHeight);
                    double mainShort = Math.Min(mainWidth, mainHeight);
                    double longitudinalAspect = mainShort > TOL ? mainLong / mainShort : 0.0;
                    double centerSpread = visibleCount > 1 ? maxCenter - minCenter : 0.0;
                    text.Append("ViewCandidate=")
                        .Append(DescribeViewForAudit(view))
                        .Append(" mainBounds=").Append(FormatBoundsForAudit(mainBox))
                        .Append(" longitudinalAspect=")
                        .Append(longitudinalAspect.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append(" matchedVisible=").Append(visibleCount)
                        .Append("/").Append(matchedPlates.Count)
                        .Append(" plateCenterSpread=")
                        .Append(centerSpread.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append(" existingDimensionObjects=")
                        .Append(CountDimensionObjectsInView(view))
                        .Append(" plates=").Append(platesText)
                        .AppendLine();
                }
            }
            catch (Exception ex)
            {
                text.Append("CrossViewAuditError=").Append(ex.Message).AppendLine();
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static int CountDimensionObjectsInView(TSD.View view)
        {
            int count = 0;
            try
            {
                if (view == null)
                    return count;

                TSD.DrawingObjectEnumerator objects = view.GetAllObjects();
                while (objects != null && objects.MoveNext())
                {
                    object value = objects.Current;
                    if (value != null && value.GetType().Name.IndexOf(
                        "Dimension",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        count++;
                    }
                }
            }
            catch
            {
            }

            return count;
        }

        public static void Run()
        {
            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                Msg("DrawingHandler chưa kết nối.");
                return;
            }

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                Msg("Không có active drawing.");
                return;
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                Msg("Model chưa kết nối.");
                return;
            }

            // Slot 5-1 không còn phụ thuộc selection. Main part lấy từ chính
            // AssemblyDrawing/SinglePartDrawing đang mở; plate và view được dò tự động.
            ModelPart mainPart = PHU_MainPartResolver.Resolve(model, drawing);
            if (mainPart == null)
            {
                Msg("Slot05 5-1: Không xác định được Main Part từ bản vẽ đang mở.");
                return;
            }

            string autoDiagnostic;
            Slot05AutoTarget autoTarget = FindAutomaticSlot05Target(
                model,
                drawing,
                mainPart,
                out autoDiagnostic);
            if (autoTarget == null || autoTarget.View == null || autoTarget.Plates.Count == 0)
            {
                Msg("Slot05 5-1: " + autoDiagnostic);
                return;
            }

            string sectionDiagnostic;
            List<Slot05SectionDimPlan> sectionPlans = BuildSlot05SectionDimPlans(
                model,
                mainPart,
                autoTarget,
                out sectionDiagnostic);

            int created = CreateSelectedPlateDims(
                model,
                drawing,
                autoTarget.View,
                autoTarget.Plates,
                mainPart);

            if (created <= 0)
            {
                Msg("Slot05 5-1: Đã nhận diện " + autoTarget.MatchedPlateCount.ToString() +
                    " plate liên kết nhưng không tạo được dimension hợp lệ.");
                return;
            }

            int sectionCreated = 0;
            for (int sectionIndex = 0;
                sectionIndex < sectionPlans.Count;
                sectionIndex++)
            {
                sectionCreated += CreateSlot05SectionDims(sectionPlans[sectionIndex]);
            }
            int expectedSectionCreated = sectionPlans.Count * 2;
            if (sectionCreated != expectedSectionCreated)
            {
                Msg("Slot05 5-1: Không tạo đủ " +
                    expectedSectionCreated.ToString() +
                    " dimension cho các section hợp lệ; bản vẽ chưa được commit " +
                    "để tránh lưu kết quả thiếu.");
                return;
            }

            try { drawing.CommitChanges(); } catch { }

            // Không popup hoàn thành để chạy gọn.
        }

        public static void RunTopBottomMode()
        {
            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                Msg("DrawingHandler chưa kết nối.");
                return;
            }

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                Msg("Không có active drawing.");
                return;
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                Msg("Model chưa kết nối.");
                return;
            }

            List<DrawingPart> selectedParts = GetSelectedDrawingParts(dh);
            if (selectedParts.Count < 1)
            {
                Msg("Slot05: Hãy chọn 1 hoặc nhiều plate/tấm cần dim.");
                return;
            }

            List<DrawingPart> selectedPlateDrawingParts = new List<DrawingPart>();
            List<ModelPart> selectedPlates = new List<ModelPart>();

            for (int i = 0; i < selectedParts.Count; i++)
            {
                DrawingPart dp = selectedParts[i];
                ModelPart mp = SelectModelPart(model, dp);
                if (mp == null)
                    continue;

                if (IsDummyReferencePart(mp))
                    continue;

                if (IsPlateLikePart(mp))
                {
                    selectedPlates.Add(mp);
                    selectedPlateDrawingParts.Add(dp);
                }
            }

            if (selectedPlates.Count == 0)
            {
                Msg("Slot05: Không nhận diện được tấm/plate trong selection.");
                return;
            }

            TSD.View view = TryGetSelectedPartsView(selectedPlateDrawingParts.ToArray());
            if (view == null)
                view = FindViewContainingPart(drawing, selectedPlates[0].Identifier);

            if (view == null)
            {
                Msg("Slot05: Không tìm thấy view chứa tấm đã chọn.");
                return;
            }

            int created = CreateSelectedPlateDims_TopBottomMode(model, drawing, view, selectedPlates);

            try { drawing.CommitChanges(); } catch { }

            // Không popup hoàn thành để chạy gọn.
        }

        private enum Slot05MainOrientation
        {
            Unknown,
            Horizontal,
            Vertical
        }

        private static Slot05MainOrientation DetectOrientation(Bounds2D mainBox)
        {
            if (!mainBox.Valid) return Slot05MainOrientation.Unknown;
            double w = mainBox.MaxX - mainBox.MinX;
            double h = mainBox.MaxY - mainBox.MinY;
            if (w <= 0 || h <= 0) return Slot05MainOrientation.Unknown;

            if (w > h * 1.5) return Slot05MainOrientation.Horizontal;
            if (h > w * 1.5) return Slot05MainOrientation.Vertical;

            return Slot05MainOrientation.Unknown;
        }

        private static int CreateSelectedPlateDims(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates,
            ModelPart authoritativeMainPart)
        {
            Slot05MainOrientation orientation = Slot05MainOrientation.Unknown;

            if (model != null && view != null && authoritativeMainPart != null && authoritativeMainPart.Identifier != null)
            {
                TSM.TransformationPlane oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                    ModelPart mainInViewPlane = model.SelectModelObject(authoritativeMainPart.Identifier) as ModelPart;
                    if (mainInViewPlane != null)
                    {
                        Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);
                        orientation = DetectOrientation(mainBox);
                    }
                }
                finally
                {
                    try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
                }
            }

            if (orientation == Slot05MainOrientation.Vertical)
            {
                return CreateSelectedPlateDimsVertical(model, drawing, view, plates, authoritativeMainPart);
            }
            else
            {
                return CreateSelectedPlateDimsHorizontal(model, drawing, view, plates, authoritativeMainPart);
            }
        }

        private static int CreateSelectedPlateDimsVertical(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates,
            ModelPart authoritativeMainPart)
        {
            int count = 0;
            if (model == null || drawing == null || view == null || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                if (allViewParts.Count == 0) return count;

                ModelPart mainInViewPlane = authoritativeMainPart == null || authoritativeMainPart.Identifier == null
                    ? null
                    : model.SelectModelObject(authoritativeMainPart.Identifier) as ModelPart;

                List<Slot05PlateGroup> groups = new List<Slot05PlateGroup>();

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart sourcePlate = plates[i];
                    if (sourcePlate == null || sourcePlate.Identifier == null) continue;

                    ModelPart plate = model.SelectModelObject(sourcePlate.Identifier) as ModelPart;
                    if (plate == null) continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid) continue;

                    ModelPart mainBeam = mainInViewPlane;
                    if (mainBeam == null || !ContainsPartIdentifier(allViewParts, mainBeam.Identifier)) continue;

                    Bounds2D mainBox = GetPartBounds2D(mainBeam);
                    if (!mainBox.Valid) continue;

                    List<Point> holeCenters = GetBoltCentersInsidePlate(model, view, plate, plateBox);
                    holeCenters.Sort(ComparePointByXThenY);

                    Slot05PlateGroup g = new Slot05PlateGroup();
                    g.Plate = plate;
                    g.PlateBox = plateBox;
                    g.MainBeam = mainBeam;
                    g.MainBox = mainBox;
                    g.HoleCenters = holeCenters;
                    groups.Add(g);
                }

                if (groups.Count == 0) return count;

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();
                Vector verticalDirection = new Vector(-1, 0, 0); // Left
                Bounds2D mainUnion = GetMainUnionBox(groups);

                // 1) DIM DỌC TẦNG 1 - TỪNG PLATE RIÊNG
                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.PlateBox.Valid || g.HoleCenters == null || g.HoleCenters.Count == 0) continue;

                    List<Point> v1 = new List<Point>();
                    double plateLeftX = g.PlateBox.MinX;

                    AddUniquePoint2D(v1, new Point(plateLeftX, g.PlateBox.MinY, 0), POINT_DUP_TOL);
                    for (int h = 0; h < g.HoleCenters.Count; h++)
                    {
                        Point hp = g.HoleCenters[h];
                        if (hp != null)
                        {
                            Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, verticalDirection);
                            AddUniquePoint2D(v1, hpGap, POINT_DUP_TOL);
                        }
                    }
                    AddUniquePoint2D(v1, new Point(plateLeftX, g.PlateBox.MaxY, 0), POINT_DUP_TOL);
                    v1.Sort(ComparePointByYThenX);

                    if (v1.Count >= 2)
                    {
                        if (CreateDimChain(handler, view, v1.ToArray(), verticalDirection, SLOT05_INTERNAL_PLATE_HOLE_TIER, "GEO_DIMENSION"))
                            count++;
                    }
                }

                // 2) DIM DỌC TẦNG 2 - CHAIN CHUNG
                if (mainUnion.Valid)
                {
                    List<Point> v2 = new List<Point>();
                    double mainLeftX = mainUnion.MinX;
                    AddUniquePoint2D(v2, new Point(mainLeftX, mainUnion.MinY, 0), POINT_DUP_TOL);

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Slot05PlateGroup g = groups[i];
                        if (g == null || !g.PlateBox.Valid) continue;
                        double plateLeftX = g.PlateBox.MinX;
                        AddUniquePoint2D(v2, new Point(plateLeftX, g.PlateBox.MinY, 0), POINT_DUP_TOL);
                        AddUniquePoint2D(v2, new Point(plateLeftX, g.PlateBox.MaxY, 0), POINT_DUP_TOL);
                    }
                    AddUniquePoint2D(v2, new Point(mainLeftX, mainUnion.MaxY, 0), POINT_DUP_TOL);
                    v2.Sort(ComparePointByYThenX);

                    if (v2.Count >= 2)
                    {
                        if (CreateDimChain(handler, view, v2.ToArray(), verticalDirection, SLOT05_MAIN_PLATE_CHAIN_TIER, "GEO_DIMENSION"))
                            count++;
                    }
                }

                // 3) DIM NGANG (TRANSVERSE)
                int edgeTopTier = 0;
                int edgeBottomTier = 0;

                groups.Sort(CompareGroupByPlateCenterYThenX);

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || g.HoleCenters == null || g.HoleCenters.Count == 0 || !g.MainBox.Valid) continue;

                    double bottomEdgeGap = Math.Abs(g.PlateBox.MinY - g.MainBox.MinY);
                    double topEdgeGap = Math.Abs(g.PlateBox.MaxY - g.MainBox.MaxY);
                    bool nearBottomMainEdge = bottomEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;
                    bool nearTopMainEdge = topEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;

                    if (nearBottomMainEdge && nearTopMainEdge)
                    {
                        if (bottomEdgeGap <= topEdgeGap) nearTopMainEdge = false;
                        else nearBottomMainEdge = false;
                    }

                    // Đường dóng dùng lỗ xa phía đặt DIM để đi xuyên qua cụm.
                    // DIM phía trên lấy lỗ dưới. DIM phía dưới lấy lỗ trên.
                    bool pickTopHole = nearBottomMainEdge && !nearTopMainEdge;
                    Point primaryHole = PickPrimaryHorizontalHole(g.PlateBox, g.HoleCenters, pickTopHole);
                    if (primaryHole == null) continue;

                    Vector horizontalDir;
                    double distance;
                    Point mainRight;
                    Point mainLeft;

                    if (nearBottomMainEdge && !nearTopMainEdge)
                    {
                        horizontalDir = new Vector(0, -1, 0); // Bottom
                        mainRight = new Point(g.MainBox.MaxX, g.MainBox.MinY, 0);
                        mainLeft = new Point(g.MainBox.MinX, g.MainBox.MinY, 0);
                        distance = Slot05TierOffset(edgeBottomTier);
                        edgeBottomTier++;
                    }
                    else if (nearTopMainEdge && !nearBottomMainEdge)
                    {
                        horizontalDir = new Vector(0, 1, 0); // Top
                        mainRight = new Point(g.MainBox.MaxX, g.MainBox.MaxY, 0);
                        mainLeft = new Point(g.MainBox.MinX, g.MainBox.MaxY, 0);
                        distance = Slot05TierOffset(edgeTopTier);
                        edgeTopTier++;
                    }
                    else
                    {
                        horizontalDir = new Vector(0, 1, 0); // Top
                        mainRight = new Point(g.MainBox.MaxX, g.PlateBox.MaxY, 0);
                        mainLeft = new Point(g.MainBox.MinX, g.PlateBox.MaxY, 0);
                        distance = Slot05TierOffset(0);
                    }

                    Point holePoint = GetHolePointWithMBoltGap(g.Plate, primaryHole, horizontalDir);

                    List<Point> h = new List<Point>();
                    h.Add(mainLeft);
                    h.Add(holePoint);
                    h.Add(mainRight);
                    h.Sort(ComparePointByXThenY);

                    if (CreateDimChain(handler, view, h.ToArray(), horizontalDir, distance, "GEO_DIMENSION"))
                        count++;
                }
            }
            catch (Exception ex)
            {
                Msg("Slot05 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static Point PickPrimaryHorizontalHole(Bounds2D plateBox, List<Point> holes, bool pickTopHole)
        {
            if (holes == null || holes.Count == 0) return null;
            if (holes.Count == 1) return holes[0];

            Point best = holes[0];
            for (int i = 1; i < holes.Count; i++)
            {
                if (pickTopHole)
                {
                    if (holes[i].Y > best.Y) best = holes[i];
                    else if (Math.Abs(holes[i].Y - best.Y) <= TOL && holes[i].X < best.X) best = holes[i];
                }
                else
                {
                    if (holes[i].Y < best.Y) best = holes[i];
                    else if (Math.Abs(holes[i].Y - best.Y) <= TOL && holes[i].X < best.X) best = holes[i];
                }
            }
            return best;
        }

        private static int CreateSelectedPlateDimsHorizontal(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates,
            ModelPart authoritativeMainPart)
        {
            int count = 0;

            if (model == null || drawing == null || view == null || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                if (allViewParts.Count == 0)
                    return count;

                ModelPart mainInViewPlane = authoritativeMainPart == null ||
                    authoritativeMainPart.Identifier == null
                    ? null
                    : model.SelectModelObject(authoritativeMainPart.Identifier) as ModelPart;

                List<Slot05PlateGroup> groups = new List<Slot05PlateGroup>();

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart sourcePlate = plates[i];
                    if (sourcePlate == null || sourcePlate.Identifier == null)
                        continue;

                    ModelPart plate = model.SelectModelObject(
                        sourcePlate.Identifier) as ModelPart;
                    if (plate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid)
                        continue;

                    ModelPart mainBeam = mainInViewPlane;
                    if (mainBeam == null ||
                        !ContainsPartIdentifier(allViewParts, mainBeam.Identifier))
                    {
                        continue;
                    }

                    Bounds2D mainBox = GetPartBounds2D(mainBeam);
                    if (!mainBox.Valid)
                        continue;

                    List<Point> holeCenters = GetBoltCentersInsidePlate(model, view, plate, plateBox);
                    holeCenters.Sort(ComparePointByXThenY);

                    Slot05PlateGroup g = new Slot05PlateGroup();
                    g.Plate = plate;
                    g.PlateBox = plateBox;
                    g.MainBeam = mainBeam;
                    g.MainBox = mainBox;
                    g.HoleCenters = holeCenters;
                    groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                // Slot05 theo dump: DIM ngang đặt phía trên.
                Vector horizontalDirection = new Vector(0, 1, 0);

                // 1) DIM NGANG TẦNG 1 - DIM NỘI BỘ TỪNG PLATE RIÊNG:
                // Mép plate -> tất cả lỗ nằm trong chính plate đó -> mép plate.
                // Nếu có N plate thì tạo N dim riêng, nhưng TẤT CẢ dùng chung một tầng offset.
                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.PlateBox.Valid)
                        continue;

                    if (g.HoleCenters == null || g.HoleCenters.Count == 0)
                        continue;

                    List<Point> h1 = new List<Point>();
                    double plateTopY = g.PlateBox.MaxY;

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);

                    if (g.HoleCenters != null)
                    {
                        for (int h = 0; h < g.HoleCenters.Count; h++)
                        {
                            Point hp = g.HoleCenters[h];
                            if (hp != null)
                            {
                                Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, horizontalDirection);
                                AddUniquePoint2D(h1, hpGap, POINT_DUP_TOL);
                            }
                        }
                    }

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                    h1.Sort(ComparePointByXThenY);

                    if (h1.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h1.ToArray(),
                            horizontalDirection,
                            SLOT05_INTERNAL_PLATE_HOLE_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // 2) DIM NGANG TẦNG 2 - CHAIN CHUNG MÉP MAIN + MÉP CÁC PLATE:
                // Giữ chain chung cho quan hệ tổng, đặt phía trên theo dump.
                List<Point> h2 = new List<Point>();
                Bounds2D mainUnion = GetMainUnionBox(groups);
                if (mainUnion.Valid)
                {
                    double mainTopY = mainUnion.MaxY;
                    AddUniquePoint2D(h2, new Point(mainUnion.MinX, mainTopY, 0), POINT_DUP_TOL);

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Slot05PlateGroup g = groups[i];
                        if (g == null || !g.PlateBox.Valid)
                            continue;

                        double plateTopY = g.PlateBox.MaxY;
                        AddUniquePoint2D(h2, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);
                        AddUniquePoint2D(h2, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                    }

                    AddUniquePoint2D(h2, new Point(mainUnion.MaxX, mainTopY, 0), POINT_DUP_TOL);
                    h2.Sort(ComparePointByXThenY);

                    if (h2.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h2.ToArray(),
                            horizontalDirection,
                            SLOT05_MAIN_PLATE_CHAIN_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // 3) DIM DỌC:
                // Quy tắc mới:
                // - Plate gần mép trái/phải main: chân trên/dưới bám đúng X mép ngoài thật của main.
                // - Plate giữa main: dim dọc offset về bên trái theo quy tắc tầng.
                // - Chuỗi điểm: mép trên main -> tâm lỗ plate -> mép dưới main.
                int edgeLeftTier = 0;
                int edgeRightTier = 0;

                // Sort theo X để tầng dọc của plate giữa ổn định và dễ đoán.
                groups.Sort(CompareGroupByPlateCenterXThenY);

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || g.HoleCenters == null || g.HoleCenters.Count == 0 || !g.MainBox.Valid)
                        continue;

                    double leftEdgeGap = Math.Abs(g.PlateBox.MinX - g.MainBox.MinX);
                    double rightEdgeGap = Math.Abs(g.PlateBox.MaxX - g.MainBox.MaxX);
                    bool nearLeftMainEdge = leftEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;
                    bool nearRightMainEdge = rightEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;

                    if (nearLeftMainEdge && nearRightMainEdge)
                    {
                        if (leftEdgeGap <= rightEdgeGap)
                            nearRightMainEdge = false;
                        else
                            nearLeftMainEdge = false;
                    }

                    // Đường dóng dùng lỗ xa phía đặt DIM để đi xuyên qua cụm.
                    // DIM đặt bên phải lấy lỗ bên trái. DIM đặt bên trái (hoặc giữa) lấy lỗ bên phải.
                    bool pickRightHole = !(nearRightMainEdge && !nearLeftMainEdge);
                    Point primaryHole = PickPrimaryVerticalHole(g.PlateBox, g.HoleCenters, pickRightHole);
                    if (primaryHole == null)
                        continue;

                    Vector verticalDirection;
                    double distance;
                    Point mainTop;
                    Point mainBottom;

                    if (nearLeftMainEdge && !nearRightMainEdge)
                    {
                        // Gần mép trái: chân main dùng đúng mép ngoài trái thật của main.
                        verticalDirection = new Vector(-1, 0, 0);
                        mainTop = new Point(g.MainBox.MinX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.MainBox.MinX, g.MainBox.MinY, 0);
                        distance = GetLeftTierDistance(mainTop, g.MainBox, edgeLeftTier);
                        edgeLeftTier++;
                    }
                    else if (nearRightMainEdge && !nearLeftMainEdge)
                    {
                        // Gần mép phải: chân main dùng đúng mép ngoài phải thật của main.
                        verticalDirection = new Vector(1, 0, 0);
                        mainTop = new Point(g.MainBox.MaxX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.MainBox.MaxX, g.MainBox.MinY, 0);
                        distance = GetRightTierDistance(mainTop, g.MainBox, edgeRightTier);
                        edgeRightTier++;
                    }
                    else
                    {
                        // Plate nằm giữa: chân trên/dưới dùng X mép ngoài trái plate, offset cố định 1 tầng.
                        verticalDirection = new Vector(-1, 0, 0);
                        mainTop = new Point(g.PlateBox.MinX, g.MainBox.MaxY, 0);
                        mainBottom = new Point(g.PlateBox.MinX, g.MainBox.MinY, 0);
                        distance = Slot05TierOffset(0);
                    }

                    Point holePoint = GetHolePointWithMBoltGap(g.Plate, primaryHole, verticalDirection);

                    List<Point> v = new List<Point>();
                    v.Add(mainBottom);
                    v.Add(holePoint);
                    v.Add(mainTop);
                    v.Sort(ComparePointByYThenX);

                    if (CreateDimChain(
                        handler,
                        view,
                        v.ToArray(),
                        verticalDirection,
                        distance,
                        "GEO_DIMENSION"))
                    {
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot05 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static int CreateSelectedPlateDims_TopBottomMode(
            TSM.Model model,
            TSD.Drawing drawing,
            TSD.View view,
            List<ModelPart> plates)
        {
            int count = 0;

            if (model == null || drawing == null || view == null || plates == null || plates.Count == 0)
                return count;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                if (allViewParts.Count == 0)
                    return count;

                List<Slot05PlateGroup> groups = new List<Slot05PlateGroup>();

                for (int i = 0; i < plates.Count; i++)
                {
                    ModelPart plate = plates[i];
                    if (plate == null)
                        continue;

                    Bounds2D plateBox = GetPartBounds2D(plate);
                    if (!plateBox.Valid)
                        continue;

                    ModelPart mainBeam = FindMainBeamForPlate(plate, plateBox, allViewParts);
                    if (mainBeam == null)
                        continue;

                    Bounds2D mainBox = GetPartBounds2D(mainBeam);
                    if (!mainBox.Valid)
                        continue;

                    List<Point> holeCenters = GetBoltCentersInsidePlate(model, view, plate, plateBox);
                    holeCenters.Sort(ComparePointByXThenY);

                    Slot05PlateGroup g = new Slot05PlateGroup();
                    g.Plate = plate;
                    g.PlateBox = plateBox;
                    g.MainBeam = mainBeam;
                    g.MainBox = mainBox;
                    g.HoleCenters = holeCenters;
                    groups.Add(g);
                }

                if (groups.Count == 0)
                    return count;

                TSD.StraightDimensionSetHandler handler =
                    new TSD.StraightDimensionSetHandler();

                Bounds2D mainUnion = GetMainUnionBox(groups);

                // SLOT05 THUẬT TOÁN 2:
                // Plate nằm trên main thì dim ngang đặt lên trên và chân dim dùng mép trên.
                // Plate nằm dưới main thì dim ngang đặt xuống dưới và chân dim dùng mép dưới.
                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.PlateBox.Valid)
                        continue;

                    if (g.HoleCenters == null || g.HoleCenters.Count == 0)
                        continue;

                    bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                    Vector horizontalDirection = plateBelowMain ? new Vector(0, -1, 0) : new Vector(0, 1, 0);
                    double plateEdgeY = plateBelowMain ? g.PlateBox.MinY : g.PlateBox.MaxY;

                    List<Point> h1 = new List<Point>();

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MinX, plateEdgeY, 0), POINT_DUP_TOL);

                    if (g.HoleCenters != null)
                    {
                        for (int h = 0; h < g.HoleCenters.Count; h++)
                        {
                            Point hp = g.HoleCenters[h];
                            if (hp != null)
                            {
                                Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, horizontalDirection);
                                AddUniquePoint2D(h1, hpGap, POINT_DUP_TOL);
                            }
                        }
                    }

                    AddUniquePoint2D(h1, new Point(g.PlateBox.MaxX, plateEdgeY, 0), POINT_DUP_TOL);
                    h1.Sort(ComparePointByXThenY);

                    if (h1.Count >= 2)
                    {
                        if (CreateDimChain(
                            handler,
                            view,
                            h1.ToArray(),
                            horizontalDirection,
                            SLOT05_INTERNAL_PLATE_HOLE_TIER,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // DIM NGANG TẦNG 2 - tách chain trên/dưới để chân dim đi theo đúng mép trên hoặc mép dưới.
                List<Point> h2Top = new List<Point>();
                List<Point> h2Bottom = new List<Point>();
                if (mainUnion.Valid)
                {
                    AddUniquePoint2D(h2Top, new Point(mainUnion.MinX, mainUnion.MaxY, 0), POINT_DUP_TOL);
                    AddUniquePoint2D(h2Bottom, new Point(mainUnion.MinX, mainUnion.MinY, 0), POINT_DUP_TOL);

                    bool hasTopPlate = false;
                    bool hasBottomPlate = false;
                    double topPlateOuterY = -999999999.0;
                    double bottomPlateOuterY = 999999999.0;

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Slot05PlateGroup g = groups[i];
                        if (g == null || !g.PlateBox.Valid)
                            continue;

                        bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                        if (plateBelowMain)
                        {
                            hasBottomPlate = true;
                            double plateBottomY = g.PlateBox.MinY;
                            if (plateBottomY < bottomPlateOuterY)
                                bottomPlateOuterY = plateBottomY;
                            AddUniquePoint2D(h2Bottom, new Point(g.PlateBox.MinX, plateBottomY, 0), POINT_DUP_TOL);
                            AddUniquePoint2D(h2Bottom, new Point(g.PlateBox.MaxX, plateBottomY, 0), POINT_DUP_TOL);
                        }
                        else
                        {
                            hasTopPlate = true;
                            double plateTopY = g.PlateBox.MaxY;
                            if (plateTopY > topPlateOuterY)
                                topPlateOuterY = plateTopY;
                            AddUniquePoint2D(h2Top, new Point(g.PlateBox.MinX, plateTopY, 0), POINT_DUP_TOL);
                            AddUniquePoint2D(h2Top, new Point(g.PlateBox.MaxX, plateTopY, 0), POINT_DUP_TOL);
                        }
                    }

                    AddUniquePoint2D(h2Top, new Point(mainUnion.MaxX, mainUnion.MaxY, 0), POINT_DUP_TOL);
                    AddUniquePoint2D(h2Bottom, new Point(mainUnion.MaxX, mainUnion.MinY, 0), POINT_DUP_TOL);

                    h2Top.Sort(ComparePointByXThenY);
                    h2Bottom.Sort(ComparePointByXThenY);

                    if (hasTopPlate && h2Top.Count >= 2)
                    {
                        double topChainTargetY = topPlateOuterY + SLOT05_MAIN_PLATE_CHAIN_TIER;
                        double topChainDistance = Math.Abs(topChainTargetY - h2Top[0].Y);

                        if (CreateDimChain(
                            handler,
                            view,
                            h2Top.ToArray(),
                            new Vector(0, 1, 0),
                            topChainDistance,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }

                    if (hasBottomPlate && h2Bottom.Count >= 2)
                    {
                        double bottomChainTargetY = bottomPlateOuterY - SLOT05_MAIN_PLATE_CHAIN_TIER;
                        double bottomChainDistance = Math.Abs(h2Bottom[0].Y - bottomChainTargetY);

                        if (CreateDimChain(
                            handler,
                            view,
                            h2Bottom.ToArray(),
                            new Vector(0, -1, 0),
                            bottomChainDistance,
                            "GEO_DIMENSION"))
                        {
                            count++;
                        }
                    }
                }

                // DIM DỌC THUẬT TOÁN 2:
                // Vẫn giữ quy luật trái/phải/giữa và tier như hiện tại.
                // Chuỗi điểm đổi thành: mép plate -> tất cả lỗ plate -> mép main gần plate.
                int edgeLeftTier = 0;
                int edgeRightTier = 0;

                groups.Sort(CompareGroupByPlateCenterXThenY);

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || g.HoleCenters == null || g.HoleCenters.Count == 0 || !g.MainBox.Valid)
                        continue;

                    double leftEdgeGap = Math.Abs(g.PlateBox.MinX - g.MainBox.MinX);
                    double rightEdgeGap = Math.Abs(g.PlateBox.MaxX - g.MainBox.MaxX);
                    bool nearLeftMainEdge = leftEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;
                    bool nearRightMainEdge = rightEdgeGap <= SLOT05_NEAR_MAIN_EDGE_TOL;

                    if (nearLeftMainEdge && nearRightMainEdge)
                    {
                        if (leftEdgeGap <= rightEdgeGap)
                            nearRightMainEdge = false;
                        else
                            nearLeftMainEdge = false;
                    }

                    bool pickRightHole = nearRightMainEdge && !nearLeftMainEdge;
                    Point primaryHole = PickPrimaryVerticalHole(g.PlateBox, g.HoleCenters, pickRightHole);
                    if (primaryHole == null)
                        continue;

                    Vector verticalDirection;
                    double distance;
                    double mainEdgeX;

                    if (nearLeftMainEdge && !nearRightMainEdge)
                    {
                        verticalDirection = new Vector(-1, 0, 0);
                        mainEdgeX = g.MainBox.MinX;
                        distance = GetLeftTierDistance(new Point(mainEdgeX, primaryHole.Y, 0), g.MainBox, edgeLeftTier);
                        edgeLeftTier++;
                    }
                    else if (nearRightMainEdge && !nearLeftMainEdge)
                    {
                        verticalDirection = new Vector(1, 0, 0);
                        mainEdgeX = g.MainBox.MaxX;
                        distance = GetRightTierDistance(new Point(mainEdgeX, primaryHole.Y, 0), g.MainBox, edgeRightTier);
                        edgeRightTier++;
                    }
                    else
                    {
                        verticalDirection = new Vector(-1, 0, 0);
                        mainEdgeX = g.PlateBox.MinX;
                        distance = Slot05TierOffset(0);
                    }

                    bool plateBelowMain = IsPlateBelowMain(g.PlateBox, g.MainBox);
                    double plateEdgeY = plateBelowMain ? g.PlateBox.MinY : g.PlateBox.MaxY;
                    double mainEdgeY = plateBelowMain ? g.MainBox.MinY : g.MainBox.MaxY;

                    List<Point> v = new List<Point>();
                    double plateOuterEdgeX = verticalDirection.X < 0.0 ? g.PlateBox.MinX : g.PlateBox.MaxX;
                    AddUniquePoint2D(v, new Point(plateOuterEdgeX, plateEdgeY, 0), POINT_DUP_TOL);

                    List<Point> holesForVerticalDim = new List<Point>();
                    for (int h = 0; h < g.HoleCenters.Count; h++)
                    {
                        Point hp = g.HoleCenters[h];
                        if (hp == null)
                            continue;

                        if (hp.X < g.PlateBox.MinX - PLATE_BOUND_TOL || hp.X > g.PlateBox.MaxX + PLATE_BOUND_TOL ||
                            hp.Y < g.PlateBox.MinY - PLATE_BOUND_TOL || hp.Y > g.PlateBox.MaxY + PLATE_BOUND_TOL)
                            continue;

                        holesForVerticalDim.Add(new Point(hp.X, hp.Y, 0));
                    }

                    holesForVerticalDim.Sort(ComparePointByYThenX);

                    for (int h = 0; h < holesForVerticalDim.Count; h++)
                    {
                        Point hp = holesForVerticalDim[h];
                        Point hpGap = GetHolePointWithMBoltGap(g.Plate, hp, verticalDirection);
                        AddUniquePoint2D(v, hpGap, POINT_DUP_TOL);
                    }

                    AddUniquePoint2D(v, new Point(mainEdgeX, mainEdgeY, 0), POINT_DUP_TOL);
                    v.Sort(ComparePointByYThenX);

                    if (CreateDimChain(
                        handler,
                        view,
                        v.ToArray(),
                        verticalDirection,
                        distance,
                        "GEO_DIMENSION"))
                    {
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Msg("Slot05 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private class Slot05SectionDimPlan
        {
            public TSD.View View;
            public Point[] PlateChain;
            public Point[] BoltChain;
            public Vector Direction;
            public double PlateChainDistance;
            public double BoltChainDistance;
        }

        private static List<Slot05SectionDimPlan> BuildSlot05SectionDimPlans(
            TSM.Model model,
            ModelPart mainPart,
            Slot05AutoTarget target,
            out string diagnostic)
        {
            List<Slot05SectionDimPlan> result = new List<Slot05SectionDimPlan>();
            List<string> rejected = new List<string>();

            if (target == null || target.SectionViews == null ||
                target.SectionViews.Count == 0)
            {
                diagnostic = "Không có section cùng tập ModelIdentifier với các plate mặt front.";
                return result;
            }

            for (int i = 0; i < target.SectionViews.Count; i++)
            {
                Slot05SectionDimPlan plan;
                string viewDiagnostic;
                if (TryBuildSlot05SectionDimPlanForView(
                    model,
                    mainPart,
                    target,
                    target.SectionViews[i],
                    out plan,
                    out viewDiagnostic))
                {
                    result.Add(plan);
                }
                else
                {
                    rejected.Add(
                        DescribeViewForAudit(target.SectionViews[i]) + ": " +
                        viewDiagnostic);
                }
            }

            diagnostic = "Đã preflight " + result.Count.ToString() + "/" +
                target.SectionViews.Count.ToString() +
                " section cùng liên kết bằng hình học.";
            if (rejected.Count > 0)
                diagnostic += " Bỏ qua an toàn: " + string.Join(" | ", rejected.ToArray());
            return result;
        }

        private static bool TryBuildSlot05SectionDimPlanForView(
            TSM.Model model,
            ModelPart mainPart,
            Slot05AutoTarget target,
            TSD.View view,
            out Slot05SectionDimPlan plan,
            out string diagnostic)
        {
            plan = null;
            diagnostic = "Không tìm thấy section đúng loại liên kết plate/main được hỗ trợ.";

            if (model == null || mainPart == null || mainPart.Identifier == null ||
                target == null || view == null ||
                target.AllMatchedPlates == null || target.AllMatchedPlates.Count == 0)
            {
                return false;
            }

            if (!IsSlot05SectionView(view))
            {
                diagnostic = "View nhận dạng liên kết không phải SectionView.";
                return false;
            }

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            try
            {
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                List<ModelPart> viewParts = GetAllModelPartsInView(model, view);
                ModelPart mainInViewPlane = model.SelectModelObject(
                    mainPart.Identifier) as ModelPart;
                if (mainInViewPlane == null ||
                    !ContainsPartIdentifier(viewParts, mainPart.Identifier))
                {
                    diagnostic = "Section không chứa Main Part chính xác của drawing.";
                    return false;
                }

                Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);
                if (!mainBox.Valid)
                {
                    diagnostic = "Không đọc được solid Main Part trong section.";
                    return false;
                }

                List<ModelPart> orderedPlates =
                    new List<ModelPart>(target.AllMatchedPlates);
                orderedPlates.Sort(CompareModelPartByIdentifier);

                Point referenceLower = null;
                Point referenceUpper = null;
                Point referenceBolt = null;
                int referenceSide = 0;
                bool referenceIsStraightSidePlate = false;

                for (int i = 0; i < orderedPlates.Count; i++)
                {
                    Identifier id = orderedPlates[i] == null
                        ? null
                        : orderedPlates[i].Identifier;
                    ModelPart plate = FindPartByIdentifier(viewParts, id);
                    if (plate == null)
                    {
                        diagnostic = "Section không chứa đủ mọi plate đã nhận diện theo ModelIdentifier.";
                        return false;
                    }

                    double score;
                    bool directConnection;
                    if (!TryAnalyzeAutomaticWrapPlate(
                        mainInViewPlane,
                        mainBox,
                        plate,
                        out score,
                        out directConnection))
                    {
                        diagnostic = "Có plate không còn thỏa quan hệ liên kết được hỗ trợ với Main Part trong section.";
                        return false;
                    }

                    Point lower;
                    Point upper;
                    Point bolt;
                    int side;
                    bool isStraightSidePlate;
                    if (!TryResolveSlot05SectionFeatures(
                        model,
                        view,
                        mainBox,
                        plate,
                        out lower,
                        out upper,
                        out bolt,
                        out side,
                        out isStraightSidePlate))
                    {
                        diagnostic = "Section có topology lỗ/đỉnh plate khác liên kết mẫu; từ chối dim để tránh nhầm.";
                        return false;
                    }

                    if (referenceLower == null)
                    {
                        referenceLower = lower;
                        referenceUpper = upper;
                        referenceBolt = bolt;
                        referenceSide = side;
                        referenceIsStraightSidePlate = isStraightSidePlate;
                    }
                    else if (side != referenceSide ||
                        isStraightSidePlate != referenceIsStraightSidePlate ||
                        Distance2D(referenceLower, lower) > SLOT05_SECTION_SIGNATURE_TOL ||
                        Distance2D(referenceUpper, upper) > SLOT05_SECTION_SIGNATURE_TOL ||
                        (referenceIsStraightSidePlate
                            ? Math.Abs(referenceBolt.Y - bolt.Y) >
                                SLOT05_SECTION_SIGNATURE_TOL
                            : Distance2D(referenceBolt, bolt) >
                                SLOT05_SECTION_SIGNATURE_TOL))
                    {
                        diagnostic = "Các plate không chiếu về cùng một chữ ký section liên kết; từ chối dim.";
                        return false;
                    }
                    else if (referenceIsStraightSidePlate &&
                        ((referenceSide < 0 && bolt.X < referenceBolt.X) ||
                         (referenceSide > 0 && bolt.X > referenceBolt.X)))
                    {
                        // Hai plate ở hai đầu main có thể chiếu tâm bolt về hai
                        // mặt khác nhau. Dim mẫu lấy tâm ngoài cùng phía plate.
                        referenceBolt = bolt;
                    }
                }

                if (referenceLower == null || referenceUpper == null ||
                    referenceBolt == null || referenceSide == 0)
                {
                    return false;
                }

                double mainEdgeX = referenceSide > 0 ? mainBox.MaxX : mainBox.MinX;
                Point mainLower = new Point(mainEdgeX, mainBox.MinY, 0);
                Point mainUpper = new Point(mainEdgeX, mainBox.MaxY, 0);
                Vector direction = new Vector(referenceSide, 0, 0);

                double scale = ReadSlot05ViewScale(view);
                if (scale <= 0.0 || double.IsNaN(scale) || double.IsInfinity(scale))
                {
                    diagnostic = "Không đọc được scale của section.";
                    return false;
                }

                plan = new Slot05SectionDimPlan();
                plan.View = view;
                plan.Direction = direction;
                plan.PlateChain = new Point[]
                {
                    mainLower,
                    referenceLower,
                    referenceUpper,
                    mainUpper
                };
                plan.BoltChain = new Point[]
                {
                    mainLower,
                    referenceBolt,
                    mainUpper
                };
                plan.PlateChainDistance = SLOT05_SECTION_NEAR_TIER_PAPER * scale;
                plan.BoltChainDistance = SLOT05_SECTION_FAR_TIER_PAPER * scale;

                diagnostic = "Section chứa đủ " + orderedPlates.Count.ToString() +
                    " plate cùng chữ ký liên kết, cùng đỉnh solid và cùng tâm lỗ chiếu.";
                return true;
            }
            catch (Exception ex)
            {
                diagnostic = "Lỗi preflight section: " + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }
        }

        private static bool TryResolveSlot05SectionFeatures(
            TSM.Model model,
            TSD.View view,
            Bounds2D mainBox,
            ModelPart plate,
            out Point lower,
            out Point upper,
            out Point bolt,
            out int side,
            out bool isStraightSidePlate)
        {
            lower = null;
            upper = null;
            bolt = null;
            side = 0;
            isStraightSidePlate = false;

            if (model == null || view == null || plate == null || !mainBox.Valid)
                return false;

            Bounds2D plateBox = GetPartBounds2D(plate);
            if (!plateBox.Valid)
                return false;

            double straightScore;
            isStraightSidePlate = TryGetStraightSidePlateScore(
                plate,
                mainBox,
                plateBox,
                out straightScore);

            double plateCenterX = (plateBox.MinX + plateBox.MaxX) / 2.0;
            double leftGap = Math.Abs(plateCenterX - mainBox.MinX);
            double rightGap = Math.Abs(plateCenterX - mainBox.MaxX);
            side = rightGap < leftGap ? 1 : -1;
            double mainEdgeX = side > 0 ? mainBox.MaxX : mainBox.MinX;

            List<Point> vertices = GetExactProjectedSolidVertices(plate);
            lower = FindSectionExtremeVertex(
                vertices,
                plateBox.MinY,
                mainEdgeX,
                isStraightSidePlate);
            upper = FindSectionExtremeVertex(
                vertices,
                plateBox.MaxY,
                mainEdgeX,
                isStraightSidePlate);
            if (lower == null || upper == null ||
                upper.Y - lower.Y <= TOL ||
                lower.Y <= mainBox.MinY + TOL ||
                upper.Y >= mainBox.MaxY - TOL)
            {
                return false;
            }

            List<Point> holes = GetBoltCentersInsidePlate(model, view, plate, plateBox);
            if (holes.Count != 1)
                return false;

            bolt = holes[0];
            if (bolt == null || bolt.Y <= mainBox.MinY + TOL ||
                bolt.Y >= mainBox.MaxY - TOL ||
                (!isStraightSidePlate &&
                 Math.Abs(bolt.X - mainEdgeX) > SLOT05_AUTO_EDGE_CONTACT_TOL))
            {
                return false;
            }

            return true;
        }

        private static List<Point> GetExactProjectedSolidVertices(ModelPart part)
        {
            List<Point> result = new List<Point>();
            try
            {
                if (part == null)
                    return result;

                Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;

                    if (edge.StartPoint != null)
                        AddUniquePoint2D(result, edge.StartPoint, 0.1);
                    if (edge.EndPoint != null)
                        AddUniquePoint2D(result, edge.EndPoint, 0.1);
                }
            }
            catch
            {
            }

            return result;
        }

        private static Point FindSectionExtremeVertex(
            List<Point> vertices,
            double targetY,
            double mainEdgeX,
            bool preferOutside)
        {
            Point best = null;
            double bestGap = double.PositiveInfinity;
            if (vertices == null)
                return null;

            for (int i = 0; i < vertices.Count; i++)
            {
                Point point = vertices[i];
                if (point == null ||
                    Math.Abs(point.Y - targetY) > SLOT05_AUTO_PROFILE_POINT_TOL)
                {
                    continue;
                }

                double gap = Math.Abs(point.X - mainEdgeX);
                bool betterGap = preferOutside
                    ? gap > bestGap + 0.01
                    : gap < bestGap - 0.01;
                if (best == null || betterGap ||
                    (Math.Abs(gap - bestGap) <= 0.01 && point.X < best.X))
                {
                    best = point;
                    bestGap = gap;
                }
            }

            return best == null ? null : new Point(best.X, best.Y, 0);
        }

        private static int CompareModelPartByIdentifier(ModelPart first, ModelPart second)
        {
            int firstId = first == null || first.Identifier == null
                ? int.MinValue
                : first.Identifier.ID;
            int secondId = second == null || second.Identifier == null
                ? int.MinValue
                : second.Identifier.ID;
            return firstId.CompareTo(secondId);
        }

        private static double ReadSlot05ViewScale(TSD.View view)
        {
            try
            {
                return view == null || view.Attributes == null
                    ? double.NaN
                    : view.Attributes.Scale;
            }
            catch
            {
                return double.NaN;
            }
        }

        private static int CreateSlot05SectionDims(Slot05SectionDimPlan plan)
        {
            if (plan == null || plan.View == null || plan.Direction == null ||
                plan.PlateChain == null || plan.BoltChain == null)
            {
                return 0;
            }

            int created = 0;
            TSD.StraightDimensionSetHandler handler =
                new TSD.StraightDimensionSetHandler();
            if (CreateDimChain(
                handler,
                plan.View,
                plan.PlateChain,
                plan.Direction,
                plan.PlateChainDistance,
                "GEO_DIMENSION"))
            {
                created++;
            }
            if (CreateDimChain(
                handler,
                plan.View,
                plan.BoltChain,
                plan.Direction,
                plan.BoltChainDistance,
                "GEO_DIMENSION"))
            {
                created++;
            }

            return created;
        }

        private class Slot05AutoTarget
        {
            public TSD.View View;
            public TSD.View ClassificationView;
            public List<TSD.View> SectionViews = new List<TSD.View>();
            public List<ModelPart> Plates = new List<ModelPart>();
            public List<ModelPart> AllMatchedPlates = new List<ModelPart>();
            public int MatchedPlateCount;
            public int DirectConnectionCount;
            public double GeometryScore;
        }

        private class Slot05DimensionViewCandidate
        {
            public TSD.View View;
            public List<ModelPart> Plates = new List<ModelPart>();
            public double MainLongitudinalAspect;
            public double PlateCenterSpread;
            public double PlateFaceScore;
        }

        private static Slot05AutoTarget FindAutomaticSlot05Target(
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart mainPart,
            out string diagnostic)
        {
            diagnostic = "Không tìm thấy plate thuộc loại liên kết 5-1 với Main Part trong các view.";

            if (model == null || drawing == null || mainPart == null || mainPart.Identifier == null)
                return null;

            List<Slot05AutoTarget> targets = new List<Slot05AutoTarget>();
            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                {
                    diagnostic = "Không đọc được sheet của bản vẽ đang mở.";
                    return null;
                }

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                    List<ModelPart> allViewParts = GetAllModelPartsInView(model, view);
                    ModelPart mainInViewPlane = model.SelectModelObject(
                        mainPart.Identifier) as ModelPart;
                    if (!ContainsPartIdentifier(allViewParts, mainPart.Identifier) ||
                        mainInViewPlane == null)
                    {
                        continue;
                    }

                    Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);
                    if (!mainBox.Valid)
                        continue;

                    Slot05AutoTarget target = new Slot05AutoTarget();
                    target.View = view;

                    for (int i = 0; i < allViewParts.Count; i++)
                    {
                        ModelPart candidate = allViewParts[i];
                        if (candidate == null || candidate.Identifier == null ||
                            SameIdentifier(candidate.Identifier, mainPart.Identifier))
                        {
                            continue;
                        }

                        double relationScore;
                        bool directConnection;
                        if (!TryAnalyzeAutomaticWrapPlate(
                            mainInViewPlane,
                            mainBox,
                            candidate,
                            out relationScore,
                            out directConnection))
                        {
                            continue;
                        }

                        target.MatchedPlateCount++;
                        AddUniqueModelPart(target.AllMatchedPlates, candidate);
                        if (!ContainsEquivalentProjectedDimensionGeometry(
                            model,
                            view,
                            target.Plates,
                            candidate))
                        {
                            AddUniqueModelPart(target.Plates, candidate);
                        }
                        target.GeometryScore += relationScore;
                        if (directConnection)
                            target.DirectConnectionCount++;
                    }

                    if (target.Plates.Count > 0)
                        targets.Add(target);
                }
            }
            catch (Exception ex)
            {
                diagnostic = "Lỗi khi dò quan hệ Main Part/plate: " + ex.Message;
                return null;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }

            Slot05AutoTarget classification = null;
            bool sectionClassificationAmbiguous = false;
            if (targets.Count > 0)
            {
                targets.Sort(CompareAutomaticTargets);
                sectionClassificationAmbiguous = targets.Count > 1 &&
                    AutomaticTargetsAreAmbiguous(targets[0], targets[1]);
                if (!sectionClassificationAmbiguous)
                    classification = targets[0];
            }

            if (classification != null)
            {
                Slot05DimensionViewCandidate dimensionView = FindFrontFaceDimensionView(
                    model,
                    drawing,
                    mainPart,
                    classification.AllMatchedPlates);
                if (dimensionView != null)
                {
                    Slot05AutoTarget result = new Slot05AutoTarget();
                    result.View = dimensionView.View;
                    result.ClassificationView = classification.View;
                    result.Plates.AddRange(dimensionView.Plates);
                    result.AllMatchedPlates.AddRange(classification.AllMatchedPlates);
                    result.MatchedPlateCount = classification.AllMatchedPlates.Count;
                    result.DirectConnectionCount = classification.DirectConnectionCount;
                    result.GeometryScore = classification.GeometryScore;
                    AttachMatchingSectionViews(result, targets);

                    diagnostic = "Đã nhận diện " + result.MatchedPlateCount.ToString() +
                        " plate qua hình học liên kết, ánh xạ đúng ModelIdentifier sang mặt front" +
                        " và ghép " + result.SectionViews.Count.ToString() +
                        " section cùng liên kết.";
                    return result;
                }
            }

            string fallbackDiagnostic;
            Slot05AutoTarget fallback = FindFrontFaceFallbackTarget(
                model,
                drawing,
                mainPart,
                out fallbackDiagnostic);
            if (fallback != null)
            {
                AttachMatchingSectionViews(fallback, targets);
                diagnostic = (sectionClassificationAmbiguous
                    ? "Có nhiều section đối xứng cùng liên kết; dùng tập plate mặt front " +
                        "làm chuẩn để giữ đúng mọi section khớp ModelIdentifier. "
                    : "Không có section liên kết hợp lệ. ") + fallbackDiagnostic;
                return fallback;
            }

            diagnostic = fallbackDiagnostic;
            return null;
        }

        private static void AttachMatchingSectionViews(
            Slot05AutoTarget result,
            List<Slot05AutoTarget> sectionCandidates)
        {
            if (result == null || result.AllMatchedPlates == null ||
                sectionCandidates == null)
            {
                return;
            }

            result.SectionViews.Clear();
            for (int i = 0; i < sectionCandidates.Count; i++)
            {
                Slot05AutoTarget candidate = sectionCandidates[i];
                if (candidate == null || !IsSlot05SectionView(candidate.View) ||
                    !PartIdentifierSetsMatch(
                        candidate.AllMatchedPlates,
                        result.AllMatchedPlates))
                {
                    continue;
                }

                // Mỗi target được tạo từ một drawing view duy nhất. Không dùng tên
                // A/B; chỉ ghép section khi toàn bộ ModelIdentifier của plate trùng.
                result.SectionViews.Add(candidate.View);
            }

            result.ClassificationView = result.SectionViews.Count > 0
                ? result.SectionViews[0]
                : null;
        }

        private static Slot05AutoTarget FindFrontFaceFallbackTarget(
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart mainPart,
            out string diagnostic)
        {
            diagnostic = "Không nhận diện được plate liên kết trên mặt front bằng hình học.";
            if (model == null || drawing == null || mainPart == null ||
                mainPart.Identifier == null)
            {
                return null;
            }

            List<Slot05AutoTarget> candidates = new List<Slot05AutoTarget>();
            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (!IsSlot05FrontFaceView(view))
                        continue;

                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                    List<ModelPart> viewParts = GetAllModelPartsInView(model, view);
                    ModelPart mainInViewPlane = model.SelectModelObject(
                        mainPart.Identifier) as ModelPart;
                    if (mainInViewPlane == null ||
                        !ContainsPartIdentifier(viewParts, mainPart.Identifier))
                    {
                        continue;
                    }

                    Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);
                    if (!mainBox.Valid)
                        continue;

                    Slot05AutoTarget target = new Slot05AutoTarget();
                    target.View = view;
                    for (int i = 0; i < viewParts.Count; i++)
                    {
                        ModelPart part = viewParts[i];
                        if (part == null || part.Identifier == null ||
                            SameIdentifier(part.Identifier, mainPart.Identifier))
                        {
                            continue;
                        }

                        double score;
                        bool directConnection;
                        if (!TryAnalyzeFrontFallbackPlate(
                            model,
                            view,
                            mainInViewPlane,
                            mainBox,
                            part,
                            out score,
                            out directConnection))
                        {
                            continue;
                        }

                        AddUniqueModelPart(target.Plates, part);
                        AddUniqueModelPart(target.AllMatchedPlates, part);
                        target.MatchedPlateCount++;
                        target.GeometryScore += score;
                        if (directConnection)
                            target.DirectConnectionCount++;
                    }

                    if (target.Plates.Count > 0)
                        candidates.Add(target);
                }
            }
            catch (Exception ex)
            {
                diagnostic = "Lỗi khi dò fallback mặt front: " + ex.Message;
                return null;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }

            if (candidates.Count == 0)
                return null;

            candidates.Sort(CompareAutomaticTargets);
            if (candidates.Count > 1 &&
                AutomaticTargetsAreAmbiguous(candidates[0], candidates[1]))
            {
                diagnostic = "Có nhiều mặt front có chữ ký plate liên kết ngang nhau; " +
                    "không tự dim để tránh chọn nhầm view.";
                return null;
            }

            diagnostic = "Đã nhận diện " + candidates[0].Plates.Count.ToString() +
                " plate trực tiếp trên mặt front; section là tùy chọn và được bỏ qua.";
            return candidates[0];
        }

        private static bool TryAnalyzeFrontFallbackPlate(
            TSM.Model model,
            TSD.View view,
            ModelPart mainPart,
            Bounds2D mainBox,
            ModelPart candidate,
            out double score,
            out bool directConnection)
        {
            score = 0.0;
            directConnection = false;
            if (model == null || view == null || mainPart == null ||
                candidate == null || !mainBox.Valid)
            {
                return false;
            }

            bool sameAssembly = IsPartInSameAssembly(candidate, mainPart);
            directConnection = ArePartsDirectlyBoltConnected(candidate, mainPart);
            if (!sameAssembly && !directConnection)
                return false;

            Bounds2D plateBox = GetPartBounds2D(candidate);
            if (!plateBox.Valid ||
                !HasSteppedOrCurvedProjectedProfile(candidate, plateBox))
            {
                return false;
            }

            double mainWidth = mainBox.MaxX - mainBox.MinX;
            double mainHeight = mainBox.MaxY - mainBox.MinY;
            double mainLong = Math.Max(mainWidth, mainHeight);
            double mainShort = Math.Min(mainWidth, mainHeight);
            if (mainLong <= TOL || mainShort <= TOL ||
                mainLong / mainShort < SLOT05_AUTO_MIN_LONGITUDINAL_ASPECT)
            {
                return false;
            }

            bool mainAxisIsX = mainWidth >= mainHeight;
            double plateLong = mainAxisIsX
                ? plateBox.MaxX - plateBox.MinX
                : plateBox.MaxY - plateBox.MinY;
            double plateTransverse = mainAxisIsX
                ? plateBox.MaxY - plateBox.MinY
                : plateBox.MaxX - plateBox.MinX;
            if (plateLong <= TOL || plateTransverse <= TOL ||
                plateLong > mainLong * 0.10 + SLOT05_AUTO_EDGE_CONTACT_TOL)
            {
                return false;
            }

            double mainAxisMin = mainAxisIsX ? mainBox.MinX : mainBox.MinY;
            double mainAxisMax = mainAxisIsX ? mainBox.MaxX : mainBox.MaxY;
            double plateAxisCenter = mainAxisIsX
                ? (plateBox.MinX + plateBox.MaxX) / 2.0
                : (plateBox.MinY + plateBox.MaxY) / 2.0;
            if (plateAxisCenter < mainAxisMin - SLOT05_AUTO_EDGE_CONTACT_TOL ||
                plateAxisCenter > mainAxisMax + SLOT05_AUTO_EDGE_CONTACT_TOL)
            {
                return false;
            }

            double transverseOverlap = mainAxisIsX
                ? IntervalOverlap(mainBox.MinY, mainBox.MaxY, plateBox.MinY, plateBox.MaxY)
                : IntervalOverlap(mainBox.MinX, mainBox.MaxX, plateBox.MinX, plateBox.MaxX);
            double overlapRatio = transverseOverlap /
                Math.Max(TOL, Math.Min(mainShort, plateTransverse));
            double faceRatio = plateTransverse / mainShort;
            if (overlapRatio < 0.90 ||
                faceRatio < SLOT05_AUTO_MIN_PLATE_FACE_RATIO || faceRatio > 1.20)
            {
                return false;
            }

            List<Point> holes = GetBoltCentersInsidePlate(model, view, candidate, plateBox);
            if (holes.Count == 0)
                return false;

            score = overlapRatio * 100.0 +
                Math.Max(0.0, 1.0 - plateLong / mainLong) * 20.0 +
                Math.Min(holes.Count, 10);
            if (directConnection)
                score += 100.0;
            if (sameAssembly)
                score += 25.0;
            return true;
        }

        private static Slot05DimensionViewCandidate FindFrontFaceDimensionView(
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart mainPart,
            List<ModelPart> matchedPlates)
        {
            if (model == null || drawing == null || mainPart == null ||
                mainPart.Identifier == null || matchedPlates == null ||
                matchedPlates.Count == 0)
            {
                return null;
            }

            List<Slot05DimensionViewCandidate> candidates =
                new List<Slot05DimensionViewCandidate>();
            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    // Luồng này chỉ chọn view đích cho chuỗi dim mặt front.
                    // Section được ghép và preflight riêng theo hình học liên kết.
                    // ViewType là topology của drawing API, không phải tên do user đặt.
                    if (!IsSlot05FrontFaceView(view))
                        continue;

                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                    List<ModelPart> viewParts = GetAllModelPartsInView(model, view);
                    ModelPart mainInViewPlane = model.SelectModelObject(
                        mainPart.Identifier) as ModelPart;
                    if (!ContainsPartIdentifier(viewParts, mainPart.Identifier) ||
                        mainInViewPlane == null)
                    {
                        continue;
                    }

                    Bounds2D mainBox = GetPartBounds2D(mainInViewPlane);
                    if (!mainBox.Valid)
                        continue;

                    double mainWidth = mainBox.MaxX - mainBox.MinX;
                    double mainHeight = mainBox.MaxY - mainBox.MinY;
                    if (mainWidth <= TOL || mainHeight <= TOL)
                        continue;

                    double mainLong = Math.Max(mainWidth, mainHeight);
                    double mainShort = Math.Min(mainWidth, mainHeight);
                    double longitudinalAspect = mainLong / mainShort;
                    if (longitudinalAspect < SLOT05_AUTO_MIN_LONGITUDINAL_ASPECT)
                        continue;

                    bool mainAxisIsX = mainWidth >= mainHeight;

                    Slot05DimensionViewCandidate candidate =
                        new Slot05DimensionViewCandidate();
                    candidate.View = view;
                    candidate.MainLongitudinalAspect = longitudinalAspect;

                    double minCenter = double.PositiveInfinity;
                    double maxCenter = double.NegativeInfinity;
                    double plateFaceScore = 0.0;
                    bool complete = true;
                    for (int i = 0; i < matchedPlates.Count; i++)
                    {
                        Identifier plateId = matchedPlates[i] == null
                            ? null
                            : matchedPlates[i].Identifier;
                        ModelPart visiblePlate = FindPartByIdentifier(viewParts, plateId);
                        if (visiblePlate == null)
                        {
                            complete = false;
                            break;
                        }

                        Bounds2D plateBox = GetPartBounds2D(visiblePlate);
                        if (!plateBox.Valid)
                        {
                            complete = false;
                            break;
                        }

                        double plateWidth = plateBox.MaxX - plateBox.MinX;
                        double plateHeight = plateBox.MaxY - plateBox.MinY;
                        double faceRatio = Math.Min(plateWidth, plateHeight) / mainShort;
                        if (faceRatio < SLOT05_AUTO_MIN_PLATE_FACE_RATIO)
                        {
                            complete = false;
                            break;
                        }

                        double center = mainAxisIsX
                            ? (plateBox.MinX + plateBox.MaxX) / 2.0
                            : (plateBox.MinY + plateBox.MaxY) / 2.0;
                        minCenter = Math.Min(minCenter, center);
                        maxCenter = Math.Max(maxCenter, center);
                        plateFaceScore += (plateWidth * plateHeight) /
                            Math.Max(TOL, mainShort * mainShort);
                        candidate.Plates.Add(visiblePlate);
                    }

                    if (!complete || candidate.Plates.Count != matchedPlates.Count)
                        continue;

                    candidate.PlateCenterSpread = candidate.Plates.Count > 1
                        ? maxCenter - minCenter
                        : 0.0;
                    candidate.PlateFaceScore = plateFaceScore /
                        Math.Max(1, candidate.Plates.Count);
                    candidates.Add(candidate);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch
                {
                }
            }

            if (candidates.Count == 0)
                return null;

            candidates.Sort(CompareDimensionViewCandidates);
            if (candidates.Count > 1 &&
                DimensionViewCandidatesAreAmbiguous(candidates[0], candidates[1]))
            {
                return null;
            }

            return candidates[0];
        }

        private static int CompareDimensionViewCandidates(
            Slot05DimensionViewCandidate first,
            Slot05DimensionViewCandidate second)
        {
            if (first == null && second == null) return 0;
            if (first == null) return 1;
            if (second == null) return -1;

            int c = second.Plates.Count.CompareTo(first.Plates.Count);
            if (c != 0) return c;

            c = second.PlateFaceScore.CompareTo(first.PlateFaceScore);
            if (c != 0) return c;

            c = second.PlateCenterSpread.CompareTo(first.PlateCenterSpread);
            if (c != 0) return c;

            return second.MainLongitudinalAspect.CompareTo(first.MainLongitudinalAspect);
        }

        private static bool DimensionViewCandidatesAreAmbiguous(
            Slot05DimensionViewCandidate first,
            Slot05DimensionViewCandidate second)
        {
            if (first == null || second == null)
                return false;

            return first.Plates.Count == second.Plates.Count &&
                Math.Abs(first.PlateFaceScore - second.PlateFaceScore) <= 0.01 &&
                Math.Abs(first.PlateCenterSpread - second.PlateCenterSpread) <= TOL &&
                Math.Abs(first.MainLongitudinalAspect - second.MainLongitudinalAspect) <= 0.05;
        }

        private static bool IsSlot05FrontFaceView(TSD.View view)
        {
            if (view == null)
                return false;

            object viewType = GetPropertyValue(view, "ViewType");
            return viewType != null && string.Equals(
                viewType.ToString(),
                "FrontView",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSlot05SectionView(TSD.View view)
        {
            if (view == null)
                return false;

            object viewType = GetPropertyValue(view, "ViewType");
            return viewType != null && string.Equals(
                viewType.ToString(),
                "SectionView",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsEquivalentProjectedDimensionGeometry(
            TSM.Model model,
            TSD.View view,
            List<ModelPart> representatives,
            ModelPart candidate)
        {
            if (model == null || view == null || representatives == null || candidate == null)
                return false;

            for (int i = 0; i < representatives.Count; i++)
            {
                if (HaveEquivalentProjectedDimensionGeometry(
                    model,
                    view,
                    representatives[i],
                    candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HaveEquivalentProjectedDimensionGeometry(
            TSM.Model model,
            TSD.View view,
            ModelPart first,
            ModelPart second)
        {
            if (first == null || second == null)
                return false;

            Bounds2D firstBounds = GetPartBounds2D(first);
            Bounds2D secondBounds = GetPartBounds2D(second);
            if (!firstBounds.Valid || !secondBounds.Valid ||
                Math.Abs(firstBounds.MinX - secondBounds.MinX) > POINT_DUP_TOL ||
                Math.Abs(firstBounds.MaxX - secondBounds.MaxX) > POINT_DUP_TOL ||
                Math.Abs(firstBounds.MinY - secondBounds.MinY) > POINT_DUP_TOL ||
                Math.Abs(firstBounds.MaxY - secondBounds.MaxY) > POINT_DUP_TOL)
            {
                return false;
            }

            // Chữ ký dimension chỉ chứa dữ liệu writer thật sự tiêu thụ:
            // projected bounds, tâm lỗ và gap M/phi. Contour đã được dùng ở
            // bước nhận diện plate; không đọc lại Solid.EdgeEnumerator tại đây.
            List<Point> firstHoles = GetBoltCentersInsidePlate(
                model,
                view,
                first,
                firstBounds);
            List<Point> secondHoles = GetBoltCentersInsidePlate(
                model,
                view,
                second,
                secondBounds);
            if (!PointSetsMatch2D(firstHoles, secondHoles, POINT_DUP_TOL))
                return false;

            firstHoles.Sort(ComparePointByXThenY);
            secondHoles.Sort(ComparePointByXThenY);
            for (int i = 0; i < firstHoles.Count; i++)
            {
                double firstGap = GetHoleCenterDimGapByMThenPhi(first, firstHoles[i]);
                double secondGap = GetHoleCenterDimGapByMThenPhi(second, secondHoles[i]);
                if (Math.Abs(firstGap - secondGap) > POINT_DUP_TOL)
                    return false;
            }

            return true;
        }

        private static bool PointSetsMatch2D(
            List<Point> first,
            List<Point> second,
            double tolerance)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;

            bool[] matched = new bool[second.Count];
            for (int i = 0; i < first.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < second.Count; j++)
                {
                    if (!matched[j] && Distance2D(first[i], second[j]) <= tolerance)
                    {
                        matched[j] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private static bool TryAnalyzeAutomaticWrapPlate(
            ModelPart mainPart,
            Bounds2D mainBox,
            ModelPart candidate,
            out double score,
            out bool directConnection)
        {
            score = 0.0;
            directConnection = false;

            if (mainPart == null || candidate == null || !mainBox.Valid)
                return false;

            bool sameAssembly = IsPartInSameAssembly(candidate, mainPart);
            directConnection = ArePartsDirectlyBoltConnected(candidate, mainPart);
            if (!sameAssembly && !directConnection)
                return false;

            Bounds2D plateBox = GetPartBounds2D(candidate);
            if (!plateBox.Valid)
                return false;

            double edgeScore;
            bool isBentProfile = HasSteppedOrCurvedProjectedProfile(
                candidate,
                plateBox);
            if (isBentProfile)
            {
                if (!TryGetBestMainEdgeWrapScore(mainBox, plateBox, out edgeScore))
                    return false;
            }
            else if (!TryGetStraightSidePlateScore(
                candidate,
                mainBox,
                plateBox,
                out edgeScore))
            {
                return false;
            }

            score = edgeScore + (directConnection ? 100.0 : 0.0) +
                (sameAssembly ? 25.0 : 0.0);
            return true;
        }

        private static bool TryGetStraightSidePlateScore(
            ModelPart candidate,
            Bounds2D mainBox,
            Bounds2D plateBox,
            out double score)
        {
            score = 0.0;
            if (candidate == null || !mainBox.Valid || !plateBox.Valid ||
                !IsProjectedRectangle(candidate))
            {
                return false;
            }

            double mainWidth = mainBox.MaxX - mainBox.MinX;
            double mainHeight = mainBox.MaxY - mainBox.MinY;
            double plateWidth = plateBox.MaxX - plateBox.MinX;
            double plateHeight = plateBox.MaxY - plateBox.MinY;
            if (mainWidth <= TOL || mainHeight <= TOL ||
                plateWidth <= TOL || plateHeight <= TOL)
            {
                return false;
            }

            bool touchesSingleOuterEdge = false;
            double spanRatio = 0.0;
            double normalRatio = 0.0;

            bool insideMainY = plateBox.MinY > mainBox.MinY + TOL &&
                plateBox.MaxY < mainBox.MaxY - TOL;
            if (insideMainY &&
                Math.Abs(plateBox.MaxX - mainBox.MinX) <=
                    SLOT05_AUTO_EDGE_CONTACT_TOL &&
                plateBox.MinX < mainBox.MinX - TOL)
            {
                touchesSingleOuterEdge = true;
            }
            else if (insideMainY &&
                Math.Abs(plateBox.MinX - mainBox.MaxX) <=
                    SLOT05_AUTO_EDGE_CONTACT_TOL &&
                plateBox.MaxX > mainBox.MaxX + TOL)
            {
                touchesSingleOuterEdge = true;
            }

            if (!touchesSingleOuterEdge)
                return false;

            // Nhánh 5-1 hiện tại chỉ hỗ trợ main nằm ngang và plate áp vào
            // cạnh trái/phải trong section. Không mở rộng sang plate trên/dưới.
            spanRatio = plateHeight / mainHeight;
            normalRatio = plateWidth / mainWidth;

            if (spanRatio < SLOT05_AUTO_STRAIGHT_MIN_SPAN_RATIO ||
                spanRatio >= 1.0 - 0.001 ||
                normalRatio > SLOT05_AUTO_STRAIGHT_MAX_NORMAL_RATIO)
            {
                return false;
            }

            score = spanRatio * 100.0 +
                Math.Max(0.0, 1.0 - normalRatio) * 20.0;
            return true;
        }

        private static bool IsProjectedRectangle(ModelPart candidate)
        {
            // Contour của ContourPlate có thể vẫn mang hệ tọa độ tạo hình,
            // trong khi solid vertices được Tekla trả theo work plane của view.
            List<Point> points = GetExactProjectedSolidVertices(candidate);
            if (points.Count != 4)
                return false;

            double centerX = 0.0;
            double centerY = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == null)
                    return false;
                centerX += points[i].X;
                centerY += points[i].Y;
            }
            centerX /= points.Count;
            centerY /= points.Count;

            points.Sort(delegate (Point first, Point second)
            {
                double firstAngle = Math.Atan2(
                    first.Y - centerY,
                    first.X - centerX);
                double secondAngle = Math.Atan2(
                    second.Y - centerY,
                    second.X - centerX);
                return firstAngle.CompareTo(secondAngle);
            });

            double[] edgeX = new double[4];
            double[] edgeY = new double[4];
            double[] edgeLength = new double[4];
            for (int i = 0; i < 4; i++)
            {
                Point start = points[i];
                Point end = points[(i + 1) % 4];
                edgeX[i] = end.X - start.X;
                edgeY[i] = end.Y - start.Y;
                edgeLength[i] = Math.Sqrt(
                    edgeX[i] * edgeX[i] + edgeY[i] * edgeY[i]);
                if (edgeLength[i] <= TOL)
                    return false;
            }

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                double normalizedDot = Math.Abs(
                    edgeX[i] * edgeX[next] + edgeY[i] * edgeY[next]) /
                    (edgeLength[i] * edgeLength[next]);
                if (normalizedDot > 0.05)
                    return false;
            }

            double oppositeTolerance = Math.Max(
                SLOT05_AUTO_PROFILE_POINT_TOL,
                Math.Max(edgeLength[0], edgeLength[1]) * 0.02);
            if (Math.Abs(edgeLength[0] - edgeLength[2]) > oppositeTolerance ||
                Math.Abs(edgeLength[1] - edgeLength[3]) > oppositeTolerance)
            {
                return false;
            }

            double diagonalFirst = Distance2D(points[0], points[2]);
            double diagonalSecond = Distance2D(points[1], points[3]);
            double diagonalTolerance = Math.Max(
                SLOT05_AUTO_PROFILE_POINT_TOL,
                Math.Max(diagonalFirst, diagonalSecond) * 0.02);

            return Math.Abs(diagonalFirst - diagonalSecond) <= diagonalTolerance;
        }

        private static bool HasSteppedOrCurvedProjectedProfile(
            ModelPart part,
            Bounds2D bounds)
        {
            if (part == null || !bounds.Valid)
                return false;

            List<Point> points = GetExactProjectedProfilePoints(part);
            // Hình chữ nhật trong hệ trục view chỉ có bốn góc chiếu duy nhất.
            // Plate mẫu đang mở có sáu điểm biên chiếu, thể hiện phần gấp/bậc.
            // Đọc danh sách điểm trước cạnh cong để kết quả không phụ thuộc thứ
            // tự Tekla remote enumerator trả solid edges.
            if (points.Count >= SLOT05_AUTO_MIN_PROFILE_POINTS)
                return true;

            return HasVisibleCurvedSolidEdge(part, bounds);
        }

        private static List<Point> GetExactProjectedProfilePoints(ModelPart part)
        {
            List<Point> result = new List<Point>();

            try
            {
                object contour = GetPropertyValue(part, "Contour");
                object contourPoints = GetPropertyValue(contour, "ContourPoints");
                IEnumerable enumerable = contourPoints as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object value in enumerable)
                    {
                        Point point = value as Point;
                        if (point == null)
                            point = GetPropertyValue(value, "Point") as Point;

                        if (point != null)
                        {
                            AddUniquePoint2D(
                                result,
                                new Point(point.X, point.Y, 0),
                                SLOT05_AUTO_PROFILE_POINT_TOL);
                        }
                    }
                }
            }
            catch
            {
            }

            if (result.Count >= 3)
                return result;

            try
            {
                Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;

                    if (edge.StartPoint != null)
                        AddUniquePoint2D(
                            result,
                            new Point(edge.StartPoint.X, edge.StartPoint.Y, 0),
                            SLOT05_AUTO_PROFILE_POINT_TOL);
                    if (edge.EndPoint != null)
                        AddUniquePoint2D(
                            result,
                            new Point(edge.EndPoint.X, edge.EndPoint.Y, 0),
                            SLOT05_AUTO_PROFILE_POINT_TOL);
                }
            }
            catch
            {
            }

            return result;
        }

        private static bool HasVisibleCurvedSolidEdge(ModelPart part, Bounds2D bounds)
        {
            try
            {
                if (part == null || !bounds.Valid)
                    return false;

                Solid solid = part.GetSolid();
                Tekla.Structures.Solid.EdgeEnumerator edges = solid.GetEdgeEnumerator();
                while (edges != null && edges.MoveNext())
                {
                    Tekla.Structures.Solid.Edge edge =
                        edges.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null || edge.StartPoint == null || edge.EndPoint == null)
                        continue;

                    string edgeType = edge.Type.ToString();
                    if (edgeType.IndexOf(
                        "CURVED_SURFACE",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Point start = new Point(edge.StartPoint.X, edge.StartPoint.Y, 0);
                    Point end = new Point(edge.EndPoint.X, edge.EndPoint.Y, 0);
                    if (Distance2D(start, end) > SLOT05_AUTO_EDGE_CONTACT_TOL &&
                        (PointTouchesProjectedBounds(start, bounds) ||
                         PointTouchesProjectedBounds(end, bounds)))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool PointTouchesProjectedBounds(Point point, Bounds2D bounds)
        {
            if (point == null || !bounds.Valid)
                return false;

            return Math.Abs(point.X - bounds.MinX) <= SLOT05_AUTO_PROFILE_POINT_TOL ||
                Math.Abs(point.X - bounds.MaxX) <= SLOT05_AUTO_PROFILE_POINT_TOL ||
                Math.Abs(point.Y - bounds.MinY) <= SLOT05_AUTO_PROFILE_POINT_TOL ||
                Math.Abs(point.Y - bounds.MaxY) <= SLOT05_AUTO_PROFILE_POINT_TOL;
        }

        private static bool TryGetBestMainEdgeWrapScore(
            Bounds2D mainBox,
            Bounds2D plateBox,
            out double bestScore)
        {
            bestScore = -999999999.0;
            double score;

            if (TryScoreVerticalMainEdge(mainBox, plateBox, false, out score) && score > bestScore)
                bestScore = score;
            if (TryScoreVerticalMainEdge(mainBox, plateBox, true, out score) && score > bestScore)
                bestScore = score;
            if (TryScoreHorizontalMainEdge(mainBox, plateBox, false, out score) && score > bestScore)
                bestScore = score;
            if (TryScoreHorizontalMainEdge(mainBox, plateBox, true, out score) && score > bestScore)
                bestScore = score;

            return bestScore > -999999000.0;
        }

        private static bool TryScoreVerticalMainEdge(
            Bounds2D mainBox,
            Bounds2D plateBox,
            bool rightSide,
            out double score)
        {
            score = 0.0;
            double mainWidth = mainBox.MaxX - mainBox.MinX;
            double mainHeight = mainBox.MaxY - mainBox.MinY;
            double plateWidth = plateBox.MaxX - plateBox.MinX;
            double plateHeight = plateBox.MaxY - plateBox.MinY;
            if (mainWidth <= TOL || mainHeight <= TOL || plateWidth <= TOL || plateHeight <= TOL)
                return false;
            if (plateWidth > mainWidth * SLOT05_AUTO_MAX_NORMAL_SIZE_RATIO + SLOT05_AUTO_EDGE_CONTACT_TOL)
                return false;

            double edgeX = rightSide ? mainBox.MaxX : mainBox.MinX;
            double plateCenterX = (plateBox.MinX + plateBox.MaxX) / 2.0;
            double nearDistance = Math.Abs(plateCenterX - edgeX);
            double farDistance = Math.Abs(plateCenterX - (rightSide ? mainBox.MinX : mainBox.MaxX));
            if (nearDistance >= farDistance)
                return false;

            double insideDepth = rightSide
                ? edgeX - plateBox.MinX
                : plateBox.MaxX - edgeX;
            double outsideDepth = rightSide
                ? plateBox.MaxX - edgeX
                : edgeX - plateBox.MinX;
            if (insideDepth < -SLOT05_AUTO_EDGE_CONTACT_TOL ||
                outsideDepth < -SLOT05_AUTO_EDGE_CONTACT_TOL)
            {
                return false;
            }

            double overlap = IntervalOverlap(
                mainBox.MinY,
                mainBox.MaxY,
                plateBox.MinY,
                plateBox.MaxY);
            double overlapRatio = overlap / Math.Max(TOL, Math.Min(mainHeight, plateHeight));
            if (overlapRatio < SLOT05_AUTO_MIN_SIDE_OVERLAP_RATIO)
                return false;

            double normalRatio = plateWidth / mainWidth;
            score = overlapRatio * 100.0 +
                Math.Max(0.0, 1.0 - normalRatio) * 20.0 +
                (insideDepth > TOL && outsideDepth > TOL ? 30.0 : 15.0);
            return true;
        }

        private static bool TryScoreHorizontalMainEdge(
            Bounds2D mainBox,
            Bounds2D plateBox,
            bool topSide,
            out double score)
        {
            score = 0.0;
            double mainWidth = mainBox.MaxX - mainBox.MinX;
            double mainHeight = mainBox.MaxY - mainBox.MinY;
            double plateWidth = plateBox.MaxX - plateBox.MinX;
            double plateHeight = plateBox.MaxY - plateBox.MinY;
            if (mainWidth <= TOL || mainHeight <= TOL || plateWidth <= TOL || plateHeight <= TOL)
                return false;
            if (plateHeight > mainHeight * SLOT05_AUTO_MAX_NORMAL_SIZE_RATIO + SLOT05_AUTO_EDGE_CONTACT_TOL)
                return false;

            double edgeY = topSide ? mainBox.MaxY : mainBox.MinY;
            double plateCenterY = (plateBox.MinY + plateBox.MaxY) / 2.0;
            double nearDistance = Math.Abs(plateCenterY - edgeY);
            double farDistance = Math.Abs(plateCenterY - (topSide ? mainBox.MinY : mainBox.MaxY));
            if (nearDistance >= farDistance)
                return false;

            double insideDepth = topSide
                ? edgeY - plateBox.MinY
                : plateBox.MaxY - edgeY;
            double outsideDepth = topSide
                ? plateBox.MaxY - edgeY
                : edgeY - plateBox.MinY;
            if (insideDepth < -SLOT05_AUTO_EDGE_CONTACT_TOL ||
                outsideDepth < -SLOT05_AUTO_EDGE_CONTACT_TOL)
            {
                return false;
            }

            double overlap = IntervalOverlap(
                mainBox.MinX,
                mainBox.MaxX,
                plateBox.MinX,
                plateBox.MaxX);
            double overlapRatio = overlap / Math.Max(TOL, Math.Min(mainWidth, plateWidth));
            if (overlapRatio < SLOT05_AUTO_MIN_SIDE_OVERLAP_RATIO)
                return false;

            double normalRatio = plateHeight / mainHeight;
            score = overlapRatio * 100.0 +
                Math.Max(0.0, 1.0 - normalRatio) * 20.0 +
                (insideDepth > TOL && outsideDepth > TOL ? 30.0 : 15.0);
            return true;
        }

        private static double IntervalOverlap(
            double firstMin,
            double firstMax,
            double secondMin,
            double secondMax)
        {
            return Math.Max(0.0, Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin));
        }

        private static bool IsPartInSameAssembly(ModelPart part, ModelPart mainPart)
        {
            try
            {
                if (part == null || mainPart == null)
                    return false;

                TSM.Assembly partAssembly = part.GetAssembly();
                TSM.Assembly mainAssembly = mainPart.GetAssembly();
                return partAssembly != null && mainAssembly != null &&
                    SameIdentifier(partAssembly.Identifier, mainAssembly.Identifier);
            }
            catch
            {
                return false;
            }
        }

        private static bool ArePartsDirectlyBoltConnected(ModelPart first, ModelPart second)
        {
            if (first == null || second == null)
                return false;

            return PartBoltCollectionReferencesPart(first, second) ||
                PartBoltCollectionReferencesPart(second, first);
        }

        private static bool PartBoltCollectionReferencesPart(ModelPart owner, ModelPart other)
        {
            try
            {
                ModelObjectEnumerator bolts = owner.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup boltGroup = bolts.Current as ModelBoltGroup;
                    if (boltGroup == null)
                        continue;

                    string[] propertyNames = new string[]
                    {
                        "PartToBoltTo",
                        "PartToBeBolted",
                        "OtherPartsToBolt",
                        "Father",
                        "FatherObject"
                    };

                    for (int i = 0; i < propertyNames.Length; i++)
                    {
                        object value = GetPropertyValue(boltGroup, propertyNames[i]);
                        if (ObjectOrEnumerableContainsIdentifier(value, other.Identifier))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool ObjectOrEnumerableContainsIdentifier(object value, Identifier id)
        {
            try
            {
                if (value == null || id == null)
                    return false;

                ModelObject modelObject = value as ModelObject;
                if (modelObject != null && SameIdentifier(modelObject.Identifier, id))
                    return true;

                Identifier directIdentifier = value as Identifier;
                if (directIdentifier != null && SameIdentifier(directIdentifier, id))
                    return true;

                IEnumerable enumerable = value as IEnumerable;
                if (enumerable != null && !(value is string))
                {
                    foreach (object item in enumerable)
                    {
                        if (ObjectOrEnumerableContainsIdentifier(item, id))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void AddUniqueModelPart(List<ModelPart> parts, ModelPart candidate)
        {
            if (parts == null || candidate == null || candidate.Identifier == null)
                return;

            if (!ContainsPartIdentifier(parts, candidate.Identifier))
                parts.Add(candidate);
        }

        private static bool ContainsPartIdentifier(List<ModelPart> parts, Identifier identifier)
        {
            if (parts == null || identifier == null)
                return false;

            for (int i = 0; i < parts.Count; i++)
            {
                ModelPart part = parts[i];
                if (part != null && SameIdentifier(part.Identifier, identifier))
                    return true;
            }

            return false;
        }

        private static ModelPart FindPartByIdentifier(
            List<ModelPart> parts,
            Identifier identifier)
        {
            if (parts == null || identifier == null)
                return null;

            for (int i = 0; i < parts.Count; i++)
            {
                ModelPart part = parts[i];
                if (part != null && SameIdentifier(part.Identifier, identifier))
                    return part;
            }

            return null;
        }

        private static bool PartIdentifierSetsMatch(
            List<ModelPart> first,
            List<ModelPart> second)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;

            for (int i = 0; i < first.Count; i++)
            {
                ModelPart part = first[i];
                if (part == null || part.Identifier == null ||
                    !ContainsPartIdentifier(second, part.Identifier))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareAutomaticTargets(Slot05AutoTarget first, Slot05AutoTarget second)
        {
            if (first == null && second == null) return 0;
            if (first == null) return 1;
            if (second == null) return -1;

            int c = second.MatchedPlateCount.CompareTo(first.MatchedPlateCount);
            if (c != 0) return c;

            c = second.Plates.Count.CompareTo(first.Plates.Count);
            if (c != 0) return c;

            c = second.DirectConnectionCount.CompareTo(first.DirectConnectionCount);
            if (c != 0) return c;

            return second.GeometryScore.CompareTo(first.GeometryScore);
        }

        private static bool AutomaticTargetsAreAmbiguous(
            Slot05AutoTarget first,
            Slot05AutoTarget second)
        {
            if (first == null || second == null)
                return false;

            return first.MatchedPlateCount == second.MatchedPlateCount &&
                first.Plates.Count == second.Plates.Count &&
                first.DirectConnectionCount == second.DirectConnectionCount &&
                Math.Abs(first.GeometryScore - second.GeometryScore) <= 1.0;
        }

        private static string DescribePartForAudit(ModelPart part)
        {
            if (part == null)
                return "<null>";

            string id = part.Identifier == null
                ? "?"
                : part.Identifier.ID.ToString(CultureInfo.InvariantCulture);
            string assemblyId = "?";
            try
            {
                TSM.Assembly assembly = part.GetAssembly();
                if (assembly != null && assembly.Identifier != null)
                {
                    assemblyId = assembly.Identifier.ID.ToString(
                        CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }

            return "id=" + id +
                " asm=" + assemblyId +
                " type=" + part.GetType().Name +
                " profile=" + GetProfileString(part) +
                " name=" + GetReportString(part, "NAME") +
                " partPos=" + GetReportString(part, "PART_POS");
        }

        private static string DescribeViewForAudit(TSD.View view)
        {
            if (view == null)
                return "<null>";

            object identifier = GetPropertyValue(view, "Identifier");
            object viewType = GetPropertyValue(view, "ViewType");
            object name = GetPropertyValue(view, "Name");
            return "id=" + (identifier == null ? "?" : identifier.ToString()) +
                " type=" + (viewType == null ? view.GetType().Name : viewType.ToString()) +
                " name=" + (name == null ? "" : name.ToString());
        }

        private static string FormatBoundsForAudit(Bounds2D bounds)
        {
            if (!bounds.Valid)
                return "NA";

            return "X[" + bounds.MinX.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + bounds.MaxX.ToString("0.###", CultureInfo.InvariantCulture) +
                "] Y[" + bounds.MinY.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + bounds.MaxY.ToString("0.###", CultureInfo.InvariantCulture) + "]";
        }

        private static string FormatPointsForAudit(List<Point> points)
        {
            if (points == null || points.Count == 0)
                return "[]";

            StringBuilder text = new StringBuilder();
            text.Append("[");
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    text.Append(";");

                Point point = points[i];
                if (point == null)
                {
                    text.Append("null");
                    continue;
                }

                text.Append(point.X.ToString("0.###", CultureInfo.InvariantCulture))
                    .Append(",")
                    .Append(point.Y.ToString("0.###", CultureInfo.InvariantCulture));
            }
            text.Append("]");
            return text.ToString();
        }

        private static string FormatVectorForAudit(Vector vector)
        {
            if (vector == null)
                return "null";

            return "[" + vector.X.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + vector.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + vector.Z.ToString("0.###", CultureInfo.InvariantCulture) + "]";
        }

        private static string FormatHoleGapsForAudit(ModelPart plate, List<Point> holes)
        {
            if (plate == null || holes == null || holes.Count == 0)
                return "[]";

            StringBuilder text = new StringBuilder();
            text.Append("[");
            for (int i = 0; i < holes.Count; i++)
            {
                if (i > 0)
                    text.Append(";");
                text.Append(GetHoleCenterDimGapByMThenPhi(plate, holes[i]).ToString(
                    "0.###",
                    CultureInfo.InvariantCulture));
            }
            text.Append("]");
            return text.ToString();
        }

        private static bool IsPlateBelowMain(Bounds2D plateBox, Bounds2D mainBox)
        {
            if (!plateBox.Valid || !mainBox.Valid)
                return false;

            double plateCenterY = (plateBox.MinY + plateBox.MaxY) / 2.0;
            double mainCenterY = (mainBox.MinY + mainBox.MaxY) / 2.0;

            return plateCenterY < mainCenterY;
        }

        private class Slot05PlateGroup
        {
            public ModelPart Plate;
            public Bounds2D PlateBox;
            public ModelPart MainBeam;
            public Bounds2D MainBox;
            public List<Point> HoleCenters;
        }

        private static double Slot05TierOffset(int tier)
        {
            if (tier < 0)
                tier = 0;

            return SLOT05_DIM_TIER_BASE + SLOT05_DIM_TIER_STEP * tier;
        }

        private static double GetLeftTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            double targetX = mainBox.MinX - Slot05TierOffset(tier);
            if (firstPoint == null)
                return Slot05TierOffset(tier);

            return Math.Abs(firstPoint.X - targetX);
        }

        private static double GetRightTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            double targetX = mainBox.MaxX + Slot05TierOffset(tier);
            if (firstPoint == null)
                return Slot05TierOffset(tier);

            return Math.Abs(targetX - firstPoint.X);
        }

        private static double GetMiddleLeftTierDistance(Point firstPoint, Bounds2D mainBox, int tier)
        {
            // Plate giữa: dim luôn đẩy về bên trái, offset tính từ mép trái ngoài cùng của main.
            return GetLeftTierDistance(firstPoint, mainBox, tier);
        }

        private static int CompareGroupByPlateCenterXThenY(Slot05PlateGroup a, Slot05PlateGroup b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            double ax = (a.PlateBox.MinX + a.PlateBox.MaxX) / 2.0;
            double bx = (b.PlateBox.MinX + b.PlateBox.MaxX) / 2.0;
            int c = ax.CompareTo(bx);
            if (c != 0) return c;

            double ay = (a.PlateBox.MinY + a.PlateBox.MaxY) / 2.0;
            double by = (b.PlateBox.MinY + b.PlateBox.MaxY) / 2.0;
            return ay.CompareTo(by);
        }

        private static int CompareGroupByPlateCenterYThenX(Slot05PlateGroup a, Slot05PlateGroup b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            double ay = (a.PlateBox.MinY + a.PlateBox.MaxY) / 2.0;
            double by = (b.PlateBox.MinY + b.PlateBox.MaxY) / 2.0;
            int c = ay.CompareTo(by);
            if (c != 0) return c;

            double ax = (a.PlateBox.MinX + a.PlateBox.MaxX) / 2.0;
            double bx = (b.PlateBox.MinX + b.PlateBox.MaxX) / 2.0;
            return ax.CompareTo(bx);
        }

        private static Bounds2D GetMainUnionBox(List<Slot05PlateGroup> groups)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            try
            {
                if (groups == null || groups.Count == 0)
                    return b;

                for (int i = 0; i < groups.Count; i++)
                {
                    Slot05PlateGroup g = groups[i];
                    if (g == null || !g.MainBox.Valid)
                        continue;

                    if (!b.Valid)
                    {
                        b = g.MainBox;
                        b.Valid = true;
                    }
                    else
                    {
                        if (g.MainBox.MinX < b.MinX) b.MinX = g.MainBox.MinX;
                        if (g.MainBox.MaxX > b.MaxX) b.MaxX = g.MainBox.MaxX;
                        if (g.MainBox.MinY < b.MinY) b.MinY = g.MainBox.MinY;
                        if (g.MainBox.MaxY > b.MaxY) b.MaxY = g.MainBox.MaxY;
                    }
                }
            }
            catch
            {
            }

            return b;
        }

        private static Point PickPrimaryVerticalHole(Bounds2D plateBox, List<Point> holes, bool pickRightHole)
        {
            if (holes == null || holes.Count == 0)
                return null;

            Point best = null;
            double bestX = pickRightHole ? -999999999.0 : 999999999.0;

            for (int i = 0; i < holes.Count; i++)
            {
                Point p = holes[i];
                if (p == null)
                    continue;

                if (p.X < plateBox.MinX - PLATE_BOUND_TOL || p.X > plateBox.MaxX + PLATE_BOUND_TOL ||
                    p.Y < plateBox.MinY - PLATE_BOUND_TOL || p.Y > plateBox.MaxY + PLATE_BOUND_TOL)
                    continue;

                if (best == null ||
                    (pickRightHole && p.X > bestX) ||
                    (!pickRightHole && p.X < bestX))
                {
                    best = p;
                    bestX = p.X;
                }
            }

            if (best == null)
                return null;

            return new Point(best.X, best.Y, 0);
        }

        private static double GetReferenceYForHorizontalPlateEdge(
            Bounds2D plateBox,
            List<Point> holeCenters,
            bool dimToTop)
        {
            try
            {
                if (holeCenters == null || holeCenters.Count == 0)
                    return dimToTop ? plateBox.MaxY : plateBox.MinY;

                double avgY = 0.0;
                for (int i = 0; i < holeCenters.Count; i++)
                    avgY += holeCenters[i].Y;
                avgY = avgY / holeCenters.Count;

                // Nếu lỗ nằm gần mép trên/dưới thì dùng đúng mép gần đó.
                // Còn lại dùng mép theo hướng dim để chân dim bám vào biên plate.
                double dTop = Math.Abs(plateBox.MaxY - avgY);
                double dBottom = Math.Abs(avgY - plateBox.MinY);

                if (dTop < dBottom)
                    return plateBox.MaxY;

                if (dBottom < dTop)
                    return plateBox.MinY;

                return dimToTop ? plateBox.MaxY : plateBox.MinY;
            }
            catch
            {
                return dimToTop ? plateBox.MaxY : plateBox.MinY;
            }
        }

        private static ModelPart FindMainBeamForPlate(
            ModelPart plate,
            Bounds2D plateBox,
            List<ModelPart> allViewParts)
        {
            if (plate == null || !plateBox.Valid || allViewParts == null || allViewParts.Count == 0)
                return null;

            string plateAssembly = GetReportString(plate, "ASSEMBLY_POS");
            Point plateCenter = CenterOf(plateBox);

            ModelPart best = null;
            double bestScore = -999999999.0;

            for (int i = 0; i < allViewParts.Count; i++)
            {
                ModelPart p = allViewParts[i];
                if (p == null || p.Identifier == null || plate.Identifier == null)
                    continue;

                if (SameIdentifier(p.Identifier, plate.Identifier))
                    continue;

                if (IsDummyReferencePart(p))
                    continue;

                if (IsPlateLikePart(p))
                    continue;

                Bounds2D b = GetPartBounds2D(p);
                if (!b.Valid)
                    continue;

                Point c = CenterOf(b);
                double area = Math.Abs(b.MaxX - b.MinX) * Math.Abs(b.MaxY - b.MinY);
                double distance = Distance2D(plateCenter, c);

                double score = area - distance * 0.25;

                string asm = GetReportString(p, "ASSEMBLY_POS");
                if (!string.IsNullOrEmpty(plateAssembly) &&
                    !string.IsNullOrEmpty(asm) &&
                    string.Equals(plateAssembly, asm, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100000000.0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        private static List<ModelPart> GetAllModelPartsInView(TSM.Model model, TSD.View view)
        {
            List<ModelPart> result = new List<ModelPart>();

            try
            {
                if (model == null || view == null)
                    return result;

                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(DrawingPart));
                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp == null || dp.ModelIdentifier == null)
                        continue;

                    ModelPart mp = model.SelectModelObject(dp.ModelIdentifier) as ModelPart;
                    if (mp == null)
                        continue;

                    bool exists = false;
                    for (int i = 0; i < result.Count; i++)
                    {
                        if (SameIdentifier(result[i].Identifier, mp.Identifier))
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                        result.Add(mp);
                }
            }
            catch
            {
            }

            return result;
        }

        private static Point GetHolePointWithMBoltGap(ModelPart plate, Point holeCenter, Vector direction)
        {
            try
            {
                if (plate == null || holeCenter == null || direction == null)
                    return holeCenter;

                double gap = GetHoleCenterDimGapByMThenPhi(plate, holeCenter);
                if (gap <= 0.0)
                    return new Point(holeCenter.X, holeCenter.Y, 0);

                double x = holeCenter.X;
                double y = holeCenter.Y;

                if (Math.Abs(direction.X) >= Math.Abs(direction.Y))
                {
                    if (direction.X < 0.0)
                        x -= gap;
                    else if (direction.X > 0.0)
                        x += gap;
                }
                else
                {
                    if (direction.Y < 0.0)
                        y -= gap;
                    else if (direction.Y > 0.0)
                        y += gap;
                }

                return new Point(x, y, 0);
            }
            catch
            {
                return holeCenter;
            }
        }

        private static double GetHoleCenterDimGapByMThenPhi(ModelPart plate, Point holeCenter)
        {
            try
            {
                if (plate == null || holeCenter == null)
                    return 0.0;

                ModelObjectEnumerator bolts = plate.GetBolts();
                while (bolts != null && bolts.MoveNext())
                {
                    ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                    if (bg == null || bg.BoltPositions == null)
                        continue;

                    foreach (object obj in bg.BoltPositions)
                    {
                        Point p = obj as Point;
                        if (p == null)
                            continue;

                        if (Math.Abs(p.X - holeCenter.X) <= 1.0 &&
                            Math.Abs(p.Y - holeCenter.Y) <= 1.0)
                        {
                            double d = GetBoltGroupMThenPhiForDimGap(bg);
                            if (d > 0.0)
                                return d;
                        }
                    }
                }
            }
            catch
            {
            }

            return 0.0;
        }

        private static double GetBoltGroupMThenPhiForDimGap(ModelBoltGroup bg)
        {
            if (bg == null)
                return 0.0;

            // Slot05 yêu cầu ưu tiên M/BoltSize trước, sau đó mới tới phi lỗ.
            double v = GetReportDouble(bg, "BOLT_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "Diameter");
            if (v > 0.0 && v < 500.0) return v;

            // Sau M mới tới phi lỗ / hole size.
            v = GetReportDouble(bg, "HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_DIAMETER");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "HOLE_DIAM");
            if (v > 0.0 && v < 500.0) return v;

            v = GetReportDouble(bg, "BOLT_HOLE_SIZE");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "HoleSize");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleDiameter");
            if (v > 0.0 && v < 500.0) return v;

            v = GetDoublePropertyByReflection(bg, "BoltHoleSize");
            if (v > 0.0 && v < 500.0) return v;

            return 0.0;
        }

        private static double GetReportDouble(ModelBoltGroup bg, string propertyName)
        {
            try
            {
                if (bg == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                double value = 0.0;
                bg.GetReportProperty(propertyName, ref value);
                return value;
            }
            catch
            {
                return 0.0;
            }
        }

        private static double GetDoublePropertyByReflection(object obj, string propertyName)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(propertyName))
                    return 0.0;

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (prop == null || !prop.CanRead)
                    return 0.0;

                object value = prop.GetValue(obj, null);
                if (value == null)
                    return 0.0;

                if (value is double)
                    return (double)value;

                if (value is int)
                    return Convert.ToDouble((int)value);

                if (value is float)
                    return Convert.ToDouble((float)value);

                double result;
                if (double.TryParse(
                    value.ToString().Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result))
                    return result;
            }
            catch
            {
            }

            return 0.0;
        }

        private static List<Point> GetBoltCentersInsidePlate(
            TSM.Model model,
            TSD.View view,
            ModelPart plate,
            Bounds2D plateBox)
        {
            List<Point> result = new List<Point>();

            try
            {
                if (model == null || view == null || plate == null || !plateBox.Valid)
                    return result;

                List<Identifier> allowedBoltIds = new List<Identifier>();

                // Ưu tiên bolts thật thuộc part.
                try
                {
                    ModelObjectEnumerator bolts = plate.GetBolts();
                    while (bolts != null && bolts.MoveNext())
                    {
                        ModelBoltGroup bg = bolts.Current as ModelBoltGroup;
                        if (bg == null)
                            continue;

                        AddUniqueIdentifier(allowedBoltIds, bg.Identifier);
                        AddBoltGroupPositionsInsideBounds(bg, plateBox, result);
                    }
                }
                catch
                {
                }

                // Fallback/ bổ sung: quét Drawing Bolt trong view, nhưng chỉ nhận bolt thuộc chính plate đang xét.
                TSD.DrawingObjectEnumerator e = view.GetAllObjects(typeof(Tekla.Structures.Drawing.Bolt));
                while (e != null && e.MoveNext())
                {
                    TSD.DrawingObject dobj = e.Current as TSD.DrawingObject;
                    if (dobj == null)
                        continue;

                    Identifier id = TryGetModelIdentifier(dobj);
                    if (id == null)
                        continue;

                    if (!ContainsIdentifier(allowedBoltIds, id))
                        continue;

                    ModelObject mo = model.SelectModelObject(id);
                    ModelBoltGroup bg = mo as ModelBoltGroup;
                    AddBoltGroupPositionsInsideBounds(bg, plateBox, result);
                }
            }
            catch
            {
            }

            result.Sort(ComparePointByXThenY);
            return result;
        }

        private static void AddBoltGroupPositionsInsideBounds(
            ModelBoltGroup bg,
            Bounds2D plateBox,
            List<Point> result)
        {
            try
            {
                if (bg == null || bg.BoltPositions == null || result == null || !plateBox.Valid)
                    return;

                foreach (object obj in bg.BoltPositions)
                {
                    Point p = obj as Point;
                    if (p == null)
                        continue;

                    if (!PointInsideBounds(p, plateBox, PLATE_BOUND_TOL))
                        continue;

                    AddUniquePoint2D(result, new Point(p.X, p.Y, 0), POINT_DUP_TOL);
                }
            }
            catch
            {
            }
        }

        private static void AddUniqueIdentifier(List<Identifier> list, Identifier id)
        {
            try
            {
                if (list == null || id == null)
                    return;

                if (ContainsIdentifier(list, id))
                    return;

                list.Add(id);
            }
            catch
            {
            }
        }

        private static bool ContainsIdentifier(List<Identifier> list, Identifier id)
        {
            try
            {
                if (list == null || id == null)
                    return false;

                for (int i = 0; i < list.Count; i++)
                {
                    if (SameIdentifier(list[i], id))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static Identifier TryGetModelIdentifier(object drawingObject)
        {
            try
            {
                if (drawingObject == null)
                    return null;

                object value = GetPropertyValue(drawingObject, "ModelIdentifier");
                return value as Identifier;
            }
            catch
            {
                return null;
            }
        }

        private struct Bounds2D
        {
            public bool Valid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private static Bounds2D GetPartBounds2D(ModelPart part)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            try
            {
                Solid s = part.GetSolid();
                Point min = s.MinimumPoint;
                Point max = s.MaximumPoint;

                b.MinX = Math.Min(min.X, max.X);
                b.MaxX = Math.Max(min.X, max.X);
                b.MinY = Math.Min(min.Y, max.Y);
                b.MaxY = Math.Max(min.Y, max.Y);
                b.Valid = Math.Abs(b.MaxX - b.MinX) > TOL && Math.Abs(b.MaxY - b.MinY) > TOL;
            }
            catch
            {
            }

            return b;
        }

        private static Point CenterOf(Bounds2D b)
        {
            return new Point(
                (b.MinX + b.MaxX) / 2.0,
                (b.MinY + b.MaxY) / 2.0,
                0);
        }

        private static bool PointInsideBounds(Point p, Bounds2D b, double tol)
        {
            if (p == null || !b.Valid)
                return false;

            return p.X >= b.MinX - tol &&
                   p.X <= b.MaxX + tol &&
                   p.Y >= b.MinY - tol &&
                   p.Y <= b.MaxY + tol;
        }

        private static int ComparePointByXThenY(Point a, Point b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            return a.Y.CompareTo(b.Y);
        }

        private static int ComparePointByYThenX(Point a, Point b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            return a.X.CompareTo(b.X);
        }

        private static void AddUniquePoint2D(List<Point> list, Point p, double tol)
        {
            if (list == null || p == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                if (Distance2D(list[i], p) <= tol)
                    return;
            }

            list.Add(p);
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            string attributeName)
        {
            try
            {
                if (handler == null || view == null || points == null || points.Length < 2)
                    return false;

                PointList list = new PointList();

                for (int i = 0; i < points.Length; i++)
                {
                    Point p = points[i];
                    if (p == null)
                        continue;

                    bool duplicate = false;
                    foreach (Point old in list)
                    {
                        if (Distance2D(old, p) <= POINT_DUP_TOL)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                        list.Add(new Point(p.X, p.Y, 0));
                }

                if (list.Count < 2)
                    return false;

                TSD.StraightDimensionSet dim =
                    handler.CreateDimensionSet(view, list, direction, distance);

                if (dim != null && !string.IsNullOrEmpty(attributeName))
                    TryApplyStraightDimAttributes(dim, attributeName);

                return dim != null;
            }
            catch
            {
                return false;
            }
        }

        private static void TryApplyStraightDimAttributes(
            TSD.StraightDimensionSet dim,
            string attributeName)
        {
            try
            {
                if (dim == null || string.IsNullOrEmpty(attributeName))
                    return;

                object attr = dim.Attributes;
                if (attr == null)
                    return;

                MethodInfo loadMethod = attr.GetType().GetMethod(
                    "LoadAttributes",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (loadMethod == null)
                    return;

                loadMethod.Invoke(attr, new object[] { attributeName });
                dim.Modify();
            }
            catch
            {
            }
        }

        private static List<DrawingPart> GetSelectedDrawingParts(TSD.DrawingHandler dh)
        {
            List<DrawingPart> result = new List<DrawingPart>();

            try
            {
                TSD.DrawingObjectEnumerator e =
                    dh.GetDrawingObjectSelector().GetSelected();

                while (e != null && e.MoveNext())
                {
                    DrawingPart dp = e.Current as DrawingPart;
                    if (dp != null)
                        result.Add(dp);
                }
            }
            catch
            {
            }

            return result;
        }

        private static ModelPart SelectModelPart(TSM.Model model, DrawingPart dp)
        {
            try
            {
                if (model == null || dp == null || dp.ModelIdentifier == null)
                    return null;

                return model.SelectModelObject(dp.ModelIdentifier) as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static TSD.View TryGetSelectedPartsView(params DrawingPart[] parts)
        {
            TSD.View result = null;

            if (parts == null)
                return null;

            for (int i = 0; i < parts.Length; i++)
            {
                TSD.View v = TryGetDrawingObjectView(parts[i]);
                if (v == null)
                    continue;

                if (result == null)
                    result = v;
                else if (!object.ReferenceEquals(result, v))
                    return result;
            }

            return result;
        }

        private static TSD.View TryGetDrawingObjectView(object drawingObject)
        {
            if (drawingObject == null)
                return null;

            string[] methodNames = new string[]
            {
                "GetView",
                "GetFatherView",
                "GetParentView"
            };

            for (int i = 0; i < methodNames.Length; i++)
            {
                try
                {
                    MethodInfo m = drawingObject.GetType().GetMethod(
                        methodNames[i],
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (m == null)
                        continue;

                    object value = m.Invoke(drawingObject, null);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            string[] propertyNames = new string[]
            {
                "View",
                "FatherView",
                "ParentView"
            };

            for (int i = 0; i < propertyNames.Length; i++)
            {
                try
                {
                    object value = GetPropertyValue(drawingObject, propertyNames[i]);
                    TSD.View view = value as TSD.View;
                    if (view != null)
                        return view;
                }
                catch
                {
                }
            }

            return null;
        }

        private static TSD.View FindViewContainingPart(TSD.Drawing drawing, Identifier id)
        {
            try
            {
                if (drawing == null || id == null)
                    return null;

                TSD.ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                TSD.DrawingObjectEnumerator views = sheet.GetAllViews();
                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null)
                        continue;

                    TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                    while (parts != null && parts.MoveNext())
                    {
                        DrawingPart dp = parts.Current as DrawingPart;
                        if (dp == null || dp.ModelIdentifier == null)
                            continue;

                        if (SameIdentifier(dp.ModelIdentifier, id))
                            return view;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            if (part == null)
                return false;

            string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();
            string material = GetReportString(part, "MATERIAL").Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (partPos == "DUMMY-99" ||
                partPos.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase) ||
                material == "JOINT" ||
                name.StartsWith("BJ", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsPlateLikePart(ModelPart part)
        {
            if (part == null)
                return false;

            if (IsDummyReferencePart(part))
                return false;

            string typeName = part.GetType().FullName;
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();

            if (typeName.IndexOf("ContourPlate") >= 0)
                return true;

            if (name.IndexOf("PLATE") >= 0)
                return true;

            if (profile.StartsWith("PL") ||
                profile.StartsWith("PLT") ||
                profile.StartsWith("FB") ||
                profile.StartsWith("FL") ||
                profile.IndexOf("PLATE") >= 0)
                return true;

            // Slot05 cho phép tấm dạng L/angle plate nếu người dùng pick trực tiếp.
            // Nhưng vẫn tránh nhận nhầm các beam chính quá lớn khi tự quét main.
            if (profile.StartsWith("L") && typeName.IndexOf("Beam") >= 0)
                return true;

            return false;
        }

        private static string GetProfileString(ModelPart part)
        {
            if (part == null)
                return "";

            try
            {
                object profileObj = GetPropertyValue(part, "Profile");
                object profileString = GetPropertyValue(profileObj, "ProfileString");
                if (profileString != null)
                    return profileString.ToString();
            }
            catch
            {
            }

            string value = "";
            try
            {
                if (part.GetReportProperty("PROFILE", ref value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
            }

            return "";
        }

        private static string GetReportString(ModelObject obj, string reportName)
        {
            if (obj == null)
                return "";

            try
            {
                string s = "";
                obj.GetReportProperty(reportName, ref s);
                if (s == null)
                    return "";
                return s.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static object GetPropertyValue(object obj, string name)
        {
            try
            {
                if (obj == null || string.IsNullOrEmpty(name))
                    return null;

                PropertyInfo p = obj.GetType().GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p == null || !p.CanRead || p.GetIndexParameters().Length > 0)
                    return null;

                return p.GetValue(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool SameIdentifier(Identifier a, Identifier b)
        {
            if (a == null || b == null)
                return false;

            try
            {
                return a.ID == b.ID;
            }
            catch
            {
                return a.ToString() == b.ToString();
            }
        }

        private static double Distance2D(Point a, Point b)
        {
            if (a == null || b == null)
                return 999999999.0;

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Msg(string text)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    text,
                    "PHU Slot05 Selected Plate Edge Hole Dim",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
