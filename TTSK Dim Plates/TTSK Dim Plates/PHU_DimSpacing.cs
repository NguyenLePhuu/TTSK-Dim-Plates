using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;

namespace TTSK_AutoDim_Plates
{
    // PHU V20 - NO SPLIT / VIEW ISOLATED / UNIFIED GEOMETRY SPACING
    // RULES:
    // - Do NOT create new dimension sets.
    // - Do NOT delete old dimension sets.
    // - Do NOT split/cut existing chains.
    // - Do NOT touch StraightDimension child objects.
    // - Tier 1 is locked absolutely; only tier 2..n Distance may change.
    // - One common arrange flow for H/V/S multi-set groups.
    // - Single combined chains are skipped because there is no separate tier object to arrange safely.
    // - Views are isolated by runtime view identity; Front/Top/Bottom never share tiers.
    // - Grouping/sorting uses visual side = OffsetDirection * Sign(Distance).
    // - V20: one spacing solver for ALL sides/views using the same visual-line geometry.
    // - No special RIGHT-side patch; if geometry is readable, every group is solved the same way.
    //   Root cause: Tekla Distance is measured from the outside reference point of the dimension points.
    //   Using center(DimensionPoints) created a small drift on right-side tiers.
    public static class PHU_DimSpacingNormalize
    {
        // TEST PORT TỪ FILE DIM PLATE:
        // Anchor A/B/C/D chỉ dùng làm GỐC TẦNG cho từng group.
        // Nếu dựng được anchor: tier1 = anchor + spacing, tier2 = tier1 + spacing, tier3 = tier2 + spacing...
        // Nếu không dựng được anchor an toàn thì tự fallback về logic DimSpacing cũ.
        private const bool USE_ANCHOR_ABCD_AS_TIER_BASE = true;

        // INTERNAL DIM: DIM nằm trong lòng thanh không được tham gia hệ tầng ngoài.
        // Nó vẫn được chỉnh khoảng cách theo input, nhưng lấy chính chân DIM của nó làm gốc.
        private const bool HANDLE_INTERNAL_DIMS_SEPARATELY = true;
        private const double INTERNAL_DIM_EDGE_TOL = 1.0;

        private class ViewAnchorInfo
        {
            public bool IsValid;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
        }

        private static Dictionary<int, ViewAnchorInfo> ViewAnchorMap = new Dictionary<int, ViewAnchorInfo>();

        public class Result
        {
            public int FoundCount;
            public int GroupCount;
            public int ChangedCount;
            public int FailedCount;
            public int SkippedCount;
            public double Spacing;
            public string Scope;

            public string ToDisplayText(bool apply)
            {
                return
                    "APPLY\r\n" +
                    "Scope: " + Scope + "\r\n" +
                    "Spacing: " + Spacing.ToString("0.###") + " mm\r\n\r\n" +
                    "Found dim sets: " + FoundCount + "\r\n" +
                    "Groups: " + GroupCount + "\r\n" +
                    "Changed: " + ChangedCount + "\r\n" +
                    "Skipped: " + SkippedCount + "\r\n" +
                    "Failed: " + FailedCount;
            }
        }

        private class DimSetItem
        {
            public object DimSet;
            public Tekla.Structures.Drawing.View View;
            public double Distance;
            public double AbsDistance;
            public double Sign;
            public SimpleVector Offset;
            public SimpleVector VisualOffset;
            public double OffsetAngle;
            public double LineAngle;
            public string GroupKey;
            public string SideKey;
            public string KindKey;
            public bool IsInternal;
        }

        public static Result Run(double spacing, string scope)
        {
            return Run(spacing, scope, true);
        }

        public static Result Run(double spacing, string scope, bool apply)
        {
            Result result = new Result();
            result.Spacing = spacing;
            result.Scope = string.IsNullOrWhiteSpace(scope) ? "Toàn bộ bản vẽ" : scope;

            if (spacing <= 0.0)
                throw new Exception("Khoảng cách dim phải lớn hơn 0.");

            DrawingHandler dh = new DrawingHandler();
            if (!dh.GetConnectionStatus())
                throw new Exception("Không kết nối được Tekla DrawingHandler.");

            Drawing drawing = dh.GetActiveDrawing();
            if (drawing == null)
                throw new Exception("Chưa mở bản vẽ Tekla active.");

            ContainerView sheet = drawing.GetSheet();
            if (sheet == null)
                throw new Exception("Không lấy được sheet của bản vẽ.");

            List<DimSetItem> items = CollectStraightDimensionSets(sheet, result.Scope);
            result.FoundCount = items.Count;

            // Dựng anchor A/B/C/D theo từng view từ toàn bộ chân DIM hiện có.
            // Chỉ dùng làm gốc tầng cho group; nếu fail thì fallback về logic cũ.
            BuildViewAnchorMap(items);

            // Tách DIM nằm trong lòng thanh ra khỏi hệ tầng ngoài.
            // Internal dim vẫn chạy spacing, nhưng dùng chính chân DIM của nó làm gốc và không ảnh hưởng tier ngoài.
            MarkInternalDimItems(items, spacing);

            Dictionary<string, List<DimSetItem>> groups = new Dictionary<string, List<DimSetItem>>();
            foreach (DimSetItem item in items)
            {
                if (item == null || item.DimSet == null)
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!groups.ContainsKey(item.GroupKey))
                    groups[item.GroupKey] = new List<DimSetItem>();

                groups[item.GroupKey].Add(item);
            }

            result.GroupCount = groups.Count;

