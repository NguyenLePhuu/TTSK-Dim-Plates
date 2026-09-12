#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;
using Tekla.Structures.Solid;
using DrawingPart = Tekla.Structures.Drawing.Part;
using ModelObject = Tekla.Structures.Model.ModelObject;
using ModelPart = Tekla.Structures.Model.Part;
using TSD = Tekla.Structures.Drawing;
using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    // Slot 04 cho MainForm:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04.Run()
    public class PHU_AutoDimSlot04
    {
        // Auto: nếu MainForm gọi slot 04 cũ thì vẫn chạy thuật toán tự chọn hiện tại.
        public static string Run()
        {
            PHU_Slot04_SelectedProfilePalletDim.Slot04Result result =
                PHU_Slot04_SelectedProfilePalletDim.RunWithResult(
                    PHU_Slot04_SelectedProfilePalletDim.TargetMode.Auto,
                    true,
                    false
                );
            return result.Success ? "OK" : result.ErrorCode;
        }

        // 3 hàm này để MainForm/new button gọi trực tiếp khi người dùng chọn kiểu DIM.
        public static string RunLeft()
        {
            PHU_Slot04_SelectedProfilePalletDim.Slot04Result result =
                PHU_Slot04_SelectedProfilePalletDim.RunWithResult(
                    PHU_Slot04_SelectedProfilePalletDim.TargetMode.Left,
                    true,
                    false
                );
            return result.Success ? "OK" : result.ErrorCode;
        }

        public static string RunCenter()
        {
            PHU_Slot04_SelectedProfilePalletDim.Slot04Result result =
                PHU_Slot04_SelectedProfilePalletDim.RunWithResult(
                    PHU_Slot04_SelectedProfilePalletDim.TargetMode.Center,
                    true,
                    false
                );
            return result.Success ? "OK" : result.ErrorCode;
        }

        public static string RunRight()
        {
            PHU_Slot04_SelectedProfilePalletDim.Slot04Result result =
                PHU_Slot04_SelectedProfilePalletDim.RunWithResult(
                    PHU_Slot04_SelectedProfilePalletDim.TargetMode.Right,
                    true,
                    false
                );
            return result.Success ? "OK" : result.ErrorCode;
        }

        // Entry point dành riêng cho Slot09 Data Center: không đọc selection,
        // luôn chạy Case B tự động với target CENTER và không hiện popup.
        public static PHU_Slot04_SelectedProfilePalletDim.Slot04Result RunDataCenter()
        {
            return PHU_Slot04_SelectedProfilePalletDim.RunWithResult(
                PHU_Slot04_SelectedProfilePalletDim.TargetMode.Center,
                false,
                true
            );
        }

        public static PHU_Slot04_SelectedProfilePalletDim.Slot04Result PreflightDataCenter()
        {
            return PHU_Slot04_SelectedProfilePalletDim.PreflightAutomaticCaseB();
        }
    }

    // Wrapper riêng nếu MainForm muốn gọi dạng class riêng:
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Left.Run()
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Center.Run()
    // Tekla.Technology.Akit.UserScript.PHU_AutoDimSlot04_Right.Run()
    public class PHU_AutoDimSlot04_Left
    {
        public static string Run()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunLeft();
            return "OK";
        }
    }

    public class PHU_AutoDimSlot04_Center
    {
        public static string Run()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunCenter();
            return "OK";
        }
    }

    public class PHU_AutoDimSlot04_Right
    {
        public static string Run()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunRight();
            return "OK";
        }
    }

    public class PHU_Slot04_SelectedProfilePalletDim
    {
        public enum TargetMode
        {
            Auto = 0,
            Left = 1,
            Center = 2,
            Right = 3
        }

        private const double TOL = 1.0;
        private const double ROUND_TOL = 0.08;

        // Lấy theo dump mẫu: dim cụm pallet ở tầng trong, dim main ở tầng ngoài.
        // Quy tắc tầng: mỗi tầng +150.
        private const double DIM_TIER_BASE = 274.0;
        private const double DIM_TIER_STEP = 150.0;

        private const double BOUND_TOL = 20.0;
        private const double AUTO_CASE_B_CONTACT_TOL = 20.0;
        private const double AUTO_CASE_B_LONGITUDINAL_RATIO = 1.50;
        private const double AUTO_CASE_B_MIN_X_OVERLAP = 1.0;

        public sealed class Slot04Result
        {
            public bool Success;
            public int CreatedCount;
            public int TargetCount;
            public bool UsedAutomaticCaseB;
            public int MainPartId;
            public int ViewId;
            public readonly List<int> TargetPartIds = new List<int>();
            public string ErrorCode;
            public string Message;

            public Slot04Result()
            {
                ErrorCode = "S04 ERR";
                Message = string.Empty;
            }
        }

        public static Slot04Result PreflightAutomaticCaseB()
        {
            Slot04Result result = new Slot04Result();
            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
                return FailResult(result, "S04 DRAWING", "DrawingHandler chưa kết nối.", false);

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
                return FailResult(result, "S04 DRAWING", "Không có active drawing.", false);

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
                return FailResult(result, "S04 MODEL", "Model chưa kết nối.", false);

            ModelPart authoritativeMain = PHU_MainPartResolver.Resolve(model, drawing);
            if (authoritativeMain == null)
            {
                return FailResult(
                    result,
                    "S04 MAIN",
                    "Slot04: Không resolve được main part authoritative từ drawing/assembly.",
                    false
                );
            }

            AutoCaseBSelection automatic = FindAutomaticCaseBSelection(
                model,
                drawing,
                authoritativeMain,
                true
            );
            if (automatic == null || automatic.View == null)
            {
                string message =
                    automatic == null
                        ? "Slot04: Không tìm thấy FRONT view hình học hợp lệ."
                        : automatic.Message;
                return FailResult(result, "S04 AUTO", message, false);
            }

            result.Success = true;
            result.CreatedCount = 0;
            result.TargetCount = automatic.Targets.Count;
            result.UsedAutomaticCaseB = true;
            result.MainPartId =
                authoritativeMain.Identifier == null ? 0 : authoritativeMain.Identifier.ID;
            result.ViewId = GetViewIdentifier(automatic.View);
            for (int i = 0; i < automatic.Targets.Count; i++)
            {
                DrawingPart target = automatic.Targets[i];
                if (target != null && target.ModelIdentifier != null)
                    result.TargetPartIds.Add(target.ModelIdentifier.ID);
            }
            result.ErrorCode = string.Empty;
            result.Message =
                automatic.Targets.Count == 0
                    ? "Slot04: FRONT hợp lệ; không có plate quan hệ hình học, bỏ qua DIM plate theo chế độ tùy chọn."
                    : automatic.Message;
            return result;
        }

        public static void RunAuto()
        {
            Run(TargetMode.Auto);
        }

        public static void RunLeft()
        {
            Run(TargetMode.Left);
        }

        public static void RunCenter()
        {
            Run(TargetMode.Center);
        }

        public static void RunRight()
        {
            Run(TargetMode.Right);
        }

        public static void Run(TargetMode targetMode)
        {
            RunWithResult(targetMode, true, false);
        }

        public static Slot04Result RunWithResult(
            TargetMode targetMode,
            bool showMessages,
            bool forceAutomaticCaseB
        )
        {
            Slot04Result result = new Slot04Result();
            TSD.DrawingHandler dh = new TSD.DrawingHandler();
            if (!dh.GetConnectionStatus())
            {
                return FailResult(
                    result,
                    "S04 DRAWING",
                    "DrawingHandler chưa kết nối.",
                    showMessages
                );
            }

            TSD.Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
            {
                return FailResult(result, "S04 DRAWING", "Không có active drawing.", showMessages);
            }

            TSM.Model model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                return FailResult(result, "S04 MODEL", "Model chưa kết nối.", showMessages);
            }

            ModelPart authoritativeMain = PHU_MainPartResolver.Resolve(model, drawing);
            if (authoritativeMain == null)
            {
                return FailResult(
                    result,
                    "S04 MAIN",
                    "Slot04: Không resolve được main part authoritative từ drawing/assembly.",
                    showMessages
                );
            }

            result.MainPartId =
                authoritativeMain.Identifier == null ? 0 : authoritativeMain.Identifier.ID;

            List<DrawingPart> selectedDrawingParts = forceAutomaticCaseB
                ? new List<DrawingPart>()
                : GetSelectedDrawingParts(dh);

            TSD.View explicitView = null;
            List<DrawingPart> explicitTargets = new List<DrawingPart>();
            if (!forceAutomaticCaseB)
            {
                explicitTargets = GetExplicitTargetsInOneView(
                    selectedDrawingParts,
                    authoritativeMain.Identifier,
                    out explicitView
                );
            }

            if (explicitView != null && explicitTargets.Count > 0)
            {
                int explicitTargetCount;
                string explicitError;
                int explicitCreated = CreateSlot04Dims(
                    model,
                    explicitView,
                    authoritativeMain,
                    explicitTargets,
                    targetMode,
                    false,
                    false,
                    false,
                    out explicitTargetCount,
                    out explicitError
                );

                if (explicitCreated > 0)
                {
                    try
                    {
                        drawing.CommitChanges();
                    }
                    catch { }
                    result.Success = true;
                    result.CreatedCount = explicitCreated;
                    result.TargetCount = explicitTargetCount;
                    result.UsedAutomaticCaseB = false;
                    result.ErrorCode = string.Empty;
                    result.Message = "Slot04 OK (explicit).";
                    return result;
                }

                // Selection có part nhưng không tạo thành Case A/C/B hợp lệ:
                // quay về Case B auto thay vì yêu cầu người dùng chọn main.
            }

            AutoCaseBSelection automatic = FindAutomaticCaseBSelection(
                model,
                drawing,
                authoritativeMain,
                forceAutomaticCaseB
            );

            if (automatic == null || automatic.View == null)
            {
                string automaticMessage =
                    automatic == null
                        ? "Slot04: Không tìm thấy FRONT view hình học hợp lệ."
                        : automatic.Message;
                return FailResult(result, "S04 AUTO", automaticMessage, showMessages);
            }

            if (automatic.Targets.Count == 0)
            {
                if (!forceAutomaticCaseB)
                {
                    return FailResult(result, "S04 AUTO", automatic.Message, showMessages);
                }

                result.Success = true;
                result.CreatedCount = 0;
                result.TargetCount = 0;
                result.UsedAutomaticCaseB = true;
                result.ViewId = GetViewIdentifier(automatic.View);
                result.ErrorCode = string.Empty;
                result.Message = "Slot04 Data Center: FRONT không có plate; DIM plate là tùy chọn.";
                return result;
            }

            int targetCount;
            string createError;
            int created = CreateSlot04Dims(
                model,
                automatic.View,
                authoritativeMain,
                automatic.Targets,
                forceAutomaticCaseB ? TargetMode.Center : targetMode,
                true,
                automatic.SwapAxes,
                forceAutomaticCaseB,
                out targetCount,
                out createError
            );

            if (created <= 0)
            {
                string message = string.IsNullOrEmpty(createError)
                    ? "Slot04: Case B auto không tạo được DIM."
                    : createError;
                return FailResult(result, "S04 CREATE", message, showMessages);
            }

            try
            {
                drawing.CommitChanges();
            }
            catch { }

            result.Success = true;
            result.CreatedCount = created;
            result.TargetCount = targetCount;
            result.UsedAutomaticCaseB = true;
            result.ViewId = GetViewIdentifier(automatic.View);
            for (int i = 0; i < automatic.Targets.Count; i++)
            {
                DrawingPart target = automatic.Targets[i];
                if (target != null && target.ModelIdentifier != null)
                    result.TargetPartIds.Add(target.ModelIdentifier.ID);
            }
            result.ErrorCode = string.Empty;
            result.Message = "Slot04 OK (automatic Case B).";
            return result;
        }

        private static int CreateSlot04Dims(
            TSM.Model model,
            TSD.View view,
            ModelPart authoritativeMain,
            List<DrawingPart> targetDrawingParts,
            TargetMode targetMode,
            bool automaticCaseB,
            bool swapAxes,
            bool dataCenterMode,
            out int targetCount,
            out string error
        )
        {
            int count = 0;
            targetCount = 0;
            error = string.Empty;

            TSM.TransformationPlane oldPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();

            try
            {
                // QUAN TRỌNG:
                // Set theo DisplayCoordinateSystem của view trước khi lấy biên dạng.
                // Mọi MinX/MaxX/MinY/MaxY đều là tọa độ thật trong view.
                model
                    .GetWorkPlaneHandler()
                    .SetCurrentTransformationPlane(
                        new TSM.TransformationPlane(view.DisplayCoordinateSystem)
                    );

                PartBox main = automaticCaseB
                    ? BuildGeometryOnlyPartBox(
                        authoritativeMain,
                        FindDrawingPartInView(view, authoritativeMain.Identifier)
                    )
                    : BuildPartBox(
                        authoritativeMain,
                        FindDrawingPartInView(view, authoritativeMain.Identifier)
                    );
                if (main == null)
                {
                    error = "Slot04: Main authoritative không hiển thị trong view đích.";
                    return count;
                }

                List<PartBox> others = automaticCaseB
                    ? BuildGeometryOnlyPartBoxes(model, targetDrawingParts)
                    : BuildSelectedPartBoxes(model, targetDrawingParts);
                RemovePartByIdentifier(others, authoritativeMain.Identifier);
                if (others.Count < 1)
                {
                    error = "Slot04: Không có target hợp lệ trong view đích.";
                    return count;
                }

                // Drawing thực tế có thể xoay longitudinal view 90°. Case B
                // luôn làm việc trong hệ chuẩn: X = trục dọc main,
                // Y = trục pháp tuyến. Chỉ auto path được chuẩn hóa;
                // Case A/C explicit giữ nguyên hình học legacy.
                if (automaticCaseB && swapAxes)
                {
                    SwapPartBoxAxes(main);
                    for (int i = 0; i < others.Count; i++)
                        SwapPartBoxAxes(others[i]);
                }

                List<PlateGroup> groups;
                if (automaticCaseB)
                {
                    groups = BuildAutomaticCaseBGroups(main, others, targetMode, dataCenterMode);
                }
                else
                {
                    List<PartBox> routed = RouteExplicitTargetsByPriority(others);
                    groups = BuildPlateGroups(main, routed, targetMode);
                }

                if (groups.Count == 0)
                {
                    error = "Slot04: Không phân loại được Case A/C/B hợp lệ.";
                    return count;
                }

                targetCount = groups.Count;

                List<PlateGroup> topGroups = new List<PlateGroup>();
                List<PlateGroup> bottomGroups = new List<PlateGroup>();

                for (int i = 0; i < groups.Count; i++)
                {
                    PlateGroup g = groups[i];
                    if (g == null)
                        continue;

                    if (g.IsTop)
                        topGroups.Add(g);
                    else
                        bottomGroups.Add(g);
                }

                topGroups.Sort(CompareGroupByTargetX);
                bottomGroups.Sort(CompareGroupByTargetX);

                TSD.StraightDimensionSetHandler handler = new TSD.StraightDimensionSetHandler();

                List<PlateGroup> topPaletteGroups = FilterGroupsByAngle(topGroups, false);
                List<PlateGroup> bottomPaletteGroups = FilterGroupsByAngle(bottomGroups, false);
                List<PlateGroup> topAngleGroups = FilterGroupsByAngle(topGroups, true);
                List<PlateGroup> bottomAngleGroups = FilterGroupsByAngle(bottomGroups, true);

                // Tầng 1: toàn bộ DIM nội bộ pallet 01 -> target pallet 02 -> pallet 01.
                // Internal chỉ dành cho cụm Palette 01/02, không áp dụng cho thanh L.
                // Theo yêu cầu: tất cả internal dùng chung một tầng.
                count += CreateInternalDimsForGroups(
                    handler,
                    view,
                    topPaletteGroups,
                    true,
                    GetTierDistance(1),
                    swapAxes
                );
                count += CreateInternalDimsForGroups(
                    handler,
                    view,
                    bottomPaletteGroups,
                    false,
                    GetTierDistance(1),
                    swapAxes
                );

                // Tầng 2: chain main riêng cho cụm Palette 01/02.
                // Không gộp chung điểm lưng thanh L vào chain này.
                if (topPaletteGroups.Count > 0)
                    count += CreateMainChainForGroups(
                        handler,
                        view,
                        main,
                        topPaletteGroups,
                        true,
                        GetTierDistance(2),
                        swapAxes,
                        dataCenterMode
                    );

                if (bottomPaletteGroups.Count > 0)
                    count += CreateMainChainForGroups(
                        handler,
                        view,
                        main,
                        bottomPaletteGroups,
                        false,
                        GetTierDistance(2),
                        swapAxes,
                        dataCenterMode
                    );

                // Tầng 3: chain main riêng cho thanh L.
                // Khi quét chọn vừa có Palette 01/02 vừa có L, L luôn tách thành chain riêng và nhảy tầng.
                // Nếu chỉ có L, vẫn dùng tầng 3 để giữ quy tắc ổn định và tránh chồng dim về sau.
                if (topAngleGroups.Count > 0)
                    count += CreateMainChainForGroups(
                        handler,
                        view,
                        main,
                        topAngleGroups,
                        true,
                        GetTierDistance(3),
                        swapAxes,
                        dataCenterMode
                    );

                if (bottomAngleGroups.Count > 0)
                    count += CreateMainChainForGroups(
                        handler,
                        view,
                        main,
                        bottomAngleGroups,
                        false,
                        GetTierDistance(3),
                        swapAxes,
                        dataCenterMode
                    );
            }
            catch (Exception ex)
            {
                error = "Slot04 ERROR: " + ex.Message;
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane);
                }
                catch { }
            }

            return count;
        }

        private static double GetTierDistance(int tier)
        {
            if (tier < 1)
                tier = 1;
            return DIM_TIER_BASE + (tier - 1) * DIM_TIER_STEP;
        }

        private class PartBox
        {
            public DrawingPart DrawingPart;
            public ModelPart ModelPart;
            public Bounds2D Box;
            public bool IsPlate;
            public bool IsAngle;
        }

        private sealed class AutoCaseBSelection
        {
            public TSD.View View;
            public bool SwapAxes;
            public readonly List<DrawingPart> Targets = new List<DrawingPart>();
            public string Message = string.Empty;
        }

        private struct Bounds2D
        {
            public bool Valid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public double Width
            {
                get { return Math.Abs(MaxX - MinX); }
            }
            public double Height
            {
                get { return Math.Abs(MaxY - MinY); }
            }
            public double CenterX
            {
                get { return (MinX + MaxX) / 2.0; }
            }
            public double CenterY
            {
                get { return (MinY + MaxY) / 2.0; }
            }
            public double Area
            {
                get { return Width * Height; }
            }
        }

        private class TargetChoice
        {
            public string Name;
            public double X;
            public int Score;
            public int Priority;
        }

        private class PlateGroup
        {
            public PartBox Plate01;
            public PartBox Plate02;
            public TargetChoice Target;
            public bool IsTop;
            public double TargetY;
            public bool IsOnlyPlate02;
            public bool IsAngleBackDim;
            public Point TargetPoint
            {
                get { return new Point(Target.X, TargetY, 0); }
            }
        }

        private static Slot04Result FailResult(
            Slot04Result result,
            string errorCode,
            string message,
            bool showMessage
        )
        {
            if (result == null)
                result = new Slot04Result();

            result.Success = false;
            result.ErrorCode = string.IsNullOrEmpty(errorCode) ? "S04 ERR" : errorCode;
            result.Message = message ?? string.Empty;

            if (showMessage && !string.IsNullOrEmpty(result.Message))
                Msg(result.Message);

            return result;
        }

        private static PartBox BuildPartBox(ModelPart modelPart, DrawingPart drawingPart)
        {
            if (modelPart == null || IsDummyReferencePart(modelPart))
                return null;

            Bounds2D bounds = GetPartProfileBounds2D(modelPart);
            if (!bounds.Valid)
                return null;

            PartBox box = new PartBox();
            box.DrawingPart = drawingPart;
            box.ModelPart = modelPart;
            box.Box = bounds;
            box.IsPlate = IsPlatePart(modelPart);
            box.IsAngle = IsAnglePart(modelPart);
            return box;
        }

        private static PartBox BuildGeometryOnlyPartBox(
            ModelPart modelPart,
            DrawingPart drawingPart
        )
        {
            // Auto Case B vẫn phân loại target bằng hình học, nhưng dummy là
            // reference object chứ không phải plate cần DIM. Dùng cùng cổng
            // loại trừ NAME/MATERIAL/PART_POS đã ổn định trong Slot03.
            if (modelPart == null || IsDummyReferencePart(modelPart))
                return null;

            Bounds2D bounds = GetPartProfileBounds2D(modelPart);
            if (!bounds.Valid)
                return null;

            PartBox box = new PartBox();
            box.DrawingPart = drawingPart;
            box.ModelPart = modelPart;
            box.Box = bounds;
            // Sau khi loại reference dummy, không dùng NAME/PROFILE/type để
            // phân loại plate thật.
            box.IsPlate = false;
            box.IsAngle = false;
            return box;
        }

        private static List<PartBox> BuildGeometryOnlyPartBoxes(
            TSM.Model model,
            List<DrawingPart> drawingParts
        )
        {
            List<PartBox> result = new List<PartBox>();
            if (model == null || drawingParts == null)
                return result;

            for (int i = 0; i < drawingParts.Count; i++)
            {
                DrawingPart drawingPart = drawingParts[i];
                ModelPart modelPart = SelectModelPart(model, drawingPart);
                PartBox box = BuildGeometryOnlyPartBox(modelPart, drawingPart);
                if (box != null)
                    result.Add(box);
            }

            return result;
        }

        private static DrawingPart FindDrawingPartInView(TSD.View view, Identifier identifier)
        {
            if (view == null || identifier == null)
                return null;

            try
            {
                TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));
                while (parts != null && parts.MoveNext())
                {
                    DrawingPart drawingPart = parts.Current as DrawingPart;
                    if (
                        drawingPart != null
                        && drawingPart.ModelIdentifier != null
                        && SameIdentifier(drawingPart.ModelIdentifier, identifier)
                    )
                        return drawingPart;
                }
            }
            catch { }

            return null;
        }

        private static void RemovePartByIdentifier(List<PartBox> boxes, Identifier identifier)
        {
            if (boxes == null || identifier == null)
                return;

            for (int i = boxes.Count - 1; i >= 0; i--)
            {
                PartBox box = boxes[i];
                if (
                    box != null
                    && box.ModelPart != null
                    && SameIdentifier(box.ModelPart.Identifier, identifier)
                )
                    boxes.RemoveAt(i);
            }
        }

        private static List<DrawingPart> GetExplicitTargetsInOneView(
            List<DrawingPart> selected,
            Identifier mainIdentifier,
            out TSD.View view
        )
        {
            view = null;
            List<DrawingPart> targets = new List<DrawingPart>();
            HashSet<int> seen = new HashSet<int>();

            if (selected == null)
                return targets;

            for (int i = 0; i < selected.Count; i++)
            {
                DrawingPart part = selected[i];
                if (part == null || part.ModelIdentifier == null)
                    continue;

                if (mainIdentifier != null && SameIdentifier(part.ModelIdentifier, mainIdentifier))
                    continue;

                TSD.View partView = null;
                try
                {
                    partView = part.GetView() as TSD.View;
                }
                catch { }
                if (partView == null)
                    continue;

                if (view == null)
                    view = partView;
                else if (!SameView(view, partView))
                {
                    // Selection qua nhiều view là mơ hồ. Không chọn view theo
                    // hình học; caller sẽ chuyển sang detector Case B authoritative.
                    view = null;
                    targets.Clear();
                    return targets;
                }

                int id = part.ModelIdentifier.ID;
                if (seen.Add(id))
                    targets.Add(part);
            }

            return targets;
        }

        private static bool SameView(TSD.View first, TSD.View second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null)
                return false;

            try
            {
                int firstId = GetViewIdentifier(first);
                int secondId = GetViewIdentifier(second);
                if (firstId != int.MaxValue && secondId != int.MaxValue)
                    return firstId == secondId;

                return string.Equals(
                    GetViewName(first),
                    GetViewName(second),
                    StringComparison.Ordinal
                );
            }
            catch
            {
                return false;
            }
        }

        private static List<PartBox> RouteExplicitTargetsByPriority(List<PartBox> candidates)
        {
            List<PartBox> verticalPlates = new List<PartBox>();
            List<PartBox> horizontalPlates = new List<PartBox>();
            List<PartBox> angles = new List<PartBox>();

            if (candidates == null)
                return verticalPlates;

            for (int i = 0; i < candidates.Count; i++)
            {
                PartBox part = candidates[i];
                if (part == null)
                    continue;

                if (part.IsAngle)
                    angles.Add(part);
                else if (part.IsPlate && part.Box.Height + TOL >= part.Box.Width)
                    verticalPlates.Add(part);
                else if (part.IsPlate)
                    horizontalPlates.Add(part);
            }

            // Case A explicit có ưu tiên cao nhất.
            if (verticalPlates.Count > 0 && horizontalPlates.Count > 0)
            {
                List<PartBox> caseA = new List<PartBox>();
                caseA.AddRange(verticalPlates);
                caseA.AddRange(horizontalPlates);
                return caseA;
            }

            // Case C explicit giữ nguyên thuật toán lưng L.
            if (angles.Count > 0)
                return angles;

            // Case B explicit: chỉ Plate02 dựng đứng.
            return verticalPlates;
        }

        private static List<PlateGroup> BuildAutomaticCaseBGroups(
            PartBox main,
            List<PartBox> candidates,
            TargetMode targetMode,
            bool dataCenterMode
        )
        {
            List<PlateGroup> groups = new List<PlateGroup>();
            if (main == null || candidates == null)
                return groups;

            for (int i = 0; i < candidates.Count; i++)
            {
                PartBox plate02 = candidates[i];
                // Sau cổng loại dummy, Auto Case B được gate bằng quan hệ
                // hình học trên FRONT; không dùng NAME/PROFILE/assembly
                // membership hay IsPlate/IsAngle để nhận plate thật.
                if (plate02 == null)
                    continue;

                // Slot09 quy định mọi DIM plate nằm ngang trên FRONT phải dùng
                // chung một tầng phía trên. Slot04 legacy ngoài Data Center vẫn
                // giữ cách chia Top/Bottom hiện hữu.
                bool isTop = dataCenterMode || plate02.Box.CenterY >= main.Box.CenterY;
                PlateGroup group = new PlateGroup();
                group.Plate01 = null;
                group.Plate02 = plate02;
                group.IsTop = isTop;
                group.TargetY = isTop ? plate02.Box.MaxY : plate02.Box.MinY;
                group.IsOnlyPlate02 = true;
                group.IsAngleBackDim = false;
                group.Target = ChooseTargetWithoutPlate01(main.Box, plate02.Box, targetMode);
                groups.Add(group);
            }

            groups.Sort(CompareGroupByTargetX);
            return groups;
        }

        private static AutoCaseBSelection FindAutomaticCaseBSelection(
            TSM.Model model,
            TSD.Drawing drawing,
            ModelPart authoritativeMain,
            bool dataCenterMode
        )
        {
            AutoCaseBSelection failure = new AutoCaseBSelection();
            failure.Message = "Slot04: Không tìm thấy FRONT view chứa main authoritative.";

            if (model == null || drawing == null || authoritativeMain == null)
                return failure;

            List<AutoCaseBSelection> validSelections = new List<AutoCaseBSelection>();
            TSM.TransformationPlane originalPlane = model
                .GetWorkPlaneHandler()
                .GetCurrentTransformationPlane();

            try
            {
                TSD.ContainerView sheet = drawing.GetSheet();
                TSD.DrawingObjectEnumerator views = sheet == null ? null : sheet.GetAllViews();

                while (views != null && views.MoveNext())
                {
                    TSD.View view = views.Current as TSD.View;
                    if (view == null || !global::PHU_OpenGridView.IsFrontView(view))
                        continue;

                    DrawingPart mainDrawingPart = FindDrawingPartInView(
                        view,
                        authoritativeMain.Identifier
                    );
                    if (mainDrawingPart == null)
                        continue;

                    try
                    {
                        model
                            .GetWorkPlaneHandler()
                            .SetCurrentTransformationPlane(
                                new TSM.TransformationPlane(view.DisplayCoordinateSystem)
                            );

                        PartBox main = BuildGeometryOnlyPartBox(authoritativeMain, mainDrawingPart);
                        if (main == null)
                            continue;

                        double mainLong = Math.Max(main.Box.Width, main.Box.Height);
                        double mainShort = Math.Min(main.Box.Width, main.Box.Height);
                        if (
                            mainShort <= TOL
                            || mainLong < mainShort * AUTO_CASE_B_LONGITUDINAL_RATIO
                        )
                            continue;

                        bool swapAxes = main.Box.Height > main.Box.Width;
                        if (swapAxes)
                            SwapPartBoxAxes(main);

                        AutoCaseBSelection selection = new AutoCaseBSelection();
                        selection.View = view;
                        selection.SwapAxes = swapAxes;
                        TSD.DrawingObjectEnumerator parts = view.GetAllObjects(typeof(DrawingPart));

                        while (parts != null && parts.MoveNext())
                        {
                            DrawingPart drawingPart = parts.Current as DrawingPart;
                            if (
                                drawingPart == null
                                || drawingPart.ModelIdentifier == null
                                || SameIdentifier(
                                    drawingPart.ModelIdentifier,
                                    authoritativeMain.Identifier
                                )
                            )
                                continue;

                            ModelPart modelPart = SelectModelPart(model, drawingPart);
                            PartBox candidate = BuildGeometryOnlyPartBox(modelPart, drawingPart);
                            if (swapAxes)
                                SwapPartBoxAxes(candidate);
                            if (IsStrictAutomaticPlate02(main, candidate, dataCenterMode))
                                selection.Targets.Add(drawingPart);
                        }

                        SortDrawingPartsByIdentifier(selection.Targets);
                        // FRONT vẫn là một lựa chọn hợp lệ khi không có plate.
                        // Plate trong Slot09 là tùy chọn; khi có, lấy toàn bộ
                        // Drawing.Part thỏa quan hệ hình học, không giới hạn số
                        // lượng và không phụ thuộc NAME/PROFILE/class/assembly.
                        validSelections.Add(selection);
                    }
                    catch { }
                }
            }
            finally
            {
                try
                {
                    model.GetWorkPlaneHandler().SetCurrentTransformationPlane(originalPlane);
                }
                catch { }
            }

            if (validSelections.Count == 0)
                return failure;

            validSelections.Sort(
                delegate(AutoCaseBSelection a, AutoCaseBSelection b)
                {
                    int countCompare = b.Targets.Count.CompareTo(a.Targets.Count);
                    if (countCompare != 0)
                        return countCompare;
                    return GetViewIdentifier(a.View).CompareTo(GetViewIdentifier(b.View));
                }
            );

            int bestCount = validSelections[0].Targets.Count;
            if (validSelections.Count > 1 && validSelections[1].Targets.Count == bestCount)
            {
                failure.Message =
                    "Slot04: Mơ hồ; có nhiều FRONT view cùng "
                    + bestCount
                    + " target hình học hợp lệ.";
                return failure;
            }

            validSelections[0].Message =
                bestCount == 0
                    ? "Slot04: FRONT hợp lệ; không có plate quan hệ hình học."
                    : "Slot04: Đã detect " + bestCount + " plate trên FRONT bằng quan hệ hình học.";
            return validSelections[0];
        }

        private static bool IsStrictAutomaticPlate02(
            PartBox main,
            PartBox candidate,
            bool dataCenterMode
        )
        {
            // Sau khi reference dummy đã bị loại, detector không dùng
            // NAME/PROFILE/class để nhận plate thật. ModelIdentifier chỉ dùng
            // loại main; target được quyết định bằng hình học hệ trục main.
            if (
                main == null
                || candidate == null
                || candidate.Box.Height + TOL < candidate.Box.Width
            )
                return false;

            double overlapX =
                Math.Min(main.Box.MaxX, candidate.Box.MaxX)
                - Math.Max(main.Box.MinX, candidate.Box.MinX);
            if (overlapX < AUTO_CASE_B_MIN_X_OVERLAP)
                return false;

            if (dataCenterMode)
            {
                // Slot09 chỉ DIM các tấm đứng trên mặt trên của dầm chính.
                // Tấm bịt đầu nằm trọn trong chiều cao dầm từng bị nhận nhầm;
                // tâm bề dày 14.4 của chúng tạo chuỗi 7.2 ở hai đầu.
                // Điều kiện này thuần hình học và chỉ áp dụng cho Data Center,
                // nên Slot04 độc lập vẫn giữ nguyên detector cũ.
                bool protrudesAboveMain = candidate.Box.MaxY > main.Box.MaxY + TOL;
                bool reachesMainTop = candidate.Box.MinY <= main.Box.MaxY + AUTO_CASE_B_CONTACT_TOL;
                if (!protrudesAboveMain || !reachesMainTop)
                    return false;

                // Type-2 owns the plate at the transverse-member station as
                // a local Plate-edge -> Beam-REF relation.  Keeping it out of
                // Slot04 prevents that local plate from being inserted into
                // the independent global top-plate chain.  The scope is
                // geometry-proven and inactive for every Type-1 drawing.
                if (
                    PHU_Slot07_DataCenterBeamType2Context
                        .ShouldExcludeFrontSlot04Target(
                            candidate.Box.MinX,
                            candidate.Box.MaxX
                        )
                )
                    return false;
            }

            // Interval gap = 0 khi Plate02 giao/bao trùm bề dày main.
            // Phép đo mép-đến-mép cũ loại sai tấm đang cắt qua main.
            double normalGap = 0.0;
            if (candidate.Box.MaxY < main.Box.MinY)
                normalGap = main.Box.MinY - candidate.Box.MaxY;
            else if (candidate.Box.MinY > main.Box.MaxY)
                normalGap = candidate.Box.MinY - main.Box.MaxY;

            return normalGap <= AUTO_CASE_B_CONTACT_TOL;
        }

        private static void SwapPartBoxAxes(PartBox part)
        {
            if (part == null || !part.Box.Valid)
                return;

            Bounds2D source = part.Box;
            Bounds2D swapped = new Bounds2D();
            swapped.Valid = source.Valid;
            swapped.MinX = source.MinY;
            swapped.MaxX = source.MaxY;
            swapped.MinY = source.MinX;
            swapped.MaxY = source.MaxX;
            part.Box = swapped;
        }

        private static void SortDrawingPartsByIdentifier(List<DrawingPart> parts)
        {
            if (parts == null)
                return;

            parts.Sort(
                delegate(DrawingPart a, DrawingPart b)
                {
                    int first =
                        a == null || a.ModelIdentifier == null
                            ? int.MaxValue
                            : a.ModelIdentifier.ID;
                    int second =
                        b == null || b.ModelIdentifier == null
                            ? int.MaxValue
                            : b.ModelIdentifier.ID;
                    return first.CompareTo(second);
                }
            );
        }

        private static int GetViewIdentifier(TSD.View view)
        {
            try
            {
                if (view == null)
                    return int.MaxValue;

                PropertyInfo identifierProperty = view.GetType()
                    .GetProperty(
                        "Identifier",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                object identifier =
                    identifierProperty == null ? null : identifierProperty.GetValue(view, null);
                PropertyInfo idProperty =
                    identifier == null ? null : identifier.GetType().GetProperty("ID");
                object id = idProperty == null ? null : idProperty.GetValue(identifier, null);
                if (id != null)
                    return Convert.ToInt32(id);
            }
            catch { }

            return int.MaxValue;
        }

        private static string GetViewName(TSD.View view)
        {
            try
            {
                if (view == null)
                    return string.Empty;

                PropertyInfo nameProperty = view.GetType().GetProperty("Name");
                object name = nameProperty == null ? null : nameProperty.GetValue(view, null);
                if (name != null && !string.IsNullOrEmpty(name.ToString()))
                    return name.ToString();

                return "VIEW_REF:"
                    + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(view).ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void PickPallet01And02(
            PartBox main,
            List<PartBox> candidates,
            out PartBox plate01,
            out PartBox plate02
        )
        {
            plate01 = null;
            plate02 = null;

            if (candidates == null || candidates.Count < 2)
                return;

            // Pallet 02 là tấm dựng đứng: hình chiếu theo view hẹp theo X và cao theo Y.
            // Pallet 01 là tấm nằm ngang/dưới: rộng theo X hơn pallet 02.
            List<PartBox> plates = new List<PartBox>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].IsPlate)
                    plates.Add(candidates[i]);
            }

            if (plates.Count < 2)
                plates = candidates;

            PartBox best02 = null;
            double best02Score = -999999999.0;

            for (int i = 0; i < plates.Count; i++)
            {
                PartBox p = plates[i];
                double slender = p.Box.Height - p.Box.Width;
                double nearMainCenter = -Math.Abs(p.Box.CenterX - main.Box.CenterX) * 0.001;
                double score = slender + nearMainCenter;

                if (score > best02Score)
                {
                    best02Score = score;
                    best02 = p;
                }
            }

            plate02 = best02;

            PartBox best01 = null;
            double best01Score = -999999999.0;

            for (int i = 0; i < plates.Count; i++)
            {
                PartBox p = plates[i];
                if (
                    plate02 != null
                    && SameIdentifier(p.ModelPart.Identifier, plate02.ModelPart.Identifier)
                )
                    continue;

                // Pallet01 ưu tiên tấm có Width lớn nhất và nằm giữa main với plate02.
                double score = p.Box.Width;
                if (plate02 != null)
                    score -= Math.Abs(p.Box.CenterX - plate02.Box.CenterX) * 0.001;

                if (score > best01Score)
                {
                    best01Score = score;
                    best01 = p;
                }
            }

            plate01 = best01;
        }

        private static TargetChoice ChooseTarget(
            Bounds2D main,
            Bounds2D plate01,
            Bounds2D plate02,
            TargetMode targetMode
        )
        {
            if (targetMode == TargetMode.Left)
                return new TargetChoice()
                {
                    Name = "LEFT",
                    X = plate02.MinX,
                    Priority = 1,
                    Score = 0
                };

            if (targetMode == TargetMode.Center)
                return new TargetChoice()
                {
                    Name = "CENTER",
                    X = plate02.CenterX,
                    Priority = 0,
                    Score = 0
                };

            if (targetMode == TargetMode.Right)
                return new TargetChoice()
                {
                    Name = "RIGHT",
                    X = plate02.MaxX,
                    Priority = 2,
                    Score = 0
                };

            // Không chọn gì thì dùng thuật toán tự động hiện tại.
            return ChooseBestTarget(main, plate01, plate02);
        }

        private static TargetChoice ChooseTargetWithoutPlate01(
            Bounds2D main,
            Bounds2D plate02,
            TargetMode targetMode
        )
        {
            if (targetMode == TargetMode.Left)
                return new TargetChoice()
                {
                    Name = "LEFT",
                    X = plate02.MinX,
                    Priority = 1,
                    Score = 0
                };

            if (targetMode == TargetMode.Center)
                return new TargetChoice()
                {
                    Name = "CENTER",
                    X = plate02.CenterX,
                    Priority = 0,
                    Score = 0
                };

            if (targetMode == TargetMode.Right)
                return new TargetChoice()
                {
                    Name = "RIGHT",
                    X = plate02.MaxX,
                    Priority = 2,
                    Score = 0
                };

            List<TargetChoice> candidates = new List<TargetChoice>();
            candidates.Add(
                new TargetChoice()
                {
                    Name = "LEFT",
                    X = plate02.MinX,
                    Priority = 1
                }
            );
            candidates.Add(
                new TargetChoice()
                {
                    Name = "CENTER",
                    X = plate02.CenterX,
                    Priority = 0
                }
            );
            candidates.Add(
                new TargetChoice()
                {
                    Name = "RIGHT",
                    X = plate02.MaxX,
                    Priority = 2
                }
            );

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetChoice c = candidates[i];
                c.Score = ScoreTargetWithoutPlate01(main, c.X);
            }

            candidates.Sort(
                delegate(TargetChoice a, TargetChoice b)
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0)
                        return c;
                    return a.X.CompareTo(b.X);
                }
            );

            return candidates[0];
        }

        private static TargetChoice ChooseAngleBackTarget(
            PartBox anglePart,
            bool isTop,
            out double targetY
        )
        {
            targetY = isTop ? anglePart.Box.MaxY : anglePart.Box.MinY;

            double backX;
            double backMinY;
            double backMaxY;

            // Thanh L bắt buộc DIM vào LƯNG: cạnh ĐỨNG dài nhất của biên dạng L.
            // Không dùng cạnh ngoài bbox nữa vì dễ bắt nhầm vào chân/cạnh ngang của chữ L.
            if (
                TryGetAngleLongestVerticalBackEdge(
                    anglePart.ModelPart,
                    out backX,
                    out backMinY,
                    out backMaxY
                )
            )
            {
                targetY = isTop ? backMaxY : backMinY;
                return new TargetChoice()
                {
                    Name = "ANGLE_BACK",
                    X = backX,
                    Priority = 0,
                    Score = 0
                };
            }

            // Fallback rất hiếm: nếu không đọc được loop solid thì mới quay về cạnh đứng bbox ngoài.
            double x = anglePart.Box.MaxX;
            return new TargetChoice()
            {
                Name = "ANGLE_BACK_FALLBACK",
                X = x,
                Priority = 0,
                Score = 0
            };
        }

        private static int ScoreTargetWithoutPlate01(Bounds2D main, double targetX)
        {
            int score = 0;
            score += ScoreDistance(targetX - main.MinX);
            score += ScoreDistance(main.MaxX - targetX);
            return score;
        }

        private static TargetChoice ChooseBestTarget(
            Bounds2D main,
            Bounds2D plate01,
            Bounds2D plate02
        )
        {
            List<TargetChoice> candidates = new List<TargetChoice>();
            candidates.Add(
                new TargetChoice()
                {
                    Name = "LEFT",
                    X = plate02.MinX,
                    Priority = 1
                }
            );
            candidates.Add(
                new TargetChoice()
                {
                    Name = "CENTER",
                    X = plate02.CenterX,
                    Priority = 0
                }
            );
            candidates.Add(
                new TargetChoice()
                {
                    Name = "RIGHT",
                    X = plate02.MaxX,
                    Priority = 2
                }
            );

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetChoice c = candidates[i];
                c.Score = ScoreTarget(main, plate01, c.X);
            }

            candidates.Sort(
                delegate(TargetChoice a, TargetChoice b)
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0)
                        return c;
                    return a.X.CompareTo(b.X);
                }
            );

            return candidates[0];
        }

        private static int ScoreTarget(Bounds2D main, Bounds2D plate01, double targetX)
        {
            int score = 0;

            // CÙNG 1 target dùng cho cả main và pallet01.
            // Chấm điểm trên 4 đoạn dim thật sẽ sinh ra.
            score += ScoreDistance(targetX - main.MinX);
            score += ScoreDistance(main.MaxX - targetX);
            score += ScoreDistance(targetX - plate01.MinX);
            score += ScoreDistance(plate01.MaxX - targetX);

            return score;
        }

        private static bool IsIntegerOrHalfDistance(double raw)
        {
            double v = Math.Abs(raw);

            double nearestInteger = Math.Round(v);
            if (Math.Abs(v - nearestInteger) <= ROUND_TOL)
                return true;

            double nearestHalf = Math.Round(v * 2.0) / 2.0;
            if (Math.Abs(v - nearestHalf) <= ROUND_TOL)
                return true;

            return false;
        }

        private static int ScoreDistance(double raw)
        {
            return IsIntegerOrHalfDistance(raw) ? 1 : 0;
        }

        private static List<PartBox> BuildSelectedPartBoxes(
            TSM.Model model,
            List<DrawingPart> selectedDrawingParts
        )
        {
            List<PartBox> boxes = new List<PartBox>();

            if (model == null || selectedDrawingParts == null)
                return boxes;

            for (int i = 0; i < selectedDrawingParts.Count; i++)
            {
                DrawingPart dp = selectedDrawingParts[i];
                ModelPart mp = SelectModelPart(model, dp);
                if (mp == null)
                    continue;

                if (IsDummyReferencePart(mp))
                    continue;

                Bounds2D b = GetPartProfileBounds2D(mp);
                if (!b.Valid)
                    continue;

                PartBox pb = new PartBox();
                pb.DrawingPart = dp;
                pb.ModelPart = mp;
                pb.Box = b;
                pb.IsPlate = IsPlatePart(mp);
                pb.IsAngle = IsAnglePart(mp);
                boxes.Add(pb);
            }

            return boxes;
        }

        private static List<PlateGroup> BuildPlateGroups(
            PartBox main,
            List<PartBox> candidates,
            TargetMode targetMode
        )
        {
            List<PlateGroup> groups = new List<PlateGroup>();

            if (main == null || candidates == null || candidates.Count < 1)
                return groups;

            List<PartBox> work = new List<PartBox>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == null)
                    continue;

                // Slot04 mới: ngoài PLATE, thanh L cũng được xem là đối tượng dim.
                if (candidates[i].IsPlate || candidates[i].IsAngle)
                    work.Add(candidates[i]);
            }

            if (work.Count == 0)
                work = candidates;

            List<PartBox> plate02List = PickPlate02Candidates(work);
            if (plate02List.Count == 0)
                return groups;

            List<PartBox> usedPlate01 = new List<PartBox>();

            for (int i = 0; i < plate02List.Count; i++)
            {
                PartBox plate02 = plate02List[i];
                if (plate02 == null)
                    continue;

                bool isTop = plate02.Box.CenterY >= main.Box.CenterY;
                double targetY = isTop ? plate02.Box.MaxY : plate02.Box.MinY;

                PlateGroup g = new PlateGroup();
                g.Plate02 = plate02;
                g.IsTop = isTop;
                g.TargetY = targetY;

                if (plate02.IsAngle)
                {
                    // Case C: thanh chữ L không có pallet 01.
                    // Bắt buộc dim vào LƯNG thanh L = cạnh ĐỨNG dài nhất.
                    // Điểm DIM là đầu trên/dưới của chính cạnh lưng đó, không phải cạnh chân L.
                    g.Plate01 = null;
                    g.IsOnlyPlate02 = true;
                    g.IsAngleBackDim = true;
                    g.Target = ChooseAngleBackTarget(plate02, isTop, out targetY);
                    g.TargetY = targetY;
                    groups.Add(g);
                    continue;
                }

                PartBox plate01 = FindNearestPlate01ForPlate02(plate02, work, usedPlate01);

                if (plate01 != null)
                {
                    // Case A: có đủ pallet 01 + pallet 02.
                    AddUsedPart(usedPlate01, plate01);
                    g.Plate01 = plate01;
                    g.IsOnlyPlate02 = false;
                    g.IsAngleBackDim = false;
                    g.Target = ChooseTarget(main.Box, plate01.Box, plate02.Box, targetMode);
                }
                else
                {
                    // Case B: chỉ có main + plate đứng 02, không tạo internal.
                    g.Plate01 = null;
                    g.IsOnlyPlate02 = true;
                    g.IsAngleBackDim = false;
                    g.Target = ChooseTargetWithoutPlate01(main.Box, plate02.Box, targetMode);
                }

                groups.Add(g);
            }

            ApplyGlobalTargetModeForNormalPlate02Groups(main, groups, targetMode);

            return groups;
        }

        private static void ApplyGlobalTargetModeForNormalPlate02Groups(
            PartBox main,
            List<PlateGroup> groups,
            TargetMode targetMode
        )
        {
            if (main == null || groups == null || groups.Count == 0)
                return;

            // Angle L luôn dùng lưng, không tham gia Left/Center/Right.
            // Auto đã chọn riêng LEFT/CENTER/RIGHT cho từng cụm theo số lượng trị DIM .0/.5.
            // Chỉ chế độ Manual mới khóa cùng một kiểu cho tất cả cụm.
            if (targetMode == TargetMode.Auto)
                return;

            TargetMode lockedMode = targetMode;

            for (int i = 0; i < groups.Count; i++)
            {
                PlateGroup g = groups[i];
                if (g == null || g.Plate02 == null || g.Plate02.IsAngle)
                    continue;

                if (g.Plate01 != null && !g.IsOnlyPlate02)
                    g.Target = ChooseTarget(main.Box, g.Plate01.Box, g.Plate02.Box, lockedMode);
                else
                    g.Target = ChooseTargetWithoutPlate01(main.Box, g.Plate02.Box, lockedMode);
            }
        }

        private static List<PartBox> PickPlate02Candidates(List<PartBox> plates)
        {
            List<PartBox> result = new List<PartBox>();

            if (plates == null)
                return result;

            // Pallet 02 là tấm đứng; thanh L cũng là target object riêng.
            for (int i = 0; i < plates.Count; i++)
            {
                PartBox p = plates[i];
                if (p == null)
                    continue;

                if (p.IsAngle)
                {
                    result.Add(p);
                    continue;
                }

                if (p.Box.Height + TOL >= p.Box.Width)
                    result.Add(p);
            }

            // Fallback: nếu chỉ chọn main + 1 plate đứng mà hình chiếu không thỏa Height>=Width, vẫn dim plate đó.
            if (result.Count == 0 && plates.Count == 1)
                result.Add(plates[0]);

            // Fallback: nếu bản vẽ xoay/scale làm không phân biệt được, lấy nửa số tấm có Height-Width lớn nhất.
            if (result.Count == 0 && plates.Count >= 2)
            {
                List<PartBox> sorted = new List<PartBox>(plates);
                sorted.Sort(
                    delegate(PartBox a, PartBox b)
                    {
                        double sa = a.Box.Height - a.Box.Width;
                        double sb = b.Box.Height - b.Box.Width;
                        return sb.CompareTo(sa);
                    }
                );

                int take = sorted.Count / 2;
                if (take < 1)
                    take = 1;
                for (int i = 0; i < take && i < sorted.Count; i++)
                    result.Add(sorted[i]);
            }

            return result;
        }

        private static PartBox FindNearestPlate01ForPlate02(
            PartBox plate02,
            List<PartBox> plates,
            List<PartBox> usedPlate01
        )
        {
            PartBox best = null;
            double bestScore = 999999999.0;

            if (plate02 == null || plates == null)
                return null;

            for (int i = 0; i < plates.Count; i++)
            {
                PartBox p = plates[i];
                if (p == null)
                    continue;

                if (SameIdentifier(p.ModelPart.Identifier, plate02.ModelPart.Identifier))
                    continue;

                if (p.IsAngle)
                    continue;

                if (ContainsSamePart(usedPlate01, p))
                    continue;

                // Pallet 01 phải là tấm/biên dạng nằm ngang rõ ràng, rộng hơn plate02 đáng kể.
                // Nếu không có miếng 01 thì không ép ghép bừa các plate đứng với nhau.
                if (
                    p.Box.Width < plate02.Box.Width * 1.20
                    && p.Box.Width < plate02.Box.Width + 20.0
                )
                    continue;

                double centerDistance = Distance2D(
                    new Point(p.Box.CenterX, p.Box.CenterY, 0),
                    new Point(plate02.Box.CenterX, plate02.Box.CenterY, 0)
                );

                double widthPenalty = p.Box.Width >= plate02.Box.Width ? 0.0 : 10000.0;
                double verticalPenalty = Math.Abs(p.Box.CenterY - plate02.Box.CenterY) * 0.25;
                double score = centerDistance + widthPenalty + verticalPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            // Không fallback reuse: nếu không tìm được pallet 01 đủ tin cậy thì coi là case chỉ có plate02/thanh L.
            return best;
        }

        private static int CreateInternalDimsForGroups(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            List<PlateGroup> groups,
            bool isTop,
            double distance,
            bool swapAxes
        )
        {
            int count = 0;

            if (groups == null || groups.Count == 0)
                return count;

            Vector direction = isTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0);

            for (int i = 0; i < groups.Count; i++)
            {
                PlateGroup g = groups[i];
                if (g == null || g.Plate02 == null || g.Target == null)
                    continue;

                if (g.IsOnlyPlate02 || g.Plate01 == null)
                    continue;

                double plate01EdgeY = isTop ? g.Plate01.Box.MaxY : g.Plate01.Box.MinY;

                Point plate01Left = new Point(g.Plate01.Box.MinX, plate01EdgeY, 0);
                Point plate01Target = new Point(g.Target.X, g.TargetY, 0);
                Point plate01Right = new Point(g.Plate01.Box.MaxX, plate01EdgeY, 0);

                if (
                    CreateDimChain(
                        handler,
                        view,
                        new Point[] { plate01Left, plate01Target, plate01Right },
                        direction,
                        distance,
                        swapAxes
                    )
                )
                {
                    count++;
                }
            }

            return count;
        }

        private static int CreateMainChainForGroups(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            PartBox main,
            List<PlateGroup> groups,
            bool isTop,
            double distance,
            bool swapAxes,
            bool dataCenterMode
        )
        {
            if (
                handler == null
                || view == null
                || main == null
                || groups == null
                || groups.Count == 0
            )
                return 0;

            Vector direction = isTop ? new Vector(0, 1, 0) : new Vector(0, -1, 0);
            double mainEdgeY = isTop ? main.Box.MaxY : main.Box.MinY;

            List<Point> chain = new List<Point>();
            chain.Add(new Point(main.Box.MinX, mainEdgeY, 0));

            for (int i = 0; i < groups.Count; i++)
            {
                PlateGroup g = groups[i];
                if (g == null || g.Target == null)
                    continue;

                AddUniquePoint2D(chain, new Point(g.Target.X, g.TargetY, 0), 0.5);
            }

            chain.Sort(ComparePointByXThenY);

            // Bảo đảm 2 mép ngoài main luôn nằm ở đầu/cuối chain.
            chain.Insert(0, new Point(main.Box.MinX, mainEdgeY, 0));
            chain.Add(new Point(main.Box.MaxX, mainEdgeY, 0));

            Vector actualDirectionOverride = null;
            if (dataCenterMode)
            {
                Point first = chain[0];
                Point actualFirst = swapAxes
                    ? new Point(first.Y, first.X, first.Z)
                    : new Point(first.X, first.Y, first.Z);
                if (
                    !PHU_Slot07_DataCenterContext.TryResolveFrontSlot04Placement(
                        view,
                        actualFirst,
                        out actualDirectionOverride,
                        out distance
                    )
                )
                {
                    // Không fallback về khoảng cách Slot04 cố định: Data Center
                    // phải giữ đúng tầng chung đã handoff từ Shape/Grid.
                    return 0;
                }
            }

            return CreateDimChain(
                handler,
                view,
                chain.ToArray(),
                direction,
                distance,
                swapAxes,
                actualDirectionOverride
            )
                ? 1
                : 0;
        }

        private static int CompareGroupByTargetX(PlateGroup a, PlateGroup b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;
            if (a.Target == null && b.Target == null)
                return 0;
            if (a.Target == null)
                return -1;
            if (b.Target == null)
                return 1;
            return a.Target.X.CompareTo(b.Target.X);
        }

        private static List<PlateGroup> FilterGroupsByAngle(List<PlateGroup> groups, bool wantAngle)
        {
            List<PlateGroup> result = new List<PlateGroup>();

            if (groups == null)
                return result;

            for (int i = 0; i < groups.Count; i++)
            {
                PlateGroup g = groups[i];
                if (g == null)
                    continue;

                bool isAngleGroup = g.IsAngleBackDim || (g.Plate02 != null && g.Plate02.IsAngle);

                if (isAngleGroup == wantAngle)
                    result.Add(g);
            }

            result.Sort(CompareGroupByTargetX);
            return result;
        }

        private static int ComparePointByXThenY(Point a, Point b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            int c = a.X.CompareTo(b.X);
            if (c != 0)
                return c;
            return a.Y.CompareTo(b.Y);
        }

        private static bool ContainsSamePart(List<PartBox> list, PartBox part)
        {
            if (list == null || part == null || part.ModelPart == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (
                    list[i] != null
                    && list[i].ModelPart != null
                    && SameIdentifier(list[i].ModelPart.Identifier, part.ModelPart.Identifier)
                )
                    return true;
            }

            return false;
        }

        private static void AddUsedPart(List<PartBox> list, PartBox part)
        {
            if (list == null || part == null)
                return;

            if (!ContainsSamePart(list, part))
                list.Add(part);
        }

        private static Bounds2D GetPartProfileBounds2D(ModelPart part)
        {
            Bounds2D b = new Bounds2D();
            b.Valid = false;

            List<Point> pts = GetSolidProfilePoints(part);

            if (pts.Count == 0)
            {
                try
                {
                    Solid s = part.GetSolid();
                    pts.Add(s.MinimumPoint);
                    pts.Add(s.MaximumPoint);
                }
                catch { }
            }

            if (pts.Count == 0)
                return b;

            b.MinX = 999999999.0;
            b.MaxX = -999999999.0;
            b.MinY = 999999999.0;
            b.MaxY = -999999999.0;

            for (int i = 0; i < pts.Count; i++)
            {
                Point p = pts[i];
                if (p == null)
                    continue;

                if (p.X < b.MinX)
                    b.MinX = p.X;
                if (p.X > b.MaxX)
                    b.MaxX = p.X;
                if (p.Y < b.MinY)
                    b.MinY = p.Y;
                if (p.Y > b.MaxY)
                    b.MaxY = p.Y;
            }

            b.Valid = Math.Abs(b.MaxX - b.MinX) > TOL && Math.Abs(b.MaxY - b.MinY) > TOL;
            return b;
        }

        private static List<Point> GetSolidProfilePoints(ModelPart part)
        {
            List<Point> result = new List<Point>();

            try
            {
                Solid solid = part.GetSolid();
                if (solid == null)
                    return result;

                // Ưu tiên đọc toàn bộ vertex của solid để lấy đúng biên dạng trong view.
                // Nếu môi trường Tekla khác version không expose Face/Loop/Vertex như mong đợi,
                // catch bên dưới sẽ fallback về MinimumPoint/MaximumPoint.
                FaceEnumerator faceEnum = solid.GetFaceEnumerator();
                while (faceEnum.MoveNext())
                {
                    Face face = faceEnum.Current as Face;
                    if (face == null)
                        continue;

                    LoopEnumerator loopEnum = face.GetLoopEnumerator();
                    while (loopEnum.MoveNext())
                    {
                        Loop loop = loopEnum.Current as Loop;
                        if (loop == null)
                            continue;

                        VertexEnumerator vertexEnum = loop.GetVertexEnumerator();
                        while (vertexEnum.MoveNext())
                        {
                            Point p = vertexEnum.Current as Point;
                            if (p != null)
                                AddUniquePoint2D(result, new Point(p.X, p.Y, 0), 0.5);
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance,
            bool swapAxes,
            Vector actualDirectionOverride = null
        )
        {
            if (handler == null || view == null || points == null || points.Length < 2)
                return false;

            PointList list = new PointList();

            for (int i = 0; i < points.Length; i++)
            {
                Point p = points[i];
                if (p == null)
                    continue;

                Point actualPoint = swapAxes ? new Point(p.Y, p.X, p.Z) : new Point(p.X, p.Y, p.Z);

                bool duplicate = false;
                foreach (Point old in list)
                {
                    if (Distance2D(old, actualPoint) <= 0.5)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    list.Add(new Point(actualPoint.X, actualPoint.Y, 0));
            }

            if (list.Count < 2)
                return false;

            Vector actualDirection =
                actualDirectionOverride
                ?? (
                    swapAxes
                        ? new Vector(direction.Y, direction.X, direction.Z)
                        : new Vector(direction.X, direction.Y, direction.Z)
                );

            TSD.StraightDimensionSet dim = handler.CreateDimensionSet(
                view,
                list,
                actualDirection,
                distance
            );
            if (dim != null)
            {
                try
                {
                    dim.Modify();
                }
                catch { }
                return true;
            }

            return false;
        }

        private static List<DrawingPart> GetSelectedDrawingParts(TSD.DrawingHandler dh)
        {
            List<DrawingPart> result = new List<DrawingPart>();

            try
            {
                TSD.DrawingObjectEnumerator selected = dh.GetDrawingObjectSelector().GetSelected();
                while (selected != null && selected.MoveNext())
                {
                    DrawingPart dp = selected.Current as DrawingPart;
                    if (dp != null && dp.ModelIdentifier != null)
                        result.Add(dp);
                }
            }
            catch { }

            return result;
        }

        private static TSD.View TryGetSelectedPartsView(List<DrawingPart> parts)
        {
            if (parts == null)
                return null;

            for (int i = 0; i < parts.Count; i++)
            {
                try
                {
                    if (parts[i] == null)
                        continue;

                    TSD.View v = parts[i].GetView() as TSD.View;
                    if (v != null)
                        return v;
                }
                catch { }
            }

            return null;
        }

        private static ModelPart SelectModelPart(TSM.Model model, DrawingPart dp)
        {
            try
            {
                if (model == null || dp == null || dp.ModelIdentifier == null)
                    return null;

                ModelObject mo = model.SelectModelObject(dp.ModelIdentifier);
                return mo as ModelPart;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetAngleLongestVerticalBackEdge(
            ModelPart part,
            out double backX,
            out double backMinY,
            out double backMaxY
        )
        {
            backX = 0.0;
            backMinY = 0.0;
            backMaxY = 0.0;

            try
            {
                if (part == null)
                    return false;

                Solid solid = part.GetSolid();
                if (solid == null)
                    return false;

                double bestLength = -1.0;
                double bestX = 0.0;
                double bestMinY = 0.0;
                double bestMaxY = 0.0;

                FaceEnumerator faceEnum = solid.GetFaceEnumerator();
                while (faceEnum.MoveNext())
                {
                    Face face = faceEnum.Current as Face;
                    if (face == null)
                        continue;

                    LoopEnumerator loopEnum = face.GetLoopEnumerator();
                    while (loopEnum.MoveNext())
                    {
                        Loop loop = loopEnum.Current as Loop;
                        if (loop == null)
                            continue;

                        List<Point> loopPts = new List<Point>();
                        VertexEnumerator vertexEnum = loop.GetVertexEnumerator();
                        while (vertexEnum.MoveNext())
                        {
                            Point p = vertexEnum.Current as Point;
                            if (p != null)
                                loopPts.Add(new Point(p.X, p.Y, 0));
                        }

                        if (loopPts.Count < 2)
                            continue;

                        for (int i = 0; i < loopPts.Count; i++)
                        {
                            Point a = loopPts[i];
                            Point b = loopPts[(i + 1) % loopPts.Count];
                            if (a == null || b == null)
                                continue;

                            double dx = Math.Abs(a.X - b.X);
                            double dy = Math.Abs(a.Y - b.Y);

                            // Lưng L là cạnh đứng dài nhất trong mặt chiếu view.
                            // Điều kiện dy > dx*5 giúp loại cạnh xiên/ngang/chân L.
                            if (dx > 1.0)
                                continue;
                            if (dy <= 5.0 || dy < dx * 5.0)
                                continue;

                            double x = (a.X + b.X) * 0.5;
                            double minY = Math.Min(a.Y, b.Y);
                            double maxY = Math.Max(a.Y, b.Y);
                            double length = maxY - minY;

                            if (length > bestLength)
                            {
                                bestLength = length;
                                bestX = x;
                                bestMinY = minY;
                                bestMaxY = maxY;
                            }
                        }
                    }
                }

                if (bestLength <= 5.0)
                    return false;

                backX = bestX;
                backMinY = bestMinY;
                backMaxY = bestMaxY;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAnglePart(ModelPart part)
        {
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(profile))
                return false;

            // Các dạng L phổ biến: L75*75*6, L-75x75x6, L75X50X6...
            if (profile.StartsWith("L") || profile.StartsWith("<L"))
                return true;

            if (profile.IndexOf("ANGLE") >= 0)
                return true;

            return false;
        }

        private static bool IsPlatePart(ModelPart part)
        {
            string profile = GetProfileString(part).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(profile))
                return false;

            if (
                profile.StartsWith("PL")
                || profile.StartsWith("PLT")
                || profile.StartsWith("FB")
                || profile.StartsWith("FL")
                || profile.IndexOf("PLATE") >= 0
            )
                return true;

            if (
                profile.StartsWith("H")
                || profile.StartsWith("I")
                || profile.StartsWith("C")
                || profile.StartsWith("L")
                || profile.IndexOf("RHS") >= 0
                || profile.IndexOf("SHS") >= 0
                || profile.IndexOf("PIPE") >= 0
            )
                return false;

            return part is TSM.ContourPlate;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            if (part == null)
                return false;

            try
            {
                string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();
                string material = GetReportString(part, "MATERIAL").Trim().ToUpperInvariant();
                string profile = GetProfileString(part).Trim().ToUpperInvariant();
                string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();

                // Đồng bộ tiêu chí reference dummy của Slot03.
                if (name.IndexOf("DUMMY") >= 0 || name.IndexOf("BJ") >= 0)
                    return true;

                if (name.IndexOf("JOINT") >= 0 || name.IndexOf("JOYCON") >= 0)
                    return true;

                if (material.IndexOf("JOINT") >= 0 || material.IndexOf("JOYCON") >= 0)
                    return true;

                if (
                    partPos.IndexOf("DUMMY") >= 0
                    || partPos.IndexOf("BJ") >= 0
                    || partPos.IndexOf("JOINT") >= 0
                    || partPos.IndexOf("JOYCON") >= 0
                )
                    return true;

                // Giữ cổng legacy của Slot04 để không đổi hành vi cũ.
                if (profile == "PL10*10" || profile == "PL10X10" || profile == "PL10-10")
                    return true;
            }
            catch { }

            return false;
        }

        private static string GetProfileString(ModelPart part)
        {
            try
            {
                string value = "";
                part.GetReportProperty("PROFILE", ref value);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            catch { }

            try
            {
                object profile = part.Profile;
                if (profile != null)
                {
                    PropertyInfo prop = profile.GetType().GetProperty("ProfileString");
                    if (prop != null)
                    {
                        object v = prop.GetValue(profile, null);
                        if (v != null)
                            return v.ToString();
                    }
                }
            }
            catch { }

            return "";
        }

        private static string GetReportString(ModelPart part, string propertyName)
        {
            try
            {
                string value = "";
                part.GetReportProperty(propertyName, ref value);
                if (value == null)
                    return "";
                return value;
            }
            catch
            {
                return "";
            }
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

        private static double Distance2D(Point a, Point b)
        {
            if (a == null || b == null)
                return 999999999.0;

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min)
                return min;
            if (v > max)
                return max;
            return v;
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

        private static void Msg(string text)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(text);
            }
            catch { }
        }
    }
}
