using System;
using System.Collections.Generic;
using System.Reflection;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using System.Collections;
using Tekla.Structures.Drawing.UI;

namespace TTSK_AutoDim_Plates
{
    public static class PHU_ArrangeView
    {
        private const double TOL = 1.0;

        // PHU SAFE ARRANGE AREA
        // Chừa vùng block dưới / block trên giống hướng Shape C.
        private const bool FORCE_SAFE_BY_TOP_BOTTOM_BLOCKS = true;
        private const double BOTTOM_BLOCK_HEIGHT_RATIO = 0.18;
        private const double TOP_BLOCK_HEIGHT_RATIO = 0.08;
        private const double BLOCK_EXTRA_GAP = 5.0;
        //Khoảng cách tối thiểu tới mép giấy.
        private const double MIN_EDGE_SAFE = 15.0;
        //Khoảng cách từ cụm dọc tới mép trên vùng khả dụng.
        private const double VERTICAL_TOP_INSET = 15.0;
        //Khoảng cách main thếp nhất đến sectuon cao nhất
        private const double HORIZONTAL_EXTRA_DOWN = 30.0;
        //Tỉ lệ view chiếm để đủ kích hoạt auto center
        private const double AUTO_CENTER_FILL_RATIO = 0.60;
        //Khoảng hở giữa cụm ngang và block GRID dưới.
        private const double HORIZONTAL_BLOCK_CLEARANCE = 40.0;
        //Né GRID
        private const double VERTICAL_GRID_CLEARANCE = 15.0;

        public class Result
        {
            public bool Success;
            public int MainCount;
            public int SectionCount;
            public string Message;

            public string ToDisplayText()
            {
                if (!string.IsNullOrEmpty(Message))
                    return Message;

                return Success
                    ? "Section: " + SectionCount
                    : "Arrange section view lỗi.";
            }
        }

        private class ViewBox
        {
            public View View;
            public double MinX;
            public double MaxX;
            public double MinY;
            public double MaxY;
            public double Width;
            public double Height;
            public double CenterX;
            public double CenterY;
            public string SortKey;
        }

        public static Result Run(bool sectionHorizontal, double gap)
        {
            return Run(false, sectionHorizontal, gap);
        }

        public static Result Run(bool mainHorizontal, bool sectionHorizontal, double gap)
        {
            return Run(mainHorizontal, sectionHorizontal, gap, false);
        }