            foreach (KeyValuePair<string, List<DimSetItem>> pair in groups)
            {
                List<DimSetItem> list = pair.Value;
                if (list == null || list.Count == 0)
                    continue;

                if (list.Count == 1)
                {
                    if (list[0] != null && list[0].IsInternal)
                    {
                        ArrangeInternalDimItem(list[0], spacing, result);
                    }
                    else
                    {
                        // NEW: External single DIM is still a valid tier-1 DIM.
                        // It has no tier-2..n group, but it can still be placed by Anchor A/B/C/D.
                        // If anchor/solver fails, only then skip to preserve the old safety rule.
                        ArrangeSingleExternalDimItemByAnchor(list[0], spacing, result);
                    }
                    continue;
                }

                ArrangeExistingSetGroup(list, spacing, result);
            }

            try { drawing.CommitChanges(); } catch { }
            return result;
        }

        private static void ArrangeExistingSetGroup(List<DimSetItem> list, double spacing, Result result)
        {
            if (list == null || list.Count <= 0)
                return;

            if (IsInternalOnlyGroup(list))
            {
                foreach (DimSetItem item in list)
                    ArrangeInternalDimItem(item, spacing, result);
                return;
            }

            // PHU V21 RULE:
            // - Existing layout/order is already correct.
            // - Use existing DimensionPoints only to read the current tier order.
            // - Do NOT create/delete/split any chain.
            // - Do NOT move points or child StraightDimension objects.
            // - User spacing is now the absolute tier distance:
            //      tier1 = spacing, tier2 = spacing*2, tier3 = spacing*3...
            //   Example: input 150 => first tier becomes 150, not keep old 123.

            SimpleVector groupAxis = GetGroupAxisFromFirstValidOffset(list);
            bool sortedByPoints = false;

            if (groupAxis.Length > 0.0001 && HasPointOrderLevel(list, groupAxis))
            {
                list.Sort(delegate (DimSetItem a, DimSetItem b)
                {
                    double la = GetTierOrderLevelFromExistingPoints(a, groupAxis);
                    double lb = GetTierOrderLevelFromExistingPoints(b, groupAxis);

                    bool aNan = Double.IsNaN(la);
                    bool bNan = Double.IsNaN(lb);
                    if (aNan && bNan)
                    {
                        int c0 = a.AbsDistance.CompareTo(b.AbsDistance);
                        if (c0 != 0) return c0;
                        return RuntimeHelpersHash(a.DimSet).CompareTo(RuntimeHelpersHash(b.DimSet));
                    }
                    if (aNan) return 1;
                    if (bNan) return -1;

                    int c = la.CompareTo(lb);
                    if (c != 0) return c;

                    c = a.AbsDistance.CompareTo(b.AbsDistance);
                    if (c != 0) return c;
                    return RuntimeHelpersHash(a.DimSet).CompareTo(RuntimeHelpersHash(b.DimSet));
                });
                sortedByPoints = true;
            }

            if (!sortedByPoints)
            {
                // Safe fallback: same logic as the vertical group that already worked.
                list.Sort(delegate (DimSetItem a, DimSetItem b)
                {
                    int c = a.AbsDistance.CompareTo(b.AbsDistance);
                    if (c != 0) return c;
                    return RuntimeHelpersHash(a.DimSet).CompareTo(RuntimeHelpersHash(b.DimSet));
                });
            }

            if (list.Count <= 1)
            {
                if (list.Count == 1)
                {
                    if (list[0] != null && list[0].IsInternal)
                        ArrangeInternalDimItem(list[0], spacing, result);
                    else
                        ArrangeSingleExternalDimItemByAnchor(list[0], spacing, result);
                }
                return;
            }

            // V21 ROOT RULE:
            // One algorithm for ALL directions / ALL views:
            //   1) Read existing visual order from points.
            //   2) Tier 1 is NOT locked anymore. It becomes spacing.
            //   3) Tier 2 becomes spacing*2, tier 3 becomes spacing*3...
            //   4) Use the same visual geometry solver for every side/view.
            double baseSign = Math.Abs(list[0].Sign) < 0.0001 ? 1.0 : list[0].Sign;
            bool useUnifiedGeometrySpacing = CanUseUnifiedGeometrySpacing(list, groupAxis);
            double targetBaseVisualLevel = Double.NaN;

            if (useUnifiedGeometrySpacing)
            {
                double tier1Distance = baseSign * spacing;
                targetBaseVisualLevel = GetVisualLineLevelForDistance(list[0], groupAxis, tier1Distance);
            }

            // TEST PORT PLATE - BẢN 2:
            // Anchor A/B/C/D không chỉ áp cho tier 1 nữa.
            // Anchor là BASE của cả group:
            //   tier1 = anchor + spacing
            //   tier2 = tier1 + spacing
            //   tier3 = tier2 + spacing...
            // Nếu fail ở group hoặc từng item thì fallback về solver cũ.
            double anchorBaseVisualLevel = Double.NaN;
            bool useAnchorBase =
                USE_ANCHOR_ABCD_AS_TIER_BASE &&
                TryGetAnchorFirstTierVisualLevel(list, groupAxis, spacing, out anchorBaseVisualLevel);

            for (int i = 0; i < list.Count; i++)
            {
                double targetAbs = spacing * (i + 1);
                double itemSign = Math.Abs(list[i].Sign) < 0.0001 ? baseSign : list[i].Sign;

                // Fallback: keep current side/sign, but make tier 1 = spacing.
                double newDistance = itemSign * targetAbs;

                bool solvedByAnchorOk = false;
                if (useAnchorBase && !Double.IsNaN(anchorBaseVisualLevel))
                {
                    double solvedByAnchor;
                    double targetLevel = anchorBaseVisualLevel + spacing * i;
                    if (TrySolveDistanceForTargetVisualLevelByFirstFoot(list[i], groupAxis, targetLevel, out solvedByAnchor))
                    {
                        newDistance = solvedByAnchor;
                        solvedByAnchorOk = true;
                    }
                }

                if (!solvedByAnchorOk && useUnifiedGeometrySpacing && !Double.IsNaN(targetBaseVisualLevel))
                {
                    double solvedDistance;
                    if (TrySolveDistanceForTargetVisualLevel(list[i], groupAxis, targetBaseVisualLevel + spacing * i, out solvedDistance))
                        newDistance = solvedDistance;
                }

                TryApplyDistance(list[i], newDistance, result);
            }
        }


