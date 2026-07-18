#pragma warning disable 1633

using System;
using System.Collections;
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
            PHU_Slot04_SelectedProfilePalletDim.RunAuto();
            return "OK";
        }

        // 3 hàm này để MainForm/new button gọi trực tiếp khi người dùng chọn kiểu DIM.
        public static string RunLeft()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunLeft();
            return "OK";
        }

        public static string RunCenter()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunCenter();
            return "OK";
        }

        public static string RunRight()
        {
            PHU_Slot04_SelectedProfilePalletDim.RunRight();
            return "OK";
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

            List<DrawingPart> selectedDrawingParts = GetSelectedDrawingParts(dh);
            if (selectedDrawingParts.Count < 2)
            {
                Msg("Slot04: Hãy chọn ít nhất 2 part trong cùng view: 1 main + pallet 02 / thanh L. Nếu có pallet 01 thì chọn thêm pallet 01.");
                return;
            }

            TSD.View view = TryGetSelectedPartsView(selectedDrawingParts);
            if (view == null)
            {
                Msg("Slot04: Không tìm được view từ part đang chọn.");
                return;
            }

            int created = CreateSlot04Dims(model, view, selectedDrawingParts, targetMode);

            try { drawing.CommitChanges(); } catch { }

            if (created <= 0)
                Msg("Slot04: Chưa tạo được DIM. Kiểm tra đã chọn đúng 1 main + plate02/thanh L, hoặc 1 main + một hoặc nhiều cụm pallet 01/02 trong cùng view chưa.");
        }

        private static int CreateSlot04Dims(
            TSM.Model model,
            TSD.View view,
            List<DrawingPart> selectedDrawingParts,
            TargetMode targetMode)
        {
            int count = 0;

            TSM.TransformationPlane oldPlane =
                model.GetWorkPlaneHandler().GetCurrentTransformationPlane();

            try
            {
                // QUAN TRỌNG:
                // Set theo DisplayCoordinateSystem của view trước khi lấy biên dạng.
                // Mọi MinX/MaxX/MinY/MaxY đều là tọa độ thật trong view.
                model.GetWorkPlaneHandler().SetCurrentTransformationPlane(
                    new TSM.TransformationPlane(view.DisplayCoordinateSystem));

                List<PartBox> boxes = BuildSelectedPartBoxes(model, selectedDrawingParts);
                if (boxes.Count < 2)
                    return count;

                PartBox main = PickMainPart(boxes);
                if (main == null)
                    return count;

                List<PartBox> others = new List<PartBox>();
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (!SameIdentifier(boxes[i].ModelPart.Identifier, main.ModelPart.Identifier))
                        others.Add(boxes[i]);
                }

                if (others.Count < 1)
                    return count;

                List<PlateGroup> groups = BuildPlateGroups(main, others, targetMode);
                if (groups.Count == 0)
                    return count;

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
                count += CreateInternalDimsForGroups(handler, view, topPaletteGroups, true, GetTierDistance(1));
                count += CreateInternalDimsForGroups(handler, view, bottomPaletteGroups, false, GetTierDistance(1));

                // Tầng 2: chain main riêng cho cụm Palette 01/02.
                // Không gộp chung điểm lưng thanh L vào chain này.
                if (topPaletteGroups.Count > 0)
                    count += CreateMainChainForGroups(handler, view, main, topPaletteGroups, true, GetTierDistance(2));

                if (bottomPaletteGroups.Count > 0)
                    count += CreateMainChainForGroups(handler, view, main, bottomPaletteGroups, false, GetTierDistance(2));

                // Tầng 3: chain main riêng cho thanh L.
                // Khi quét chọn vừa có Palette 01/02 vừa có L, L luôn tách thành chain riêng và nhảy tầng.
                // Nếu chỉ có L, vẫn dùng tầng 3 để giữ quy tắc ổn định và tránh chồng dim về sau.
                if (topAngleGroups.Count > 0)
                    count += CreateMainChainForGroups(handler, view, main, topAngleGroups, true, GetTierDistance(3));

                if (bottomAngleGroups.Count > 0)
                    count += CreateMainChainForGroups(handler, view, main, bottomAngleGroups, false, GetTierDistance(3));
            }
            catch (Exception ex)
            {
                Msg("Slot04 ERROR:\n" + ex.Message);
            }
            finally
            {
                try { model.GetWorkPlaneHandler().SetCurrentTransformationPlane(oldPlane); } catch { }
            }

            return count;
        }

        private static double GetTierDistance(int tier)
        {
            if (tier < 1) tier = 1;
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

        private struct Bounds2D
        {
            public bool Valid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;

            public double Width { get { return Math.Abs(MaxX - MinX); } }
            public double Height { get { return Math.Abs(MaxY - MinY); } }
            public double CenterX { get { return (MinX + MaxX) / 2.0; } }
            public double CenterY { get { return (MinY + MaxY) / 2.0; } }
            public double Area { get { return Width * Height; } }
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

        private static PartBox PickMainPart(List<PartBox> boxes)
        {
            PartBox best = null;
            double bestScore = -1.0;

            for (int i = 0; i < boxes.Count; i++)
            {
                PartBox p = boxes[i];
                if (p == null || p.ModelPart == null)
                    continue;

                // Main thường là beam / purlin, không phải PLATE/L, và có chiều dài X lớn nhất trong view.
                double score = p.Box.Width;
                if (!p.IsPlate && !p.IsAngle) score += 1000000.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }

            return best;
        }

        private static void PickPallet01And02(
            PartBox main,
            List<PartBox> candidates,
            out PartBox plate01,
            out PartBox plate02)
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
                if (plate02 != null && SameIdentifier(p.ModelPart.Identifier, plate02.ModelPart.Identifier))
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
            TargetMode targetMode)
        {
            if (targetMode == TargetMode.Left)
                return new TargetChoice() { Name = "LEFT", X = plate02.MinX, Priority = 1, Score = 0 };

            if (targetMode == TargetMode.Center)
                return new TargetChoice() { Name = "CENTER", X = plate02.CenterX, Priority = 0, Score = 0 };

            if (targetMode == TargetMode.Right)
                return new TargetChoice() { Name = "RIGHT", X = plate02.MaxX, Priority = 2, Score = 0 };

            // Không chọn gì thì dùng thuật toán tự động hiện tại.
            return ChooseBestTarget(main, plate01, plate02);
        }

        private static TargetChoice ChooseTargetWithoutPlate01(
            Bounds2D main,
            Bounds2D plate02,
            TargetMode targetMode)
        {
            if (targetMode == TargetMode.Left)
                return new TargetChoice() { Name = "LEFT", X = plate02.MinX, Priority = 1, Score = 0 };

            if (targetMode == TargetMode.Center)
                return new TargetChoice() { Name = "CENTER", X = plate02.CenterX, Priority = 0, Score = 0 };

            if (targetMode == TargetMode.Right)
                return new TargetChoice() { Name = "RIGHT", X = plate02.MaxX, Priority = 2, Score = 0 };

            List<TargetChoice> candidates = new List<TargetChoice>();
            candidates.Add(new TargetChoice() { Name = "LEFT", X = plate02.MinX, Priority = 1 });
            candidates.Add(new TargetChoice() { Name = "CENTER", X = plate02.CenterX, Priority = 0 });
            candidates.Add(new TargetChoice() { Name = "RIGHT", X = plate02.MaxX, Priority = 2 });

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetChoice c = candidates[i];
                c.Score = ScoreTargetWithoutPlate01(main, c.X);
            }

            candidates.Sort(delegate (TargetChoice a, TargetChoice b)
            {
                int c = b.Score.CompareTo(a.Score);
                if (c != 0) return c;
                return a.X.CompareTo(b.X);
            });

            return candidates[0];
        }

        private static TargetChoice ChooseAngleBackTarget(
            PartBox anglePart,
            bool isTop,
            out double targetY)
        {
            targetY = isTop ? anglePart.Box.MaxY : anglePart.Box.MinY;

            double backX;
            double backMinY;
            double backMaxY;

            // Thanh L bắt buộc DIM vào LƯNG: cạnh ĐỨNG dài nhất của biên dạng L.
            // Không dùng cạnh ngoài bbox nữa vì dễ bắt nhầm vào chân/cạnh ngang của chữ L.
            if (TryGetAngleLongestVerticalBackEdge(anglePart.ModelPart, out backX, out backMinY, out backMaxY))
            {
                targetY = isTop ? backMaxY : backMinY;
                return new TargetChoice() { Name = "ANGLE_BACK", X = backX, Priority = 0, Score = 0 };
            }

            // Fallback rất hiếm: nếu không đọc được loop solid thì mới quay về cạnh đứng bbox ngoài.
            double x = anglePart.Box.MaxX;
            return new TargetChoice() { Name = "ANGLE_BACK_FALLBACK", X = x, Priority = 0, Score = 0 };
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
            Bounds2D plate02)
        {
            List<TargetChoice> candidates = new List<TargetChoice>();
            candidates.Add(new TargetChoice() { Name = "LEFT", X = plate02.MinX, Priority = 1 });
            candidates.Add(new TargetChoice() { Name = "CENTER", X = plate02.CenterX, Priority = 0 });
            candidates.Add(new TargetChoice() { Name = "RIGHT", X = plate02.MaxX, Priority = 2 });

            for (int i = 0; i < candidates.Count; i++)
            {
                TargetChoice c = candidates[i];
                c.Score = ScoreTarget(main, plate01, c.X);
            }

            candidates.Sort(delegate (TargetChoice a, TargetChoice b)
            {
                int c = b.Score.CompareTo(a.Score);
                if (c != 0) return c;
                return a.X.CompareTo(b.X);
            });

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

        private static List<PartBox> BuildSelectedPartBoxes(TSM.Model model, List<DrawingPart> selectedDrawingParts)
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
            TargetMode targetMode)
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
            TargetMode targetMode)
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
                sorted.Sort(delegate (PartBox a, PartBox b)
                {
                    double sa = a.Box.Height - a.Box.Width;
                    double sb = b.Box.Height - b.Box.Width;
                    return sb.CompareTo(sa);
                });

                int take = sorted.Count / 2;
                if (take < 1) take = 1;
                for (int i = 0; i < take && i < sorted.Count; i++)
                    result.Add(sorted[i]);
            }

            return result;
        }

        private static PartBox FindNearestPlate01ForPlate02(
            PartBox plate02,
            List<PartBox> plates,
            List<PartBox> usedPlate01)
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
                if (p.Box.Width < plate02.Box.Width * 1.20 && p.Box.Width < plate02.Box.Width + 20.0)
                    continue;

                double centerDistance = Distance2D(
                    new Point(p.Box.CenterX, p.Box.CenterY, 0),
                    new Point(plate02.Box.CenterX, plate02.Box.CenterY, 0));

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
            double distance)
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

                if (CreateDimChain(handler, view,
                    new Point[] { plate01Left, plate01Target, plate01Right },
                    direction,
                    distance))
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
            double distance)
        {
            if (handler == null || view == null || main == null || groups == null || groups.Count == 0)
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

            return CreateDimChain(handler, view, chain.ToArray(), direction, distance) ? 1 : 0;
        }

        private static int CompareGroupByTargetX(PlateGroup a, PlateGroup b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a.Target == null && b.Target == null) return 0;
            if (a.Target == null) return -1;
            if (b.Target == null) return 1;
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

                bool isAngleGroup = g.IsAngleBackDim ||
                    (g.Plate02 != null && g.Plate02.IsAngle);

                if (isAngleGroup == wantAngle)
                    result.Add(g);
            }

            result.Sort(CompareGroupByTargetX);
            return result;
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

        private static bool ContainsSamePart(List<PartBox> list, PartBox part)
        {
            if (list == null || part == null || part.ModelPart == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].ModelPart != null &&
                    SameIdentifier(list[i].ModelPart.Identifier, part.ModelPart.Identifier))
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
                catch
                {
                }
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

                if (p.X < b.MinX) b.MinX = p.X;
                if (p.X > b.MaxX) b.MaxX = p.X;
                if (p.Y < b.MinY) b.MinY = p.Y;
                if (p.Y > b.MaxY) b.MaxY = p.Y;
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
            catch
            {
            }

            return result;
        }

        private static bool CreateDimChain(
            TSD.StraightDimensionSetHandler handler,
            TSD.View view,
            Point[] points,
            Vector direction,
            double distance)
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
                    if (Distance2D(old, p) <= 0.5)
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

            TSD.StraightDimensionSet dim = handler.CreateDimensionSet(view, list, direction, distance);
            if (dim != null)
            {
                try { dim.Modify(); } catch { }
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
            catch
            {
            }

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
                catch
                {
                }
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
            out double backMaxY)
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

            if (profile.StartsWith("PL") ||
                profile.StartsWith("PLT") ||
                profile.StartsWith("FB") ||
                profile.StartsWith("FL") ||
                profile.IndexOf("PLATE") >= 0)
                return true;

            if (profile.StartsWith("H") ||
                profile.StartsWith("I") ||
                profile.StartsWith("C") ||
                profile.StartsWith("L") ||
                profile.IndexOf("RHS") >= 0 ||
                profile.IndexOf("SHS") >= 0 ||
                profile.IndexOf("PIPE") >= 0)
                return false;

            return part is TSM.ContourPlate;
        }

        private static bool IsDummyReferencePart(ModelPart part)
        {
            try
            {
                string name = GetReportString(part, "NAME").Trim().ToUpperInvariant();
                string profile = GetProfileString(part).Trim().ToUpperInvariant();
                string partPos = GetReportString(part, "PART_POS").Trim().ToUpperInvariant();

                if (name.IndexOf("DUMMY") >= 0 || partPos.IndexOf("DUMMY") >= 0)
                    return true;

                if (name.IndexOf("JOINT") >= 0 || partPos.IndexOf("JOINT") >= 0)
                    return true;

                if (profile == "PL10*10" || profile == "PL10X10" || profile == "PL10-10")
                    return true;
            }
            catch
            {
            }

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
            catch
            {
            }

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
            catch
            {
            }

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
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static bool SameIdentifier(Identifier a, Identifier b)
        {
            if (a == null || b == null)
                return false;

            try { return a.ID == b.ID; }
            catch { return a.ToString() == b.ToString(); }
        }

        private static void Msg(string text)
        {
            try { System.Windows.Forms.MessageBox.Show(text); }
            catch { }
        }
    }
}