        public static Result Run(bool mainHorizontal, bool sectionHorizontal, double gap, bool verticalBottomUp)
        {
            Result result = new Result();

            try
            {
                DrawingHandler dh = new DrawingHandler();
                Drawing drawing = dh.GetActiveDrawing();
                if (drawing == null)
                {
                    result.Success = false;
                    result.Message = "Không có active drawing.";
                    return result;
                }

                if (gap < 0.0)
                    gap = 0.0;

                List<View> allViews = GetAllViews(drawing);
                if (allViews.Count == 0)
                {
                    result.Success = false;
                    result.Message = "Không tìm thấy view.";
                    return result;
                }

                List<View> mainViews;
                List<View> regularSectionViews;
                ClassifyViews(allViews, out mainViews, out regularSectionViews);

                // SECTION ONLY: không move Main View / Top / Bottom / Front / special section.
                ArrangeRegularSectionViews(drawing, mainViews, regularSectionViews, sectionHorizontal, gap, verticalBottomUp);

                try { drawing.CommitChanges(); }
                catch { }

                SelectViews(dh, regularSectionViews);

                result.Success = true;
                result.MainCount = mainViews.Count;
                result.SectionCount = regularSectionViews.Count;
                result.Message = result.SectionCount > 0
                    ? "Đã arrange " + result.SectionCount + " section view."
                    : "Không tìm thấy section thường để arrange.";
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
        }

        private static List<View> GetAllViews(Drawing drawing)
        {
            List<View> views = new List<View>();

            try
            {
                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return views;

                DrawingObjectEnumerator e = sheet.GetAllViews();
                while (e.MoveNext())
                {
                    View v = e.Current as View;
                    if (v == null)
                        continue;

                    AddUniqueView(views, v);
                }
            }
            catch
            {
            }

            return views;
        }

        private static void ClassifyViews(
            List<View> allViews,
            out List<View> mainViews,
            out List<View> regularSectionViews)
        {
            mainViews = new List<View>();
            regularSectionViews = new List<View>();

            List<View> sectionViews = new List<View>();
            List<View> nonSectionViews = new List<View>();

            foreach (View v in allViews)
            {
                if (v == null)
                    continue;

                if (IsSectionView(v))
                    sectionViews.Add(v);
                else
                    nonSectionViews.Add(v);
            }

            foreach (View v in nonSectionViews)
                AddUniqueView(mainViews, v);

            View frontView = FindFrontView(nonSectionViews);
            if (frontView == null && nonSectionViews.Count > 0)
                frontView = FindWidestView(nonSectionViews);

            double frontWidth = GetBoxWidth(frontView);
            if (frontWidth <= 1.0)
                frontWidth = GetMaxViewWidth(nonSectionViews);

            foreach (View section in sectionViews)
            {
                // View tên A/B/C... hoặc A-A/B-B... là section thường chắc chắn.
                // Không cho rule hình học nhận nhầm I/J hoặc các section rộng thành special/main.
                if (IsNamedSectionView(section))
                    AddUniqueView(regularSectionViews, section);
                else if (IsSpecialTopBottomSection(section, frontView, frontWidth))
                    AddUniqueView(mainViews, section);
                else
                    AddUniqueView(regularSectionViews, section);
            }

            // Fallback: một số view section trong Tekla không trả ViewType/Name có chữ Section.
            // Khi đó chọn các view nhỏ/hẹp hơn Front làm section thường, nhưng vẫn không đụng main view lớn.
            if (regularSectionViews.Count == 0)
                AddRegularSectionsByGeometry(allViews, mainViews, regularSectionViews, frontView, frontWidth);

            mainViews.Sort(delegate (View a, View b)
            {
                ViewBox ba;
                ViewBox bb;
                if (!TryGetViewBox(a, out ba) || !TryGetViewBox(b, out bb))
                    return 0;

                int cy = bb.CenterY.CompareTo(ba.CenterY);
                if (cy != 0) return cy;
                return ba.CenterX.CompareTo(bb.CenterX);
            });

            regularSectionViews.Sort(CompareSectionViewsByNameThenPosition);
        }

        private static void AddRegularSectionsByGeometry(
            List<View> allViews,
            List<View> mainViews,
            List<View> regularSectionViews,
            View frontView,
            double frontWidth)
        {
            try
            {
                if (allViews == null)
                    return;

                if (frontWidth <= 1.0)
                    frontWidth = GetMaxViewWidth(allViews);

                foreach (View v in allViews)
                {
                    if (v == null)
                        continue;

                    if (IsKnownMainView(v))
                        continue;

                    ViewBox b;
                    if (!TryGetViewBox(v, out b))
                        continue;

                    // Section thường hay là mặt cắt nhỏ ở hai bên: hẹp hơn rõ so với front.
                    // Không dùng rule này để move Top/Bottom vì Top/Bottom thường dài gần Front.
                    bool narrowSection = frontWidth > 1.0 && b.Width <= frontWidth * 0.45;
                    bool tallSmallSection = b.Height > b.Width * 1.15 && b.Width <= frontWidth * 0.60;

                    if (!narrowSection && !tallSmallSection)
                        continue;

                    // Nếu view quá rộng gần Front thì xem như main/special, không move.
                    if (frontWidth > 1.0 && b.Width >= frontWidth * 0.55)
                    {
                        AddUniqueView(mainViews, v);
                        continue;
                    }

                    AddUniqueView(regularSectionViews, v);
                }
            }
            catch
            {
            }
        }

        private static bool IsKnownMainView(View view)
        {
            string type = GetViewTypeText(view);
            string name = SafeViewName(view);

            if (ContainsIgnoreCase(type, "Front") || ContainsIgnoreCase(name, "Front")) return true;
            if (ContainsIgnoreCase(type, "Top") || ContainsIgnoreCase(name, "Top")) return true;
            if (ContainsIgnoreCase(type, "Bottom") || ContainsIgnoreCase(name, "Bottom")) return true;

            return false;
        }

        private static bool IsSpecialTopBottomSection(View section, View frontView, double frontWidth)
        {
            try
            {
                if (section == null || frontView == null)
                    return false;

                ViewBox sb;
                ViewBox fb;
                if (!TryGetViewBox(section, out sb) || !TryGetViewBox(frontView, out fb))
                    return false;

                // Section đặc biệt Top/Bottom thường là section có bề ngang gần bằng Front
                // và nằm trên/dưới Front. Section thường A-A nhỏ hơn rõ rệt.
                if (frontWidth > 1.0 && sb.Width >= frontWidth * 0.55)
                {
                    if (sb.CenterY > fb.CenterY + Math.Min(20.0, Math.Max(5.0, fb.Height * 0.15)))
                        return true;

                    if (sb.CenterY < fb.CenterY - Math.Min(20.0, Math.Max(5.0, fb.Height * 0.15)))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void ArrangeRegularSectionViews(
            Drawing drawing,
            List<View> mainViews,
            List<View> sectionViews,
            bool horizontal,
            double gap,
            bool verticalBottomUp)
        {
            try
            {
                if (sectionViews == null || sectionViews.Count == 0)
                    return;

                if (gap < 0.0)
                    gap = 0.0;

                double arrangeGap = gap;
                double safeGap = Math.Max(gap, MIN_EDGE_SAFE);

                List<ViewBox> sectionBoxes = BuildBoxes(sectionViews);
                if (sectionBoxes.Count == 0)
                    return;

                sectionBoxes.Sort(CompareSectionBoxesByNameThenPosition);

                List<ViewBox> mainBoxes = BuildBoxes(mainViews);
                UsableRect usable = GetUsableRect(drawing, safeGap);

                ViewBox mainCluster = GetClusterBoxFromBoxes(mainBoxes);
                if (mainCluster == null)
                    mainCluster = sectionBoxes[0];

                ViewBox lowestMain = GetLowestMainBox(mainBoxes);
                ViewBox highestMain = GetHighestMainBox(mainBoxes);
                ViewBox rightMostMain = GetRightMostMainBox(mainBoxes);

                if (lowestMain == null) lowestMain = mainCluster;
                if (highestMain == null) highestMain = mainCluster;
                if (rightMostMain == null) rightMostMain = mainCluster;

                if (usable == null)
                {
                    usable = new UsableRect();
                    usable.Left = mainCluster.MinX - 200.0;
                    usable.Right = mainCluster.MaxX + 300.0;
                    usable.Bottom = mainCluster.MinY - 200.0;
                    usable.Top = mainCluster.MaxY + 200.0;
                }

                if (horizontal)
                {
                    // PHU HORIZONTAL SAFE:
                    // 1) Ưu tiên dàn từ bên trái usable.Left, không canh giữa / không dồn qua phải.
                    // 2) Nếu hàng quá dài thì tự xuống hàng, không vượt margin / title block.
                    // 3) Toàn bộ cụm đặt dưới MAIN VIEW thấp nhất, nếu đụng block thì đẩy lên khỏi block.
                    ArrangeHorizontalLeftPriority(sectionBoxes, mainBoxes, rightMostMain, highestMain, lowestMain, usable, arrangeGap);
                }
                else
                {
                    // PHU VERTICAL SAFE:
                    // 1) Bắt đầu bên phải main view xa nhất + safeGap.
                    // 2) Cách margin phải / top / bottom xa hơn.
                    // 3) Nếu dự kiến chạm main view thì tự dịch sang phải thêm safeGap.
                    ArrangeVerticalRightOfMain(sectionBoxes, mainBoxes, rightMostMain, highestMain, usable, arrangeGap, verticalBottomUp);
                }
            }
            catch
            {
            }
        }

        private class ArrangeRow
        {
            public List<ViewBox> Items = new List<ViewBox>();
            public double Width;
            public double Height;
        }

        private static void ArrangeHorizontalLeftPriority(
            List<ViewBox> sectionBoxes,
            List<ViewBox> mainBoxes,
            ViewBox rightMostMain,
            ViewBox highestMain,
            ViewBox lowestMain,
            UsableRect usable,
            double gap)
        {
            if (sectionBoxes == null || sectionBoxes.Count == 0 || lowestMain == null || usable == null)
                return;

            // PHU FIX GAP:
            // Chế độ ngang chỉ được có 1 hàng dưới main.
            // Khi hết chỗ, phần dư chuyển sang vùng phải và xếp 2 cột bottom-up.
            // Không cho phần dư và hàng ngang dùng chung vùng X để tránh chồng khung xanh.
            if (rightMostMain == null)
                rightMostMain = lowestMain;
            if (highestMain == null)
                highestMain = lowestMain;

            double rightAreaLeft = rightMostMain.MaxX + gap;
            double horizontalRightLimit = Math.Min(usable.Right, rightAreaLeft - gap);

            // Nếu vùng trái quá nhỏ thì vẫn cho hàng ngang dùng usable rộng nhất,
            // nhưng khi có overflow thì phần dư sẽ được ép sang phải main.
            if (horizontalRightLimit <= usable.Left + 1.0)
                horizontalRightLimit = usable.Right;

            double firstRowWidthLimit = Math.Max(1.0, horizontalRightLimit - usable.Left);

            List<ViewBox> firstRow = new List<ViewBox>();
            List<ViewBox> overflow = new List<ViewBox>();

            double usedWidth = 0.0;
            bool overflowStarted = false;

            for (int i = 0; i < sectionBoxes.Count; i++)
            {
                ViewBox b = sectionBoxes[i];
                if (b == null)
                    continue;

                double addWidth = firstRow.Count == 0 ? b.Width : gap + b.Width;

                // Sau khi đã hết chỗ một lần, toàn bộ view còn lại phải qua vùng phải.
                // Nếu cho view sau quay lại hàng ngang thì thứ tự và gap sẽ rất dễ chồng nhau.
                if (overflowStarted || (firstRow.Count > 0 && usedWidth + addWidth > firstRowWidthLimit))
                {
                    overflowStarted = true;
                    overflow.Add(b);
                }
                else
                {
                    firstRow.Add(b);
                    usedWidth += addWidth;
                }
            }

            // Nếu view đầu tiên rộng hơn vùng trái thì vẫn đặt nó ở hàng ngang,
            // không để toàn bộ list qua overflow.
            if (firstRow.Count == 0 && overflow.Count > 0)
            {
                firstRow.Add(overflow[0]);
                overflow.RemoveAt(0);
            }

            if (firstRow.Count > 0)
            {
                double rowHeight = GetMaxHeight(firstRow);
                // PHU: dàn ngang dịch xuống thêm 30mm để tận dụng vùng trống phía dưới.
                // Vẫn clamp theo usable.Bottom nên không đụng title block / margin.
                double targetBottom = lowestMain.MinY - gap - rowHeight - HORIZONTAL_EXTRA_DOWN;

                // PHU FIX: bản trước có trừ HORIZONTAL_EXTRA_DOWN nhưng bị clamp lại bởi usable.Bottom,
                // nên thực tế không dịch xuống được. Cho phép dàn ngang dùng thêm 30mm vùng trống
                // phía trên title block / block dưới, nhưng chỉ áp dụng cho HORIZONTAL.
                double horizontalBottomLimit = usable.Bottom - HORIZONTAL_EXTRA_DOWN;
                if (horizontalBottomLimit < 0.0)
                    horizontalBottomLimit = 0.0;

                targetBottom = Math.Max(targetBottom, horizontalBottomLimit);
                if (targetBottom + rowHeight > usable.Top)
                    targetBottom = Math.Max(horizontalBottomLimit, usable.Top - rowHeight);

                double x = usable.Left;

                // PHU AUTO CENTER HORIZONTAL:
                // Chỉ canh giữa cụm dàn ngang khi cụm chiếm ít vùng đặt được.
                // Không sửa thuật toán overflow/dàn dọc phía sau.
                double rowWidth = GetTotalRowWidth(firstRow, gap);
                double horizontalAreaWidth = Math.Max(1.0, firstRowWidthLimit);
                if (rowWidth > 0.0 && rowWidth < horizontalAreaWidth * AUTO_CENTER_FILL_RATIO)
                {
                    // PHU FEW SECTION CENTER BY MAIN ORIGIN:
                    // Nếu 2 main gốc tọa độ gần như cùng X và khác Y => main đang xếp dọc,
                    // canh cụm section theo tâm main thấp nhất/gần section nhất.
                    // Ngược lại giữ nguyên thuật toán base hiện tại.
                    if (IsMainLayoutVerticalByOrigins(mainBoxes))
                    {
                        x = lowestMain.CenterX - rowWidth * 0.5;

                        if (x < usable.Left)
                            x = usable.Left;

                        double maxX = horizontalRightLimit - rowWidth;
                        if (maxX < usable.Left)
                            maxX = usable.Left;

                        if (x > maxX)
                            x = maxX;
                    }
                    else
                    {
                        x = usable.Left + (horizontalAreaWidth - rowWidth) * 0.5;
                    }
                }

                for (int i = 0; i < firstRow.Count; i++)
                {
                    ViewBox b = firstRow[i];
                    double targetCenterX = x + b.Width * 0.5;
                    double targetCenterY = targetBottom + b.Height * 0.5;
                    MoveViewBySheetDelta(b.View, targetCenterX - b.CenterX, targetCenterY - b.CenterY);
                    x += b.Width + gap;
                }
            }

            if (overflow.Count > 0)
            {
                ArrangeVerticalBottomUpRightOfMain(
                    overflow,
                    mainBoxes,
                    rightMostMain,
                    highestMain,
                    usable,
                    gap);
            }
        }

        private static void ArrangeVerticalBottomUpRightOfMain(
            List<ViewBox> sectionBoxes,
            List<ViewBox> mainBoxes,
            ViewBox rightMostMain,
            ViewBox highestMain,
            UsableRect usable,
            double gap)
        {
            if (sectionBoxes == null || sectionBoxes.Count == 0 || rightMostMain == null || usable == null)
                return;

            // PHU FIX GAP CHỒNG VIEW:
            // Tính cột bằng width lớn nhất của từng cột, hàng bằng height lớn nhất của từng hàng.
            // Sau đó đặt từng view theo left/top của ô lưới, tuyệt đối không dùng center cũ để suy ra gap.
            double col0Width = GetColumnWidth(sectionBoxes, 0);
            double col1Width = GetColumnWidth(sectionBoxes, 1);
            if (col1Width <= 0.0)
                col1Width = 0.0;

            double gridWidth = col0Width + (sectionBoxes.Count > 1 ? gap + col1Width : 0.0);

            int rows = (sectionBoxes.Count + 1) / 2;
            double[] rowHeights = new double[rows];
            for (int i = 0; i < sectionBoxes.Count; i++)
            {
                int row = i / 2;
                if (sectionBoxes[i].Height > rowHeights[row])
                    rowHeights[row] = sectionBoxes[i].Height;
            }

            double gridHeight = 0.0;
            for (int i = 0; i < rows; i++)
            {
                if (i > 0) gridHeight += gap;
                gridHeight += rowHeights[i];
            }

            double preferredLeft = rightMostMain.MaxX + gap;
            double col0Left = preferredLeft;

            // Không kéo cụm overflow vào vùng main nếu còn có thể giữ bên phải main.
            if (col0Left + gridWidth > usable.Right)
                col0Left = Math.Max(preferredLeft, usable.Right - gridWidth);
            if (col0Left < usable.Left)
                col0Left = usable.Left;

            double bottomLimit = usable.Bottom + HORIZONTAL_BLOCK_CLEARANCE;
            if (bottomLimit > usable.Top - 1.0)
                bottomLimit = usable.Bottom;

            double bottom = bottomLimit;
            if (bottom + gridHeight > usable.Top)
                bottom = Math.Max(bottomLimit, usable.Top - gridHeight);

            // Nếu vùng dự kiến đụng main, dịch sang phải theo đúng gap.
            double testLeft = col0Left;
            double testRight = testLeft + gridWidth;
            double testBottom = bottom;
            double testTop = bottom + gridHeight;

            int guard = 0;
            while (IntersectsAnyMain(testLeft, testRight, testBottom, testTop, mainBoxes) && guard < 50)
            {
                guard++;
                if (testRight + gap <= usable.Right)
                {
                    testLeft += gap;
                    testRight += gap;
                    col0Left = testLeft;
                }
                else
                {
                    break;
                }
            }

            double col1Left = col0Left + col0Width + gap;
            double yBottom = bottom;

            for (int row = 0; row < rows; row++)
            {
                double rowBottom = yBottom;

                for (int col = 0; col < 2; col++)
                {
                    int index = row * 2 + col;
                    if (index >= sectionBoxes.Count)
                        continue;

                    ViewBox b = sectionBoxes[index];
                    double cellLeft = col == 0 ? col0Left : col1Left;

                    // Căn giữa view trong ô cột của nó để view nhỏ không bị lệch khó nhìn,
                    // nhưng khoảng cách giữa 2 ô vẫn luôn >= gap.
                    double cellWidth = col == 0 ? col0Width : col1Width;
                    double targetLeft = cellLeft;
                    double targetBottom = rowBottom;

                    double targetCenterX = targetLeft + b.Width * 0.5;
                    double targetCenterY = targetBottom + b.Height * 0.5;
                    MoveViewBySheetDelta(b.View, targetCenterX - b.CenterX, targetCenterY - b.CenterY);
                }

                yBottom += rowHeights[row] + gap;
            }
        }

        private static bool IsMainLayoutVerticalByOrigins(List<ViewBox> mainBoxes)
        {
            try
            {
                if (mainBoxes == null || mainBoxes.Count < 2)
                    return false;

                ViewBox a = null;
                ViewBox b = null;

                for (int i = 0; i < mainBoxes.Count; i++)
                {
                    if (mainBoxes[i] == null || mainBoxes[i].View == null)
                        continue;

                    if (a == null)
                        a = mainBoxes[i];
                    else
                    {
                        b = mainBoxes[i];
                        break;
                    }
                }

                if (a == null || b == null)
                    return false;

                Point oa = null;
                Point ob = null;

                try { oa = a.View.Origin; } catch { oa = null; }
                try { ob = b.View.Origin; } catch { ob = null; }

                double ax = oa != null ? oa.X : a.CenterX;
                double ay = oa != null ? oa.Y : a.CenterY;
                double bx = ob != null ? ob.X : b.CenterX;
                double by = ob != null ? ob.Y : b.CenterY;

                double dx = Math.Abs(ax - bx);
                double dy = Math.Abs(ay - by);
                double tol = 5.0;

                // Cùng X, khác Y rõ ràng => layout main dọc.
                if (dx <= tol && dy > tol)
                    return true;

                // Cùng Y, khác X rõ ràng => layout main ngang, giữ base.
                if (dy <= tol && dx > tol)
                    return false;
            }
            catch
            {
            }

            return false;
        }

        private static double GetMaxHeight(List<ViewBox> boxes)
        {
            double max = 0.0;
            if (boxes == null)
                return max;

            foreach (ViewBox b in boxes)
            {
                if (b != null && b.Height > max)
                    max = b.Height;
            }
            return max;
        }

        private static List<ArrangeRow> BuildRowsByWidth(List<ViewBox> boxes, double usableWidth, double gap)
        {
            List<ArrangeRow> rows = new List<ArrangeRow>();
            ArrangeRow row = new ArrangeRow();

            foreach (ViewBox b in boxes)
            {
                if (b == null)
                    continue;

                double addWidth = row.Items.Count == 0 ? b.Width : gap + b.Width;

                if (row.Items.Count > 0 && row.Width + addWidth > usableWidth)
                {
                    rows.Add(row);
                    row = new ArrangeRow();
                    addWidth = b.Width;
                }

                row.Items.Add(b);
                row.Width += addWidth;
                if (b.Height > row.Height)
                    row.Height = b.Height;
            }

            if (row.Items.Count > 0)
                rows.Add(row);

            return rows;
        }

        private static void SelectViews(DrawingHandler dh, List<View> views)
        {
            try
            {
                if (dh == null || views == null || views.Count == 0)
                    return;

                DrawingObjectSelector selector = dh.GetDrawingObjectSelector();
                if (selector == null)
                    return;

                ArrayList arr = new ArrayList();

                foreach (View v in views)
                {
                    if (v != null)
                        arr.Add(v);
                }

                selector.SelectObjects(arr, false);
            }
            catch
            {
            }
        }

        private static double GetRowsTotalHeight(List<ArrangeRow> rows, double gap)
        {
            double h = 0.0;
            if (rows == null)
                return h;

            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) h += gap;
                h += rows[i].Height;
            }
            return h;
        }

        private static void ArrangeVerticalRightOfMain(
            List<ViewBox> sectionBoxes,
            List<ViewBox> mainBoxes,
            ViewBox rightMostMain,
            ViewBox highestMain,
            UsableRect usable,
            double gap,
            bool verticalBottomUp)
        {
            if (sectionBoxes == null || sectionBoxes.Count == 0 || rightMostMain == null || highestMain == null || usable == null)
                return;

            UsableRect verticalUsable = new UsableRect();
            verticalUsable.Left = usable.Left;
            verticalUsable.Right = usable.Right;
            verticalUsable.Bottom = usable.Bottom + VERTICAL_GRID_CLEARANCE;
            verticalUsable.Top = usable.Top;
            if (verticalUsable.Bottom > verticalUsable.Top - 1.0)
                verticalUsable.Bottom = usable.Bottom;

            // PHU VERTICAL FINAL:
            // Bên phải luôn là layout chính 2 x X.
            // Không được phá thành 1 hàng ngang nếu vùng phải còn chứa được.
            // Nếu tổng section quá cao và phần dưới sẽ đụng grid/title block,
            // chỉ phần DƯ bị tràn mới đưa xuống hàng ngang dưới main.
            // Nếu cụm dọc đụng main view, ưu tiên dịch nguyên cụm sang phải theo gap.

            // PHU: dàn dọc luôn ưu tiên đẩy cụm 2 cột lên vùng trống phía trên.
            // Trước đây nhiều section bị khóa theo highestMain.MaxY nên phía trên còn trống nhiều.
            // usable.Top đã né margin / block trên nên vẫn an toàn.
            double preferredTop = verticalUsable.Top;

            preferredTop = Clamp(preferredTop, verticalUsable.Bottom, verticalUsable.Top);

            int keepCount = sectionBoxes.Count;
            double keepLeft = 0.0;
            double keepTop = 0.0;
            double keepGridWidth = 0.0;
            double keepGridHeight = 0.0;

            // Thử giữ nhiều section nhất có thể trong vùng phải 2 cột.
            // Luôn giảm theo cặp để không làm vỡ quy tắc 2 x X.
            while (keepCount > 0)
            {
                if (TryFindVerticalPlacement(
                    sectionBoxes,
                    keepCount,
                    mainBoxes,
                    rightMostMain,
                    verticalUsable,
                    gap,
                    preferredTop,
                    out keepLeft,
                    out keepTop,
                    out keepGridWidth,
                    out keepGridHeight))
                {
                    break;
                }

                keepCount -= 2;
            }

            if (keepCount < 0)
                keepCount = 0;

            if (keepCount > 0)
            {
                PlaceVerticalPrefix(
                    sectionBoxes,
                    keepCount,
                    keepLeft,
                    keepTop,
                    gap,
                    verticalBottomUp);
            }

            if (keepCount < sectionBoxes.Count)
            {
                List<ViewBox> overflow = new List<ViewBox>();
                for (int i = keepCount; i < sectionBoxes.Count; i++)
                {
                    if (sectionBoxes[i] != null)
                        overflow.Add(sectionBoxes[i]);
                }

                // Phần dư đưa xuống dưới và dàn ngang giống mode ngang:
                // dưới main thấp nhất, có HORIZONTAL_EXTRA_DOWN, vẫn né block/grid bằng usable.Bottom.
                ViewBox lowestMain = GetLowestMainBox(mainBoxes);
                if (lowestMain == null)
                    lowestMain = rightMostMain;

                ArrangeBottomOverflowRow(
                    overflow,
                    lowestMain,
                    verticalUsable,
                    gap,
                    keepLeft);
            }
        }

        private static bool TryFindVerticalPlacement(
            List<ViewBox> boxes,
            int count,
            List<ViewBox> mainBoxes,
            ViewBox rightMostMain,
            UsableRect usable,
            double gap,
            double preferredTop,
            out double col0Left,
            out double rowTop,
            out double gridWidth,
            out double gridHeight)
        {
            col0Left = 0.0;
            rowTop = 0.0;
            gridWidth = 0.0;
            gridHeight = 0.0;

            if (boxes == null || count <= 0 || rightMostMain == null || usable == null)
                return false;

            gridWidth = GetColumnWidthForPrefix(boxes, count, 0);
            double col1Width = GetColumnWidthForPrefix(boxes, count, 1);
            if (count > 1)
                gridWidth += gap + col1Width;

            gridHeight = GetVerticalGridHeightForPrefix(boxes, count, gap);

            // Không đủ chiều cao vùng an toàn thì prefix này phải giảm bớt.
            if (gridHeight > (usable.Top - usable.Bottom) + 0.01)
                return false;

            double minTop = usable.Bottom + gridHeight;
            double maxTop = usable.Top;
            if (minTop > maxTop)
                return false;

            // PHU AUTO CENTER VERTICAL:
            // Chỉ canh giữa cụm dàn dọc khi cụm chiếm ít chiều cao vùng usable.
            // Không sửa thuật toán giảm keepCount / overflow ngang phía sau.
            double usableHeight = Math.Max(1.0, usable.Top - usable.Bottom);
            double targetPreferredTop = preferredTop;
            if (gridHeight > 0.0 && gridHeight < usableHeight * AUTO_CENTER_FILL_RATIO)
                targetPreferredTop = usable.Bottom + (usableHeight + gridHeight) * 0.5;

            rowTop = Clamp(targetPreferredTop, minTop, maxTop);

            double preferredLeft = rightMostMain.MaxX + gap;
            double minLeft = usable.Left;
            double maxLeft = usable.Right - gridWidth;
            if (maxLeft < minLeft)
                return false;

            col0Left = preferredLeft;
            if (col0Left < minLeft)
                col0Left = minLeft;

            // Nếu bên phải không đủ, cho phép kéo về trong giấy nhưng không được chui vào main.
            if (col0Left > maxLeft)
                col0Left = maxLeft;

            double testLeft = col0Left;
            double testRight = testLeft + gridWidth;
            double testBottom = rowTop - gridHeight;
            double testTop = rowTop;

            // Nếu chạm main thì đẩy nguyên cụm sang phải từng gap.
            int guard = 0;
            while (IntersectsAnyMain(testLeft, testRight, testBottom, testTop, mainBoxes) && guard < 80)
            {
                guard++;
                if (testRight + gap <= usable.Right)
                {
                    testLeft += gap;
                    testRight += gap;
                    col0Left = testLeft;
                }
                else
                {
                    return false;
                }
            }

            // Sau khi đẩy vẫn phải nằm trong usable, không đụng grid/title block.
            if (testBottom < usable.Bottom - 0.01)
                return false;
            if (testTop > usable.Top + 0.01)
                return false;
            if (testLeft < usable.Left - 0.01 || testRight > usable.Right + 0.01)
                return false;

            return true;
        }

        private static void PlaceVerticalPrefix(
            List<ViewBox> boxes,
            int count,
            double col0Left,
            double rowTop,
            double gap,
            bool verticalBottomUp)
        {
            if (boxes == null || count <= 0)
                return;

            double col0Width = GetColumnWidthForPrefix(boxes, count, 0);
            double col1Left = col0Left + col0Width + gap;

            int rows = (count + 1) / 2;
            double[] rowHeights = GetRowHeightsForPrefix(boxes, count);

            if (verticalBottomUp)
            {
                double totalHeight = 0.0;
                for (int i = 0; i < rows; i++)
                {
                    if (i > 0) totalHeight += gap;
                    totalHeight += rowHeights[i];
                }

                double yBottom = rowTop - totalHeight;
                for (int row = 0; row < rows; row++)
                {
                    double rowBottomY = yBottom;

                    for (int col = 0; col < 2; col++)
                    {
                        int index = row * 2 + col;
                        if (index >= count || index >= boxes.Count)
                            continue;

                        ViewBox b = boxes[index];
                        if (b == null)
                            continue;

                        double left = col == 0 ? col0Left : col1Left;
                        double cellWidth = col == 0 ? col0Width : GetColumnWidthForPrefix(boxes, count, 1);
                        double targetLeft = left;
                        double targetBottom = rowBottomY;

                        double targetCenterX = targetLeft + b.Width * 0.5;
                        double targetCenterY = targetBottom + b.Height * 0.5;
                        MoveViewBySheetDelta(b.View, targetCenterX - b.CenterX, targetCenterY - b.CenterY);
                    }

                    yBottom += rowHeights[row] + gap;
                }
            }
            else
            {
                double yTop = rowTop;
                for (int row = 0; row < rows; row++)
                {
                    double rowBottomY = yTop - rowHeights[row];

                    for (int col = 0; col < 2; col++)
                    {
                        int index = row * 2 + col;
                        if (index >= count || index >= boxes.Count)
                            continue;

                        ViewBox b = boxes[index];
                        if (b == null)
                            continue;

                        double left = col == 0 ? col0Left : col1Left;
                        double cellWidth = col == 0 ? col0Width : GetColumnWidthForPrefix(boxes, count, 1);
                        double targetLeft = left;
                        double targetBottom = rowBottomY;

                        double targetCenterX = targetLeft + b.Width * 0.5;
                        double targetCenterY = targetBottom + b.Height * 0.5;
                        MoveViewBySheetDelta(b.View, targetCenterX - b.CenterX, targetCenterY - b.CenterY);
                    }

                    yTop = rowBottomY - gap;
                }
            }
        }

        private static void ArrangeBottomOverflowRow(
            List<ViewBox> overflow,
            ViewBox lowestMain,
            UsableRect usable,
            double gap,
            double rightClusterLeft)
        {
            if (overflow == null || overflow.Count == 0 || lowestMain == null || usable == null)
                return;

            double rowHeight = GetMaxHeight(overflow);
            double targetBottom = lowestMain.MinY - gap - rowHeight - HORIZONTAL_EXTRA_DOWN;

            double horizontalBottomLimit = usable.Bottom - HORIZONTAL_EXTRA_DOWN;
            if (horizontalBottomLimit < 0.0)
                horizontalBottomLimit = 0.0;

            targetBottom = Math.Max(targetBottom, horizontalBottomLimit);
            if (targetBottom + rowHeight > usable.Top)
                targetBottom = Math.Max(horizontalBottomLimit, usable.Top - rowHeight);

            double x = usable.Left;

            // Nếu hàng dưới tiến gần cụm dọc bên phải, vẫn giữ gap tối thiểu.
            double rightLimit = rightClusterLeft - gap;
            if (rightLimit <= usable.Left + 1.0)
                rightLimit = usable.Right;

            for (int i = 0; i < overflow.Count; i++)
            {
                ViewBox b = overflow[i];
                if (b == null)
                    continue;

                // PHU: không wrap về usable.Left trên cùng một hàng.
                // Wrap cũ làm các view phía dưới chồng lên nhau khi vùng trái hẹp.
                // Cứ đi tiếp theo đúng width + gap để luôn giữ Gap View.
                double targetCenterX = x + b.Width * 0.5;
                double targetCenterY = targetBottom + b.Height * 0.5;
                MoveViewBySheetDelta(b.View, targetCenterX - b.CenterX, targetCenterY - b.CenterY);

                x += b.Width + gap;
            }
        }

        private static double GetColumnWidthForPrefix(List<ViewBox> boxes, int count, int col)
        {
            double max = 0.0;
            if (boxes == null)
                return max;

            int n = Math.Min(count, boxes.Count);
            for (int i = col; i < n; i += 2)
            {
                if (boxes[i] != null && boxes[i].Width > max)
                    max = boxes[i].Width;
            }
            return max;
        }

        private static double[] GetRowHeightsForPrefix(List<ViewBox> boxes, int count)
        {
            int rows = (count + 1) / 2;
            if (rows < 1) rows = 1;

            double[] rowHeights = new double[rows];
            if (boxes == null)
                return rowHeights;

            int n = Math.Min(count, boxes.Count);
            for (int i = 0; i < n; i++)
            {
                int row = i / 2;
                if (boxes[i] != null && boxes[i].Height > rowHeights[row])
                    rowHeights[row] = boxes[i].Height;
            }

            return rowHeights;
        }

        private static double GetVerticalGridHeightForPrefix(List<ViewBox> boxes, int count, double gap)
        {
            double[] rowHeights = GetRowHeightsForPrefix(boxes, count);
            double h = 0.0;
            for (int i = 0; i < rowHeights.Length; i++)
            {
                if (i > 0) h += gap;
                h += rowHeights[i];
            }
            return h;
        }

        private static bool IntersectsAnyMain(double minX, double maxX, double minY, double maxY, List<ViewBox> mainBoxes)
        {
            if (mainBoxes == null)
                return false;

            foreach (ViewBox m in mainBoxes)
            {
                if (m == null)
                    continue;

                if (maxX <= m.MinX || minX >= m.MaxX || maxY <= m.MinY || minY >= m.MaxY)
                    continue;

                return true;
            }

            return false;
        }

        private static int CompareSectionViewsByNameThenPosition(View a, View b)
        {
            string ka = GetSectionSortKey(a);
            string kb = GetSectionSortKey(b);

            int nameCompare = CompareSectionKeys(ka, kb);
            if (nameCompare != 0)
                return nameCompare;

            ViewBox ba;
            ViewBox bb;
            if (!TryGetViewBox(a, out ba) || !TryGetViewBox(b, out bb))
                return 0;

            int cx = ba.CenterX.CompareTo(bb.CenterX);
            if (cx != 0) return cx;
            return bb.CenterY.CompareTo(ba.CenterY);
        }

        private static int CompareSectionBoxesByNameThenPosition(ViewBox a, ViewBox b)
        {
            string ka = a == null ? "" : a.SortKey;
            string kb = b == null ? "" : b.SortKey;

            int nameCompare = CompareSectionKeys(ka, kb);
            if (nameCompare != 0)
                return nameCompare;

            if (a == null || b == null)
                return 0;

            int cx = a.CenterX.CompareTo(b.CenterX);
            if (cx != 0) return cx;
            return b.CenterY.CompareTo(a.CenterY);
        }

        private static int CompareSectionKeys(string a, string b)
        {
            bool hasA = !string.IsNullOrEmpty(a);
            bool hasB = !string.IsNullOrEmpty(b);

            if (hasA && hasB)
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

            if (hasA && !hasB)
                return -1;

            if (!hasA && hasB)
                return 1;

            return 0;
        }

        private static string GetSectionSortKey(View view)
        {
            // PHU OPTIMIZE:
            // Các dump đã xác nhận title A-A không nằm trong object bên trong SectionView.
            // File nền hiện tại đang sort đúng theo View.Name/ViewName.
            // Vì vậy chỉ đọc tên view trực tiếp, KHÔNG quét view.GetAllObjects() nữa.
            // Việc quét object + reflection là nguyên nhân bấm Arrange bị chậm vài giây.
            try
            {
                string name = SafeViewName(view);
                if (string.IsNullOrWhiteSpace(name))
                    return "";

                name = name.Trim();

                string key = ExtractSectionLetterKey(name, true);
                if (!string.IsNullOrEmpty(key))
                    return key;

                key = ExtractSectionLetterKey(name, false);
                if (!string.IsNullOrEmpty(key))
                    return key;

                return name.ToUpperInvariant();
            }
            catch
            {
                return "";
            }
        }

        private static void AddSectionTextCandidatesFromViewObjects(View view, List<string> candidates)
        {
            // PHU OPTIMIZE: giữ hàm để không phá cấu trúc file, nhưng không dùng nữa.
            // Không quét object trong view để tránh chậm.
            return;
        }

        private static bool MoveNextObject(object enumerator)
        {
            try
            {
                MethodInfo m = enumerator.GetType().GetMethod("MoveNext");
                if (m == null)
                    return false;

                object value = m.Invoke(enumerator, null);
                return value is bool && (bool)value;
            }
            catch
            {
                return false;
            }
        }

        private static object GetCurrentObject(object enumerator)
        {
            try
            {
                PropertyInfo p = enumerator.GetType().GetProperty("Current");
                if (p != null && p.CanRead)
                    return p.GetValue(enumerator, null);
            }
            catch
            {
            }

            return null;
        }

        private static void AddCandidate(List<string> candidates, string text)
        {
            if (candidates == null || string.IsNullOrEmpty(text))
                return;

            text = text.Trim();
            if (text.Length == 0)
                return;

            candidates.Add(text);
        }

        private static string PickBestSectionKey(List<string> candidates)
        {
            if (candidates == null)
                return "";

            foreach (string c in candidates)
            {
                string key = ExtractSectionLetterKey(c, true);
                if (!string.IsNullOrEmpty(key))
                    return key;
            }

            foreach (string c in candidates)
            {
                string key = ExtractSectionLetterKey(c, false);
                if (!string.IsNullOrEmpty(key))
                    return key;
            }

            return "";
        }

        private static string ExtractSectionLetterKey(string text, bool requirePair)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            string upper = text.ToUpperInvariant();

            for (int i = 0; i < upper.Length; i++)
            {
                char ch = upper[i];
                if (ch < 'A' || ch > 'Z')
                    continue;

                if (requirePair)
                {
                    bool hasPairSeparator = false;
                    for (int j = i + 1; j < upper.Length; j++)
                    {
                        char cj = upper[j];
                        if (cj == '-' || cj == '－' || cj == '–' || cj == '_' || cj == ' ' || cj == '=')
                            hasPairSeparator = true;

                        if (cj == ch && hasPairSeparator)
                            return ch.ToString();
                    }
                }
                else
                {
                    return ch.ToString();
                }
            }

            return "";
        }


        private class UsableRect
        {
            public double Left;
            public double Right;
            public double Bottom;
            public double Top;
        }

        private static UsableRect GetUsableRect(Drawing drawing, double gap)
        {
            try
            {
                if (drawing == null)
                    return null;

                ContainerView sheet = drawing.GetSheet();
                if (sheet == null)
                    return null;

                double paperW, paperH;
                GetPaperSize(drawing, sheet, out paperW, out paperH);

                UsableRect rect;
                if (!TryGetReservedRectByPaperSize(paperW, paperH, out rect))
                {
                    if (!TryFindInnerFrameRect(sheet, paperW, paperH, out rect))
                    {
                        rect = new UsableRect();
                        rect.Left = 5.0;
                        rect.Bottom = 5.0;
                        rect.Right = Math.Max(rect.Left + 1.0, paperW - 15.0);
                        rect.Top = Math.Max(rect.Bottom + 1.0, paperH - 5.0);
                    }
                }

                // Trừ title block phía dưới nếu tìm được đường biên trên của block.
                // Đây là nguyên nhân section bị rơi vào khung block dù vẫn còn trong margin giấy.
                double titleTop;
                if (TryFindTitleBlockTop(sheet, rect, paperW, paperH, out titleTop))
                {
                    double safeBottom = titleTop + Math.Max(BLOCK_EXTRA_GAP, gap);
                    if (safeBottom < rect.Top - 1.0)
                        rect.Bottom = Math.Max(rect.Bottom, safeBottom);
                }

                // PHU BLOCK RESERVE giống Shape C:
                // Nếu không bắt được template/title block bằng Line, vẫn ép vùng an toàn theo % khổ giấy.
                // Tránh section nằm đè block dưới hoặc sát block trên.
                if (FORCE_SAFE_BY_TOP_BOTTOM_BLOCKS)
                {
                    double h = rect.Top - rect.Bottom;
                    double bottomByRatio = rect.Bottom + h * BOTTOM_BLOCK_HEIGHT_RATIO + BLOCK_EXTRA_GAP;
                    double topByRatio = rect.Top - h * TOP_BLOCK_HEIGHT_RATIO - BLOCK_EXTRA_GAP;

                    if (bottomByRatio < rect.Top - 1.0)
                        rect.Bottom = Math.Max(rect.Bottom, bottomByRatio);

                    if (topByRatio > rect.Bottom + 1.0)
                        rect.Top = Math.Min(rect.Top, topByRatio);
                }

                // Chừa thêm mép trái/phải cho dễ nhìn, đặc biệt dàn dọc bên phải.
                rect.Left += MIN_EDGE_SAFE;
                rect.Right -= MIN_EDGE_SAFE;
                rect.Bottom += MIN_EDGE_SAFE * 0.5;
                rect.Top -= MIN_EDGE_SAFE * 0.5;

                if (rect.Right <= rect.Left + 1.0 || rect.Top <= rect.Bottom + 1.0)
                    return null;

                return rect;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetReservedRectByPaperSize(double paperW, double paperH, out UsableRect rect)
        {
            rect = null;

            if (IsPaperSize(paperW, paperH, 420.0, 297.0))
            {
                rect = BuildReservedRect(paperW, paperH, 5.0, 15.0, 5.0, 5.0);
                return true;
            }

            if (IsPaperSize(paperW, paperH, 841.0, 594.0))
            {
                rect = BuildReservedRect(paperW, paperH, 10.0, 17.3, 10.0, 10.0);
                return true;
            }

            return false;
        }

        private static bool IsPaperSize(double paperW, double paperH, double targetW, double targetH)
        {
            const double tol = 2.0;
            bool same = Math.Abs(paperW - targetW) <= tol && Math.Abs(paperH - targetH) <= tol;
            bool swapped = Math.Abs(paperW - targetH) <= tol && Math.Abs(paperH - targetW) <= tol;
            return same || swapped;
        }

        private static UsableRect BuildReservedRect(
            double paperW,
            double paperH,
            double leftReserve,
            double rightReserve,
            double bottomReserve,
            double topReserve)
        {
            UsableRect rect = new UsableRect();
            rect.Left = leftReserve;
            rect.Bottom = bottomReserve;
            rect.Right = Math.Max(rect.Left + 1.0, paperW - rightReserve);
            rect.Top = Math.Max(rect.Bottom + 1.0, paperH - topReserve);
            return rect;
        }

        private static bool TryFindInnerFrameRect(ContainerView sheet, double paperW, double paperH, out UsableRect rect)
        {
            rect = null;

            try
            {
                List<double> xs = new List<double>();
                List<double> ys = new List<double>();

                DrawingObjectEnumerator e = sheet.GetAllObjects(typeof(Tekla.Structures.Drawing.Line));
                while (e.MoveNext())
                {
                    Tekla.Structures.Drawing.Line ln = e.Current as Tekla.Structures.Drawing.Line;
                    if (ln == null) continue;

                    Point a, b;
                    if (!TryGetLinePoints(ln, out a, out b)) continue;

                    bool vertical = Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 20.0;
                    bool horizontal = Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.X - b.X) > 20.0;

                    if (vertical)
                    {
                        double x = (a.X + b.X) / 2.0;
                        if (x > 0.5 && x < paperW - 0.5) AddUniqueNear(xs, x, 1.0);
                    }
                    else if (horizontal)
                    {
                        double y = (a.Y + b.Y) / 2.0;
                        if (y > 0.5 && y < paperH - 0.5) AddUniqueNear(ys, y, 1.0);
                    }
                }

                if (xs.Count < 2 || ys.Count < 2) return false;

                xs.Sort();
                ys.Sort();

                double left = xs[0];
                double right = xs[xs.Count - 1];
                double bottom = ys[0];
                double top = ys[ys.Count - 1];

                if ((right - left) < paperW * 0.60) return false;
                if ((top - bottom) < paperH * 0.60) return false;

                rect = new UsableRect();
                rect.Left = left;
                rect.Right = right;
                rect.Bottom = bottom;
                rect.Top = top;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFindTitleBlockTop(ContainerView sheet, UsableRect frame, double paperW, double paperH, out double titleTop)
        {
            titleTop = 0.0;

            try
            {
                if (sheet == null || frame == null)
                    return false;

                double best = double.MinValue;
                double lowerLimit = frame.Bottom + 2.0;
                double upperLimit = frame.Bottom + (frame.Top - frame.Bottom) * 0.38;
                double minLongLine = Math.Max(80.0, (frame.Right - frame.Left) * 0.35);

                DrawingObjectEnumerator e = sheet.GetAllObjects(typeof(Tekla.Structures.Drawing.Line));
                while (e.MoveNext())
                {
                    Tekla.Structures.Drawing.Line ln = e.Current as Tekla.Structures.Drawing.Line;
                    if (ln == null) continue;

                    Point a, b;
                    if (!TryGetLinePoints(ln, out a, out b)) continue;

                    bool horizontal = Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.X - b.X) >= minLongLine;
                    if (!horizontal)
                        continue;

                    double y = (a.Y + b.Y) * 0.5;
                    if (y <= lowerLimit || y >= upperLimit)
                        continue;

                    double minX = Math.Min(a.X, b.X);
                    double maxX = Math.Max(a.X, b.X);

                    // Ưu tiên line thuộc title block nằm gần biên trong, không bắt nhầm dim line ngắn.
                    bool overlapsFrame = maxX > frame.Left + 20.0 && minX < frame.Right - 20.0;
                    if (!overlapsFrame)
                        continue;

                    if (y > best)
                        best = y;
                }

                if (best == double.MinValue)
                    return false;

                titleTop = best;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void GetPaperSize(Drawing drawing, ContainerView sheet, out double width, out double height)
        {
            width = 420.0;
            height = 297.0;

            object[] sources = new object[] { drawing, sheet, GetProp(drawing, "Layout"), GetProp(drawing, "DrawingAttributes"), GetProp(drawing, "Attributes") };
            string[] wNames = new string[] { "Width", "PaperWidth", "SheetWidth", "DrawingWidth" };
            string[] hNames = new string[] { "Height", "PaperHeight", "SheetHeight", "DrawingHeight" };

            foreach (object s in sources)
            {
                if (s == null) continue;
                double w, h;
                if (TryReadDoubleAny(s, wNames, out w) && TryReadDoubleAny(s, hNames, out h) && w > 50 && h > 50)
                {
                    width = w;
                    height = h;
                    return;
                }
            }

            string paperName = Convert.ToString(GetProp(GetProp(drawing, "Layout"), "Name") ?? "").ToUpperInvariant();
            if (paperName.Contains("A1")) { width = 841; height = 594; return; }
            if (paperName.Contains("A2")) { width = 594; height = 420; return; }
            if (paperName.Contains("A3")) { width = 420; height = 297; return; }
            if (paperName.Contains("A4")) { width = 297; height = 210; return; }
        }

        private static object GetProp(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanRead) return null;
            try { return p.GetValue(target, null); } catch { return null; }
        }

        private static bool TryReadDoubleAny(object target, string[] names, out double value)
        {
            value = 0.0;
            foreach (string n in names)
            {
                object v = GetProp(target, n);
                if (v == null) continue;
                try
                {
                    value = Convert.ToDouble(v);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static bool TryGetLinePoints(Tekla.Structures.Drawing.Line line, out Point a, out Point b)
        {
            a = null;
            b = null;

            string[] aNames = new string[] { "StartPoint", "Start", "Point1", "FirstPoint", "P1" };
            string[] bNames = new string[] { "EndPoint", "End", "Point2", "SecondPoint", "P2" };

            for (int i = 0; i < aNames.Length; i++)
            {
                object av = GetProp(line, aNames[i]);
                object bv = GetProp(line, bNames[i]);
                a = av as Point;
                b = bv as Point;
                if (a != null && b != null) return true;
            }

            return false;
        }

        private static void AddUniqueNear(List<double> values, double value, double tol)
        {
            if (values == null)
                return;

            for (int i = 0; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - value) <= tol)
                    return;
            }
            values.Add(value);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static ViewBox GetLowestMainBox(List<ViewBox> boxes)
        {
            if (boxes == null || boxes.Count == 0)
                return null;

            ViewBox best = null;
            foreach (ViewBox b in boxes)
            {
                if (b == null)
                    continue;

                if (best == null || b.MinY < best.MinY)
                    best = b;
            }
            return best;
        }

        private static ViewBox GetHighestMainBox(List<ViewBox> boxes)
        {
            if (boxes == null || boxes.Count == 0)
                return null;

            ViewBox best = null;
            foreach (ViewBox b in boxes)
            {
                if (b == null)
                    continue;

                if (best == null || b.MaxY > best.MaxY)
                    best = b;
            }
            return best;
        }

        private static ViewBox GetRightMostMainBox(List<ViewBox> boxes)
        {
            if (boxes == null || boxes.Count == 0)
                return null;

            ViewBox best = null;
            foreach (ViewBox b in boxes)
            {
                if (b == null)
                    continue;

                if (best == null || b.MaxX > best.MaxX)
                    best = b;
            }
            return best;
        }

        private static double GetTotalRowWidth(List<ViewBox> boxes, double gap)
        {
            if (boxes == null || boxes.Count == 0)
                return 0.0;

            double total = 0.0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i] == null)
                    continue;

                total += boxes[i].Width;
            }

            if (boxes.Count > 1)
                total += gap * (boxes.Count - 1);

            return total;
        }

        private static ViewBox GetClusterBoxFromBoxes(List<ViewBox> boxes)
        {
            if (boxes == null || boxes.Count == 0)
                return null;

            ViewBox c = new ViewBox();
            c.MinX = double.MaxValue;
            c.MinY = double.MaxValue;
            c.MaxX = double.MinValue;
            c.MaxY = double.MinValue;

            foreach (ViewBox b in boxes)
            {
                if (b == null)
                    continue;

                if (b.MinX < c.MinX) c.MinX = b.MinX;
                if (b.MaxX > c.MaxX) c.MaxX = b.MaxX;
                if (b.MinY < c.MinY) c.MinY = b.MinY;
                if (b.MaxY > c.MaxY) c.MaxY = b.MaxY;
            }

            if (c.MinX == double.MaxValue || c.MinY == double.MaxValue)
                return null;

            c.Width = c.MaxX - c.MinX;
            c.Height = c.MaxY - c.MinY;
            c.CenterX = (c.MinX + c.MaxX) * 0.5;
            c.CenterY = (c.MinY + c.MaxY) * 0.5;
            return c;
        }

        private static List<ViewBox> BuildBoxes(List<View> views)
        {
            List<ViewBox> boxes = new List<ViewBox>();

            if (views == null)
                return boxes;

            foreach (View v in views)
            {
                ViewBox b;
                if (TryGetViewBox(v, out b))
                    boxes.Add(b);
            }

            return boxes;
        }

        private static ViewBox GetClusterBox(List<View> views)
        {
            List<ViewBox> boxes = BuildBoxes(views);
            if (boxes.Count == 0)
                return null;

            ViewBox c = new ViewBox();
            c.MinX = double.MaxValue;
            c.MinY = double.MaxValue;
            c.MaxX = double.MinValue;
            c.MaxY = double.MinValue;

            foreach (ViewBox b in boxes)
            {
                if (b.MinX < c.MinX) c.MinX = b.MinX;
                if (b.MaxX > c.MaxX) c.MaxX = b.MaxX;
                if (b.MinY < c.MinY) c.MinY = b.MinY;
                if (b.MaxY > c.MaxY) c.MaxY = b.MaxY;
            }

            c.Width = c.MaxX - c.MinX;
            c.Height = c.MaxY - c.MinY;
            c.CenterX = (c.MinX + c.MaxX) * 0.5;
            c.CenterY = (c.MinY + c.MaxY) * 0.5;
            return c;
        }

        private static bool TryGetViewBox(View view, out ViewBox box)
        {
            box = null;

            try
            {
                if (view == null)
                    return false;

                AABB bb = null;
                try { bb = view.GetAxisAlignedBoundingBox(); }
                catch { bb = null; }

                if (bb == null || bb.MinPoint == null || bb.MaxPoint == null)
                    return TryGetRestrictionBoxOnSheet(view, out box);

                double minX = Math.Min(bb.MinPoint.X, bb.MaxPoint.X);
                double maxX = Math.Max(bb.MinPoint.X, bb.MaxPoint.X);
                double minY = Math.Min(bb.MinPoint.Y, bb.MaxPoint.Y);
                double maxY = Math.Max(bb.MinPoint.Y, bb.MaxPoint.Y);

                if (maxX <= minX + 0.5 || maxY <= minY + 0.5)
                    return TryGetRestrictionBoxOnSheet(view, out box);

                box = new ViewBox();
                box.View = view;
                box.MinX = minX;
                box.MaxX = maxX;
                box.MinY = minY;
                box.MaxY = maxY;
                box.Width = maxX - minX;
                box.Height = maxY - minY;
                box.CenterX = (minX + maxX) * 0.5;
                box.CenterY = (minY + maxY) * 0.5;
                box.SortKey = GetSectionSortKey(view);
                return true;
            }
            catch
            {
                box = null;
                return false;
            }
        }

        private static bool TryGetRestrictionBoxOnSheet(View view, out ViewBox box)
        {
            box = null;

            try
            {
                AABB rb = view.RestrictionBox;
                if (rb == null || rb.MinPoint == null || rb.MaxPoint == null)
                    return false;

                Point origin = view.Origin;
                if (origin == null)
                    return false;

                double scale = TryGetViewScale(view);
                if (scale <= 0.0)
                    scale = 1.0;

                double minX = origin.X + Math.Min(rb.MinPoint.X, rb.MaxPoint.X) / scale;
                double maxX = origin.X + Math.Max(rb.MinPoint.X, rb.MaxPoint.X) / scale;
                double minY = origin.Y + Math.Min(rb.MinPoint.Y, rb.MaxPoint.Y) / scale;
                double maxY = origin.Y + Math.Max(rb.MinPoint.Y, rb.MaxPoint.Y) / scale;

                box = new ViewBox();
                box.View = view;
                box.MinX = minX;
                box.MaxX = maxX;
                box.MinY = minY;
                box.MaxY = maxY;
                box.Width = maxX - minX;
                box.Height = maxY - minY;
                box.CenterX = (minX + maxX) * 0.5;
                box.CenterY = (minY + maxY) * 0.5;
                box.SortKey = GetSectionSortKey(view);
                return box.Width > 0.5 && box.Height > 0.5;
            }
            catch
            {
                return false;
            }
        }

        private static void MoveViewBySheetDelta(View view, double dx, double dy)
        {
            try
            {
                if (view == null)
                    return;

                if (Math.Abs(dx) <= 0.01 && Math.Abs(dy) <= 0.01)
                    return;

                Point origin = view.Origin;
                if (origin == null)
                    return;

                Point newOrigin = new Point(origin.X + dx, origin.Y + dy, origin.Z);
                if (TrySetViewOrigin(view, newOrigin))
                {
                    try { view.Modify(); }
                    catch { }
                }
            }
            catch
            {
            }
        }

        private static bool TrySetViewOrigin(View view, Point origin)
        {
            try
            {
                if (view == null || origin == null)
                    return false;

                PropertyInfo prop = view.GetType().GetProperty(
                    "Origin",
                    BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanWrite)
                    return false;

                prop.SetValue(view, origin, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSectionView(View view)
        {
            string type = GetViewTypeText(view);

            if (ContainsIgnoreCase(type, "Section"))
                return true;

            string name = SafeViewName(view);
            if (ContainsIgnoreCase(name, "Section"))
                return true;

            // Assembly drawing có trường hợp ViewType rỗng nhưng Name là A/B/C.../I/J.
            // Đây vẫn là section thường, phải nhận trực tiếp theo tên để không mất view rộng như I/J.
            if (IsNamedSectionView(view))
                return true;

            return false;
        }

        private static bool IsNamedSectionView(View view)
        {
            try
            {
                string name = SafeViewName(view);
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                name = name.Trim().ToUpperInvariant();

                // A, B, C ... Z
                if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z')
                    return true;

                // A-A, B-B, C-C ... hoặc ký tự gạch fullwidth/Japanese.
                string key = ExtractSectionLetterKey(name, true);
                return !string.IsNullOrEmpty(key);
            }
            catch
            {
                return false;
            }
        }

        private static View FindFrontView(List<View> views)
        {
            foreach (View v in views)
            {
                string type = GetViewTypeText(v);
                if (ContainsIgnoreCase(type, "Front"))
                    return v;
            }

            foreach (View v in views)
            {
                string name = SafeViewName(v);
                if (ContainsIgnoreCase(name, "Front"))
                    return v;
            }

            return null;
        }

        private static View FindWidestView(List<View> views)
        {
            View best = null;
            double bestWidth = -1.0;

            foreach (View v in views)
            {
                double w = GetBoxWidth(v);
                if (w > bestWidth)
                {
                    bestWidth = w;
                    best = v;
                }
            }

            return best;
        }

        private static double GetMaxViewWidth(List<View> views)
        {
            double max = 0.0;
            if (views == null)
                return max;

            foreach (View v in views)
            {
                double w = GetBoxWidth(v);
                if (w > max)
                    max = w;
            }

            return max;
        }

        private static double GetBoxWidth(View view)
        {
            ViewBox b;
            if (TryGetViewBox(view, out b))
                return b.Width;

            return 0.0;
        }

        private static double GetColumnWidth(List<ViewBox> boxes, int col)
        {
            double max = 0.0;
            for (int i = col; i < boxes.Count; i += 2)
            {
                if (boxes[i].Width > max)
                    max = boxes[i].Width;
            }
            return max;
        }

        private static void AddUniqueView(List<View> views, View view)
        {
            if (views == null || view == null)
                return;

            foreach (View v in views)
            {
                if (Object.ReferenceEquals(v, view))
                    return;
            }

            views.Add(view);
        }

        private static string GetViewTypeText(View view)
        {
            try
            {
                if (view == null)
                    return "";

                object attr = null;
                PropertyInfo attrProp = view.GetType().GetProperty("Attributes");
                if (attrProp != null && attrProp.CanRead)
                    attr = attrProp.GetValue(view, null);

                if (attr != null)
                {
                    string text = ReadAnyPropertyAsString(attr, "ViewType");
                    if (!string.IsNullOrEmpty(text)) return text;

                    text = ReadAnyPropertyAsString(attr, "Type");
                    if (!string.IsNullOrEmpty(text)) return text;
                }

                string direct = ReadAnyPropertyAsString(view, "ViewType");
                if (!string.IsNullOrEmpty(direct)) return direct;
            }
            catch
            {
            }

            return "";
        }

        private static string SafeViewName(View view)
        {
            string name = ReadAnyPropertyAsString(view, "Name");
            if (!string.IsNullOrEmpty(name)) return name;

            name = ReadAnyPropertyAsString(view, "ViewName");
            if (!string.IsNullOrEmpty(name)) return name;

            return "";
        }

        private static string ReadAnyPropertyAsString(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return "";

                PropertyInfo prop = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance);

                if (prop == null || !prop.CanRead)
                    return "";

                object value = prop.GetValue(obj, null);
                if (value == null)
                    return "";

                return value.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static bool ContainsIgnoreCase(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
                return false;

            return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double TryGetViewScale(View view)
        {
            try
            {
                if (view == null || view.Attributes == null)
                    return 1.0;

                object attr = view.Attributes;

                PropertyInfo p = attr.GetType().GetProperty("Scale");
                if (p != null && p.CanRead)
                {
                    object value = p.GetValue(attr, null);
                    if (value != null)
                    {
                        double scale;
                        if (double.TryParse(value.ToString(), out scale) && scale > 0.0)
                            return scale;
                    }
                }
            }
            catch
            {
            }

            return 1.0;
        }
    }
}