        private static void MarkInternalDimItems(List<DimSetItem> items, double spacing)
        {
            try
            {
                if (!HANDLE_INTERNAL_DIMS_SEPARATELY || items == null)
                    return;

                foreach (DimSetItem item in items)
                {
                    if (item == null || item.View == null || item.DimSet == null)
                        continue;

                    if (IsInternalDimItem(item, spacing))
                    {
                        item.IsInternal = true;
                        item.GroupKey = GetViewKey(item.View) + "_INTERNAL_" + RuntimeHelpersHash(item.DimSet).ToString();
                    }
                }
            }
            catch
            {
            }
        }

        private static bool IsInternalOnlyGroup(List<DimSetItem> list)
        {
            if (list == null || list.Count == 0)
                return false;

            foreach (DimSetItem item in list)
            {
                if (item == null || !item.IsInternal)
                    return false;
            }

            return true;
        }

        private static bool IsInternalDimItem(DimSetItem item, double spacing)
        {
            try
            {
                if (item == null || item.View == null || item.DimSet == null)
                    return false;

                ViewAnchorInfo info;
                if (ViewAnchorMap == null || !ViewAnchorMap.TryGetValue(RuntimeHelpersHash(item.View), out info))
                    return false;

                if (info == null || !info.IsValid)
                    return false;

                List<Point> pts = GetDimensionPoints(item.DimSet);
                if (pts == null || pts.Count < 2)
                    return false;

                // Ngưỡng mới theo yêu cầu:
                // - DIM nằm trong lòng thật sự mới xem là INTERNAL.
                // - Nếu chân DIM nằm gần mép ngoài trong khoảng spacing người dùng nhập,
                //   tối đa 200mm, thì KHÔNG xem là internal để nó được chạy anchor/tier ngoài.
                // Ví dụ dim rãnh 7 nằm sát mép trên: không còn bị ArrangeInternalDimItem()
                // kéo offset từ chính chân nhỏ đó nữa.
                double nearEdgeLimit = spacing;
                if (nearEdgeLimit <= 0.0)
                    nearEdgeLimit = 200.0;
                if (nearEdgeLimit > 200.0)
                    nearEdgeLimit = 200.0;

                // H = dim đo ngang, offset lên/xuống: internal nếu chân dim nằm trong chiều cao vật thể
                // và KHÔNG nằm gần mép trên/dưới theo nearEdgeLimit.
                if (item.KindKey == "H")
                {
                    foreach (Point p in pts)
                    {
                        if (p == null)
                            return false;

                        if (p.Y <= info.MinY + INTERNAL_DIM_EDGE_TOL || p.Y >= info.MaxY - INTERNAL_DIM_EDGE_TOL)
                            return false;

                        if (p.Y <= info.MinY + nearEdgeLimit || p.Y >= info.MaxY - nearEdgeLimit)
                            return false;
                    }

                    return true;
                }

                // V = dim đo dọc, offset trái/phải: internal nếu chân dim nằm trong chiều rộng vật thể
                // và KHÔNG nằm gần mép trái/phải theo nearEdgeLimit.
                if (item.KindKey == "V")
                {
                    foreach (Point p in pts)
                    {
                        if (p == null)
                            return false;

                        if (p.X <= info.MinX + INTERNAL_DIM_EDGE_TOL || p.X >= info.MaxX - INTERNAL_DIM_EDGE_TOL)
                            return false;

                        if (p.X <= info.MinX + nearEdgeLimit || p.X >= info.MaxX - nearEdgeLimit)
                            return false;
                    }

                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void ArrangeInternalDimItem(DimSetItem item, double spacing, Result result)
        {
            try
            {
                if (item == null || item.DimSet == null)
                {
                    if (result != null) result.SkippedCount++;
                    return;
                }

                double sign = Math.Abs(item.Sign) < 0.0001 ? 1.0 : item.Sign;
                double newDistance = sign * spacing;
                TryApplyDistance(item, newDistance, result);
            }
            catch
            {
                if (result != null) result.FailedCount++;
            }
        }

        private static void ArrangeSingleExternalDimItemByAnchor(DimSetItem item, double spacing, Result result)
        {
            try
            {
                if (item == null || item.DimSet == null)
                {
                    if (result != null) result.SkippedCount++;
                    return;
                }

                SimpleVector axis = item.VisualOffset;
                if (axis.Length < 0.0001)
                {
                    if (result != null) result.SkippedCount++;
                    return;
                }
                axis.Normalize();

                List<DimSetItem> one = new List<DimSetItem>();
                one.Add(item);

                double targetLevel;
                if (!USE_ANCHOR_ABCD_AS_TIER_BASE ||
                    !TryGetAnchorFirstTierVisualLevel(one, axis, spacing, out targetLevel) ||
                    Double.IsNaN(targetLevel))
                {
                    if (result != null) result.SkippedCount++;
                    return;
                }

                double solvedDistance;
                if (TrySolveDistanceForTargetVisualLevelByFirstFoot(item, axis, targetLevel, out solvedDistance))
                {
                    TryApplyDistance(item, solvedDistance, result);
                    return;
                }

                if (result != null) result.SkippedCount++;
            }
            catch
            {
                if (result != null) result.FailedCount++;
            }
        }

        private static void BuildViewAnchorMap(List<DimSetItem> items)
        {
            ViewAnchorMap = new Dictionary<int, ViewAnchorInfo>();

            try
            {
                if (items == null)
                    return;

                foreach (DimSetItem item in items)
                {
                    if (item == null || item.View == null || item.DimSet == null)
                        continue;

                    List<Point> pts = GetDimensionPoints(item.DimSet);
                    if (pts == null || pts.Count == 0)
                        continue;

                    int key = RuntimeHelpersHash(item.View);
                    ViewAnchorInfo info;
                    if (!ViewAnchorMap.TryGetValue(key, out info) || info == null)
                    {
                        info = new ViewAnchorInfo();
                        info.MinX = 999999999.0;
                        info.MaxX = -999999999.0;
                        info.MinY = 999999999.0;
                        info.MaxY = -999999999.0;
                        ViewAnchorMap[key] = info;
                    }

                    foreach (Point p in pts)
                    {
                        if (p == null)
                            continue;

                        if (p.X < info.MinX) info.MinX = p.X;
                        if (p.X > info.MaxX) info.MaxX = p.X;
                        if (p.Y < info.MinY) info.MinY = p.Y;
                        if (p.Y > info.MaxY) info.MaxY = p.Y;
                    }
                }

                foreach (KeyValuePair<int, ViewAnchorInfo> pair in ViewAnchorMap)
                {
                    ViewAnchorInfo info = pair.Value;
                    if (info == null)
                        continue;

                    info.IsValid =
                        info.MaxX > info.MinX + 0.001 &&
                        info.MaxY > info.MinY + 0.001 &&
                        !Double.IsNaN(info.MinX) && !Double.IsNaN(info.MaxX) &&
                        !Double.IsNaN(info.MinY) && !Double.IsNaN(info.MaxY) &&
                        !Double.IsInfinity(info.MinX) && !Double.IsInfinity(info.MaxX) &&
                        !Double.IsInfinity(info.MinY) && !Double.IsInfinity(info.MaxY);
                }
            }
            catch
            {
                ViewAnchorMap = new Dictionary<int, ViewAnchorInfo>();
            }
        }

        private static bool TryGetAnchorFirstTierVisualLevel(
            List<DimSetItem> list,
            SimpleVector visualAxis,
            double spacing,
            out double targetVisualLevel)
        {
            targetVisualLevel = Double.NaN;

            try
            {
                if (list == null || list.Count == 0 || visualAxis.Length < 0.0001 || spacing <= 0.0)
                    return false;

                DimSetItem seed = list[0];
                if (seed == null || seed.View == null)
                    return false;

                ViewAnchorInfo info;
                if (ViewAnchorMap == null || !ViewAnchorMap.TryGetValue(RuntimeHelpersHash(seed.View), out info))
                    return false;

                if (info == null || !info.IsValid)
                    return false;

                visualAxis.Normalize();

                // Cùng ý tưởng PA9 plate:
                // Top    = Y cao nhất + spacing
                // Bottom = Y thấp nhất - spacing
                // Left   = X nhỏ nhất - spacing
                // Right  = X lớn nhất + spacing
                // Viết dưới dạng projection theo visualAxis để dùng chung cho mọi hướng.
                if (Math.Abs(visualAxis.Y) >= Math.Abs(visualAxis.X))
                {
                    if (visualAxis.Y >= 0.0)
                        targetVisualLevel = info.MaxY + spacing;
                    else
                        targetVisualLevel = -info.MinY + spacing;
                }
                else
                {
                    if (visualAxis.X >= 0.0)
                        targetVisualLevel = info.MaxX + spacing;
                    else
                        targetVisualLevel = -info.MinX + spacing;
                }

                return !Double.IsNaN(targetVisualLevel) && !Double.IsInfinity(targetVisualLevel);
            }
            catch
            {
                targetVisualLevel = Double.NaN;
                return false;
            }
        }

        private static bool TrySolveDistanceForTargetVisualLevelByFirstFoot(
            DimSetItem item,
            SimpleVector visualAxis,
            double targetVisualLevel,
            out double distance)
        {
            distance = 0.0;

            try
            {
                if (item == null || item.DimSet == null || visualAxis.Length < 0.0001)
                    return false;

                List<Point> pts = GetDimensionPoints(item.DimSet);
                if (pts == null || pts.Count == 0 || pts[0] == null)
                    return false;

                Point firstFoot = pts[0];
                visualAxis.Normalize();

                SimpleVector rawOffset = item.Offset;
                if (rawOffset.Length < 0.0001)
                    return false;
                rawOffset.Normalize();

                double dot = rawOffset.X * visualAxis.X + rawOffset.Y * visualAxis.Y;
                if (Math.Abs(dot) < 0.0001)
                    return false;

                double firstFootLevel = firstFoot.X * visualAxis.X + firstFoot.Y * visualAxis.Y;
                distance = (targetVisualLevel - firstFootLevel) / dot;

                if (Double.IsNaN(distance) || Double.IsInfinity(distance))
                    return false;

                return Math.Abs(distance) > 1.0;
            }
            catch
            {
                distance = 0.0;
                return false;
            }
        }

        private static SimpleVector GetGroupAxisFromFirstValidOffset(List<DimSetItem> list)
        {
            SimpleVector v = new SimpleVector();
            if (list == null) return v;

            foreach (DimSetItem item in list)
            {
                if (item == null) continue;

                // Use VISUAL side, not raw UpDirection.
                // Raw UpDirection + negative Distance can visually place the dim on the opposite side.
                v = item.VisualOffset;
                if (v.Length > 0.0001)
                {
                    v.Normalize();
                    return v;
                }
            }

            return v;
        }

        private static bool HasPointOrderLevel(List<DimSetItem> list, SimpleVector axis)
        {
            if (list == null || axis.Length < 0.0001)
                return false;

            int ok = 0;
            foreach (DimSetItem item in list)
            {
                if (!Double.IsNaN(GetTierOrderLevelFromExistingPoints(item, axis)))
                    ok++;
            }
            return ok >= 2;
        }

        private static double GetTierOrderLevelFromExistingPoints(DimSetItem item, SimpleVector axis)
        {
            if (item == null || item.DimSet == null || axis.Length < 0.0001)
                return Double.NaN;

            List<Point> pts = GetDimensionPoints(item.DimSet);
            if (pts == null || pts.Count == 0)
                return Double.NaN;

            double anchor = GetPointsCenterProjection(pts, axis);
            if (Double.IsNaN(anchor))
                return Double.NaN;

            // IMPORTANT:
            // Use DimensionPoints + AbsDistance only to rank tiers by their existing visible side.
            // Do not use this value to set Distance. This avoids the old double/projection error.
            return anchor + item.AbsDistance;
        }

        private static void TryApplyDistance(DimSetItem item, double newDistance, Result result)
        {
            try
            {
                bool ok = SetDistanceValue(item.DimSet, newDistance);
                if (ok)
                {
                    InvokeNoArg(item.DimSet, "Modify");
                    result.ChangedCount++;
                }
                else
                {
                    result.FailedCount++;
                }
            }
            catch
            {
                result.FailedCount++;
            }
        }


        private static bool CanUseUnifiedGeometrySpacing(List<DimSetItem> list, SimpleVector groupAxis)
        {
            if (list == null || list.Count <= 1 || groupAxis.Length < 0.0001)
                return false;

            int ok = 0;
            foreach (DimSetItem item in list)
            {
                double level = GetVisualLineLevel(item, groupAxis);
                if (!Double.IsNaN(level))
                    ok++;
            }

            return ok >= 2;
        }

        private static bool TrySolveDistanceForTargetVisualLevel(
            DimSetItem item,
            SimpleVector visualAxis,
            double targetVisualLevel,
            out double distance)
        {
            distance = 0.0;

            if (item == null || item.DimSet == null || visualAxis.Length < 0.0001)
                return false;

            double anchor = GetSideAnchorProjection(item, visualAxis);
            if (Double.IsNaN(anchor))
                return false;

            SimpleVector rawOffset = item.Offset;
            if (rawOffset.Length < 0.0001)
                return false;
            rawOffset.Normalize();

            double dot = rawOffset.X * visualAxis.X + rawOffset.Y * visualAxis.Y;
            if (Math.Abs(dot) < 0.0001)
                return false;

            distance = (targetVisualLevel - anchor) / dot;
            return true;
        }

        private static SimpleVector GetGroupOutDirection(List<DimSetItem> list)
        {
            SimpleVector v = new SimpleVector();
            if (list == null || list.Count == 0)
                return v;

            // Use tier-1 candidate by AbsDistance only to define stable outward side.
            // This does not move tier 1; it only chooses a common axis for measuring existing levels.
            DimSetItem seed = list[0];
            foreach (DimSetItem item in list)
            {
                if (item != null && item.AbsDistance < seed.AbsDistance)
                    seed = item;
            }

            v = seed.Offset;
            if (v.Length < 0.0001)
                return v;
            v.Normalize();

            // Tekla Distance may be negative. Actual visual line can lie opposite to OffsetDirection.
            double sign = Math.Abs(seed.Sign) < 0.0001 ? 1.0 : seed.Sign;
            v.X *= sign;
            v.Y *= sign;
            v.Normalize();
            return v;
        }

        private static bool HasGeometryLevel(List<DimSetItem> list, SimpleVector groupOut)
        {
            if (list == null || list.Count == 0 || groupOut.Length < 0.0001)
                return false;

            int okCount = 0;
            foreach (DimSetItem item in list)
            {
                double level = GetVisualLineLevel(item, groupOut);
                if (!Double.IsNaN(level))
                    okCount++;
            }
            return okCount >= 2;
        }


        private static double GetVisualLineLevelForDistance(DimSetItem item, SimpleVector outUnit, double distance)
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            double anchor = GetSideAnchorProjection(item, outUnit);
            if (Double.IsNaN(anchor))
                return Double.NaN;

            SimpleVector itemOffset = item.Offset;
            if (itemOffset.Length < 0.0001)
                return Double.NaN;
            itemOffset.Normalize();

            double dotOffsetToOut = itemOffset.X * outUnit.X + itemOffset.Y * outUnit.Y;
            if (Math.Abs(dotOffsetToOut) < 0.0001)
                return Double.NaN;

            return anchor + distance * dotOffsetToOut;
        }

        private static double GetVisualLineLevel(DimSetItem item, SimpleVector outUnit)
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            double anchor = GetSideAnchorProjection(item, outUnit);
            if (Double.IsNaN(anchor))
                return Double.NaN;

            SimpleVector itemOffset = item.Offset;
            if (itemOffset.Length < 0.0001)
                return Double.NaN;
            itemOffset.Normalize();

            double dotOffsetToOut = itemOffset.X * outUnit.X + itemOffset.Y * outUnit.Y;
            if (Math.Abs(dotOffsetToOut) < 0.0001)
                return Double.NaN;

            return anchor + item.Distance * dotOffsetToOut;
        }


        private static double GetSideAnchorProjection(DimSetItem item, SimpleVector outUnit)
        {
            if (item == null || item.DimSet == null || outUnit.Length < 0.0001)
                return Double.NaN;

            List<Point> pts = GetDimensionPoints(item.DimSet);
            if (pts == null || pts.Count == 0)
                return Double.NaN;

            outUnit.Normalize();

            // UNIFIED ANCHOR:
            // Use the same anchor rule for every side: the outer-most dimension point
            // in the current visual offset direction. This keeps one algorithm for
            // LEFT/RIGHT/TOP/BOTTOM/SLOPE and removes the old per-side correction.
            double best = Double.NegativeInfinity;
            foreach (Point p in pts)
            {
                double v = p.X * outUnit.X + p.Y * outUnit.Y;
                if (v > best)
                    best = v;
            }

            return Double.IsNegativeInfinity(best) ? Double.NaN : best;
        }

        private static double GetAnchorProjection(DimSetItem item, SimpleVector offUnit)
        {
            if (item == null || item.DimSet == null)
                return Double.NaN;

            List<Point> pts = GetDimensionPoints(item.DimSet);
            if (pts == null || pts.Count == 0)
                return Double.NaN;

            return GetPointsCenterProjection(pts, offUnit);
        }

        private static double GetPointsCenterProjection(List<Point> pts, SimpleVector offUnit)
        {
            if (pts == null || pts.Count == 0 || offUnit.Length < 0.0001)
                return Double.NaN;

            double cx = 0.0;
            double cy = 0.0;
            foreach (Point p in pts)
            {
                cx += p.X;
                cy += p.Y;
            }
            cx /= pts.Count;
            cy /= pts.Count;

            return cx * offUnit.X + cy * offUnit.Y;
        }

        private static List<Point> GetDimensionPoints(object dimSet)
        {
            object raw = GetPropertyOrFieldValue(dimSet, "DimensionPoints");
            if (raw == null)
                return null;

            List<Point> points = new List<Point>();

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable != null)
            {
                foreach (object obj in enumerable)
                {
                    Point p = obj as Point;
                    if (p != null)
                        points.Add(p);
                }
            }

            if (points.Count > 0)
                return RemoveDuplicatePoints(points);

            // Fallback: Count + indexer / Item property.
            int count = 0;
            object countObj = GetPropertyOrFieldValue(raw, "Count");
            try { if (countObj != null) count = Convert.ToInt32(countObj); } catch { count = 0; }

            if (count <= 0)
                return null;

            Type t = raw.GetType();
            PropertyInfo indexer = null;
            foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length == 1)
                {
                    indexer = prop;
                    break;
                }
            }

            if (indexer != null)
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        object obj = indexer.GetValue(raw, new object[] { i });
                        Point p = obj as Point;
                        if (p != null)
                            points.Add(p);
                    }
                    catch { }
                }
            }

            return points.Count > 0 ? RemoveDuplicatePoints(points) : null;
        }

        private static List<Point> RemoveDuplicatePoints(List<Point> points)
        {
            List<Point> clean = new List<Point>();
            foreach (Point p in points)
            {
                bool exists = false;
                foreach (Point q in clean)
                {
                    if (Distance2(p, q) < 0.01)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    clean.Add(p);
            }
            return clean;
        }

        private static double Distance2(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static List<Point> SortPointsAlongDimension(List<Point> points, string kindKey)
        {
            List<Point> sorted = new List<Point>(points);

            if (kindKey == "H")
            {
                sorted.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });
                return sorted;
            }

            if (kindKey == "V")
            {
                sorted.Sort(delegate (Point a, Point b)
                {
                    int c = a.Y.CompareTo(b.Y);
                    if (c != 0) return c;
                    return a.X.CompareTo(b.X);
                });
                return sorted;
            }

            // Sloped: sort along the longest axis. This keeps point order stable without moving anchors.
            double minX = sorted[0].X, maxX = sorted[0].X;
            double minY = sorted[0].Y, maxY = sorted[0].Y;
            foreach (Point p in sorted)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if ((maxX - minX) >= (maxY - minY))
            {
                sorted.Sort(delegate (Point a, Point b)
                {
                    int c = a.X.CompareTo(b.X);
                    if (c != 0) return c;
                    return a.Y.CompareTo(b.Y);
                });
            }
            else
            {
                sorted.Sort(delegate (Point a, Point b)
                {
                    int c = a.Y.CompareTo(b.Y);
                    if (c != 0) return c;
                    return a.X.CompareTo(b.X);
                });
            }

            return sorted;
        }

        private static List<DimSetItem> CollectStraightDimensionSets(ContainerView sheet, string scope)
        {
            List<DimSetItem> items = new List<DimSetItem>();

            Type straightSetType = Type.GetType("Tekla.Structures.Drawing.StraightDimensionSet, Tekla.Structures.Drawing");
            if (straightSetType == null)
                return items;

            List<Tekla.Structures.Drawing.View> allViews = new List<Tekla.Structures.Drawing.View>();
            DrawingObjectEnumerator views = sheet.GetAllViews();
            while (views.MoveNext())
            {
                Tekla.Structures.Drawing.View view = views.Current as Tekla.Structures.Drawing.View;
                if (view != null)
                    allViews.Add(view);
            }

            Tekla.Structures.Drawing.View frontView = FindViewByViewTypeForDimSpacing(allViews, "FrontView", "Front");

            foreach (Tekla.Structures.Drawing.View view in allViews)
            {
                if (view == null) continue;
                if (!ScopeAcceptsView(view, scope, frontView)) continue;

                try
                {
                    DrawingObjectEnumerator e = view.GetAllObjects(straightSetType);
                    while (e.MoveNext())
                    {
                        object dimSet = e.Current;
                        if (dimSet == null) continue;

                        DimSetItem item = BuildDimSetItem(dimSet, view);
                        if (item != null)
                            items.Add(item);
                    }
                }
                catch { }
            }

            return items;
        }

        private static DimSetItem BuildDimSetItem(object dimSet, Tekla.Structures.Drawing.View view)
        {
            double distance;
            if (!TryGetDistanceValue(dimSet, out distance))
                return null;

            SimpleVector offset;
            if (!TryGetOffsetDirection(dimSet, out offset))
                return null;

            offset.Normalize();

            string viewKey = GetViewKey(view);
            double sign = distance < 0.0 ? -1.0 : 1.0;

            // VISUAL side = UpDirection/OffsetDirection * sign(Distance).
            // This is the key fix for: upper dims running downward / lower dims running upward.
            SimpleVector visualOffset = offset;
            visualOffset.X *= sign;
            visualOffset.Y *= sign;
            visualOffset.Normalize();

            double offsetAngle = Normalize360(Math.Atan2(visualOffset.Y, visualOffset.X) * 180.0 / Math.PI);
            double roundedOffset = RoundAngle(offsetAngle, 5.0, 360.0);

            string kindKey = GetKindKey(visualOffset);
            string sideKey = GetSideKey(visualOffset, kindKey);

            DimSetItem item = new DimSetItem();
            item.DimSet = dimSet;
            item.View = view;
            item.Distance = distance;
            item.AbsDistance = Math.Abs(distance);
            item.Sign = sign;
            item.Offset = offset;             // raw Tekla direction, used only when setting Distance
            item.VisualOffset = visualOffset; // actual drawing side, used for grouping/sorting
            item.OffsetAngle = roundedOffset;
            item.LineAngle = 0.0;
            item.SideKey = sideKey;
            item.KindKey = kindKey;

            if (kindKey == "V")
                item.GroupKey = viewKey + "_VERT_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;
            else if (kindKey == "H")
                item.GroupKey = viewKey + "_HORIZ_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;
            else
                item.GroupKey = viewKey + "_SLOPE_OFF_" + roundedOffset.ToString("0") + "_SIDE_" + sideKey;

            return item;
        }

        private static string GetKindKey(SimpleVector offset)
        {
            double ax = Math.Abs(offset.X);
            double ay = Math.Abs(offset.Y);

            if (ay >= ax * 2.0) return "H";
            if (ax >= ay * 2.0) return "V";
            return "S";
        }

        private static string GetSideKey(SimpleVector offset, string kindKey)
        {
            if (kindKey == "H")
                return offset.Y >= 0.0 ? "TOP" : "BOTTOM";

            if (kindKey == "V")
                return offset.X >= 0.0 ? "RIGHT" : "LEFT";

            double angle = Normalize360(Math.Atan2(offset.Y, offset.X) * 180.0 / Math.PI);
            if (angle >= 45.0 && angle < 135.0) return "SLOPE_TOP";
            if (angle >= 135.0 && angle < 225.0) return "SLOPE_LEFT";
            if (angle >= 225.0 && angle < 315.0) return "SLOPE_BOTTOM";
            return "SLOPE_RIGHT";
        }

        private static bool ScopeAcceptsView(
            Tekla.Structures.Drawing.View view,
            string scope,
            Tekla.Structures.Drawing.View frontView)
        {
            if (view == null) return false;

            if (string.IsNullOrWhiteSpace(scope) ||
                scope.IndexOf("Toàn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                scope.IndexOf("All", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string target = scope.Trim().ToLowerInvariant();

            if (target == "front")
                return ViewTypeMatchesForDimSpacing(view, "FrontView", "Front");

            if (target == "top")
            {
                if (ViewTypeMatchesForDimSpacing(view, "TopView", "Top"))
                    return true;

                return IsSpecialTopSectionForDimSpacing(view, frontView);
            }

            if (target == "bottom")
            {
                if (ViewTypeMatchesForDimSpacing(view, "BottomView", "Bottom"))
                    return true;

                return IsSpecialBottomSectionForDimSpacing(view, frontView);
            }

            return true;
        }

        private static Tekla.Structures.Drawing.View FindViewByViewTypeForDimSpacing(
            List<Tekla.Structures.Drawing.View> views,
            string exactViewTypeName,
            string fallbackText)
        {
            try
            {
                if (views == null)
                    return null;

                foreach (Tekla.Structures.Drawing.View view in views)
                {
                    if (ViewTypeMatchesForDimSpacing(view, exactViewTypeName, fallbackText))
                        return view;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ViewTypeMatchesForDimSpacing(
            Tekla.Structures.Drawing.View view,
            string exactViewTypeName,
            string fallbackText)
        {
            try
            {
                if (view == null)
                    return false;

                string text = "";
                try { text = view.ViewType.ToString(); } catch { text = ""; }

                if (!string.IsNullOrEmpty(exactViewTypeName) &&
                    string.Equals(text, exactViewTypeName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrEmpty(fallbackText) &&
                    text.IndexOf(fallbackText, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch
            {
            }

            return false;
        }

        private static bool IsSpecialTopSectionForDimSpacing(
            Tekla.Structures.Drawing.View view,
            Tekla.Structures.Drawing.View frontView)
        {
            try
            {
                if (view == null || frontView == null)
                    return false;

                if (!ViewTypeMatchesForDimSpacing(view, "SectionView", "Section"))
                    return false;

                if (!IsSectionWidthCloseToFrontForDimSpacing(view, frontView))
                    return false;

                return view.Origin.Y > frontView.Origin.Y;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSpecialBottomSectionForDimSpacing(
            Tekla.Structures.Drawing.View view,
            Tekla.Structures.Drawing.View frontView)
        {
            try
            {
                if (view == null || frontView == null)
                    return false;

                if (!ViewTypeMatchesForDimSpacing(view, "SectionView", "Section"))
                    return false;

                if (!IsSectionWidthCloseToFrontForDimSpacing(view, frontView))
                    return false;

                return view.Origin.Y < frontView.Origin.Y;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSectionWidthCloseToFrontForDimSpacing(
            Tekla.Structures.Drawing.View sectionView,
            Tekla.Structures.Drawing.View frontView)
        {
            try
            {
                double sectionWidth = GetViewRestrictionBoxWidthForDimSpacing(sectionView);
                double frontWidth = GetViewRestrictionBoxWidthForDimSpacing(frontView);

                if (sectionWidth <= 0.0 || frontWidth <= 0.0)
                    return false;

                double tolerance = Math.Max(10.0, frontWidth * 0.10);
                return Math.Abs(sectionWidth - frontWidth) <= tolerance;
            }
            catch
            {
                return false;
            }
        }

        private static double GetViewRestrictionBoxWidthForDimSpacing(Tekla.Structures.Drawing.View view)
        {
            try
            {
                if (view == null || view.RestrictionBox == null)
                    return 0.0;

                AABB box = view.RestrictionBox;
                if (box.MinPoint == null || box.MaxPoint == null)
                    return 0.0;

                return Math.Abs(box.MaxPoint.X - box.MinPoint.X);
            }
            catch
            {
                return 0.0;
            }
        }

        private static string GetViewKey(Tekla.Structures.Drawing.View view)
        {
            // IMPORTANT: view.Name can be empty for Front/Top/Bottom in some drawings.
            // If the key is empty, dimensions from different views are mixed together.
            // Always append runtime hash so each Tekla view has independent tiers.
            string viewKey = "VIEW";
            try
            {
                object typeObj = view.GetType().GetProperty("ViewType")?.GetValue(view, null);
                if (typeObj != null)
                    viewKey = typeObj.ToString();
            }
            catch { }

            try
            {
                object nameObj = view.GetType().GetProperty("Name")?.GetValue(view, null);
                if (nameObj != null && !string.IsNullOrWhiteSpace(nameObj.ToString()))
                    viewKey = viewKey + "_" + nameObj.ToString();
            }
            catch { }

            return viewKey + "_VH_" + RuntimeHelpersHash(view).ToString();
        }

        private static double Normalize360(double angle)
        {
            while (angle < 0.0) angle += 360.0;
            while (angle >= 360.0) angle -= 360.0;
            return angle;
        }

        private static double RoundAngle(double angle, double step, double max)
        {
            double a = Math.Round(angle / step) * step;
            while (a < 0.0) a += max;
            while (a >= max) a -= max;
            return a;
        }

        private static bool TryGetDistanceValue(object dim, out double distance)
        {
            distance = 0.0;
            if (TryReadDoublePropertyOrField(dim, "Distance", out distance)) return true;

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            if (TryReadDoublePropertyOrField(attr, "Distance", out distance)) return true;

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            if (TryReadDoublePropertyOrField(setAttr, "Distance", out distance)) return true;

            return false;
        }

        private static bool SetDistanceValue(object dim, double distance)
        {
            if (TrySetDoublePropertyOrField(dim, "Distance", distance)) return true;

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            if (TrySetDoublePropertyOrField(attr, "Distance", distance)) return true;

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            if (TrySetDoublePropertyOrField(setAttr, "Distance", distance)) return true;

            return false;
        }

        private static bool TryGetOffsetDirection(object dim, out SimpleVector up)
        {
            up = new SimpleVector();
            string[] names = new string[] { "UpDirection", "OffsetDirection", "Normal", "Direction" };

            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(dim, name);
                if (TryConvertToVector(value, out up)) return true;
            }

            object attr = GetPropertyOrFieldValue(dim, "Attributes");
            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(attr, name);
                if (TryConvertToVector(value, out up)) return true;
            }

            object setAttr = GetPropertyOrFieldValue(dim, "DimensionSetAttributes");
            foreach (string name in names)
            {
                object value = GetPropertyOrFieldValue(setAttr, name);
                if (TryConvertToVector(value, out up)) return true;
            }

            return false;
        }

        private static bool TryConvertToVector(object value, out SimpleVector v)
        {
            v = new SimpleVector();
            if (value == null) return false;

            try
            {
                object xObj = GetPropertyOrFieldValue(value, "X");
                object yObj = GetPropertyOrFieldValue(value, "Y");
                if (xObj == null || yObj == null) return false;

                v.X = Convert.ToDouble(xObj);
                v.Y = Convert.ToDouble(yObj);
                return v.Length > 0.0001;
            }
            catch { return false; }
        }

        private struct SimpleVector
        {
            public double X;
            public double Y;

            public SimpleVector(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double Length { get { return Math.Sqrt(X * X + Y * Y); } }

            public void Normalize()
            {
                double len = Length;
                if (len < 0.0001) return;
                X /= len;
                Y /= len;
            }
        }

        private static bool TryReadDoublePropertyOrField(object obj, string name, out double value)
        {
            value = 0.0;
            object raw = GetPropertyOrFieldValue(obj, name);
            if (raw == null) return false;

            try
            {
                value = Convert.ToDouble(raw);
                return true;
            }
            catch { return false; }
        }

        private static bool TrySetDoublePropertyOrField(object obj, string name, double value)
        {
            if (obj == null) return false;
            Type type = obj.GetType();

            try
            {
                PropertyInfo prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanWrite && prop.GetIndexParameters().Length == 0)
                {
                    object converted = Convert.ChangeType(value, prop.PropertyType);
                    prop.SetValue(obj, converted, null);
                    return true;
                }
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    object converted = Convert.ChangeType(value, field.FieldType);
                    field.SetValue(obj, converted);
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static object GetPropertyOrFieldValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return null;
            Type type = obj.GetType();

            try
            {
                PropertyInfo prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(obj, null);
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(obj);
            }
            catch { }

            return null;
        }

        private static object InvokeNoArg(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(methodName)) return null;
            try
            {
                MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) return null;
                if (method.GetParameters().Length != 0) return null;
                return method.Invoke(obj, null);
            }
            catch { return null; }
        }

        private static int RuntimeHelpersHash(object obj)
        {
            if (obj == null) return 0;
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
